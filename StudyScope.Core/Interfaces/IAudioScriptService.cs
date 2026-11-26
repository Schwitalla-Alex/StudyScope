using System.Globalization;
using StudyScope.Core.Models;

namespace StudyScope.Core.Interfaces;

/// <summary>
/// Audio-Zusammenfassung: Sprechskript per KI aus Dokumentauszügen (Zusammenfassung, Podcast-Gespräch, Kurz-Briefing),
/// Speicherung (mehrere Beiträge je Profil unter <c>Audio/&lt;Profil&gt;.json</c>, Audiodateien unter
/// <c>Audio/&lt;Profil&gt;/</c>) und Export des Skripts als Text oder Markdown.
/// </summary>
public interface IAudioScriptService
{
    /// <param name="chunks">Textstellen samt Quelldokument.</param>
    /// <param name="topic">Optionaler Schwerpunkt; die Textstellen werden dann per Stichwort-Treffer ausgewählt.</param>
    /// <param name="fallbackTitle">Titel, falls die KI keinen liefert.</param>
    Task<AudioScriptResult> GenerateAsync(
        IEnumerable<SourceChunk> chunks,
        AudioScriptFormat format,
        AudioScriptLength length,
        string topic,
        string fallbackTitle,
        CancellationToken cancellationToken = default);

    Task<AudioLibrary> LoadLibraryAsync(string profileName);
    Task SaveLibraryAsync(string profileName, AudioLibrary library);

    /// <summary>Ordner für die Audiodateien des Profils (wird bei Bedarf angelegt).</summary>
    string GetAudioDirectory(string profileName);

    /// <summary>Voller Pfad der Audiodatei eines Beitrags oder null, wenn keine (vorhandene) Datei existiert.</summary>
    string? GetAudioFilePath(string profileName, AudioSummary summary);

    /// <summary>Löscht die Audiodatei eines Beitrags (falls vorhanden) und leert <see cref="AudioSummary.AudioFileName"/>.</summary>
    void DeleteAudioFile(string profileName, AudioSummary summary);

    /// <summary>Pfad, unter dem eine neu erzeugte Audiodatei (WAV) eines Beitrags gespeichert wird.</summary>
    string GetTargetAudioFilePath(string profileName, AudioSummary summary);

    string ExportToText(AudioSummary summary, CultureInfo culture);
    string ExportToMarkdown(AudioSummary summary, CultureInfo culture);
}
