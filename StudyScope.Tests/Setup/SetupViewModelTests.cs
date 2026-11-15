using Moq;
using NUnit.Framework;
using StudyScope.Setup.Core.Interfaces;
using StudyScope.Setup.Core.Models;
using StudyScope.Setup.Core.ViewModels;

namespace StudyScope.Tests.Setup;

[TestFixture]
public class SetupWizardViewModelTests
{
    private SetupTestEnvironment _env = null!;
    private FakePathProvider _paths = null!;
    private FakeShortcutService _shortcuts = null!;
    private FakeProcessService _processes = null!;
    private Mock<IFolderPicker> _folderPicker = null!;
    private TestInstallService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _env = new SetupTestEnvironment();
        _paths = new FakePathProvider(_env);
        _shortcuts = new FakeShortcutService(Path.Combine(_env.Root, "lnk"));
        _processes = new FakeProcessService();
        _folderPicker = new Mock<IFolderPicker>();
        _service = new TestInstallService(_paths, FakePayloadProvider.Default(), _shortcuts, _processes);
    }

    [TearDown]
    public void TearDown() => _env.Dispose();

    private SetupWizardViewModel CreateViewModel(IInstallService? service = null, IPayloadProvider? payload = null)
    {
        var s = service ?? _service;
        var vm = new SetupWizardViewModel(s, _paths, payload ?? FakePayloadProvider.Default(), _shortcuts,
            _folderPicker.Object, _processes, new InstallViewModel(s));
        vm.Initialize();
        return vm;
    }

    [Test]
    public void Initialize_BelegtStandardordnerVorUndStartetBeiWillkommen()
    {
        var vm = CreateViewModel();

        Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Welcome));
        Assert.That(vm.InstallDirectory, Is.EqualTo(_env.InstallDirectory));
        Assert.That(vm.IsUpdate, Is.False);
        Assert.That(vm.CanGoBack, Is.False);
        Assert.That(vm.CanGoNext, Is.True);
        Assert.That(vm.Version, Is.EqualTo("1.0.3"));
        Assert.That(vm.StepNumber, Is.EqualTo(1));
        Assert.That(vm.StepCount, Is.EqualTo(5));
    }

    [Test]
    public void OhnePayload_KannNichtStarten()
    {
        var payload = FakePayloadProvider.Default();
        payload.IsAvailable = false;

        var vm = CreateViewModel(payload: payload);

        Assert.That(vm.CanGoNext, Is.False);
    }

    [Test]
    public async Task KompletterDurchlauf_InstalliertUndStartetStudyScope()
    {
        var vm = CreateViewModel();
        var closed = false;
        vm.CloseRequested += (_, _) => closed = true;

        await vm.GoNextAsync();
        Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Folder));
        await vm.GoNextAsync();
        Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Options));
        vm.CreateDesktopShortcut = false;
        await vm.GoNextAsync();

        Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Finished));
        Assert.That(vm.Install.ProgressPercent, Is.EqualTo(100));
        Assert.That(vm.Install.IsCompleted, Is.True);
        Assert.That(_shortcuts.Requests.Single().CreateDesktopShortcut, Is.False);
        Assert.That(File.Exists(Path.Combine(_env.InstallDirectory, "StudyScope.UI.exe")), Is.True);

        vm.Finish();

        Assert.That(_processes.Started.Single().Exe, Is.EqualTo(Path.Combine(_env.InstallDirectory, "StudyScope.UI.exe")));
        Assert.That(closed, Is.True);
    }

    [Test]
    public async Task Finish_OhneStartWunsch_StartetNichts()
    {
        var vm = CreateViewModel();
        await vm.GoNextAsync(); await vm.GoNextAsync(); await vm.GoNextAsync();
        vm.LaunchAfterFinish = false;

        vm.Finish();

        Assert.That(_processes.Started, Is.Empty);
    }

    [Test]
    public async Task UngueltigerOrdner_BlockiertWeiterUndZeigtMeldung()
    {
        var vm = CreateViewModel();
        await vm.GoNextAsync();

        vm.InstallDirectory = _env.UserDataDirectory;

        Assert.That(vm.FolderError, Does.Contain("Profile und Einstellungen"));
        Assert.That(vm.CanGoNext, Is.False);
        await vm.GoNextAsync();
        Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Folder));

        vm.ResetFolderToDefault();
        Assert.That(vm.FolderError, Is.Null);
        Assert.That(vm.CanGoNext, Is.True);
    }

    [Test]
    public async Task BrowseFolderAsync_NichtLeererOrdner_SchlaegtUnterordnerVor()
    {
        var gewaehlt = Path.Combine(_env.Root, "Werkzeuge");
        Directory.CreateDirectory(gewaehlt);
        File.WriteAllText(Path.Combine(gewaehlt, "anderes.txt"), "x");
        _folderPicker.Setup(p => p.PickFolderAsync(It.IsAny<string?>())).ReturnsAsync(gewaehlt);
        var vm = CreateViewModel();

        await vm.BrowseFolderAsync();

        Assert.That(vm.InstallDirectory, Is.EqualTo(Path.Combine(gewaehlt, "StudyScope")));
        Assert.That(vm.FolderError, Is.Null);
    }

    [Test]
    public async Task BrowseFolderAsync_Abgebrochen_BehaeltOrdner()
    {
        _folderPicker.Setup(p => p.PickFolderAsync(It.IsAny<string?>())).ReturnsAsync((string?)null);
        var vm = CreateViewModel();

        await vm.BrowseFolderAsync();

        Assert.That(vm.InstallDirectory, Is.EqualTo(_env.InstallDirectory));
    }

    [Test]
    public async Task VorhandeneInstallation_WirdAlsUpdateErkanntUndOrdnerIstFest()
    {
        var anderer = Path.Combine(_env.Root, "Anders", "StudyScope");
        await _service.InstallAsync(new InstallOptions { InstallDirectory = anderer });

        var vm = CreateViewModel();
        vm.InstallDirectory = _env.InstallDirectory;

        Assert.That(vm.IsUpdate, Is.True);
        Assert.That(vm.InstalledVersion, Is.EqualTo("1.0.3"));
        Assert.That(vm.InstallDirectory, Is.EqualTo(Path.GetFullPath(anderer)), "Update muss im vorhandenen Ordner bleiben");
    }

    [Test]
    public async Task Fehler_BleibtAufFortschrittsseiteMitMeldungUndErlaubtZurueckUndWiederholen()
    {
        _processes.AppRunning = true;
        var vm = CreateViewModel();
        await vm.GoNextAsync(); await vm.GoNextAsync(); await vm.GoNextAsync();

        Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Progress));
        Assert.That(vm.Install.ErrorMessage, Does.Contain("StudyScope läuft noch"));
        Assert.That(vm.CanGoBack, Is.True);

        _processes.AppRunning = false;
        await vm.RetryAsync();

        Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Finished));
        Assert.That(vm.Install.HasError, Is.False);
    }

    [Test]
    public void MenueVerknuepfung_UnterLinuxPflicht()
    {
        _shortcuts.IsMenuShortcutOptional = false;
        var vm = CreateViewModel();

        vm.CreateMenuShortcut = false;

        Assert.That(vm.CreateMenuShortcut, Is.True);
        Assert.That(vm.IsMenuShortcutOptional, Is.False);
    }

    [Test]
    public void Cancel_OhneLaufendeInstallation_SchliesstSetup()
    {
        var vm = CreateViewModel();
        var closed = false;
        vm.CloseRequested += (_, _) => closed = true;

        vm.Cancel();

        Assert.That(closed, Is.True);
    }

    [Test]
    public async Task GoBack_NavigiertZurueck()
    {
        var vm = CreateViewModel();
        await vm.GoNextAsync(); await vm.GoNextAsync();

        vm.GoBack();
        Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Folder));
        vm.GoBack();
        Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Welcome));
    }
}

