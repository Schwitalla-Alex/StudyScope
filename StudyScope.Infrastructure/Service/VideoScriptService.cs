using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.Core.Localization;
using StudyScope.Infrastructure.Resources;

namespace StudyScope.Infrastructure.Services;

/// <summary>
/// Videoübersicht: Die Chat-KI schreibt je Folie einer Präsentation einen kurzen Sprechtext (oder die Sprechernotizen
/// werden direkt übernommen). Nachsichtige Auswertung über <see cref="LenientJson"/>, fehlende Szenen aus den Notizen.
/// Speicherung je Profil unter <c>Videos/&lt;Profil&gt;.json</c>, Audiodateien je Video unter
/// <c>Videos/&lt;Profil&gt;/&lt;Video-Kennung&gt;/</c>: eine WAV je Szene (nur geänderte Szenen werden neu erzeugt) und
/// die daraus zusammengesetzte Datei für die Wiedergabe mit exakten Szenen-Startzeiten.
/// </summary>
public partial class VideoScriptService : IVideoScriptService
{
    private const int MaxTitleLength = 200;

    /// <summary>Stille zwischen zwei Szenen in der zusammengesetzten Audiodatei (Folie bleibt kurz stehen).</summary>
    public const double PauseBetweenScenesSeconds = 0.6;

    /// <summary>Dauer einer Szene ohne Sprechtext (die Folie wird trotzdem gezeigt).</summary>
    public const double SilentSceneSeconds = 3.0;

    public const string CombinedAudioFileName = "video.wav";

    private static readonly string[] TitleKeys = { "title", "titel", "name", "überschrift", "ueberschrift" };
    private static readonly string[] ScenesKeys =
    {
        "scenes", "szenen", "slides", "folien", "items", "segments", "segmente", "abschnitte", "parts", "teile", "narrations", "sprechtexte"
    };
    private static readonly string[] IndexKeys =
    {
        "slideIndex", "slide_index", "slide", "folie", "folienindex", "folien_index", "folienNummer", "foliennummer",
        "index", "nr", "nummer", "number", "slideNumber", "slide_number", "position"
    };
    private static readonly string[] NarrationKeys =
    {
        "narration", "sprechtext", "sprechertext", "erzähltext", "erzaehltext", "text", "script", "skript", "voiceover",
        "voice_over", "kommentar", "inhalt", "content", "sprecher", "speech", "gesprochen", "spoken"
    };

    private readonly ILlmService _llmService;
    private readonly string _baseDirectory;

    /// <param name="baseDirectory">Basisordner für die Speicherung (Tests); Standard ist <see cref="AppPaths.UserDataDirectory"/>.</param>
    public VideoScriptService(ILlmService llmService, string? baseDirectory = null)
    {
        _llmService = llmService;
        _baseDirectory = baseDirectory ?? AppPaths.UserDataDirectory;
    }

    // ---------------- Erzeugung ----------------

    public async Task<VideoScriptResult> GenerateAsync(PresentationDocument presentation, string fallbackTitle, string? outputLanguage = null, CancellationToken cancellationToken = default)
    {
        if (presentation.Slides.Count == 0)
            return new VideoScriptResult { FailureReason = MindmapFailureReason.NoContent };

        // Sprechtexte in der Sprache der Folien (Präsentation ohne Angabe: Einstellung, aufgelöst auf den Folientexten)
        var slideChunks = BuildSlideChunks(presentation);
        var language = ResultLanguage.FromCode(presentation.Language) ?? ResultLanguage.Resolve(outputLanguage, slideChunks);
        var prompt = ResultLanguage.ApplyToPrompt(BuildPrompt(presentation), language);
        var raw = await _llmService.AskQuestionAsync(prompt, slideChunks, cancellationToken);
        var analysis = AnalyzeResponse(raw, presentation.Slides.Count);
        if (analysis.Value == null)
            return new VideoScriptResult { RawResponse = raw ?? string.Empty, FailureReason = analysis.FailureReason };

        var video = CreateVideo(presentation, analysis.Value.Title, fallbackTitle, VideoNarrationSource.Ai, language);
        var filled = 0;
        for (var i = 0; i < video.Scenes.Count; i++)
        {
            var narration = i < analysis.Value.Narrations.Count ? analysis.Value.Narrations[i] : string.Empty;
            if (string.IsNullOrWhiteSpace(narration))
            {
                narration = FallbackNarration(presentation.Slides[i]);
                filled++;
            }
            video.Scenes[i].Narration = narration;
        }

        // Hat die KI zu keiner einzigen Folie einen Text geliefert, ist die Antwort nicht verwertbar
        if (filled == video.Scenes.Count)
            return new VideoScriptResult { RawResponse = raw ?? string.Empty, FailureReason = MindmapFailureReason.NoTopics };

        return new VideoScriptResult { Video = video, RawResponse = raw ?? string.Empty, WasRepaired = analysis.WasRepaired, FilledFromNotes = filled };
    }

