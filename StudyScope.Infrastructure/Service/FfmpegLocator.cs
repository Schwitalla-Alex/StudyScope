namespace StudyScope.Infrastructure.Services;

/// <summary>
/// Mögliche Orte von ffmpeg in der Reihenfolge der Prüfung: eigener Pfad aus den Einstellungen, PATH, unter Windows
/// zusätzlich WinGet (Links und Paketordner, z. B. nach „winget install Gyan.FFmpeg“ ohne Neuanmeldung), Chocolatey,
/// Scoop und übliche Ordner; unter Linux /usr/bin, /usr/local/bin und Snap. Dateisystem und Umgebung sind austauschbar
/// (Tests ohne echtes ffmpeg).
/// </summary>
public sealed class FfmpegLocator
{
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, bool> _directoryExists;
    private readonly Func<string, string?> _environment;
    private readonly Func<string, IEnumerable<string>> _findPackageExecutables;
    private readonly bool _isWindows;

    public FfmpegLocator(Func<string, bool> fileExists, Func<string, bool> directoryExists, Func<string, string?> environment,
        Func<string, IEnumerable<string>> findPackageExecutables, bool isWindows)
    {
        _fileExists = fileExists;
        _directoryExists = directoryExists;
        _environment = environment;
        _findPackageExecutables = findPackageExecutables;
        _isWindows = isWindows;
    }

    /// <summary>Echtes Dateisystem und echte Umgebung.</summary>
    public static FfmpegLocator Default { get; } = new(File.Exists, Directory.Exists, Environment.GetEnvironmentVariable,
        FindWinGetPackageExecutables, OperatingSystem.IsWindows());

    public string ExecutableName => _isWindows ? "ffmpeg.exe" : "ffmpeg";

    /// <summary>Kandidaten für einen eingetragenen Pfad: die Datei selbst oder – bei einem Ordner – ffmpeg darin bzw. in „bin“.</summary>
    public IReadOnlyList<string> CustomCandidates(string customPath)
    {
        var path = customPath.Trim().Trim('"');
        if (path.Length == 0) return Array.Empty<string>();
        if (_directoryExists(path))
            return new[] { Path.Combine(path, ExecutableName), Path.Combine(path, "bin", ExecutableName) };
        return new[] { path };
    }

    /// <summary>Alle vorhandenen Kandidaten (ohne Doppelte), eigener Pfad zuerst.</summary>
    public IReadOnlyList<string> Candidates(string? customPath)
    {
        var list = new List<string>();
        if (!string.IsNullOrWhiteSpace(customPath))
            list.AddRange(CustomCandidates(customPath));

        var separator = _isWindows ? ';' : ':';
        foreach (var dir in (_environment("PATH") ?? string.Empty).Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            list.Add(Path.Combine(dir.Trim('"'), ExecutableName));

        if (_isWindows)
        {
            var local = _environment("LOCALAPPDATA");
            if (!string.IsNullOrEmpty(local))
            {
                list.Add(Path.Combine(local, "Microsoft", "WinGet", "Links", ExecutableName));
                list.AddRange(_findPackageExecutables(Path.Combine(local, "Microsoft", "WinGet", "Packages")));
            }
            var programFiles = _environment("ProgramFiles");
            if (!string.IsNullOrEmpty(programFiles))
                list.Add(Path.Combine(programFiles, "ffmpeg", "bin", ExecutableName));
            var programData = _environment("ProgramData");
            if (!string.IsNullOrEmpty(programData))
                list.Add(Path.Combine(programData, "chocolatey", "bin", ExecutableName));
            var profile = _environment("USERPROFILE");
            if (!string.IsNullOrEmpty(profile))
                list.Add(Path.Combine(profile, "scoop", "shims", ExecutableName));
            list.Add(Path.Combine("C:\\", "ffmpeg", "bin", ExecutableName));
        }
        else
        {
            list.AddRange(new[] { "/usr/bin/ffmpeg", "/usr/local/bin/ffmpeg", "/snap/bin/ffmpeg", "/opt/homebrew/bin/ffmpeg" });
        }

        var comparer = _isWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        return list.Distinct(comparer).Where(_fileExists).ToList();
    }

    /// <summary>ffmpeg.exe in WinGet-Paketordnern mit „FFmpeg“ im Namen (z. B. Gyan.FFmpeg_…\ffmpeg-9.0-full_build\bin).</summary>
    public static IEnumerable<string> FindWinGetPackageExecutables(string packagesDirectory)
    {
        if (!Directory.Exists(packagesDirectory)) return Array.Empty<string>();
        try
        {
            return Directory.EnumerateDirectories(packagesDirectory, "*FFmpeg*")
                .SelectMany(package => Directory.EnumerateDirectories(package)
                    .Select(version => Path.Combine(version, "bin", "ffmpeg.exe"))
                    .Append(Path.Combine(package, "bin", "ffmpeg.exe")))
                .Where(File.Exists)
                .OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase) // neuere Versionen zuerst
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }
}
