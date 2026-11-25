namespace StudyScope.Core.Interfaces;

/// <summary>
/// Öffnet Webadressen im System-Browser – nie in der WebView der App. Erlaubt sind nur http- und https-Adressen
/// (<see cref="Common.ExternalUrl"/>).
/// </summary>
public interface IExternalLinkOpener
{
    /// <summary>Liefert false, wenn die Adresse abgelehnt wurde oder der Browser nicht gestartet werden konnte.</summary>
    bool OpenUrl(string url);
}
