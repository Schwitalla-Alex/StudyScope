namespace StudyScope.Core.Interfaces;

/// <summary>
/// Zoom des WebViews im Hauptfenster (Photino: <c>PhotinoWindow.Zoom</c> → WebView2 <c>ZoomFactor</c> bzw.
/// WebKitGTK <c>zoom-level</c>). Eigene Schnittstelle, damit <c>ZoomViewModel</c> ohne Photino testbar ist.
/// Erst aufrufen, wenn die Oberfläche geladen ist (unter Windows existiert der WebView vorher noch nicht).
/// </summary>
public interface IWindowZoom
{
    /// <summary>Aktueller Zoom in Prozent (kann ein nicht gewählter Wert sein, z. B. nach einem Zoom des WebViews selbst).</summary>
    int Get();

    /// <summary>Zoom in Prozent setzen.</summary>
    void Set(int percent);
}
