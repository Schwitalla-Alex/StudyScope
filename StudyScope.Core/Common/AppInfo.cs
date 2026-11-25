using System.Reflection;

namespace StudyScope.Core.Common;

/// <summary>
/// Programminformationen zur Laufzeit. Die Version stammt aus Directory.Build.props (gilt für alle Projekte)
/// und wird hier aus der Assembly gelesen – sie darf nirgends fest im Code stehen.
/// </summary>
public static class AppInfo
{
    public const string ProductName = "StudyScope";

    /// <summary>Quellcode auf GitHub (Über-Dialog).</summary>
    public const string RepositoryUrl = "https://github.com/Schwitalla-Alex/StudyScope";

    /// <summary>Lizenztext (Apache 2.0) im Repository.</summary>
    public const string LicenseUrl = "https://github.com/Schwitalla-Alex/StudyScope/blob/master/LICENSE";

    /// <summary>
    /// Spendenseite für „StudyScope unterstützen“ (Hilfe-Menü). Leer bzw. keine https-Adresse → Menüeintrag ausgeblendet.
    /// Dieselbe Adresse steht in README.md und .github/FUNDING.yml.
    /// </summary>
    public const string DonationUrl = "https://www.paypal.com/ncp/payment/JS8K7QTNVQGN8";

    /// <summary>Ist die Spenden-Adresse verwendbar? Nur https (strenger als andere Links).</summary>
    public static bool IsValidDonationUrl(string? url) => ExternalUrl.TryParse(url, out _, httpsOnly: true);

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
