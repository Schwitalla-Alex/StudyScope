using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Jeffijoe.MessageFormat;

namespace StudyScope.Tests.Localization;

/// <summary>
/// Übersetzungen vollständig und passend halten. Gilt automatisch für jeden Ressourcensatz (<c>*.resx</c>) und jede
/// Sprache (<c>*.&lt;kultur&gt;.resx</c>), auch für künftige: gleiche Schlüssel wie die neutrale (deutsche) Datei,
/// keine leeren Texte, gleiche Platzhalter, gültiges ICU-Format mit „other“ in jeder Mehrzahl-Auswahl. Außerdem wird
/// der Code geprüft: <c>Loc.Format</c>/<c>LocHtml.Format</c> übergeben genau die Platzhalter des Textes, und ein Text
/// mit Platzhaltern wird nie ohne <c>Format</c> ausgegeben (sonst stünde „{count}“ auf dem Bildschirm).
/// </summary>
[TestFixture]
public class ResourceTests
{
    /// <summary>Projekte, deren Code auf Ressourcen-Aufrufe geprüft wird.</summary>
    private static readonly string[] CodeProjects = { "StudyScope.UI", "StudyScope.ViewModels", "StudyScope.Core", "StudyScope.Infrastructure", "StudyScope.Setup", "StudyScope.Setup.Core" };

    private static readonly Regex ResourceRef = new(@"\b(\w+Strings)\.(\w+)\b", RegexOptions.Compiled);
    private static readonly Regex FormatCall = new(@"\b(?:Loc|LocHtml)\.Format\(", RegexOptions.Compiled);
    private static readonly Regex TupleName = new(@"^\(\s*""(\w+)""\s*,", RegexOptions.Compiled);
    private static readonly double[] SampleNumbers = { 0, 1, 2, 3, 4, 5, 6, 11, 12, 21, 22, 100, 101, 1000000, 1.5 };

    private sealed record ResourceSet(string Name, string NeutralPath, Dictionary<string, string> CulturePaths);

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

