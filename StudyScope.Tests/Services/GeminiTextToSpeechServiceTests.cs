using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.Services;

namespace StudyScope.Tests.Services;

/// <summary>
/// Tests für Gemini TTS ohne Netz (HttpMessageHandler-Attrappe): Aufteilung langer Skripte, Anfrageformat
/// (ein bzw. zwei Sprecher), Zusammenfügen zu einer WAV-Datei mit Startzeiten, Fortschritt, keine halbe Datei bei
/// Abbruch/Fehler, deutsche Fehlermeldungen und „Verbindung testen“ über die kostenlose Modellabfrage.
/// </summary>
[TestFixture]
public class GeminiTextToSpeechServiceTests
{
    private const string DummyKey = "DUMMY_TEST_KEY_GEMINI_TTS_123";
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "StudyScopeGeminiTts_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static SpeechContext Context(string ownKey = DummyKey, AiConfig? chat = null, string model = "") =>
        new(new SpeechSettings { Provider = SpeechProviderType.GeminiTts, GeminiApiKey = ownKey, GeminiModel = model }, chat);

    private static AudioSummary Summary(AudioScriptFormat format, params (string Speaker, string Text)[] segments) => new()
    {
        Title = "Testbeitrag",
        Format = format,
        Segments = segments.Select(s => new AudioSegment { Speaker = s.Speaker, Text = s.Text }).ToList()
    };

    /// <summary>WAV mit Kopf: 24 kHz, mono, 16 Bit, <paramref name="seconds"/> Sekunden Stille.</summary>
    private static byte[] Wav(double seconds, int rate = 24000)
    {
        using var stream = new MemoryStream();
        var data = new byte[(int)(rate * 2 * seconds)];
        WavAudio.WriteHeader(stream, rate, 1, 16, data.Length);
        stream.Write(data);
        return stream.ToArray();
    }

    private static string AudioResponse(byte[] audio, string mime = "audio/wav") => new JsonObject
    {
        ["id"] = "test",
        ["steps"] = new JsonArray(
            new JsonObject { ["type"] = "user_input" },
            new JsonObject
            {
                ["type"] = "model_output",
                ["content"] = new JsonArray(new JsonObject { ["type"] = "audio", ["mime_type"] = mime, ["data"] = Convert.ToBase64String(audio) })
            })
    }.ToJsonString();

    private static string Error(int code, string status, string message, string? reason = null)
    {
        var error = new JsonObject { ["code"] = code, ["message"] = message, ["status"] = status };
        if (reason != null)
            error["details"] = new JsonArray(new JsonObject { ["@type"] = "type.googleapis.com/google.rpc.ErrorInfo", ["reason"] = reason });
        return new JsonObject { ["error"] = error }.ToJsonString();
    }

