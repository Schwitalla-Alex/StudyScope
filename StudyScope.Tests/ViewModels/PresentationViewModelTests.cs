using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.ViewModels;

namespace StudyScope.Tests.ViewModels;

/// <summary>
/// Tests für das PresentationViewModel: Erstellen (KI/Mindmap), Blättern, Vortragsmodus, Tastatur und Export.
/// </summary>
[TestFixture]
public class PresentationViewModelTests
{
    private FakePresentationService _presentationService = null!;
    private FakeMindmapService _mindmapService = null!;
    private PresentationViewModel _viewModel = null!;

    [SetUp]
    public void SetUp()
    {
        _presentationService = new FakePresentationService();
        _mindmapService = new FakeMindmapService();
        _viewModel = new PresentationViewModel(_presentationService, _mindmapService, new FakeDocumentService());
    }

    private static PresentationDocument CreatePresentation(int contentSlides = 3)
    {
        var p = new PresentationDocument { Title = "Vortrag" };
        p.Slides.Add(new PresentationSlide { Kind = SlideKind.Title, Title = "Vortrag" });
        for (var i = 1; i <= contentSlides; i++)
            p.Slides.Add(new PresentationSlide { Title = $"Folie {i}", Bullets = { "a", "b" } });
        return p;
    }

    private static List<DocumentItem> Docs(params (string name, bool selected)[] docs) =>
        docs.Select(d => new DocumentItem { FileName = d.name, FilePath = "/tmp/" + d.name, IsSelected = d.selected }).ToList();