[TestFixture]
public class UninstallViewModelTests
{
    private Mock<IInstallService> _service = null!;
    private Mock<IPathProvider> _paths = null!;
    private readonly ExistingInstallation _existing = new("C:\\Programme\\StudyScope", new InstallManifest { Version = "1.0.3" });

    [SetUp]
    public void SetUp()
    {
        _service = new Mock<IInstallService>();
        _service.Setup(s => s.FindExistingInstallation()).Returns(_existing);
        _paths = new Mock<IPathProvider>();
        _paths.Setup(p => p.UserDataDirectory).Returns("C:\\Users\\anna\\AppData\\Roaming\\StudyScope");
    }

    private UninstallViewModel CreateViewModel()
    {
        var vm = new UninstallViewModel(_service.Object, _paths.Object);
        vm.Initialize();
        return vm;
    }

    [Test]
    public void Initialize_ZeigtInstallationUndBenutzerdatenSindStandardmaessigGeschuetzt()
    {
        var vm = CreateViewModel();

        Assert.That(vm.IsInstalled, Is.True);
        Assert.That(vm.InstalledVersion, Is.EqualTo("1.0.3"));
        Assert.That(vm.DeleteUserData, Is.False);
        Assert.That(vm.CurrentStep, Is.EqualTo(UninstallStep.Confirm));
    }

