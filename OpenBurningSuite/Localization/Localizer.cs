using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace OpenBurningSuite.Localization;

/// <summary>
/// Translates presentation strings, never persisted option IDs, paths, or disc metadata.
/// English source text is the stable resource key. Numbered slots represent runtime data.
/// </summary>
public static class Localizer
{
    public sealed record Language(string Code, string Name);
    public static readonly Language[] Languages =
    {
        new("en", "English"), new("ru", "Русский"),
        new("zh-Hans", "简体中文"), new("ja", "日本語")
    };

    private sealed record Pattern(Regex Regex, string Anchor, string Translation);
    private static Dictionary<string, string> _strings = new();
    private static Pattern[] _patterns = Array.Empty<Pattern>();
    private static readonly Dictionary<string, string> Cache = new();
    public static string LanguageCode { get; private set; } = "en";
    public static event Action? LanguageChanged;

    public static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "en";
        if (code.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return "zh-Hans";
        return Languages.FirstOrDefault(l => code.Equals(l.Code, StringComparison.OrdinalIgnoreCase)
            || code.StartsWith(l.Code + "-", StringComparison.OrdinalIgnoreCase))?.Code ?? "en";
    }

    public static string ResolveLanguage(string? preference, string? installer, string system)
        => Normalize(!string.IsNullOrWhiteSpace(preference) ? preference :
            !string.IsNullOrWhiteSpace(installer) ? installer : system);

    public static void Initialize(string? preference)
    {
        string? installer = null;
        if (OperatingSystem.IsWindows())
        {
            try
            {
                installer = Registry.GetValue(@"HKEY_LOCAL_MACHINE\Software\OpenBurningSuite",
                    "UILanguage", null) as string;
            }
            catch (System.Security.SecurityException) { }
            catch (UnauthorizedAccessException) { }
        }
        SetLanguage(ResolveLanguage(preference, installer, CultureInfo.CurrentUICulture.Name));
    }

    public static IReadOnlyDictionary<string, string> ReadCatalog(string code)
    {
        using var stream = typeof(Localizer).Assembly.GetManifestResourceStream(
            $"OpenBurningSuite.Localization.Resources.{Normalize(code)}.json")
            ?? throw new InvalidDataException($"Missing language resource: {code}");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidDataException($"Invalid language resource: {code}");
    }

    public static void SetLanguage(string code)
    {
        LanguageCode = Normalize(code);
        _strings = new Dictionary<string, string>(ReadCatalog(LanguageCode), StringComparer.Ordinal);
        _patterns = _strings.Where(p => p.Key != p.Value && Regex.IsMatch(p.Key, @"\{\d+\}"))
            .Select(p => CreatePattern(p.Key, p.Value))
            .Where(p => p != null).Cast<Pattern>()
            .OrderByDescending(p => p.Anchor.Length).ToArray();
        Cache.Clear();
        // Keep numeric/date formatting compatible with existing device/format parsers.
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(LanguageCode);
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture;
        LanguageChanged?.Invoke();
    }

    private static Pattern? CreatePattern(string source, string translation)
    {
        var parts = Regex.Split(source, @"(\{\d+\})");
        var anchor = parts.Where(p => !Regex.IsMatch(p, @"^\{\d+\}$"))
            .OrderByDescending(p => p.Length).FirstOrDefault() ?? "";
        if (anchor.Trim().Length < 3) return null;
        var seen = new HashSet<string>();
        var regex = "\\A" + string.Concat(parts.Select(p =>
        {
            var slot = Regex.Match(p, @"^\{(\d+)\}$");
            if (!slot.Success) return Regex.Escape(p);
            var name = "v" + slot.Groups[1].Value;
            return seen.Add(name) ? $"(?<{name}>.*?)" : $"\\k<{name}>";
        })) + "\\z";
        return new Pattern(new Regex(regex, RegexOptions.Singleline | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(50)), anchor, translation);
    }

    public static string Text(string? source)
    {
        if (string.IsNullOrEmpty(source) || LanguageCode == "en") return source ?? "";
        if (Cache.TryGetValue(source, out var cached)) return cached;
        var result = TranslateCore(source);
        if (Cache.Count > 8192) Cache.Clear();
        Cache[source] = result;
        return result;
    }

    private static string TranslateCore(string source)
    {
        if (_strings.TryGetValue(source, out var exact)) return exact;
        foreach (var pattern in _patterns)
        {
            if (!source.Contains(pattern.Anchor, StringComparison.Ordinal)) continue;
            Match match;
            try { match = pattern.Regex.Match(source); }
            catch (RegexMatchTimeoutException) { continue; }
            if (!match.Success) continue;
            // Substitute only original slots; never interpret braces in user file names as formatting.
            return Regex.Replace(pattern.Translation, @"\{(\d+)\}", m =>
            {
                var value = match.Groups["v" + m.Groups[1].Value];
                return value.Success ? (_strings.TryGetValue(value.Value, out var caption) ? caption : value.Value) : m.Value;
            });
        }
        var trimmed = source.Trim();
        if (trimmed != source && _strings.TryGetValue(trimmed, out exact))
        {
            var start = source.IndexOf(trimmed, StringComparison.Ordinal);
            return source[..start] + exact + source[(start + trimmed.Length)..];
        }
        if (source.EndsWith(':') && _strings.TryGetValue(source[..^1], out exact)) return exact + ":";
        // Operation logs use a timestamp prefix and often combine multiple translated lines.
        var prefix = Regex.Match(source, @"^\[\d{2}:\d{2}:\d{2}(?:\.\d+)?\]\s*");
        if (prefix.Success) return prefix.Value + TranslateCore(source[prefix.Length..]);
        if (source.Contains('\n')) return string.Join("\n", source.Split('\n').Select(TranslateCore));
        return source;
    }
}
