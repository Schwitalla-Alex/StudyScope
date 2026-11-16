using StudyScope.Core.Models;

namespace StudyScope.ViewModels.Mindmap;

/// <summary>Verständliche Statusmeldung: Hauptsatz, konkrete Tipps und optional die technische Antwort der KI.</summary>
public sealed record MindmapStatusText(string Message, IReadOnlyList<string> Tips, string? Details);

/// <summary>
/// Übersetzt das Ergebnis einer Mindmap-Erzeugung in Meldungen für den Benutzer.
/// Die rohe KI-Antwort steht nicht im Hauptsatz, sondern nur in den (einklappbaren) Details.
/// </summary>
public static class MindmapStatusTexts
{
    private const int MaxDetailsLength = 2000;
    private const int MaxServiceMessageLength = 200;

    /// <summary>Begriffe, an denen eine Fehlermeldung des KI-Dienstes erkannt wird.</summary>
    private static readonly string[] ErrorKeywords =
    {
        "fehler", "error", "api-schlüssel", "api-key", "api key", "apikey", "unauthorized", "forbidden",
        "timeout", "zeitüberschreitung", "nicht erreichbar", "verbindung", "connection", "quota", "rate limit",
        "kein gültiger ki-provider", "not found", "401", "403", "404", "429", "500", "503"
    };

    private const string TipFewerDocuments = "Aktivieren Sie weniger Dokumente gleichzeitig, damit die Antwort kürzer wird.";
    private const string TipOtherModel = "Wählen Sie in den Einstellungen ein größeres oder anderes Modell. Kleine lokale Modelle brechen lange Antworten oft ab.";
    private const string TipCheckSettings = "Prüfen Sie in den Einstellungen Verbindung, Modell und gegebenenfalls den API-Schlüssel („Speichern & Prüfen“).";

    public static MindmapStatusText ForFailure(MindmapGenerationResult result) =>
        ForFailure(result.FailureReason, result.RawResponse, "Mindmap", "Mindmap erstellen");

    /// <summary>
    /// Meldung für einen Misserfolg. Auch für die Präsentation genutzt: <paramref name="subject"/> ist der
    /// Gegenstand („Mindmap“, „Präsentation“), <paramref name="buttonText"/> die Beschriftung des Erstellen-Knopfs.
    /// </summary>
    public static MindmapStatusText ForFailure(MindmapFailureReason failureReason, string? rawResponse, string subject, string buttonText) =>
        ForFailure(failureReason, rawResponse, subject, buttonText, $"keine {subject}", $"einer {subject}", $"eine {subject}");

    /// <summary>
    /// Wie oben, mit frei wählbaren Wortformen (z. B. für das Quiz: „kein Quiz“, „einem Quiz“, „ein Quiz“).
    /// </summary>
    /// <param name="none">Verneinung im Akkusativ, z. B. „keine Mindmap“.</param>
    /// <param name="withArticle">Dativ nach „mit“, z. B. „einer Mindmap“.</param>
    /// <param name="forArticle">Akkusativ nach „für“, z. B. „eine Mindmap“.</param>
    public static MindmapStatusText ForFailure(MindmapFailureReason failureReason, string? rawResponse, string subject, string buttonText,
        string none, string withArticle, string forArticle)
    {
        var raw = rawResponse?.Trim() ?? string.Empty;
        var reason = failureReason != MindmapFailureReason.None
            ? failureReason
            : string.IsNullOrWhiteSpace(raw) ? MindmapFailureReason.EmptyResponse : MindmapFailureReason.InvalidJson;
        var tipRetry = $"Klicken Sie erneut auf „{buttonText}“ – die KI antwortet jedes Mal etwas anders.";

        switch (reason)
        {
            case MindmapFailureReason.NoContent:
                return new MindmapStatusText(
                    "⚠️ Aus den gewählten Dokumenten konnte kein Text gelesen werden.",
                    new[] { "Prüfen Sie, ob die Dateien noch vorhanden sind und Text enthalten (eingescannte PDFs ohne Texterkennung enthalten nur Bilder)." },
                    null);

            case MindmapFailureReason.EmptyResponse:
                return new MindmapStatusText(
                    "⚠️ Die KI hat keine Antwort geliefert.",
                    new[] { TipCheckSettings, "Bei lokalen Modellen: Läuft Ollama bzw. LM Studio und ist das Modell geladen?" },
                    null);

            case MindmapFailureReason.NoJson when LooksLikeServiceError(raw):
                return new MindmapStatusText(
                    $"⚠️ Der KI-Dienst hat einen Fehler gemeldet, daher konnte {none} erstellt werden.",
                    new[] { $"Meldung des Dienstes: „{Shorten(raw, MaxServiceMessageLength)}“", TipCheckSettings },
                    Shorten(raw, MaxDetailsLength));

            case MindmapFailureReason.NoJson:
                return new MindmapStatusText(
                    $"⚠️ Die KI hat mit normalem Text statt mit {withArticle} geantwortet.",
                    new[] { tipRetry, TipOtherModel },
                    Shorten(raw, MaxDetailsLength));

            case MindmapFailureReason.NoTopics:
                return new MindmapStatusText(
                    $"⚠️ Die KI hat in den Dokumenten keine Themen für {forArticle} gefunden.",
                    new[] { tipRetry, "Verwenden Sie Dokumente mit mehr Fließtext." },
                    Shorten(raw, MaxDetailsLength));

            default: // InvalidJson
                return new MindmapStatusText(
                    $"⚠️ Die Antwort der KI war unvollständig oder fehlerhaft, daher konnte {none} erstellt werden.",
                    new[] { tipRetry, TipFewerDocuments, TipOtherModel },
                    Shorten(raw, MaxDetailsLength));
        }
    }

    public static MindmapStatusText ForSuccess(int topicCount, bool wasRepaired) =>
        wasRepaired
            ? new MindmapStatusText(
                $"✓ Mindmap mit {topicCount} Themen erstellt. Die Antwort der KI war allerdings unvollständig oder fehlerhaft, daher fehlen eventuell einige Themen.",
                new[] { "Für eine vollständige Mindmap auf „Neu erstellen“ klicken – die KI antwortet jedes Mal etwas anders.", "Nur bei sehr umfangreichen Unterlagen: weniger Dokumente aktivieren, damit die Antwort kürzer wird." },
                null)
            : new MindmapStatusText(
                $"✓ Mindmap mit {topicCount} Themen erstellt. Klicken Sie auf ein Thema für Details.",
                Array.Empty<string>(),
                null);

    public static bool LooksLikeServiceError(string text)
    {
        var lower = text.ToLowerInvariant();
        return ErrorKeywords.Any(lower.Contains);
    }

    private static string Shorten(string text, int maxLength)
    {
        var t = text.Trim();
        return t.Length <= maxLength ? t : t[..maxLength].TrimEnd() + " …";
    }
}
