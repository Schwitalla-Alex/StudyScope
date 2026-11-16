using System.Text.Json;
using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;

namespace StudyScope.Infrastructure.Services;

/// <summary>
/// Erstellt Präsentationen per KI aus Dokumentauszügen oder ohne KI aus einer Mindmap,
/// speichert sie je Profil unter <c>Presentations/&lt;Profil&gt;.json</c> und exportiert sie als PowerPoint-Datei.
/// </summary>
public class PresentationService : IPresentationService
{
    public const int MinSlides = 3;
    public const int MaxSlides = 20;

    /// <summary>Höchstzahl an Stichpunkten je Folie (mehr passt nicht lesbar auf eine Folie).</summary>
    public const int MaxBulletsPerSlide = 6;

    private const int MaxTextLength = 200;

    private static readonly string[] TitleKeys = { "title", "titel", "name", "thema", "heading", "überschrift", "ueberschrift" };
    private static readonly string[] SubtitleKeys = { "subtitle", "untertitel" };
    private static readonly string[] SlidesKeys = { "slides", "folien", "pages", "seiten" };
    private static readonly string[] BulletKeys = { "bullets", "stichpunkte", "points", "punkte", "content", "inhalt", "items", "text" };
    private static readonly string[] NotesKeys = { "notes", "sprechernotizen", "notizen", "speakernotes", "speaker_notes", "sprechertext" };

    private readonly ILlmService _llmService;
    private readonly string _baseDirectory;

    /// <param name="baseDirectory">Basisordner für die Speicherung (Tests); Standard ist <see cref="AppPaths.UserDataDirectory"/>.</param>
    public PresentationService(ILlmService llmService, string? baseDirectory = null)
    {
        _llmService = llmService;
        _baseDirectory = baseDirectory ?? AppPaths.UserDataDirectory;
    }

    // ---------------- Erzeugung per KI ----------------

    public async Task<PresentationGenerationResult> GenerateFromDocumentsAsync(
        IEnumerable<string> contextChunks, string fallbackTitle, int slideCount, CancellationToken cancellationToken = default)
    {
        var chunks = MindmapService.SelectRepresentativeChunks(contextChunks, MindmapService.MaxChunks);
        if (chunks.Count == 0)
            return new PresentationGenerationResult { FailureReason = MindmapFailureReason.NoContent };

        var count = Math.Clamp(slideCount, MinSlides, MaxSlides);

        // Wie bei der Mindmap ein JSON-ARRAY mit genau einem Objekt (Vorgabe des Dispatchers bei "JSON"-Prompts).
        // Der Prompt darf das Wort "Mindmap" nicht enthalten (Erkennung der Fake-KI).
        var prompt =
            $"Erstelle eine Präsentation mit genau {count} Inhaltsfolien aus den bereitgestellten Textauszügen.\n" +
            "Antworte AUSSCHLIESSLICH als gültiges JSON-Array mit genau EINEM Objekt, ohne Einleitung, ohne Markdown und ohne Schlusssatz.\n" +
            "Schema:\n" +
            "[{\"title\": \"Titel der Präsentation\", \"subtitle\": \"Untertitel\", \"slides\": [" +
            "{\"title\": \"Folientitel\", \"bullets\": [\"Stichpunkt\", \"Stichpunkt\"], \"notes\": \"Sprechernotiz\"}]}]\n" +
            "Regeln:\n" +
            "- Sinnvolle Reihenfolge: Einstieg, Hauptteil, die letzte Folie ist ein Fazit.\n" +
            "- Je Folie 3 bis 5 kurze Stichpunkte (höchstens 12 Wörter).\n" +
            "- notes: 2 bis 3 Sätze, die der Vortragende zu der Folie sagt.\n" +
            "- Nur Inhalte verwenden, die in den Auszügen vorkommen. Sprache: Deutsch.";

        var raw = await _llmService.AskQuestionAsync(prompt, chunks, cancellationToken);
        var parsed = AnalyzeResponse(raw, fallbackTitle);

        return new PresentationGenerationResult
        {
            Presentation = parsed.Value,
            RawResponse = raw ?? string.Empty,
            WasRepaired = parsed.WasRepaired,
            FailureReason = parsed.FailureReason
        };
    }

    /// <summary>
    /// Wertet die KI-Antwort aus (nachsichtig wie bei der Mindmap, inkl. Reparatur abgeschnittener Antworten).
    /// Akzeptiert auch ein Objekt statt eines Arrays, eine reine Folienliste, deutsche Schlüssel und
    /// Stichpunkte als Text mit Zeilenumbrüchen.
    /// </summary>
    public static LenientJson.Result<PresentationDocument> AnalyzeResponse(string? rawResponse, string fallbackTitle) =>
        LenientJson.Analyze(rawResponse, element => BuildPresentation(element, fallbackTitle));

