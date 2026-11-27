using System.Globalization;
using System.Text.RegularExpressions;
using StudyScope.Setup.Core.Models;
using StudyScope.Setup.Core.Resources;
using StudyScope.Setup.Core.Services;
using StudyScope.Setup.Core.ViewModels;
using StudyScope.Tests.Setup;

namespace StudyScope.Tests.Localization;

/// <summary>
/// Installationsprogramm (Teil 3b-3): Sprache nach dem System (Deutsch, sonst Englisch), Meldungen der Setup-Logik
/// und der Linux-Menüeintrag in der Sprache des Setups, keine fest eingebauten deutschen Texte mehr.
/// </summary>
[TestFixture]
public class SetupLocalizationTests
{
    private static CultureInfo C(string name) => CultureInfo.GetCultureInfo(name);

    // ------------------------------------------------------------------ Sprache des Setups

    [TestCase("de-DE", "de-DE")]
    [TestCase("de-AT", "de-AT")] // Sprache vorhanden → regionale Kultur bleibt (Datum, Zahlen)
    [TestCase("en-US", "en-US")]
    [TestCase("en-GB", "en-GB")]
    [TestCase("fr-FR", "en")]    // nicht übersetzt → Englisch
    [TestCase("ja-JP", "en")]
    public void Resolve_FollowsTheSystemLanguage(string installed, string expected)
    {
        Assert.That(SetupLanguage.Resolve(null, C(installed)).Name, Is.EqualTo(expected));
    }

    [Test]
    public void Resolve_InvariantSystemCulture_FallsBackToEnglish()
    {
        Assert.That(SetupLanguage.Resolve(null, CultureInfo.InvariantCulture).Name, Is.EqualTo("en"));
    }

    [TestCase("en", "de-DE", "en")]
    [TestCase("de", "en-US", "de")]
    [TestCase("fr", "de-DE", "de-DE")] // Vorgabe ohne Übersetzung → Systemsprache
    [TestCase("system", "en-US", "en-US")]
    [TestCase("kein-gueltiger-name", "de-DE", "de-DE")]
    public void Resolve_OverrideVariable_WinsWhenTranslated(string setting, string installed, string expected)
    {
        Assert.That(SetupLanguage.Resolve(setting, C(installed)).Name, Is.EqualTo(expected));
    }

    [Test]
    public void Resolve_NewLanguage_IsFoundWithoutCodeChange()
    {
        // Simuliert eine künftige französische Übersetzung (Satellitenassembly „fr“)
        bool HasTexts(CultureInfo c) => c.Name is "en" or "fr";
        Assert.That(SetupLanguage.Resolve(null, C("fr-CA"), HasTexts).Name, Is.EqualTo("fr-CA"));
        Assert.That(SetupLanguage.Resolve(null, C("it-IT"), HasTexts).Name, Is.EqualTo("en"));
    }

    [Test]
    public void HasTexts_OnlyForTranslatedCultures_WithoutFallback()
    {
        Assert.That(SetupLanguage.HasTexts(C("en")), Is.True, "Satellitenassembly en fehlt");
        Assert.That(SetupLanguage.HasTexts(C("fr")), Is.False);
        Assert.That(SetupLanguage.HasTexts(C("de-DE")), Is.False, "de-DE darf nicht über den Rückfall als vorhanden gelten");
    }

    // ------------------------------------------------------------------ Meldungen der Setup-Logik

    [Test, SetUICulture("en")]
    public void ValidateInstallDirectory_English()
    {
        using var env = new SetupTestEnvironment();
        var service = new TestInstallService(new FakePathProvider(env), FakePayloadProvider.Default(),
            new FakeShortcutService(Path.Combine(env.Root, "Links")), new FakeProcessService());

        Assert.That(service.ValidateInstallDirectory(""), Is.EqualTo("Select an installation folder."));
        Assert.That(service.ValidateInstallDirectory("relativ"), Does.StartWith("Enter a full path (for example "));
        Assert.That(service.ValidateInstallDirectory(env.UserDataDirectory),
            Is.EqualTo($"The installation folder must not overlap with the folder for your profiles and settings ({Path.GetFullPath(env.UserDataDirectory)})."));
    }

