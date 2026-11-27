using StudyScope.Core.Interfaces;
using StudyScope.ViewModels.Audio;

namespace StudyScope.Tests.ViewModels;

/// <summary>
/// Tests für die Bewertung der Systemstimme: Verfügbarkeit samt Hinweis, Stimmenreihenfolge und -wahl, Sprechstücke –
/// jeweils für die Sprache des Beitrags (seit 3c-3).
/// </summary>
[TestFixture]
public class SystemVoiceRulesTests
{
    private static readonly SpeechVoice GermanLocal = new("de-local", "Katja", "de-DE");
    private static readonly SpeechVoice GermanOnline = new("de-online", "Amala Online", "de-DE", IsLocal: false);
    private static readonly SpeechVoice English = new("en", "Zira", "en-US");

    [Test]
    public void Evaluate_NotSupported_OnLinux_PointsToGemini()
    {
        var result = SystemVoiceRules.Evaluate(false, Array.Empty<SpeechVoice>(), isLinux: true, "de");

        Assert.That(result.Status, Is.EqualTo(SpeechAvailabilityStatus.Unavailable));
        Assert.That(result.Hint, Does.Contain("WebKitGTK").And.Contain("Gemini TTS"));
    }

    [Test]
    public void Evaluate_NoVoices_OnWindows_ExplainsInstallation()
    {
        var result = SystemVoiceRules.Evaluate(true, Array.Empty<SpeechVoice>(), isLinux: false, "de");

        Assert.That(result.Status, Is.EqualTo(SpeechAvailabilityStatus.Unavailable));
        Assert.That(result.Hint, Is.EqualTo(SystemVoiceRules.WindowsVoiceHint("de")));
        Assert.That(result.Hint, Does.StartWith("Deutsche Stimme unter Windows installieren"));
    }

    [Test]
    public void Evaluate_OnlyEnglish_IsWarningWithInstallHint()
    {
        var result = SystemVoiceRules.Evaluate(true, new[] { English }, isLinux: false, "de");

        Assert.That(result.Status, Is.EqualTo(SpeechAvailabilityStatus.ReadyWithWarning));
        Assert.That(result.IsAvailable, Is.True);
        Assert.That(result.Message, Does.Contain("keine deutsche Stimme"));
        Assert.That(result.Hint, Does.Contain("Zeit und Sprache"));
    }

    [Test]
    public void Evaluate_OnlyGermanOnline_WarnsAboutDataTransfer()
    {
        var result = SystemVoiceRules.Evaluate(true, new[] { GermanOnline, English }, isLinux: false, "de");

        Assert.That(result.Status, Is.EqualTo(SpeechAvailabilityStatus.ReadyWithWarning));
        Assert.That(result.Message, Does.Contain("1 deutsche Online-Stimme"));
    }

    [Test]
    public void Evaluate_GermanLocal_IsReady()
    {
        var result = SystemVoiceRules.Evaluate(true, new[] { English, GermanLocal }, isLinux: false, "de");

        Assert.That(result.Status, Is.EqualTo(SpeechAvailabilityStatus.Ready));
        Assert.That(result.Message, Does.Contain("1 deutsche Stimme"));
    }

    [Test]
    public void Evaluate_EnglishContent_ChecksEnglishVoices()
    {
        var onlyGerman = SystemVoiceRules.Evaluate(true, new[] { GermanLocal }, isLinux: false, "en");
        Assert.That(onlyGerman.Status, Is.EqualTo(SpeechAvailabilityStatus.ReadyWithWarning));
        Assert.That(onlyGerman.Message, Does.Contain("keine englische Stimme"));
        Assert.That(onlyGerman.Hint, Does.StartWith("Englische Stimme unter Windows installieren").And.Contain("› Englisch"));

        var ready = SystemVoiceRules.Evaluate(true, new[] { GermanLocal, English, new SpeechVoice("gb", "Hazel", "en-GB") }, isLinux: false, "en");
        Assert.That(ready.Status, Is.EqualTo(SpeechAvailabilityStatus.Ready));
        Assert.That(ready.Message, Is.EqualTo("Bereit – 2 englische Stimmen verfügbar."));
    }

