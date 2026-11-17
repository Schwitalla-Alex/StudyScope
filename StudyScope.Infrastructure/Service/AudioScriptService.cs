using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;

namespace StudyScope.Infrastructure.Services;

/// <summary>
/// Audio-Zusammenfassung: Sprechskript per KI (Zusammenfassung, Podcast-Gespräch, Kurz-Briefing) in gut hörbarem Deutsch,
/// nachsichtige Auswertung (JSON mit deutschen Schlüsseln, Sprecherzeilen, reiner Text), Speicherung aller Beiträge eines
/// Profils unter <c>Audio/&lt;Profil&gt;.json</c> samt Audiodateien unter <c>Audio/&lt;Profil&gt;/</c> und Skript-Export.
/// </summary>
public partial class AudioScriptService : IAudioScriptService
{
    private const int MaxTitleLength = 200;

    /// <summary>Höchstzahl an Sprechern in einem Skript (Gemini TTS kann zwei Stimmen je Anfrage).</summary>
    public const int MaxSpeakers = 2;

    private static readonly string[] TitleKeys = { "title", "titel", "name", "überschrift", "ueberschrift", "thema" };
    private static readonly string[] SegmentsKeys =
    {
        "segments", "segmente", "abschnitte", "sections", "dialog", "dialogue", "script", "skript", "parts", "teile",
        "lines", "zeilen", "beitraege", "beiträge", "absaetze", "absätze", "paragraphs", "turns", "redebeiträge", "redebeitraege"
    };
    private static readonly string[] SpeakerKeys = { "speaker", "sprecher", "sprecherin", "rolle", "role", "person", "stimme", "voice", "name" };
    private static readonly string[] TextKeys = { "text", "inhalt", "content", "line", "zeile", "satz", "aussage", "beitrag", "gesagt", "spoken", "absatz" };

    /// <summary>Sprecherbezeichnungen, die im reinen Text auch bei einmaligem Vorkommen als Sprecher gelten.</summary>
    private static readonly string[] KnownSpeakerWords =
    {
        "sprecher", "sprecherin", "moderator", "moderatorin", "experte", "expertin", "host", "gast", "erzähler", "erzählerin",
        "interviewer", "interviewerin", "a", "b", "sprecher 1", "sprecher 2", "sprecherin 1", "sprecherin 2"
    };

    private readonly ILlmService _llmService;
    private readonly string _baseDirectory;

    /// <param name="baseDirectory">Basisordner für die Speicherung (Tests); Standard ist <see cref="AppPaths.UserDataDirectory"/>.</param>
    public AudioScriptService(ILlmService llmService, string? baseDirectory = null)
    {
        _llmService = llmService;
        _baseDirectory = baseDirectory ?? AppPaths.UserDataDirectory;
    }

    // ---------------- Erzeugung ----------------

