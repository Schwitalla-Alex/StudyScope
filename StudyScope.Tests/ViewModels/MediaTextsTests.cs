using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.ViewModels;

namespace StudyScope.Tests.ViewModels;

/// <summary>
/// Übersetzte Anzeige für Audio und Video: Deutsch entspricht den (gespeicherten bzw. im Prompt genutzten) Angaben aus
/// <see cref="AudioScriptFormats"/>, Englisch ist übersetzt, Zahlen folgen der Formatkultur.
/// </summary>
[TestFixture]
public class MediaTextsTests
{
    [Test]
    public void German_MatchesCoreDisplayNames()
    {
        foreach (var format in AudioScriptFormats.All)
        {
            Assert.That(MediaTexts.FormatName(format), Is.EqualTo(AudioScriptFormats.DisplayName(format)));
            Assert.That(MediaTexts.FormatDescription(format), Is.EqualTo(AudioScriptFormats.Description(format)));
        }
        foreach (var length in AudioScriptFormats.AllLengths)
            Assert.That(MediaTexts.LengthName(length), Is.EqualTo(AudioScriptFormats.LengthName(length)));
        Assert.That(MediaTexts.SpeakerName(AudioScriptFormats.HostName), Is.EqualTo("Moderatorin"));
    }

    [Test, SetUICulture("en"), SetCulture("en-US")]
    public void English_TranslatesDisplay_KeepsOwnSpeakerNames()
    {
        Assert.That(MediaTexts.FormatName(AudioScriptFormat.Podcast), Is.EqualTo("Podcast conversation"));
        Assert.That(MediaTexts.LengthName(AudioScriptLength.Short), Is.EqualTo("short (approx. 3 min)"));
        Assert.That(MediaTexts.SpeakerName(AudioScriptFormats.ExpertName), Is.EqualTo("Expert"));
        Assert.That(MediaTexts.SpeakerName("Dr. Weber"), Is.EqualTo("Dr. Weber"), "eigene Sprechernamen bleiben");
        Assert.That(MediaTexts.ProviderName(SpeechProviderType.SystemVoice), Is.EqualTo("System voice (free, offline)"));
        Assert.That(MediaTexts.Minutes(7.4), Is.EqualTo("approx. 7 min"));
        Assert.That(MediaTexts.Minutes(0.5), Is.EqualTo("under 1 min"));
        Assert.That(MediaTexts.Size(4.24), Is.EqualTo("approx. 4.2 MB"));
        Assert.That(MediaTexts.Usd(0.005), Is.EqualTo("under 0.01"));
    }

    [Test, SetCulture("de-DE")]
    public void German_NumbersWithComma()
    {
        Assert.That(MediaTexts.Size(4.24), Is.EqualTo("ca. 4,2 MB"));
        Assert.That(MediaTexts.Size(0.05), Is.EqualTo("unter 0,1 MB"));
        Assert.That(MediaTexts.Size(23.4), Is.EqualTo("ca. 23 MB"));
        Assert.That(MediaTexts.Usd(0.114), Is.EqualTo("0,11"));
        Assert.That(MediaTexts.ExportTime(30), Is.EqualTo("unter 1 Min."));
        Assert.That(MediaTexts.ExportTime(130), Is.EqualTo("ca. 3 Min."));
        // Gleiche Rundung wie die (deutsche) Angabe in Core, die Exporte weiter nutzen
        Assert.That(MediaTexts.ExportTime(130), Is.EqualTo(new Mp4ExportEstimate(10, 60, 1, 130).ExportTimeText));
    }

    [Test, SetCulture("de-DE")]
    public void GeminiEstimate_ContainsDurationSizeAndCost()
    {
        var text = MediaTexts.GeminiEstimate(new GeminiTtsEstimate(480, 23_000_000, 0.114));
        Assert.That(text, Does.StartWith("ca. 8 Min. Audio · ca. 23 MB (WAV) · geschätzt 0,11 US-Dollar"));
        Assert.That(text, Does.Contain("kostenlosen Stufe"));
    }
}