    [Test, SetUICulture("en")]
    public void Evaluate_EnglishUi_NamesTheContentLanguage()
    {
        Assert.That(SystemVoiceRules.Evaluate(true, new[] { GermanLocal }, isLinux: false, "en").Message,
            Is.EqualTo("Ready – but no English voice was found; the text is read aloud with a voice for another language."));
        Assert.That(SystemVoiceRules.Evaluate(true, new[] { English }, isLinux: false, "de").Message,
            Does.Contain("no German voice"));
        Assert.That(SystemVoiceRules.WindowsVoiceHint("en"), Does.StartWith("Install an English voice on Windows"));
    }

    [TestCase(null, "de")]
    [TestCase("", "de")]
    [TestCase("en-GB", "en")]
    [TestCase("fr", "fr")]
    public void LanguageKey_FallsBackToGerman(string? language, string expected) =>
        Assert.That(SystemVoiceRules.LanguageKey(language), Is.EqualTo(expected));

    [Test]
    public void Evaluate_UnknownLanguage_UsesNeutralWording()
    {
        var result = SystemVoiceRules.Evaluate(true, new[] { GermanLocal }, isLinux: false, "fr");
        Assert.That(result.Message, Does.Contain("keine zur Sprache passende Stimme"));
    }

    [Test]
    public void Order_LanguageAndLocalFirst()
    {
        var ordered = SystemVoiceRules.Order(new[] { English, GermanOnline, GermanLocal }, "de");
        Assert.That(ordered.Select(v => v.Id), Is.EqualTo(new[] { "de-local", "de-online", "en" }));

        var english = SystemVoiceRules.Order(new[] { GermanLocal, GermanOnline, English }, "en");
        Assert.That(english[0].Id, Is.EqualTo("en"));
    }

    [Test]
    public void Order_PrefersVoicesFromLanguageFile()
    {
        // „Zira“ steht in en/voices.txt vor „Hazel“; ohne Liste ginge es alphabetisch (Hazel vor Zira)
        var voices = new[]
        {
            new SpeechVoice("hazel", "Microsoft Hazel - English (United Kingdom)", "en-GB"),
            new SpeechVoice("zira", "Microsoft Zira - English (United States)", "en-US"),
            new SpeechVoice("aaa", "Aaron Unbekannt", "en-US")
        };
        Assert.That(SystemVoiceRules.Order(voices, "en").Select(v => v.Id), Is.EqualTo(new[] { "zira", "hazel", "aaa" }));
    }

    [TestCase("Microsoft Stefan - German (Germany)", VoiceGender.Male)]
    [TestCase("Microsoft Katja Online (Natural) - German (Germany)", VoiceGender.Female)]
    [TestCase("Microsoft Hedda - German (Germany)", VoiceGender.Female)]
    [TestCase("Microsoft Conrad Online (Natural) - German (Germany)", VoiceGender.Male)]
    [TestCase("German+female2", VoiceGender.Female)]
    [TestCase("Google Deutsch", VoiceGender.Unknown)]
    [TestCase("Microsoft David - English (United States)", VoiceGender.Male)]
    [TestCase("Microsoft Zira - English (United States)", VoiceGender.Female)]
    [TestCase("Microsoft Aria Online (Natural) - English (United States)", VoiceGender.Female)]
    [TestCase("Microsoft Guy Online (Natural) - English (United States)", VoiceGender.Male)]
    [TestCase("English (America)+male1", VoiceGender.Male)]
    [TestCase("Google US English", VoiceGender.Unknown)]
    public void GuessGender_FromVoiceName(string name, VoiceGender expected) =>
        Assert.That(SystemVoiceRules.GuessGender(new SpeechVoice("id", name, "de-DE")), Is.EqualTo(expected));

