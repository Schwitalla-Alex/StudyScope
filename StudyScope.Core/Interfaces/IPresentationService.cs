using StudyScope.Core.Models;

namespace StudyScope.Core.Interfaces;

/// <summary>
/// Erstellt Präsentationen (per KI aus Dokumentauszügen oder aus einer Mindmap),
/// speichert sie je Profil und exportiert sie als PowerPoint-Datei.
/// </summary>
public interface IPresentationService
{
    /// <param name="contextChunks">Textstellen der aktivierten Dokumente.</param>
    /// <param name="fallbackTitle">Titel, falls die KI keinen liefert.</param>
    /// <param name="slideCount">Gewünschte Anzahl Inhaltsfolien (ohne Titelfolie).</param>
    Task<PresentationGenerationResult> GenerateFromDocumentsAsync(
        IEnumerable<string> contextChunks, string fallbackTitle, int slideCount, CancellationToken cancellationToken = default);

    /// <summary>Leitet eine Präsentation aus einer Mindmap ab (ohne KI-Aufruf).</summary>
    PresentationDocument CreateFromMindmap(MindmapDocument mindmap);

    Task<PresentationDocument?> LoadPresentationAsync(string profileName);
    Task SavePresentationAsync(string profileName, PresentationDocument presentation);
    Task DeletePresentationAsync(string profileName);

    /// <summary>Schreibt die Präsentation als PowerPoint-Datei (.pptx, 16:9, mit Sprechernotizen).</summary>
    Task ExportToPptxAsync(PresentationDocument presentation, string filePath);
}
