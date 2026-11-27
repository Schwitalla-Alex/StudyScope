using StudyScope.Core.Interfaces;

namespace StudyScope.UI.Services;

/// <summary>
/// Zoom des WebViews über Photino (<c>PhotinoWindow.Zoom</c>, Prozent): Windows WebView2 <c>ZoomFactor</c>,
/// Linux WebKitGTK <c>zoom-level</c>. Photino prüft keine Grenzen und unter Windows nicht, ob der WebView schon
/// existiert – daher erst nach dem ersten Zeichnen aufrufen (den Startwert setzt <c>Program.cs</c> vor <c>Run</c>).
/// </summary>
public sealed class PhotinoWindowZoom : IWindowZoom
{
    public int Get()
    {
        try
        {
            return DesktopDialogs.MainWindow?.Zoom ?? 0;
        }
        catch
        {
            return 0; // unbekannt: kein Abgleich
        }
    }

    public void Set(int percent)
    {
        try
        {
            DesktopDialogs.MainWindow?.SetZoom(percent);
        }
        catch
        {
            // z. B. von der Plattform nicht unterstützt – die Oberfläche bleibt bei ihrer Größe
        }
    }
}