    public async Task<AudioScriptResult> GenerateAsync(
        IEnumerable<SourceChunk> chunks,
        AudioScriptFormat format,
        AudioScriptLength length,
        string topic,
        string fallbackTitle,
        CancellationToken cancellationToken = default)
    {
        var selected = SelectChunks(chunks.ToList(), topic, length);
        if (selected.Count == 0)
            return new AudioScriptResult { FailureReason = MindmapFailureReason.NoContent };

        var prompt = BuildPrompt(format, length, topic);
        var raw = await _llmService.AskQuestionAsync(prompt, selected.Select(c => c.Text), cancellationToken);
        var result = AnalyzeResponse(raw, format, fallbackTitle);

        if (result.Summary != null)
        {
            result.Summary.Format = format;
            result.Summary.Length = length;
            result.Summary.Topic = topic?.Trim() ?? string.Empty;
            result.Summary.SourceDocuments = selected.Select(c => c.SourceDocument).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        return result;
    }

    /// <summary>Höchstzahl an Textstellen je Länge (mehr Stoff für längere Beiträge, begrenzt für lokale Modelle).</summary>
    public static int MaxChunks(AudioScriptLength length) => length switch
    {
        AudioScriptLength.Short => 8,
        AudioScriptLength.Long => 16,
        _ => 12
    };

    /// <summary>Textstellen: mit Schwerpunkt per Stichwort-Treffer, sonst gleichmäßig über die Dokumente verteilt.</summary>
    public static List<SourceChunk> SelectChunks(IReadOnlyList<SourceChunk> chunks, string? topic, AudioScriptLength length) =>
        string.IsNullOrWhiteSpace(topic)
            ? ChunkSelector.SelectRepresentative(chunks, MaxChunks(length), c => c.Text)
            : ChunkSelector.SelectByKeywords(chunks, topic, 0, MaxChunks(length), c => c.Text);

    /// <summary>
    /// Prompt für das Sprechskript. Beginnt mit „Erstelle ein Audio-Skript“ (Erkennung der Fake-KI) und verlangt ein
    /// JSON-Array mit genau einem Objekt (der Dispatcher gibt bei „JSON-Array“ für einige Provider den JSON-Modus vor).
    /// </summary>
    public static string BuildPrompt(AudioScriptFormat format, AudioScriptLength length, string? topic)
    {
        var speakers = AudioScriptFormats.Speakers(format);
        var words = AudioScriptFormats.TargetWords(length);
        var sb = new StringBuilder();

        sb.Append("Erstelle ein Audio-Skript (Sprechtext zum Vorlesen) aus den bereitgestellten Textauszügen.\n");
        sb.Append("Format: ").Append(AudioScriptFormats.DisplayName(format)).Append(" – ").Append(FormatInstruction(format)).Append('\n');
        sb.Append("Länge: etwa ").Append(words).Append(" Wörter (ca. ").Append(AudioScriptFormats.TargetMinutes(length)).Append(" Minuten Sprechzeit).\n");
        sb.Append(speakers.Count == 1
            ? $"Sprecher: genau einer, \"speaker\" ist immer \"{speakers[0]}\".\n"
            : $"Sprecher: genau zwei, \"speaker\" ist immer \"{speakers[0]}\" oder \"{speakers[1]}\"; sie wechseln sich ab.\n");
        if (!string.IsNullOrWhiteSpace(topic))
            sb.Append("Schwerpunkt: Konzentriere dich auf das Thema „").Append(topic.Trim()).Append("“.\n");

        sb.Append("Antworte AUSSCHLIESSLICH als gültiges JSON-Array mit genau EINEM Objekt, ohne Einleitung, ohne Markdown und ohne Schlusssatz.\n");
        sb.Append("Schema:\n");
        sb.Append("[{\"title\": \"Titel des Beitrags\", \"segments\": [{\"speaker\": \"").Append(speakers[0]).Append("\", \"text\": \"Gesprochener Text\"}]}]\n");
        sb.Append("Regeln für den Sprechtext:\n");
        sb.Append("- Natürliches, gesprochenes Deutsch in ganzen, eher kurzen Sätzen – so, wie man es laut vorliest.\n");
        sb.Append("- Keine Aufzählungszeichen, keine Nummerierungen, keine Überschriften, kein Markdown im Text.\n");
        sb.Append("- Abkürzungen ausschreiben (zum Beispiel statt z. B., das heißt statt d. h., Prozent statt %). Zahlen, Daten und Einheiten so schreiben, wie man sie spricht.\n");
        sb.Append("- Keine Formeln, Tabellen, Code oder Literaturangaben vorlesen – erkläre stattdessen in Worten, was sie aussagen.\n");
        sb.Append("- Verwende NUR Inhalte aus den Auszügen. Nichts erfinden; was nicht in den Auszügen steht, weglassen.\n");
        sb.Append("- Kurzer Einstieg, der sagt, worum es geht, und ein kurzes Fazit am Ende.\n");
        sb.Append("- Jedes Segment umfasst ein bis fünf Sätze.\n");
        return sb.ToString();
    }

    public static string FormatInstruction(AudioScriptFormat format) => format switch
    {
        AudioScriptFormat.Podcast =>
            $"ein lebendiges Gespräch: Die {AudioScriptFormats.HostName} führt durch das Thema und stellt verständliche Fragen, " +
            $"der {AudioScriptFormats.ExpertName} erklärt anschaulich mit Beispielen aus den Auszügen. Beide sprechen sich nicht mit erfundenen Namen an.",
        AudioScriptFormat.Briefing =>
            "ein kompaktes Briefing: die wichtigsten Kernaussagen und Merksätze knapp und klar, ohne Ausschmückung.",
        _ => "eine zusammenhängende, verständliche Zusammenfassung der wichtigsten Inhalte mit roter Linie."
    };

    // ---------------- Auswertung ----------------

    /// <summary>
    /// Wertet die KI-Antwort aus: JSON (auch mit deutschen Schlüsseln, Hüllen, Segmenten als Text oder „Sprecher: Text“,
    /// abgeschnittene Antworten werden repariert) oder reiner Text (ein Segment je Absatz bzw. Sprecherzeile).
    /// Die Sprecher werden auf die Namen des Formats abgebildet.
    /// </summary>
    public static AudioScriptResult AnalyzeResponse(string? rawResponse, AudioScriptFormat format, string fallbackTitle)
    {
        var raw = rawResponse ?? string.Empty;
        if (raw.Trim() == LlmServiceDispatcher.CancelledResponse)
            return new AudioScriptResult { RawResponse = raw, FailureReason = MindmapFailureReason.EmptyResponse };

        var parsed = LenientJson.Analyze(raw, element => BuildSummary(element, fallbackTitle));
        AudioSummary? summary = parsed.Value;
        var wasPlainText = false;

        if (summary == null && parsed.FailureReason == MindmapFailureReason.NoJson)
        {
            summary = ParsePlainText(raw, fallbackTitle);
            wasPlainText = summary != null;
        }

        if (summary == null)
        {
            return new AudioScriptResult
            {
                RawResponse = raw,
                FailureReason = parsed.FailureReason == MindmapFailureReason.NoJson ? MindmapFailureReason.NoTopics : parsed.FailureReason
            };
        }

        Finish(summary, format);
        if (summary.Segments.Count == 0)
            return new AudioScriptResult { RawResponse = raw, FailureReason = MindmapFailureReason.NoTopics };

        return new AudioScriptResult
        {
            Summary = summary,
            RawResponse = raw,
            WasRepaired = parsed.WasRepaired,
            WasPlainText = wasPlainText,
            FailureReason = MindmapFailureReason.None
        };
    }

    private static bool IsScript(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object && LenientJson.GetProperty(element, SegmentsKeys) is { ValueKind: JsonValueKind.Array or JsonValueKind.String };

    private static bool IsSegmentObject(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object && !IsScript(element) &&
        (LenientJson.HasAny(element, TextKeys) || element.EnumerateObject().Count() == 1);

    private static AudioSummary? BuildSummary(JsonElement element, string fallbackTitle)
    {
        // Hülle aus dem JSON-Modus ({"items": [...]}, {"skript": {...}}) auspacken
        element = LenientJson.Unwrap(element, e => IsScript(e) || LenientJson.IsArrayOf(e, IsScript));

        var title = string.Empty;
        JsonElement segmentsElement;

        if (element.ValueKind == JsonValueKind.Array)
        {
            var script = element.EnumerateArray().FirstOrDefault(IsScript);
            if (script.ValueKind == JsonValueKind.Object)
            {
                element = script;
            }
            else
            {
                // Direkt eine Liste von Segmenten (Objekte oder Texte)
                return Create(title, ReadSegments(element), fallbackTitle);
            }
        }

        if (element.ValueKind != JsonValueKind.Object)
            return null;

        title = LenientJson.GetString(element, TitleKeys);
        var segmentsValue = LenientJson.GetProperty(element, SegmentsKeys);
        if (segmentsValue is { ValueKind: JsonValueKind.String } scriptText)
        {
            // {"title": "…", "skript": "ganzer Text"} → wie reiner Text auswerten
            var fromText = ParsePlainText(scriptText.GetString(), fallbackTitle);
            if (fromText != null && title.Length > 0)
                fromText.Title = Clean(title, MaxTitleLength);
            return fromText;
        }

        if (segmentsValue is not { ValueKind: JsonValueKind.Array } array)
        {
            // Einzelnes Segment oder Hülle ohne bekannten Schlüssel: erstes Array mit Segmenten suchen
            var inner = element.EnumerateObject()
                .Select(p => p.Value)
                .FirstOrDefault(v => LenientJson.IsArrayOf(v, s => IsSegmentObject(s) || s.ValueKind == JsonValueKind.String));
            if (inner.ValueKind != JsonValueKind.Array)
                return null;
            array = inner;
        }

        segmentsElement = array;
        return Create(title, ReadSegments(segmentsElement), fallbackTitle);
    }

    private static AudioSummary? Create(string title, List<AudioSegment> segments, string fallbackTitle) =>
        segments.Count == 0
            ? null
            : new AudioSummary { Title = TitleOrFallback(title, fallbackTitle), Segments = segments };

    private static List<AudioSegment> ReadSegments(JsonElement array)
    {
        // Texte wie „Moderatorin: …“: als Sprecher gelten bekannte Bezeichnungen oder Namen, die mehrfach vorkommen
        var speakerCandidates = SpeakerCandidates(array.EnumerateArray()
            .Where(i => i.ValueKind == JsonValueKind.String)
            .Select(i => i.GetString() ?? string.Empty));

        var result = new List<AudioSegment>();
        foreach (var item in array.EnumerateArray())
        {
            switch (item.ValueKind)
            {
                case JsonValueKind.String:
                    result.Add(SplitSpeakerLine(item.GetString() ?? string.Empty, speakerCandidates));
                    break;

                case JsonValueKind.Object:
                    var text = LenientJson.GetString(item, TextKeys);
                    var speaker = LenientJson.GetString(item, SpeakerKeys);
                    if (text.Length == 0 && item.EnumerateObject().Count() == 1)
                    {
                        // {"Moderatorin": "Text"}
                        var only = item.EnumerateObject().First();
                        if (only.Value.ValueKind == JsonValueKind.String)
                        {
                            speaker = only.Name;
                            text = only.Value.GetString() ?? string.Empty;
                        }
                    }
                    if (text.Length > 0)
                        result.Add(new AudioSegment { Speaker = speaker, Text = text });
                    break;
            }

            if (result.Count >= AudioSummary.MaxSegments)
                break;
        }

        return result;
    }

    /// <summary>
    /// Reiner Text: Titelzeile („# …“ oder „Titel: …“) wird erkannt, Zeilen „Sprecher: Text“ werden zu Segmenten
    /// dieses Sprechers, sonst entsteht je Absatz ein Segment.
    /// </summary>
    public static AudioSummary? ParsePlainText(string? text, string fallbackTitle)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            .Where(l => !l.TrimStart().StartsWith("```", StringComparison.Ordinal))
            .ToList();

        var title = string.Empty;
        var firstContent = lines.FindIndex(l => l.Trim().Length > 0);
        if (firstContent >= 0)
        {
            var first = lines[firstContent].Trim();
            var titleMatch = TitleLine().Match(first);
            if (titleMatch.Success)
            {
                title = titleMatch.Groups["title"].Value;
                lines.RemoveAt(firstContent);
            }
        }

        var candidates = SpeakerCandidates(lines);

        var segments = new List<AudioSegment>();
        var current = new StringBuilder();
        var currentSpeaker = string.Empty;

        void Flush()
        {
            var t = current.ToString().Trim();
            if (t.Length > 0 && segments.Count < AudioSummary.MaxSegments)
                segments.Add(new AudioSegment { Speaker = currentSpeaker, Text = t });
            current.Clear();
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                Flush();
                continue;
            }

            var match = SpeakerLine().Match(line);
            if (match.Success && candidates.Contains(match.Groups["speaker"].Value.Trim()))
            {
                Flush();
                currentSpeaker = match.Groups["speaker"].Value.Trim();
                current.Append(match.Groups["text"].Value.Trim());
                continue;
            }

            // Zwischenüberschriften („## Teil 1“) werden nicht vorgelesen
            if (line.StartsWith('#'))
            {
                Flush();
                continue;
            }

            if (current.Length > 0) current.Append(' ');
            current.Append(line);
        }

        Flush();
        return Create(title, segments, fallbackTitle);
    }

    private static bool IsKnownSpeaker(string name) =>
        KnownSpeakerWords.Contains(name.Trim().ToLowerInvariant());

    /// <summary>
    /// Sprechernamen am Zeilenanfang („Name: …“): bekannte Bezeichnungen oder Namen, die mindestens zweimal vorkommen
    /// (damit „Merke: …“ oder „Beispiel: …“ nicht als Sprecher gelten).
    /// </summary>
    private static HashSet<string> SpeakerCandidates(IEnumerable<string> lines) =>
        lines
            .Select(l => SpeakerLine().Match(l.Trim()))
            .Where(m => m.Success)
            .GroupBy(m => m.Groups["speaker"].Value.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() >= 2 || IsKnownSpeaker(g.Key))
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>„Moderatorin: Text“ → Sprecher und Text; ohne erkennbaren Sprecher bleibt der Sprecher leer.</summary>
    private static AudioSegment SplitSpeakerLine(string line, HashSet<string> candidates)
    {
        var match = SpeakerLine().Match(line.Trim());
        if (match.Success && candidates.Contains(match.Groups["speaker"].Value.Trim()))
            return new AudioSegment { Speaker = match.Groups["speaker"].Value.Trim(), Text = match.Groups["text"].Value };
        return new AudioSegment { Text = line };
    }

    /// <summary>Bereinigt die Texte, teilt zu lange Segmente und bildet die Sprecher auf die Namen des Formats ab.</summary>
    private static void Finish(AudioSummary summary, AudioScriptFormat format)
    {
        var cleaned = new List<AudioSegment>();
        foreach (var segment in summary.Segments)
        {
            var text = CleanSpokenText(segment.Text);
            if (text.Length == 0)
                continue;

            foreach (var part in SplitLongText(text, AudioSummary.MaxSegmentLength))
                cleaned.Add(new AudioSegment { Speaker = Clean(segment.Speaker, 60).Trim('*', '_', ' '), Text = part });
        }

        summary.Segments = cleaned.Take(AudioSummary.MaxSegments).ToList();
        NormalizeSpeakers(summary.Segments, AudioScriptFormats.Speakers(format));
    }

    /// <summary>
    /// Ein Sprecher: alle Segmente bekommen den Erzählernamen. Zwei Sprecher: passende Namen bleiben, andere werden in
    /// der Reihenfolge ihres Auftretens den noch freien Namen zugeordnet (weitere abwechselnd); fehlende Sprecher
    /// übernehmen den vorherigen.
    /// </summary>
    public static void NormalizeSpeakers(List<AudioSegment> segments, IReadOnlyList<string> expected)
    {
        if (segments.Count == 0 || expected.Count == 0)
            return;

        if (expected.Count == 1)
        {
            foreach (var s in segments) s.Speaker = expected[0];
            return;
        }

        var names = segments.Select(s => s.Speaker.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in expected)
            map[name] = name;

        var free = expected.Where(e => !names.Contains(e, StringComparer.OrdinalIgnoreCase)).ToList();
        var unknown = names.Where(n => !map.ContainsKey(n)).ToList();
        for (var i = 0; i < unknown.Count; i++)
            map[unknown[i]] = i < free.Count ? free[i] : expected[(i - free.Count) % expected.Count];

        var previous = string.Empty;
        foreach (var segment in segments)
        {
            var name = segment.Speaker.Trim();
            if (name.Length == 0)
            {
                // Ohne Angabe: abwechselnd zum vorherigen Sprecher (Gespräch), am Anfang der erste
                segment.Speaker = previous.Length == 0 ? expected[0] : expected.First(e => e != previous);
            }
            else
            {
                segment.Speaker = map[name];
            }
            previous = segment.Speaker;
        }
    }

    /// <summary>
    /// Entfernt, was nicht vorgelesen werden soll: Aufzählungszeichen, Nummerierungen, Markdown-Hervorhebungen,
    /// Quellenmarkierungen, Regieanweisungen in eckigen Klammern; mehrfache Leerzeichen werden zusammengefasst.
    /// </summary>
    public static string CleanSpokenText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var lines = text.Replace("\r\n", "\n").Split('\n')
            .Select(l => BulletPrefix().Replace(l.Trim(), string.Empty))
            .Where(l => l.Length > 0);
        var joined = string.Join(" ", lines);
        joined = SourceMarker().Replace(joined, " ");
        joined = joined.Replace("**", string.Empty).Replace("__", string.Empty).Replace("`", string.Empty);
        joined = MultiSpace().Replace(joined, " ").Trim();
        return joined;
    }

