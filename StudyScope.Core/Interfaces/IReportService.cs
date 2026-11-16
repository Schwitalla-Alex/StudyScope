using StudyScope.Core.Models;

namespace StudyScope.Core.Interfaces;

/// <summary>
/// Berichte: Erstellung per KI aus Dokumentauszügen nach einer Vorlage, Speicherung (mehrere je Profil)
/// und Export als Word, Markdown, Text oder interaktive HTML-Datei.
/// </summary>
public interface IReportService
{
    /// <param name="customInstruction">Nur bei <see cref="ReportTemplate.Custom"/>: Vorgabe des Benutzers.</param>
    /// <param name="fallbackTitle">Titel, falls die KI keinen liefert.</param>
    Task<ReportGenerationResult> GenerateReportAsync(
        IEnumerable<string> contextChunks,
        ReportTemplate template,
        string customInstruction,
        string fallbackTitle,
        CancellationToken cancellationToken = default);

    Task<ReportLibrary> LoadLibraryAsync(string profileName);
    Task SaveLibraryAsync(string profileName, ReportLibrary library);

    /// <summary>Word-Dokument (.docx) mit Überschriften, Listen, Begriffen und den eingebetteten Studio-Inhalten.</summary>
    Task ExportToDocxAsync(Report report, ReportStudioContent studio, string filePath);

    string ExportToMarkdown(Report report, ReportStudioContent studio);

    string ExportToPlainText(Report report, ReportStudioContent studio);

    /// <summary>Eigenständige, interaktive HTML-Datei (funktioniert ohne StudyScope und ohne Internet).</summary>
    string ExportToHtml(Report report, ReportStudioContent studio);
}