    private static PresentationDocument? BuildPresentation(JsonElement element, string fallbackTitle)
    {
        string title = string.Empty, subtitle = string.Empty;
        IEnumerable<JsonElement> slideElements;

        if (element.ValueKind == JsonValueKind.Array)
        {
            var items = element.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).ToList();

            // Ein Objekt mit "slides" → Präsentation; sonst wird das Array selbst als Folienliste behandelt
            var container = items.FirstOrDefault(i => LenientJson.GetProperty(i, SlidesKeys) is { ValueKind: JsonValueKind.Array });
            if (container.ValueKind == JsonValueKind.Object)
            {
                title = LenientJson.GetString(container, TitleKeys);
                subtitle = LenientJson.GetString(container, SubtitleKeys);
                slideElements = LenientJson.GetProperty(container, SlidesKeys)!.Value.EnumerateArray();
            }
            else
            {
                slideElements = items;
            }
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            title = LenientJson.GetString(element, TitleKeys);
            subtitle = LenientJson.GetString(element, SubtitleKeys);
            slideElements = LenientJson.GetProperty(element, SlidesKeys) is { ValueKind: JsonValueKind.Array } arr
                ? arr.EnumerateArray()
                : Enumerable.Empty<JsonElement>();
        }
        else
        {
            return null;
        }

        var contentSlides = slideElements
            .Select(ParseSlide)
            .Where(s => s != null)
            .Take(MaxSlides)
            .Select(s => s!)
            .ToList();

        if (contentSlides.Count == 0)
            return null;

        var presentationTitle = Clean(string.IsNullOrWhiteSpace(title) ? fallbackTitle : title);
        if (string.IsNullOrWhiteSpace(presentationTitle))
            presentationTitle = "Präsentation";