    public VideoDocument CreateFromNotes(PresentationDocument presentation, string fallbackTitle)
    {
        var language = ResultLanguage.FromCode(presentation.Language) ?? OutputLanguage.Neutral(System.Globalization.CultureInfo.CurrentUICulture)
                       ?? System.Globalization.CultureInfo.GetCultureInfo(UiLanguage.NeutralLanguage);
        var video = CreateVideo(presentation, string.Empty, fallbackTitle, VideoNarrationSource.Notes, language);
        video.Language = presentation.Language;
        for (var i = 0; i < video.Scenes.Count; i++)
            video.Scenes[i].Narration = FallbackNarration(presentation.Slides[i]);
        return video;
    }

    private static VideoDocument CreateVideo(PresentationDocument presentation, string aiTitle, string fallbackTitle, VideoNarrationSource source,
        System.Globalization.CultureInfo language)
    {
        var slides = presentation.Slides.Take(VideoDocument.MaxScenes).Select(CloneSlide).ToList();
        var title = Clean(aiTitle, MaxTitleLength);
        if (title.Length == 0) title = Clean(presentation.Title, MaxTitleLength);
        if (title.Length == 0) title = string.IsNullOrWhiteSpace(fallbackTitle) ? ResultLanguage.Content(language, () => ContentStrings.Content_VideoTitle) : fallbackTitle.Trim();

        return new VideoDocument
        {
            Title = title,
            Language = source == VideoNarrationSource.Ai ? ResultLanguage.Code(language) : presentation.Language,
            PresentationTitle = presentation.Title ?? string.Empty,
            PresentationHash = VideoDocument.ComputePresentationHash(presentation),
            Slides = slides,
            Scenes = slides.Select(s => new VideoScene { SlideId = s.Id }).ToList(),
            NarrationSource = source,
            SourceDocuments = presentation.SourceDocuments.ToList()
        };
    }

    private static PresentationSlide CloneSlide(PresentationSlide slide) => new()
    {
        Id = string.IsNullOrWhiteSpace(slide.Id) ? Guid.NewGuid().ToString("N") : slide.Id,
        Kind = slide.Kind,
        Title = slide.Title ?? string.Empty,
        Subtitle = slide.Subtitle ?? string.Empty,
        Bullets = (slide.Bullets ?? new List<string>()).ToList(),
        Notes = slide.Notes ?? string.Empty
    };

    /// <summary>
    /// Ob die Präsentation brauchbare Sprechernotizen hat: mindestens 80 % der Folien mit mindestens 6 Wörtern Notiz.
    /// Dann bietet der Reiter „Sprechernotizen direkt verwenden“ (ohne KI) an.
    /// </summary>
    public static bool HasUsableNotes(PresentationDocument? presentation) => VideoNarration.HasUsableNotes(presentation);

    /// <summary>Sprechtext ohne KI: Sprechernotizen, sonst aus Titel und Stichpunkten gebildet.</summary>
    public static string FallbackNarration(PresentationSlide slide) => VideoNarration.FromSlide(slide, AudioScriptService.CleanSpokenText);

