using System.IO.Compression;
using System.Text;
using NUnit.Framework;
using StudyScope.Setup.Core.Models;
using StudyScope.Setup.Core.Services;

namespace StudyScope.Tests.Setup;

[TestFixture]
public class InstallServiceBaseTests
{
    private SetupTestEnvironment _env = null!;
    private FakePathProvider _paths = null!;
    private FakeShortcutService _shortcuts = null!;
    private FakeProcessService _processes = null!;

    [SetUp]
    public void SetUp()
    {
        _env = new SetupTestEnvironment();
        _paths = new FakePathProvider(_env);
        _shortcuts = new FakeShortcutService(Path.Combine(_env.Root, "Verknuepfungen"));
        _processes = new FakeProcessService();
    }

    [TearDown]
    public void TearDown() => _env.Dispose();

    private TestInstallService CreateService(FakePayloadProvider? payload = null) =>
        new(_paths, payload ?? FakePayloadProvider.Default(), _shortcuts, _processes);

    private InstallOptions Options(string? dir = null) => new() { InstallDirectory = dir ?? _env.InstallDirectory };

    // ------------------------------------------------------------------ Installation

    [Test]
    public async Task InstallAsync_Neuinstallation_EntpacktDateienUndSchreibtManifest()
    {
        var service = CreateService();
        var reports = new List<InstallProgress>();

        var manifest = await service.InstallAsync(Options(), new DirectProgress<InstallProgress>(reports.Add));

        Assert.That(File.ReadAllText(Path.Combine(_env.InstallDirectory, "StudyScope.UI.exe")), Is.EqualTo("app 1.0.3"));
        Assert.That(File.Exists(Path.Combine(_env.InstallDirectory, "wwwroot", "index.html")), Is.True);
        Assert.That(File.Exists(Path.Combine(_env.InstallDirectory, InstallServiceBase.ManifestFileName)), Is.True);
        Assert.That(manifest.Version, Is.EqualTo("1.0.3"));
        Assert.That(manifest.Files, Is.EquivalentTo(new[] { "StudyScope.UI.exe", "StudyScope.UI.dll", "wwwroot/index.html", "wwwroot/favicon.ico" }));
        Assert.That(manifest.Shortcuts, Has.Count.EqualTo(2));
        Assert.That(service.Calls, Is.EqualTo(new[] { "OnFilesInstalled", "Register" }));
        Assert.That(reports.Last(), Is.EqualTo(new InstallProgress(100, "Installation abgeschlossen.")));
        Assert.That(reports.Select(r => r.Percent), Is.Ordered);
    }

    [Test]
    public async Task InstallAsync_OhneVerknuepfungen_LegtKeineAn()
    {
        var manifest = await CreateService().InstallAsync(new InstallOptions
        {
            InstallDirectory = _env.InstallDirectory, CreateDesktopShortcut = false, CreateMenuShortcut = false
        });

        Assert.That(manifest.Shortcuts, Is.Empty);
        Assert.That(_shortcuts.Requests.Single().AppExecutablePath, Is.EqualTo(Path.Combine(_env.InstallDirectory, "StudyScope.UI.exe")));
    }

