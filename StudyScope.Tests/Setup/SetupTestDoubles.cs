using System.IO.Compression;
using System.Text;
using StudyScope.Setup.Core.Interfaces;
using StudyScope.Setup.Core.Models;
using StudyScope.Setup.Core.Services;

namespace StudyScope.Tests.Setup;

/// <summary>Testumgebung in einem eigenen Temp-Ordner: Installationsziel, Benutzerdaten, Caches, „laufendes Setup“.</summary>
public sealed class SetupTestEnvironment : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "StudyScope_SetupTests_" + Guid.NewGuid().ToString("N")[..8]);

    public string InstallDirectory => Path.Combine(Root, "Programme", "StudyScope");
    public string UserDataDirectory => Path.Combine(Root, "Benutzerdaten", "StudyScope");
    public string CacheDirectory => Path.Combine(Root, "Cache", "StudyScope.UI");
    public string SetupExecutable => Path.Combine(Root, "Download", "StudyScope.Setup.exe");

    public SetupTestEnvironment() => Directory.CreateDirectory(Root);

    public void Dispose()
    {
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }

    /// <summary>Simuliert vorhandene Profile des Benutzers.</summary>
    public string CreateUserData()
    {
        var file = Path.Combine(UserDataDirectory, "Profiles", "Testprofil.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "{\"Name\":\"Testprofil\"}");
        return file;
    }
}

public sealed class FakePathProvider : IPathProvider
{
    private readonly SetupTestEnvironment _env;
    public FakePathProvider(SetupTestEnvironment env) => _env = env;

    public string DefaultInstallDirectory => _env.InstallDirectory;
    public string UserDataDirectory => _env.UserDataDirectory;
    public IReadOnlyList<string> AppCacheDirectories => new[] { _env.CacheDirectory };
    public string AppExecutableName => "StudyScope.UI.exe";
    public string SetupExecutableName => "StudyScope.Setup.exe";
    public string? CurrentSetupExecutablePath { get; set; }
}

/// <summary>Installationspaket als ZIP im Speicher.</summary>
public sealed class FakePayloadProvider : IPayloadProvider
{
    private readonly byte[] _zip;

    public FakePayloadProvider(IDictionary<string, string> files, string version = "1.0.3")
    {
        Version = version;
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                var entry = archive.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }
        _zip = ms.ToArray();
    }

    public static FakePayloadProvider Default(string version = "1.0.3") => new(new Dictionary<string, string>
    {
        ["StudyScope.UI.exe"] = "app " + version,
        ["StudyScope.UI.dll"] = "dll " + version,
        ["wwwroot/index.html"] = "<html></html>",
        ["wwwroot/favicon.ico"] = "ico"
    }, version);

    public bool IsAvailable { get; set; } = true;
    public string Version { get; }
    public Stream OpenPayload() => new MemoryStream(_zip, writable: false);
}

public sealed class FakeShortcutService : IShortcutService
{
    private readonly string _shortcutDirectory;
    public FakeShortcutService(string shortcutDirectory) => _shortcutDirectory = shortcutDirectory;

    public bool IsMenuShortcutOptional { get; set; } = true;
    public string MenuShortcutLabel => "Verknüpfung im Startmenü";
    public List<ShortcutRequest> Requests { get; } = new();
    public List<string> Removed { get; } = new();

    public IReadOnlyList<string> CreateShortcuts(ShortcutRequest request)
    {
        Requests.Add(request);
        Directory.CreateDirectory(_shortcutDirectory);
        var created = new List<string>();
        if (request.CreateMenuShortcut) created.Add(Touch("Startmenue.lnk"));
        if (request.CreateDesktopShortcut) created.Add(Touch("Desktop.lnk"));
        return created;
    }

    public void RemoveShortcuts(IEnumerable<string> shortcutPaths)
    {
        foreach (var p in shortcutPaths)
        {
            Removed.Add(p);
            if (File.Exists(p)) File.Delete(p);
        }
    }

    private string Touch(string name)
    {
        var path = Path.Combine(_shortcutDirectory, name);
        File.WriteAllText(path, "lnk");
        return path;
    }
}

public sealed class FakeProcessService : IProcessService
{
    public bool AppRunning { get; set; }
    public List<(string Exe, string WorkingDirectory)> Started { get; } = new();

    public bool IsAppRunning(string installDirectory, string appExecutableName) => AppRunning;
    public void StartApp(string executablePath, string workingDirectory) => Started.Add((executablePath, workingDirectory));
}

/// <summary>Konkrete Testvariante von InstallServiceBase, die die Plattform-Hooks mitprotokolliert.</summary>
public sealed class TestInstallService : InstallServiceBase
{
    public TestInstallService(IPathProvider paths, IPayloadProvider payload, IShortcutService shortcuts, IProcessService processes)
        : base(paths, payload, shortcuts, processes) { }

    public string? RegisteredDirectory { get; set; }
    public List<string> Calls { get; } = new();
    public List<string> LockedFiles { get; } = new();

    protected override string? GetRegisteredInstallDirectory() => RegisteredDirectory;
    protected override void OnFilesInstalled(string installDirectory, InstallManifest manifest) => Calls.Add("OnFilesInstalled");
    protected override void RegisterInstallation(InstallManifest manifest) { Calls.Add("Register"); RegisteredDirectory = manifest.InstallDirectory; }
    protected override void UnregisterInstallation(string installDirectory) { Calls.Add("Unregister"); RegisteredDirectory = null; }
    protected override void HandleLockedFiles(string installDirectory, IReadOnlyList<string> lockedFiles) => LockedFiles.AddRange(lockedFiles);
}
