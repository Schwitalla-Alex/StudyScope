using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Localization;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.AI;
using StudyScope.Infrastructure.Services;
using StudyScope.ViewModels.Audio;

namespace StudyScope.Tests.Localization;

/// <summary>
/// Sprachausgabe je Ergebnissprache (3c-3): Vorlesesprache eines Inhalts, Sprachdateien (bevorzugte Stimmen,
/// Abkürzungen), Stimmenwahl je Sprache in den Einstellungen, Zuordnung englischer Sprecherrollen, Gemini-Stil und
/// das Skript der Systemstimme.
/// </summary>
[TestFixture]
public class SpeechLanguageTests
{
    private const string EnglishText =
        "The cell is the basic unit of life. It is surrounded by a membrane and contains the nucleus, which holds the genetic " +
        "information. Most of the energy is produced in the mitochondria, and the plants also have chloroplasts for photosynthesis.";

    private const string GermanText =
        "Die Zelle ist die kleinste Einheit des Lebens. Sie ist von einer Membran umgeben und enthält den Zellkern, in dem die " +
        "Erbinformation liegt. Die meiste Energie wird in den Mitochondrien gewonnen, und die Pflanzen haben außerdem Chloroplasten.";

    // ---------- Vorlesesprache ----------

    [Test]
    public void For_StoredLanguage_Wins()
    {
        Assert.That(SpeechLanguage.For("en", new[] { GermanText }, "de"), Is.EqualTo("en"));
        Assert.That(SpeechLanguage.For("en-GB", Array.Empty<string>(), null), Is.EqualTo("en"));
    }

    [Test]
    public void For_OlderContentWithoutLanguage_GuessesFromText()
    {
        Assert.That(SpeechLanguage.For(null, new[] { EnglishText }, "de"), Is.EqualTo("en"));
        Assert.That(SpeechLanguage.For(null, new[] { GermanText }, "en"), Is.EqualTo("de"));
    }

    [Test]
    public void For_NoContent_FollowsOutputLanguageSetting()
    {
        Assert.That(SpeechLanguage.For(null, Array.Empty<string>(), "en"), Is.EqualTo("en"));
        Assert.That(SpeechLanguage.For(null, new[] { "Kurz." }, "de"), Is.EqualTo("de"));
    }

    [Test, SetUICulture("en")]
    public void For_NoContent_FollowUi_UsesUiLanguage() =>
        Assert.That(SpeechLanguage.For(null, Array.Empty<string>(), OutputLanguage.FollowUi), Is.EqualTo("en"));

    [TestCase("en-GB", "en", true)]
    [TestCase("en_US", "en", true)]
    [TestCase("de-DE", "en", false)]
    [TestCase("de-AT", "de-CH", true)]
    [TestCase("", "de", false)]
    [TestCase("de-DE", null, false)]
    public void Matches_ComparesLanguagePart(string voice, string? language, bool expected) =>
        Assert.That(SpeechLanguage.Matches(voice, language), Is.EqualTo(expected));

    [TestCase("en", "en-US")]
    [TestCase("de", "de-DE")]
    [TestCase(null, "de-DE")]
    public void SpecificTag_ForWebSpeech(string? language, string expected) =>
        Assert.That(SpeechLanguage.SpecificTag(language), Is.EqualTo(expected));

    [Test]
    public void SpeechVoice_Matches_ReplacesIsGerman()
    {
        var voice = new SpeechVoice("x", "Zira", "en-US");
        Assert.That(voice.Matches("en"), Is.True);
        Assert.That(voice.Matches("de"), Is.False);
    }

    // ---------- Sprachdateien ----------

    [Test]
    public void EveryLanguage_HasVoiceListAndAbbreviations()
    {
        foreach (var language in LanguageData.Languages(StopWords.FileName))
        {
            Assert.That(VoiceNames.Entries(language), Is.Not.Empty, $"{language}/voices.txt fehlt");
            Assert.That(SpokenAbbreviations.Entries(language), Is.Not.Empty, $"{language}/abbreviations.txt fehlt");
        }
    }

