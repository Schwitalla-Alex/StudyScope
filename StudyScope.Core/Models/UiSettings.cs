using System.Text.Json.Serialization;

namespace StudyScope.Core.Models;

/// <summary>Farbschema der Oberfläche.</summary>
public enum ThemeMode
{
    /// <summary>Wie das Betriebssystem (hell/dunkel), Wechsel zur Laufzeit wird übernommen.</summary>
    System,
    Light,
    Dark
}

/// <summary>
/// Globale Einstellungen der Oberfläche (gehören zum Rechner, nicht zum Profil) als Eigenschaft <c>UiSettings</c>
/// in <c>config.json</c>. Wer einzelne Felder ändert, lädt vorher den gespeicherten Stand und ändert nur seine
/// Felder – sonst gehen die anderen verloren.
/// </summary>
public class UiSettings
{
    /// <summary>Linke Seitenleiste (Profil, Dokumente) eingeklappt – vom Benutzer gewählt.</summary>
    public bool SidebarCollapsed { get; set; }

    /// <summary>Gewählte Breite der Seitenleiste in CSS-Pixeln; 0 = Standardbreite (Einträge von vor dieser Einstellung).</summary>
    public double SidebarWidth { get; set; }

    /// <summary>Farbschema (fehlt in älteren Dateien → System).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<ThemeMode>))]
    public ThemeMode Theme { get; set; } = ThemeMode.System;

    public UiSettings Clone() => new() { SidebarCollapsed = SidebarCollapsed, SidebarWidth = SidebarWidth, Theme = Theme };
}
