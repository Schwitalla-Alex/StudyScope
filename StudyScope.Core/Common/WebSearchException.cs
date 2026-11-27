using System.Globalization;
using StudyScope.Core.Localization;
using StudyScope.Core.Resources;

namespace StudyScope.Core.Common;

/// <summary>
/// Fehler der Websuche (HTTP-Fehler, Netzwerk, Zeitüberschreitung, unerwartete Antwort, kein Dienst eingerichtet).
/// <see cref="Exception.Message"/> ist ein verständlicher Satz mit Handlungsempfehlung für die Oberfläche,
/// die technische Antwort des Dienstes steht in <see cref="Detail"/>.
/// </summary>
public class WebSearchException : Exception
{
    public WebSearchException(string userMessage, string providerName, int? statusCode = null, string? detail = null, Exception? innerException = null)
        : base(userMessage, innerException)
    {
        ProviderName = providerName;
        StatusCode = statusCode;
        Detail = Shorten(detail);
    }

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
            providerName, statusCode, responseBody);
    }

    /// <summary>Dienst nicht erreichbar (Netzwerk, DNS, Zertifikat).</summary>
    public static WebSearchException FromNetworkError(string providerName, Exception exception) =>
        new(Loc.Format(CoreStrings.WebSearch_NotReachable, ("provider", providerName), ("error", exception.Message)),
            providerName, null, exception.Message, exception);

    /// <summary>Keine Antwort innerhalb der Wartezeit.</summary>
    public static WebSearchException Timeout(string providerName, int seconds, Exception? exception = null) =>
        new(Loc.Format(CoreStrings.WebSearch_Timeout, ("provider", providerName), ("seconds", seconds.ToString(CultureInfo.InvariantCulture))),
            providerName, null, exception?.Message, exception);

    /// <summary>Antwort ohne erkennbare Treffer-Liste.</summary>
    public static WebSearchException UnexpectedResponse(string providerName, int? statusCode, string? responseBody) =>
        new(Loc.Format(CoreStrings.WebSearch_UnexpectedResponse, ("provider", providerName)), providerName, statusCode, responseBody);

    /// <summary>Kein Suchdienst eingerichtet (bzw. Schlüssel oder Adresse fehlt).</summary>
    public static WebSearchException NotConfigured() => new(CoreStrings.WebSearch_NotConfigured, string.Empty);

    private static string Shorten(string? text)
    {
        var value = text?.Trim() ?? string.Empty;
        return value.Length <= MaxDetailLength ? value : value[..MaxDetailLength] + " …";
    }
}
