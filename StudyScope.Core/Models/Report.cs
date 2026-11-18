using System.Text.Json.Serialization;

namespace StudyScope.Core.Models;

/// <summary>Vorlage eines Berichts – bestimmt Aufbau und Schwerpunkt.</summary>
public enum ReportTemplate
{
    /// <summary>Lernübersicht: wichtige Konzepte, Begriffe, Merksätze und Wiederholungsfragen.</summary>
    StudyGuide,

    /// <summary>Zusammenfassung / Briefing: Kernaussagen kompakt.</summary>
    Briefing,

    /// <summary>Glossar: Fachbegriffe mit Erklärung, nach Themen gruppiert.</summary>
    Glossary,

    /// <summary>FAQ: häufige Fragen mit Antworten.</summary>
    Faq,

    /// <summary>Eigene Vorgabe des Benutzers.</summary>
    Custom
}

/// <summary>Fachbegriff mit Erklärung.</summary>
public class ReportTerm
{
    public string Term { get; set; } = string.Empty;
    public string Definition { get; set; } = string.Empty;
}

/// <summary>Frage mit Antwort (FAQ, Wiederholungsfragen).</summary>
public class ReportQuestion
{
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
}

/// <summary>Abschnitt eines Berichts. Alle Teile sind optional – je nach Vorlage werden andere genutzt.</summary>
public class ReportSection
{
    public string Heading { get; set; } = string.Empty;
    public List<string> Paragraphs { get; set; } = new();
    public List<string> Bullets { get; set; } = new();
    public List<ReportTerm> Terms { get; set; } = new();
    public List<ReportQuestion> Questions { get; set; } = new();

    /// <summary>Merksatz / Kernaussage des Abschnitts.</summary>
    public string Takeaway { get; set; } = string.Empty;
}

/// <summary>Welche Studio-Inhalte im interaktiven Bericht (und in den Exporten) eingebettet werden.</summary>
public class ReportEmbeds
{
    public bool Mindmap { get; set; } = true;
    public bool Flashcards { get; set; } = true;
    public bool Quiz { get; set; } = true;
    public bool Presentation { get; set; } = true;

    /// <summary>
    /// Datentabellen einbetten. Bewusst ohne Standardwert: Fehlt die Eigenschaft beim Einlesen (Bericht aus einer
    /// Version vor den Datentabellen), bleibt sie null und gilt als „aus“ – ältere Berichte sehen unverändert aus.
    /// Neue Berichte setzen sie ausdrücklich (Standard: an).
    /// </summary>
    public bool? DataTables { get; set; }

    /// <summary>Ob Datentabellen eingebettet werden (null = älterer Bericht = nein).</summary>
    [JsonIgnore]
    public bool IncludesDataTables => DataTables == true;

    /// <summary>
    /// Sprechskripte der Audio-Beiträge einbetten (nur als Text; die Audiodatei selbst wird nicht eingebettet, nur ihr
    /// Name genannt). Wie <see cref="DataTables"/> ohne Standardwert: ältere Berichte bleiben unverändert.
    /// </summary>
    public bool? AudioScripts { get; set; }

    /// <summary>Ob Audio-Skripte eingebettet werden (null = älterer Bericht = nein).</summary>
    [JsonIgnore]
    public bool IncludesAudioScripts => AudioScripts == true;

    /// <summary>
    /// Sprechtexte der Videos einbetten (nur als Text je Folie; Folien und Audio werden nicht eingebettet, nur
    /// „Video: Titel“ genannt). Wie <see cref="DataTables"/> ohne Standardwert: ältere Berichte bleiben unverändert.
    /// </summary>
    public bool? VideoScripts { get; set; }

    /// <summary>Ob Video-Skripte eingebettet werden (null = älterer Bericht = nein).</summary>
    [JsonIgnore]
    public bool IncludesVideoScripts => VideoScripts == true;
}

