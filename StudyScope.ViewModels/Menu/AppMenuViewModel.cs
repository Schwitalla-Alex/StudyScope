using StudyScope.Core.Common;
using StudyScope.ViewModels.Resources;

namespace StudyScope.ViewModels.Menu;

/// <summary>
/// Hamburger-Menü (☰) oben links: Einträge und Zustand. Die Kennungen werden in <c>AppMenu.razor</c> ausgewertet.
/// Im Vortrags-/Vollbildmodus gibt es kein Menü (<see cref="IsAvailable"/>).
/// </summary>
public class AppMenuViewModel
{
    public const string OpenFile = "open-file";
    public const string NewProfile = "new-profile";
    public const string Settings = "settings";
    public const string SettingsAi = "settings:ai";
    public const string SettingsSpeech = "settings:speech";
    public const string SettingsExport = "settings:export";
    public const string SettingsAppearance = "settings:appearance";
    public const string Help = "help";
    public const string About = "about";
    public const string Support = "support";
    public const string Exit = "exit";

    /// <summary>Präfix für Einstellungen-Einträge; der Rest ist der Abschnitt im Einstellungsdialog.</summary>
    public const string SettingsPrefix = "settings:";

    /// <summary>Abschnitt „Darstellung“ im Einstellungsdialog ist vorhanden.</summary>
    public static bool AppearanceSettingsAvailable => true;

    public MenuNavigator Navigator { get; } = new("ss-appmenu");

    /// <summary>Menü „» Mehr“ der Reiterleiste (Reiter, die nicht in die Zeile passen).</summary>
    public MenuNavigator OverflowNavigator { get; } = new("ss-tabmore");

    /// <summary>Ist eines der Menüs offen? Dann wirkt Strg+B nicht.</summary>
    public bool IsAnyMenuOpen => Navigator.IsOpen || OverflowNavigator.IsOpen;

    /// <summary>Beide Menüs schließen (z. B. beim Start des Vortrags).</summary>
    public void CloseAll()
    {
        Navigator.Close();
        OverflowNavigator.Close();
    }

    public AppMenuViewModel() =>
        Navigator.SetItems(BuildItems(AppInfo.IsValidDonationUrl(AppInfo.DonationUrl), AppearanceSettingsAvailable));

    /// <summary>Menü nur außerhalb von Vortrag und Vollbild.</summary>
    public static bool IsAvailable(bool isPresenting) => !isPresenting;

    /// <summary>
    /// Einträge des Menüs. „StudyScope unterstützen“ nur mit gültiger Spenden-URL, „Darstellung“ erst, wenn der
    /// Abschnitt im Einstellungsdialog vorhanden ist.
    /// </summary>
    public static IReadOnlyList<MenuEntry> BuildItems(bool donationAvailable, bool appearanceAvailable)
    {
        var settings = new List<MenuEntry>
        {
            new(SettingsAi, VmStrings.Menu_SettingsAi, "🤖"),
            new(SettingsSpeech, VmStrings.Menu_SettingsSpeech, "🔊"),
            new(SettingsExport, VmStrings.Menu_SettingsExport, "🎞️"),
        };
        if (appearanceAvailable) settings.Add(new(SettingsAppearance, VmStrings.Menu_SettingsAppearance, "🎨"));

        var help = new List<MenuEntry> { new(About, VmStrings.Menu_About, "ℹ️") };
        if (donationAvailable) help.Add(new(Support, VmStrings.Menu_Support, "🤝"));

        return
        [
            new(OpenFile, VmStrings.Menu_OpenFile, "📂"),
            new(NewProfile, VmStrings.Menu_NewProfile, "➕"),
            new(Settings, VmStrings.Menu_Settings, "⚙️", settings, SeparatorBefore: true),
            new(Help, VmStrings.Menu_Help, "❓", help),
            new(Exit, VmStrings.Menu_Exit, "✕", SeparatorBefore: true, IsDanger: true),
        ];
    }

    /// <summary>Abschnitt im Einstellungsdialog zu einer Kennung („settings:speech“ → „speech“), sonst null.</summary>
    public static string? SettingsSectionOf(string id) =>
        id.StartsWith(SettingsPrefix, StringComparison.Ordinal) ? id[SettingsPrefix.Length..] : null;
}
