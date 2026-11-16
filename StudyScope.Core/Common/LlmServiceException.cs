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
            400 => "Die Anfrage wurde abgelehnt. Bitte Modell und Einstellungen prüfen.",
            401 or 403 => "Der API-Schlüssel wurde abgelehnt. Bitte den Schlüssel in den Einstellungen prüfen.",
            404 => "Modell oder Adresse wurde nicht gefunden. Bitte Modell und Server-URL in den Einstellungen prüfen.",
            408 or 504 => "Der KI-Dienst hat nicht rechtzeitig geantwortet. Bitte später erneut versuchen.",
            429 => "Zu viele Anfragen oder das Kontingent ist erschöpft. Bitte kurz warten und erneut versuchen.",
            >= 500 => "Der KI-Dienst ist gerade gestört oder überlastet. Bitte in einigen Minuten erneut versuchen.",
            _ => "Der KI-Dienst hat die Anfrage nicht beantwortet."
        };

        return new LlmServiceException($"{providerName} meldet einen Fehler (HTTP {statusCode}). {hint}",
            providerName, statusCode, Shorten(responseBody));
    }

    /// <summary>Erzeugt die Ausnahme für einen Netzwerkfehler (Dienst nicht erreichbar).</summary>
    public static LlmServiceException FromNetworkError(string providerName, Exception exception) =>
        new($"{providerName} ist nicht erreichbar. Bitte Internetverbindung bzw. Server-Adresse prüfen ({exception.Message}).",
            providerName, null, exception.Message, exception);

    private static string Shorten(string? text)
    {
        var value = text?.Trim() ?? string.Empty;
        return value.Length <= MaxDetailLength ? value : value[..MaxDetailLength] + " …";
    }
}
