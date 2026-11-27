using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.Services;
using StudyScope.ViewModels;

namespace StudyScope.Tests.ViewModels;

/// <summary>
/// Zoom der ganzen Oberfläche: Anwenden über das Fenster, sofortiges Speichern ohne Verlust anderer UiSettings,
/// Tastatur- und Mausradentscheidung, Abgleich nach einem Zoom des WebViews selbst.
/// </summary>
[TestFixture]
public class ZoomViewModelTests
{
    private sealed class FakeWindow : IWindowZoom
    {
        public int Zoom = 100;
        public List<int> Sets = [];
        public int Get() => Zoom;
        public void Set(int percent) { Zoom = percent; Sets.Add(percent); }
    }

    private string _tempDir = null!;
    private JsonStorageService _storage = null!;
    private FakeWindow _window = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "StudyScopeZoom_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _storage = new JsonStorageService(_tempDir);
        _window = new FakeWindow();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
    }

    private string ConfigPath => Path.Combine(_tempDir, "config.json");

    private async Task<ZoomViewModel> LoadedAsync()
    {
        var vm = new ZoomViewModel(_storage, _window);
        await vm.LoadAsync();
        return vm;
    }

    [Test]
    public async Task Load_WithoutConfig_Is100()
    {
        var vm = await LoadedAsync();
        Assert.That(vm.Percent, Is.EqualTo(100));
        Assert.That(vm.IsDefault, Is.True);
        Assert.That(vm.CanZoomIn && vm.CanZoomOut, Is.True);
    }

    [Test]
    public async Task Load_OlderConfigWithoutZoom_Is100_AndInvalidValueIsSnapped()
    {
        await File.WriteAllTextAsync(ConfigPath,
            new JsonObject { ["UiSettings"] = new JsonObject { ["SidebarCollapsed"] = true } }.ToJsonString());
        Assert.That((await LoadedAsync()).Percent, Is.EqualTo(100));

        await File.WriteAllTextAsync(ConfigPath,
            new JsonObject { ["UiSettings"] = new JsonObject { ["Zoom"] = 133 } }.ToJsonString());
        Assert.That((await LoadedAsync()).Percent, Is.EqualTo(125));
    }

    [Test]
    public async Task ApplyToWindow_SetsOnlyWhenDifferent()
    {
        var vm = await LoadedAsync();
        vm.ApplyToWindow();
        Assert.That(_window.Sets, Is.Empty);

        await _storage.SaveUiSettingsAsync(new UiSettings { Zoom = 125 });
        vm = await LoadedAsync();
        vm.ApplyToWindow();
        Assert.That(_window.Sets, Is.EqualTo(new[] { 125 }));
    }

    [Test]
    public async Task ZoomIn_AppliesAndSavesImmediately_KeepingOtherUiSettings()
    {
        await _storage.SaveUiSettingsAsync(new UiSettings
        {
            SidebarCollapsed = true, SidebarWidth = 410, Theme = ThemeMode.Dark, Language = "en", OutputLanguage = "documents"
        });
        var vm = await LoadedAsync();

        await vm.ZoomInAsync();

        Assert.That(vm.Percent, Is.EqualTo(110));
        Assert.That(_window.Zoom, Is.EqualTo(110));
        var saved = await _storage.LoadUiSettingsAsync();
        Assert.That(saved.Zoom, Is.EqualTo(110));
        Assert.That(saved.SidebarCollapsed, Is.True);
        Assert.That(saved.SidebarWidth, Is.EqualTo(410));
        Assert.That(saved.Theme, Is.EqualTo(ThemeMode.Dark));
        Assert.That(saved.Language, Is.EqualTo("en"));
        Assert.That(saved.OutputLanguage, Is.EqualTo("documents"));
    }

    [Test]
    public async Task OtherUiSettingsSavers_KeepZoom()
    {
        var vm = await LoadedAsync();
        await vm.SetAsync(150);

        var appearance = new AppearanceViewModel(_storage);
        await appearance.LoadAsync();
        appearance.SelectedTheme = ThemeMode.Light;
        await appearance.SaveAsync();

        var layout = new LayoutViewModel(_storage);
        await layout.LoadAsync();
        await layout.ToggleAsync();

        Assert.That((await _storage.LoadUiSettingsAsync()).Zoom, Is.EqualTo(150));
    }

    [Test]
    public async Task Steps_StopAtLimits()
    {
        var vm = await LoadedAsync();
        for (var i = 0; i < 10; i++) await vm.ZoomInAsync();
        Assert.That(vm.Percent, Is.EqualTo(150));
        Assert.That(vm.CanZoomIn, Is.False);
        for (var i = 0; i < 10; i++) await vm.ZoomOutAsync();
        Assert.That(vm.Percent, Is.EqualTo(80));
        Assert.That(vm.CanZoomOut, Is.False);
        await vm.ResetAsync();
        Assert.That(vm.Percent, Is.EqualTo(100));
        Assert.That((await _storage.LoadUiSettingsAsync()).Zoom, Is.EqualTo(100));
    }

    [Test]
    public async Task SetAsync_SnapsToLevel()
    {
        var vm = await LoadedAsync();
        await vm.SetAsync(118);
        Assert.That(vm.Percent, Is.EqualTo(125));
        Assert.That(_window.Zoom, Is.EqualTo(125));
    }

    [Test]
    public async Task SyncFromWindow_AdoptsZoomOfTheWebView_SnappedAndSaved()
    {
        var vm = await LoadedAsync();
        _window.Zoom = 175;   // WebView2 hat selbst gezoomt (Stufe, die StudyScope nicht anbietet)

        await vm.SyncFromWindowAsync();

        Assert.That(vm.Percent, Is.EqualTo(150));
        Assert.That(_window.Zoom, Is.EqualTo(150));
        Assert.That((await _storage.LoadUiSettingsAsync()).Zoom, Is.EqualTo(150));
    }

    [Test]
    public async Task SyncFromWindow_UnchangedOrUnknown_DoesNothing()
    {
        var vm = await LoadedAsync();
        await vm.SyncFromWindowAsync();
        _window.Zoom = 0;     // Photino nicht erreichbar
        await vm.SyncFromWindowAsync();
        Assert.That(vm.Percent, Is.EqualTo(100));
        Assert.That(_window.Sets, Is.Empty);
        Assert.That(File.Exists(ConfigPath), Is.False);
    }

    [TestCase("+", "BracketRight", ZoomAction.In)]
    [TestCase("=", "Equal", ZoomAction.In)]
    [TestCase("+", "NumpadAdd", ZoomAction.In)]
    [TestCase("-", "Slash", ZoomAction.Out)]
    [TestCase("-", "NumpadSubtract", ZoomAction.Out)]
    [TestCase("0", "Digit0", ZoomAction.Reset)]
    [TestCase("Insert", "Numpad0", ZoomAction.Reset)]   // Ziffernblock ohne NumLock
    [TestCase("b", "KeyB", ZoomAction.None)]             // Strg + B bleibt Seitenleiste
    [TestCase("9", "Digit9", ZoomAction.None)]
    public void EvaluateShortcut_WithCtrl(string key, string code, ZoomAction expected) =>
        Assert.That(ZoomViewModel.EvaluateShortcut(key, code, ctrl: true), Is.EqualTo(expected));

    [Test]
    public void EvaluateShortcut_NeedsCtrl_WithoutAltOrMeta()
    {
        Assert.That(ZoomViewModel.EvaluateShortcut("+", "BracketRight", ctrl: false), Is.EqualTo(ZoomAction.None));
        Assert.That(ZoomViewModel.EvaluateShortcut("+", "BracketRight", ctrl: true, alt: true), Is.EqualTo(ZoomAction.None)); // AltGr
        Assert.That(ZoomViewModel.EvaluateShortcut("+", "BracketRight", ctrl: true, meta: true), Is.EqualTo(ZoomAction.None));
    }

    [Test]
    public void EvaluateWheel_UpZoomsIn()
    {
        Assert.That(ZoomViewModel.EvaluateWheel(-100), Is.EqualTo(ZoomAction.In));
        Assert.That(ZoomViewModel.EvaluateWheel(100), Is.EqualTo(ZoomAction.Out));
        Assert.That(ZoomViewModel.EvaluateWheel(0), Is.EqualTo(ZoomAction.None));
    }

    [Test]
    public async Task HandleShortcutAndWheel_ChangeZoom()
    {
        var vm = await LoadedAsync();
        Assert.That(await vm.HandleShortcutAsync("+", "BracketRight", ctrl: true), Is.True);
        await vm.HandleWheelAsync(-120);
        Assert.That(vm.Percent, Is.EqualTo(125));
        Assert.That(await vm.HandleShortcutAsync("b", "KeyB", ctrl: true), Is.False);
        Assert.That(await vm.HandleShortcutAsync("0", "Digit0", ctrl: true), Is.True);
        Assert.That(vm.Percent, Is.EqualTo(100));
    }

    [Test]
    public async Task RapidSteps_AreAllSaved()
    {
        var vm = await LoadedAsync();
        await Task.WhenAll(vm.ZoomInAsync(), vm.ZoomInAsync(), vm.ZoomInAsync());
        Assert.That((await _storage.LoadUiSettingsAsync()).Zoom, Is.EqualTo(vm.Percent));
    }

    /// <summary>Das Skript entscheidet synchron über preventDefault – es muss genau dieselben Tasten kennen.</summary>
    [Test]
    public void LayoutScript_InterceptsSameKeysAsViewModel()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StudyScope.slnx"))) dir = dir.Parent;
        Assert.That(dir, Is.Not.Null);
        var js = File.ReadAllText(Path.Combine(dir!.FullName, "StudyScope.UI", "wwwroot", "js", "studyscope-layout.js"));

        static List<string> ArrayOf(string js, string name)
        {
            var m = Regex.Match(js, name + @"\s*=\s*\[(?<items>[^\]]*)\]");
            Assert.That(m.Success, Is.True, name);
            return Regex.Matches(m.Groups["items"].Value, @"'([^']*)'").Select(x => x.Groups[1].Value).ToList();
        }

        var keys = ZoomViewModel.ZoomInKeys.Concat(ZoomViewModel.ZoomOutKeys).Concat(ZoomViewModel.ResetKeys);
        Assert.That(ArrayOf(js, "ZOOM_KEYS"), Is.EquivalentTo(keys));
        Assert.That(ArrayOf(js, "ZOOM_CODES"), Is.EquivalentTo(ZoomViewModel.NumpadCodes));
    }
}
