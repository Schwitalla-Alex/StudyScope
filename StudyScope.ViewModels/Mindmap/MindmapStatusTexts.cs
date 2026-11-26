using StudyScope.Core.Localization;
using StudyScope.Core.Models;
using StudyScope.ViewModels.Resources;

namespace StudyScope.ViewModels.Mindmap;

/// <summary>Verständliche Statusmeldung: Hauptsatz, konkrete Tipps und optional die technische Antwort der KI.</summary>
public sealed record MindmapStatusText(string Message, IReadOnlyList<string> Tips, string? Details);

/// <summary>
/// Was erzeugt werden sollte. Die Meldungen wählen daraus per ICU-<c>select</c> die passende Wortform
/// („keine Mindmap“, „einem Quiz“ …) – jede Sprache legt ihre Formen selbst in der Ressource fest.
/// </summary>
public enum GenerationSubject
{
    Mindmap,
    Presentation,
    TopicList,
    Quiz,
    AudioScript,
    DataTable,
    NewRows,
    Report,
    VideoScript
}

/// <summary>
/// Übersetzt das Ergebnis einer KI-Erzeugung (Mindmap, Präsentation, Quiz, …) in Meldungen für den Benutzer.
/// Die rohe KI-Antwort steht nicht im Hauptsatz, sondern nur in den (einklappbaren) Details.
/// </summary>
public static class MindmapStatusTexts
{
    private const int MaxDetailsLength = 2000;
    private const int MaxServiceMessageLength = 200;

    /// <summary>Begriffe, an denen eine Fehlermeldung des KI-Dienstes erkannt wird (Dienste antworten deutsch oder englisch).</summary>
    private static readonly string[] ErrorKeywords =
    {
        "fehler", "error", "api-schlüssel", "api-key", "api key", "apikey", "unauthorized", "forbidden",
        "timeout", "zeitüberschreitung", "nicht erreichbar", "verbindung", "connection", "quota", "rate limit",
        "kein gültiger ki-provider", "not found", "401", "403", "404", "429", "500", "503"
    };

    public static MindmapStatusText ForFailure(MindmapGenerationResult result) =>
        ForFailure(result.FailureReason, result.RawResponse, GenerationSubject.Mindmap, VmStrings.Mindmap_Create);

    /// <summary>Wert für das ICU-<c>select</c> der Meldungen (z. B. „mindmap“, „topicList“).</summary>
    public static string SelectKey(GenerationSubject subject)
    {
        var name = subject.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    /// <summary>
    /// Meldung für einen Misserfolg.
    /// </summary>
    /// <param name="subject">Was erzeugt werden sollte.</param>
    /// <param name="buttonText">Beschriftung des Knopfs, mit dem man es erneut versucht (übersetzt).</param>
    public static MindmapStatusText ForFailure(MindmapFailureReason failureReason, string? rawResponse, GenerationSubject subject, string buttonText)
    {
        var raw = rawResponse?.Trim() ?? string.Empty;
        var reason = failureReason != MindmapFailureReason.None
            ? failureReason
            : string.IsNullOrWhiteSpace(raw) ? MindmapFailureReason.EmptyResponse : MindmapFailureReason.InvalidJson;
        var key = SelectKey(subject);
        var tipRetry = Loc.Format(VmStrings.GenStatus_TipRetry, ("button", buttonText));

        switch (reason)
        {
            case MindmapFailureReason.NoContent:
                return new MindmapStatusText(VmStrings.GenStatus_NoContent, new[] { VmStrings.GenStatus_NoContent_Tip }, null);

            case MindmapFailureReason.EmptyResponse:
                return new MindmapStatusText(
                    VmStrings.GenStatus_EmptyResponse,
                    new[] { VmStrings.GenStatus_TipCheckSettings, VmStrings.GenStatus_EmptyResponse_TipLocal },
                    null);

            case MindmapFailureReason.NoJson when LooksLikeServiceError(raw):
                return new MindmapStatusText(
                    Loc.Format(VmStrings.GenStatus_ServiceError, ("subject", key)),
                    new[] { Loc.Format(VmStrings.GenStatus_ServiceMessage, ("message", Shorten(raw, MaxServiceMessageLength))), VmStrings.GenStatus_TipCheckSettings },
                    Shorten(raw, MaxDetailsLength));

            case MindmapFailureReason.NoJson:
                return new MindmapStatusText(
                    Loc.Format(VmStrings.GenStatus_PlainText, ("subject", key)),
                    new[] { tipRetry, VmStrings.GenStatus_TipOtherModel },
                    Shorten(raw, MaxDetailsLength));

            case MindmapFailureReason.NoTopics:
                return new MindmapStatusText(
                    Loc.Format(VmStrings.GenStatus_NoTopics, ("subject", key)),
                    new[] { tipRetry, VmStrings.GenStatus_NoTopics_Tip },
                    Shorten(raw, MaxDetailsLength));

            default: // InvalidJson
                return new MindmapStatusText(
                    Loc.Format(VmStrings.GenStatus_InvalidJson, ("subject", key)),
                    new[] { tipRetry, VmStrings.GenStatus_TipFewerDocuments, VmStrings.GenStatus_TipOtherModel },
                    Shorten(raw, MaxDetailsLength));
        }
    }

    public static MindmapStatusText ForSuccess(int topicCount, bool wasRepaired) =>
        wasRepaired
            ? new MindmapStatusText(
                Loc.Format(VmStrings.Mindmap_SuccessRepaired, ("count", topicCount)),
                new[]
                {
                    Loc.Format(VmStrings.Mindmap_SuccessRepaired_TipRegenerate, ("button", VmStrings.Common_Regenerate)),
                    VmStrings.Mindmap_SuccessRepaired_TipFewer
                },
                null)
            : new MindmapStatusText(Loc.Format(VmStrings.Mindmap_Success, ("count", topicCount)), Array.Empty<string>(), null);

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