    private static bool IsBuildOutput(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(p => p is "bin" or "obj");

    private static List<ResourceSet> Sets()
    {
        var files = Directory.EnumerateFiles(RepoRoot, "*.resx", SearchOption.AllDirectories)
            .Where(f => !IsBuildOutput(Path.GetRelativePath(RepoRoot, f)))
            .ToList();
        var sets = new List<ResourceSet>();
        foreach (var neutral in files)
        {
            var name = Path.GetFileNameWithoutExtension(neutral);
            if (CultureOf(name) != null) continue;
            var cultures = files
                .Where(f => Path.GetDirectoryName(f) == Path.GetDirectoryName(neutral))
                .Select(f => (File: f, Stem: Path.GetFileNameWithoutExtension(f)))
                .Where(x => x.Stem.StartsWith(name + ".", StringComparison.Ordinal) && CultureOf(x.Stem) is not null)
                .ToDictionary(x => CultureOf(x.Stem)!, x => x.File);
            sets.Add(new ResourceSet(name, neutral, cultures));
        }
        return sets;
    }

    /// <summary>„UiStrings.en“ → „en“, „UiStrings“ → null.</summary>
    private static string? CultureOf(string stem)
    {
        var dot = stem.LastIndexOf('.');
        if (dot < 0) return null;
        try
        {
            var culture = CultureInfo.GetCultureInfo(stem[(dot + 1)..], predefinedOnly: true);
            return string.IsNullOrEmpty(culture.Name) ? null : culture.Name;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    private static Dictionary<string, string> Read(string path) =>
        XDocument.Load(path).Root!.Elements("data")
            .Where(d => d.Attribute("type") == null)
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? string.Empty);

    [Test]
    public void ResourceSets_AreFound()
    {
        var names = Sets().Select(s => s.Name).ToList();
        Assert.That(names, Does.Contain("UiStrings").And.Contain("VmStrings").And.Contain("CoreStrings").And.Contain("InfraStrings")
            .And.Contain("SetupCoreStrings").And.Contain("SetupStrings"));
    }

    [Test]
    public void EveryLanguage_ExistsForEveryResourceSet()
    {
        var sets = Sets();
        var cultures = sets.SelectMany(s => s.CulturePaths.Keys).ToHashSet();
        foreach (var set in sets)
            foreach (var culture in cultures)
                Assert.That(set.CulturePaths.ContainsKey(culture), Is.True, $"{set.Name}.{culture}.resx fehlt");
    }

    [Test]
    public void Translations_HaveExactlyTheNeutralKeys_AndNoEmptyTexts()
    {
        var problems = new List<string>();
        foreach (var set in Sets())
        {
            var neutral = Read(set.NeutralPath);
            problems.AddRange(neutral.Where(e => string.IsNullOrWhiteSpace(e.Value)).Select(e => $"{set.Name}: „{e.Key}“ ist leer"));
            foreach (var (culture, path) in set.CulturePaths)
            {
                var translated = Read(path);
                problems.AddRange(neutral.Keys.Except(translated.Keys).Select(k => $"{set.Name}.{culture}: „{k}“ fehlt"));
                problems.AddRange(translated.Keys.Except(neutral.Keys).Select(k => $"{set.Name}.{culture}: „{k}“ ist überzählig"));
                problems.AddRange(translated.Where(e => string.IsNullOrWhiteSpace(e.Value)).Select(e => $"{set.Name}.{culture}: „{e.Key}“ ist leer"));
            }
        }
        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }

    [Test]
    public void Translations_UseTheSamePlaceholders_AndValidIcuFormat()
    {
        var problems = new List<string>();
        foreach (var set in Sets())
        {
            var neutral = Read(set.NeutralPath);
            var files = new List<(string Culture, Dictionary<string, string> Texts)> { (UiLanguageNeutral, neutral) };
            files.AddRange(set.CulturePaths.Select(c => (c.Key, Read(c.Value))));

            foreach (var (culture, texts) in files)
            {
                foreach (var (key, value) in texts)
                {
                    var label = $"{set.Name}.{culture}: „{key}“";
                    IcuPattern pattern;
                    try { pattern = IcuPattern.Parse(value); }
                    catch (FormatException ex) { problems.Add($"{label}: {ex.Message}"); continue; }

                    if (neutral.TryGetValue(key, out var original) && !ReferenceEquals(texts, neutral))
                    {
                        var expected = IcuPattern.Parse(original).Arguments;
                        if (!pattern.Arguments.SetEquals(expected))
                            problems.Add($"{label}: Platzhalter {Names(pattern.Arguments)} statt {Names(expected)}");
                    }

                    foreach (var (name, type, selectors) in pattern.Choices)
                        if (!selectors.Contains("other"))
                            problems.Add($"{label}: {type}-Auswahl „{name}“ ohne „other“");

                    if (pattern.Arguments.Count > 0)
                        problems.AddRange(TryFormat(label, value, pattern, culture));
                }
            }
        }
        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }

    private const string UiLanguageNeutral = "de";

    /// <summary>Mit Beispielzahlen in der jeweiligen Sprache formatieren: findet Fehler, die der Leser nicht sieht.</summary>
    private static IEnumerable<string> TryFormat(string label, string value, IcuPattern pattern, string culture)
    {
        var formatter = new MessageFormatter(culture: CultureInfo.GetCultureInfo(culture));
        var selectArgs = pattern.Choices.Where(c => c.Type == "select").Select(c => c.Name).ToHashSet();
        foreach (var number in SampleNumbers)
        {
            var args = pattern.Arguments.ToDictionary(a => a, a => selectArgs.Contains(a) ? (object?)"other" : number);
            string? error = null;
            try { formatter.FormatMessage(value, args); }
            catch (Exception ex) { error = $"{label}: Formatieren mit {number} schlägt fehl ({ex.Message})"; }
            if (error != null)
            {
                yield return error;
                yield break;
            }
        }
    }

    private static string Names(IEnumerable<string> names) => "{" + string.Join(", ", names.OrderBy(n => n)) + "}";

    [Test]
    public void Code_PassesExactlyThePlaceholders_AndNeverShowsAPatternUnformatted()
    {
        var placeholders = Sets()
            .ToDictionary(s => s.Name, s => Read(s.NeutralPath).ToDictionary(e => e.Key, e => IcuPattern.Parse(e.Value).Arguments));
        var problems = new List<string>();

        foreach (var file in CodeFiles())
        {
            var code = File.ReadAllText(file);
            var rel = Path.GetRelativePath(RepoRoot, file);
            var formatted = new List<(int Start, int End)>();

            foreach (Match call in FormatCall.Matches(code))
            {
                var open = call.Index + call.Length - 1;
                var args = SplitArguments(code, open, out var close);
                if (args.Count == 0) continue;
                formatted.Add((open, open + 1 + args[0].Length));
                var passed = args.Skip(1).Select(a => TupleName.Match(a.Trim())).Where(m => m.Success).Select(m => m.Groups[1].Value).ToHashSet();
                foreach (Match r in ResourceRef.Matches(args[0]))
                {
                    if (!placeholders.TryGetValue(r.Groups[1].Value, out var keys)) continue;
                    if (!keys.TryGetValue(r.Groups[2].Value, out var expected))
                    {
                        problems.Add($"{rel}: {r.Value} gibt es nicht");
                        continue;
                    }
                    if (!expected.SetEquals(passed))
                        problems.Add($"{rel}: {r.Value} erwartet {Names(expected)}, übergeben {Names(passed)}");
                }
            }

            foreach (Match r in ResourceRef.Matches(code))
            {
                if (!placeholders.TryGetValue(r.Groups[1].Value, out var keys)) continue;
                if (!keys.TryGetValue(r.Groups[2].Value, out var expected) || expected.Count == 0) continue;
                if (!formatted.Any(f => r.Index > f.Start && r.Index < f.End))
                    problems.Add($"{rel}: {r.Value} hat Platzhalter {Names(expected)}, wird aber ohne Loc.Format ausgegeben");
            }
        }
        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }

    private static IEnumerable<string> CodeFiles() =>
        CodeProjects.SelectMany(p => Directory.EnumerateFiles(Path.Combine(RepoRoot, p), "*.*", SearchOption.AllDirectories))
            .Where(f => (f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal))
                        && !IsBuildOutput(Path.GetRelativePath(RepoRoot, f)));

    /// <summary>Argumente eines Aufrufs ab der öffnenden Klammer, auf oberster Ebene an Kommas getrennt (Zeichenketten werden übersprungen).</summary>
    private static List<string> SplitArguments(string code, int open, out int close)
    {
        var args = new List<string>();
        var depth = 0;
        var start = open + 1;
        for (var i = open; i < code.Length; i++)
        {
            var c = code[i];
            if (c == '"')
            {
                var verbatim = i > 0 && (code[i - 1] == '@' || (code[i - 1] == '$' && i > 1 && code[i - 2] == '@'));
                i++;
                while (i < code.Length)
                {
                    if (!verbatim && code[i] == '\\') { i += 2; continue; }
                    if (code[i] == '"')
                    {
                        if (verbatim && i + 1 < code.Length && code[i + 1] == '"') { i += 2; continue; }
                        break;
                    }
                    i++;
                }
                continue;
            }
            if (c == '\'' && i + 2 < code.Length && (code[i + 2] == '\'' || code[i + 1] == '\\'))
            {
                i = code.IndexOf('\'', i + 2);
                continue;
            }
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}')
            {
                depth--;
                if (depth == 0)
                {
                    args.Add(code[start..i]);
                    close = i;
                    return args;
                }
            }
            else if (c == ',' && depth == 1)
            {
                args.Add(code[start..i]);
                start = i + 1;
            }
        }
        close = code.Length;
        return args;
    }
}
