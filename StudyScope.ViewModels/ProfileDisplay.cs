using System.Globalization;
using StudyScope.Core.Localization;
using StudyScope.ViewModels.Resources;

namespace StudyScope.ViewModels;

/// <summary>
/// Anzeige von Profilnamen. Das Standardprofil heißt gespeichert immer „Standard“ (Dateiname
/// <c>Profiles/Standard.json</c>, <c>last_profile.txt</c>) und wird nur beim Anzeigen übersetzt – englisch „Default“.
/// Eigene Profile erscheinen unverändert.
/// </summary>
public static class ProfileDisplay
{
    /// <summary>Gespeicherter Name des Standardprofils (bleibt in jeder Sprache gleich).</summary>
    public const string DefaultProfileName = "Standard";

    public static bool IsDefault(string? profile) =>
        string.Equals(profile?.Trim(), DefaultProfileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Anzeigename in der Oberflächensprache: „Standard“/„Default“ für das Standardprofil, sonst der Name selbst.</summary>
    public static string Name(string? profile) =>
        IsDefault(profile) ? VmStrings.Profile_DefaultName : profile ?? string.Empty;

    /// <summary>
    /// Anzeigename, der nicht mit einem eigenen Profil verwechselt werden kann: Gibt es (noch von früher) ein eigenes
    /// Profil, das so heißt wie die Übersetzung („Default“), bleibt das Standardprofil bei „Standard“.
    /// </summary>
    public static string Name(string? profile, IEnumerable<string> allProfiles)
    {
        if (!IsDefault(profile)) return profile ?? string.Empty;
        var display = VmStrings.Profile_DefaultName;
        var clash = allProfiles.Any(p => !IsDefault(p) && string.Equals(p.Trim(), display, StringComparison.OrdinalIgnoreCase));
        return clash ? DefaultProfileName : display;
    }

    /// <summary>
    /// Namen, unter denen das Standardprofil in irgendeiner verfügbaren Sprache erscheint („Standard“, „Default“ …).
    /// Ein neues Profil mit einem dieser Namen wäre in einer anderen Sprache nicht vom Standardprofil zu unterscheiden.
    /// </summary>
    public static IReadOnlyCollection<string> DefaultNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DefaultProfileName };
        void Add(CultureInfo culture)
        {
            var value = VmStrings.ResourceManager.GetString(nameof(VmStrings.Profile_DefaultName), culture);
            if (!string.IsNullOrWhiteSpace(value)) names.Add(value.Trim());
        }

        Add(CultureInfo.InvariantCulture);
        Add(CultureInfo.CurrentUICulture);
        foreach (var name in UiLanguage.AvailableCultures(AppContext.BaseDirectory, typeof(VmStrings).Assembly.GetName().Name!))
            Add(CultureInfo.GetCultureInfo(name));
        return names;
    }

    /// <summary>Ist der Name einer der Anzeigenamen des Standardprofils (in irgendeiner Sprache)?</summary>
    public static bool IsDefaultAlias(string? name)
    {
        var n = name?.Trim();
        return !string.IsNullOrEmpty(n) && DefaultNames().Contains(n);
    }
}