    [Test]
    public void ResolveVoice_PrefersGender_ButStaysInLanguage()
    {
        var voices = new[]
        {
            new SpeechVoice("hedda", "Hedda", "de-DE"),
            new SpeechVoice("katja", "Katja", "de-DE"),
            new SpeechVoice("david", "David", "en-US")
        };

        Assert.That(SystemVoiceRules.ResolveVoice(voices, "", 1, "hedda", VoiceGender.Male, "de"), Is.EqualTo("katja"),
            "keine männliche deutsche Stimme: andere deutsche Stimme, nicht die englische");
        Assert.That(SystemVoiceRules.ResolveVoice(voices, "", 0, null, VoiceGender.Female, "de"), Is.EqualTo("hedda"),
            "Hedda steht in de/voices.txt vor Katja");
        Assert.That(SystemVoiceRules.ResolveVoice(voices, "", 0, null, VoiceGender.Female, "en"), Is.EqualTo("david"),
            "englischer Beitrag: die englische Stimme, auch wenn sie nicht zur Rolle passt");
    }

    [Test]
    public void ResolveVoice_EnglishPodcast_HostFemaleExpertMale()
    {
        var voices = new[]
        {
            new SpeechVoice("katja", "Microsoft Katja - German (Germany)", "de-DE"),
            new SpeechVoice("david", "Microsoft David - English (United States)", "en-US"),
            new SpeechVoice("zira", "Microsoft Zira - English (United States)", "en-US")
        };

        var host = SystemVoiceRules.ResolveVoice(voices, "", 0, null, VoiceGender.Female, "en");
        var expert = SystemVoiceRules.ResolveVoice(voices, "", 1, host, VoiceGender.Male, "en");
        Assert.That((host, expert), Is.EqualTo(("zira", "david")));
    }

    [Test]
    public void ShortName_RemovesVendorAndLanguage() =>
        Assert.That(SystemVoiceRules.ShortName(new SpeechVoice("x", "Microsoft Stefan - German (Germany)", "de-DE")), Is.EqualTo("Stefan"));

    [Test]
    public void SecondVoiceHint_OnLinux_HasNoWindowsPath()
    {
        var voices = new[] { new SpeechVoice("a", "Anna", "de-DE"), new SpeechVoice("b", "Petra", "de-DE") };

        var hint = SystemVoiceRules.SecondVoiceHint(voices, "a", "b", "Experte", VoiceGender.Male, isLinux: true, "de");

        Assert.That(hint, Does.Contain("Keine männliche deutsche Stimme").And.Not.Contain("Windows"));
        Assert.That(SystemVoiceRules.SecondVoiceHint(voices, "a", "b", "Experte", VoiceGender.Female, isLinux: false, "de"), Is.Null);
    }

    [Test]
    public void SecondVoiceHint_EnglishContent_OnlyOneEnglishVoice()
    {
        var voices = new[] { new SpeechVoice("a", "Anna", "de-DE"), new SpeechVoice("z", "Zira", "en-US") };

        var hint = SystemVoiceRules.SecondVoiceHint(voices, "z", "z", "Experte", VoiceGender.Male, isLinux: false, "en");

        Assert.That(hint, Does.StartWith("Für zwei Sprecher ist nur eine englische Stimme installiert"));
        Assert.That(hint, Does.Contain("Weitere englische Stimmen (z. B. „David“)"));
    }

    [Test]
    public void ResolveVoice_ChosenOrAutomatic_SecondSpeakerDiffers()
    {
        var voices = new[] { English, GermanLocal, GermanOnline };

        Assert.That(SystemVoiceRules.ResolveVoice(voices, "en", 0, null, VoiceGender.Unknown, "de"), Is.EqualTo("en"));
        Assert.That(SystemVoiceRules.ResolveVoice(voices, "gibt-es-nicht", 0, null, VoiceGender.Unknown, "de"), Is.EqualTo("de-local"));
        Assert.That(SystemVoiceRules.ResolveVoice(voices, "", 1, "de-local", VoiceGender.Unknown, "de"), Is.EqualTo("de-online"));
        Assert.That(SystemVoiceRules.ResolveVoice(new[] { GermanLocal }, "", 1, "de-local", VoiceGender.Unknown, "de"), Is.EqualTo("de-local"), "nur eine Stimme: beide gleich");
        Assert.That(SystemVoiceRules.ResolveVoice(Array.Empty<SpeechVoice>(), "", 0, null, VoiceGender.Unknown, "de"), Is.Empty);
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