    private (GeminiTextToSpeechService Service, FakeHandler Handler) Create(params Func<HttpRequestMessage, string, HttpResponseMessage>[] responses)
    {
        var handler = new FakeHandler(responses);
        return (new GeminiTextToSpeechService(new HttpClient(handler), TimeSpan.Zero), handler);
    }

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Status(int code, string body) => new((HttpStatusCode)code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    // ---------- Aufteilung und Anfrage ----------

    [Test]
    public void BuildParts_GroupsSegments_AndSplitsLongOnes()
    {
        var longText = string.Join(" ", Enumerable.Repeat("Ein ziemlich langer Satz über Zellen.", 30)); // ~1100 Zeichen
        var summary = Summary(AudioScriptFormat.Podcast,
            ("Moderatorin", "Kurze Frage?"), ("Experte", longText), ("Moderatorin", "Danke."));

        var parts = GeminiTextToSpeechService.BuildParts(summary, maxCharacters: 500);

        Assert.That(parts.All(p => p.Sum(x => x.Text.Length) <= 500 + 2 * p.Count), Is.True);
        Assert.That(parts.SelectMany(p => p).Select(x => x.SegmentIndex).Distinct(), Is.EqualTo(new[] { 0, 1, 2 }));
        Assert.That(parts.SelectMany(p => p).Count(x => x.SegmentIndex == 1), Is.GreaterThan(1), "langes Segment geteilt");
        Assert.That(parts.SelectMany(p => p).Where(x => x.SegmentIndex == 1).All(x => x.Slot == 1), Is.True, "Experte = Platz 1");
        Assert.That(string.Join(" ", parts.SelectMany(p => p).Where(x => x.SegmentIndex == 1).Select(x => x.Text)), Is.EqualTo(longText));
    }

    [Test]
    public void BuildRequestBody_SingleSpeaker_UsesVoiceListAndOneTextItem()
    {
        var pieces = new[] { new GeminiTextToSpeechService.SpeechPiece(0, 0, "Erster Absatz."), new GeminiTextToSpeechService.SpeechPiece(1, 0, "Zweiter.") };

        var body = GeminiTextToSpeechService.BuildRequestBody("gemini-test-tts", pieces, false, "Kore", "Puck");

        Assert.That(body["model"]!.GetValue<string>(), Is.EqualTo("gemini-test-tts"));
        Assert.That(body["response_format"]!["type"]!.GetValue<string>(), Is.EqualTo("audio"));
        var config = body["generation_config"]!["speech_config"]!.AsArray();
        Assert.That(config.Single()!["voice"]!.GetValue<string>(), Is.EqualTo("Kore"));
        var content = body["input"]![0]!["content"]!.AsArray();
        Assert.That(body["input"]![0]!["type"]!.GetValue<string>(), Is.EqualTo("user_input"));
        Assert.That(content.Count, Is.EqualTo(1));
        Assert.That(content[0]!["text"]!.GetValue<string>(), Is.EqualTo("Erster Absatz.\n\nZweiter."));
        Assert.That(content[0]!["annotations"]![0]!["type"]!.GetValue<string>(), Is.EqualTo("speech_metadata"));
    }

    [Test]
    public void BuildRequestBody_TwoSpeakers_OneItemPerTurn_WithSpeakerVoices()
    {
        var pieces = new[]
        {
            new GeminiTextToSpeechService.SpeechPiece(0, 0, "Hallo."),
            new GeminiTextToSpeechService.SpeechPiece(1, 1, "Guten Tag."),
            new GeminiTextToSpeechService.SpeechPiece(1, 1, "Weiter im Text."),
            new GeminiTextToSpeechService.SpeechPiece(2, 0, "Danke.")
        };

        var body = GeminiTextToSpeechService.BuildRequestBody("m", pieces, true, "Kore", "Puck");

        var speakers = body["generation_config"]!["speech_config"]!["speakers"]!.AsArray();
        Assert.That(speakers.Select(s => $"{s!["speaker"]}={s["voice"]}"), Is.EqualTo(new[] { "Moderatorin=Kore", "Experte=Puck" }));
        var content = body["input"]![0]!["content"]!.AsArray();
        Assert.That(content.Count, Is.EqualTo(3), "aufeinanderfolgende Stücke desselben Sprechers zusammengefasst");
        Assert.That(content.Select(c => c!["annotations"]![0]!["speaker"]!.GetValue<string>()), Is.EqualTo(new[] { "Moderatorin", "Experte", "Moderatorin" }));
        Assert.That(content[1]!["text"]!.GetValue<string>(), Is.EqualTo("Guten Tag.\nWeiter im Text."));
        Assert.That(content.All(c => !c!["text"]!.GetValue<string>().Contains("Moderatorin:")), Is.True, "keine Sprecher-Präfixe im Text");
    }

    // ---------- Erzeugen ----------

    [Test]
    public async Task Synthesize_JoinsPartsToWav_WithStartsAndProgress_KeyOnlyInHeader()
    {
        var text = string.Join(" ", Enumerable.Repeat("Ein Satz mit etwas Inhalt.", 80)); // ~2100 Zeichen
        var summary = Summary(AudioScriptFormat.Podcast, ("Moderatorin", "Willkommen."), ("Experte", text), ("Moderatorin", text), ("Experte", "Tschüss."));
        var (service, handler) = Create(
            (_, _) => Ok(AudioResponse(Wav(2.0))),
            (_, _) => Ok(AudioResponse(Wav(1.5))));
        var path = Path.Combine(_tempDir, "Audio", "beitrag.wav");
        var reports = new List<SpeechSynthesisProgress>();

        var result = await service.SynthesizeToFileAsync(summary, Context(), path, new SyncProgress(reports.Add));

        Assert.That(handler.Requests.Count, Is.EqualTo(GeminiTextToSpeechService.BuildParts(summary).Count));
        Assert.That(handler.Requests.Count, Is.EqualTo(2), "je ~2100 Zeichen: zwei Teile");
        Assert.That(handler.Requests.All(r => r.Uri == $"{GeminiTextToSpeechService.BaseUrl}/interactions"), Is.True);
        Assert.That(handler.Requests.All(r => r.ApiKey == DummyKey), Is.True);
        Assert.That(handler.Requests.All(r => !r.Uri.Contains(DummyKey)), Is.True, "Schlüssel nie in der URL");
        Assert.That(handler.Requests[0].Body, Does.Contain("\"model\":\"" + GeminiTtsInfo.DefaultModel + "\""));
        Assert.That(handler.Requests[0].Body, Does.Contain("\"speakers\""));

        // WAV: 2 + 1,5 s plus eine Pause
        var expected = 3.5 + GeminiTextToSpeechService.PauseBetweenPartsSeconds;
        Assert.That(result.DurationSeconds, Is.EqualTo(expected).Within(0.01));
        var decoded = WavAudio.Decode(await File.ReadAllBytesAsync(path));
        Assert.That(decoded.SampleRate, Is.EqualTo(24000));
        Assert.That(decoded.DurationSeconds, Is.EqualTo(expected).Within(0.01));
        Assert.That(File.Exists(path + ".part"), Is.False);

        Assert.That(result.SegmentStartSeconds.Count, Is.EqualTo(4));
        Assert.That(result.SegmentStartSeconds[0], Is.EqualTo(0));
        Assert.That(result.SegmentStartSeconds, Is.Ordered.Ascending);
        Assert.That(result.SegmentStartSeconds[3], Is.LessThan(result.DurationSeconds));
        Assert.That(result.CreatedWith, Does.Contain(GeminiTtsInfo.DefaultModel));

        Assert.That(result.SegmentStartSeconds[2], Is.EqualTo(2.0 + GeminiTextToSpeechService.PauseBetweenPartsSeconds).Within(0.01), "Teil 2 beginnt mit Segment 3");
        Assert.That(reports.Select(r => r.CompletedParts), Is.EqualTo(new[] { 0, 1, 2 }));
        Assert.That(reports.Last().Fraction, Is.EqualTo(1));
    }

    [Test]
    public async Task Synthesize_SingleSpeaker_RawPcmResponse_IsAccepted()
    {
        var summary = Summary(AudioScriptFormat.Summary, ("Sprecherin", "Ein kurzer Text."));
        var pcm = new byte[24000 * 2]; // 1 s
        var (service, handler) = Create((_, _) => Ok(AudioResponse(pcm, "audio/l16; rate=24000")));
        var path = Path.Combine(_tempDir, "kurz.wav");

        var result = await service.SynthesizeToFileAsync(summary, Context(model: "eigenes-tts-modell"), path);

        Assert.That(result.DurationSeconds, Is.EqualTo(1.0).Within(0.001));
        Assert.That(handler.Requests[0].Body, Does.Contain("\"speech_config\":[{\"voice\":\"Kore\"}]"));
        Assert.That(handler.Requests[0].Body, Does.Contain("eigenes-tts-modell"));
    }

    [Test]
    public async Task Synthesize_UsesChatKey_WhenChatIsGemini()
    {
        var summary = Summary(AudioScriptFormat.Summary, ("Sprecherin", "Text."));
        var (service, handler) = Create((_, _) => Ok(AudioResponse(Wav(0.5))));
        var chat = new AiConfig { SelectedProvider = AiProviderType.GoogleGemini, ApiKey = "DUMMY_TEST_KEY_CHAT" };

        await service.SynthesizeToFileAsync(summary, Context(ownKey: string.Empty, chat: chat), Path.Combine(_tempDir, "a.wav"));

        Assert.That(handler.Requests[0].ApiKey, Is.EqualTo("DUMMY_TEST_KEY_CHAT"));
    }

    [Test]
    public async Task Synthesize_ErrorInSecondPart_LeavesNoFile_AndKeepsExistingFile()
    {
        var text = string.Join(" ", Enumerable.Repeat("Satz.", 700));
        var summary = Summary(AudioScriptFormat.Summary, ("Sprecherin", text), ("Sprecherin", text));
        var (service, _) = Create(
            (_, _) => Ok(AudioResponse(Wav(1))),
            (_, _) => Status(429, Error(429, "RESOURCE_EXHAUSTED", "Quota exceeded for metric generate_requests_per_day.")));
        var path = Path.Combine(_tempDir, "vorhanden.wav");
        await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3 });