    [Test, SetUICulture("en")]
    public async Task InstallAndUninstall_ReportProgressInEnglish()
    {
        using var env = new SetupTestEnvironment();
        var service = new TestInstallService(new FakePathProvider(env), FakePayloadProvider.Default(),
            new FakeShortcutService(Path.Combine(env.Root, "Links")), new FakeProcessService());

        var reports = new List<InstallProgress>();
        await service.InstallAsync(new InstallOptions { InstallDirectory = env.InstallDirectory }, new DirectProgress<InstallProgress>(reports.Add));
        await service.UninstallAsync(new UninstallOptions { InstallDirectory = env.InstallDirectory }, new DirectProgress<InstallProgress>(reports.Add));

        var messages = reports.Select(r => r.Message).ToList();
        Assert.That(messages, Does.Contain("Preparing installation…"));
        Assert.That(messages, Does.Contain("Copying StudyScope.UI.exe"));
        Assert.That(messages, Does.Contain("Installation complete."));
        Assert.That(messages, Does.Contain("Removing StudyScope.UI.exe"));
        Assert.That(messages.Last(), Is.EqualTo("StudyScope has been removed."));
    }

    [Test, SetUICulture("en")]
    public async Task InstallViewModel_AppRunning_English()
    {
        using var env = new SetupTestEnvironment();
        var service = new TestInstallService(new FakePathProvider(env), FakePayloadProvider.Default(),
            new FakeShortcutService(Path.Combine(env.Root, "Links")), new FakeProcessService { AppRunning = true });
        var vm = new InstallViewModel(service);

        var ok = await vm.RunAsync(new InstallOptions { InstallDirectory = env.InstallDirectory });

        Assert.That(ok, Is.False);
        Assert.That(vm.ErrorMessage, Is.EqualTo("StudyScope is still running. Close StudyScope and try again."));
        Assert.That(vm.StatusMessage, Is.EqualTo("The installation failed."));
    }

    // ------------------------------------------------------------------ Linux-Menüeintrag

    [Test, SetUICulture("en")]
    public void DesktopEntry_English()
    {
        var content = DesktopEntryBuilder.Build("/opt/s", "/opt/s/StudyScope.UI", "/opt/s/StudyScope.Setup", "studyscope");

        Assert.That(content, Does.Contain("Name=StudyScope\n"), "Produktname bleibt gleich");
        Assert.That(content, Does.Contain("GenericName=Learning software\n"));
        Assert.That(content, Does.Contain("Keywords=Learning;Flashcards;PDF;AI;Mind map;Quiz;Presentation;Report;Table;Audio;Podcast;Video;\n"));
        Assert.That(content, Does.Contain("[Desktop Action Uninstall]\nName=Uninstall StudyScope\n"));
    }

    [Test]
    public void DesktopEntry_German_Unchanged()
    {
        var content = DesktopEntryBuilder.Build("/opt/s", "/opt/s/StudyScope.UI", "/opt/s/StudyScope.Setup", "studyscope");

        Assert.That(content, Does.Contain("GenericName=Lernsoftware\n"));
        Assert.That(content, Does.Contain("Comment=Mit KI aus eigenen Unterlagen lernen: Chat, Karteikarten, Mindmap, Quiz und mehr\n"));
        Assert.That(content, Does.Contain("[Desktop Action Uninstall]\nName=StudyScope deinstallieren\n"));
    }

    [TestCase("de", "Eintrag im Anwendungsmenü (mit „StudyScope deinstallieren“)")]
    [TestCase("en", "Application menu entry (with “Uninstall StudyScope”)")]
    public void LinuxMenuLabel_NamesTheUninstallAction(string culture, string expected)
    {
        // Gleiche Formatierung wie LinuxShortcutService.MenuShortcutLabel (Klasse nur unter Linux nutzbar)
        using var scope = StudyScope.Core.Localization.CultureScope.Use(C(culture));
        var label = StudyScope.Core.Localization.Loc.Format(SetupCoreStrings.Shortcut_MenuLinux, ("action", SetupCoreStrings.Desktop_UninstallAction));
        Assert.That(label, Is.EqualTo(expected));
    }

