using System.Globalization;
using System.Text;
using System.Text.Json;
using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;

namespace StudyScope.Infrastructure.Services;

/// <summary>
/// Berichte per KI nach Vorlage (Lernübersicht, Zusammenfassung, Glossar, FAQ, eigene Vorgabe),
/// Speicherung aller Berichte eines Profils unter <c>Reports/&lt;Profil&gt;.json</c> und Exporte.
/// </summary>
public class ReportService : IReportService
{
    public const int MaxSections = 15;
    public const int MaxItemsPerList = 20;
    private const int MaxTextLength = 2000;
    private const int MaxShortTextLength = 300;

    private static readonly string[] TitleKeys = { "title", "titel", "name", "thema" };
    private static readonly string[] SummaryKeys = { "summary", "zusammenfassung", "einleitung", "intro", "introduction", "überblick", "ueberblick" };
    private static readonly string[] SectionsKeys = { "sections", "abschnitte", "kapitel", "chapters", "teile" };
    private static readonly string[] HeadingKeys = { "heading", "überschrift", "ueberschrift", "title", "titel", "thema" };
    private static readonly string[] ParagraphKeys = { "paragraphs", "absätze", "absaetze", "text", "content", "inhalt", "erklärung", "erklaerung" };
    private static readonly string[] BulletKeys = { "bullets", "stichpunkte", "punkte", "keypoints", "key_points", "points", "liste" };
    private static readonly string[] TermsKeys = { "terms", "begriffe", "glossar", "glossary", "fachbegriffe" };
    private static readonly string[] TermKeys = { "term", "begriff", "name", "wort" };
    private static readonly string[] DefinitionKeys = { "definition", "erklärung", "erklaerung", "beschreibung", "bedeutung", "description" };
    private static readonly string[] QuestionListKeys = { "faq", "questions", "fragen", "wiederholungsfragen" };
    private static readonly string[] QuestionKeys = { "question", "frage", "q" };
    private static readonly string[] AnswerKeys = { "answer", "antwort", "a" };
    private static readonly string[] TakeawayKeys = { "takeaway", "merksatz", "kernaussage", "fazit", "keytakeaway", "key_takeaway" };

    private readonly ILlmService _llmService;
    private readonly string _baseDirectory;

    /// <param name="baseDirectory">Basisordner für die Speicherung (Tests); Standard ist <see cref="AppPaths.UserDataDirectory"/>.</param>
    public ReportService(ILlmService llmService, string? baseDirectory = null)
    {
        _llmService = llmService;
        _baseDirectory = baseDirectory ?? AppPaths.UserDataDirectory;
    }

    // ---------------- Erzeugung ----------------

    public async Task<ReportGenerationResult> GenerateReportAsync(
        IEnumerable<string> contextChunks,
        ReportTemplate template,
        string customInstruction,
        string fallbackTitle,
        CancellationToken cancellationToken = default)
    {
        var chunks = ChunkSelector.SelectRepresentative(contextChunks, MindmapService.MaxChunks);
        if (chunks.Count == 0)
            return new ReportGenerationResult { FailureReason = MindmapFailureReason.NoContent };

        var prompt = BuildPrompt(template, customInstruction);
        var raw = await _llmService.AskQuestionAsync(prompt, chunks, cancellationToken);
        var parsed = AnalyzeResponse(raw, fallbackTitle);

        if (parsed.Value != null)
        {
            parsed.Value.Template = template;
            parsed.Value.CustomInstruction = template == ReportTemplate.Custom ? customInstruction.Trim() : string.Empty;
        }

        return new ReportGenerationResult
        {
            Report = parsed.Value,
            RawResponse = raw ?? string.Empty,
            WasRepaired = parsed.WasRepaired,
            FailureReason = parsed.FailureReason
        };
    }