/// <summary>Ein gespeicherter Bericht.</summary>
public class Report
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;

    /// <summary>Einleitung bzw. Kurzfassung.</summary>
    public string Summary { get; set; } = string.Empty;

    public ReportTemplate Template { get; set; } = ReportTemplate.StudyGuide;

    /// <summary>Nur bei <see cref="ReportTemplate.Custom"/>: Vorgabe des Benutzers.</summary>
    public string CustomInstruction { get; set; } = string.Empty;

    public List<ReportSection> Sections { get; set; } = new();
    public ReportEmbeds Embeds { get; set; } = new();
    public List<string> SourceDocuments { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>Alle Berichte eines Profils.</summary>
public class ReportLibrary
{
    public List<Report> Reports { get; set; } = new();
}

/// <summary>Ergebnis einer Berichtserstellung durch die KI (Misserfolge wie bei der Mindmap eingeordnet).</summary>
public class ReportGenerationResult
{
    public Report? Report { get; init; }
    public string RawResponse { get; init; } = string.Empty;
    public bool WasRepaired { get; init; }
    public MindmapFailureReason FailureReason { get; init; }
    public bool IsSuccess => Report != null;
}

/// <summary>
/// Momentaufnahme der Studio-Inhalte eines Profils, die in einen Bericht eingebettet werden
/// (Mindmap, Karteikarten, Quizfragen, Präsentation). Leere Teile werden ausgelassen.
/// </summary>
public class ReportStudioContent
{
    public MindmapDocument? Mindmap { get; init; }
    public IReadOnlyList<Flashcard> Flashcards { get; init; } = Array.Empty<Flashcard>();
    public IReadOnlyList<QuizQuestion> QuizQuestions { get; init; } = Array.Empty<QuizQuestion>();
    public PresentationDocument? Presentation { get; init; }
    public IReadOnlyList<ReportDataTable> DataTables { get; init; } = Array.Empty<ReportDataTable>();

    /// <summary>Sprechskripte der Audio-Beiträge (Text; Audiodatei nur als Name).</summary>
    public IReadOnlyList<AudioSummary> AudioScripts { get; init; } = Array.Empty<AudioSummary>();

    /// <summary>Sprechtexte der Videos (Text je Folie; Folien und Audio nur als Hinweis „Video: Titel“).</summary>
    public IReadOnlyList<VideoDocument> VideoScripts { get; init; } = Array.Empty<VideoDocument>();

    public static ReportStudioContent Empty { get; } = new();

    /// <summary>Nur die Teile, die laut <paramref name="embeds"/> gewünscht und vorhanden sind.</summary>
    public ReportStudioContent Filter(ReportEmbeds embeds) => new()
    {
        Mindmap = embeds.Mindmap ? Mindmap : null,
        Flashcards = embeds.Flashcards ? Flashcards : Array.Empty<Flashcard>(),
        QuizQuestions = embeds.Quiz ? QuizQuestions : Array.Empty<QuizQuestion>(),
        Presentation = embeds.Presentation ? Presentation : null,
        DataTables = embeds.IncludesDataTables ? DataTables : Array.Empty<ReportDataTable>(),
        AudioScripts = embeds.IncludesAudioScripts ? AudioScripts : Array.Empty<AudioSummary>(),
        VideoScripts = embeds.IncludesVideoScripts ? VideoScripts : Array.Empty<VideoDocument>()
    };

    public bool HasAny => Mindmap != null || Flashcards.Count > 0 || QuizQuestions.Count > 0 || Presentation is { Slides.Count: > 0 }
                          || DataTables.Any(t => t.Rows.Count > 0) || AudioScripts.Any(a => a.Segments.Count > 0)
                          || VideoScripts.Any(ReportVideoScripts.HasNarration);
}

/// <summary>
/// Eingebettete Datentabelle: die Tabelle, die übernommenen Zeilen (ggf. gekürzt) und die Gesamtzahl ihrer Zeilen.
/// </summary>
public sealed record ReportDataTable(DataTableDocument Table, IReadOnlyList<DataTableRow> Rows)
{
    public int TotalRows => Table.Rows.Count;
    public bool IsTruncated => Rows.Count < TotalRows;
}

/// <summary>Hilfen für eingebettete Video-Skripte.</summary>
public static class ReportVideoScripts
{
    /// <summary>Das Video hat mindestens eine Szene mit Sprechtext (sonst wird es nicht eingebettet).</summary>
    public static bool HasNarration(VideoDocument video) => video.Scenes.Any(s => s.HasNarration);

    /// <summary>Überschrift einer Szene: „Folie 3: Titel“ bzw. „Folie 3“, wenn die Folie keinen Titel hat.</summary>
    public static string SceneHeading(VideoDocument video, int index)
    {
        var title = video.SlideAt(index)?.Title?.Trim() ?? string.Empty;
        return title.Length > 0 ? $"Folie {index + 1}: {title}" : $"Folie {index + 1}";
    }
}

/// <summary>Anzeigenamen und Beschreibungen der Berichtsvorlagen (gemeinsam für Oberfläche und Exporte).</summary>
public static class ReportTemplates
{
    public static string DisplayName(ReportTemplate template) => template switch
    {
        ReportTemplate.StudyGuide => "Lernübersicht",
        ReportTemplate.Briefing => "Zusammenfassung / Briefing",
        ReportTemplate.Glossary => "Glossar",
        ReportTemplate.Faq => "FAQ",
        _ => "Eigene Vorgabe"
    };

    public static string Description(ReportTemplate template) => template switch
    {
        ReportTemplate.StudyGuide => "Fasst wichtige Konzepte mit Begriffen, Merksätzen und Wiederholungsfragen zusammen – ideal zum Lernen.",
        ReportTemplate.Briefing => "Die Kernaussagen kompakt für einen schnellen Überblick, mit Fazit.",
        ReportTemplate.Glossary => "Die wichtigen Fachbegriffe mit verständlicher Erklärung, nach Themen gruppiert.",
        ReportTemplate.Faq => "Häufige Fragen zum Stoff mit ausführlichen Antworten.",
        _ => "Sie beschreiben selbst, was der Bericht enthalten soll."
    };

    public static string Icon(ReportTemplate template) => template switch
    {
        ReportTemplate.StudyGuide => "🎓",
        ReportTemplate.Briefing => "📋",
        ReportTemplate.Glossary => "📖",
        ReportTemplate.Faq => "❔",
        _ => "✍️"
    };

    public static IReadOnlyList<ReportTemplate> All { get; } =
        new[] { ReportTemplate.StudyGuide, ReportTemplate.Briefing, ReportTemplate.Glossary, ReportTemplate.Faq, ReportTemplate.Custom };
}
