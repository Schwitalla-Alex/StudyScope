using Moq;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.AI;
using StudyScope.Infrastructure.Services;
using StudyScope.ViewModels;

namespace StudyScope.Tests.ViewModels;

/// <summary>
/// Fall aus dem Test: Im Profil liegen zwei Dokumente, nur eines ist angehakt. Erzeugt werden darf nur aus dem
/// angehakten Dokument – für Präsentation, Video (neue Folien, Sprechtexte, Folien aktualisieren), Audio-Skript,
/// Datentabelle und Bericht. Außerdem: Warnung, wenn die vorhandene Präsentation aus anderen Dokumenten stammt.
/// </summary>
[TestFixture]
public class SelectedDocumentsTests
{
    private const string Profile = "Testprofil";
    private const string NotCheckedText = "Vulkane speien glühende Lava aus dem Erdinneren.";
    private const string CheckedText = "Photosynthese wandelt Licht in chemische Energie um.";
    private string _tempDir = null!;
    private TrackingDocuments _documents = null!;
    private DocumentItem _notChecked = null!;
    private DocumentItem _checked = null!;
    private DocumentItem[] _all = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "StudyScopeSelected_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _notChecked = new DocumentItem { FileName = "vulkane.txt", FilePath = Path.Combine(_tempDir, "vulkane.txt"), IsSelected = false };
        _checked = new DocumentItem { FileName = "pflanzen.txt", FilePath = Path.Combine(_tempDir, "pflanzen.txt"), IsSelected = true };
        _all = new[] { _notChecked, _checked };
        _documents = new TrackingDocuments(new Dictionary<string, string>
        {
            [_notChecked.FilePath] = NotCheckedText,
            [_checked.FilePath] = CheckedText
        });
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private VideoViewModel CreateVideoViewModel(ILlmService llm) =>
        new(new VideoScriptService(llm, _tempDir), new PresentationService(llm, _tempDir), _documents, new JsonStorageService(_tempDir),
            Array.Empty<ITextToSpeechService>());

    private static string AllText(VideoDocument video) =>
        string.Join(" ", video.Slides.SelectMany(s => s.Bullets.Append(s.Title).Append(s.Notes)).Concat(video.Scenes.Select(s => s.Narration)));

    // ---------- Video ----------

    [Test]
    public async Task Video_NewSlides_UseOnlyCheckedDocument()
    {
        var vm = CreateVideoViewModel(new FakeLlmService());
        await vm.LoadForProfileAsync(Profile);
        vm.SlideSource = VideoSlideSource.NewPresentation;

        await vm.GenerateAsync(_all);

        Assert.That(vm.IsError, Is.False, vm.StatusMessage);
        Assert.That(_documents.Extracted, Is.EqualTo(new[] { _checked.FilePath }));
        var video = vm.SelectedVideo!;
        Assert.That(video.SourceDocuments, Is.EqualTo(new[] { "pflanzen.txt" }));
        Assert.That(AllText(video), Does.Contain("Photosynthese").And.Not.Contain("Vulkan"));
    }