    /// <summary>Teilt einen zu langen Text an Satzenden (notfalls hart) in Stücke von höchstens <paramref name="maxLength"/> Zeichen.</summary>
    public static IEnumerable<string> SplitLongText(string text, int maxLength)
    {
        var rest = text.Trim();
        while (rest.Length > maxLength)
        {
            var cut = rest.LastIndexOfAny(new[] { '.', '!', '?' }, maxLength - 1);
            if (cut < maxLength / 3)
                cut = rest.LastIndexOf(' ', maxLength - 1);
            if (cut <= 0)
                cut = maxLength - 1;

            yield return rest[..(cut + 1)].Trim();
            rest = rest[(cut + 1)..].Trim();
        }

        if (rest.Length > 0)
            yield return rest;
    }

    private static string TitleOrFallback(string title, string fallbackTitle) =>
        Clean(title, MaxTitleLength) is { Length: > 0 } t ? t : (string.IsNullOrWhiteSpace(fallbackTitle) ? "Audio-Zusammenfassung" : fallbackTitle.Trim());

    private static string Clean(string? text, int maxLength)
    {
        var value = MultiSpace().Replace((text ?? string.Empty).Replace('\n', ' '), " ").Trim().Trim('#', '*').Trim();
        return value.Length > maxLength ? value[..maxLength].TrimEnd() : value;
    }