    /// <summary>
    /// Prompt für das Video-Skript. Beginnt mit „Erstelle ein Video-Skript“ (Erkennung der Fake-KI) und verlangt ein
    /// JSON-Array mit genau einem Objekt. Die Folien gehen als Textauszüge („Folie N: …“) mit.
    /// </summary>
    public static string BuildPrompt(PresentationDocument presentation)
    {
        var count = presentation.Slides.Count;
        var sb = new StringBuilder();
        sb.Append("Erstelle ein Video-Skript: Schreibe zu jeder Folie der bereitgestellten Präsentation einen kurzen Sprechtext, der vorgelesen wird, während die Folie zu sehen ist.\n");
        if (!string.IsNullOrWhiteSpace(presentation.Title))
            sb.Append("Präsentation: „").Append(presentation.Title.Trim()).Append("“\n");
        sb.Append("Anzahl Folien: ").Append(count).Append(" (slideIndex von 0 bis ").Append(count - 1).Append(", in dieser Reihenfolge, für jede Folie genau eine Szene).\n");
        sb.Append("Jede Folie steht in den Auszügen als „Folie N: Titel“ mit Stichpunkten und Sprechernotizen (N beginnt bei 1, slideIndex = N - 1).\n");
        sb.Append("Antworte AUSSCHLIESSLICH als gültiges JSON-Array mit genau EINEM Objekt, ohne Einleitung, ohne Markdown und ohne Schlusssatz.\n");
        sb.Append("Schema:\n");
        sb.Append("[{\"title\": \"Titel des Videos\", \"scenes\": [{\"slideIndex\": 0, \"narration\": \"Gesprochener Text zur ersten Folie\"}]}]\n");
        sb.Append("Regeln für den Sprechtext:\n");
        sb.Append("- Titelfolie: zwei bis drei Sätze Einstieg (etwa 25 bis 45 Wörter), der sagt, worum es geht.\n");
        sb.Append("- Inhaltsfolien: etwa 50 bis 90 Wörter; erkläre die Stichpunkte in ihrer Reihenfolge in ganzen Sätzen und nutze die Sprechernotizen.\n");
        sb.Append("- Lies die Stichpunkte nicht wörtlich vor, sondern erkläre sie verständlich; letzte Folie mit kurzem Fazit.\n");
        sb.Append("- Natürliches, gesprochenes Deutsch in eher kurzen Sätzen – keine Aufzählungszeichen, keine Überschriften, kein Markdown.\n");
        sb.Append("- Abkürzungen ausschreiben (zum Beispiel statt z. B., Prozent statt %), Zahlen und Einheiten so schreiben, wie man sie spricht.\n");
        sb.Append("- Verwende NUR Inhalte der Folien und Notizen. Nichts erfinden.\n");
        return sb.ToString();
    }

    /// <summary>Folien als Textauszüge für die KI: „Folie N: Titel“, Untertitel, Stichpunkte, Sprechernotizen.</summary>
    public static List<string> BuildSlideChunks(PresentationDocument presentation)
    {
        var chunks = new List<string>();
        for (var i = 0; i < presentation.Slides.Count; i++)
        {
            var slide = presentation.Slides[i];
            var sb = new StringBuilder();
            sb.Append("Folie ").Append(i + 1).Append(": ").Append(slide.Title?.Trim());
            if (slide.Kind == SlideKind.Title) sb.Append(" (Titelfolie)");
            if (!string.IsNullOrWhiteSpace(slide.Subtitle)) sb.Append("\nUntertitel: ").Append(slide.Subtitle.Trim());
            var bullets = (slide.Bullets ?? new List<string>()).Where(b => !string.IsNullOrWhiteSpace(b)).ToList();
            if (bullets.Count > 0)
            {
                sb.Append("\nStichpunkte:");
                foreach (var bullet in bullets) sb.Append("\n- ").Append(bullet.Trim());
            }
            if (!string.IsNullOrWhiteSpace(slide.Notes)) sb.Append("\nSprechernotizen: ").Append(slide.Notes.Trim());
            chunks.Add(sb.ToString());
        }
        return chunks;
    }

    // ---------------- Auswertung ----------------

    /// <summary>Ausgewertete KI-Antwort: Titel und Sprechtext je Folie (leer = fehlt).</summary>
    public sealed class ParsedScript
    {
        public string Title { get; init; } = string.Empty;
        public List<string> Narrations { get; init; } = new();
    }

