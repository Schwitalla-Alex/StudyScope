using System.Diagnostics;
using System.Runtime.InteropServices;
using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;

namespace StudyScope.Infrastructure.Services;

/// <summary>
/// Öffnet http-/https-Adressen im Standardbrowser: Windows über die Shell, Linux über <c>xdg-open</c>, macOS über
/// <c>open</c> (Adresse immer als einzelnes Argument über <see cref="ProcessStartInfo.ArgumentList"/>).
/// Der Prozessstart ist für Tests austauschbar.
/// </summary>
public class ExternalLinkOpener : IExternalLinkOpener
{
    private readonly Func<ProcessStartInfo, bool> _start;
    private readonly OSPlatform _platform;

    public ExternalLinkOpener() : this(DefaultStart, CurrentPlatform()) { }

    public ExternalLinkOpener(Func<ProcessStartInfo, bool> start, OSPlatform platform)
    {
        _start = start;
        _platform = platform;
    }

    public bool OpenUrl(string url)
    {
        if (!ExternalUrl.TryParse(url, out var uri)) return false;
        var info = CreateStartInfo(uri.AbsoluteUri, _platform);
        if (info is null) return false;
        try
        {
            return _start(info);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Browser konnte nicht gestartet werden: {ex.Message}");
            return false;
        }
    }

    /// <summary>Startinformationen je Betriebssystem; null bei unbekanntem System.</summary>
    public static ProcessStartInfo? CreateStartInfo(string absoluteUrl, OSPlatform platform)
    {
        if (platform == OSPlatform.Windows)
            return new ProcessStartInfo(absoluteUrl) { UseShellExecute = true };

        string? tool = platform == OSPlatform.Linux ? "xdg-open" : platform == OSPlatform.OSX ? "open" : null;
        if (tool is null) return null;
        var info = new ProcessStartInfo(tool) { UseShellExecute = false };
        info.ArgumentList.Add(absoluteUrl);
        return info;
    }

    private static bool DefaultStart(ProcessStartInfo info)
    {
        using var process = Process.Start(info);
        return true;
    }

    private static OSPlatform CurrentPlatform() =>
        OperatingSystem.IsWindows() ? OSPlatform.Windows
        : OperatingSystem.IsMacOS() ? OSPlatform.OSX
        : OSPlatform.Linux;
}