    [Test]
    public async Task Video_ProfilePresentationFromOtherDocument_WarnsAndOffersNewSlides()
    {
        // Präsentation wurde früher aus dem jetzt NICHT angehakten Dokument erstellt
        var presentationService = new PresentationService(new FakeLlmService(), _tempDir);
        var old = (await presentationService.GenerateFromDocumentsAsync(new[] { NotCheckedText }, "Alt", 3)).Presentation!;
        old.SourceDocuments = new List<string> { "vulkane.txt" };
        await presentationService.SavePresentationAsync(Profile, old);

        var vm = CreateVideoViewModel(new FakeLlmService());
        await vm.LoadForProfileAsync(Profile);
        Assert.That(vm.SlideSource, Is.EqualTo(VideoSlideSource.ProfilePresentation), "vorhandene Präsentation ist vorausgewählt");

        var info = vm.PresentationSources(_all);
        Assert.That(info.Match, Is.EqualTo(PresentationSourceMatch.Mismatch));
        Assert.That(info.Message, Is.EqualTo("Die Präsentation stammt aus: vulkane.txt. Angehakt ist: pflanzen.txt."));

        // Mit der vorhandenen Präsentation entstünde das Video aus dem alten Dokument (deshalb die Warnung) …
        await vm.GenerateAsync(_all);
        Assert.That(AllText(vm.SelectedVideo!), Does.Contain("Vulkan"));
        Assert.That(vm.SelectedVideo!.SourceDocuments, Is.EqualTo(new[] { "vulkane.txt" }), "Liste zeigt, woraus das Video stammt");
        Assert.That(_documents.Extracted, Is.Empty, "Dokumente werden dabei gar nicht gelesen");

        // … „Stattdessen neu aus den angehakten Dokumenten erzeugen“
        vm.OpenForm();
        await vm.GenerateFromSelectedDocumentsAsync(_all);
        var video = vm.SelectedVideo!;
        Assert.That(video.OwnSlides, Is.True);
        Assert.That(video.SourceDocuments, Is.EqualTo(new[] { "pflanzen.txt" }));
        Assert.That(AllText(video), Does.Contain("Photosynthese").And.Not.Contain("Vulkan"));
        Assert.That(_documents.Extracted, Is.EqualTo(new[] { _checked.FilePath }));
        Assert.That((await presentationService.LoadPresentationAsync(Profile))!.SourceDocuments, Is.EqualTo(new[] { "vulkane.txt" }), "Präsentation des Profils unverändert");
    }

    [Test]
    public async Task DefaultSlideSource_MismatchPreselectsNewSlides_UnknownAndMatchKeepPresentation()
    {
        var presentationService = new PresentationService(new FakeLlmService(), _tempDir);
        var old = (await presentationService.GenerateFromDocumentsAsync(new[] { NotCheckedText }, "Alt", 3)).Presentation!;
        old.SourceDocuments = new List<string> { "vulkane.txt" };
        await presentationService.SavePresentationAsync(Profile, old);

        // Laden mit den Dokumenten des Profils: abweichende Quellen → „Neue Folien“ vorausgewählt
        var vm = CreateVideoViewModel(new FakeLlmService());
        await vm.LoadForProfileAsync(Profile, _all);
        Assert.That(vm.IsFormOpen, Is.True);
        Assert.That(vm.SlideSource, Is.EqualTo(VideoSlideSource.NewPresentation));
        Assert.That(vm.PresentationSources(_all).Match, Is.EqualTo(PresentationSourceMatch.Mismatch), "Warnung bleibt");

        // Bewusst zurück auf die Präsentation des Profils – bleibt beim Wiederöffnen des Reiters erhalten
        vm.SlideSource = VideoSlideSource.ProfilePresentation;
        await vm.EnsureLoadedForProfileAsync(Profile, _all);
        Assert.That(vm.SlideSource, Is.EqualTo(VideoSlideSource.ProfilePresentation));

        // „Neues Video“ wählt wieder nach den Quellen vor
        vm.OpenForm(_all);
        Assert.That(vm.SlideSource, Is.EqualTo(VideoSlideSource.NewPresentation));

        // Passende Quellen → Präsentation des Profils
        old.SourceDocuments = new List<string> { "pflanzen.txt" };
        await presentationService.SavePresentationAsync(Profile, old);
        await vm.EnsureLoadedForProfileAsync(Profile, _all);
        vm.OpenForm(_all);
        Assert.That(vm.SlideSource, Is.EqualTo(VideoSlideSource.ProfilePresentation));

        // Unbekannte Quellen (ältere Präsentation) → bisherige Vorauswahl
        old.SourceDocuments = new List<string>();
        await presentationService.SavePresentationAsync(Profile, old);
        await vm.EnsureLoadedForProfileAsync(Profile, _all);
        vm.OpenForm(_all);
        Assert.That(vm.SlideSource, Is.EqualTo(VideoSlideSource.ProfilePresentation));

        // Ohne Präsentation → neue Folien
        await presentationService.DeletePresentationAsync(Profile);
        await vm.EnsureLoadedForProfileAsync(Profile, _all);
        vm.OpenForm(_all);
        Assert.That(vm.SlideSource, Is.EqualTo(VideoSlideSource.NewPresentation));
    }

