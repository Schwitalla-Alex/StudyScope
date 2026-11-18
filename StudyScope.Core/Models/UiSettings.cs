namespace StudyScope.Core.Models;

/// <summary>
/// Globale Einstellungen der Oberfläche (gehören zum Rechner, nicht zum Profil) als Eigenschaft <c>UiSettings</c>
/// in <c>config.json</c>.
/// </summary>
public class UiSettings
{
    /// <summary>Linke Seitenleiste (Profil, Dokumente) eingeklappt – vom Benutzer gewählt.</summary>
    public bool SidebarCollapsed { get; set; }

    public UiSettings Clone() => new() { SidebarCollapsed = SidebarCollapsed };
}
