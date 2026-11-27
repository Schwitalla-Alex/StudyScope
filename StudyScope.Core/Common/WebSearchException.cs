using System.Globalization;
using StudyScope.Core.Localization;
using StudyScope.Core.Resources;

namespace StudyScope.Core.Common;

/// <summary>Art eines Fehlers der Websuche – entscheidet, ob die Einstellungen helfen und ob trotzdem gespeichert werden darf.</summary>
public enum WebSearchErrorKind
{
    /// <summary>Unerwartete Antwort oder sonstiger Fehler.</summary>
    Unexpected,
    /// <summary>Kein Suchdienst eingerichtet (bzw. Schlüssel oder Adresse fehlt).</summary>
    NotConfigured,
    /// <summary>Zugang abgelehnt bzw. falsch eingerichtet: Schlüssel ungültig, SearXNG ohne JSON, Adresse falsch.</summary>
    Rejected,
    /// <summary>Kontingent oder Grenze erreicht (429/432/433) – der Zugang selbst gilt als gültig.</summary>
    LimitReached,
    /// <summary>Dienst nicht erreichbar: Netzwerk, Zeitüberschreitung, Serverfehler (5xx).</summary>
    Unreachable
}

/// <summary>
/// Fehler der Websuche (HTTP-Fehler, Netzwerk, Zeitüberschreitung, unerwartete Antwort, kein Dienst eingerichtet).
/// <see cref="Exception.Message"/> ist ein verständlicher Satz mit Handlungsempfehlung für die Oberfläche,
/// die technische Antwort des Dienstes steht in <see cref="Detail"/>.
/// </summary>
public class WebSearchException : Exception
{
    public WebSearchException(string userMessage, string providerName, int? statusCode = null, string? detail = null, Exception? innerException = null,
        WebSearchErrorKind kind = WebSearchErrorKind.Unexpected)
        : base(userMessage, innerException)
    {
        Kind = kind;
        ProviderName = providerName;
        StatusCode = statusCode;
        Detail = Shorten(detail);
    }

    /// <summary>Art des Fehlers.</summary>
    public WebSearchErrorKind Kind { get; }

    /// <summary>Die Einstellungen der Websuche können helfen (Schlüssel abgelehnt, kein Dienst, SearXNG falsch eingerichtet).</summary>
    public bool SettingsCanHelp => Kind is WebSearchErrorKind.Rejected or WebSearchErrorKind.NotConfigured;

    /// <summary>Name des Suchdienstes, z. B. „Tavily“.</summary>
    public string ProviderName { get; }

    /// <summary>HTTP-Statuscode, falls der Dienst geantwortet hat.</summary>
    public int? StatusCode { get; }

    /// <summary>Technische Antwort des Dienstes (gekürzt).</summary>
    public string Detail { get; }

    /// <summary>Text für die Oberfläche (identisch mit <see cref="Exception.Message"/>).</summary>
    public string UserMessage => Message;

    private const int MaxDetailLength = 500;

    /// <summary>
    /// HTTP-Fehler mit passender Handlungsempfehlung. Tavily: 401/403 Schlüssel, 429 zu viele Anfragen,
    /// 432 Monatskontingent bzw. Tarifgrenze, 433 Pay-as-you-go-Grenze.
    /// </summary>
    public static WebSearchException FromHttpStatus(string providerName, int statusCode, string? responseBody)
    {
        var hint = statusCode switch
        {
            400 => CoreStrings.WebSearch_Http400,
            401 or 403 => CoreStrings.WebSearch_Http401,
            404 => CoreStrings.WebSearch_Http404,
            408 or 504 => CoreStrings.WebSearch_Http408,
            429 => CoreStrings.WebSearch_Http429,
            432 => CoreStrings.WebSearch_Http432,
            433 => CoreStrings.WebSearch_Http433,
            >= 500 => CoreStrings.WebSearch_Http5xx,
            _ => CoreStrings.WebSearch_HttpOther
        };

        return new WebSearchException(
            Loc.Format(CoreStrings.WebSearch_HttpError, ("provider", providerName), ("status", statusCode.ToString(CultureInfo.InvariantCulture)), ("hint", hint)),
            providerName, statusCode, responseBody, kind: KindOf(statusCode));
    }

    /// <summary>Art eines HTTP-Fehlers: 401/403/404 abgelehnt, 429/432/433 Grenze, 408/5xx nicht erreichbar.</summary>
    public static WebSearchErrorKind KindOf(int statusCode) => statusCode switch
    {
        401 or 403 or 404 => WebSearchErrorKind.Rejected,
        429 or 432 or 433 => WebSearchErrorKind.LimitReached,
        408 or >= 500 => WebSearchErrorKind.Unreachable,
        _ => WebSearchErrorKind.Unexpected
    };

    /// <summary>Dienst nicht erreichbar (Netzwerk, DNS, Zertifikat).</summary>
    public static WebSearchException FromNetworkError(string providerName, Exception exception) =>
        new(Loc.Format(CoreStrings.WebSearch_NotReachable, ("provider", providerName), ("error", exception.Message)),
            providerName, null, exception.Message, exception, WebSearchErrorKind.Unreachable);

    /// <summary>Keine Antwort innerhalb der Wartezeit.</summary>
    public static WebSearchException Timeout(string providerName, int seconds, Exception? exception = null) =>
        new(Loc.Format(CoreStrings.WebSearch_Timeout, ("provider", providerName), ("seconds", seconds.ToString(CultureInfo.InvariantCulture))),
            providerName, null, exception?.Message, exception, WebSearchErrorKind.Unreachable);

    /// <summary>Antwort ohne erkennbare Treffer-Liste.</summary>
    public static WebSearchException UnexpectedResponse(string providerName, int? statusCode, string? responseBody) =>
        new(Loc.Format(CoreStrings.WebSearch_UnexpectedResponse, ("provider", providerName)), providerName, statusCode, responseBody);

    /// <summary>Kein Suchdienst eingerichtet (bzw. Schlüssel oder Adresse fehlt).</summary>
    public static WebSearchException NotConfigured() => new(CoreStrings.WebSearch_NotConfigured, string.Empty, kind: WebSearchErrorKind.NotConfigured);

    private static string Shorten(string? text)
    {
        var value = text?.Trim() ?? string.Empty;
        return value.Length <= MaxDetailLength ? value : value[..MaxDetailLength] + " …";
    }
}