    [Test]
    public void VoiceFiles_EveryLineHasGender_NoConflictsBetweenLanguages()
    {
        var all = new Dictionary<string, (VoiceGender Gender, string Language)>(StringComparer.OrdinalIgnoreCase);
        foreach (var language in LanguageData.Languages(VoiceNames.FileName))
        {
            foreach (var line in LanguageData.ReadLines(language, VoiceNames.FileName))
                Assert.That(line, Does.Match(@"^[^=]+ = (female|male)$"), $"{language}/voices.txt: „{line}“");

            foreach (var (name, gender) in VoiceNames.Entries(language))
            {
                if (all.TryGetValue(name, out var other))
                    Assert.That(other.Gender, Is.EqualTo(gender), $"„{name}“ in {other.Language} und {language} mit verschiedenem Geschlecht");
                else
                    all[name] = (gender, language);
            }
        }
    }

    [Test]
    public void VoiceFiles_EveryNameIsOneWholeWord()
    {
        // Abgleich nur nach ganzen Wörtern: ein Eintrag mit Leerzeichen, Bindestrich o. Ä. könnte nie treffen.
        foreach (var language in LanguageData.Languages(VoiceNames.FileName))
        {
            foreach (var (name, _) in VoiceNames.Entries(language))
                Assert.That(VoiceNames.Words(name), Is.EqualTo(new[] { name }), $"{language}/voices.txt: „{name}“ ist kein einzelnes Wort");
        }
    }

    [TestCase("Microsoft Jan", VoiceGender.Male)]
    [TestCase("Jana", VoiceGender.Unknown)]
    [TestCase("Microsoft Janina Online (Natural) - German (Germany)", VoiceGender.Unknown)]
    [TestCase("Google Markus", VoiceGender.Male)]
    [TestCase("Microsoft Stefan - German (Germany)", VoiceGender.Male)]
    [TestCase("Germania", VoiceGender.Unknown)]
    public void VoiceNames_MatchWholeWordsOnly(string voiceName, VoiceGender expected) =>
        Assert.That(SystemVoiceRules.GuessGender(new SpeechVoice("x", voiceName, "de-DE")), Is.EqualTo(expected));

    [Test]
    public void Rank_MatchesWholeWordsOnly()
    {
        Assert.That(VoiceNames.Rank(VoiceNames.Words("Microsoft Jan"), "de"), Is.LessThan(int.MaxValue));
        Assert.That(VoiceNames.Rank(VoiceNames.Words("Microsoft Jana"), "de"), Is.EqualTo(int.MaxValue));
        Assert.That(VoiceNames.Rank(VoiceNames.Words("Heddas Stimme"), "de"), Is.EqualTo(int.MaxValue));
        Assert.That(VoiceNames.Rank(VoiceNames.Words("Microsoft Hedda Desktop"), "de"), Is.EqualTo(0));
    }

    [Test]
    public void AbbreviationFiles_EveryLineHasExpansion()
    {
        foreach (var language in LanguageData.Languages(SpokenAbbreviations.FileName))
        {
            foreach (var line in LanguageData.ReadLines(language, SpokenAbbreviations.FileName))
                Assert.That(line, Does.Match(@"^\S.* = \S.*$"), $"{language}/abbreviations.txt: „{line}“");
        }
    }

    [TestCase("Das gilt z. B. für Pflanzen.", "de", "Das gilt zum Beispiel für Pflanzen.")]
    [TestCase("Das gilt z.B. für Glukose.", "de", "Das gilt zum Beispiel für Glukose.")]
    [TestCase("Zucker, Stärke usw. Danach folgt Fett.", "de", "Zucker, Stärke und so weiter. Danach folgt Fett.")]
    [TestCase("Zucker, Stärke usw. und Fett.", "de", "Zucker, Stärke und so weiter und Fett.")]
    [TestCase("Im 19. Jh. entdeckt.", "de", "Im 19. Jahrhundert entdeckt.")]
    [TestCase("Das gilt d. h. immer.", "de", "Das gilt das heißt immer.")]
    [TestCase("This applies e.g. to plants.", "en", "This applies for example to plants.")]
    [TestCase("Sugar, starch etc. Then fat.", "en", "Sugar, starch and so on. Then fat.")]
    [TestCase("Sugar, starch etc.", "en", "Sugar, starch and so on.")]
    public void Expand_WritesOutAbbreviations(string text, string language, string expected) =>
        Assert.That(SpokenAbbreviations.Expand(text, language), Is.EqualTo(expected));