    [Test]
    public async Task Deinstallieren_OhneHaken_LoeschtKeineBenutzerdatenUndOhneRueckfrage()
    {
        var vm = CreateViewModel();

        await vm.RequestUninstallAsync();

        Assert.That(vm.IsDeleteConfirmationVisible, Is.False);
        _service.Verify(s => s.UninstallAsync(It.Is<UninstallOptions>(o => !o.DeleteUserData && o.InstallDirectory == _existing.InstallDirectory),
            It.IsAny<IProgress<InstallProgress>>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.That(vm.CurrentStep, Is.EqualTo(UninstallStep.Finished));
        Assert.That(vm.UserDataDeleted, Is.False);
    }

    [Test]
    public async Task Deinstallieren_MitHaken_ZeigtErstSicherheitsabfrage()
    {
        var vm = CreateViewModel();
        vm.DeleteUserData = true;

        await vm.RequestUninstallAsync();

        Assert.That(vm.IsDeleteConfirmationVisible, Is.True);
        _service.Verify(s => s.UninstallAsync(It.IsAny<UninstallOptions>(), It.IsAny<IProgress<InstallProgress>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Sicherheitsabfrage_Bestaetigt_LoeschtBenutzerdaten()
    {
        var vm = CreateViewModel();
        vm.DeleteUserData = true;
        await vm.RequestUninstallAsync();

        await vm.ConfirmDeleteUserDataAsync();

        _service.Verify(s => s.UninstallAsync(It.Is<UninstallOptions>(o => o.DeleteUserData),
            It.IsAny<IProgress<InstallProgress>>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.That(vm.IsDeleteConfirmationVisible, Is.False);
        Assert.That(vm.UserDataDeleted, Is.True);
    }

    [Test]
    public async Task Sicherheitsabfrage_Abgebrochen_DeinstalliertNicht()
    {
        var vm = CreateViewModel();
        vm.DeleteUserData = true;
        await vm.RequestUninstallAsync();

        vm.CancelDeleteConfirmation();

        Assert.That(vm.IsDeleteConfirmationVisible, Is.False);
        Assert.That(vm.CurrentStep, Is.EqualTo(UninstallStep.Confirm));
        _service.Verify(s => s.UninstallAsync(It.IsAny<UninstallOptions>(), It.IsAny<IProgress<InstallProgress>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ConfirmOhneAbfrage_TutNichts()
    {
        var vm = CreateViewModel();

        await vm.ConfirmDeleteUserDataAsync();

        _service.Verify(s => s.UninstallAsync(It.IsAny<UninstallOptions>(), It.IsAny<IProgress<InstallProgress>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Fehler_ZeigtMeldungUndBleibtAufBestaetigung()
    {
        _service.Setup(s => s.UninstallAsync(It.IsAny<UninstallOptions>(), It.IsAny<IProgress<InstallProgress>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InstallException("StudyScope läuft noch."));
        var vm = CreateViewModel();

        await vm.RequestUninstallAsync();

        Assert.That(vm.ErrorMessage, Is.EqualTo("StudyScope läuft noch."));
        Assert.That(vm.CurrentStep, Is.EqualTo(UninstallStep.Confirm));
        Assert.That(vm.IsRunning, Is.False);
    }

    [Test]
    public async Task NichtInstalliert_TutNichts()
    {
        _service.Setup(s => s.FindExistingInstallation()).Returns((ExistingInstallation?)null);
        var vm = CreateViewModel();

        await vm.RequestUninstallAsync();

        Assert.That(vm.IsInstalled, Is.False);
        _service.Verify(s => s.UninstallAsync(It.IsAny<UninstallOptions>(), It.IsAny<IProgress<InstallProgress>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
