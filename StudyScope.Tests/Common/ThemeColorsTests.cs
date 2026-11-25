using System.Text.RegularExpressions;

namespace StudyScope.Tests.Common;

/// <summary>
/// Farbschema (Hell/Dunkel): Razor-Dateien der App dürfen keine festen Farben enthalten (Schatten ausgenommen), und jede verwendete
/// Variable muss im hellen und im dunklen Satz von studyscope-base.css stehen. Absichtlich feste Farben (z. B. die
/// Vorschaumuster im Abschnitt „Darstellung“) tragen den Kommentar „ss:feste-farbe“ in derselben Zeile.
/// </summary>
[TestFixture]
public class ThemeColorsTests
{
    /// <summary>Alle Razor-Dateien der App (relativ zu StudyScope.UI); seit Phase 2b sind alle auf das Farbschema umgestellt.</summary>
    private static IEnumerable<string> ThemedFiles()
    {
        var ui = UiDirectory;
        return Directory.EnumerateFiles(ui, "*.razor", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(f => Path.GetRelativePath(ui, f).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(f => f, StringComparer.Ordinal);
    }

    private const string FixedColorMarker = "ss:feste-farbe";

    private static readonly Regex FixedColor = new(@"#[0-9a-fA-F]{3,8}\b|\brgba?\(|\b(?:color|background(?:-color)?|border(?:-[a-z]+)?|fill|stroke)\s*:\s*(?:white|black)\b",
        RegexOptions.Compiled);
    /// <summary>Schatten mit Schwarz-Transparenz wirken in beiden Schemata und sind erlaubt.</summary>
    private static readonly Regex Shadow = new(@"(?:box|text)-shadow\s*:[^;""}]*", RegexOptions.Compiled);
    private static readonly Regex VarUse = new(@"var\(\s*(--ss-[a-z0-9-]+)", RegexOptions.Compiled);
    private static readonly Regex VarDefinition = new(@"(--ss-[a-z0-9-]+)\s*:", RegexOptions.Compiled);

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

    private static string BaseCss => File.ReadAllText(Path.Combine(UiDirectory, "wwwroot", "css", "studyscope-base.css"));

    /// <summary>Inhalt eines CSS-Blocks ab dem Selektor bis zur passenden schließenden Klammer.</summary>
    private static string Block(string css, string selector)
    {
        var start = css.IndexOf(selector, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"Block „{selector}“ fehlt in studyscope-base.css");
        var open = css.IndexOf('{', start);
        var depth = 0;
        for (var i = open; i < css.Length; i++)
        {
            if (css[i] == '{') depth++;
            else if (css[i] == '}' && --depth == 0) return css[(open + 1)..i];
        }
        return css[(open + 1)..];
    }

    private static HashSet<string> Definitions(string block) =>
        VarDefinition.Matches(block).Select(m => m.Groups[1].Value).ToHashSet();

    [TestCaseSource(nameof(ThemedFiles))]
    public void ThemedFile_HasNoFixedColors(string file)
    {
        var lines = File.ReadAllLines(Path.Combine(UiDirectory, file));
        var offenders = lines
            .Select((line, i) => (line, number: i + 1))
            .Where(l => !l.line.Contains(FixedColorMarker) && FixedColor.IsMatch(Shadow.Replace(l.line, string.Empty)))
            .Select(l => $"{file}:{l.number}: {l.line.Trim()}")
            .ToList();

        Assert.That(offenders, Is.Empty, "Feste Farben statt var(--ss-…):\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// Ein Razor-Kommentar <c>@* … *@</c> innerhalb eines Tags wird zur Laufzeit als Attributname gesetzt und bricht das
    /// Zeichnen ab (InvalidCharacterError) – der Build meldet das nicht. Vermerke daher immer hinter das Tag schreiben.
    /// </summary>
    [TestCaseSource(nameof(ThemedFiles))]
    public void RazorComment_NotInsideTag(string file)
    {
        var text = File.ReadAllText(Path.Combine(UiDirectory, file));
        var offenders = new List<string>();
        foreach (Match m in Regex.Matches(text, @"@\*"))
        {
            var before = text[..m.Index];
            var lastOpen = before.LastIndexOf('<');
            var lastClose = before.LastIndexOf('>');
            // offenes Tag: letztes '<' nach dem letzten '>' und kein Kommentar/Textknoten dazwischen
            if (lastOpen > lastClose && lastOpen + 1 < text.Length && char.IsLetter(text[lastOpen + 1]))
                offenders.Add($"{file}:{before.Count(c => c == '\n') + 1}");
        }
        Assert.That(offenders, Is.Empty, "Razor-Kommentar innerhalb eines Tags:\n" + string.Join("\n", offenders));
    }

    [Test]
    public void AllTabsAreChecked() =>
        Assert.That(ThemedFiles().ToList(), Has.Member("Components/QuizView.razor").And.Member("Components/MindmapView.razor").And.Member("App.razor"));

    [Test]
    public void LightAndDarkSets_DefineTheSameVariables()
    {
        var css = BaseCss;
        var light = Definitions(Block(css, ":root {"));
        var dark = Definitions(Block(css, ":root[data-theme-effective=\"dark\"] {"));

        Assert.Multiple(() =>
        {
            Assert.That(light.Except(dark), Is.Empty, "Nur im hellen Satz definiert");
            Assert.That(dark.Except(light), Is.Empty, "Nur im dunklen Satz definiert");
        });
    }

    [Test]
    public void UsedVariables_AreDefined()
    {
        var css = BaseCss;
        var defined = Definitions(Block(css, ":root {"));
        defined.Add("--ss-sidebar-width");   // wird zur Laufzeit am Element gesetzt

        var sources = ThemedFiles().Select(f => (name: f, text: File.ReadAllText(Path.Combine(UiDirectory, f))))
            .Append((name: "wwwroot/css/studyscope-base.css", text: css));
        var missing = sources
            .SelectMany(s => VarUse.Matches(s.text).Select(m => $"{s.name}: {m.Groups[1].Value}"))
            .Where(entry => !defined.Contains(entry[(entry.LastIndexOf(' ') + 1)..]))
            .Distinct()
            .ToList();

        Assert.That(missing, Is.Empty, "Nicht definierte Variablen:\n" + string.Join("\n", missing));
    }

    [Test]
    public void DarkSet_AppliesOnlyOnScreen_SoPrintStaysLight()
    {
        var css = BaseCss;
        var screen = Block(css, "@media screen {");
        Assert.That(screen, Does.Contain(":root[data-theme-effective=\"dark\"]"));
        // Außerhalb von @media screen darf es keinen Dunkel-Block geben
        var outside = css.Replace(screen, string.Empty);
        Assert.That(outside, Does.Not.Contain("data-theme-effective=\"dark\""));
    }
}
