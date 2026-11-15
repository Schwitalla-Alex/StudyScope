using System.Reflection;

namespace StudyScope.Core.Common;

/// <summary>
/// Programminformationen zur Laufzeit. Die Version stammt aus Directory.Build.props (gilt für alle Projekte)
/// und wird hier aus der Assembly gelesen – sie darf nirgends fest im Code stehen.
/// </summary>
public static class AppInfo
{
    public const string ProductName = "StudyScope";

    /// <summary>Version im Format "1.0.3" (ohne Git-Hash, den das SDK an die InformationalVersion anhängt).</summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var assembly = typeof(AppInfo).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
            return informational.Split('+')[0];

        var v = assembly.GetName().Version;
        return v == null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }
}