        var ex = Assert.ThrowsAsync<LlmServiceException>(() => service.SynthesizeToFileAsync(summary, Context(), path));

        Assert.That(ex!.Message, Does.Contain("Kontingent").And.Contain("HTTP 429").And.Contain("Tageslimits"));
        Assert.That(ex.Detail, Does.Contain("Quota exceeded"));
        Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(new byte[] { 1, 2, 3 }), "vorhandene Datei unverändert");
        Assert.That(File.Exists(path + ".part"), Is.False, "keine halbe Datei");
    }

    [Test]
    public async Task Synthesize_Cancelled_LeavesNoFile()
    {
        var text = string.Join(" ", Enumerable.Repeat("Satz.", 700));
        var summary = Summary(AudioScriptFormat.Summary, ("Sprecherin", text), ("Sprecherin", text));
        using var cts = new CancellationTokenSource();
        var (service, _) = Create(
            (_, _) => { cts.Cancel(); return Ok(AudioResponse(Wav(1))); },
            (_, _) => Ok(AudioResponse(Wav(1))));
        var path = Path.Combine(_tempDir, "abgebrochen.wav");

        Assert.CatchAsync<OperationCanceledException>(() => service.SynthesizeToFileAsync(summary, Context(), path, null, cts.Token));

        Assert.That(File.Exists(path), Is.False);
        Assert.That(File.Exists(path + ".part"), Is.False);
        await Task.CompletedTask;
    }

    [Test]
    public async Task Synthesize_Overloaded_RetriesOnce()
    {
        var summary = Summary(AudioScriptFormat.Summary, ("Sprecherin", "Text."));
        var (service, handler) = Create(
            (_, _) => Status(503, Error(503, "UNAVAILABLE", "The model is overloaded.")),
            (_, _) => Ok(AudioResponse(Wav(0.5))));

        var result = await service.SynthesizeToFileAsync(summary, Context(), Path.Combine(_tempDir, "b.wav"));

        Assert.That(handler.Requests.Count, Is.EqualTo(2));
        Assert.That(result.DurationSeconds, Is.EqualTo(0.5).Within(0.01));
    }

    [Test]
    public void Synthesize_ResponseWithoutAudio_GermanMessage()
    {
        var summary = Summary(AudioScriptFormat.Summary, ("Sprecherin", "Text."));
        var (service, _) = Create((_, _) => Ok("""{"id":"x","steps":[{"type":"model_output","content":[{"type":"text","text":"Ich kann das nicht."}]}]}"""));

        var ex = Assert.ThrowsAsync<LlmServiceException>(() => service.SynthesizeToFileAsync(summary, Context(), Path.Combine(_tempDir, "c.wav")));

        Assert.That(ex!.Message, Does.Contain("keine Audiodaten"));
    }

    [Test]
    public void Synthesize_WithoutKey_PointsToSettings()
    {
        var summary = Summary(AudioScriptFormat.Summary, ("Sprecherin", "Text."));
        var (service, handler) = Create();

        var ex = Assert.ThrowsAsync<LlmServiceException>(() => service.SynthesizeToFileAsync(summary, Context(ownKey: ""), Path.Combine(_tempDir, "d.wav")));

        Assert.That(ex!.Message, Does.Contain("Einstellungen › Sprachausgabe"));
        Assert.That(handler.Requests, Is.Empty);
    }

    // ---------- Fehlermeldungen ----------

    [Test]
    public void ErrorFromResponse_InvalidKey()
    {
        var ex = GeminiTextToSpeechService.ErrorFromResponse(400, Error(400, "INVALID_ARGUMENT", "API key not valid. Please pass a valid API key.", "API_KEY_INVALID"), "m");
        Assert.That(ex.Message, Does.Contain("Schlüssel wurde abgelehnt").And.Contain("Google AI Studio"));
        Assert.That(ex.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public void ErrorFromResponse_ModelNotFound_NamesModelAndDefault()
    {
        var ex = GeminiTextToSpeechService.ErrorFromResponse(404, Error(404, "NOT_FOUND", "models/gibt-es-nicht is not found."), "gibt-es-nicht");
        Assert.That(ex.Message, Does.Contain("„gibt-es-nicht“ wurde bei Gemini nicht gefunden").And.Contain(GeminiTtsInfo.DefaultModel));
    }

    [TestCase(403, "keinen Zugriff")]
    [TestCase(429, "Kontingent")]
    [TestCase(500, "überlastet")]
    [TestCase(400, "abgelehnt (HTTP 400)")]
    public void ErrorFromResponse_OtherStatus(int status, string expected)
    {
        var ex = GeminiTextToSpeechService.ErrorFromResponse(status, Error(status, "X", "Something went wrong."), "m");
        Assert.That(ex.Message, Does.Contain(expected));
        Assert.That(ex.Detail, Does.Contain("Something went wrong."));
    }

    [Test]
    public void ErrorFromResponse_Region()
    {
        var ex = GeminiTextToSpeechService.ErrorFromResponse(400, Error(400, "FAILED_PRECONDITION", "User location is not supported for the API use."), "m");
        Assert.That(ex.Message, Does.Contain("Region"));
    }

    // ---------- Verbindung testen ----------

    [Test]
    public async Task TestConnection_UsesFreeModelLookup()
    {
        var (service, handler) = Create((_, _) => Ok("""{"name":"models/gemini-3.8-flash-tts","displayName":"Gemini 3.8 Flash TTS"}"""));

        var message = await service.TestConnectionAsync(Context());

        Assert.That(handler.Requests.Single().Method, Is.EqualTo("GET"));
        Assert.That(handler.Requests.Single().Uri, Is.EqualTo($"{GeminiTextToSpeechService.BaseUrl}/models/{GeminiTtsInfo.DefaultModel}"));
        Assert.That(handler.Requests.Single().ApiKey, Is.EqualTo(DummyKey));
        Assert.That(message, Does.Contain("erfolgreich").And.Contain("keine Kosten").And.Contain("eigener Schlüssel"));
    }

    [Test]
    public void TestConnection_UnknownModel_And_InvalidKey()
    {
        var (service, _) = Create(
            (_, _) => Status(404, Error(404, "NOT_FOUND", "not found")),
            (_, _) => Status(400, Error(400, "INVALID_ARGUMENT", "API key not valid.", "API_KEY_INVALID")));

        var notFound = Assert.ThrowsAsync<LlmServiceException>(() => service.TestConnectionAsync(Context(model: "tippfehler-tts")));
        var badKey = Assert.ThrowsAsync<LlmServiceException>(() => service.TestConnectionAsync(Context()));

        Assert.That(notFound!.Message, Does.Contain("„tippfehler-tts“"));
        Assert.That(badKey!.Message, Does.Contain("Schlüssel wurde abgelehnt"));
    }

    [Test]
    public void TestConnection_NetworkError_German()
    {
        var (service, _) = Create((_, _) => throw new HttpRequestException("Host nicht gefunden"));

        var ex = Assert.ThrowsAsync<LlmServiceException>(() => service.TestConnectionAsync(Context()));

        Assert.That(ex!.Message, Does.Contain("nicht erreichbar"));
    }

    // ---------- Hilfsklassen ----------

    private sealed class SyncProgress(Action<SpeechSynthesisProgress> report) : IProgress<SpeechSynthesisProgress>
    {
        public void Report(SpeechSynthesisProgress value) => report(value);
    }

    private sealed record RecordedRequest(string Method, string Uri, string? ApiKey, string Body);

    private sealed class FakeHandler(Func<HttpRequestMessage, string, HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var key = request.Headers.TryGetValues("x-goog-api-key", out var values) ? values.Single() : null;
            Requests.Add(new RecordedRequest(request.Method.Method, request.RequestUri!.ToString(), key, body));
            var index = Math.Min(Requests.Count - 1, responses.Length - 1);
            if (responses.Length == 0)
                throw new InvalidOperationException("Keine Antwort vorgesehen.");
            return responses[index](request, body);
        }
    }
}

