using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;

namespace OpenBurningSuite.Localization;

/// <summary>
/// Localizes the final text presenter. ComboBox items and their selected values remain
/// unchanged, so localized captions cannot alter burn modes, command arguments or settings.
/// Editable text (paths, passwords, keys, volume names) is deliberately never translated.
/// </summary>
public sealed class PresentationLocalization : AvaloniaObject
{
    private PresentationLocalization() { }
    public static readonly AttachedProperty<bool> KeepOriginalProperty =
        AvaloniaProperty.RegisterAttached<PresentationLocalization, AvaloniaObject, bool>("KeepOriginal");
    public static bool GetKeepOriginal(AvaloniaObject target) => target.GetValue(KeepOriginalProperty);
    public static void SetKeepOriginal(AvaloniaObject target, bool value) => target.SetValue(KeepOriginalProperty, value);
    private sealed class Source { public string Text = ""; public bool Updating; }
    private static readonly ConditionalWeakTable<AvaloniaObject, Dictionary<AvaloniaProperty, Source>> Sources = new();
    private static readonly List<WeakReference<AvaloniaObject>> Presenters = new();
    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        _registered = true;
        KeepOriginalProperty.Changed.AddClassHandler<AvaloniaObject>((target, _) =>
        {
            if (!GetKeepOriginal(target) || !Sources.TryGetValue(target, out var values)) return;
            foreach (var pair in values)
            {
                pair.Value.Updating = true;
                try { target.SetCurrentValue(pair.Key, pair.Value.Text); }
                finally { pair.Value.Updating = false; }
            }
        });
        TextBlock.TextProperty.Changed.AddClassHandler<TextBlock>((c, _) => Apply(c, TextBlock.TextProperty));
        Run.TextProperty.Changed.AddClassHandler<Run>((c, _) => Apply(c, Run.TextProperty));
        Window.TitleProperty.Changed.AddClassHandler<Window>((c, _) => Apply(c, Window.TitleProperty));
        NativeMenuItem.HeaderProperty.Changed.AddClassHandler<NativeMenuItem>((c, _) => Apply(c, NativeMenuItem.HeaderProperty));
        TextBox.WatermarkProperty.Changed.AddClassHandler<TextBox>((c, _) => Apply(c, TextBox.WatermarkProperty));
        TextBox.TextProperty.Changed.AddClassHandler<TextBox>((c, _) =>
        {
            if (c.IsReadOnly) Apply(c, TextBox.TextProperty);
        });
        TextBox.IsReadOnlyProperty.Changed.AddClassHandler<TextBox>((c, _) =>
        {
            if (c.IsReadOnly) Apply(c, TextBox.TextProperty);
        });
        Localizer.LanguageChanged += Refresh;
    }

    private static void Apply(AvaloniaObject target, AvaloniaProperty property)
    {
        if (GetKeepOriginal(target)) return;
        if (target.GetValue(property) is not string text) return;
        if (!Sources.TryGetValue(target, out var properties))
        {
            properties = new();
            Sources.Add(target, properties);
            Presenters.Add(new(target));
        }
        if (!properties.TryGetValue(property, out var source)) properties[property] = source = new();
        if (source.Updating) return;
        source.Text = text;
        Update(target, property, source);
    }

    private static void Update(AvaloniaObject target, AvaloniaProperty property, Source source)
    {
        source.Updating = true;
        try
        {
            var translated = Localizer.Text(source.Text);
            if (!Equals(target.GetValue(property), translated)) target.SetCurrentValue(property, translated);
        }
        finally { source.Updating = false; }
    }

    private static void Refresh()
    {
        for (int i = Presenters.Count - 1; i >= 0; i--)
        {
            if (!Presenters[i].TryGetTarget(out var target)) { Presenters.RemoveAt(i); continue; }
            if (GetKeepOriginal(target)) continue;
            if (Sources.TryGetValue(target, out var properties))
                foreach (var pair in properties)
                {
                    if (target is TextBox input && pair.Key == TextBox.TextProperty && !input.IsReadOnly) continue;
                    Update(target, pair.Key, pair.Value);
                }
        }
    }
}
