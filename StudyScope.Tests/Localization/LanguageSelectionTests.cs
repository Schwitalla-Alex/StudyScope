using System.Globalization;
using System.Text.RegularExpressions;
using Moq;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Localization;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.Services;
using StudyScope.Setup.Core.Resources;
using StudyScope.Setup.Core.Services;
using StudyScope.ViewModels;
using StudyScope.ViewModels.Resources;

namespace StudyScope.Tests.Localization;

/// <summary>
/// Sprachwahl (Teil 3b-4): Systemsprache → Oberflächensprache in App und Setup, Auswahl unter „Darstellung“,
/// Anzeige des Standardprofils, Esc in allen Dialogen.
/// Wichtig: Eine nicht unterstützte Systemsprache (fr-FR, ja-JP, ar-SA) ergibt ausdrücklich „en“ als UI-Kultur. Bliebe
/// sie bei fr-FR, fände der ResourceManager keine französische Datei und fiele auf die neutrale, deutsche .resx zurück.
/// </summary>
[TestFixture]
public class LanguageSelectionTests
{
    private static readonly string[] AppLanguages = { "de", "en" };
    private static CultureInfo C(string name) => CultureInfo.GetCultureInfo(name);

    // ------------------------------------------------------------------ Systemsprache → Oberflächensprache

    /// <summary>Erwartete UI-Kultur und Sprache der Texte („de“ = neutrale deutsche Datei, „en“ = Übersetzung).</summary>
    private static readonly object[] SystemCultures =
    {
        new object[] { "de-DE", "de-DE", "de" },
        new object[] { "de-AT", "de-AT", "de" },
        new object[] { "de-CH", "de-CH", "de" },
        new object[] { "en-US", "en-US", "en" },
        new object[] { "en-GB", "en-GB", "en" },
        new object[] { "fr-FR", "en", "en" },
        new object[] { "ja-JP", "en", "en" },
        new object[] { "ar-SA", "en", "en" },
    };

    [TestCaseSource(nameof(SystemCultures))]
    public void App_SystemLanguage_ResolvesToSupportedCulture(string installed, string expectedCulture, string textLanguage)
    {
        var culture = UiLanguage.Resolve(UiLanguage.SystemSetting, C(installed), AppLanguages);

        Assert.That(culture.Name, Is.EqualTo(expectedCulture));
        // Die Texte in dieser Kultur: Englisch nie aus Versehen Deutsch und umgekehrt
        var saved = VmStrings.ResourceManager.GetString(nameof(VmStrings.Appearance_Saved), culture);
        Assert.That(saved, Is.EqualTo(textLanguage == "de" ? "Darstellung gespeichert." : "Appearance saved."));
        Assert.That(VmStrings.ResourceManager.GetString(nameof(VmStrings.Profile_DefaultName), culture),
            Is.EqualTo(textLanguage == "de" ? "Standard" : "Default"));
    }

    [TestCaseSource(nameof(SystemCultures))]
    public void Setup_SystemLanguage_ResolvesToSupportedCulture(string installed, string expectedCulture, string textLanguage)
    {
        var culture = SetupLanguage.Resolve(null, C(installed));

        Assert.That(culture.Name, Is.EqualTo(expectedCulture));
        var text = SetupCoreStrings.ResourceManager.GetString(nameof(SetupCoreStrings.Install_Cancelled), culture);
        Assert.That(text, Is.EqualTo(textLanguage == "de" ? "Die Installation wurde abgebrochen." : "The installation was cancelled."));
    }

    [TestCase("fr-FR")]
    [TestCase("ja-JP")]
    [TestCase("ar-SA")]
    public void UnsupportedSystemLanguage_IsNeverLeftAsUiCulture(string installed)
    {
        // Gegenprobe: Mit fr-FR als UI-Kultur kämen die Texte aus der neutralen (deutschen) Datei
        Assert.That(VmStrings.ResourceManager.GetString(nameof(VmStrings.Appearance_Saved), C(installed)), Is.EqualTo("Darstellung gespeichert."));
        Assert.That(UiLanguage.Resolve(null, C(installed), AppLanguages).Name, Is.EqualTo("en"));
        Assert.That(UiLanguage.Resolve("", C(installed), AppLanguages).Name, Is.EqualTo("en"));
        Assert.That(SetupLanguage.Resolve(null, C(installed)).Name, Is.EqualTo("en"));
    }

