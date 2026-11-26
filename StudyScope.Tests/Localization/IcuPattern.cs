namespace StudyScope.Tests.Localization;

/// <summary>
/// Kleiner Leser für ICU-MessageFormat-Muster (nur für Tests): sammelt die Namen der Platzhalter und je
/// Plural-/Select-Argument die Auswahlwerte (one, other, =0 …). Wirft <see cref="FormatException"/> bei
/// unausgeglichenen Klammern.
/// </summary>
public sealed class IcuPattern
{
    public HashSet<string> Arguments { get; } = new(StringComparer.Ordinal);

    /// <summary>Argumentname → Typ (plural, select, selectordinal) und dessen Auswahlwerte.</summary>
    public List<(string Name, string Type, List<string> Selectors)> Choices { get; } = new();

    public static IcuPattern Parse(string pattern)
    {
        var result = new IcuPattern();
        var i = 0;
        result.ParseText(pattern, ref i, nested: false);
        if (i < pattern.Length)
            throw new FormatException($"Unerwartete „}}“ an Position {i}");
        return result;
    }

    private void ParseText(string s, ref int i, bool nested)
    {
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '\'')
            {
                // '' = Apostroph; '{…}' = wörtlicher Text; ein einzelnes ' vor normalem Text bleibt wörtlich
                if (i + 1 < s.Length && s[i + 1] == '\'') { i += 2; continue; }
                if (i + 1 < s.Length && (s[i + 1] is '{' or '}' or '#' or '|'))
                {
                    var end = s.IndexOf('\'', i + 1);
                    i = end < 0 ? s.Length : end + 1;
                    continue;
                }
                i++;
                continue;
            }
            if (c == '{') { i++; ParseArgument(s, ref i); continue; }
            if (c == '}')
            {
                if (nested) return;
                throw new FormatException($"Unerwartete „}}“ an Position {i}");
            }
            i++;
        }
        if (nested) throw new FormatException("Fehlende „}“");
    }

    private void ParseArgument(string s, ref int i)
    {
        var name = ReadToken(s, ref i);
        if (name.Length == 0) throw new FormatException($"Leerer Platzhalter an Position {i}");
        Arguments.Add(name);
        SkipSpace(s, ref i);
        if (i < s.Length && s[i] == '}') { i++; return; }
        if (i >= s.Length || s[i] != ',') throw new FormatException($"Erwartet „,“ oder „}}“ nach „{name}“");
        i++;
        var type = ReadToken(s, ref i).ToLowerInvariant();
        SkipSpace(s, ref i);
        if (type is "plural" or "select" or "selectordinal")
        {
            if (i >= s.Length || s[i] != ',') throw new FormatException($"Erwartet „,“ nach „{type}“");
            i++;
            var selectors = new List<string>();
            while (true)
            {
                SkipSpace(s, ref i);
                if (i >= s.Length) throw new FormatException($"„{name}“ nicht geschlossen");
                if (s[i] == '}') { i++; break; }
                var selector = ReadToken(s, ref i);
                if (selector.StartsWith("offset:", StringComparison.Ordinal)) continue;
                SkipSpace(s, ref i);
                if (i >= s.Length || s[i] != '{') throw new FormatException($"Erwartet „{{“ nach „{selector}“");
                i++;
                ParseText(s, ref i, nested: true);
                i++; // schließende Klammer der Auswahl
                selectors.Add(selector);
            }
            Choices.Add((name, type, selectors));
            return;
        }
        // number, date, time: Stil bis zur schließenden Klammer überspringen
        var depth = 0;
        while (i < s.Length && !(s[i] == '}' && depth == 0))
        {
            if (s[i] == '{') depth++;
            else if (s[i] == '}') depth--;
            i++;
        }
        if (i >= s.Length) throw new FormatException($"„{name}“ nicht geschlossen");
        i++;
    }

    private static string ReadToken(string s, ref int i)
    {
        SkipSpace(s, ref i);
        var start = i;
        while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] is not (',' or '{' or '}')) i++;
        return s[start..i];
    }

    private static void SkipSpace(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
    }
}
