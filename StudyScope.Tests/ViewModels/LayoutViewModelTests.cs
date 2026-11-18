using System.Text.Json.Nodes;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.Services;
using StudyScope.ViewModels;

namespace StudyScope.Tests.ViewModels;

/// <summary>
/// Seitenleiste ein-/ausklappen: gemerkter Zustand in config.json, schmales Fenster ohne Überschreiben des gemerkten
/// Zustands, Strg+B-Bedingungen und Hinweis auf die aktiven Dokumente.
/// </summary>
[TestFixture]
public class LayoutViewModelTests
{
    private string _tempDir = null!;
    private JsonStorageService _storage = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "StudyScopeLayoutVm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _storage = new JsonStorageService(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
    }

    private static ShortcutKey CtrlB(string? tag = null, string? type = null, bool editable = false) =>
        new("b", Ctrl: true, FocusTag: tag, FocusInputType: type, FocusEditable: editable);

    // ---------- Speichern und Laden ----------

    [Test]
    public async Task Load_WithoutConfig_IsExpanded()
    {
        var vm = new LayoutViewModel(_storage);

        await vm.LoadAsync();

        Assert.That(vm.IsSidebarCollapsed, Is.False);
    }

    [Test]
    public async Task Toggle_SavesState_AndNewInstanceRestoresIt()
    {
        var vm = new LayoutViewModel(_storage);
        await vm.LoadAsync();

        await vm.ToggleAsync();

        Assert.That(vm.IsSidebarCollapsed, Is.True);
        var restarted = new LayoutViewModel(new JsonStorageService(_tempDir));
        await restarted.LoadAsync();
        Assert.That(restarted.IsSidebarCollapsed, Is.True, "nach dem Neustart wiederhergestellt");

        await restarted.ToggleAsync();
        Assert.That((await _storage.LoadUiSettingsAsync()).SidebarCollapsed, Is.False);
    }

    [Test]
    public async Task UiSettings_SurviveSavingOtherSettings_AndViceVersa()
    {
        await _storage.SaveAiConfigAsync(new AiConfig { SelectedProvider = AiProviderType.FakeAi, SelectedModelName = "Testmodell" });
        await _storage.SaveExportSettingsAsync(new ExportSettings { FfmpegPath = @"C:\Werkzeuge\ffmpeg.exe" });
        await _storage.SaveUiSettingsAsync(new UiSettings { SidebarCollapsed = true });

        await _storage.SaveAiConfigAsync(new AiConfig { SelectedProvider = AiProviderType.FakeAi, SelectedModelName = "Anderes Modell" });
        await _storage.SaveExportSettingsAsync(new ExportSettings { FfmpegPath = @"D:\ffmpeg" });
        await _storage.SaveSpeechSettingsAsync("Testprofil", new SpeechSettings());

        Assert.That((await _storage.LoadUiSettingsAsync()).SidebarCollapsed, Is.True);
        Assert.That((await _storage.LoadAiConfigAsync()).SelectedModelName, Is.EqualTo("Anderes Modell"));
        Assert.That((await _storage.LoadExportSettingsAsync()).FfmpegPath, Is.EqualTo(@"D:\ffmpeg"));
    }

    [Test]
    public async Task Load_BrokenEntry_FallsBackToDefault()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "config.json"), new JsonObject { ["UiSettings"] = "kaputt" }.ToJsonString());

        Assert.That((await _storage.LoadUiSettingsAsync()).SidebarCollapsed, Is.False);
    }

    // ---------- Schmales Fenster ----------

    [Test]
    public async Task NarrowWindow_CollapsesAutomatically_WithoutChangingSavedState()
    {
        var vm = new LayoutViewModel(_storage);
        await vm.LoadAsync();

        vm.SetWindowWidth(LayoutViewModel.NarrowWindowWidth - 1);

        Assert.That(vm.IsSidebarCollapsed, Is.True);
        Assert.That(vm.IsAutoCollapsed, Is.True);
        Assert.That(vm.ToggleTooltip, Does.Contain("automatisch"));
        Assert.That(File.Exists(Path.Combine(_tempDir, "config.json")), Is.False, "nichts gespeichert");

        vm.SetWindowWidth(1400);
        Assert.That(vm.IsSidebarCollapsed, Is.False, "breit: wieder der gemerkte Zustand");
    }

    [Test]
    public async Task NarrowWindow_ToggleExpandsTemporarily_NotSaved_ResetWhenWide()
    {
        var vm = new LayoutViewModel(_storage);
        await vm.LoadAsync();
        vm.SetWindowWidth(700);

        await vm.ToggleAsync();
        Assert.That(vm.IsSidebarCollapsed, Is.False, "vorübergehend aufgeklappt");
        Assert.That((await _storage.LoadUiSettingsAsync()).SidebarCollapsed, Is.False);
        Assert.That(File.Exists(Path.Combine(_tempDir, "config.json")), Is.False, "nicht gespeichert");

        await vm.ToggleAsync();
        Assert.That(vm.IsSidebarCollapsed, Is.True);

        await vm.ToggleAsync();
        vm.SetWindowWidth(1200);
        vm.SetWindowWidth(700);
        Assert.That(vm.IsSidebarCollapsed, Is.True, "nach breit → schmal wieder automatisch eingeklappt");
    }

    [Test]
    public async Task NarrowWindow_SavedCollapsed_ToggleOpensAndSaves()
    {
        await _storage.SaveUiSettingsAsync(new UiSettings { SidebarCollapsed = true });
        var vm = new LayoutViewModel(_storage);
        await vm.LoadAsync();
        vm.SetWindowWidth(700);

        await vm.ToggleAsync();

        Assert.That(vm.IsSidebarCollapsed, Is.False, "ausdrücklich aufgeklappt bleibt sichtbar");
        Assert.That((await _storage.LoadUiSettingsAsync()).SidebarCollapsed, Is.False);
    }

    [Test]
    public void SetWindowWidth_Zero_IsIgnored()
    {
        var vm = new LayoutViewModel(_storage);

        vm.SetWindowWidth(0);

        Assert.That(vm.IsNarrowWindow, Is.False);
    }

    // ---------- Strg+B ----------

    [Test]
    public void Shortcut_CtrlB_WithoutFocus_Toggles()
    {
        Assert.That(LayoutViewModel.IsToggleShortcut(CtrlB(), isPresenting: false), Is.True);
        Assert.That(LayoutViewModel.IsToggleShortcut(new ShortcutKey("B", Ctrl: true, FocusTag: "BUTTON"), false), Is.True, "Großbuchstabe, Knopf fokussiert");
        Assert.That(LayoutViewModel.IsToggleShortcut(CtrlB("INPUT", "checkbox"), false), Is.True, "Häkchen ist kein Eingabefeld");
    }

    [TestCase("INPUT", "text", false)]
    [TestCase("INPUT", null, false)]
    [TestCase("INPUT", "search", false)]
    [TestCase("INPUT", "number", false)]
    [TestCase("TEXTAREA", null, false)]
    [TestCase("SELECT", null, false)]
    [TestCase("DIV", null, true)]
    public void Shortcut_WhileTyping_IsIgnored(string tag, string? type, bool editable)
    {
        Assert.That(LayoutViewModel.IsToggleShortcut(CtrlB(tag, type, editable), isPresenting: false), Is.False);
    }

    [Test]
    public void Shortcut_InPresentationOrFullScreen_IsIgnored()
    {
        Assert.That(LayoutViewModel.IsToggleShortcut(CtrlB(), isPresenting: true), Is.False);
    }

    [Test]
    public void Shortcut_OtherKeysOrModifiers_AreIgnored()
    {
        Assert.That(LayoutViewModel.IsToggleShortcut(new ShortcutKey("b", Ctrl: false), false), Is.False);
        Assert.That(LayoutViewModel.IsToggleShortcut(new ShortcutKey("n", Ctrl: true), false), Is.False);
        Assert.That(LayoutViewModel.IsToggleShortcut(new ShortcutKey("b", Ctrl: true, Shift: true), false), Is.False);
        Assert.That(LayoutViewModel.IsToggleShortcut(new ShortcutKey("b", Ctrl: true, Alt: true), false), Is.False, "AltGr = Strg+Alt");
        Assert.That(LayoutViewModel.IsToggleShortcut(new ShortcutKey("b", Ctrl: true, Meta: true), false), Is.False);
        Assert.That(LayoutViewModel.IsToggleShortcut(new ShortcutKey("b", Ctrl: true, Repeat: true), false), Is.False, "gehaltene Taste schaltet nicht mehrfach");
    }

    [Test]
    public async Task HandleShortcut_TogglesOnlyWhenAllowed()
    {
        var vm = new LayoutViewModel(_storage);
        await vm.LoadAsync();

        Assert.That(await vm.HandleShortcutAsync(CtrlB("TEXTAREA"), false), Is.False);
        Assert.That(vm.IsSidebarCollapsed, Is.False);

        Assert.That(await vm.HandleShortcutAsync(CtrlB(), false), Is.True);
        Assert.That(vm.IsSidebarCollapsed, Is.True);
    }

    [Test]
    public async Task Toggle_RaisesPropertyChanged_ForCollapsedState()
    {
        var vm = new LayoutViewModel(_storage);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await vm.ToggleAsync();

        Assert.That(changed, Does.Contain(nameof(LayoutViewModel.IsSidebarCollapsed)));
        Assert.That(changed, Does.Contain(nameof(LayoutViewModel.ToggleTooltip)));
    }

    // ---------- Hinweis auf die aktiven Dokumente ----------

    private static DocumentItem Doc(string name, bool selected = true, bool missing = false) =>
        new() { FileName = name, FilePath = @"C:\Unterlagen\" + name, IsSelected = selected, IsMissing = missing };

    [Test]
    public void Badge_CountsActiveDocuments_MissingNotActive()
    {
        var docs = new[] { Doc("zellbiologie.pdf"), Doc("genetik.txt", selected: false), Doc("verschoben.pdf", missing: true) };

        Assert.That(LayoutViewModel.DocumentBadge(docs), Is.EqualTo("📄 1/3"));
        var tooltip = LayoutViewModel.DocumentBadgeTooltip("Testprofil", docs);
        Assert.That(tooltip, Does.Contain("Profil: Testprofil").And.Contain("Aktiv (1 von 3):").And.Contain("• zellbiologie.pdf"));
        Assert.That(tooltip, Does.Not.Contain("genetik.txt").And.Not.Contain("verschoben.pdf"));
    }

    [Test]
    public void Badge_NoDocuments_AndNoneActive()
    {
        Assert.That(LayoutViewModel.DocumentBadge(Array.Empty<DocumentItem>()), Is.EqualTo("📄 0/0"));
        Assert.That(LayoutViewModel.DocumentBadgeTooltip("Testprofil", Array.Empty<DocumentItem>()), Does.Contain("Keine Dokumente geladen"));
        Assert.That(LayoutViewModel.DocumentBadgeTooltip("Testprofil", new[] { Doc("a.pdf", selected: false) }), Does.Contain("Keines von 1 Dokumenten aktiv"));
    }

    [Test]
    public void BadgeTooltip_ManyDocuments_IsShortened()
    {
        var docs = Enumerable.Range(1, LayoutViewModel.MaxTooltipDocuments + 3).Select(i => Doc($"kapitel{i}.pdf")).ToList();

        var tooltip = LayoutViewModel.DocumentBadgeTooltip("Testprofil", docs);

        Assert.That(tooltip, Does.Contain("… und 3 weitere"));
        Assert.That(tooltip, Does.Not.Contain($"kapitel{LayoutViewModel.MaxTooltipDocuments + 1}.pdf"));
    }
}
