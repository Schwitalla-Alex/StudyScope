using System.Text.RegularExpressions;

namespace StudyScope.Tests.Localization;

/// <summary>
/// Vorbereitung auf Sprachen mit Schreibrichtung rechts-nach-links (<c>dir="rtl"</c>): Abstände, Rahmen und Positionen
/// werden mit logischen Eigenschaften geschrieben (<c>margin-inline-start</c> statt <c>margin-left</c>,
/// <c>inset-inline-end</c> statt <c>right</c>, <c>text-align: start</c> …). Bewusst physische Angaben (z. B. Folien,
/// Druck, Zeichenfläche der Mindmap) tragen „ss:physisch“ in derselben Zeile.
/// Geprüft werden die bereits umgestellten Dateien; mit jedem Teil von Phase 3a kommen weitere dazu.
/// </summary>
[TestFixture]
public class LogicalCssTests
{
    private const string Marker = "ss:physisch";

    /// <summary>Umgestellte Dateien relativ zu StudyScope.UI.</summary>
    private static readonly string[] CheckedFiles =
    {
        "wwwroot/css/studyscope-base.css",
        "App.razor",
        "Components/AboutModal.razor",
        "Components/AppMenu.razor",
        "Components/AppearanceSettingsSection.razor",
        "Components/ChatArea.razor",
        "Components/DropdownMenu.razor",
        "Components/ExportSettingsSection.razor",
        "Components/NewProfileDialog.razor",
        "Components/SettingsModal.razor",
        "Components/SidebarPanel.razor",
        "Components/SpeechSettingsSection.razor",
        "Components/StatusBar.razor",
        "Components/SupportDialog.razor",
        "Components/TabBar.razor",
        // 3a-2 Lernen
        "Components/FlashcardView.razor",
        "Components/MindmapView.razor",
        "Components/PresentationView.razor",
        "Components/QuizView.razor",
        "Components/SlideView.razor",
    };

    private static readonly Regex Physical = new(
        @"(?<![\w-])(?:margin|padding|border)-(?:left|right)\b" +
        @"|(?<![\w-])(?:left|right)\s*:" +
        @"|\b(?:text-align|float|clear)\s*:\s*(?:left|right)\b" +
        @"|\bborder-(?:top|bottom)-(?:left|right)-radius\b",
        RegexOptions.Compiled);

    /// <summary>Kurzschreibweise mit vier Werten und unterschiedlichem linkem/rechtem Wert („margin: 0 4px 0 8px“).</summary>
    private static readonly Regex Shorthand = new(@"(?<![\w-])(?:margin|padding)\s*:\s*([^;""}{]+)", RegexOptions.Compiled);

    private static string UiDirectory
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StudyScope.slnx"))) dir = dir.Parent;
            Assert.That(dir, Is.Not.Null, "Repository-Wurzel (StudyScope.slnx) nicht gefunden");
            return Path.Combine(dir!.FullName, "StudyScope.UI");
        }
    }

    [Test]
    public void CheckedFiles_UseLogicalProperties()
    {
        var problems = new List<string>();
        foreach (var file in CheckedFiles)
        {
            var path = Path.Combine(UiDirectory, file);
            Assert.That(File.Exists(path), Is.True, $"{file} fehlt");
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Contains(Marker, StringComparison.Ordinal)) continue;
                foreach (Match m in Physical.Matches(line))
                    problems.Add($"{file}:{i + 1}: „{m.Value.Trim()}“ – logische Eigenschaft verwenden");
                foreach (Match m in Shorthand.Matches(line))
                {
                    var values = m.Groups[1].Value.Replace("!important", "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (values.Length == 4 && values[1] != values[3])
                        problems.Add($"{file}:{i + 1}: „{m.Value.Trim()}“ – links und rechts verschieden, logische Eigenschaften verwenden");
                }
            }
        }
        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }

    [Test]
    public void DirectionArrows_FlipInRightToLeft()
    {
        var css = File.ReadAllText(Path.Combine(UiDirectory, "wwwroot", "css", "studyscope-base.css"));
        Assert.That(css, Does.Contain(":root[dir=\"rtl\"] .ss-flip-rtl"));
    }
}