    [TestCase("de", "fr-FR", "de")]
    [TestCase("en", "de-AT", "en")]
    [TestCase("system", "en-GB", "en-GB")]
    [TestCase("fr", "de-CH", "de-CH")] // gespeicherte Sprache gibt es nicht (mehr) → Systemsprache
    [TestCase("fr", "ja-JP", "en")]
    public void App_Setting_WinsOverSystem(string setting, string installed, string expected)
    {
        Assert.That(UiLanguage.Resolve(setting, C(installed), AppLanguages).Name, Is.EqualTo(expected));
    }

    [Test]
    public void NativeName_InTheLanguageItself()
    {
        Assert.That(UiLanguage.NativeName("de"), Is.EqualTo("Deutsch"));
        Assert.That(UiLanguage.NativeName("en"), Is.EqualTo("English"));
        Assert.That(UiLanguage.NativeName("fr"), Is.EqualTo("Français"));
    }

    // ------------------------------------------------------------------ Auswahl unter „Darstellung“

    private sealed class FakeLanguageService : IUiLanguageService
    {
        public IReadOnlyList<string> AvailableCultures { get; init; } = new[] { "de", "en" };
        public string? OverrideCulture { get; init; }
        public string Installed { get; init; } = "de-DE";
        public List<string> Applied { get; } = new();
        public CultureInfo Resolve(string? setting) => UiLanguage.Resolve(OverrideCulture ?? setting, C(Installed), AvailableCultures);
        public void Apply(CultureInfo culture) => Applied.Add(culture.Name); // nicht wirklich umstellen: Tests laufen parallel
    }

