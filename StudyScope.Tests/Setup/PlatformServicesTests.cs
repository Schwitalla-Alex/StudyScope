using System.Runtime.Versioning;
using Microsoft.Win32;
using NUnit.Framework;
using StudyScope.Setup.Core.Models;
using StudyScope.Setup.Core.Services;
using StudyScope.Setup.Core.Services.Linux;
using StudyScope.Setup.Core.Services.Windows;

namespace StudyScope.Tests.Setup;

[TestFixture]
public class DesktopEntryBuilderTests
{
    [Test]
    public void Build_EnthaeltPflichtfelderUndDeinstallationsaktion()
    {
        var content = DesktopEntryBuilder.Build("/home/anna/.local/share/StudyScope",
            "/home/anna/.local/share/StudyScope/StudyScope.UI",
            "/home/anna/.local/share/StudyScope/StudyScope.Setup", "studyscope");

        Assert.That(content, Does.StartWith("[Desktop Entry]\n"));
        Assert.That(content, Does.Contain("Type=Application\n"));
        Assert.That(content, Does.Contain("Name=StudyScope\n"));
        Assert.That(content, Does.Contain("Exec=\"/home/anna/.local/share/StudyScope/StudyScope.UI\"\n"));
        Assert.That(content, Does.Contain("Path=/home/anna/.local/share/StudyScope\n"));
        Assert.That(content, Does.Contain("Icon=studyscope\n"));
        Assert.That(content, Does.Contain("Keywords=Lernen;Karteikarten;PDF;KI;Mindmap;Quiz;Präsentation;Bericht;Tabelle;Audio;Podcast;Video;\n"));
        Assert.That(content, Does.Match(@"\nComment=[^\n]*Mindmap[^\n]*\n"));
        Assert.That(content, Does.Contain("Actions=Uninstall;\n"));
        Assert.That(content, Does.Contain("[Desktop Action Uninstall]\n"));
        Assert.That(content, Does.Contain("Exec=\"/home/anna/.local/share/StudyScope/StudyScope.Setup\" --uninstall\n"));
        Assert.That(content, Does.Not.Contain("\r"));
    }

    [Test]
    public void Build_OhneSetup_KeineDeinstallationsaktion()
    {
        var content = DesktopEntryBuilder.Build("/opt/s", "/opt/s/StudyScope.UI", null, "studyscope");

        Assert.That(content, Does.Not.Contain("Actions="));
    }

    [TestCase("/home/a/Meine Programme/StudyScope.UI", "\"/home/a/Meine Programme/StudyScope.UI\"")]
    [TestCase("/home/a/100%/StudyScope.UI", "\"/home/a/100%%/StudyScope.UI\"")]
    [TestCase("/home/a/$HOME/x", "\"/home/a/\\\\$HOME/x\"")]
    [TestCase("/home/a/\"q\"/x", "\"/home/a/\\\\\"q\\\\\"/x\"")]
    public void QuoteExecArgument_MaskiertSonderzeichenNachSpezifikation(string input, string erwartet)
    {
        Assert.That(DesktopEntryBuilder.QuoteExecArgument(input), Is.EqualTo(erwartet));
    }

    [Test]
    public void ReadPath_LiestInstallationsordnerZurueck()
    {
        var content = DesktopEntryBuilder.Build("/home/a/Mein Ordner/StudyScope", "/home/a/Mein Ordner/StudyScope/StudyScope.UI", null, "x");

        Assert.That(DesktopEntryBuilder.ReadPath(content), Is.EqualTo("/home/a/Mein Ordner/StudyScope"));
    }
}

/// <summary>Windows-spezifische Services – laufen nur unter Windows, mit eigenem Registry-Testschlüssel und Temp-Ordnern.</summary>
[TestFixture]
[Platform("Win")]
[SupportedOSPlatform("windows")]
public class WindowsServicesTests
{
    private SetupTestEnvironment _env = null!;
    private string _testKey = null!;

    [SetUp]
    public void SetUp()
    {
        _env = new SetupTestEnvironment();
        _testKey = @"Software\StudyScope.Tests\Uninstall\" + Guid.NewGuid().ToString("N");
    }

    [TearDown]
    public void TearDown()
    {
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\StudyScope.Tests", throwOnMissingSubKey: false);
        _env.Dispose();
    }