    /// <summary>Titelzeile: „# Titel“ oder „Titel: …“ (auch fett).</summary>
    [GeneratedRegex(@"^(?:#{1,3}\s+(?:Titel:\s*)?|\**Titel:\s*\**)(?<title>.+?)\**$", RegexOptions.IgnoreCase)]
    private static partial Regex TitleLine();

    /// <summary>„Moderatorin: Text“, auch „**Experte:** Text“ oder „- Sprecher 1: Text“.</summary>
    [GeneratedRegex(@"^(?:[-*•]\s*)?\**(?<speaker>[A-Za-zÄÖÜäöüß][A-Za-zÄÖÜäöüß0-9 .\-]{0,28}?)\**\s*:\s*\**\s*(?<text>\S.*)$")]
    private static partial Regex SpeakerLine();

    [GeneratedRegex(@"^(?:[-*•–]\s+|\d{1,2}[.)]\s+)")]
    private static partial Regex BulletPrefix();

    [GeneratedRegex(@"\[Quelle:[^\]]*\]", RegexOptions.IgnoreCase)]
    private static partial Regex SourceMarker();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultiSpace();

    // ---------------- Speicherung ----------------

    public async Task<AudioLibrary> LoadLibraryAsync(string profileName)
    {
        var path = GetFilePath(profileName);
        if (!File.Exists(path))
            return new AudioLibrary();

        try
        {
            await using var stream = File.OpenRead(path);
            var library = await JsonSerializer.DeserializeAsync<AudioLibrary>(stream) ?? new AudioLibrary();
            library.Items ??= new List<AudioSummary>();
            library.Items.RemoveAll(i => i == null);
            foreach (var item in library.Items)
            {
                item.Segments ??= new List<AudioSegment>();
                item.Segments.RemoveAll(s => s == null);
                foreach (var segment in item.Segments)
                {
                    segment.Speaker ??= string.Empty;
                    segment.Text ??= string.Empty;
                }
                item.SourceDocuments ??= new List<string>();
                item.AudioFileName ??= string.Empty;
                item.Title ??= string.Empty;
                item.Topic ??= string.Empty;
            }

            return library;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new AudioLibrary();
        }
    }

