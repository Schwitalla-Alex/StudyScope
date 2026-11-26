using StudyScope.Core.Localization;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.Resources;

namespace StudyScope.Infrastructure.Services;

/// <summary>
/// Übersetzte Namen gespeicherter Werte für Exporte: Vorlagen, Audio-Formate, Sprecherrollen, Lernstand und Datum.
/// Gespeichert und in Prompts bleiben die deutschen Werte aus Core (<see cref="ReportTemplates"/>,
/// <see cref="DataTableTemplates"/>, <see cref="AudioScriptFormats"/>); die Exporte zeigen die Sprache, die der
/// Aufrufer per <see cref="CultureScope"/> gesetzt hat.
/// </summary>
public static class ExportNames
{
    public static string Template(ReportTemplate template) => template switch
    {
        ReportTemplate.StudyGuide => ExportStrings.ReportTemplate_StudyGuide,
        ReportTemplate.Briefing => ExportStrings.ReportTemplate_Briefing,
        ReportTemplate.Glossary => ExportStrings.ReportTemplate_Glossary,
        ReportTemplate.Faq => ExportStrings.ReportTemplate_Faq,
        _ => ExportStrings.ReportTemplate_Custom
    };

    public static string Template(DataTableTemplate template) => template switch
    {
        DataTableTemplate.Terms => ExportStrings.DtTemplate_Terms,
        DataTableTemplate.Comparison => ExportStrings.DtTemplate_Comparison,
        DataTableTemplate.Timeline => ExportStrings.DtTemplate_Timeline,
        DataTableTemplate.People => ExportStrings.DtTemplate_People,
        DataTableTemplate.Formulas => ExportStrings.DtTemplate_Formulas,
        _ => ExportStrings.DtTemplate_Custom
    };

    public static string Format(AudioScriptFormat format) => format switch
    {
        AudioScriptFormat.Podcast => ExportStrings.AudioFormat_Podcast,
        AudioScriptFormat.Briefing => ExportStrings.AudioFormat_Briefing,
        _ => ExportStrings.AudioFormat_Summary
    };

    /// <summary>Feste Sprecherrollen übersetzt („Moderatorin“ → „Host“), andere Namen unverändert.</summary>
    public static string Speaker(string? speaker) => speaker?.Trim() switch
    {
        AudioScriptFormats.NarratorName => ExportStrings.Speaker_Narrator,
        AudioScriptFormats.HostName => ExportStrings.Speaker_Host,
        AudioScriptFormats.ExpertName => ExportStrings.Speaker_Expert,
        _ => speaker ?? string.Empty
    };

    public static string FlashcardStatus(FlashcardStatus status) => status switch
    {
        Core.Models.FlashcardStatus.Known => ExportStrings.Flashcards_StatusKnown,
        Core.Models.FlashcardStatus.Difficult => ExportStrings.Flashcards_StatusDifficult,
        _ => ExportStrings.Flashcards_StatusNew
    };

    /// <summary>Datum mit Uhrzeit im kurzen Format der Exportsprache („26.11.2026 14:30“, „11/26/2026 2:30 PM“).</summary>
    public static string DateTime(DateTime value) => value.ToString("g");

    /// <summary>Datum ohne Uhrzeit im kurzen Format der Exportsprache.</summary>
    public static string Date(DateTime value) => value.ToString("d");

    /// <summary>„erstellt am …“ (mit Uhrzeit).</summary>
    public static string CreatedAt(DateTime value) => Loc.Format(ExportStrings.Export_CreatedAt, ("date", DateTime(value)));

    /// <summary>„erstellt am …“ (nur Datum).</summary>
    public static string CreatedOn(DateTime value) => Loc.Format(ExportStrings.Export_CreatedAt, ("date", Date(value)));

    /// <summary>„Quellen: a.pdf, b.pdf“.</summary>
    public static string Sources(IEnumerable<string> sources) => Loc.Format(ExportStrings.Export_Sources, ("sources", string.Join(", ", sources)));
}