    private string _tempDir = null!;
    private JsonStorageService _storage = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "StudyScopeLanguage_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _storage = new JsonStorageService(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
    }

    [Test]
    public async Task Language_MissingInConfig_IsSystem()
    {
        var vm = new AppearanceViewModel(_storage, new FakeLanguageService());
        await vm.LoadAsync();
        Assert.That(vm.SavedLanguage, Is.EqualTo("system"));
        Assert.That((await _storage.LoadUiSettingsAsync()).Language, Is.EqualTo("system"));
    }

    [Test]
    public void LanguageOptions_SystemPlusEveryLanguageInItsOwnName()
    {
        var vm = new AppearanceViewModel(_storage, new FakeLanguageService());
        Assert.That(vm.LanguageOptions.Select(o => o.Value), Is.EqualTo(new[] { "system", "de", "en" }));
        Assert.That(vm.LanguageOptions.Select(o => o.Label), Is.EqualTo(new[] { "Wie System", "Deutsch", "English" }));
    }

    [Test]
    public async Task SaveLanguage_AppliesAndRaisesLanguageChanged_KeepsOtherSettings()
    {
        await _storage.SaveUiSettingsAsync(new UiSettings { SidebarCollapsed = true, SidebarWidth = 400, Theme = ThemeMode.Dark });
        var language = new FakeLanguageService();
        var vm = new AppearanceViewModel(_storage, language);
        await vm.LoadAsync();
        var raised = 0;
        vm.LanguageChanged += (_, _) => raised++;

        vm.SelectedLanguage = "en";
        Assert.That(vm.IsDirty, Is.True);
        Assert.That(language.Applied, Is.Empty, "keine Vorschau: erst Speichern stellt um");

        await vm.SaveAsync();

        Assert.That(language.Applied, Is.EqualTo(new[] { "en" }));
        Assert.That(raised, Is.EqualTo(1));
        Assert.That(vm.IsDirty, Is.False);
        var saved = await _storage.LoadUiSettingsAsync();
        Assert.That(saved.Language, Is.EqualTo("en"));
        Assert.That(saved.Theme, Is.EqualTo(ThemeMode.Dark));
        Assert.That(saved.SidebarCollapsed, Is.True);
        Assert.That(saved.SidebarWidth, Is.EqualTo(400));
    }

    [Test, SetUICulture("de-DE")]
    public async Task SaveLanguage_SameEffectiveCulture_NoRebuild()
    {
        // „Wie System“ → „Deutsch“ auf einem deutschen System: gespeichert, aber nichts neu aufzubauen
        var language = new FakeLanguageService { Installed = "de-DE" };
        var vm = new AppearanceViewModel(_storage, language);
        await vm.LoadAsync();
        var raised = 0;
        vm.LanguageChanged += (_, _) => raised++;

        vm.SelectedLanguage = "de-DE";
        await vm.SaveAsync();

        Assert.That((await _storage.LoadUiSettingsAsync()).Language, Is.EqualTo("de-DE"));
        Assert.That(raised, Is.Zero);
        Assert.That(language.Applied, Is.Empty);
    }

    [Test]
    public async Task OnlyThemeChanged_NoLanguageSwitch()
    {
        var language = new FakeLanguageService();
        var vm = new AppearanceViewModel(_storage, language);
        await vm.LoadAsync();
        vm.SelectedTheme = ThemeMode.Dark;
        await vm.SaveAsync();
        Assert.That(language.Applied, Is.Empty);
    }

    [Test]
    public async Task Revert_DiscardsLanguageSelection()
    {
        var vm = new AppearanceViewModel(_storage, new FakeLanguageService());
        await vm.LoadAsync();
        vm.SelectedLanguage = "en";
        vm.Revert();
        Assert.That(vm.SelectedLanguage, Is.EqualTo("system"));
        Assert.That(vm.IsDirty, Is.False);
    }

    [Test]
    public async Task StatusMessage_SurvivesTheRebuildAfterASwitch()
    {
        var vm = new AppearanceViewModel(_storage, new FakeLanguageService());
        await vm.LoadAsync();
        vm.SelectedLanguage = "en";
        await vm.SaveAsync();
        await vm.LoadAsync(); // neu aufgebauter Abschnitt
        Assert.That(vm.StatusMessage, Is.Not.Empty);
        await vm.LoadAsync(); // späteres Öffnen
        Assert.That(vm.StatusMessage, Is.Empty);
    }

    [TestCase(null, "system")]
    [TestCase("", "system")]
    [TestCase("SYSTEM", "system")]
    [TestCase("en", "en")]
    [TestCase("de-AT", "de-AT")]
    [TestCase("kein-gueltiger-name", "system")]
    public void NormalizeLanguage(string? value, string expected) =>
        Assert.That(AppearanceViewModel.NormalizeLanguage(value), Is.EqualTo(expected));

    [Test]
    public void Override_IsShown()
    {
        var vm = new AppearanceViewModel(_storage, new FakeLanguageService { OverrideCulture = "en" });
        Assert.That(vm.LanguageOverride, Is.EqualTo("en"));
    }

    [Test]
    public void UiSettings_Clone_KeepsLanguage() =>
        Assert.That(new UiSettings { Language = "en" }.Clone().Language, Is.EqualTo("en"));

    [Test]
    public void AppMenu_RefreshTexts_FollowsTheNewLanguage()
    {
        var menu = new StudyScope.ViewModels.Menu.AppMenuViewModel();
        using (CultureScope.Use(C("de-DE"))) menu.RefreshTexts();
        Assert.That(menu.Navigator.Items[0].Text, Is.EqualTo("Datei öffnen…"));
        using (CultureScope.Use(C("en"))) menu.RefreshTexts();
        Assert.That(menu.Navigator.Items[0].Text, Is.EqualTo("Open file…"));
    }

    // ------------------------------------------------------------------ Standardprofil „Default“

    [Test, SetUICulture("de-DE")]
    public void DefaultProfile_German_IsStandard()
    {
        Assert.That(ProfileDisplay.Name("Standard"), Is.EqualTo("Standard"));
        Assert.That(ProfileDisplay.Name("Biologie"), Is.EqualTo("Biologie"));
    }

    [Test, SetUICulture("en-US")]
    public void DefaultProfile_English_IsDefault_StoredNameUnchanged()
    {
        Assert.That(ProfileDisplay.Name("Standard"), Is.EqualTo("Default"));
        Assert.That(ProfileDisplay.Name("standard"), Is.EqualTo("Default"));
        Assert.That(ProfileDisplay.Name("Biologie"), Is.EqualTo("Biologie"));
        Assert.That(ProfileDisplay.DefaultProfileName, Is.EqualTo("Standard"));
    }

    [Test, SetUICulture("en")]
    public void DefaultProfile_OwnProfileNamedDefault_KeepsStandard()
    {
        // Ein früher angelegtes eigenes Profil „Default“: das Standardprofil bleibt unterscheidbar
        var all = new[] { "Standard", "Default" };
        Assert.That(ProfileDisplay.Name("Standard", all), Is.EqualTo("Standard"));
        Assert.That(ProfileDisplay.Name("Default", all), Is.EqualTo("Default"));
    }

    [TestCase("de-DE")]
    [TestCase("en")]
    public void NewProfile_NamedLikeTheDefaultProfileInAnyLanguage_IsRejected(string culture)
    {
        using var scope = CultureScope.Use(C(culture));
        var storage = new Mock<IStorageService>();
        storage.Setup(s => s.LoadAiConfigForProfileAsync(It.IsAny<string>())).ReturnsAsync(new AiConfig());
        storage.Setup(s => s.LoadDocumentsAsync(It.IsAny<string>())).ReturnsAsync(new List<DocumentItem>());
        var vm = new DocumentListViewModel(storage.Object, new Mock<IDocumentService>().Object);
        vm.AvailableProfiles.Add("Standard");

        foreach (var name in new[] { "Standard", "standard", "Default", "DEFAULT" })
            Assert.That(vm.ValidateProfileName(name), Is.Not.Null, $"{name} ({culture})");
        Assert.That(vm.ValidateProfileName("Defaults"), Is.Null);
        // Die Meldung nennt das Profil so, wie es in der Liste steht
        Assert.That(vm.ValidateProfileName("Default"), Does.Contain(culture == "en" ? "“Default”" : "„Standard“"));
    }

    [Test, SetUICulture("en")]
    public void StatusBarAndTitles_UseTheDisplayName()
    {
        var storage = new Mock<IStorageService>();
        var vm = new DocumentListViewModel(storage.Object, new Mock<IDocumentService>().Object);
        vm.AvailableProfiles.Add("Standard");
        vm.CurrentProfile = "Standard";
        Assert.That(vm.CurrentProfileDisplayName, Is.EqualTo("Default"));
        Assert.That(vm.CurrentProfile, Is.EqualTo("Standard"), "gespeicherter Name bleibt");
    }

    // ------------------------------------------------------------------ Esc schließt jeden Dialog

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StudyScope.slnx"))) dir = dir.Parent;
            Assert.That(dir, Is.Not.Null);
            return dir!.FullName;
        }
    }

    /// <summary>Dialoge erkennt man an der Abdeckung (Overlay) bzw. aria-modal.</summary>
    private static readonly Regex DialogMarker = new(@"aria-modal=""true""|ss-dialog-overlay|background:\s*var\(--ss-overlay\)", RegexOptions.Compiled);

    [Test]
    public void EveryDialog_ClosesWithEscape()
    {
        var problems = new List<string>();
        var dialogs = 0;
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot, "StudyScope.UI", "Components"), "*.razor"))
        {
            var text = File.ReadAllText(file);
            var markup = Regex.Replace(text, @"<style>.*?</style>", "", RegexOptions.Singleline);
            if (!DialogMarker.IsMatch(markup)) continue;
            dialogs++;
            var name = Path.GetFileName(file);
            var opened = Regex.Matches(markup, @"\sdata-ss-dialog[\s>]").Count;
            var closers = Regex.Matches(markup, @"\sdata-ss-dialog-close[\s>]").Count;
            if (opened == 0) problems.Add($"{name}: Dialog ohne data-ss-dialog");
            if (closers < opened) problems.Add($"{name}: {opened} Dialog(e), aber {closers} Element(e) mit data-ss-dialog-close");
            // Eigene Esc-Behandlung würde zusammen mit der zentralen zweimal schließen
            if (Regex.IsMatch(text, @"Key\s*==\s*""Escape""\)\s*await\s+Close|Key\s*==\s*""Escape""\)\s*\{?\s*Cancel"))
                problems.Add($"{name}: eigene Esc-Behandlung zusätzlich zur zentralen");
        }
        Assert.That(dialogs, Is.GreaterThanOrEqualTo(6), "Einstellungen, Über, Neues Profil (2×), Unterstützen, Karteikarten-Auswahl");
        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }

    [Test]
    public void LayoutScript_HandlesEscapeForDialogs()
    {
        var js = File.ReadAllText(Path.Combine(RepoRoot, "StudyScope.UI", "wwwroot", "js", "studyscope-layout.js"));
        Assert.That(js, Does.Contain("[data-ss-dialog]"));
        Assert.That(js, Does.Contain("[data-ss-dialog-close]"));
        Assert.That(js, Does.Contain("ss-appmenu-trigger"), "Fokus fällt auf ☰ zurück");
    }
}
