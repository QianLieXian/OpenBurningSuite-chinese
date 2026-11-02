using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.VisualTree;
using OpenBurningSuite;
using OpenBurningSuite.Localization;
using OpenBurningSuite.Services;

int passed = 0;
void Equal<T>(T expected, T actual, string test)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"FAIL {test}: expected [{expected}], actual [{actual}]");
    Console.WriteLine("PASS " + test);
    passed++;
}

// Exercise the real styles, controls and property bindings with a headless Avalonia renderer.
SettingsService.Current.Language = "en";
AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
Equal("ru", Localizer.ResolveLanguage("ru", "ja", "zh-CN"), "saved preference overrides installation");
Equal("ja", Localizer.ResolveLanguage("", "ja", "ru-RU"), "installation overrides system language");
Equal("zh-Hans", Localizer.ResolveLanguage(null, null, "zh-CN"), "system Simplified Chinese");
Equal("en", Localizer.Normalize("unsupported-language"), "unsupported locale falls back to English");
Equal("ru", Localizer.Normalize("ru-RU"), "regional Russian locale");

var caption = new TextBlock { Text = "Home" };
var inline = new Run { Text = "Home" };
var fileName = new TextBlock { Text = "Home" };
PresentationLocalization.SetKeepOriginal(fileName, true);
var editable = new TextBox { Text = "Home" };
var log = new TextBox { IsReadOnly = true, Text = "[12:34:56] Scanning for optical drives..." };
var combo = new ComboBox { ItemsSource = new[] { "Auto", "Max" }, SelectedIndex = 0 };
var window = new Window { Title = "Settings", Width = 600, Height = 400,
    Content = new StackPanel { Children = { caption, editable, combo, log } } };
window.Show();
window.UpdateLayout();

var names = new Dictionary<string, string> { ["en"]="Home", ["ru"]="Главная", ["zh-Hans"]="主页", ["ja"]="ホーム" };
foreach (var language in Localizer.Languages)
{
    Localizer.SetLanguage(language.Code);
    window.UpdateLayout();
    Equal(names[language.Code], caption.Text, language.Code + " static caption switches immediately");
    Equal(names[language.Code], inline.Text, language.Code + " rich-text help captions switch immediately");
    Equal("Home", fileName.Text, language.Code + " user filename matching a resource key is unchanged");
    Equal("Home", editable.Text, language.Code + " editable user data is unchanged");
    Equal("Auto", combo.SelectedItem?.ToString(), language.Code + " selected burn option retains stable ID");
    var visible = combo.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
    Equal(true, visible.Contains(Localizer.Text("Auto")), language.Code + " combo caption is localized");
    var path = @"C:\images\{0} $1 日本語.iso";
    var template = Localizer.ReadCatalog(language.Code)["File not found: {0}"];
    Equal(template.Replace("{0}", path), Localizer.Text("File not found: " + path), language.Code + " runtime slots preserve user path and braces");
    Equal(path, Localizer.Text(path), language.Code + " paths are invariant");
}
Localizer.SetLanguage("zh-Hans");
Equal("设置", window.Title, "native window title");
Equal(true, log.Text?.Contains("正在扫描光驱"), "timestamped operation log");
var changing = new TextBlock { Text = "Home" };
changing.Text = "Cancel";
Equal("取消", changing.Text, "dynamic caption update");
Localizer.SetLanguage("en");
Equal("Cancel", changing.Text, "switching back restores original source");
window.Close();

var main = new OpenBurningSuite.Views.MainWindow();
main.Show();
string[] pages = { "home", "discover", "read", "build", "write", "verify", "advanced",
    "audiowizard", "videowizard", "datawizard", "gamewizard", "copywizard", "blankwizard", "settings", "help" };
var testDirectory = Path.Combine(Path.GetTempPath(), "obs-localization-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(testDirectory, "source"));
const string unicodeName = "日本語-简体中文-Русский.txt";
const string unicodeContent = "English · Русский · 简体中文 · 日本語";
File.WriteAllText(Path.Combine(testDirectory, "source", unicodeName), unicodeContent);
try
{
    foreach (var language in Localizer.Languages)
    {
        Localizer.SetLanguage(language.Code);
        foreach (var page in pages)
        {
            main.NavigateToView(page);
            main.UpdateLayout();
        }
        Equal(Localizer.Text("Open Burning Suite — Disc Burning & Imaging"), main.Title,
            language.Code + " all application pages render and navigate");
        var job = new OpenBurningSuite.Models.BuildJob
        {
            SourceFolder = Path.Combine(testDirectory, "source"),
            OutputImagePath = Path.Combine(testDirectory, language.Code + ".iso"),
            FileSystem = "ISO 9660 + Joliet", VolumeLabel = "LOCALIZATION_TEST"
        };
        Task.Run(() => new OpenBurningSuite.Native.Iso.IsoBuilder().BuildDataImageAsync(
            job, new Progress<OpenBurningSuite.Models.BuildProgress>(), CancellationToken.None)).GetAwaiter().GetResult();
        using var image = File.OpenRead(job.OutputImagePath);
        using var reader = new DiscUtils.Iso9660.CDReader(image, true);
        var file = reader.GetFiles(@"\").Single();
        Equal(true, file.Contains("日本語-简体中文-Русский"), language.Code + " Joliet image retains Unicode filename");
        using var data = reader.OpenFile(file, FileMode.Open, FileAccess.Read);
        using var text = new StreamReader(data);
        Equal(unicodeContent, text.ReadToEnd(), language.Code + " ISO creation preserves multilingual file contents");
        Equal("ISO 9660 + Joliet", job.FileSystem, language.Code + " image builder retains filesystem ID");
    }
}
finally
{
    main.Close();
    Directory.Delete(testDirectory, true);
}
Console.WriteLine($"{passed} localization behavior checks passed.");
