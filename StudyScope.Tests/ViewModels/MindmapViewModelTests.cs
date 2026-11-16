using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.ViewModels;
using StudyScope.ViewModels.Mindmap;

namespace StudyScope.Tests.ViewModels;

/// <summary>
/// Tests für das MindmapViewModel (Erzeugen, Auf-/Zuklappen, Auswahl, Chat-Frage) und das Baum-Layout.
/// </summary>
[TestFixture]
public class MindmapViewModelTests
{
    private FakeMindmapService _mindmapService = null!;
    private FakeDocumentService _documentService = null!;
    private MindmapViewModel _viewModel = null!;

    [SetUp]
    public void SetUp()
    {
        _mindmapService = new FakeMindmapService();
        _documentService = new FakeDocumentService();
        _viewModel = new MindmapViewModel(_mindmapService, _documentService);
    }

    private static MindmapNode CreateTree() => new()
    {
        Title = "Wurzel",
        Children =
        {
            new MindmapNode
            {
                Title = "Ast A",
                Children = { new MindmapNode { Title = "Blatt A1" }, new MindmapNode { Title = "Blatt A2" } }
            },
            new MindmapNode { Title = "Ast B" }
        }
    };

    private static List<DocumentItem> Docs(params (string name, bool selected)[] docs) =>
        docs.Select(d => new DocumentItem { FileName = d.name, FilePath = "/tmp/" + d.name, IsSelected = d.selected }).ToList();

