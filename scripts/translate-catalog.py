#!/usr/bin/env python3
"""Generate reviewable offline translations using official Argos CTranslate2 models.

Maintenance only. The shipped application reads embedded JSON and never downloads models
or sends user content to a translation service. Existing reviewed entries are preserved.
"""
import argparse
import collections
import hashlib
import json
from pathlib import Path
import re
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[1]
RESOURCES = ROOT / 'OpenBurningSuite/Localization/Resources'
SLOTS = re.compile(r'\{\d+\}')
MODEL_URLS = {
    'ru': 'https://argos-net.com/v1/translate-en_ru-1_9.argosmodel',
    'zh-Hans': 'https://argos-net.com/v1/translate-en_zh-1_9.argosmodel',
    'ja': 'https://argos-net.com/v1/translate-en_ja-1_1.argosmodel',
}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('language', choices=MODEL_URLS)
    parser.add_argument('--models-dir', type=Path, required=True)
    parser.add_argument('--download', action='store_true')
    args = parser.parse_args()
    import ctranslate2
    import sentencepiece

    args.models_dir.mkdir(parents=True, exist_ok=True)
    archive = args.models_dir / (args.language + '.argosmodel')
    destination = args.models_dir / args.language
    if not archive.exists():
        if not args.download:
            parser.error(f'Model missing: {archive}. Supply it or explicitly use --download.')
        # HTTPS certificate validation and inherited proxy configuration remain enabled.
        temporary = archive.with_suffix('.download')
        subprocess.run(['curl', '-fL', '--retry', '2', MODEL_URLS[args.language], '-o', str(temporary)], check=True)
        temporary.replace(archive)
    print('Model SHA-256:', hashlib.sha256(archive.read_bytes()).hexdigest(), flush=True)
    if not destination.exists():
        with zipfile.ZipFile(archive) as model:
            for member in model.infolist():
                path = (destination / member.filename).resolve()
                if not path.is_relative_to(destination.resolve()):
                    raise ValueError('Unsafe path in model archive')
            model.extractall(destination)
    metadata_path = next(destination.rglob('metadata.json'))
    metadata = json.loads(metadata_path.read_text())
    expected = 'zh' if args.language == 'zh-Hans' else args.language
    if metadata['from_code'] != 'en' or metadata['to_code'] != expected:
        raise ValueError('Model language does not match the requested catalog')
    package = metadata_path.parent
    tokenizer = sentencepiece.SentencePieceProcessor(model_file=str(package / 'sentencepiece.model'))
    translator = ctranslate2.Translator(str(package / 'model'), device='cpu', compute_type='int8', inter_threads=1, intra_threads=1)
    english = json.loads((RESOURCES / 'en.json').read_text())
    path = RESOURCES / f'{args.language}.json'
    translated = json.loads(path.read_text()) if path.exists() else {}
    invariant = set(json.loads((RESOURCES.parent / 'invariant.json').read_text()))

    cache = {}
    def precompute(texts):
        unique = list(dict.fromkeys(text.strip() for text in texts if text.strip() and re.search('[A-Za-z]', text)))
        unique = [text for text in unique if text not in cache]
        if not unique:
            return
        tokens = [tokenizer.encode(text, out_type=str) for text in unique]
        results = translator.translate_batch(tokens, beam_size=4, max_decoding_length=768,
                                             max_batch_size=1024, batch_type='tokens')
        for text, result in zip(unique, results):
            cache[text] = tokenizer.decode(result.hypotheses[0])

    def sentence(text):
        stripped = text.strip()
        if not stripped or not re.search('[A-Za-z]', stripped):
            return text
        precompute([text])
        return text[:len(text) - len(text.lstrip())] + cache[stripped] + text[len(text.rstrip()):]

    def translate(source):
        # Numeric sentinels survive normal translation while allowing natural word order.
        sentinels = {}
        def protect(match):
            marker = f'9876{len(sentinels):03d}6789'
            sentinels[marker] = match.group()
            return marker
        protected = SLOTS.sub(protect, source)
        output = '\n'.join(sentence(line) for line in protected.split('\n'))
        if all(output.count(marker) == 1 for marker in sentinels):
            for marker, slot in sentinels.items():
                output = output.replace(marker, slot)
        else:
            # Fail-safe for a model that rewrites a sentinel: translate literal spans only.
            output = ''.join(part if SLOTS.fullmatch(part) else '\n'.join(sentence(line) for line in part.split('\n'))
                             for part in re.split(r'(\{\d+\})', source))
        if collections.Counter(SLOTS.findall(source)) != collections.Counter(SLOTS.findall(output)):
            raise ValueError(f'Runtime placeholder changed: {source!r}')
        if not output.strip():
            raise ValueError(f'Empty model output: {source!r}')
        return output

    pending = [source for source in english if source not in translated]
    print(f'{args.language}: {len(pending)} strings need translation', flush=True)
    for offset in range(0, len(pending), 100):
        group = pending[offset:offset + 100]
        inputs = []
        for source in group:
            counter = iter(range(10000))
            protected = SLOTS.sub(lambda m: f'9876{next(counter):03d}6789', source)
            inputs.extend(protected.split('\n'))
        precompute(inputs)
        for source in group:
            translated[source] = source if source in invariant else translate(source)
        path.write_text(json.dumps(dict(sorted(translated.items())), ensure_ascii=False, indent=2) + '\n')
        print(f'{args.language}: {min(offset + 100, len(pending))}/{len(pending)} translated; checkpoint saved', flush=True)
    print('Review terminology and unchanged prose; run scripts/check-localization.py before release.', flush=True)


if __name__ == '__main__':
    main()
