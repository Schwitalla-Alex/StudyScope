namespace StudyScope.Core.Models;

/// <summary>
/// Globale Einstellungen der Oberfläche (gehören zum Rechner, nicht zum Profil) als Eigenschaft <c>UiSettings</c>
/// in <c>config.json</c>.
/// </summary>
public class UiSettings
{
    /// <summary>Linke Seitenleiste (Profil, Dokumente) eingeklappt – vom Benutzer gewählt.</summary>
    public bool SidebarCollapsed { get; set; }

    /// <summary>Gewählte Breite der Seitenleiste in CSS-Pixeln; 0 = Standardbreite (Einträge von vor dieser Einstellung).</summary>
    public double SidebarWidth { get; set; }

    public UiSettings Clone() => new() { SidebarCollapsed = SidebarCollapsed, SidebarWidth = SidebarWidth };
}
