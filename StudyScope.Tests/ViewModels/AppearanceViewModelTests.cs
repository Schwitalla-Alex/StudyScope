using System.Text.Json.Nodes;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.Services;
using StudyScope.ViewModels;

namespace StudyScope.Tests.ViewModels;

/// <summary>
/// Darstellung (Hell/Dunkel/System): Speichern in config.json unter UiSettings, Vorschau/Zurücksetzen und Erhalt des
/// Eintrags, wenn andere Einstellungen gespeichert werden.
/// </summary>
[TestFixture]
public class AppearanceViewModelTests
{
    private string _tempDir = null!;
    private JsonStorageService _storage = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "StudyScopeAppearance_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _storage = new JsonStorageService(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
    }

    private string ConfigPath => Path.Combine(_tempDir, "config.json");

    [Test]
    public async Task Load_WithoutConfig_IsSystem()
    {
        var vm = new AppearanceViewModel(_storage);
        await vm.LoadAsync();
        Assert.That(vm.SavedTheme, Is.EqualTo(ThemeMode.System));
        Assert.That(vm.SelectedTheme, Is.EqualTo(ThemeMode.System));
        Assert.That(vm.IsDirty, Is.False);
    }

    [Test]
    public async Task Load_OlderConfigWithoutTheme_IsSystem_AndKeepsSidebar()
    {
        await File.WriteAllTextAsync(ConfigPath,
            new JsonObject { ["UiSettings"] = new JsonObject { ["SidebarCollapsed"] = true, ["SidebarWidth"] = 400 } }.ToJsonString());

        var settings = await _storage.LoadUiSettingsAsync();
        Assert.That(settings.Theme, Is.EqualTo(ThemeMode.System));
        Assert.That(settings.SidebarCollapsed, Is.True);
    }

    [Test]
    public async Task Select_IsPreviewUntilSaved()
    {
        var vm = new AppearanceViewModel(_storage);
        await vm.LoadAsync();
        vm.SelectedTheme = ThemeMode.Dark;

        Assert.That(vm.IsDirty, Is.True);
        Assert.That((await _storage.LoadUiSettingsAsync()).Theme, Is.EqualTo(ThemeMode.System));

        await vm.SaveAsync();
        Assert.That(vm.IsDirty, Is.False);
        Assert.That((await _storage.LoadUiSettingsAsync()).Theme, Is.EqualTo(ThemeMode.Dark));
    }

    [Test]
    public async Task Revert_RestoresSavedTheme()
    {
        var vm = new AppearanceViewModel(_storage);
        await vm.LoadAsync();
        vm.SelectedTheme = ThemeMode.Light;
        await vm.SaveAsync();
        vm.SelectedTheme = ThemeMode.Dark;

        vm.Revert();

        Assert.That(vm.SelectedTheme, Is.EqualTo(ThemeMode.Light));
        Assert.That(vm.IsDirty, Is.False);
    }

    [Test]
    public async Task Theme_IsStoredAsReadableText()
    {
        var vm = new AppearanceViewModel(_storage);
        await vm.LoadAsync();
        vm.SelectedTheme = ThemeMode.Dark;
        await vm.SaveAsync();

        var node = JsonNode.Parse(await File.ReadAllTextAsync(ConfigPath))!;
        Assert.That(node["UiSettings"]!["Theme"]!.GetValue<string>(), Is.EqualTo("Dark"));
    }

    [Test]
    public async Task UnknownNumericTheme_FallsBackToSystem()
    {
        await File.WriteAllTextAsync(ConfigPath, new JsonObject { ["UiSettings"] = new JsonObject { ["Theme"] = 7 } }.ToJsonString());
        var vm = new AppearanceViewModel(_storage);
        await vm.LoadAsync();
        Assert.That(vm.SavedTheme, Is.EqualTo(ThemeMode.System));
    }

    [Test]
    public async Task Theme_SurvivesAllOtherSaveFunctions()
    {
        var vm = new AppearanceViewModel(_storage);
        await vm.LoadAsync();
        vm.SelectedTheme = ThemeMode.Dark;
        await vm.SaveAsync();

        await _storage.SaveAiConfigAsync(new AiConfig { SelectedProvider = AiProviderType.FakeAi });
        await _storage.SaveSpeechSettingsAsync("Testprofil", new SpeechSettings());
        await _storage.SaveExportSettingsAsync(new ExportSettings { FfmpegPath = "C:/ffmpeg/bin" });

        var layout = new LayoutViewModel(_storage);
        await layout.LoadAsync();
        await layout.ToggleAsync();
        await layout.EndResizeAsync(400);
        await layout.ResetWidthAsync();

        Assert.That((await _storage.LoadUiSettingsAsync()).Theme, Is.EqualTo(ThemeMode.Dark));
    }

    [Test]
    public async Task SavingTheme_KeepsSidebarSettings()
    {
        var layout = new LayoutViewModel(_storage);
        await layout.LoadAsync();
        await layout.ToggleAsync();
        await layout.EndResizeAsync(400);

        var vm = new AppearanceViewModel(_storage);
        await vm.LoadAsync();
        vm.SelectedTheme = ThemeMode.Light;
        await vm.SaveAsync();

        var settings = await _storage.LoadUiSettingsAsync();
        Assert.Multiple(() =>
        {
            Assert.That(settings.SidebarWidth, Is.EqualTo(400));
            Assert.That(settings.Theme, Is.EqualTo(ThemeMode.Light));
        });
    }

    [TestCase(ThemeMode.System, "system")]
    [TestCase(ThemeMode.Light, "light")]
    [TestCase(ThemeMode.Dark, "dark")]
    public void ThemeAttribute_MatchesScript(ThemeMode mode, string expected) =>
        Assert.That(AppearanceViewModel.ThemeAttribute(mode), Is.EqualTo(expected));

    [Test]
    public void UiSettingsClone_CopiesTheme() =>
        Assert.That(new UiSettings { Theme = ThemeMode.Dark }.Clone().Theme, Is.EqualTo(ThemeMode.Dark));
}