    [Test]
    public void DesktopKeywords_EndWithSemicolon_InEveryLanguage()
    {
        // Desktop Entry Specification: Listenwerte enden mit „;“
        foreach (var culture in new[] { "de", "en" })
        {
            var keywords = SetupCoreStrings.ResourceManager.GetString(nameof(SetupCoreStrings.Desktop_Keywords), C(culture))!;
            Assert.That(keywords, Does.EndWith(";"), culture);
            Assert.That(keywords, Does.Not.Contain("\n"), culture);
        }
    }

    // ------------------------------------------------------------------ Keine fest eingebauten Texte

    private static readonly Regex GermanLiteral = new(@"""[^""\n]*[äöüÄÖÜß][^""\n]*""", RegexOptions.Compiled);
    private static readonly Regex GermanLetter = new("[äöüÄÖÜß]", RegexOptions.Compiled);

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StudyScope.slnx"))) dir = dir.Parent;
            Assert.That(dir, Is.Not.Null, "Repository-Wurzel (StudyScope.slnx) nicht gefunden");
            return dir!.FullName;
        }
    }

    private static bool IsComment(string line)
    {
        var t = line.TrimStart();
        return t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("<!--", StringComparison.Ordinal)
               || t.StartsWith("@*", StringComparison.Ordinal) || t.StartsWith("*", StringComparison.Ordinal);
    }

    /// <summary>
    /// Heuristik: Texte mit Umlauten im Code von Setup und Setup.Core (außerhalb von Kommentaren) sind vergessene
    /// Übersetzungen. Ausnahme sind nur Pfade der Sandbox („Startmenue“ ist absichtlich ohne Umlaut).
    /// </summary>
    [Test]
    public void SetupCode_HasNoHardCodedGermanTexts()
    {
        var problems = new List<string>();
        foreach (var project in new[] { "StudyScope.Setup", "StudyScope.Setup.Core" })
        {
            var files = Directory.EnumerateFiles(Path.Combine(RepoRoot, project), "*.*", SearchOption.AllDirectories)
                .Where(f => (f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal))
                            && !Path.GetRelativePath(RepoRoot, f).Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj"));
            foreach (var file in files)
            {
                var lines = File.ReadAllLines(file);
                var razor = file.EndsWith(".razor", StringComparison.Ordinal);
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (IsComment(line)) continue;
                    var code = line.Contains("//", StringComparison.Ordinal) && !line.Contains("://", StringComparison.Ordinal)
                        ? line[..line.IndexOf("//", StringComparison.Ordinal)]
                        : line;
                    if (GermanLiteral.IsMatch(code) || (razor && GermanLetter.IsMatch(code)))
                        problems.Add($"{Path.GetRelativePath(RepoRoot, file)}:{i + 1}: {line.Trim()}");
                }
            }
        }
        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }

    [Test]
    public void IndexHtml_HasNoFixedLanguage()
    {
        // lang/dir setzt App.razor aus der Kultur des Setups
        var html = File.ReadAllText(Path.Combine(RepoRoot, "StudyScope.Setup", "wwwroot", "index.html"));
        Assert.That(html, Does.Not.Contain("lang=\"de\""));
        Assert.That(GermanLetter.IsMatch(html), Is.False, "Ladetext ohne deutsche Wörter");
    }

    [Test]
    public void InstallScript_HasGermanAndEnglishOutput_WithoutExtraFiles()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot, "StudyScope.Setup", "Packaging", "linux", "install.sh"));
        Assert.That(script, Does.Contain("LC_ALL:-${LC_MESSAGES:-${LANG:-}}"));
        Assert.That(script, Does.Contain("msg() {"));
        Assert.That(script, Does.Not.Contain("\r"), "install.sh muss LF-Zeilenenden haben");

        // Jede Ausgabe läuft über msg: keine echo-/fail-Zeile mit deutschem Text außerhalb von msg
        var problems = script.Split('\n')
            .Select((l, i) => (Line: l, No: i + 1))
            .Where(x => !x.Line.TrimStart().StartsWith('#'))
            .Where(x => GermanLetter.IsMatch(x.Line) && !x.Line.Contains("msg", StringComparison.Ordinal) && !x.Line.TrimStart().StartsWith('"'))
            .Select(x => $"install.sh:{x.No}: {x.Line.Trim()}")
            .ToList();
        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }
}