    [Test]
    public async Task GenerateAsync_WithoutSelectedDocuments_ShowsErrorAndDoesNotCallService()
    {
        await _viewModel.GenerateAsync("Testprofil", Docs(("a.pdf", false)));

        Assert.That(_viewModel.IsError, Is.True);
        Assert.That(_mindmapService.GenerateCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task GenerateAsync_Success_SavesAndShowsRootWithMainBranches()
    {
        _mindmapService.NextResult = new MindmapGenerationResult { Root = CreateTree() };

        await _viewModel.GenerateAsync("Testprofil", Docs(("skript.pdf", true), ("alt.pdf", false)));

        Assert.That(_viewModel.HasMindmap, Is.True);
        Assert.That(_viewModel.IsError, Is.False);
        Assert.That(_viewModel.IsGenerating, Is.False);
        Assert.That(_mindmapService.Saved["Testprofil"].SourceDocuments, Is.EqualTo(new[] { "skript.pdf" }));
        Assert.That(_mindmapService.LastRootTitle, Is.EqualTo("skript"));
        Assert.That(_documentService.ExtractedPaths, Is.EqualTo(new[] { "/tmp/skript.pdf" }));

        // Ausgangszustand: Wurzel + 2 Hauptäste sichtbar, Blätter eingeklappt
        Assert.That(_viewModel.Layout.Nodes.Select(n => n.Node.Title), Is.EqualTo(new[] { "Wurzel", "Ast A", "Ast B" }));
        Assert.That(_viewModel.Layout.Edges, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GenerateAsync_SeveralDocuments_UsesProfileAsRootTitle()
    {
        _mindmapService.NextResult = new MindmapGenerationResult { Root = CreateTree() };

        await _viewModel.GenerateAsync("Testprofil", Docs(("a.pdf", true), ("b.pdf", true)));

        Assert.That(_mindmapService.LastRootTitle, Is.EqualTo("Unterlagen: Testprofil"));
    }

    [Test]
    public async Task GenerateAsync_InvalidResponse_ShowsErrorWithRawHint()
    {
        _mindmapService.NextResult = new MindmapGenerationResult { RawResponse = "Netzwerkfehler zu Google Gemini" };

        await _viewModel.GenerateAsync("Testprofil", Docs(("a.pdf", true)));

        Assert.That(_viewModel.HasMindmap, Is.False);
        Assert.That(_viewModel.IsError, Is.True);
        Assert.That(_viewModel.StatusMessage, Does.Contain("Netzwerkfehler"));
        Assert.That(_mindmapService.Saved, Is.Empty);
    }

    [Test]
    public async Task ToggleNode_ExpandsAndCollapsesBranch()
    {
        _mindmapService.NextResult = new MindmapGenerationResult { Root = CreateTree() };
        await _viewModel.GenerateAsync("Testprofil", Docs(("a.pdf", true)));
        var astA = _viewModel.Mindmap!.Root.Children[0];

        _viewModel.ToggleNode(astA.Id);
        Assert.That(_viewModel.Layout.Nodes, Has.Count.EqualTo(5));

        _viewModel.ToggleNode(astA.Id);
        Assert.That(_viewModel.Layout.Nodes, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task ExpandAllAndCollapseAll()
    {
        _mindmapService.NextResult = new MindmapGenerationResult { Root = CreateTree() };
        await _viewModel.GenerateAsync("Testprofil", Docs(("a.pdf", true)));

        _viewModel.ExpandAll();
        Assert.That(_viewModel.Layout.Nodes, Has.Count.EqualTo(5));

        _viewModel.CollapseAll();
        Assert.That(_viewModel.Layout.Nodes, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task BuildQuestionFor_MentionsParentTopic()
    {
        _mindmapService.NextResult = new MindmapGenerationResult { Root = CreateTree() };
        await _viewModel.GenerateAsync("Testprofil", Docs(("a.pdf", true)));
        var root = _viewModel.Mindmap!.Root;
        var leaf = root.Children[0].Children[1];

        Assert.That(_viewModel.BuildQuestionFor(leaf), Does.Contain("„Blatt A2“").And.Contain("„Ast A“"));
        Assert.That(_viewModel.BuildQuestionFor(root), Does.Contain("Überblick"));
    }

    [Test]
    public async Task SelectNode_SetsPathFromRoot()
    {
        _mindmapService.NextResult = new MindmapGenerationResult { Root = CreateTree() };
        await _viewModel.GenerateAsync("Testprofil", Docs(("a.pdf", true)));
        var leaf = _viewModel.Mindmap!.Root.Children[0].Children[0];

        _viewModel.SelectNode(leaf.Id);

        Assert.That(_viewModel.SelectedNode, Is.SameAs(leaf));
        Assert.That(_viewModel.SelectedPath.Select(n => n.Title), Is.EqualTo(new[] { "Wurzel", "Ast A", "Blatt A1" }));

        _viewModel.SelectNode(null);
        Assert.That(_viewModel.SelectedNode, Is.Null);
    }

    [Test]
    public async Task LoadForProfileAsync_LoadsSavedMindmapOrNothing()
    {
        _mindmapService.Saved["Testprofil"] = new MindmapDocument { Root = CreateTree() };

        await _viewModel.LoadForProfileAsync("Testprofil");
        Assert.That(_viewModel.HasMindmap, Is.True);

        await _viewModel.LoadForProfileAsync("Anderes Profil");
        Assert.That(_viewModel.HasMindmap, Is.False);
        Assert.That(_viewModel.Layout.Nodes, Is.Empty);
    }

    [Test]
    public async Task DeleteAsync_RemovesMindmap()
    {
        _mindmapService.Saved["Testprofil"] = new MindmapDocument { Root = CreateTree() };
        await _viewModel.LoadForProfileAsync("Testprofil");

        await _viewModel.DeleteAsync("Testprofil");

        Assert.That(_viewModel.HasMindmap, Is.False);
        Assert.That(_mindmapService.Saved, Is.Empty);
    }

    [Test]
    public void Zoom_IsClamped()
    {
        for (var i = 0; i < 20; i++) _viewModel.ZoomIn();
        Assert.That(_viewModel.Zoom, Is.EqualTo(MindmapViewModel.MaxZoom));

        for (var i = 0; i < 20; i++) _viewModel.ZoomOut();
        Assert.That(_viewModel.Zoom, Is.EqualTo(MindmapViewModel.MinZoom));

        _viewModel.ResetZoom();
        Assert.That(_viewModel.ZoomText, Is.EqualTo("100 %"));
    }

    // ---------- Layout ----------

    [Test]
    public void Layout_ChildrenAreRightOfParentAndParentIsCentered()
    {
        var root = CreateTree();
        var layout = MindmapLayout.Calculate(root, _ => true);

        var byTitle = layout.Nodes.ToDictionary(n => n.Node.Title);
        Assert.That(byTitle["Ast A"].X, Is.GreaterThan(byTitle["Wurzel"].Right));
        Assert.That(byTitle["Blatt A1"].X, Is.GreaterThan(byTitle["Ast A"].Right));

        var expectedY = (byTitle["Blatt A1"].Y + byTitle["Blatt A2"].Y) / 2;
        Assert.That(byTitle["Ast A"].Y, Is.EqualTo(expectedY));
        Assert.That(byTitle["Ast B"].BranchIndex, Is.EqualTo(1));
        Assert.That(byTitle["Blatt A2"].BranchIndex, Is.EqualTo(0));
    }

    [Test]
    public void Layout_NodesInSameColumnDoNotOverlap()
    {
        var root = CreateTree();
        var layout = MindmapLayout.Calculate(root, _ => true);

        foreach (var column in layout.Nodes.GroupBy(n => n.Depth))
        {
            var ordered = column.OrderBy(n => n.Y).ToList();
            for (var i = 1; i < ordered.Count; i++)
                Assert.That(ordered[i].Y, Is.GreaterThanOrEqualTo(ordered[i - 1].Y + ordered[i - 1].Height));
        }

        Assert.That(layout.Width, Is.GreaterThan(layout.Nodes.Max(n => n.Right)));
        Assert.That(layout.Height, Is.GreaterThan(layout.Nodes.Max(n => n.Y + n.Height)));
    }

    [Test]
    public void Layout_LongTitleIsTruncated()
    {
        var title = new string('x', 200);
        var width = MindmapLayout.EstimateWidth(title);

        Assert.That(width, Is.EqualTo(MindmapLayout.MaxNodeWidth));
        Assert.That(MindmapLayout.Truncate(title, width), Does.EndWith("…"));
        Assert.That(MindmapLayout.Truncate("Kurz", MindmapLayout.EstimateWidth("Kurz")), Is.EqualTo("Kurz"));

        foreach (var word in new[] { "Mitochondrien", "Photosynthese", "Zellatmung" })
            Assert.That(MindmapLayout.Truncate(word, MindmapLayout.EstimateWidth(word)), Is.EqualTo(word));
    }

    [Test]
    public void Layout_NullRoot_IsEmpty()
    {
        Assert.That(MindmapLayout.Calculate(null, _ => true).Nodes, Is.Empty);
    }

    // ---------- Attrappen ----------

    private sealed class FakeMindmapService : IMindmapService
    {
        public MindmapGenerationResult NextResult { get; set; } = new();
        public int GenerateCalls { get; private set; }
        public string? LastRootTitle { get; private set; }
        public Dictionary<string, MindmapDocument> Saved { get; } = new();

        public Task<MindmapGenerationResult> GenerateMindmapAsync(IEnumerable<string> contextChunks, string rootTitle, CancellationToken cancellationToken = default)
        {
            GenerateCalls++;
            LastRootTitle = rootTitle;
            return Task.FromResult(NextResult);
        }

        public Task<MindmapDocument?> LoadMindmapAsync(string profileName) =>
            Task.FromResult(Saved.TryGetValue(profileName, out var m) ? m : null);

        public Task SaveMindmapAsync(string profileName, MindmapDocument mindmap)
        {
            Saved[profileName] = mindmap;
            return Task.CompletedTask;
        }

        public Task DeleteMindmapAsync(string profileName)
        {
            Saved.Remove(profileName);
            return Task.CompletedTask;
        }

        public string ExportToMarkdown(MindmapDocument mindmap) => "# " + mindmap.Root.Title;
    }

    private sealed class FakeDocumentService : IDocumentService
    {
        public List<string> ExtractedPaths { get; } = new();

        public Task<int> GetChunkCountAsync(string filePath) => Task.FromResult(1);

        public Task<List<string>> ExtractChunksAsync(string filePath, int maxWordsPerChunk = 250)
        {
            ExtractedPaths.Add(filePath);
            return Task.FromResult(new List<string> { $"Inhalt von {Path.GetFileName(filePath)}" });
        }
    }
}
