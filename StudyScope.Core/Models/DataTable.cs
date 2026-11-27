using System.Globalization;
using StudyScope.Core.Localization;
using StudyScope.Core.Resources;

namespace StudyScope.Core.Models;

/// <summary>Vorlage einer Datentabelle – bestimmt, welche Angaben die KI heraussucht.</summary>
public enum DataTableTemplate
{
    /// <summary>Fachbegriffe mit Definition.</summary>
    Terms,

    /// <summary>Gegenüberstellung von Theorien, Verfahren, Modellen o. Ä.</summary>
    Comparison,

    /// <summary>Zeitleiste: Datum, Ereignis, Bedeutung.</summary>
    Timeline,

    /// <summary>Personen und ihre Beiträge.</summary>
    People,

    /// <summary>Formeln und Kennzahlen.</summary>
    Formulas,

    /// <summary>Eigene Vorgabe des Benutzers (Freitext, optional mit Spalten).</summary>
    Custom
}

/// <summary>Eine Zeile: je Spalte eine Zelle (Text) und das Quelldokument der Angaben.</summary>
public class DataTableRow
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Zellen in der Reihenfolge von <see cref="DataTableDocument.Columns"/>; leere Zellen sind leere Texte.</summary>
    public List<string> Cells { get; set; } = new();

    /// <summary>Dateiname des Dokuments, aus dem die Angaben stammen (leer, wenn unbekannt).</summary>
    public string Source { get; set; } = string.Empty;
}

/// <summary>Eine gespeicherte Datentabelle.</summary>
public class DataTableDocument
{
    /// <summary>
    /// Sprache des Inhalts (zweistelliger Code wie „de“/„en“), in der die KI ihn erzeugt hat; null = unbekannt
    /// (ältere Daten oder ohne KI erstellt). Siehe <c>OutputLanguage</c>.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>Höchstzahl an Zeilen je Tabelle (auch beim Ergänzen und manuellen Hinzufügen).</summary>
    public const int MaxRows = 500;

    /// <summary>Höchstzahl an Spalten je Tabelle (ohne die Quelle).</summary>
    public const int MaxColumns = 20;

    /// <summary>
    /// Anzeigename der festen Quellspalte in Ansicht und Exporten, in der aktuellen Sprache („Quelle“, „Source“).
    /// Gespeichert wird nur das Feld <see cref="DataTableRow.Source"/>, nie dieser Name.
    /// </summary>
    public static string SourceColumnName => CoreStrings.DataTable_SourceColumn;

