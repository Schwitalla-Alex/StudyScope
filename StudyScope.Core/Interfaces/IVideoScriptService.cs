using StudyScope.Core.Models;

namespace StudyScope.Core.Interfaces;

/// <summary>
/// Videoübersicht: Sprechtexte je Folie per KI (oder aus den Sprechernotizen), Speicherung mehrerer Videos je Profil
/// unter <c>Videos/&lt;Profil&gt;.json</c> mit Audiodateien unter <c>Videos/&lt;Profil&gt;/&lt;Video&gt;/</c>
/// (eine WAV je Szene plus die daraus zusammengesetzte Datei für die Wiedergabe).
/// </summary>
public interface IVideoScriptService
{
    /// <summary>Lässt die KI je Folie einen Sprechtext schreiben; fehlende Szenen werden aus den Notizen aufgefüllt.</summary>
    Task<VideoScriptResult> GenerateAsync(PresentationDocument presentation, string fallbackTitle, CancellationToken cancellationToken = default);

    /// <summary>Video direkt aus den Sprechernotizen (ohne KI-Aufruf).</summary>
    VideoDocument CreateFromNotes(PresentationDocument presentation, string fallbackTitle);

    /// <summary>
    /// Übernimmt die aktuellen Folien der Präsentation: Szenen werden über die Folienkennung (sonst gleiche Position und
    /// gleicher Titel) zugeordnet und behalten ihren Sprechtext; neue Folien bekommen den Text aus den Notizen.
    /// Liefert die entfallenen Szenen (deren Audiodateien der Aufrufer löscht).
    /// </summary>
    IReadOnlyList<VideoScene> RefreshSlides(VideoDocument video, PresentationDocument presentation);

    Task<VideoLibrary> LoadLibraryAsync(string profileName);
    Task SaveLibraryAsync(string profileName, VideoLibrary library);

    /// <summary>Ordner für die Audiodateien eines Videos (wird bei Bedarf angelegt).</summary>
    string GetVideoDirectory(string profileName, VideoDocument video);

    /// <summary>Pfad der Szenen-Audiodatei oder null, wenn keine (vorhandene) Datei existiert.</summary>
    string? GetSceneAudioPath(string profileName, VideoDocument video, VideoScene scene);

    /// <summary>Zielpfad für eine neu erzeugte Szenen-Audiodatei.</summary>
    string GetTargetSceneAudioPath(string profileName, VideoDocument video, VideoScene scene);

    /// <summary>Pfad der zusammengesetzten Audiodatei oder null, wenn keine (vorhandene) Datei existiert.</summary>
    string? GetAudioFilePath(string profileName, VideoDocument video);

    /// <summary>
    /// Setzt die Szenen-Audiodateien zu einer Datei zusammen (Szenen ohne Sprechtext als kurze Stille), schreibt sie
    /// atomar über eine temporäre Datei und liefert die exakten Startzeiten je Szene.
    /// </summary>
    Task<VideoAudioAssembly> AssembleAudioAsync(string profileName, VideoDocument video, CancellationToken cancellationToken = default);

    /// <summary>Löscht die Audiodatei einer Szene (falls vorhanden) und leert ihre Audioangaben.</summary>
    void DeleteSceneAudio(string profileName, VideoDocument video, VideoScene scene);

    /// <summary>Löscht alle Audiodateien eines Videos (Ordner des Videos) und leert die Audioangaben.</summary>
    void DeleteAllAudio(string profileName, VideoDocument video);

    /// <summary>Sprechtexte als Text bzw. Markdown (je Folie).</summary>
    string ExportToText(VideoDocument video);
    string ExportToMarkdown(VideoDocument video);
}
