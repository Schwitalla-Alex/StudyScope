using System.IO.Compression;
using System.Text.Json;
using StudyScope.Core.Localization;
using StudyScope.Setup.Core.Interfaces;
using StudyScope.Setup.Core.Models;
using StudyScope.Setup.Core.Resources;

namespace StudyScope.Setup.Core.Services;

/// <summary>
/// Plattformneutrale Installationslogik: Paket entpacken, Update aufräumen, Manifest schreiben, Deinstallation.
/// Windows- und Linux-Implementierung ergänzen nur ihre plattformeigenen Schritte über die virtuellen Methoden.
///
/// Sicherheitsregeln:
/// - Es wird nur in den Installationsordner geschrieben; ZIP-Einträge mit ".." oder absoluten Pfaden werden abgelehnt.
/// - Der Benutzerdaten-Ordner wird bei Installation und Update nie angefasst und darf nicht im Installationsordner liegen.
/// - Die Deinstallation löscht nur Dateien aus dem Manifest, Benutzerdaten nur auf ausdrücklichen Wunsch.
/// </summary>
public abstract class InstallServiceBase : IInstallService
{
    public const string ManifestFileName = "install.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    protected IPathProvider Paths { get; }
    protected IPayloadProvider Payload { get; }
    protected IShortcutService Shortcuts { get; }
    protected IProcessService Processes { get; }

    protected InstallServiceBase(IPathProvider paths, IPayloadProvider payload, IShortcutService shortcuts, IProcessService processes)
    {
        Paths = paths;
        Payload = payload;
        Shortcuts = shortcuts;
        Processes = processes;
    }

    /// <summary>Vergleich von Pfaden: unter Windows ohne Beachtung der Groß-/Kleinschreibung.</summary>
    protected static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    // ------------------------------------------------------------------ Plattform-Erweiterungspunkte

    /// <summary>Ordner einer früheren Installation, wie ihn das System kennt (Windows: Registry, Linux: .desktop-Datei).</summary>
    protected virtual string? GetRegisteredInstallDirectory() => null;

    /// <summary>Nach dem Kopieren aller Dateien (Linux: Ausführungsrechte setzen).</summary>
    protected virtual void OnFilesInstalled(string installDirectory, InstallManifest manifest) { }

    /// <summary>Installation beim System anmelden (Windows: Eintrag in „Apps &amp; Features“).</summary>
    protected virtual void RegisterInstallation(InstallManifest manifest) { }

    /// <summary>Anmeldung beim System entfernen.</summary>
    protected virtual void UnregisterInstallation(string installDirectory) { }

    /// <summary>Dateien, die während der Deinstallation gesperrt sind (Windows: das laufende Deinstallationsprogramm).</summary>
    protected virtual void HandleLockedFiles(string installDirectory, IReadOnlyList<string> lockedFiles) { }

    // ------------------------------------------------------------------ Vorhandene Installation

    public ExistingInstallation? FindExistingInstallation()
    {
        var candidates = new[] { GetRegisteredInstallDirectory(), Paths.DefaultInstallDirectory }
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => d!)
            .Distinct(StringComparer.FromComparison(PathComparison));

