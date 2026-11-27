using System.Globalization;
using StudyScope.Core.Localization;
using StudyScope.Setup.Core.Resources;

namespace StudyScope.Setup.Core.Services;

/// <summary>
/// Sprache des Installationsprogramms. Sie folgt der Systemsprache (Deutsch, wenn vorhanden, sonst Englisch);
/// eine Auswahl im Assistenten gibt es nicht, und es wird nichts in die Benutzerdaten geschrieben.
/// <para>
/// Anders als die App kann das Setup die Sprachen nicht über Ordner neben der exe finden: In der Einzeldatei
/// setup.exe stecken die Satellitenassemblies im Bündel. Geprüft wird daher, ob sich die Satellitenassembly einer
/// Kultur laden lässt – eine neue Sprache braucht so keine Codeänderung.
/// </para>
/// </summary>
public static class SetupLanguage
{
    /// <summary>
    /// Bestimmt die Kultur: <see cref="UiLanguage.OverrideVariable"/> (für Prüfungen), sonst die Systemsprache,
    /// sonst Englisch bzw. Deutsch (siehe <see cref="UiLanguage.Resolve"/>).
    /// </summary>
    public static CultureInfo Resolve(string? overrideSetting, CultureInfo installed) =>
        Resolve(overrideSetting, installed, HasTexts);

    /// <summary>Wie <see cref="Resolve(string?, CultureInfo)"/>, mit austauschbarer Prüfung (Tests).</summary>
    public static CultureInfo Resolve(string? overrideSetting, CultureInfo installed, Func<CultureInfo, bool> hasTexts)
    {
        var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { UiLanguage.NeutralLanguage };
        foreach (var candidate in Candidates(overrideSetting, installed))
        {
            if (!available.Contains(candidate.Name) && hasTexts(candidate))
                available.Add(candidate.Name);
        }
        return UiLanguage.Resolve(overrideSetting, installed, available);
    }

    /// <summary>Setzt die Sprache für das ganze Setup (Aufruf ganz am Anfang von Main).</summary>
    public static CultureInfo Apply()
    {
        var culture = Resolve(Environment.GetEnvironmentVariable(UiLanguage.OverrideVariable), CultureInfo.InstalledUICulture);
        UiLanguage.Apply(culture);
        return culture;
    }

    /// <summary>
    /// Gibt es Übersetzungen genau für diese Kultur (ohne Rückfall auf die neutrale Sprache)? Die Texte der
    /// Setup-Logik stehen für jede Sprache in jedem Satz (ResourceTests), eine Satellitenassembly genügt daher als Probe.
    /// Bewusst nicht <c>ResourceManager.GetResourceSet(…, tryParents: false)</c>: Der ResourceManager merkt sich nach
    /// einem Rückfall die neutrale Fassung auch unter der angefragten Kultur und meldete sie dann als vorhanden.
    /// Im Einzeldatei-Setup lädt die Laufzeit die Satellitenassembly aus dem Bündel.
    /// </summary>
    public static bool HasTexts(CultureInfo culture)
    {
        if (string.IsNullOrEmpty(culture.Name))
            return false;
        try
        {
            return typeof(SetupCoreStrings).Assembly.GetSatelliteAssembly(culture) != null;
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            return false;
        }
    }

    /// <summary>Die Kulturen, die für die Auswahl infrage kommen: Vorgabe, Systemsprache (genau und als Sprache), Englisch.</summary>
    private static IEnumerable<CultureInfo> Candidates(string? overrideSetting, CultureInfo installed)
    {
        if (!string.IsNullOrWhiteSpace(overrideSetting) && TryGetCulture(overrideSetting.Trim()) is { } chosen)
        {
            yield return chosen;
            if (!chosen.IsNeutralCulture && TryGetCulture(chosen.TwoLetterISOLanguageName) is { } chosenLanguage)
                yield return chosenLanguage;
        }

        if (!string.IsNullOrEmpty(installed.Name))
        {
            yield return installed;
            if (!installed.IsNeutralCulture && TryGetCulture(installed.TwoLetterISOLanguageName) is { } language)
                yield return language;
        }

        if (TryGetCulture(UiLanguage.FallbackLanguage) is { } fallback)
            yield return fallback;
    }

    private static CultureInfo? TryGetCulture(string name)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(name, predefinedOnly: true);
            return string.IsNullOrEmpty(culture.Name) ? null : culture;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}
