using System.Collections.Concurrent;
using System.Globalization;
using Jeffijoe.MessageFormat;

namespace StudyScope.Core.Localization;

/// <summary>
/// Setzt Texte mit Platzhaltern im ICU-MessageFormat zusammen, z. B.
/// <c>{count, plural, one {# Folie} other {# Folien}}</c> oder <c>Profil: {name}</c>.
/// Die Pluralregeln stammen aus CLDR (Bibliothek MessageFormat) und richten sich nach der
/// Oberflächensprache (<see cref="CultureInfo.CurrentUICulture"/>), Zahlen nach <see cref="CultureInfo.CurrentCulture"/>.
/// Texte ohne Platzhalter werden direkt aus der Ressourcenklasse gelesen, nicht über diese Klasse.
/// </summary>
public static class Loc
{
    // Ein Formatierer je Sprache; MessageFormatter speichert die zerlegten Muster zwischen.
    // Die Bibliothek sagt nichts über Thread-Sicherheit, daher wird je Instanz gesperrt.
    private static readonly ConcurrentDictionary<string, MessageFormatter> Formatters = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Setzt die Werte in das Muster ein. Namen ohne Wert bleiben als Fehler sichtbar (Ausnahme der Bibliothek).</summary>
    public static string Format(string pattern, params (string Name, object? Value)[] args)
    {
        var values = new Dictionary<string, object?>(args.Length, StringComparer.Ordinal);
        foreach (var (name, value) in args)
            values[name] = value;
        return Format(pattern, values);
    }

    /// <summary>Wie <see cref="Format(string, ValueTuple{string, object}[])"/>, mit Werten als Wörterbuch.</summary>
    public static string Format(string pattern, IReadOnlyDictionary<string, object?> values)
    {
        if (string.IsNullOrEmpty(pattern))
            return string.Empty;

        var culture = CultureInfo.CurrentUICulture;
        var formatter = Formatters.GetOrAdd(culture.Name, _ => new MessageFormatter(culture: culture));
        lock (formatter)
            return formatter.FormatMessage(pattern, values);
    }
}
