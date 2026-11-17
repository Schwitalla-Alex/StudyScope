using StudyScope.Core.Interfaces;
using StudyScope.ViewModels.Audio;

namespace StudyScope.Tests.ViewModels;

/// <summary>Tests für die Bewertung der Systemstimme: Verfügbarkeit samt Hinweis, Stimmenreihenfolge und -wahl, Sprechstücke.</summary>
[TestFixture]
public class SystemVoiceRulesTests
{
    private static readonly SpeechVoice GermanLocal = new("de-local", "Katja", "de-DE");
    private static readonly SpeechVoice GermanOnline = new("de-online", "Amala Online", "de-DE", IsLocal: false);
    private static readonly SpeechVoice English = new("en", "Zira", "en-US");

    [Test]
    public void Evaluate_NotSupported_OnLinux_PointsToGemini()
    {
        var result = SystemVoiceRules.Evaluate(false, Array.Empty<SpeechVoice>(), isLinux: true);

        Assert.That(result.Status, Is.EqualTo(SpeechAvailabilityStatus.Unavailable));
        Assert.That(result.Hint, Does.Contain("WebKitGTK").And.Contain("Gemini TTS"));
    }

    [Test]
    public void Evaluate_NoVoices_OnWindows_ExplainsInstallation()
    {
        var result = SystemVoiceRules.Evaluate(true, Array.Empty<SpeechVoice>(), isLinux: false);

        Assert.That(result.Status, Is.EqualTo(SpeechAvailabilityStatus.Unavailable));
        Assert.That(result.Hint, Is.EqualTo(SystemVoiceRules.WindowsGermanVoiceHint));
    }

    [Test]
    public void Evaluate_OnlyEnglish_IsWarningWithInstallHint()
    {
        var result = SystemVoiceRules.Evaluate(true, new[] { English }, isLinux: false);

        Assert.That(result.Status, Is.EqualTo(SpeechAvailabilityStatus.ReadyWithWarning));
        Assert.That(result.IsAvailable, Is.True);
        Assert.That(result.Message, Does.Contain("keine deutsche Stimme"));
        Assert.That(result.Hint, Does.Contain("Zeit und Sprache"));
    }

    [Test]
    public void Evaluate_OnlyGermanOnline_WarnsAboutDataTransfer()
    {
        var result = SystemVoiceRules.Evaluate(true, new[] { GermanOnline, English }, isLinux: false);

        Assert.That(result.Status, Is.EqualTo(SpeechAvailabilityStatus.ReadyWithWarning));
        Assert.That(result.Message, Does.Contain("Online-Stimme"));
    }

    [Test]
    public void Evaluate_GermanLocal_IsReady()
    {
        var result = SystemVoiceRules.Evaluate(true, new[] { English, GermanLocal }, isLinux: false);

        Assert.That(result.Status, Is.EqualTo(SpeechAvailabilityStatus.Ready));
        Assert.That(result.Message, Does.Contain("1 deutsche Stimme"));
    }

    [Test]
    public void Order_GermanAndLocalFirst()
    {
        var ordered = SystemVoiceRules.Order(new[] { English, GermanOnline, GermanLocal });
        Assert.That(ordered.Select(v => v.Id), Is.EqualTo(new[] { "de-local", "de-online", "en" }));
    }

    [TestCase("Microsoft Stefan - German (Germany)", VoiceGender.Male)]
    [TestCase("Microsoft Katja Online (Natural) - German (Germany)", VoiceGender.Female)]
    [TestCase("Microsoft Hedda - German (Germany)", VoiceGender.Female)]
    [TestCase("Microsoft Conrad Online (Natural) - German (Germany)", VoiceGender.Male)]
    [TestCase("German+female2", VoiceGender.Female)]
    [TestCase("Google Deutsch", VoiceGender.Unknown)]
    public void GuessGender_FromVoiceName(string name, VoiceGender expected) =>
        Assert.That(SystemVoiceRules.GuessGender(new SpeechVoice("id", name, "de-DE")), Is.EqualTo(expected));

    [Test]
    public void ResolveVoice_PrefersGender_ButStaysGerman()
    {
        var voices = new[]
        {
            new SpeechVoice("hedda", "Hedda", "de-DE"),
            new SpeechVoice("katja", "Katja", "de-DE"),
            new SpeechVoice("david", "David", "en-US")
        };

        Assert.That(SystemVoiceRules.ResolveVoice(voices, "", 1, "hedda", VoiceGender.Male), Is.EqualTo("katja"),
            "keine männliche deutsche Stimme: andere deutsche Stimme, nicht die englische");
        Assert.That(SystemVoiceRules.ResolveVoice(voices, "", 0, null, VoiceGender.Female), Is.EqualTo("hedda"));
    }

    [Test]
    public void ShortName_RemovesVendorAndLanguage() =>
        Assert.That(SystemVoiceRules.ShortName(new SpeechVoice("x", "Microsoft Stefan - German (Germany)", "de-DE")), Is.EqualTo("Stefan"));

    [Test]
    public void SecondVoiceHint_OnLinux_HasNoWindowsPath()
    {
        var voices = new[] { new SpeechVoice("a", "Anna", "de-DE"), new SpeechVoice("b", "Petra", "de-DE") };

        var hint = SystemVoiceRules.SecondVoiceHint(voices, "a", "b", "Experte", VoiceGender.Male, isLinux: true);

        Assert.That(hint, Does.Contain("Keine männliche deutsche Stimme").And.Not.Contain("Windows"));
        Assert.That(SystemVoiceRules.SecondVoiceHint(voices, "a", "b", "Experte", VoiceGender.Female, isLinux: false), Is.Null);
    }

    [Test]
    public void ResolveVoice_ChosenOrAutomatic_SecondSpeakerDiffers()
    {
        var voices = new[] { English, GermanLocal, GermanOnline };

        Assert.That(SystemVoiceRules.ResolveVoice(voices, "en", 0), Is.EqualTo("en"));
        Assert.That(SystemVoiceRules.ResolveVoice(voices, "gibt-es-nicht", 0), Is.EqualTo("de-local"));
        Assert.That(SystemVoiceRules.ResolveVoice(voices, "", 1, "de-local"), Is.EqualTo("de-online"));
        Assert.That(SystemVoiceRules.ResolveVoice(new[] { GermanLocal }, "", 1, "de-local"), Is.EqualTo("de-local"), "nur eine Stimme: beide gleich");
        Assert.That(SystemVoiceRules.ResolveVoice(Array.Empty<SpeechVoice>(), "", 0), Is.Empty);
    }

    [Test]
    public void SplitForSpeech_KeepsShortText_SplitsLongAtSentences()
    {
        Assert.That(SystemVoiceRules.SplitForSpeech("Kurz."), Is.EqualTo(new[] { "Kurz." }));
        Assert.That(SystemVoiceRules.SplitForSpeech("  "), Is.Empty);

        var text = string.Concat(Enumerable.Repeat("Ein Satz mit mehreren Wörtern darin. ", 30));
        var parts = SystemVoiceRules.SplitForSpeech(text, 120);

        Assert.That(parts, Has.Count.GreaterThan(1));
        Assert.That(parts.All(p => p.Length <= 120 && p.EndsWith('.')), Is.True);
        Assert.That(string.Join(" ", parts), Is.EqualTo(text.Trim()));
    }
}
