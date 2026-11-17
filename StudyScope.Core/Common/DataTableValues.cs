using System.Globalization;
using System.Text.RegularExpressions;

namespace StudyScope.Core.Common;

/// <summary>
/// Erkennt Zahlen und Datumsangaben in Tabellenzellen (deutsche und englische Schreibweise),
/// gemeinsam für die Sortierung der Ansicht, den Excel-Export und die HTML-Berichte.
/// </summary>
public static partial class DataTableValues
{
    private static readonly string[] Months =
    {
        "januar", "februar", "märz", "april", "mai", "juni", "juli", "august", "september", "oktober", "november", "dezember"
    };

    private static readonly Dictionary<string, int> MonthLookup = BuildMonthLookup();

    private static Dictionary<string, int> BuildMonthLookup()
    {
        var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < Months.Length; i++)
        {
            lookup[Months[i]] = i + 1;
            lookup[Months[i][..3]] = i + 1;
        }

        lookup["jänner"] = 1;
        lookup["maerz"] = 3;
        lookup["mrz"] = 3;
        lookup["sept"] = 9;
        foreach (var (name, month) in new[] { ("january", 1), ("february", 2), ("march", 3), ("may", 5), ("june", 6), ("july", 7), ("october", 10), ("december", 12), ("mar", 3), ("oct", 10), ("dec", 12) })
            lookup[name] = month;
        return lookup;
    }

    // ---------------- Zahlen ----------------

    /// <summary>
    /// Reine Zahl ohne Einheit, die sich eindeutig lesen lässt (für den Excel-Export): „42“, „-3,5“, „0.25“, „1.234,5“.
    /// Mehrdeutiges wie „1.234“ (Tausender oder Dezimal?) und Werte mit führender Null („007“) bleiben Text.
    /// </summary>
    public static bool TryParseStrictNumber(string? text, out double value)
    {
        value = 0;
        var t = (text ?? string.Empty).Trim().Replace('−', '-');
        if (t.Length == 0 || t.Length > 20)
            return false;

        if (StrictPlainNumber().IsMatch(t))
        {
            var digits = t.TrimStart('-', '+');
            if (digits.Length > 1 && digits[0] == '0' && digits[1] != '.' && digits[1] != ',')
                return false;

            // „1.234“ wäre im Deutschen 1234, im Englischen 1,234 → nicht als Zahl ausgeben
            if (AmbiguousThousands().IsMatch(t))
                return false;

            return double.TryParse(t.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        if (GermanGroupedNumber().IsMatch(t))
            return double.TryParse(t.Replace(".", string.Empty).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        return false;
    }

    /// <summary>
    /// Zahl für die Sortierung, auch mit Währung, Einheit oder Prozentzeichen („3,5 %“, „12 kg“, „1.200 €“, „5 Mio.“).
    /// </summary>
    public static bool TryParseSortNumber(string? text, out double value)
    {
        value = 0;
        var t = (text ?? string.Empty).Trim().Replace('−', '-').Replace(' ', ' ');
        if (t.Length == 0)
            return false;

        var match = LenientNumber().Match(t);
        if (!match.Success)
            return false;

        var rest = match.Groups["rest"].Value.Trim();
        if (rest.Length > 15 || rest.Any(char.IsDigit))
            return false;

        return TryParseNumberPart(match.Groups["num"].Value, out value);
    }

    private static bool TryParseNumberPart(string part, out double value)
    {
        value = 0;
        var s = part.Replace(" ", string.Empty).Replace("'", string.Empty).Replace("’", string.Empty);
        var negative = s.StartsWith('-');
        s = s.TrimStart('-', '+');
        if (s.Length == 0 || !char.IsDigit(s[0]))
            return false;

        var lastDot = s.LastIndexOf('.');
        var lastComma = s.LastIndexOf(',');
        string normalized;
        if (lastDot >= 0 && lastComma >= 0)
        {
            // Das zuletzt stehende Zeichen ist das Dezimaltrennzeichen
            normalized = lastComma > lastDot
                ? s.Replace(".", string.Empty).Replace(',', '.')
                : s.Replace(",", string.Empty);
        }
        else if (lastComma >= 0)
        {
            normalized = s.Count(c => c == ',') > 1 ? s.Replace(",", string.Empty) : s.Replace(',', '.');
        }
        else if (lastDot >= 0)
        {
            normalized = s.Count(c => c == '.') > 1 || DotThousands().IsMatch(s) ? s.Replace(".", string.Empty) : s;
        }
        else
        {
            normalized = s;
        }

        normalized = normalized.TrimEnd('.');
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return false;

        if (negative) value = -value;
        return true;
    }

    // ---------------- Datumsangaben ----------------

    /// <summary>
    /// Sortierschlüssel einer Datumsangabe (Jahr mit Bruchteil für Monat/Tag). Erkennt „14.07.1789“, „1789-07-14“,
    /// „Juli 1789“, „14. Juli 1789“, „07/1789“, „1789“, „um 1800“, „1914–1918“, „500 v. Chr.“ und „19. Jh.“.
    /// </summary>
    public static bool TryParseDateKey(string? text, out double key)
    {
        key = 0;
        var t = (text ?? string.Empty).Trim().Replace(' ', ' ').ToLowerInvariant();
        if (t.Length == 0 || t.Length > 60)
            return false;

        var beforeChrist = BeforeChrist().IsMatch(t);
        t = BeforeChrist().Replace(t, " ");
        t = AfterChrist().Replace(t, " ");
        t = DatePrefix().Replace(t, string.Empty).Trim();

        double? year = null;
        double fraction = 0;
        Match m;

        if ((m = NumericDate().Match(t)).Success)
        {
            if (!TryDate(m.Groups["y"].Value, m.Groups["m"].Value, m.Groups["d"].Value, out year, out fraction)) return false;
        }
        else if ((m = IsoDate().Match(t)).Success)
        {
            if (!TryDate(m.Groups["y"].Value, m.Groups["m"].Value, m.Groups["d"].Value, out year, out fraction)) return false;
        }
        else if ((m = MonthNameDate().Match(t)).Success && MonthLookup.TryGetValue(m.Groups["mon"].Value.TrimEnd('.'), out var month))
        {
            var day = m.Groups["d"].Success ? m.Groups["d"].Value : "1";
            if (!TryDate(m.Groups["y"].Value, month.ToString(CultureInfo.InvariantCulture), day, out year, out fraction)) return false;
        }
        else if ((m = MonthYear().Match(t)).Success)
        {
            if (!TryDate(m.Groups["y"].Value, m.Groups["m"].Value, "1", out year, out fraction)) return false;
        }
        else if ((m = Century().Match(t)).Success)
        {
            var century = int.Parse(m.Groups["c"].Value, CultureInfo.InvariantCulture);
            if (century is < 1 or > 30) return false;
            year = (century - 1) * 100;
        }
        else if ((m = YearOnly().Match(t)).Success)
        {
            year = int.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture);
        }
        else
        {
            return false;
        }

        if (year == null)
            return false;

        key = beforeChrist ? -year.Value + fraction : year.Value + fraction;
        return true;
    }

    private static bool TryDate(string y, string m, string d, out double? year, out double fraction)
    {
        year = null;
        fraction = 0;
        if (!int.TryParse(y, NumberStyles.None, CultureInfo.InvariantCulture, out var yy) ||
            !int.TryParse(m, NumberStyles.None, CultureInfo.InvariantCulture, out var mm) ||
            !int.TryParse(d, NumberStyles.None, CultureInfo.InvariantCulture, out var dd))
            return false;

        if (mm is < 1 or > 12 || dd is < 1 or > 31)
            return false;

        year = yy;
        fraction = ((mm - 1) * 31 + (dd - 1)) / 400.0;
        return true;
    }

    [GeneratedRegex(@"^[-+]?\d+(?:[.,]\d+)?$")]
    private static partial Regex StrictPlainNumber();

    [GeneratedRegex(@"^[-+]?\d{1,3}\.\d{3}$")]
    private static partial Regex AmbiguousThousands();

    [GeneratedRegex(@"^[-+]?\d{1,3}(?:\.\d{3})+,\d+$")]
    private static partial Regex GermanGroupedNumber();

    [GeneratedRegex(@"^\d{1,3}(?:\.\d{3})+$")]
    private static partial Regex DotThousands();

    [GeneratedRegex(@"^(?:[€$£]\s*)?(?<num>[-+]?\d[\d.,'’ ]*)(?<rest>.*)$")]
    private static partial Regex LenientNumber();

    [GeneratedRegex(@"v\.\s*chr\.?|vor christus|v\.\s*u\.\s*z\.?|\bbc\b|\bbce\b")]
    private static partial Regex BeforeChrist();

    [GeneratedRegex(@"n\.\s*chr\.?|nach christus|\bad\b|\bce\b")]
    private static partial Regex AfterChrist();

    [GeneratedRegex(@"^(?:(?:um|ca\.?|circa|etwa|ab|seit|bis|vor|nach|im|in|jahr|anfang|mitte|ende|frühjahr|sommer|herbst|winter|spätestens|frühestens)\s+)+")]
    private static partial Regex DatePrefix();

    [GeneratedRegex(@"^(?<d>\d{1,2})\.\s*(?<m>\d{1,2})\.\s*(?<y>\d{3,4})\b")]
    private static partial Regex NumericDate();

    [GeneratedRegex(@"^(?<y>\d{4})-(?<m>\d{1,2})-(?<d>\d{1,2})\b")]
    private static partial Regex IsoDate();

    [GeneratedRegex(@"^(?:(?<d>\d{1,2})\.\s*)?(?<mon>[a-zäöü]{3,9}\.?)\s+(?<y>\d{1,4})\b")]
    private static partial Regex MonthNameDate();

    [GeneratedRegex(@"^(?<m>\d{1,2})[./](?<y>\d{4})\b")]
    private static partial Regex MonthYear();

    [GeneratedRegex(@"^(?<c>\d{1,2})\.\s*(?:jh\.?|jhd\.?|jahrhundert)")]
    private static partial Regex Century();

    // Jahreszahl allein, optional mit Zeitraum dahinter („1914–1918“, „1914 bis 1918“)
    [GeneratedRegex(@"^(?<y>\d{1,4})\s*(?:(?:[-–—]|bis)\s*\d{1,4}\s*)?$")]
    private static partial Regex YearOnly();
}