    /// <summary>
    /// Prompt je Vorlage. Beginnt mit „Erstelle einen Bericht“ (Erkennung der Fake-KI), enthält „JSON“
    /// (der Dispatcher gibt dann für einige Provider ein JSON-Array vor – daher Array mit genau einem Objekt).
    /// </summary>
    public static string BuildPrompt(ReportTemplate template, string customInstruction)
    {
        var sb = new StringBuilder();
        sb.Append("Erstelle einen Bericht aus den bereitgestellten Textauszügen.\n");
        sb.Append("Art des Berichts: ").Append(TemplateInstruction(template, customInstruction)).Append('\n');
        sb.Append("Antworte AUSSCHLIESSLICH als gültiges JSON-Array mit genau EINEM Objekt, ohne Einleitung, ohne Markdown und ohne Schlusssatz.\n");
        sb.Append("Schema:\n");
        sb.Append("[{\"title\": \"Titel des Berichts\", \"summary\": \"Einleitung in 2-3 Sätzen\", \"sections\": [{");
        sb.Append("\"heading\": \"Überschrift\", \"paragraphs\": [\"Absatz\"], \"bullets\": [\"Stichpunkt\"], ");
        sb.Append("\"terms\": [{\"term\": \"Fachbegriff\", \"definition\": \"Erklärung\"}], ");
        sb.Append("\"faq\": [{\"question\": \"Frage\", \"answer\": \"Antwort\"}], \"takeaway\": \"Merksatz\"}]}]\n");
        sb.Append("Regeln:\n");
        sb.Append("- Felder, die für einen Abschnitt nicht passen, als leere Liste bzw. leeren Text angeben.\n");
        sb.Append("- Sachlich, verständlich, gut gegliedert. Nur Inhalte verwenden, die in den Auszügen vorkommen. Sprache: Deutsch.\n");
        return sb.ToString();
    }

    public static string TemplateInstruction(ReportTemplate template, string customInstruction) => template switch
    {
        ReportTemplate.StudyGuide =>
            "Lernübersicht. 4 bis 7 Abschnitte zu den wichtigsten Konzepten. Je Abschnitt: 1 bis 2 Absätze verständliche Erklärung, " +
            "3 bis 5 Stichpunkte, wichtige Fachbegriffe mit kurzer Definition in \"terms\" und ein Merksatz in \"takeaway\". " +
            "Der letzte Abschnitt heißt „Wiederholungsfragen“ und enthält 4 bis 6 Fragen mit Antworten in \"faq\".",
        ReportTemplate.Briefing =>
            "Zusammenfassung / Briefing. Kernaussagen kompakt für einen schnellen Überblick: 3 bis 6 Abschnitte mit kurzen Absätzen " +
            "und Stichpunkten, am Ende ein Abschnitt „Fazit“ mit den wichtigsten Schlussfolgerungen.",
        ReportTemplate.Glossary =>
            "Glossar. Die wichtigen Fachbegriffe mit verständlicher Erklärung in \"terms\", nach Themen in Abschnitte gruppiert, " +
            "innerhalb eines Abschnitts alphabetisch sortiert. Keine Absätze nötig.",
        ReportTemplate.Faq =>
            "FAQ. Häufige Fragen, die sich beim Lernen des Stoffs stellen, mit ausführlichen Antworten in \"faq\", nach Themen in Abschnitte gruppiert.",
        _ => string.IsNullOrWhiteSpace(customInstruction)
            ? "Bericht nach freier Gliederung."
            : $"Nach folgender Vorgabe des Benutzers: „{customInstruction.Trim()}“"
    };

    /// <summary>Wertet die KI-Antwort aus (nachsichtig, inkl. Reparatur abgeschnittener Antworten).</summary>
    public static LenientJson.Result<Report> AnalyzeResponse(string? rawResponse, string fallbackTitle) =>
        LenientJson.Analyze(rawResponse, element => BuildReport(element, fallbackTitle));

