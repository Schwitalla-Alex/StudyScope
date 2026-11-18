using StudyScope.Core.Models;
using StudyScope.Infrastructure.Services;
using StudyScope.Tests.Services;
using StudyScope.ViewModels;

namespace StudyScope.Tests.ViewModels;

/// <summary>Einstellungen › Export: Pfad laden, testen ohne Speichern, speichern und neu suchen.</summary>
[TestFixture]
public class ExportSettingsViewModelTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "StudyScopeExportVm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
    }

    [Test]
    public async Task Load_Test_Save_Roundtrip()
    {
        var storage = new JsonStorageService(_tempDir);
        await storage.SaveExportSettingsAsync(new ExportSettings { FfmpegPath = @"C:\alt\ffmpeg.exe" });
        var ffmpeg = new VideoMp4Tests.FakeFfmpeg();
        var vm = new ExportSettingsViewModel(storage, ffmpeg);

        await vm.LoadAsync();
        Assert.That(vm.FfmpegPath, Is.EqualTo(@"C:\alt\ffmpeg.exe"));
        Assert.That(vm.Status!.IsAvailable, Is.True);
        Assert.That(vm.InstallCommand, Is.EqualTo(OperatingSystem.IsLinux() ? FfmpegInfo.LinuxInstall : FfmpegInfo.WindowsInstall));

        vm.FfmpegPath = "  \"D:\\Neu mit Leerzeichen\\ffmpeg.exe\"  ";
        await vm.TestAsync();
        Assert.That((await storage.LoadExportSettingsAsync()).FfmpegPath, Is.EqualTo(@"C:\alt\ffmpeg.exe"), "Testen speichert nicht");

        Assert.That(await vm.SaveAsync(), Is.True);
        Assert.That((await storage.LoadExportSettingsAsync()).FfmpegPath, Is.EqualTo(@"D:\Neu mit Leerzeichen\ffmpeg.exe"), "ohne Anführungszeichen");
        Assert.That(vm.IsSuccess, Is.True);
        Assert.That(vm.StatusMessage, Does.StartWith("✓ Gespeichert"));
    }

    [Test]
    public async Task Save_WhenNotFound_SavedButWarned()
    {
        var ffmpeg = new VideoMp4Tests.FakeFfmpeg { Status = FfmpegStatus.NotFound("ffmpeg wurde nicht gefunden.", "Hinweis") };
        var vm = new ExportSettingsViewModel(new JsonStorageService(_tempDir), ffmpeg);
        await vm.LoadAsync();

        Assert.That(await vm.SaveAsync(), Is.True);
        Assert.That(vm.IsSuccess, Is.False);
        Assert.That(vm.StatusMessage, Does.Contain("nicht gefunden"));
    }
}