        var presentation = new PresentationDocument { Title = presentationTitle, Origin = PresentationOrigin.Ai };
        presentation.Slides.Add(new PresentationSlide { Kind = SlideKind.Title, Title = presentationTitle, Subtitle = Clean(subtitle) });
        presentation.Slides.AddRange(contentSlides);
        return presentation;
    }

    private static PresentationSlide? ParseSlide(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        var title = Clean(LenientJson.GetString(element, TitleKeys));
        var bullets = ReadBullets(LenientJson.GetProperty(element, BulletKeys));
        if (string.IsNullOrWhiteSpace(title) && bullets.Count == 0)
            return null;

        return new PresentationSlide
        {
            Kind = SlideKind.Content,
            Title = string.IsNullOrWhiteSpace(title) ? bullets[0] : title,
            Bullets = bullets.Take(MaxBulletsPerSlide).ToList(),
            Notes = LenientJson.GetString(element, NotesKeys).Trim()
        };
    }

    private static List<string> ReadBullets(JsonElement? value)
    {
        var result = new List<string>();
        if (value == null)
            return result;

        switch (value.Value.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in value.Value.EnumerateArray())
                {
                    var text = item.ValueKind switch
                    {
                        JsonValueKind.String => item.GetString(),
                        JsonValueKind.Object => LenientJson.GetString(item, TitleKeys.Concat(BulletKeys).ToArray()),
                        _ => null
                    };
                    AddBullet(result, text);
                }
                break;

            case JsonValueKind.String:
                // Stichpunkte als Fließtext mit Zeilenumbrüchen ("- Punkt\n- Punkt")
                foreach (var line in (value.Value.GetString() ?? string.Empty).Split('\n'))
                    AddBullet(result, line);
                break;
        }

        return result;
    }

    private static void AddBullet(List<string> bullets, string? text)
    {
        var cleaned = Clean(text?.Trim().TrimStart('-', '*', '•', '–').Trim());
        if (!string.IsNullOrWhiteSpace(cleaned))
            bullets.Add(cleaned);
    }

    private static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var t = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return t.Length <= MaxTextLength ? t : t[..(MaxTextLength - 1)].TrimEnd() + "…";
    }

    // ---------------- Ableitung aus der Mindmap ----------------

    /// <summary>
    /// Titelfolie = Hauptthema, danach eine Überblicksfolie mit den Hauptästen, je Hauptast eine Folie
    /// (Unterthemen als Stichpunkte, Erklärungen als Sprechernotizen) und eine Zusammenfassung.
    /// Hat ein Ast mehr Unterthemen als auf eine Folie passen, wird er auf Folgefolien verteilt.
    /// </summary>
    public PresentationDocument CreateFromMindmap(MindmapDocument mindmap)
    {
        var root = mindmap.Root;
        var presentation = new PresentationDocument
        {
            Title = root.Title,
            Origin = PresentationOrigin.Mindmap,
            SourceDocuments = mindmap.SourceDocuments.ToList()
        };

        presentation.Slides.Add(new PresentationSlide
        {
            Kind = SlideKind.Title,
            Title = root.Title,
            Subtitle = root.Summary,
            Notes = root.Summary
        });

        var branches = root.Children;
        if (branches.Count > 0)
        {
            AddSlides(presentation, "Überblick", branches.Select(b => b.Title).ToList(),
                "Die Präsentation behandelt folgende Themen: " + string.Join(", ", branches.Select(b => b.Title)) + ".");
        }

        foreach (var branch in branches)
        {
            var bullets = branch.Children.Count > 0
                ? branch.Children.Select(c => string.IsNullOrWhiteSpace(c.Summary) ? c.Title : $"{c.Title}: {c.Summary}").ToList()
                : new List<string> { string.IsNullOrWhiteSpace(branch.Summary) ? branch.Title : branch.Summary };

            var notes = string.Join(" ", new[] { branch.Summary }
                .Concat(branch.Children.SelectMany(c => c.Children).Select(d => string.IsNullOrWhiteSpace(d.Summary) ? d.Title : $"{d.Title}: {d.Summary}"))
                .Where(t => !string.IsNullOrWhiteSpace(t)));

            AddSlides(presentation, branch.Title, bullets, notes);
        }

        if (branches.Count > 1)
        {
            AddSlides(presentation, "Zusammenfassung",
                branches.Select(b => string.IsNullOrWhiteSpace(b.Summary) ? b.Title : $"{b.Title}: {b.Summary}").ToList(),
                root.Summary);
        }

        return presentation;
    }

    private static void AddSlides(PresentationDocument presentation, string title, List<string> bullets, string notes)
    {
        var cleaned = bullets.Select(Clean).Where(b => b.Length > 0).ToList();
        var pages = Math.Max(1, (int)Math.Ceiling(cleaned.Count / (double)MaxBulletsPerSlide));

        for (var page = 0; page < pages; page++)
        {
            presentation.Slides.Add(new PresentationSlide
            {
                Kind = SlideKind.Content,
                Title = page == 0 ? Clean(title) : $"{Clean(title)} (Fortsetzung)",
                Bullets = cleaned.Skip(page * MaxBulletsPerSlide).Take(MaxBulletsPerSlide).ToList(),
                Notes = page == 0 ? notes.Trim() : string.Empty
            });
        }
    }

    // ---------------- Speicherung ----------------

    public async Task<PresentationDocument?> LoadPresentationAsync(string profileName)
    {
        var path = GetFilePath(profileName);
        if (!File.Exists(path))
            return null;

        try
        {
            await using var stream = File.OpenRead(path);
            var presentation = await JsonSerializer.DeserializeAsync<PresentationDocument>(stream);
            if (presentation?.Slides == null || presentation.Slides.Count == 0)
                return null;

            foreach (var slide in presentation.Slides)
            {
                slide.Bullets ??= new List<string>();
                slide.Title ??= string.Empty;
                slide.Subtitle ??= string.Empty;
                slide.Notes ??= string.Empty;
                if (string.IsNullOrWhiteSpace(slide.Id))
                    slide.Id = Guid.NewGuid().ToString("N");
            }

            return presentation;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    public async Task SavePresentationAsync(string profileName, PresentationDocument presentation)
    {
        var path = GetFilePath(profileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, presentation, new JsonSerializerOptions { WriteIndented = true });
    }

    public Task DeletePresentationAsync(string profileName)
    {
        var path = GetFilePath(profileName);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    private string GetFilePath(string profileName) =>
        Path.Combine(_baseDirectory, "Presentations", $"{JsonStorageService.ToSafeProfileFileName(profileName)}.json");

    // ---------------- Export ----------------

    public async Task ExportToPptxAsync(PresentationDocument presentation, string filePath)
    {
        // Erst in den Speicher schreiben, damit bei einem Fehler keine halbe Datei zurückbleibt
        using var buffer = new MemoryStream();
        PptxWriter.Write(presentation, buffer);
        buffer.Position = 0;

        await using var file = File.Create(filePath);
        await buffer.CopyToAsync(file);
    }
}