    /// <summary>
    /// Wertet die KI-Antwort nachsichtig aus: Hüllen, deutsche Schlüssel, Folienindex ab 0 oder 1 (auch „Folie 3“),
    /// Szenen ohne Index nach Position, Texte als Liste von Sätzen, reine Liste von Strings, abgeschnittene Antworten.
    /// </summary>
    public static LenientJson.Result<ParsedScript> AnalyzeResponse(string? rawResponse, int slideCount) =>
        LenientJson.Analyze(rawResponse, element => BuildScript(element, slideCount));

    private static bool IsScene(JsonElement e) =>
        e.ValueKind == JsonValueKind.Object && LenientJson.HasAny(e, NarrationKeys);

    private static bool HasScenes(JsonElement e) =>
        LenientJson.GetProperty(e, ScenesKeys) is { ValueKind: JsonValueKind.Array } arr && arr.EnumerateArray().Any(x => IsScene(x) || x.ValueKind == JsonValueKind.String);

    private static bool IsScriptOrSceneList(JsonElement e) =>
        HasScenes(e) || LenientJson.IsArrayOf(e, x => HasScenes(x) || IsScene(x));

    private static ParsedScript? BuildScript(JsonElement element, int slideCount)
    {
        if (slideCount <= 0) return null;
        element = LenientJson.Unwrap(element, IsScriptOrSceneList);

        var title = string.Empty;
        List<JsonElement> scenes;
        if (element.ValueKind == JsonValueKind.Array)
        {
            var items = element.EnumerateArray().ToList();
            var container = items.FirstOrDefault(HasScenes);
            if (container.ValueKind == JsonValueKind.Object)
            {
                title = LenientJson.GetString(container, TitleKeys);
                scenes = LenientJson.GetProperty(container, ScenesKeys)!.Value.EnumerateArray().ToList();
            }
            else
            {
                scenes = items;
            }
        }
        else if (element.ValueKind == JsonValueKind.Object && HasScenes(element))
        {
            title = LenientJson.GetString(element, TitleKeys);
            scenes = LenientJson.GetProperty(element, ScenesKeys)!.Value.EnumerateArray().ToList();
        }
        else
        {
            return null;
        }

        var entries = new List<(int? Index, string Text)>();
        foreach (var scene in scenes)
        {
            if (scene.ValueKind == JsonValueKind.String)
            {
                entries.Add((null, scene.GetString() ?? string.Empty));
            }
            else if (scene.ValueKind == JsonValueKind.Object)
            {
                entries.Add((ReadIndex(scene), ReadNarration(scene)));
            }
        }

        entries = entries.Where(e => !string.IsNullOrWhiteSpace(e.Text)).ToList();
        if (entries.Count == 0) return null;

        // Index ab 1 erkennen: keine 0, größter Index = Folienzahl (bzw. alle Indizes ≥ 1 und einer davon = Folienzahl)
        var indices = entries.Where(e => e.Index.HasValue).Select(e => e.Index!.Value).ToList();
        var shift = indices.Count > 0 && !indices.Contains(0) && indices.Max() == slideCount ? 1 : 0;

        var narrations = Enumerable.Repeat(string.Empty, slideCount).ToList();
        var position = 0;
        foreach (var (index, text) in entries)
        {
            var target = index.HasValue ? index.Value - shift : position;
            position = target + 1;
            if (target < 0 || target >= slideCount) continue;
            var cleaned = CleanNarration(text);
            if (cleaned.Length == 0) continue;
            narrations[target] = narrations[target].Length == 0 ? cleaned : $"{narrations[target]} {cleaned}";
        }

        return narrations.All(n => n.Length == 0) ? null : new ParsedScript { Title = title, Narrations = narrations };
    }