    [Test]
    public async Task GenerateFromDocumentsAsync_WithoutSelection_ShowsError()
    {
        await _viewModel.GenerateFromDocumentsAsync("Testprofil", Docs(("a.pdf", false)));

        Assert.That(_viewModel.IsError, Is.True);
        Assert.That(_presentationService.GenerateCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task GenerateFromDocumentsAsync_Success_SavesAndShows()
    {
        _presentationService.NextResult = new PresentationGenerationResult { Presentation = CreatePresentation() };
        _viewModel.SlideCount = 5;

        await _viewModel.GenerateFromDocumentsAsync("Testprofil", Docs(("skript.pdf", true)));

        Assert.That(_viewModel.HasPresentation, Is.True);
        Assert.That(_viewModel.CurrentIndex, Is.EqualTo(0));
        Assert.That(_presentationService.LastSlideCount, Is.EqualTo(5));
        Assert.That(_presentationService.LastFallbackTitle, Is.EqualTo("skript"));
        Assert.That(_presentationService.Saved["Testprofil"].SourceDocuments, Is.EqualTo(new[] { "skript.pdf" }));
        Assert.That(_viewModel.IsError, Is.False);
    }

    [Test]
    public async Task GenerateFromDocumentsAsync_Failure_ShowsPresentationSpecificMessage()
    {
        _presentationService.NextResult = new PresentationGenerationResult
        {
            RawResponse = "[{\"title\": \"abgeschn",
            FailureReason = MindmapFailureReason.InvalidJson
        };

        await _viewModel.GenerateFromDocumentsAsync("Testprofil", Docs(("a.pdf", true)));

        Assert.That(_viewModel.IsError, Is.True);
        Assert.That(_viewModel.StatusMessage, Does.Contain("keine Präsentation"));
        Assert.That(_viewModel.StatusTips.Any(t => t.Contains("Mit KI erstellen")), Is.True);
        Assert.That(_viewModel.StatusDetails, Does.Contain("abgeschn"));
    }

    [Test]
    public async Task GenerateFromDocumentsAsync_Repaired_ShowsWarning()
    {
        _presentationService.NextResult = new PresentationGenerationResult { Presentation = CreatePresentation(), WasRepaired = true };

        await _viewModel.GenerateFromDocumentsAsync("Testprofil", Docs(("a.pdf", true)));

        Assert.That(_viewModel.IsWarning, Is.True);
        Assert.That(_viewModel.HasPresentation, Is.True);
    }

    [Test]
    public async Task CreateFromMindmapAsync_WithoutMindmap_ShowsHint()
    {
        await _viewModel.CreateFromMindmapAsync("Testprofil");

        Assert.That(_viewModel.HasPresentation, Is.False);
        Assert.That(_viewModel.IsError, Is.True);
        Assert.That(_viewModel.StatusMessage, Does.Contain("noch keine Mindmap"));
    }

    [Test]
    public async Task CreateFromMindmapAsync_WithMindmap_CreatesAndSaves()
    {
        _mindmapService.Mindmap = new MindmapDocument { Root = new MindmapNode { Title = "Wurzel" } };

        await _viewModel.CreateFromMindmapAsync("Testprofil");

        Assert.That(_viewModel.HasPresentation, Is.True);
        Assert.That(_presentationService.Saved.ContainsKey("Testprofil"), Is.True);
        Assert.That(_viewModel.Presentation!.Origin, Is.EqualTo(PresentationOrigin.Mindmap));
    }

    [Test]
    public async Task Navigation_StaysWithinBounds()
    {
        _presentationService.Saved["Testprofil"] = CreatePresentation(contentSlides: 2); // 3 Folien
        await _viewModel.LoadForProfileAsync("Testprofil");

        _viewModel.Previous();
        Assert.That(_viewModel.CurrentIndex, Is.EqualTo(0));
        Assert.That(_viewModel.HasPrevious, Is.False);

        _viewModel.Next();
        _viewModel.Next();
        _viewModel.Next();
        Assert.That(_viewModel.CurrentIndex, Is.EqualTo(2));
        Assert.That(_viewModel.HasNext, Is.False);
        Assert.That(_viewModel.ProgressText, Is.EqualTo("3 / 3"));

        _viewModel.GoTo(1);
        Assert.That(_viewModel.CurrentSlide!.Title, Is.EqualTo("Folie 1"));
    }

    [Test]
    public async Task HandleKey_NavigatesAndEscapeStopsPresenting()
    {
        _presentationService.Saved["Testprofil"] = CreatePresentation(contentSlides: 4);
        await _viewModel.LoadForProfileAsync("Testprofil");
        _viewModel.GoTo(2);

        _viewModel.StartPresenting(fromBeginning: true);
        Assert.That(_viewModel.IsPresenting, Is.True);
        Assert.That(_viewModel.CurrentIndex, Is.EqualTo(0));

        Assert.That(_viewModel.HandleKey("ArrowRight"), Is.True);
        Assert.That(_viewModel.HandleKey(" "), Is.True);
        Assert.That(_viewModel.CurrentIndex, Is.EqualTo(2));
        _viewModel.HandleKey("ArrowLeft");
        Assert.That(_viewModel.CurrentIndex, Is.EqualTo(1));
        _viewModel.HandleKey("End");
        Assert.That(_viewModel.CurrentIndex, Is.EqualTo(4));
        _viewModel.HandleKey("Home");
        Assert.That(_viewModel.CurrentIndex, Is.EqualTo(0));
        Assert.That(_viewModel.HandleKey("x"), Is.False);

        _viewModel.HandleKey("Escape");
        Assert.That(_viewModel.IsPresenting, Is.False);
    }

    [Test]
    public async Task StartPresenting_FromCurrentSlide_KeepsPosition()
    {
        _presentationService.Saved["Testprofil"] = CreatePresentation();
        await _viewModel.LoadForProfileAsync("Testprofil");
        _viewModel.GoTo(2);

        _viewModel.StartPresenting(fromBeginning: false);

        Assert.That(_viewModel.CurrentIndex, Is.EqualTo(2));
    }

    [Test]
    public async Task LoadForProfileAsync_StopsPresentingAndLoadsOtherProfile()
    {
        _presentationService.Saved["A"] = CreatePresentation();
        await _viewModel.LoadForProfileAsync("A");
        _viewModel.StartPresenting(true);

        await _viewModel.LoadForProfileAsync("B");

        Assert.That(_viewModel.IsPresenting, Is.False);
        Assert.That(_viewModel.HasPresentation, Is.False);
        Assert.That(_viewModel.CurrentSlide, Is.Null);
    }

    [Test]
    public async Task ExportToPptxAsync_ReportsSuccessAndFailure()
    {
        _presentationService.Saved["Testprofil"] = CreatePresentation();
        await _viewModel.LoadForProfileAsync("Testprofil");

        Assert.That(await _viewModel.ExportToPptxAsync("/tmp/vortrag.pptx"), Is.True);
        Assert.That(_viewModel.StatusMessage, Does.Contain("vortrag.pptx"));

        _presentationService.ExportThrows = true;
        Assert.That(await _viewModel.ExportToPptxAsync("/tmp/vortrag.pptx"), Is.False);
        Assert.That(_viewModel.IsError, Is.True);
    }

    [Test]
    public async Task SuggestFileName_RemovesInvalidCharacters()
    {
        var p = CreatePresentation();
        p.Title = "Bio: Zellen/Teil 1?";
        _presentationService.Saved["Testprofil"] = p;
        await _viewModel.LoadForProfileAsync("Testprofil");

        var name = _viewModel.SuggestFileName("pptx");

        Assert.That(name, Does.StartWith("Bio_ Zellen_Teil 1_"));
        Assert.That(name, Does.EndWith(".pptx"));
    }

    [Test]
    public void SlideCount_IsClamped()
    {
        _viewModel.SlideCount = 1;
        Assert.That(_viewModel.SlideCount, Is.EqualTo(3));
        _viewModel.SlideCount = 50;
        Assert.That(_viewModel.SlideCount, Is.EqualTo(20));
    }

    [Test]
    public void GetDensity_DependsOnTextAmount()
    {
        var few = new PresentationSlide { Bullets = { "Kurz", "Kurz" } };
        var many = new PresentationSlide { Bullets = Enumerable.Repeat(new string('x', 120), 6).ToList() };

        Assert.That(PresentationViewModel.GetDensity(few), Is.EqualTo(SlideDensity.Normal));
        Assert.That(PresentationViewModel.GetDensity(many), Is.EqualTo(SlideDensity.VeryDense));
    }

    // ---------- Attrappen ----------

    private sealed class FakePresentationService : IPresentationService
    {
        public PresentationGenerationResult NextResult { get; set; } = new();
        public int GenerateCalls { get; private set; }
        public int LastSlideCount { get; private set; }
        public string? LastFallbackTitle { get; private set; }
        public bool ExportThrows { get; set; }
        public Dictionary<string, PresentationDocument> Saved { get; } = new();

        public Task<PresentationGenerationResult> GenerateFromDocumentsAsync(IEnumerable<string> contextChunks, string fallbackTitle, int slideCount, string? outputLanguage = null, CancellationToken cancellationToken = default)
        {
            GenerateCalls++;
            LastSlideCount = slideCount;
            LastFallbackTitle = fallbackTitle;
            return Task.FromResult(NextResult);
        }

        public PresentationDocument CreateFromMindmap(MindmapDocument mindmap)
        {
            var p = new PresentationDocument { Title = mindmap.Root.Title, Origin = PresentationOrigin.Mindmap };
            p.Slides.Add(new PresentationSlide { Kind = SlideKind.Title, Title = mindmap.Root.Title });
            return p;
        }

        public Task<PresentationDocument?> LoadPresentationAsync(string profileName) =>
            Task.FromResult(Saved.TryGetValue(profileName, out var p) ? p : null);

        public Task SavePresentationAsync(string profileName, PresentationDocument presentation)
        {
            Saved[profileName] = presentation;
            return Task.CompletedTask;
        }

        public Task DeletePresentationAsync(string profileName)
        {
            Saved.Remove(profileName);
            return Task.CompletedTask;
        }

        public Task ExportToPptxAsync(PresentationDocument presentation, string filePath, System.Globalization.CultureInfo culture) =>
            ExportThrows ? throw new IOException("Datei ist gesperrt") : Task.CompletedTask;
    }

    private sealed class FakeMindmapService : IMindmapService
    {
        public MindmapDocument? Mindmap { get; set; }

        public Task<MindmapGenerationResult> GenerateMindmapAsync(IEnumerable<string> contextChunks, string rootTitle, string? outputLanguage = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MindmapGenerationResult());

        public Task<MindmapDocument?> LoadMindmapAsync(string profileName) => Task.FromResult(Mindmap);
        public Task SaveMindmapAsync(string profileName, MindmapDocument mindmap) => Task.CompletedTask;
        public Task DeleteMindmapAsync(string profileName) => Task.CompletedTask;
        public string ExportToMarkdown(MindmapDocument mindmap, System.Globalization.CultureInfo culture) => string.Empty;
    }

    private sealed class FakeDocumentService : IDocumentService
    {
        public Task<int> GetChunkCountAsync(string filePath) => Task.FromResult(1);

        public Task<List<string>> ExtractChunksAsync(string filePath, int maxWordsPerChunk = 250) =>
            Task.FromResult(new List<string> { $"Inhalt von {Path.GetFileName(filePath)}" });
    }
}