/// <summary>WAV-Hilfen und Kostenschätzung.</summary>
[TestFixture]
public class WavAudioTests
{
    [Test]
    public void Decode_WavWithHeader_ReadsFormatAndSamples()
    {
        using var stream = new MemoryStream();
        WavAudio.WriteHeader(stream, 16000, 1, 16, 8);
        stream.Write(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        var audio = WavAudio.Decode(stream.ToArray());

        Assert.That(audio.SampleRate, Is.EqualTo(16000));
        Assert.That(audio.Samples, Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
    }

    [Test]
    public void Decode_RawPcm_RateFromMime_Mp3Rejected()
    {
        Assert.That(WavAudio.Decode(new byte[4], "audio/L16;codec=pcm;rate=16000").SampleRate, Is.EqualTo(16000));
        Assert.That(WavAudio.Decode(new byte[4]).SampleRate, Is.EqualTo(24000));
        Assert.Throws<InvalidDataException>(() => WavAudio.Decode(new byte[] { 0xFF, 0xFB, 0, 0 }, "audio/mp3"));
    }

    [Test]
    public void Writer_WritesValidHeader_AndRejectsOtherFormat()
    {
        var path = Path.Combine(Path.GetTempPath(), "StudyScopeWav_" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            using (var writer = new WavFileWriter(path))
            {
                writer.Append(new PcmAudio(new byte[48000], 24000, 1, 16));
                writer.AppendSilence(0.5);
                Assert.Throws<InvalidDataException>(() => writer.Append(new PcmAudio(new byte[10], 16000, 1, 16)));
                writer.Complete();
            }

            var decoded = WavAudio.Decode(File.ReadAllBytes(path));
            Assert.That(decoded.DurationSeconds, Is.EqualTo(1.5).Within(0.001));
            Assert.That(new FileInfo(path).Length, Is.EqualTo(44 + 72000));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Estimate_OneMinute_SizeAndCost_PriceDoublesAfterIntro()
    {
        var words = (int)AudioScriptFormats.WordsPerMinute;

        var now = GeminiTtsInfo.Estimate(words, 800, new DateTime(2026, 11, 17));
        var later = GeminiTtsInfo.Estimate(words, 800, new DateTime(2027, 1, 1));

        Assert.That(now.Seconds, Is.EqualTo(60).Within(0.001));
        Assert.That(now.Megabytes, Is.EqualTo(2.88).Within(0.01), "≈ 2,9 MB je Minute");
        Assert.That(now.Usd, Is.EqualTo(1500 * 9.0 / 1_000_000).Within(0.001));
        Assert.That(later.Usd, Is.GreaterThan(now.Usd * 1.9));
    }

    [TestCase("Kore", true)]
    [TestCase("Puck", false)]
    public void VoiceGender(string voice, bool female) => Assert.That(GeminiTtsInfo.IsFemaleVoice(voice), Is.EqualTo(female));
}
