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

    /// <summary>
    /// Sprache der Oberfläche: <c>system</c> (wie das Betriebssystem) oder ein Kulturname wie <c>de</c>/<c>en</c>.
    /// Fehlt in älteren Dateien → <c>system</c>.
    /// </summary>
    public string Language { get; set; } = Localization.UiLanguage.SystemSetting;

    /// <summary>
    /// Sprache der KI-Ergebnisse als Rückfall für Profile ohne eigene Einstellung („ui“, „documents“ oder Kulturname);
    /// null = nicht eingestellt → „ui“. Maßgeblich ist die Einstellung im Profil.
    /// </summary>
    public string? OutputLanguage { get; set; }

    /// <summary>
    /// Zoom der ganzen Oberfläche in Prozent (Stufen in <see cref="Common.UiZoom"/>); 0 bzw. fehlend in älteren
    /// Dateien = 100.
    /// </summary>
    public int Zoom { get; set; }

    public UiSettings Clone() => new() { SidebarCollapsed = SidebarCollapsed, SidebarWidth = SidebarWidth, Theme = Theme, Language = Language, OutputLanguage = OutputLanguage, Zoom = Zoom };
}
