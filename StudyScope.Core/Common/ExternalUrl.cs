namespace StudyScope.Core.Common;

/// <summary>
/// Prüfung von Adressen, die im System-Browser geöffnet werden: nur absolute http- bzw. https-Adressen mit Host,
/// ohne Benutzerdaten in der Adresse. Alles andere (file:, javascript:, mailto:, relative Pfade) wird abgelehnt.
/// </summary>
public static class ExternalUrl
{
    /// <param name="httpsOnly">Nur https zulassen (Spendenknopf).</param>
    public static bool TryParse(string? value, out Uri uri, bool httpsOnly = false)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsed)) return false;

        var schemeOk = parsed.Scheme == Uri.UriSchemeHttps || (!httpsOnly && parsed.Scheme == Uri.UriSchemeHttp);
        if (!schemeOk || string.IsNullOrEmpty(parsed.Host) || !string.IsNullOrEmpty(parsed.UserInfo)) return false;

        uri = parsed;
        return true;
    }
}
