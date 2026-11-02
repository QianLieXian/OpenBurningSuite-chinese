#!/usr/bin/env python3
"""Fail closed on missing translations, untranslated prose, or damaged runtime slots."""
import collections
import json
from pathlib import Path
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
RESOURCES = ROOT / 'OpenBurningSuite/Localization/Resources'
LANGUAGES = ('en', 'ru', 'zh-Hans', 'ja')


def eligible(value):
    if not re.search('[a-zA-Z]{2}', value):
        return False
    if re.match(r'(?:avares:|https?:|/|\\\\|#[0-9a-fA-F]{6}$)', value):
        return False
    if re.search(r'\.(?:dll|exe|axaml|png|json|log|iso|bin|cue|txt|xml|chd|wav)$', value, re.I) and ' ' not in value:
        return False
    return len(value) <= 6000


def load_json(path):
    def reject_duplicates(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError(f'Duplicate resource key in {path}: {key!r}')
            result[key] = value
        return result
    return json.loads(path.read_text(encoding='utf-8'), object_pairs_hook=reject_duplicates)


def main():
    catalogs = {code: load_json(RESOURCES / f'{code}.json') for code in LANGUAGES}
    english = catalogs['en']
    invariant = set(load_json(RESOURCES.parent / 'invariant.json'))
    errors = []
    for code, catalog in catalogs.items():
        missing, extra = set(english) - set(catalog), set(catalog) - set(english)
        if missing:
            errors.append(f'{code}: {len(missing)} missing translations (example: {sorted(missing)[0]!r})')
        if extra:
            errors.append(f'{code}: {len(extra)} keys absent from the English catalog')
        for source, translation in catalog.items():
            if not isinstance(translation, str) or not translation.strip():
                errors.append(f'{code}: empty/invalid translation: {source!r}')
                continue
            if collections.Counter(re.findall(r'\{\d+\}', source)) != collections.Counter(re.findall(r'\{\d+\}', translation)):
                errors.append(f'{code}: runtime slots changed: {source!r}')
            if re.search(r'9865\d{6}5689|8734\d{9}4378|▁|⁇', translation):
                errors.append(f'{code}: translation artifact: {source!r}')
            if code != 'en' and translation == source and source not in invariant:
                errors.append(f'{code}: untranslated text: {source!r}')
            if code == 'en' and translation != source:
                errors.append(f'English source key changed: {source!r}')
        print(f'{code}: {len(catalog)}/{len(english)} resource keys present')

    values = set()
    for path in (ROOT / 'OpenBurningSuite').rglob('*.axaml'):
        for element in ET.parse(path).iter():
            for key, value in element.attrib.items():
                if key in ('Text', 'Content', 'Header', 'Title', 'Watermark', 'ToolTip.Tip') and not value.startswith('{') and eligible(value):
                    values.add(value)
            if element.tag.rsplit('}', 1)[-1] in ('Run', 'Bold', 'Span', 'TextBlock', 'ComboBoxItem', 'ListBoxItem') and element.text and eligible(element.text.strip()):
                values.add(element.text.strip())
    process = subprocess.run(['dotnet', 'run', '--project', str(ROOT / 'tools/LocalizationCatalog'), '-c', 'Release', '--', str(ROOT)], cwd=ROOT, text=True, stdout=subprocess.PIPE)
    if process.returncode:
        sys.stderr.write(process.stdout)
        return process.returncode
    # dotnet may prepend first-run/build notices; the scanner's JSON starts at the first [.
    output = process.stdout
    entries = json.loads(output[output.index('[{"file"'):])
    values.update(entry['value'] for entry in entries if eligible(entry['value']))
    for value in sorted(values - set(english)):
        errors.append(f'Source string absent from catalog: {value!r}')
    if errors:
        print('\n'.join(errors[:30]), file=sys.stderr)
        if len(errors) > 30:
            print(f'... {len(errors) - 30} further errors', file=sys.stderr)
        print('Localization validation FAILED. Do not publish this build.', file=sys.stderr)
        return 1
    print(f'Localization validation passed: {len(english)} keys × 4 languages; source coverage and runtime slots verified.')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