    [Test]
    public async Task InstallAsync_KopiertLaufendesSetupAlsDeinstallationsprogramm()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_env.SetupExecutable)!);
        File.WriteAllText(_env.SetupExecutable, "setup");
        _paths.CurrentSetupExecutablePath = _env.SetupExecutable;

        var manifest = await CreateService().InstallAsync(Options());

        Assert.That(File.ReadAllText(Path.Combine(_env.InstallDirectory, "StudyScope.Setup.exe")), Is.EqualTo("setup"));
        Assert.That(manifest.Files, Does.Contain("StudyScope.Setup.exe"));
        Assert.That(_shortcuts.Requests.Single().SetupExecutablePath, Is.EqualTo(Path.Combine(_env.InstallDirectory, "StudyScope.Setup.exe")));
    }

    [Test]
    public async Task InstallAsync_Update_UeberschreibtEntferntVeralteteDateienUndBehaeltBenutzerdaten()
    {
        var userFile = _env.CreateUserData();
        await CreateService(new FakePayloadProvider(new Dictionary<string, string>
        {
            ["StudyScope.UI.exe"] = "alt",
            ["Alt.dll"] = "veraltet",
            ["alt/ordner/datei.txt"] = "veraltet"
        }, "1.0.2")).InstallAsync(Options());

        // Datei, die der Benutzer selbst in den Installationsordner gelegt hat
        var fremd = Path.Combine(_env.InstallDirectory, "eigene-notiz.txt");
        File.WriteAllText(fremd, "bleibt");

        var service = CreateService(FakePayloadProvider.Default("1.0.3"));
        var reports = new List<InstallProgress>();
        var manifest = await service.InstallAsync(Options(), new DirectProgress<InstallProgress>(reports.Add));

        Assert.That(File.ReadAllText(Path.Combine(_env.InstallDirectory, "StudyScope.UI.exe")), Is.EqualTo("app 1.0.3"));
        Assert.That(File.Exists(Path.Combine(_env.InstallDirectory, "Alt.dll")), Is.False, "veraltete Datei nicht entfernt");
        Assert.That(Directory.Exists(Path.Combine(_env.InstallDirectory, "alt")), Is.False, "leerer alter Ordner nicht entfernt");
        Assert.That(File.Exists(fremd), Is.True, "fremde Datei wurde gelöscht");
        Assert.That(File.ReadAllText(userFile), Is.EqualTo("{\"Name\":\"Testprofil\"}"), "Benutzerdaten verändert");
        Assert.That(manifest.Version, Is.EqualTo("1.0.3"));
        Assert.That(_shortcuts.Removed, Has.Count.EqualTo(2), "alte Verknüpfungen nicht ersetzt");
        Assert.That(reports.Last().Message, Is.EqualTo("Update abgeschlossen."));
    }

    [Test]
    public void InstallAsync_AppLaeuft_BrichtAbOhneZuSchreiben()
    {
        _processes.AppRunning = true;

        var ex = Assert.ThrowsAsync<InstallException>(() => CreateService().InstallAsync(Options()));

        Assert.That(ex!.Message, Does.Contain("StudyScope läuft noch"));
        Assert.That(Directory.Exists(_env.InstallDirectory), Is.False);
    }

    [Test]
    public void InstallAsync_OhnePayload_MeldetFehler()
    {
        var payload = FakePayloadProvider.Default();
        payload.IsAvailable = false;

        var ex = Assert.ThrowsAsync<InstallException>(() => CreateService(payload).InstallAsync(Options()));

        Assert.That(ex!.Message, Does.Contain("kein Installationspaket"));
    }

    [TestCase("../ausserhalb.txt")]
    [TestCase("../../ausserhalb.txt")]
    [TestCase("ordner/../../ausserhalb.txt")]
    public void InstallAsync_ZipMitPfadAusbruch_WirdAbgelehntUndSchreibtNichts(string boeserEintrag)
    {
        var payload = new FakePayloadProvider(new Dictionary<string, string>
        {
            ["StudyScope.UI.exe"] = "app",
            [boeserEintrag] = "böse"
        });

        var ex = Assert.ThrowsAsync<InstallException>(() => CreateService(payload).InstallAsync(Options()));

        Assert.That(ex!.Message, Does.Contain("Installationspaket ist ungültig"));
        Assert.That(Directory.GetFiles(_env.Root, "ausserhalb.txt", SearchOption.AllDirectories), Is.Empty);
        Assert.That(File.Exists(Path.Combine(_env.InstallDirectory, "StudyScope.UI.exe")), Is.False, "Teilinstallation nicht zurückgerollt");
    }

    [Test]
    public void InstallAsync_Abbruch_RolltNeuinstallationZurueck()
    {
        using var cts = new CancellationTokenSource();
        var progress = new DirectProgress<InstallProgress>(p => { if (p.Message.StartsWith("Kopiere")) cts.Cancel(); });

        Assert.CatchAsync<OperationCanceledException>(() => CreateService().InstallAsync(Options(), progress, cts.Token));

        Assert.That(Directory.Exists(_env.InstallDirectory), Is.False, "Installationsordner nach Abbruch nicht entfernt");
    }

    // ------------------------------------------------------------------ Prüfung des Ordners

    [Test]
    public void ValidateInstallDirectory_Standardordner_IstGueltig()
    {
        Assert.That(CreateService().ValidateInstallDirectory(_env.InstallDirectory), Is.Null);
    }

    [TestCase(null, "Bitte wählen Sie einen Installationsordner.")]
    [TestCase("", "Bitte wählen Sie einen Installationsordner.")]
    [TestCase("relativ/StudyScope", "vollständigen Pfad")]
    public void ValidateInstallDirectory_UngueltigeEingabe(string? dir, string erwartet)
    {
        Assert.That(CreateService().ValidateInstallDirectory(dir), Does.Contain(erwartet));
    }

    [Test]
    public void ValidateInstallDirectory_BenutzerdatenOrdnerOderDarueber_WirdAbgelehnt()
    {
        var service = CreateService();

        Assert.That(service.ValidateInstallDirectory(_env.UserDataDirectory), Does.Contain("Profile und Einstellungen"));
        Assert.That(service.ValidateInstallDirectory(Path.Combine(_env.UserDataDirectory, "Programm")), Does.Contain("Profile und Einstellungen"));
        Assert.That(service.ValidateInstallDirectory(Path.GetDirectoryName(_env.UserDataDirectory)), Does.Contain("Profile und Einstellungen"));
    }

    [Test]
    public void ValidateInstallDirectory_FremderNichtLeererOrdner_WirdAbgelehnt()
    {
        var fremd = Path.Combine(_env.Root, "Fremd");
        Directory.CreateDirectory(fremd);
        File.WriteAllText(Path.Combine(fremd, "irgendwas.txt"), "x");

        Assert.That(CreateService().ValidateInstallDirectory(fremd), Does.Contain("nicht leer"));
    }

    [Test]
    public async Task ValidateInstallDirectory_VorhandeneInstallation_IstGueltig()
    {
        await CreateService().InstallAsync(Options());

        Assert.That(CreateService().ValidateInstallDirectory(_env.InstallDirectory), Is.Null);
    }

    [Test]
    public void ValidateInstallDirectory_Laufwerkswurzel_WirdAbgelehnt()
    {
        var root = Path.GetPathRoot(_env.Root)!;

        Assert.That(CreateService().ValidateInstallDirectory(root), Does.Contain("Unterordner"));
    }

    // ------------------------------------------------------------------ Vorhandene Installation

    [Test]
    public async Task FindExistingInstallation_FindetRegistriertenOrdner()
    {
        var anderer = Path.Combine(_env.Root, "Anders", "StudyScope");
        var service = CreateService();
        await service.InstallAsync(Options(anderer));

        var neu = CreateService();
        neu.RegisteredDirectory = service.RegisteredDirectory;
        var found = neu.FindExistingInstallation();

        Assert.That(found, Is.Not.Null);
        Assert.That(found!.InstallDirectory, Is.EqualTo(Path.GetFullPath(anderer)));
        Assert.That(found.Manifest.Version, Is.EqualTo("1.0.3"));
    }

    [Test]
    public void FindExistingInstallation_OhneInstallation_LiefertNull()
    {
        Assert.That(CreateService().FindExistingInstallation(), Is.Null);
    }

    // ------------------------------------------------------------------ Deinstallation

    [Test]
    public async Task UninstallAsync_EntferntNurManifestDateienUndBehaeltBenutzerdaten()
    {
        var userFile = _env.CreateUserData();
        Directory.CreateDirectory(_env.CacheDirectory);
        File.WriteAllText(Path.Combine(_env.CacheDirectory, "cache.bin"), "x");
        var service = CreateService();
        var manifest = await service.InstallAsync(Options());
        var fremd = Path.Combine(_env.InstallDirectory, "eigene-notiz.txt");
        File.WriteAllText(fremd, "bleibt");

        await service.UninstallAsync(new UninstallOptions { InstallDirectory = _env.InstallDirectory });

        Assert.That(File.Exists(Path.Combine(_env.InstallDirectory, "StudyScope.UI.exe")), Is.False);
        Assert.That(File.Exists(Path.Combine(_env.InstallDirectory, InstallServiceBase.ManifestFileName)), Is.False);
        Assert.That(Directory.Exists(Path.Combine(_env.InstallDirectory, "wwwroot")), Is.False);
        Assert.That(File.Exists(fremd), Is.True, "fremde Datei wurde gelöscht");
        Assert.That(manifest.Shortcuts.All(s => !File.Exists(s)), Is.True, "Verknüpfungen nicht entfernt");
        Assert.That(File.Exists(userFile), Is.True, "Benutzerdaten ohne Zustimmung gelöscht");
        Assert.That(Directory.Exists(_env.CacheDirectory), Is.False, "App-Cache nicht entfernt");
        Assert.That(service.Calls, Does.Contain("Unregister"));
    }

    [Test]
    public async Task UninstallAsync_LeererOrdner_WirdEntfernt()
    {
        var service = CreateService();
        await service.InstallAsync(Options());

        await service.UninstallAsync(new UninstallOptions { InstallDirectory = _env.InstallDirectory });

        Assert.That(Directory.Exists(_env.InstallDirectory), Is.False);
    }

    [Test]
    public async Task UninstallAsync_MitBenutzerdaten_LoeschtAuchProfile()
    {
        _env.CreateUserData();
        var service = CreateService();
        await service.InstallAsync(Options());

        await service.UninstallAsync(new UninstallOptions { InstallDirectory = _env.InstallDirectory, DeleteUserData = true });

        Assert.That(Directory.Exists(_env.UserDataDirectory), Is.False);
    }

    [Test]
    public async Task UninstallAsync_LaufendesSetup_WirdAlsGesperrtGemeldet()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_env.SetupExecutable)!);
        File.WriteAllText(_env.SetupExecutable, "setup");
        _paths.CurrentSetupExecutablePath = _env.SetupExecutable;
        var service = CreateService();
        await service.InstallAsync(Options());

        // Deinstallation wird aus dem Installationsordner gestartet
        var installedSetup = Path.Combine(_env.InstallDirectory, "StudyScope.Setup.exe");
        _paths.CurrentSetupExecutablePath = installedSetup;
        await service.UninstallAsync(new UninstallOptions { InstallDirectory = _env.InstallDirectory });

        Assert.That(service.LockedFiles, Is.EqualTo(new[] { installedSetup }));
        Assert.That(File.Exists(installedSetup), Is.True, "laufendes Setup darf nicht direkt gelöscht werden");
    }

    [Test]
    public void UninstallAsync_OhneInstallation_MeldetFehler()
    {
        var ex = Assert.ThrowsAsync<InstallException>(() =>
            CreateService().UninstallAsync(new UninstallOptions { InstallDirectory = _env.InstallDirectory }));

        Assert.That(ex!.Message, Does.Contain("keine StudyScope-Installation"));
    }

    [Test]
    public async Task UninstallAsync_ManipuliertesManifest_LoeschtNichtsAusserhalb()
    {
        var service = CreateService();
        await service.InstallAsync(Options());
        var opfer = Path.Combine(_env.Root, "opfer.txt");
        File.WriteAllText(opfer, "bleibt");

        var manifestPath = Path.Combine(_env.InstallDirectory, InstallServiceBase.ManifestFileName);
        var json = File.ReadAllText(manifestPath).Replace("\"StudyScope.UI.dll\"", "\"../../opfer.txt\"");
        File.WriteAllText(manifestPath, json);

        await service.UninstallAsync(new UninstallOptions { InstallDirectory = _env.InstallDirectory });

        Assert.That(File.Exists(opfer), Is.True);
    }

    [Test]
    public void UninstallAsync_AppLaeuft_BrichtAb()
    {
        var service = CreateService();
        service.InstallAsync(Options()).GetAwaiter().GetResult();
        _processes.AppRunning = true;

        Assert.ThrowsAsync<InstallException>(() => service.UninstallAsync(new UninstallOptions { InstallDirectory = _env.InstallDirectory }));
        Assert.That(File.Exists(Path.Combine(_env.InstallDirectory, "StudyScope.UI.exe")), Is.True);
    }
}