    [Test]
    public void ComparePresentationSources_MatchUnknownAndMissingDocuments()
    {
        var presentation = new PresentationDocument { Slides = { new PresentationSlide { Title = "A" } }, SourceDocuments = { "PFLANZEN.txt" } };
        Assert.That(VideoViewModel.ComparePresentationSources(presentation, _all).Match, Is.EqualTo(PresentationSourceMatch.Match), "ohne Groß-/Kleinschreibung");
        Assert.That(VideoViewModel.ComparePresentationSources(presentation, _all).Message, Is.EqualTo("Erstellt aus: PFLANZEN.txt"));

        // Fehlende Dateien zählen nicht als angehakt
        _checked.IsMissing = true;
        var missing = VideoViewModel.ComparePresentationSources(presentation, _all);
        Assert.That(missing.Match, Is.EqualTo(PresentationSourceMatch.Mismatch));
        Assert.That(missing.Message, Does.EndWith("Angehakt ist: kein Dokument."));

        presentation.SourceDocuments.Clear();
        var unknown = VideoViewModel.ComparePresentationSources(presentation, _all);
        Assert.That(unknown.Match, Is.EqualTo(PresentationSourceMatch.Unknown));
        Assert.That(unknown.Message, Does.Contain("nicht gespeichert"));

        Assert.That(VideoViewModel.ComparePresentationSources(null, _all).Match, Is.EqualTo(PresentationSourceMatch.None));
    }

    [Test]
    public async Task Video_ScriptAndRefreshSlides_UseOnlyPresentation_RefreshTakesItsSources()
    {
        var presentationService = new PresentationService(new FakeLlmService(), _tempDir);
        var first = (await presentationService.GenerateFromDocumentsAsync(new[] { CheckedText }, "P", 3)).Presentation!;
        first.SourceDocuments = new List<string> { "pflanzen.txt" };
        await presentationService.SavePresentationAsync(Profile, first);

        var llm = new RecordingLlm();
        var vm = CreateVideoViewModel(llm);
        await vm.LoadForProfileAsync(Profile);
        Assert.That(vm.PresentationSources(_all).Match, Is.EqualTo(PresentationSourceMatch.Match));
        await vm.GenerateAsync(_all);

        // Sprechtexte: die KI bekommt nur die Folien als Auszüge – keine Dokumente
        Assert.That(_documents.Extracted, Is.Empty);
        Assert.That(llm.LastChunks, Has.All.StartWith("Folie "));
        Assert.That(string.Join(" ", llm.LastChunks), Does.Not.Contain("Vulkan"));

        // „Folien aktualisieren“ übernimmt Folien und Quellen der (neuen) Präsentation
        var second = (await presentationService.GenerateFromDocumentsAsync(new[] { CheckedText, "Chlorophyll färbt Blätter grün und fängt Licht ein." }, "P", 3)).Presentation!;
        second.SourceDocuments = new List<string> { "pflanzen.txt", "blaetter.txt" };
        await presentationService.SavePresentationAsync(Profile, second);
        await vm.EnsureLoadedForProfileAsync(Profile);
        await vm.RefreshSlidesAsync();
        Assert.That(vm.SelectedVideo!.SourceDocuments, Is.EqualTo(new[] { "pflanzen.txt", "blaetter.txt" }));
        Assert.That(_documents.Extracted, Is.Empty);
    }

    // ---------- Präsentation, Audio, Datentabelle, Bericht ----------