    [Test]
    public void Expand_OnlyForTheGivenLanguage_AndWholeWords()
    {
        Assert.That(SpokenAbbreviations.Expand("Das gilt z. B. hier.", "en"), Is.EqualTo("Das gilt z. B. hier."));
        Assert.That(SpokenAbbreviations.Expand("Das gilt z. B. hier.", null), Is.EqualTo("Das gilt z. B. hier."));
        Assert.That(SpokenAbbreviations.Expand("Die Abzw. bleibt.", "de"), Is.EqualTo("Die Abzw. bleibt."), "„bzw.“ nur als eigenes Wort");
    }

    // ---------- Stimmen je Sprache in den Einstellungen ----------

    [Test]
    public void SpeechSettings_VoicePerLanguage_GermanKeepsLegacyFields()
    {
        var settings = new SpeechSettings { SystemVoicePrimary = "hedda", SystemVoiceSecondary = "stefan" };

        Assert.That(settings.GetSystemVoice("de", 0), Is.EqualTo("hedda"), "ältere Einstellung gilt für Deutsch");
        Assert.That(settings.GetSystemVoice("en", 0), Is.Empty, "für Englisch noch keine Wahl → automatisch");

        settings.SetSystemVoice("en", 0, "zira");
        settings.SetSystemVoice("de", 1, "conrad");

        Assert.That(settings.GetSystemVoice("en-US", 0), Is.EqualTo("zira"));
        Assert.That(settings.GetSystemVoice("de", 0), Is.EqualTo("hedda"));
        Assert.That(settings.GetSystemVoice("de", 1), Is.EqualTo("conrad"));
        Assert.That(settings.SystemVoiceSecondary, Is.EqualTo("conrad"), "Deutsch auch in den alten Feldern");
        Assert.That(settings.SystemVoicePrimary, Is.EqualTo("hedda"));
    }

    [Test]
    public void SpeechSettings_Clone_CopiesVoicesPerLanguage()
    {
        var settings = new SpeechSettings();
        settings.SetSystemVoice("en", 0, "zira");

        var copy = settings.Clone();
        copy.SetSystemVoice("en", 0, "david");

        Assert.That(settings.GetSystemVoice("en", 0), Is.EqualTo("zira"));
        Assert.That(copy.GetSystemVoice("en", 0), Is.EqualTo("david"));
    }