    private static bool IsReportOrSection(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object &&
        (LenientJson.HasAny(element, SectionsKeys) || LenientJson.HasAny(element, HeadingKeys) ||
         LenientJson.HasAny(element, TermsKeys) || LenientJson.HasAny(element, QuestionListKeys));

    private static Report? BuildReport(JsonElement element, string fallbackTitle)
    {
        // Hülle aus dem JSON-Modus (z. B. {"bericht": {...}} oder {"items": [...]}) auspacken
        element = LenientJson.Unwrap(element, e => IsReportOrSection(e) || LenientJson.IsArrayOf(e, IsReportOrSection));

        var root = element;
        if (element.ValueKind == JsonValueKind.Array)
        {
            var objects = element.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).ToList();
            if (objects.Count == 0)
                return null;

            // [{ title, sections }] oder direkt eine Liste von Abschnitten
            root = objects.FirstOrDefault(o => LenientJson.GetProperty(o, SectionsKeys) is { ValueKind: JsonValueKind.Array });
            if (root.ValueKind != JsonValueKind.Object)
            {
                return objects.Count == 1
                    ? BuildReport(objects[0], fallbackTitle)
                    : Finish(new Report(), fallbackTitle, objects.Select(ParseSection));
            }
        }

        if (root.ValueKind != JsonValueKind.Object)
            return null;

        var report = new Report
        {
            Title = Clean(LenientJson.GetString(root, TitleKeys), MaxShortTextLength),
            Summary = Clean(LenientJson.GetString(root, SummaryKeys), MaxTextLength)
        };

        var sections = new List<ReportSection?>();
        if (LenientJson.GetProperty(root, SectionsKeys) is { ValueKind: JsonValueKind.Array } sectionArray)
            sections.AddRange(sectionArray.EnumerateArray().Select(ParseSection));

        // Glossar/FAQ ohne Abschnitte: Begriffe bzw. Fragen direkt im Wurzelobjekt
        if (sections.All(s => s == null))
        {
            var direct = ParseSection(root);
            if (direct != null)
            {
                direct.Heading = string.IsNullOrWhiteSpace(direct.Heading) || direct.Heading == report.Title ? string.Empty : direct.Heading;
                sections.Add(direct);
            }
        }

        return Finish(report, fallbackTitle, sections);
    }

    private static Report? Finish(Report report, string fallbackTitle, IEnumerable<ReportSection?> sections)
    {
        report.Sections = sections.Where(s => s != null).Select(s => s!).Take(MaxSections).ToList();
        if (report.Sections.Count == 0)
            return null;

        if (string.IsNullOrWhiteSpace(report.Title))
            report.Title = string.IsNullOrWhiteSpace(fallbackTitle) ? "Bericht" : fallbackTitle.Trim();

        return report;
    }

    private static ReportSection? ParseSection(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        var section = new ReportSection
        {
            Heading = Clean(LenientJson.GetString(element, HeadingKeys), MaxShortTextLength),
            Paragraphs = ReadTexts(LenientJson.GetProperty(element, ParagraphKeys), splitLines: false),
            Bullets = ReadTexts(LenientJson.GetProperty(element, BulletKeys), splitLines: true),
            Terms = ReadTerms(LenientJson.GetProperty(element, TermsKeys)),
            Questions = ReadQuestions(LenientJson.GetProperty(element, QuestionListKeys)),
            Takeaway = Clean(LenientJson.GetString(element, TakeawayKeys), MaxTextLength)
        };

        var hasContent = section.Paragraphs.Count > 0 || section.Bullets.Count > 0 || section.Terms.Count > 0
                         || section.Questions.Count > 0 || section.Takeaway.Length > 0;
        return hasContent ? section : null;
    }