    [Test]
    public void WindowsPathProvider_StandardordnerUnterLocalAppDataPrograms()
    {
        var paths = new WindowsPathProvider();

        Assert.That(paths.DefaultInstallDirectory, Is.EqualTo(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "StudyScope")));
        Assert.That(paths.UserDataDirectory, Is.EqualTo(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StudyScope")));
        Assert.That(paths.AppCacheDirectories, Is.Empty, "der gemeinsame Photino-Ordner darf nicht gelöscht werden");
        Assert.That(paths.AppExecutableName, Is.EqualTo("StudyScope.UI.exe"));
    }

    [Test]
    public async Task WindowsInstallService_RegistriertUndEntferntEintragInAppsUndFeatures()
    {
        var paths = new FakePathProvider(_env);
        var service = new WindowsInstallService(paths, FakePayloadProvider.Default(), new FakeShortcutService(Path.Combine(_env.Root, "lnk")),
            new FakeProcessService(), _testKey);

        await service.InstallAsync(new InstallOptions { InstallDirectory = _env.InstallDirectory });

        using (var key = Registry.CurrentUser.OpenSubKey(_testKey))
        {
            Assert.That(key, Is.Not.Null);
            Assert.That(key!.GetValue("DisplayName"), Is.EqualTo("StudyScope"));
            Assert.That(key.GetValue("DisplayVersion"), Is.EqualTo("1.0.3"));
            Assert.That(key.GetValue("InstallLocation"), Is.EqualTo(Path.GetFullPath(_env.InstallDirectory)));
            Assert.That(key.GetValue("UninstallString"), Is.EqualTo($"\"{Path.Combine(Path.GetFullPath(_env.InstallDirectory), "StudyScope.Setup.exe")}\" --uninstall"));
            Assert.That(key.GetValue("NoModify"), Is.EqualTo(1));
        }

        // Ein neues Setup findet die Installation über die Registry
        var found = new WindowsInstallService(new FakePathProvider(_env), FakePayloadProvider.Default(),
            new FakeShortcutService(_env.Root), new FakeProcessService(), _testKey).FindExistingInstallation();
        Assert.That(found?.InstallDirectory, Is.EqualTo(Path.GetFullPath(_env.InstallDirectory)));

        await service.UninstallAsync(new UninstallOptions { InstallDirectory = _env.InstallDirectory });

        Assert.That(Registry.CurrentUser.OpenSubKey(_testKey), Is.Null);
    }

    [Test]
    public void WindowsShortcutService_LegtLnkMitZielUndArbeitsverzeichnisAn()
    {
        var startMenu = Path.Combine(_env.Root, "Startmenue");
        var desktop = Path.Combine(_env.Root, "Desktop");
        var exe = Path.Combine(_env.InstallDirectory, "StudyScope.UI.exe");
        Directory.CreateDirectory(_env.InstallDirectory);
        File.WriteAllText(exe, "x");
        var service = new WindowsShortcutService(startMenu, desktop);

        var created = service.CreateShortcuts(new ShortcutRequest
        {
            InstallDirectory = _env.InstallDirectory,
            AppExecutablePath = exe,
            CreateDesktopShortcut = true,
            CreateMenuShortcut = true
        });

        Assert.That(created, Is.EqualTo(new[] { Path.Combine(startMenu, "StudyScope.lnk"), Path.Combine(desktop, "StudyScope.lnk") }));
        Assert.That(created.All(File.Exists), Is.True);

        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic link = shell.CreateShortcut(created[0]);
        Assert.That((string)link.TargetPath, Is.EqualTo(exe).IgnoreCase);
        Assert.That((string)link.WorkingDirectory, Is.EqualTo(_env.InstallDirectory).IgnoreCase);

        service.RemoveShortcuts(created);
        Assert.That(created.Any(File.Exists), Is.False);
    }

    [Test]
    public void WindowsShortcutService_RemoveShortcuts_LoeschtNurLnkDateien()
    {
        var other = Path.Combine(_env.Root, "wichtig.txt");
        File.WriteAllText(other, "x");

        new WindowsShortcutService(_env.Root, _env.Root).RemoveShortcuts(new[] { other });

        Assert.That(File.Exists(other), Is.True);
    }

    [Test]
    public void WebView2Checker_LiefertErgebnisMitDownloadLinkFallsFehlend()
    {
        var result = new WebView2Checker().Check();

        // Auf dem Entwicklungsrechner ist WebView2 installiert (StudyScope läuft hier)
        Assert.That(result.IsSatisfied, Is.True, result.Message);
    }
}

/// <summary>Linux-spezifische Services – laufen nur unter Linux.</summary>
[TestFixture]
[Platform("Linux")]
[SupportedOSPlatform("linux")]
public class LinuxServicesTests
{
    private SetupTestEnvironment _env = null!;

