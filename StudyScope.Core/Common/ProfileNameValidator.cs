using StudyScope.Core.Localization;
using StudyScope.Core.Resources;
namespace StudyScope.Core.Common;

/// <summary>
/// Prüft Profilnamen, bevor daraus ein Dateiname wird. Die Regeln gelten bewusst auf allen
/// Plattformen gleich (strengste Variante = Windows), damit Profile zwischen Windows und Linux portabel bleiben.
/// </summary>
public static class ProfileNameValidator
{
    public const int MaxLength = 50;

    /// <summary>Zeichen, die unter Windows in Dateinamen verboten sind (zusätzlich alle Steuerzeichen).</summary>
    public static readonly char[] InvalidCharacters = { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };

    /// <summary>Unter Windows reservierte Gerätenamen – auch mit Endung (z. B. „CON.json“) unzulässig.</summary>
    public static readonly IReadOnlySet<string> ReservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>
    /// Liefert eine verständliche Fehlermeldung oder <c>null</c>, wenn der Name gültig ist.
    /// </summary>
    public static string? Validate(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
            return CoreStrings.Profile_NameEmpty;

        if (trimmed.Length > MaxLength)
            return Loc.Format(CoreStrings.Profile_NameTooLong, ("max", MaxLength));

        var invalid = trimmed.Where(c => InvalidCharacters.Contains(c) || char.IsControl(c)).Distinct().ToList();
        if (invalid.Count > 0)
        {
            var shown = string.Join(" ", invalid.Where(c => !char.IsControl(c)));
            return string.IsNullOrEmpty(shown)
                ? CoreStrings.Profile_NameControlChars
                : Loc.Format(CoreStrings.Profile_NameInvalidChars, ("chars", shown));
        }

        if (trimmed.EndsWith('.'))
            return CoreStrings.Profile_NameEndsWithDot;

        if (IsReservedName(trimmed))
            return Loc.Format(CoreStrings.Profile_NameReserved, ("name", trimmed));

        return null;
    }

    /// <summary>
    /// Windows prüft reservierte Namen nur am Teil vor dem ersten Punkt („CON.backup“ ist ebenfalls reserviert).
    /// </summary>
    public static bool IsReservedName(string name)
    {
        var baseName = name.Split('.')[0].TrimEnd(' ');
        return ReservedNames.Contains(baseName);
    }
}
