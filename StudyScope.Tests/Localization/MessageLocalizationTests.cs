using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Localization;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.Services;
using StudyScope.ViewModels.Audio;

namespace StudyScope.Tests.Localization;

/// <summary>
/// Meldungen aus Core, Diensten und der Stimmenbewertung (Teil 3b-1): Deutsch unverändert, Englisch übersetzt,
/// technische Angaben (HTTP-Status, Details) bleiben in jeder Sprache gleich.
/// </summary>
[TestFixture]
public class MessageLocalizationTests
{
    [Test]
    public void LlmServiceException_German_Unchanged()
    {
        var ex = LlmServiceException.FromHttpStatus("OpenAI", 429, "{\"error\":\"quota\"}");
        Assert.That(ex.Message, Is.EqualTo("OpenAI meldet einen Fehler (HTTP 429). Zu viele Anfragen oder das Kontingent ist erschöpft. Bitte kurz warten und erneut versuchen."));
        Assert.That(ex.Detail, Is.EqualTo("{\"error\":\"quota\"}"));
    }

    [Test, SetUICulture("en"), SetCulture("de-DE")]
    public void LlmServiceException_English_StatusWithoutGrouping()
    {
        var ex = LlmServiceException.FromHttpStatus("Mistral AI", 503, null);
        Assert.That(ex.Message, Is.EqualTo("Mistral AI reports an error (HTTP 503). The AI service is currently down or overloaded. Try again in a few minutes."));
    }

    [Test]
    public void LlmServiceException_402_NamesBilling_German()
    {
        // Mistral meldet 402, wenn das Konto kein Guthaben bzw. keinen Zahlungsplan hat (Echttest 3c-1)
        var ex = LlmServiceException.FromHttpStatus("Mistral AI", 402, "{\"message\":\"payment required\"}");
        Assert.That(ex.Message, Is.EqualTo("Mistral AI meldet einen Fehler (HTTP 402). Das Guthaben bzw. der Zahlungsplan des Kontos reicht nicht aus. " +
                                           "Bitte die Abrechnung beim Anbieter prüfen (z. B. Guthaben aufladen oder Zahlungsart hinterlegen)."));
        Assert.That(ex.StatusCode, Is.EqualTo(402));
        Assert.That(ex.Detail, Does.Contain("payment required"));
    }

    [Test, SetUICulture("en")]
    public void LlmServiceException_402_NamesBilling_English()
    {
        var ex = LlmServiceException.FromHttpStatus("Mistral AI", 402, null);
        Assert.That(ex.Message, Is.EqualTo("Mistral AI reports an error (HTTP 402). The account has no credit or no active billing plan. " +
                                           "Check the billing settings with the provider (for example, add credit or a payment method)."));
    }

    [Test, SetUICulture("en")]
    public void ProfileNameValidator_English()
    {
        Assert.That(ProfileNameValidator.Validate(""), Is.EqualTo("Enter a name."));
        Assert.That(ProfileNameValidator.Validate("Test."), Is.EqualTo("The name must not end with a period."));
        Assert.That(ProfileNameValidator.Validate("CON"), Is.EqualTo("“CON” is a reserved system name and cannot be used."));
    }

    [Test, SetUICulture("en")]
    public void GeminiTts_English_QuotaAndModelNotFound()
    {
        const string body = "{\"error\":{\"code\":429,\"status\":\"RESOURCE_EXHAUSTED\",\"message\":\"Quota exceeded\"}}";
        var quota = GeminiTextToSpeechService.ErrorFromResponse(429, body, "m");
        Assert.That(quota.Message, Does.StartWith("The Gemini TTS quota is used up"));
        Assert.That(quota.Detail, Does.Contain("RESOURCE_EXHAUSTED"), "technische Angabe bleibt unverändert");

        var missing = GeminiTextToSpeechService.ErrorFromResponse(404, "{\"error\":{\"code\":404,\"status\":\"NOT_FOUND\",\"message\":\"not found\"}}", "nope");
        Assert.That(missing.Message, Does.Contain("The model “nope” was not found").And.Contain(GeminiTtsInfo.DefaultModel));
    }

    [Test, SetUICulture("en")]
    public void SystemVoice_English_ReadyAndGenderHint()
    {
        var voices = new List<SpeechVoice>
        {
            new("katja", "Microsoft Katja - German (Germany)", "de-DE"),
            new("hedda", "Microsoft Hedda - German (Germany)", "de-DE"),
        };
        Assert.That(SystemVoiceRules.Evaluate(true, voices, isLinux: false).Message, Is.EqualTo("Ready – 2 German voices available."));
        Assert.That(SystemVoiceRules.Evaluate(true, voices.Take(1).ToList(), isLinux: false).Message, Is.EqualTo("Ready – 1 German voice available."));

        var hint = SystemVoiceRules.SecondVoiceHint(voices, "katja", "hedda", AudioScriptFormats.ExpertName, VoiceGender.Male, isLinux: false);
        Assert.That(hint, Does.StartWith("No male German voice installed for “Expert” – “Hedda” is used."));
        Assert.That(hint, Does.Contain("Settings › Time & language").And.Contain("↻ Check again"));
    }

    [Test]
    public void SystemVoice_German_Unchanged()
    {
        var voices = new List<SpeechVoice> { new("katja", "Microsoft Katja - German (Germany)", "de-DE") };
        Assert.That(SystemVoiceRules.Evaluate(true, voices, isLinux: false).Message, Is.EqualTo("Bereit – 1 deutsche Stimme verfügbar."));
        Assert.That(SystemVoiceRules.WindowsMoreVoicesHint, Does.Contain("„↻ Neu prüfen“"));
    }

    [Test, SetUICulture("en")]
    public void FfmpegAndSpeechDefaults_English()
    {
        Assert.That(FfmpegInfo.InstallHint(isLinux: true), Does.StartWith("Install ffmpeg: in the terminal, run “sudo apt install ffmpeg”"));
        Assert.That(SpeechAvailability.Checking().Message, Is.EqualTo("Checking text-to-speech…"));
    }

    [Test, SetUICulture("en"), SetCulture("en-US")]
    public void MediaFormat_English()
    {
        Assert.That(MediaFormat.Minutes(3.2), Is.EqualTo("approx. 3 min"));
        Assert.That(MediaFormat.Size(0.05), Is.EqualTo("under 0.1 MB"));
    }
}