    private static int? ReadIndex(JsonElement scene)
    {
        if (LenientJson.GetProperty(scene, IndexKeys) is not { } value) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n)) return n;
        if (value.ValueKind == JsonValueKind.String)
        {
            var match = Digits().Match(value.GetString() ?? string.Empty);
            if (match.Success && int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
        }
        return null;
    }

    private static string ReadNarration(JsonElement scene)
    {
        if (LenientJson.GetProperty(scene, NarrationKeys) is not { } value) return string.Empty;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Array => string.Join(" ", value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString())),
            _ => string.Empty
        };
    }

    /// <summary>Sprechtext bereinigen (wie beim Audio-Skript) und auf die Höchstlänge kürzen.</summary>
    public static string CleanNarration(string? text)
    {
        var cleaned = AudioScriptService.CleanSpokenText(text);
        if (cleaned.Length <= VideoDocument.MaxNarrationLength) return cleaned;
        var cut = cleaned.LastIndexOfAny(new[] { '.', '!', '?' }, VideoDocument.MaxNarrationLength - 1);
        return cut > VideoDocument.MaxNarrationLength / 2 ? cleaned[..(cut + 1)] : cleaned[..VideoDocument.MaxNarrationLength].TrimEnd();
    }

    private static string Clean(string? text, int maxLength)
    {
        var value = MultiSpace().Replace((text ?? string.Empty).Replace('\n', ' '), " ").Trim().Trim('#', '*').Trim();
        return value.Length > maxLength ? value[..maxLength].TrimEnd() : value;
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultiSpace();

    // ---------------- Folien aktualisieren ----------------

    public IReadOnlyList<VideoScene> RefreshSlides(VideoDocument video, PresentationDocument presentation)
    {
        var newSlides = presentation.Slides.Take(VideoDocument.MaxScenes).Select(CloneSlide).ToList();
        var oldScenes = video.Scenes.ToList();
        var oldSlides = video.Slides.ToList();
        var used = new HashSet<VideoScene>();
        var scenes = new List<VideoScene>();

        for (var i = 0; i < newSlides.Count; i++)
        {
            var slide = newSlides[i];
            var match = oldScenes.FirstOrDefault(s => !used.Contains(s) && s.SlideId == slide.Id);
            if (match == null && i < oldScenes.Count && !used.Contains(oldScenes[i]) && i < oldSlides.Count &&
                string.Equals(oldSlides[i].Title?.Trim(), slide.Title?.Trim(), StringComparison.OrdinalIgnoreCase))
                match = oldScenes[i];

            if (match != null)
            {
                used.Add(match);
                match.SlideId = slide.Id;
                scenes.Add(match);
            }
            else
            {
                scenes.Add(new VideoScene { SlideId = slide.Id, Narration = FallbackNarration(slide) });
            }
        }

        video.Slides = newSlides;
        video.Scenes = scenes;
        video.PresentationTitle = presentation.Title ?? string.Empty;
        video.PresentationHash = VideoDocument.ComputePresentationHash(presentation);
        video.OwnSlides = false;
        video.SourceDocuments = presentation.SourceDocuments.ToList();
        return oldScenes.Where(s => !used.Contains(s)).ToList();
    }

    // ---------------- Speicherung ----------------

    public async Task<VideoLibrary> LoadLibraryAsync(string profileName)
    {
        var path = GetFilePath(profileName);
        if (!File.Exists(path))
            return new VideoLibrary();

        try
        {
            await using var stream = File.OpenRead(path);
            var library = await JsonSerializer.DeserializeAsync<VideoLibrary>(stream) ?? new VideoLibrary();
            library.Items ??= new List<VideoDocument>();
            library.Items.RemoveAll(i => i == null);
            foreach (var video in library.Items)
                Normalize(video);
            return library;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new VideoLibrary();
        }
    }

    /// <summary>Fehlende Felder auffüllen und Folien/Szenen in Gleichstand bringen (eine Szene je Folie).</summary>
    private static void Normalize(VideoDocument video)
    {
        video.Title ??= string.Empty;
        video.PresentationTitle ??= string.Empty;
        video.PresentationHash ??= string.Empty;
        video.Slides ??= new List<PresentationSlide>();
        video.Slides.RemoveAll(s => s == null);
        foreach (var slide in video.Slides)
        {
            slide.Id = string.IsNullOrWhiteSpace(slide.Id) ? Guid.NewGuid().ToString("N") : slide.Id;
            slide.Title ??= string.Empty;
            slide.Subtitle ??= string.Empty;
            slide.Bullets ??= new List<string>();
            slide.Notes ??= string.Empty;
        }
        video.Scenes ??= new List<VideoScene>();
        video.Scenes.RemoveAll(s => s == null);
        foreach (var scene in video.Scenes)
        {
            scene.Id = string.IsNullOrWhiteSpace(scene.Id) ? Guid.NewGuid().ToString("N") : scene.Id;
            scene.SlideId ??= string.Empty;
            scene.Narration ??= string.Empty;
            scene.AudioFileName ??= string.Empty;
            scene.AudioTextHash ??= string.Empty;
        }
        while (video.Scenes.Count < video.Slides.Count)
            video.Scenes.Add(new VideoScene { SlideId = video.Slides[video.Scenes.Count].Id });
        if (video.Scenes.Count > video.Slides.Count)
            video.Scenes.RemoveRange(video.Slides.Count, video.Scenes.Count - video.Slides.Count);
        video.SourceDocuments ??= new List<string>();
        video.AudioFileName ??= string.Empty;
        video.AudioSceneStarts ??= new List<double>();
        video.AudioCreatedWith ??= string.Empty;
        video.AudioScenesHash ??= string.Empty;
    }

    public async Task SaveLibraryAsync(string profileName, VideoLibrary library)
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
        Path.Combine(_baseDirectory, "Videos", $"{JsonStorageService.ToSafeProfileFileName(profileName)}.json");

    private string ProfileDirectory(string profileName) =>
        Path.Combine(_baseDirectory, "Videos", JsonStorageService.ToSafeProfileFileName(profileName));

    /// <summary>Ordnername eines Videos: nur Buchstaben und Ziffern der Kennung (kein Ausbrechen über „..\“).</summary>
    public static string VideoFolderName(VideoDocument video)
    {
        var id = new string((video.Id ?? string.Empty).Where(char.IsAsciiLetterOrDigit).ToArray());
        return id.Length == 0 ? "video" : id;
    }

    private string VideoDirectoryPath(string profileName, VideoDocument video) =>
        Path.Combine(ProfileDirectory(profileName), VideoFolderName(video));

    public string GetVideoDirectory(string profileName, VideoDocument video)
    {
        var directory = VideoDirectoryPath(profileName, video);
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>Dateiname der Szenen-Audiodatei aus der Szenenkennung, z. B. „szene_1a2b3c4d5e6f.wav“.</summary>
    public static string SceneFileName(VideoScene scene)
    {
        var id = new string((scene.Id ?? string.Empty).Where(char.IsAsciiLetterOrDigit).ToArray());
        if (id.Length > 16) id = id[..16];
        return $"szene_{(id.Length == 0 ? "x" : id)}.wav";
    }

    public string? GetSceneAudioPath(string profileName, VideoDocument video, VideoScene scene)
    {
        if (!scene.HasAudio) return null;
        var path = Path.Combine(VideoDirectoryPath(profileName, video), Path.GetFileName(scene.AudioFileName));
        return File.Exists(path) ? path : null;
    }

    public string GetTargetSceneAudioPath(string profileName, VideoDocument video, VideoScene scene) =>
        Path.Combine(GetVideoDirectory(profileName, video), SceneFileName(scene));

    public string? GetAudioFilePath(string profileName, VideoDocument video)
    {
        if (!video.HasAudioFile) return null;
        var path = Path.Combine(VideoDirectoryPath(profileName, video), Path.GetFileName(video.AudioFileName));
        return File.Exists(path) ? path : null;
    }

    public async Task<VideoAudioAssembly> AssembleAudioAsync(string profileName, VideoDocument video, CancellationToken cancellationToken = default)
    {
        // Format der ersten vorhandenen Szenen-Audiodatei bestimmt das Format der Gesamtdatei
        var sources = new List<string?>();
        for (var i = 0; i < video.Scenes.Count; i++)
        {
            var scene = video.Scenes[i];
            if (!scene.HasNarration)
            {
                sources.Add(null);
                continue;
            }

            var path = GetSceneAudioPath(profileName, video, scene);
            if (path == null || scene.NeedsAudio)
                throw new InvalidOperationException(Loc.Format(InfraStrings.Video_SceneAudioMissing, ("number", i + 1)));
            sources.Add(path);
        }

        if (sources.All(s => s == null))
            throw new InvalidOperationException(InfraStrings.Video_NoNarrationToAssemble);

        var directory = GetVideoDirectory(profileName, video);
        var target = Path.Combine(directory, CombinedAudioFileName);
        var temp = target + ".part";
        TryDelete(temp);

        var starts = new List<double>();
        double duration;
        try
        {
            PcmAudio? format = null;
            using (var writer = new WavFileWriter(temp))
            {
                for (var i = 0; i < sources.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var source = sources[i];
                    PcmAudio audio;
                    if (source != null)
                    {
                        audio = WavAudio.Decode(await File.ReadAllBytesAsync(source, cancellationToken), "audio/wav");
                        format ??= audio;
                    }
                    else
                    {
                        format ??= await FirstFormatAsync(sources, cancellationToken);
                        audio = format with { Samples = WavAudio.Silence(format, SilentSceneSeconds) };
                    }

                    starts.Add(Math.Round(writer.DurationSeconds, 3));
                    writer.Append(audio);
                    video.Scenes[i].AudioDurationSeconds = source != null ? Math.Round(audio.DurationSeconds, 3) : video.Scenes[i].AudioDurationSeconds;
                    if (i < sources.Count - 1)
                        writer.AppendSilence(PauseBetweenScenesSeconds);
                }

                duration = writer.DurationSeconds;
                writer.Complete();
            }

            File.Move(temp, target, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }

        return new VideoAudioAssembly(CombinedAudioFileName, Math.Round(duration, 3), starts);
    }

    private static async Task<PcmAudio> FirstFormatAsync(IEnumerable<string?> sources, CancellationToken cancellationToken)
    {
        var first = sources.First(s => s != null)!;
        var audio = WavAudio.Decode(await File.ReadAllBytesAsync(first, cancellationToken), "audio/wav");
        return audio with { Samples = Array.Empty<byte>() };
    }

    public void DeleteSceneAudio(string profileName, VideoDocument video, VideoScene scene)
    {
        var path = GetSceneAudioPath(profileName, video, scene);
        if (path != null && !TryDelete(path))
            return;
        scene.ClearAudio();
    }

    public void DeleteAllAudio(string profileName, VideoDocument video)
    {
        var directory = VideoDirectoryPath(profileName, video);
        if (Directory.Exists(directory))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Datei in Benutzung (Player): Angaben bleiben, damit später erneut gelöscht werden kann
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
        }

        foreach (var scene in video.Scenes)
            scene.ClearAudio();
        video.ClearCombinedAudio();
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // ---------------- Export ----------------

    /// <summary>„8 Folien · ca. 3 Min. · erstellt am 26.11.2026“ in der aktuellen Sprache.</summary>
    private static string ExportMetaLine(VideoDocument video) =>
        string.Join(" · ",
            Loc.Format(ExportStrings.Export_SlideCount, ("count", video.Scenes.Count)),
            MediaFormat.Minutes(AudioScriptFormats.EstimateMinutes(video.WordCount)),
            ExportNames.CreatedOn(video.CreatedAt));

    public string ExportToText(VideoDocument video, CultureInfo culture)
    {
        using var scope = CultureScope.Use(culture);
        var sb = new StringBuilder();
        sb.AppendLine(video.Title);
        sb.AppendLine(ExportMetaLine(video));
        for (var i = 0; i < video.Scenes.Count; i++)
        {
            sb.AppendLine();
            sb.AppendLine(ReportVideoScripts.SceneHeading(video, i));
            sb.AppendLine(video.Scenes[i].Narration);
        }
        return sb.ToString();
    }

    public string ExportToMarkdown(VideoDocument video, CultureInfo culture)
    {
        using var scope = CultureScope.Use(culture);
        var sb = new StringBuilder();
        sb.AppendLine($"# {video.Title}");
        sb.AppendLine();
        sb.AppendLine($"*{ExportMetaLine(video)}*");
        for (var i = 0; i < video.Scenes.Count; i++)
        {
            sb.AppendLine();
            sb.AppendLine($"## {ReportVideoScripts.SceneHeading(video, i)}");
            sb.AppendLine();
            sb.AppendLine(video.Scenes[i].Narration);
        }
        return sb.ToString();
    }
}