        foreach (var dir in candidates)
        {
            var manifest = TryReadManifest(dir);
            if (manifest != null)
                return new ExistingInstallation(dir, manifest);
        }
        return null;
    }

    public static InstallManifest? TryReadManifest(string installDirectory)
    {
        try
        {
            var path = Path.Combine(installDirectory, ManifestFileName);
            if (!File.Exists(path))
                return null;
            return JsonSerializer.Deserialize<InstallManifest>(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ Prüfung des Installationsordners

    public string? ValidateInstallDirectory(string? installDirectory)
    {
        if (string.IsNullOrWhiteSpace(installDirectory))
            return SetupCoreStrings.Folder_Empty;

        var dir = installDirectory.Trim();
        if (dir.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return SetupCoreStrings.Folder_InvalidChars;

        if (!Path.IsPathFullyQualified(dir))
            return Loc.Format(SetupCoreStrings.Folder_NotFullyQualified, ("example", Paths.DefaultInstallDirectory));

        string full;
        try { full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return SetupCoreStrings.Folder_Invalid;
        }

        var root = Path.GetPathRoot(full);
        if (root != null && string.Equals(Path.TrimEndingDirectorySeparator(root), full, PathComparison))
            return SetupCoreStrings.Folder_DriveRoot;

        var home = Path.TrimEndingDirectorySeparator(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        if (!string.IsNullOrEmpty(home) && string.Equals(full, home, PathComparison))
            return SetupCoreStrings.Folder_HomeDirectory;

        var userData = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Paths.UserDataDirectory));
        if (IsSameOrInside(full, userData) || IsSameOrInside(userData, full))
            return Loc.Format(SetupCoreStrings.Folder_OverlapsUserData, ("path", userData));

        if (File.Exists(full))
            return SetupCoreStrings.Folder_IsFile;

        // Ein fremder, nicht leerer Ordner würde mit Programmdateien vermischt
        if (Directory.Exists(full) && TryReadManifest(full) == null && Directory.EnumerateFileSystemEntries(full).Any())
            return SetupCoreStrings.Folder_NotEmpty;

        return null;
    }

    protected static bool IsSameOrInside(string path, string folder)
    {
        var p = Path.TrimEndingDirectorySeparator(path);
        var f = Path.TrimEndingDirectorySeparator(folder);
        return string.Equals(p, f, PathComparison)
               || p.StartsWith(f + Path.DirectorySeparatorChar, PathComparison)
               || p.StartsWith(f + Path.AltDirectorySeparatorChar, PathComparison);
    }

    /// <summary>Kombiniert einen relativen Pfad mit dem Installationsordner und stellt sicher, dass er darin bleibt.</summary>
    protected static string? SafeCombine(string installDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            return null;

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installDirectory));
        var full = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        return full.StartsWith(root + Path.DirectorySeparatorChar, PathComparison) ? full : null;
    }

    // ------------------------------------------------------------------ Installation / Update

    public Task<InstallManifest> InstallAsync(InstallOptions options, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
        => Task.Run(() => Install(options, progress, cancellationToken), cancellationToken);

    private InstallManifest Install(InstallOptions options, IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        var error = ValidateInstallDirectory(options.InstallDirectory);
        if (error != null)
            throw new InstallException(error);

        var dir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.InstallDirectory.Trim()));

        if (!Payload.IsAvailable)
            throw new InstallException(SetupCoreStrings.Install_NoPayload);

        if (Processes.IsAppRunning(dir, Paths.AppExecutableName))
            throw new InstallException(SetupCoreStrings.Install_AppRunning);

        progress?.Report(new InstallProgress(0, SetupCoreStrings.Progress_Preparing));

        var previous = TryReadManifest(dir);
        var isUpdate = previous != null;
        var written = new List<string>();

        try
        {
            Directory.CreateDirectory(dir);

            using var stream = Payload.OpenPayload();
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

            // Erst alle Einträge prüfen, dann schreiben – ein manipuliertes Paket schreibt gar nichts
            var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
            foreach (var entry in entries)
            {
                if (SafeCombine(dir, entry.FullName) == null)
                    throw new InstallException(Loc.Format(SetupCoreStrings.Install_InvalidPackagePath, ("path", entry.FullName)));
            }

            var files = new List<string>();
            for (var i = 0; i < entries.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var entry = entries[i];
                var target = SafeCombine(dir, entry.FullName)!;

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var existedBefore = File.Exists(target);
                entry.ExtractToFile(target, overwrite: true);
                if (!existedBefore)
                    written.Add(target);

                files.Add(NormalizeRelative(entry.FullName));
                progress?.Report(new InstallProgress(5 + (int)(75.0 * (i + 1) / entries.Count), Loc.Format(SetupCoreStrings.Progress_Copying, ("file", entry.FullName))));
            }

            // Update: Dateien der alten Version entfernen, die es nicht mehr gibt (nur aus dem alten Manifest)
            if (previous != null)
            {
                progress?.Report(new InstallProgress(82, SetupCoreStrings.Progress_RemovingObsolete));
                var current = new HashSet<string>(files, StringComparer.FromComparison(PathComparison));
                foreach (var old in previous.Files.Where(f => !current.Contains(f)))
                    TryDeleteFile(SafeCombine(dir, old));
                DeleteEmptyDirectories(dir);
            }

            // Setup als Deinstallationsprogramm in den Installationsordner legen
            var setupSource = Paths.CurrentSetupExecutablePath;
            var setupTarget = Path.Combine(dir, Paths.SetupExecutableName);
            if (!string.IsNullOrEmpty(setupSource) && File.Exists(setupSource))
            {
                if (!string.Equals(Path.GetFullPath(setupSource), setupTarget, PathComparison))
                {
                    if (!File.Exists(setupTarget))
                        written.Add(setupTarget);
                    File.Copy(setupSource, setupTarget, overwrite: true);
                }
                files.Add(Paths.SetupExecutableName);
            }

            var manifest = new InstallManifest
            {
                Version = Payload.Version,
                InstallDirectory = dir,
                InstalledAtUtc = DateTime.UtcNow,
                Files = files.Distinct(StringComparer.FromComparison(PathComparison)).ToList()
            };

            progress?.Report(new InstallProgress(86, SetupCoreStrings.Progress_SettingPermissions));
            OnFilesInstalled(dir, manifest);

            progress?.Report(new InstallProgress(90, SetupCoreStrings.Progress_CreatingShortcuts));
            if (previous != null)
                Shortcuts.RemoveShortcuts(previous.Shortcuts);
            manifest.Shortcuts = Shortcuts.CreateShortcuts(new ShortcutRequest
            {
                InstallDirectory = dir,
                AppExecutablePath = Path.Combine(dir, Paths.AppExecutableName),
                SetupExecutablePath = File.Exists(setupTarget) ? setupTarget : null,
                CreateDesktopShortcut = options.CreateDesktopShortcut,
                CreateMenuShortcut = options.CreateMenuShortcut
            }).ToList();

            File.WriteAllText(Path.Combine(dir, ManifestFileName), JsonSerializer.Serialize(manifest, JsonOptions));

            progress?.Report(new InstallProgress(96, SetupCoreStrings.Progress_Registering));
            RegisterInstallation(manifest);

            progress?.Report(new InstallProgress(100, isUpdate ? SetupCoreStrings.Progress_UpdateDone : SetupCoreStrings.Progress_InstallDone));
            return manifest;
        }
        catch (Exception ex)
        {
            // Bei einer Neuinstallation alles wieder entfernen, was dieses Setup angelegt hat
            if (!isUpdate)
            {
                foreach (var file in written)
                    TryDeleteFile(file);
                TryDeleteFile(Path.Combine(dir, ManifestFileName));
                DeleteEmptyDirectories(dir);
                TryDeleteEmptyDirectory(dir);
            }

            if (ex is OperationCanceledException or InstallException)
                throw;
            throw new InstallException(DescribeError(ex, isUpdate), ex);
        }
    }

    private static string DescribeError(Exception ex, bool isUpdate) => ex switch
    {
        UnauthorizedAccessException => SetupCoreStrings.Error_NoWriteAccess,
        IOException io when io.HResult == unchecked((int)0x80070020) => SetupCoreStrings.Error_FileInUse,
        IOException when isUpdate => Loc.Format(SetupCoreStrings.Error_CopyFailedUpdate, ("error", ex.Message)),
        IOException => Loc.Format(SetupCoreStrings.Error_CopyFailed, ("error", ex.Message)),
        InvalidDataException => SetupCoreStrings.Error_PackageCorrupt,
        _ => Loc.Format(SetupCoreStrings.Error_Unexpected, ("error", ex.Message))
    };

    // ------------------------------------------------------------------ Deinstallation

    public Task UninstallAsync(UninstallOptions options, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
        => Task.Run(() => Uninstall(options, progress, cancellationToken), cancellationToken);

    private void Uninstall(UninstallOptions options, IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        var dir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.InstallDirectory));
        var manifest = TryReadManifest(dir)
            ?? throw new InstallException(SetupCoreStrings.Uninstall_NotFound);

        if (Processes.IsAppRunning(dir, Paths.AppExecutableName))
            throw new InstallException(SetupCoreStrings.Install_AppRunning);

        progress?.Report(new InstallProgress(5, SetupCoreStrings.Progress_RemovingShortcuts));
        Shortcuts.RemoveShortcuts(manifest.Shortcuts);
        UnregisterInstallation(dir);

        var running = Paths.CurrentSetupExecutablePath is { } p ? Path.GetFullPath(p) : null;
        var locked = new List<string>();
        for (var i = 0; i < manifest.Files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var file = SafeCombine(dir, manifest.Files[i]);
            if (file == null)
                continue;

            if (running != null && string.Equals(file, running, PathComparison))
                locked.Add(file);
            else if (!TryDeleteFile(file) && File.Exists(file))
                locked.Add(file);

            progress?.Report(new InstallProgress(10 + (int)(70.0 * (i + 1) / Math.Max(1, manifest.Files.Count)), Loc.Format(SetupCoreStrings.Progress_Removing, ("file", manifest.Files[i]))));
        }

        TryDeleteFile(Path.Combine(dir, ManifestFileName));
        DeleteEmptyDirectories(dir);
        TryDeleteEmptyDirectory(dir);

        progress?.Report(new InstallProgress(85, SetupCoreStrings.Progress_RemovingCaches));
        foreach (var cache in Paths.AppCacheDirectories)
            TryDeleteDirectory(cache);

        if (options.DeleteUserData)
        {
            progress?.Report(new InstallProgress(92, SetupCoreStrings.Progress_RemovingUserData));
            TryDeleteDirectory(Paths.UserDataDirectory);
        }

        if (locked.Count > 0)
            HandleLockedFiles(dir, locked);

        progress?.Report(new InstallProgress(100, SetupCoreStrings.Progress_Removed));
    }

    // ------------------------------------------------------------------ Hilfsmethoden

    private static string NormalizeRelative(string path) => path.Replace('\\', '/').TrimStart('/');

    protected static bool TryDeleteFile(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return false;
        try { File.Delete(path); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
                Directory.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Entfernt leere Unterordner (tiefste zuerst), nie den Installationsordner selbst.</summary>
    private static void DeleteEmptyDirectories(string root)
    {
        if (!Directory.Exists(root))
            return;
        foreach (var sub in Directory.GetDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
            TryDeleteEmptyDirectory(sub);
    }
}
