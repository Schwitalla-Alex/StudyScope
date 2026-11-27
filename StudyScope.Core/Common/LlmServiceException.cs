using StudyScope.Core.Localization;
using StudyScope.Core.Resources;
namespace StudyScope.Core.Common;

/// <summary>
/// Fehler beim Aufruf eines KI-Dienstes (HTTP-Fehler, Netzwerk, unerwartete Antwort).
/// <see cref="Exception.Message"/> ist ein verständlicher deutscher Satz für die Oberfläche;
/// die technische Antwort des Dienstes steht in <see cref="Detail"/>.
/// </summary>
public class LlmServiceException : Exception
{
    public LlmServiceException(string userMessage, string providerName, int? statusCode = null, string? detail = null, Exception? innerException = null)
        : base(userMessage, innerException)
    {
        ProviderName = providerName;
        StatusCode = statusCode;
        Detail = detail ?? string.Empty;
    }

    /// <summary>Anzeigename des Dienstes, z. B. „Mistral AI“.</summary>
    public string ProviderName { get; }

    /// <summary>HTTP-Statuscode, falls der Dienst geantwortet hat.</summary>
    public int? StatusCode { get; }

    /// <summary>Technische Antwort des Dienstes (gekürzt), z. B. für „Technische Details“.</summary>
    public string Detail { get; }

    /// <summary>Text für die Oberfläche (identisch mit <see cref="Exception.Message"/>).</summary>
    public string UserMessage => Message;

    private const int MaxDetailLength = 500;

    /// <summary>Erzeugt die Ausnahme für eine HTTP-Fehlerantwort mit passender Handlungsempfehlung.</summary>
    public static LlmServiceException FromHttpStatus(string providerName, int statusCode, string? responseBody)
    {
        var hint = statusCode switch
        {
            400 => CoreStrings.Llm_Http400,
            401 or 403 => CoreStrings.Llm_Http401,
            402 => CoreStrings.Llm_Http402,
            404 => CoreStrings.Llm_Http404,
            408 or 504 => CoreStrings.Llm_Http408,
            429 => CoreStrings.Llm_Http429,
            >= 500 => CoreStrings.Llm_Http5xx,
            _ => CoreStrings.Llm_HttpOther
        };

        return new LlmServiceException(Loc.Format(CoreStrings.Llm_HttpError, ("provider", providerName), ("status", statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("hint", hint)),
            providerName, statusCode, Shorten(responseBody));
    }

    /// <summary>Erzeugt die Ausnahme für einen Netzwerkfehler (Dienst nicht erreichbar).</summary>
    public static LlmServiceException FromNetworkError(string providerName, Exception exception) =>
        new(Loc.Format(CoreStrings.Llm_NotReachable, ("provider", providerName), ("error", exception.Message)),
            providerName, null, exception.Message, exception);

    private static string Shorten(string? text)
    {
        var value = text?.Trim() ?? string.Empty;
        return value.Length <= MaxDetailLength ? value : value[..MaxDetailLength] + " …";
    }
}