    public async Task SaveLibraryAsync(string profileName, AudioLibrary library)
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

    public string GetAudioDirectory(string profileName)
    {
        var directory = Path.Combine(_baseDirectory, "Audio", JsonStorageService.ToSafeProfileFileName(profileName));
        Directory.CreateDirectory(directory);
        return directory;
    }

    public string? GetAudioFilePath(string profileName, AudioSummary summary)
    {
        if (string.IsNullOrWhiteSpace(summary.AudioFileName))
            return null;

        // Nur der Dateiname zählt – kein Ausbrechen aus dem Audio-Ordner über „..\“
        var path = Path.Combine(_baseDirectory, "Audio", JsonStorageService.ToSafeProfileFileName(profileName), Path.GetFileName(summary.AudioFileName));
        return File.Exists(path) ? path : null;
    }

    public void DeleteAudioFile(string profileName, AudioSummary summary)
    {
        var path = GetAudioFilePath(profileName, summary);
        if (path != null)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Datei gerade in Benutzung (z. B. im Player): beim nächsten Löschen erneut versuchen
                return;
            }
        }

        summary.AudioFileName = string.Empty;
    }

    private string GetFilePath(string profileName) =>
        Path.Combine(_baseDirectory, "Audio", $"{JsonStorageService.ToSafeProfileFileName(profileName)}.json");

    // ---------------- Export ----------------

    private static bool ShowSpeakers(AudioSummary summary) => summary.Speakers.Count > 1;

    private static string MetaLine(AudioSummary summary)
    {
        var parts = new List<string>
        {
            AudioScriptFormats.DisplayName(summary.Format),
            AudioScriptFormats.FormatMinutes(summary.EstimatedMinutes)
        };
        if (!string.IsNullOrWhiteSpace(summary.Topic))
            parts.Add($"Schwerpunkt: {summary.Topic}");
        parts.Add($"erstellt am {summary.CreatedAt:dd.MM.yyyy}");
        return string.Join(" · ", parts);
    }

    public string ExportToText(AudioSummary summary)
    {
        var sb = new StringBuilder();
        sb.AppendLine(summary.Title);
        sb.AppendLine(MetaLine(summary));
        if (summary.SourceDocuments.Count > 0)
            sb.AppendLine($"Quellen: {string.Join(", ", summary.SourceDocuments)}");

        var withSpeakers = ShowSpeakers(summary);
        foreach (var segment in summary.Segments)
        {
            sb.AppendLine();
            sb.AppendLine(withSpeakers ? $"{segment.Speaker}: {segment.Text}" : segment.Text);
        }

        return sb.ToString();
    }

    public string ExportToMarkdown(AudioSummary summary)
    {
        var sb = new StringBuilder();
        sb.Append("# ").AppendLine(EscapeMarkdown(summary.Title));
        sb.AppendLine();
        sb.Append('*').Append(EscapeMarkdown(MetaLine(summary))).AppendLine("*");
        if (summary.SourceDocuments.Count > 0)
        {
            sb.AppendLine();
            sb.Append("*Quellen: ").Append(EscapeMarkdown(string.Join(", ", summary.SourceDocuments))).AppendLine("*");
        }

        var withSpeakers = ShowSpeakers(summary);
        foreach (var segment in summary.Segments)
        {
            sb.AppendLine();
            sb.AppendLine(withSpeakers
                ? $"**{EscapeMarkdown(segment.Speaker)}:** {EscapeMarkdown(segment.Text)}"
                : EscapeMarkdown(segment.Text));
        }

        return sb.ToString();
    }

    private static string EscapeMarkdown(string text) =>
        text.Replace("\\", "\\\\").Replace("*", "\\*").Replace("_", "\\_").Replace("#", "\\#");
}