    /// <summary>
    /// Der Name ist als Spaltenname reserviert: der Name der Quellspalte in irgendeiner verfügbaren Sprache (ohne
    /// Groß-/Kleinschreibung), damit nach einem Sprachwechsel keine zweite Spalte „Quelle“/„Source“ entsteht.
    /// </summary>
    public static bool IsReservedColumnName(string? name)
    {
        var n = name?.Trim();
        if (string.IsNullOrEmpty(n)) return false;
        return SourceColumnNames().Any(s => string.Equals(s, n, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Name der Quellspalte in der neutralen Sprache, der Oberflächensprache und jeder Sprache neben dem Programm.</summary>
    public static IReadOnlyCollection<string> SourceColumnNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(CultureInfo culture)
        {
            var value = CoreStrings.ResourceManager.GetString(nameof(CoreStrings.DataTable_SourceColumn), culture);
            if (!string.IsNullOrWhiteSpace(value)) names.Add(value.Trim());
        }

        Add(CultureInfo.InvariantCulture);
        Add(CultureInfo.CurrentUICulture);
        foreach (var name in UiLanguage.AvailableCultures(AppContext.BaseDirectory, typeof(CoreStrings).Assembly.GetName().Name!))
            Add(CultureInfo.GetCultureInfo(name));
        return names;
    }

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public DataTableTemplate Template { get; set; } = DataTableTemplate.Terms;

    /// <summary>Nur bei <see cref="DataTableTemplate.Custom"/>: Vorgabe des Benutzers.</summary>
    public string CustomInstruction { get; set; } = string.Empty;

    /// <summary>Optionales Thema bzw. Suchbegriff, auf das die Tabelle eingegrenzt wurde.</summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>Spaltennamen (ohne die Quellspalte).</summary>
    public List<string> Columns { get; set; } = new();

    public List<DataTableRow> Rows { get; set; } = new();
    public List<string> SourceDocuments { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>Wie oft schon „Weitere Zeilen ergänzen“ lief (wechselt den Textausschnitt).</summary>
    public int AppendCount { get; set; }
}

/// <summary>Alle Datentabellen eines Profils.</summary>
public class DataTableLibrary
{
    public List<DataTableDocument> Tables { get; set; } = new();
}

/// <summary>Textstelle mit dem Dokument, aus dem sie stammt.</summary>
public sealed record SourceChunk(string SourceDocument, string Text);

/// <summary>Ergebnis einer Tabellenerstellung durch die KI (Misserfolge wie bei der Mindmap eingeordnet).</summary>
public class DataTableGenerationResult
{
    public DataTableDocument? Table { get; init; }
    public string RawResponse { get; init; } = string.Empty;
    public bool WasRepaired { get; init; }
    public MindmapFailureReason FailureReason { get; init; }
    public bool IsSuccess => Table != null;
}

/// <summary>Ergebnis von „Weitere Zeilen ergänzen“.</summary>
public class DataTableRowsResult
{
    /// <summary>Neue Zeilen (passend zu den vorhandenen Spalten, ohne Wiederholungen).</summary>
    public List<DataTableRow> Rows { get; init; } = new();

    /// <summary>Verworfene Zeilen, die es schon gab.</summary>
    public int DuplicatesSkipped { get; init; }

    public string RawResponse { get; init; } = string.Empty;
    public bool WasRepaired { get; init; }
    public MindmapFailureReason FailureReason { get; init; }
}

/// <summary>Anzeigenamen, Beschreibungen und Spaltenvorschläge der Tabellenvorlagen.</summary>
public static class DataTableTemplates
{
    public static string DisplayName(DataTableTemplate template) => template switch
    {
        DataTableTemplate.Terms => "Begriffe & Definitionen",
        DataTableTemplate.Comparison => "Vergleich",
        DataTableTemplate.Timeline => "Zeitleiste",
        DataTableTemplate.People => "Personen & Beiträge",
        DataTableTemplate.Formulas => "Formeln & Kennzahlen",
        _ => "Eigene Vorgabe"
    };

    public static string Description(DataTableTemplate template) => template switch
    {
        DataTableTemplate.Terms => "Fachbegriffe mit kurzer Definition und Beispiel.",
        DataTableTemplate.Comparison => "Theorien, Verfahren oder Modelle mit ihren Merkmalen gegenübergestellt.",
        DataTableTemplate.Timeline => "Ereignisse mit Datum und Bedeutung in zeitlicher Folge.",
        DataTableTemplate.People => "Personen mit ihren Beiträgen, Werken und ihrer Zeit.",
        DataTableTemplate.Formulas => "Formeln, Kennzahlen und Größen mit Bedeutung und Einheit.",
        _ => "Sie beschreiben selbst, was die Tabelle enthalten soll – optional mit Spalten."
    };

    public static string Icon(DataTableTemplate template) => template switch
    {
        DataTableTemplate.Terms => "📖",
        DataTableTemplate.Comparison => "⚖️",
        DataTableTemplate.Timeline => "🕰️",
        DataTableTemplate.People => "👤",
        DataTableTemplate.Formulas => "🧮",
        _ => "✍️"
    };

    /// <summary>Spaltenvorschlag der Vorlage (die KI darf ihn an den Inhalt anpassen).</summary>
    public static IReadOnlyList<string> SuggestedColumns(DataTableTemplate template) => template switch
    {
        DataTableTemplate.Terms => new[] { "Begriff", "Definition", "Beispiel" },
        DataTableTemplate.Comparison => new[] { "Gegenstand", "Kernidee", "Merkmale", "Vorteile", "Nachteile" },
        DataTableTemplate.Timeline => new[] { "Datum", "Ereignis", "Bedeutung" },
        DataTableTemplate.People => new[] { "Person", "Lebensdaten", "Beitrag", "Werk / Bekannt für" },
        DataTableTemplate.Formulas => new[] { "Bezeichnung", "Formel / Wert", "Bedeutung", "Einheit" },
        _ => Array.Empty<string>()
    };

    public static IReadOnlyList<DataTableTemplate> All { get; } = new[]
    {
        DataTableTemplate.Terms, DataTableTemplate.Comparison, DataTableTemplate.Timeline,
        DataTableTemplate.People, DataTableTemplate.Formulas, DataTableTemplate.Custom
    };
}
