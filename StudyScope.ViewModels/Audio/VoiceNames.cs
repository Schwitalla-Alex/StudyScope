using StudyScope.Core.Localization;

namespace StudyScope.ViewModels.Audio;

/// <summary>
/// Bekannte Stimmennamen je Sprache aus <c>Languages/&lt;sprache&gt;/voices.txt</c> (eine Zeile je Stimme:
/// <c>Name = female|male</c>, Reihenfolge = Vorrang). Dient der automatischen Stimmenwahl (<see cref="Rank"/>) und der
/// Schätzung des Geschlechts (<see cref="GenderOf"/>, Namen aller Sprachen gemeinsam – eine englische Stimme kann auch
/// einen deutschen Text vorlesen). Abgeglichen wird nur nach ganzen Wörtern des Stimmennamens (<see cref="Words"/>):
/// „Jan“ trifft „Microsoft Jan“, aber nicht „Jana“ oder „Janina“. Eine neue Sprache braucht nur ihre Datei.
/// </summary>
public static class VoiceNames
{
    public const string FileName = "voices.txt";

    private static readonly Lazy<IReadOnlyDictionary<string, VoiceGender>> Genders = new(LoadGenders);
    private static readonly Dictionary<string, IReadOnlyList<string>> Preferred = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();
    private static readonly System.Text.RegularExpressions.Regex WordSeparator = new(@"[^\p{L}]+");

    /// <summary>Einträge der Sprache in Dateireihenfolge: Name und Geschlecht (unbekannt, wenn nicht angegeben).</summary>
    public static IReadOnlyList<(string Name, VoiceGender Gender)> Entries(string language) =>
        LanguageData.ReadLines(language, FileName)
            .Select(Parse)
            .Where(e => e.Name.Length > 0)
            .ToList();

    /// <summary>
    /// Zerlegt einen Stimmennamen in ganze Wörter (getrennt an allen Nicht-Buchstaben):
    /// „Microsoft Stefan - German (Germany)“ → Microsoft, Stefan, German, Germany.
    /// </summary>
    public static IEnumerable<string> Words(string? voiceName) =>
        WordSeparator.Split(voiceName ?? string.Empty).Where(w => w.Length > 0);

    /// <summary>Geschlecht eines ganzen Worts aus dem Stimmennamen („Katja“ → weiblich) über die Listen aller Sprachen.</summary>
    public static VoiceGender GenderOf(string word) =>
        Genders.Value.TryGetValue(word, out var gender) ? gender : VoiceGender.Unknown;

    /// <summary>
    /// Vorrang einer Stimme in der Sprache: beste Position eines ihrer ganzen Wörter (<see cref="Words"/>) in der Liste
    /// der Sprache (kleiner = besser),
    /// <see cref="int.MaxValue"/>, wenn die Stimme nicht aufgeführt ist.
    /// </summary>
    public static int Rank(IEnumerable<string> nameWords, string? language)
    {
        if (SpeechLanguage.Code(language) is not { } code)
            return int.MaxValue;
        var preferred = PreferredNames(code);
        var best = int.MaxValue;
        foreach (var word in nameWords)
        {
            for (var i = 0; i < preferred.Count && i < best; i++)
            {
                if (string.Equals(preferred[i], word, StringComparison.OrdinalIgnoreCase))
                    best = i;
            }
        }
        return best;
    }

    private static IReadOnlyList<string> PreferredNames(string code)
    {
        lock (Gate)
        {
            if (!Preferred.TryGetValue(code, out var names))
                Preferred[code] = names = Entries(code).Select(e => e.Name).ToList();
            return names;
        }
    }

    private static (string Name, VoiceGender Gender) Parse(string line)
    {
        var parts = line.Split('=', 2);
        var name = parts[0].Trim();
        var gender = parts.Length < 2
            ? VoiceGender.Unknown
            : parts[1].Trim().ToLowerInvariant() switch
            {
                "female" => VoiceGender.Female,
                "male" => VoiceGender.Male,
                _ => VoiceGender.Unknown
            };
        return (name, gender);
    }

    private static IReadOnlyDictionary<string, VoiceGender> LoadGenders()
    {
        var map = new Dictionary<string, VoiceGender>(StringComparer.OrdinalIgnoreCase);
        foreach (var language in LanguageData.Languages(FileName))
        {
            foreach (var (name, gender) in Entries(language))
            {
                if (gender != VoiceGender.Unknown)
                    map.TryAdd(name, gender);
            }
        }
        return map;
    }
}