    [SetUp]
    public void SetUp() => _env = new SetupTestEnvironment();

    [TearDown]
    public void TearDown() => _env.Dispose();

    [Test]
    public void LinuxPathProvider_NutztXdgVerzeichnisse()
    {
        var paths = new LinuxPathProvider();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.That(paths.DefaultInstallDirectory, Does.EndWith(Path.Combine("share", "StudyScope")).Or.StartsWith(Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? home));
        Assert.That(paths.AppCacheDirectories.Select(Path.GetFileName), Is.All.EqualTo("StudyScope.UI"));
        Assert.That(paths.AppExecutableName, Is.EqualTo("StudyScope.UI"));
    }

    [Test]
    public async Task LinuxInstallService_SetztAusfuehrungsrechteUndFindetInstallationUeberDesktopDatei()
    {
        var apps = Path.Combine(_env.Root, "applications");
        var paths = new FakePathProviderLinux(_env);
        var shortcuts = new LinuxShortcutService(apps, Path.Combine(_env.Root, "icons"), Path.Combine(_env.Root, "Desktop"));
        var service = new LinuxInstallService(paths, FakePayloadProviderLinux(), shortcuts, new FakeProcessService(), apps);

        await service.InstallAsync(new InstallOptions { InstallDirectory = _env.InstallDirectory });

        var mode = File.GetUnixFileMode(Path.Combine(_env.InstallDirectory, "StudyScope.UI"));
        Assert.That(mode.HasFlag(UnixFileMode.UserExecute), Is.True);
        Assert.That(File.Exists(Path.Combine(apps, "studyscope.desktop")), Is.True);

        var found = new LinuxInstallService(new FakePathProviderLinux(_env), FakePayloadProviderLinux(), shortcuts, new FakeProcessService(), apps)
            .FindExistingInstallation();
        Assert.That(found?.InstallDirectory, Is.EqualTo(Path.GetFullPath(_env.InstallDirectory)));
    }

    [Test]
    public async Task LinuxInstallService_DeinstallationAusDemInstallationsordner_EntferntAuchDasLaufendeSetup()
    {
        var apps = Path.Combine(_env.Root, "applications");
        var download = Path.Combine(_env.Root, "Download", "StudyScope.Setup");
        Directory.CreateDirectory(Path.GetDirectoryName(download)!);
        File.WriteAllText(download, "setup");
        var paths = new FakePathProviderLinux(_env) { CurrentSetupExecutablePath = download };
        var shortcuts = new LinuxShortcutService(apps, Path.Combine(_env.Root, "icons"), Path.Combine(_env.Root, "Desktop"));
        var service = new LinuxInstallService(paths, FakePayloadProviderLinux(), shortcuts, new FakeProcessService(), apps);
        await service.InstallAsync(new InstallOptions { InstallDirectory = _env.InstallDirectory });

        // Deinstallation über die Menüaktion: das Setup läuft aus dem Installationsordner
        paths.CurrentSetupExecutablePath = Path.Combine(_env.InstallDirectory, "StudyScope.Setup");
        await service.UninstallAsync(new UninstallOptions { InstallDirectory = _env.InstallDirectory });

        Assert.That(Directory.Exists(_env.InstallDirectory), Is.False, "Installationsordner blieb mit dem laufenden Setup zurück");
        Assert.That(File.Exists(Path.Combine(apps, "studyscope.desktop")), Is.False);
    }

    private static FakePayloadProvider FakePayloadProviderLinux() => new(new Dictionary<string, string>
    {
        ["StudyScope.UI"] = "elf",
        ["wwwroot/favicon.ico"] = "ico"
    });

    private sealed class FakePathProviderLinux : StudyScope.Setup.Core.Interfaces.IPathProvider
    {
        private readonly SetupTestEnvironment _env;
        public FakePathProviderLinux(SetupTestEnvironment env) => _env = env;
        public string DefaultInstallDirectory => _env.InstallDirectory;
        public string UserDataDirectory => _env.UserDataDirectory;
        public IReadOnlyList<string> AppCacheDirectories => Array.Empty<string>();
        public string AppExecutableName => "StudyScope.UI";
        public string SetupExecutableName => "StudyScope.Setup";
        public string? CurrentSetupExecutablePath { get; set; }
    }
}
