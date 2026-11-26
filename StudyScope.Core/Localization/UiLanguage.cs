using System.Globalization;

namespace StudyScope.Core.Localization;

/// <summary>
/// Oberflächensprache: welche Sprachen es gibt, welche gilt und wie sie gesetzt wird.
/// Die Liste ergibt sich aus den Satellitenassemblies neben der App (Ordner je Kultur mit
/// <c>&lt;Assembly&gt;.resources.dll</c>), eine neue Sprache braucht daher keine Codeänderung.
/// </summary>
public static class UiLanguage
{
    /// <summary>Sprache der neutralen Ressourcen (Original der Texte).</summary>
    public const string NeutralLanguage = "de";

    /// <summary>Rückfall, wenn die Systemsprache nicht unterstützt wird.</summary>
    public const string FallbackLanguage = "en";

    /// <summary>Einstellungswert „wie das System“.</summary>
    public const string SystemSetting = "system";

    /// <summary>Umgebungsvariable, die die Oberflächensprache für Tests und Prüfungen erzwingt (z. B. „en“).</summary>
    public const string OverrideVariable = "STUDYSCOPE_UI_CULTURE";

    /// <summary>
    /// Vorhandene Sprachen: die neutrale Sprache plus jede Kultur, für die neben <paramref name="baseDirectory"/>
    /// ein Ordner mit <c>&lt;assemblyName&gt;.resources.dll</c> liegt. Sortiert nach Kulturname.
    /// </summary>
    public static IReadOnlyList<string> AvailableCultures(string baseDirectory, string assemblyName)
    {
        var result = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { NeutralLanguage };
        if (!Directory.Exists(baseDirectory))
            return result.ToList();

        foreach (var dir in Directory.EnumerateDirectories(baseDirectory))
        {
            var name = Path.GetFileName(dir);
            if (TryGetCulture(name) is not { } culture)
                continue;
            if (File.Exists(Path.Combine(dir, assemblyName + ".resources.dll")))
                result.Add(culture.Name);
        }
        return result.ToList();
    }

    /// <summary>
    /// Bestimmt die Kultur aus der Einstellung: ein Kulturname, wenn er (oder seine Sprache) vorhanden ist,
    /// sonst bei „system“, leer oder unbekannt die Systemsprache, falls vorhanden, sonst <see cref="FallbackLanguage"/>
    /// bzw. die neutrale Sprache, wenn es nur diese gibt.
    /// </summary>
    public static CultureInfo Resolve(string? setting, CultureInfo installed, IReadOnlyCollection<string> available)
    {
        if (!string.IsNullOrWhiteSpace(setting)
            && !string.Equals(setting.Trim(), SystemSetting, StringComparison.OrdinalIgnoreCase)
            && Match(TryGetCulture(setting.Trim()), available) is { } chosen)
            return chosen;

        if (Match(installed, available) is { } system)
            return system;

        return CultureInfo.GetCultureInfo(
            available.Contains(FallbackLanguage, StringComparer.OrdinalIgnoreCase) ? FallbackLanguage : NeutralLanguage);
    }

    /// <summary>Setzt Oberflächen- und Formatkultur für den aktuellen und alle künftigen Threads.</summary>
    public static void Apply(CultureInfo culture)
    {
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
    }

    /// <summary>
    /// Passt die Kultur an die vorhandenen Sprachen an: genau vorhanden (de-AT, wenn es de-AT gibt),
    /// sonst die Sprache (de-AT → de). Bei der Sprache bleibt die regionale Formatkultur erhalten.
    /// </summary>
    private static CultureInfo? Match(CultureInfo? culture, IReadOnlyCollection<string> available)
    {
        if (culture == null || string.IsNullOrEmpty(culture.Name))
            return null;
        if (available.Contains(culture.Name, StringComparer.OrdinalIgnoreCase))
            return culture;
        return available.Contains(culture.TwoLetterISOLanguageName, StringComparer.OrdinalIgnoreCase) ? culture : null;
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
