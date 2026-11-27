using Moq;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.ViewModels;

namespace StudyScope.Tests.ViewModels;

/// <summary>
/// Eine Meldung zu <see cref="DocumentListViewModel.CurrentProfile"/> verstehen die Reiter als Profilwechsel: Sie laden
/// neu, und Audio/Video beenden eine laufende Wiedergabe. Deshalb meldet nur ein echter Wechsel (und einmal der Start)
/// das Profil – nicht das Speichern der KI-Einstellungen oder ein Sprachwechsel (beide rufen
/// <see cref="DocumentListViewModel.RefreshCurrentAiModelAsync"/>).
/// </summary>
[TestFixture]
public class ProfileAnnouncementTests
{
    private static (DocumentListViewModel Vm, List<string> Changes) Create()
    {
        var storage = new Mock<IStorageService>();
        storage.Setup(s => s.GetAllProfileNamesAsync()).ReturnsAsync(new List<string> { "Standard", "Testprofil" });
        storage.Setup(s => s.GetLastActiveProfileNameAsync()).ReturnsAsync("Standard");
        storage.Setup(s => s.LoadDocumentsAsync(It.IsAny<string>())).ReturnsAsync(new List<DocumentItem>());
        storage.Setup(s => s.LoadAiConfigForProfileAsync(It.IsAny<string>())).ReturnsAsync(new AiConfig());
        var vm = new DocumentListViewModel(storage.Object, new Mock<IDocumentService>().Object);
        var changes = new List<string>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName ?? string.Empty);
        return (vm, changes);
    }

    [Test]
    public async Task SavingAiSettings_DoesNotAnnounceAProfileChange()
    {
        var (vm, changes) = Create();
        await vm.InitializeAsync();
        changes.Clear();

        // So rufen „Speichern & Verbinden“ (SettingsModal) und der Sprachwechsel (App.razor) auf
        await vm.RefreshCurrentAiModelAsync();

        Assert.That(changes, Does.Contain(nameof(DocumentListViewModel.CurrentAiModelDisplayName)));
        Assert.That(changes, Does.Not.Contain(nameof(DocumentListViewModel.CurrentProfile)));
    }

    [Test]
    public async Task Start_AnnouncesTheProfileOnce_EvenIfItEqualsTheInitialValue()
    {
        var (vm, changes) = Create();
        await vm.InitializeAsync();
        Assert.That(changes.Count(c => c == nameof(DocumentListViewModel.CurrentProfile)), Is.EqualTo(1));
    }

    [Test]
    public async Task SwitchingProfiles_AnnouncesTheProfile()
    {
        var (vm, changes) = Create();
        await vm.InitializeAsync();
        changes.Clear();
        await vm.SwitchProfileAsync("Testprofil");
        Assert.That(changes.Count(c => c == nameof(DocumentListViewModel.CurrentProfile)), Is.EqualTo(1));
    }
}
