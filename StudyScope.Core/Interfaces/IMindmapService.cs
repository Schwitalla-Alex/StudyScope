using System.Globalization;
using StudyScope.Core.Models;

namespace StudyScope.Core.Interfaces;

/// <summary>
/// Erzeugt per KI eine Mindmap aus Dokumentauszügen und speichert sie je Profil.
/// </summary>
public interface IMindmapService
{
    /// <param name="contextChunks">Textstellen der aktivierten Dokumente.</param>
    /// <param name="rootTitle">Vorschlag für das Hauptthema, falls die KI keines liefert.</param>
    Task<MindmapGenerationResult> GenerateMindmapAsync(IEnumerable<string> contextChunks, string rootTitle, CancellationToken cancellationToken = default);

    Task<MindmapDocument?> LoadMindmapAsync(string profileName);
    Task SaveMindmapAsync(string profileName, MindmapDocument mindmap);
    Task DeleteMindmapAsync(string profileName);

    /// <summary>Gliederung als Markdown (Überschriften/Aufzählung), z. B. für Obsidian oder Markmap.</summary>
    string ExportToMarkdown(MindmapDocument mindmap, CultureInfo culture);
}