    [Test]
    public async Task Presentation_UsesOnlyCheckedDocument()
    {
        var vm = new PresentationViewModel(new PresentationService(new FakeLlmService(), _tempDir), new MindmapService(new FakeLlmService(), _tempDir), _documents);
        await vm.GenerateFromDocumentsAsync(Profile, _all);

        Assert.That(_documents.Extracted, Is.EqualTo(new[] { _checked.FilePath }));
        Assert.That(vm.Presentation!.SourceDocuments, Is.EqualTo(new[] { "pflanzen.txt" }));
    }

    [Test]
    public async Task AudioScript_UsesOnlyCheckedDocument()
    {
        var vm = new AudioViewModel(new AudioScriptService(new FakeLlmService(), _tempDir), _documents, new JsonStorageService(_tempDir), Array.Empty<ITextToSpeechService>());
        await vm.LoadForProfileAsync(Profile);
        await vm.GenerateAsync(_all);

        Assert.That(vm.IsError, Is.False, vm.StatusMessage);
        Assert.That(_documents.Extracted, Is.EqualTo(new[] { _checked.FilePath }));
        Assert.That(vm.SelectedSummary!.SourceDocuments, Is.EqualTo(new[] { "pflanzen.txt" }));
        Assert.That(string.Join(" ", vm.SelectedSummary.Segments.Select(s => s.Text)), Does.Not.Contain("Vulkan"));
    }

    [Test]
    public async Task DataTable_UsesOnlyCheckedDocument()
    {
        var vm = new DataTableViewModel(new DataTableService(new FakeLlmService(), _tempDir), _documents);
        await vm.LoadForProfileAsync(Profile);
        await vm.GenerateAsync(_all);

        Assert.That(vm.IsError, Is.False, vm.StatusMessage);
        Assert.That(_documents.Extracted, Is.EqualTo(new[] { _checked.FilePath }));
        Assert.That(vm.SelectedTable!.SourceDocuments, Is.EqualTo(new[] { "pflanzen.txt" }));
    }

    [Test]
    public async Task Report_UsesOnlyCheckedDocument()
    {
        var vm = new ReportViewModel(new ReportService(new FakeLlmService(), _tempDir), _documents,
            Mock.Of<IMindmapService>(), Mock.Of<IFlashcardService>(), Mock.Of<IQuizService>(), Mock.Of<IPresentationService>(),
            new DataTableService(new FakeLlmService(), _tempDir), new AudioScriptService(new FakeLlmService(), _tempDir));
        await vm.LoadForProfileAsync(Profile);
        await vm.GenerateAsync(_all);

        Assert.That(vm.IsError, Is.False, vm.StatusMessage);
        Assert.That(_documents.Extracted, Is.EqualTo(new[] { _checked.FilePath }));
        Assert.That(vm.SelectedReport!.SourceDocuments, Is.EqualTo(new[] { "pflanzen.txt" }));
    }

    // ---------- Hilfsklassen ----------

    /// <summary>Liefert je Pfad einen eigenen Text und merkt sich, welche Dokumente gelesen wurden.</summary>
    private sealed class TrackingDocuments(Dictionary<string, string> texts) : IDocumentService
    {
        public List<string> Extracted { get; } = new();
        public Task<int> GetChunkCountAsync(string filePath) => Task.FromResult(1);
        public Task<List<string>> ExtractChunksAsync(string filePath, int maxWordsPerChunk = 250)
        {
            Extracted.Add(filePath);
            return Task.FromResult(new List<string> { texts[filePath] });
        }
    }

    private sealed class RecordingLlm : ILlmService
    {
        private readonly FakeLlmService _inner = new();
        public List<string> LastChunks { get; private set; } = new();
        public Task<bool> ValidateConnectionAsync(AiConfig? config = null) => Task.FromResult(true);
        public Task<string> AskQuestionAsync(string prompt, IEnumerable<string> contextChunks, CancellationToken cancellationToken = default)
        {
            LastChunks = contextChunks.ToList();
            return _inner.AskQuestionAsync(prompt, LastChunks, cancellationToken);
        }
    }
}
