using StudyScope.Core.Localization;
using StudyScope.Core.Models;
using StudyScope.ViewModels.Resources;

namespace StudyScope.ViewModels;

/// <summary>
/// Übersetzte Anzeige für Audio und Video: Formate, Längen, Sprecherrollen, Sprachausgabe-Anbieter sowie Dauer, Größe
/// und Kosten in der Schreibweise der Oberflächensprache. Gespeicherte Werte und Prompts verwenden weiter die deutschen
/// Angaben aus <see cref="AudioScriptFormats"/> (z. B. der Sprecher „Moderatorin“); übersetzt wird nur beim Anzeigen.
/// </summary>
public static class MediaTexts
{
    public static string FormatName(AudioScriptFormat format) => format switch
    {
        AudioScriptFormat.Podcast => VmStrings.AudioFormat_Podcast,
        AudioScriptFormat.Briefing => VmStrings.AudioFormat_Briefing,
        _ => VmStrings.AudioFormat_Summary
    };

    public static string FormatDescription(AudioScriptFormat format) => format switch
    {
        AudioScriptFormat.Podcast => VmStrings.AudioFormat_Podcast_Desc,
        AudioScriptFormat.Briefing => VmStrings.AudioFormat_Briefing_Desc,
        _ => VmStrings.AudioFormat_Summary_Desc
    };

    public static string LengthName(AudioScriptLength length) => length switch
    {
        AudioScriptLength.Short => VmStrings.AudioLength_Short,
        AudioScriptLength.Long => VmStrings.AudioLength_Long,
        _ => VmStrings.AudioLength_Medium
    };

    /// <summary>Anzeige eines gespeicherten Sprechers: bekannte Rollen übersetzt, eigene Namen unverändert.</summary>
    public static string SpeakerName(string? speaker) => speaker?.Trim() switch
    {
        AudioScriptFormats.NarratorName => VmStrings.Speaker_Narrator,
        AudioScriptFormats.HostName => VmStrings.Speaker_Host,
        AudioScriptFormats.ExpertName => VmStrings.Speaker_Expert,
        _ => speaker ?? string.Empty
    };

    public static string ProviderName(SpeechProviderType provider) => provider switch
    {
        SpeechProviderType.GeminiTts => VmStrings.SpeechProvider_Gemini,
        _ => VmStrings.SpeechProvider_System
    };

    /// <summary>„ca. 7 Min.“ bzw. „unter 1 Min.“ (wie <see cref="MediaFormat.Minutes"/>).</summary>
    public static string Minutes(double minutes) => MediaFormat.Minutes(minutes);

    /// <summary>Zahl in Megabyte: unter 10 mit einer Nachkommastelle, sonst ganzzahlig (Formatkultur der Oberfläche).</summary>
    public static string MegabyteNumber(double megabytes) => MediaFormat.MegabyteNumber(megabytes);

    /// <summary>„ca. 4,2 MB“ bzw. „unter 0,1 MB“.</summary>
    public static string Size(double megabytes) => MediaFormat.Size(megabytes);

    /// <summary>Zahl in Megabyte für Meldungen „(… MB)“: sehr kleine Dateien als „unter 0,1“.</summary>
    public static string SizeNumber(double megabytes) => MediaFormat.SizeNumber(megabytes);

    /// <summary>Betrag in US-Dollar mit zwei Nachkommastellen, sehr kleine Beträge als „unter 0,01“.</summary>
    public static string Usd(double usd) => MediaFormat.Amount(usd);

    /// <summary>Schätzung für Gemini TTS: Dauer, Größe (WAV), Kosten und Hinweis auf die kostenlose Stufe.</summary>
    public static string GeminiEstimate(GeminiTtsEstimate estimate) =>
        Loc.Format(VmStrings.Media_GeminiEstimate,
            ("minutes", Minutes(estimate.Seconds / 60)),
            ("size", MegabyteNumber(estimate.Megabytes)),
            ("usd", Usd(estimate.Usd)));

    /// <summary>Exportdauer: „unter 1 Min.“ bzw. „ca. 3 Min.“ (aufgerundet, wie <c>Mp4ExportEstimate.ExportTimeText</c>).</summary>
    public static string ExportTime(double seconds) => MediaFormat.MinutesRoundedUp(seconds);
}