    [Test]
    public async Task SpeechSettings_VoicesPerLanguage_AreSavedAndLoaded()
    {
        var dir = Path.Combine(Path.GetTempPath(), "StudyScopeSpeechLang_" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new JsonStorageService(dir);
            var settings = new SpeechSettings();
            settings.SetSystemVoice("en", 0, "zira");
            settings.SetSystemVoice("de", 0, "katja");
            await storage.SaveSpeechSettingsAsync("Testprofil", settings);

            var loaded = await new JsonStorageService(dir).LoadSpeechSettingsAsync("Testprofil");

            Assert.That(loaded.GetSystemVoice("en", 0), Is.EqualTo("zira"));
            Assert.That(loaded.GetSystemVoice("de", 0), Is.EqualTo("katja"));
            Assert.That(loaded.SystemVoicePrimary, Is.EqualTo("katja"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    // ---------- Audio-Skript: englische Rollen, Bereinigung ----------

    [Test]
    public void NormalizeSpeakers_EnglishRoles_MapToStoredRoles_EvenIfExpertSpeaksFirst()
    {
        var segments = new List<AudioSegment>
        {
            new() { Speaker = "Expert", Text = "First." },
            new() { Speaker = "Host", Text = "Second." },
            new() { Speaker = "Expert", Text = "Third." }
        };

        AudioScriptService.NormalizeSpeakers(segments, AudioScriptFormats.Speakers(AudioScriptFormat.Podcast));

        Assert.That(segments.Select(s => s.Speaker), Is.EqualTo(new[] { AudioScriptFormats.ExpertName, AudioScriptFormats.HostName, AudioScriptFormats.ExpertName }));
    }

    [Test]
    public void AnalyzeResponse_English_ExpandsAbbreviations_RemovesSourceMarkers()
    {
        const string raw = "[{\"title\":\"Cells\",\"segments\":[{\"speaker\":\"Narrator\",\"text\":\"Organelles, e.g. the nucleus [Source: biology.pdf], matter.\"}]}]";

        var result = AudioScriptService.AnalyzeResponse(raw, AudioScriptFormat.Summary, "Fallback", "en");

        var segment = result.Summary!.Segments.Single();
        Assert.That(segment.Speaker, Is.EqualTo(AudioScriptFormats.NarratorName));
        Assert.That(segment.Text, Is.EqualTo("Organelles, for example the nucleus, matter."));
    }

    [Test]
    public async Task FakeAi_EnglishPodcast_UsesEnglishRoles_StoredAsGermanRoles()
    {
        var prompt = "Erstelle ein Audio-Skript … JSON-Array\nFormat: Podcast-Gespräch\n- " + OutputLanguage.PromptLine(System.Globalization.CultureInfo.GetCultureInfo("en"));
        Assert.That(FakeAudioScriptResponse.Create(prompt, new[] { EnglishText }), Does.Contain("\"Host\"").And.Contain("\"Expert\""));

        var service = new AudioScriptService(new FakeLlmService(), Path.GetTempPath());
        var result = await service.GenerateAsync(new[] { new SourceChunk("cells.txt", EnglishText) },
            AudioScriptFormat.Podcast, AudioScriptLength.Short, string.Empty, string.Empty, "en");

        Assert.That(result.Summary!.Language, Is.EqualTo("en"));
        Assert.That(result.Summary.Segments.Select(s => s.Speaker).Distinct(),
            Is.EquivalentTo(new[] { AudioScriptFormats.HostName, AudioScriptFormats.ExpertName }));
        Assert.That(result.Summary.Segments[0].Speaker, Is.EqualTo(AudioScriptFormats.HostName));
    }

    // ---------- Gemini TTS ----------

    [Test]
    public void GeminiStyle_FollowsLanguageOfTheEpisode()
    {
        Assert.That(GeminiTextToSpeechService.SpeechStyleFor("de"), Is.EqualTo("Auf Deutsch, natürlich, ruhig und deutlich vorlesen."));
        Assert.That(GeminiTextToSpeechService.SpeechStyleFor(null), Does.StartWith("Auf Deutsch"), "ohne Angabe wie vor 3c-3");
        Assert.That(GeminiTextToSpeechService.SpeechStyleFor("en"), Is.EqualTo("Read aloud in English, naturally, calmly and clearly."));

        var pieces = new[] { new GeminiTextToSpeechService.SpeechPiece(0, 0, "Hello."), new GeminiTextToSpeechService.SpeechPiece(1, 1, "Hi.") };
        var body = GeminiTextToSpeechService.BuildRequestBody("m", pieces, true, "Kore", "Puck", "en");
        var styles = body["input"]![0]!["content"]!.AsArray().Select(c => c!["annotations"]![0]!["style"]!.GetValue<string>());
        Assert.That(styles, Is.All.EqualTo("Read aloud in English, naturally, calmly and clearly."));
    }

    [Test, SetUICulture("en")]
    public void GeminiStyle_DoesNotFollowUiLanguage() =>
        Assert.That(GeminiTextToSpeechService.SpeechStyleFor("de"), Does.StartWith("Auf Deutsch"));

    // ---------- Systemstimme (Skript) ----------

    [Test]
    public void SpeechScript_TakesLanguage_NoFixedGerman()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StudyScope.slnx"))) dir = dir.Parent;
        Assert.That(dir, Is.Not.Null, "Repository-Wurzel (StudyScope.slnx) nicht gefunden");
        var script = File.ReadAllText(Path.Combine(dir!.FullName, "StudyScope.UI", "wwwroot", "js", "studyscope-speech.js"));

        Assert.That(script, Does.Contain("function speak(dotnetRef, id, text, voiceId, rate, lang)"));
        Assert.That(script, Does.Not.Contain("/^de/"), "Sprache nicht fest auf Deutsch");
    }
}