    private static List<string> ReadTexts(JsonElement? value, bool splitLines)
    {
        var result = new List<string>();
        if (value == null)
            return result;

        void Add(string? text)
        {
            var cleaned = Clean(text?.Trim().TrimStart('-', '*', '•', '–').Trim(), MaxTextLength);
            if (cleaned.Length > 0 && result.Count < MaxItemsPerList)
                result.Add(cleaned);
        }

        switch (value.Value.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in value.Value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                        Add(item.GetString());
                    else if (item.ValueKind == JsonValueKind.Object)
                        Add(LenientJson.GetString(item, new[] { "text", "inhalt", "punkt", "content" }));
                }
                break;

            case JsonValueKind.String:
                var text = value.Value.GetString() ?? string.Empty;
                if (splitLines)
                    foreach (var line in text.Split('\n')) Add(line);
                else
                    foreach (var paragraph in text.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries)) Add(paragraph);
                break;
        }

        return result;
    }

    private static List<ReportTerm> ReadTerms(JsonElement? value)
    {
        var result = new List<ReportTerm>();
        if (value == null)
            return result;

        void Add(string? term, string? definition)
        {
            var t = Clean(term, MaxShortTextLength);
            if (t.Length > 0 && result.Count < MaxItemsPerList && result.All(x => !string.Equals(x.Term, t, StringComparison.OrdinalIgnoreCase)))
                result.Add(new ReportTerm { Term = t, Definition = Clean(definition, MaxTextLength) });
        }

        switch (value.Value.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in value.Value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                        Add(LenientJson.GetString(item, TermKeys), LenientJson.GetString(item, DefinitionKeys));
                    else if (item.ValueKind == JsonValueKind.String)
                    {
                        // "Begriff: Erklärung"
                        var s = item.GetString() ?? string.Empty;
                        var colon = s.IndexOf(':');
                        if (colon > 0) Add(s[..colon], s[(colon + 1)..]);
                        else Add(s, string.Empty);
                    }
                }
                break;

            case JsonValueKind.Object:
                // { "Begriff": "Erklärung", ... }
                foreach (var prop in value.Value.EnumerateObject())
                    Add(prop.Name, prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : string.Empty);
                break;
        }

        return result;
    }

    private static List<ReportQuestion> ReadQuestions(JsonElement? value)
    {
        var result = new List<ReportQuestion>();
        if (value is not { ValueKind: JsonValueKind.Array } array)
            return result;

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || result.Count >= MaxItemsPerList)
                continue;

            var question = Clean(LenientJson.GetString(item, QuestionKeys), MaxShortTextLength);
            if (question.Length > 0)
                result.Add(new ReportQuestion { Question = question, Answer = Clean(LenientJson.GetString(item, AnswerKeys), MaxTextLength) });
        }

        return result;
    }

    private static string Clean(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var t = text.Replace("\r", string.Empty).Trim();
        return t.Length <= maxLength ? t : t[..(maxLength - 1)].TrimEnd() + "…";
    }

    // ---------------- Speicherung ----------------

    public async Task<ReportLibrary> LoadLibraryAsync(string profileName)
    {
        var path = GetFilePath(profileName);
        if (!File.Exists(path))
            return new ReportLibrary();

        try
        {
            await using var stream = File.OpenRead(path);
            var library = await JsonSerializer.DeserializeAsync<ReportLibrary>(stream) ?? new ReportLibrary();
            library.Reports ??= new List<Report>();
            library.Reports.RemoveAll(r => r == null);

            foreach (var report in library.Reports)
            {
                report.Sections ??= new List<ReportSection>();
                report.Embeds ??= new ReportEmbeds();
                // Berichte aus der Zeit vor den Datentabellen: Eigenschaft fehlt → keine Tabellen einbetten
                report.Embeds.DataTables ??= false;
                // … und vor den Audio-Beiträgen: keine Skripte einbetten
                report.Embeds.AudioScripts ??= false;
                // … und vor den Videos: keine Video-Skripte einbetten
                report.Embeds.VideoScripts ??= false;
                report.SourceDocuments ??= new List<string>();
                foreach (var section in report.Sections)
                {
                    section.Paragraphs ??= new List<string>();
                    section.Bullets ??= new List<string>();
                    section.Terms ??= new List<ReportTerm>();
                    section.Questions ??= new List<ReportQuestion>();
                }
            }

            return library;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new ReportLibrary();
        }
    }

    public async Task SaveLibraryAsync(string profileName, ReportLibrary library)
    {
        var path = GetFilePath(profileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var temp = path + ".tmp";
        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, library, new JsonSerializerOptions { WriteIndented = true });
        }

        File.Move(temp, path, overwrite: true);
    }

    private string GetFilePath(string profileName) =>
        Path.Combine(_baseDirectory, "Reports", $"{JsonStorageService.ToSafeProfileFileName(profileName)}.json");

    // ---------------- Exporte ----------------

    public async Task ExportToDocxAsync(Report report, ReportStudioContent studio, string filePath, CultureInfo culture)
    {
        using var buffer = new MemoryStream();
        DocxWriter.Write(report, studio.Filter(report.Embeds), culture, buffer);
        buffer.Position = 0;

        await using var file = File.Create(filePath);
        await buffer.CopyToAsync(file);
    }

    public string ExportToMarkdown(Report report, ReportStudioContent studio, CultureInfo culture) =>
        ReportTextExporter.ToMarkdown(report, studio.Filter(report.Embeds), culture);

    public string ExportToPlainText(Report report, ReportStudioContent studio, CultureInfo culture) =>
        ReportTextExporter.ToPlainText(report, studio.Filter(report.Embeds), culture);

    public string ExportToHtml(Report report, ReportStudioContent studio, CultureInfo culture) =>
        ReportHtmlExporter.Export(report, studio.Filter(report.Embeds), culture);

    public string ExportToHtml(Report report, ReportStudioContent studio, ReportHtmlOptions options, CultureInfo culture) =>
        ReportHtmlExporter.Export(report, studio.Filter(report.Embeds), culture, options);
}
