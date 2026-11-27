using System.ComponentModel;
using System.Runtime.CompilerServices;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.ViewModels.Mindmap;
using StudyScope.Core.Localization;
using StudyScope.ViewModels.Resources;

namespace StudyScope.ViewModels;

/// <summary>
/// Steuert die Mindmap-Ansicht: Erzeugung aus den aktivierten Dokumenten, Laden/Speichern je Profil,
/// Auf- und Zuklappen von Ästen, Zoom, Auswahl eines Knotens und Formulierung einer Chat-Frage dazu.
/// </summary>
public class MindmapViewModel : INotifyPropertyChanged
{
    public const double MinZoom = 0.4;
    public const double MaxZoom = 2.0;
    private const double ZoomStep = 0.2;

    private readonly IMindmapService _mindmapService;
    private readonly IDocumentService _documentService;

    private readonly HashSet<string> _expandedIds = new();
    private Dictionary<string, MindmapNode> _nodesById = new();
    private Dictionary<string, MindmapNode> _parentById = new();
    private CancellationTokenSource? _generationCts;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Einstellung „Sprache der KI-Ergebnisse“ des Profils (fehlt in Tests → wie Oberfläche).</summary>
    public IOutputLanguageProvider? OutputLanguageProvider { get; set; }

    private async Task<string?> OutputLanguageSettingAsync(string profileName) =>
        OutputLanguageProvider == null ? null : await OutputLanguageProvider.GetSettingAsync(profileName);

    public MindmapViewModel(IMindmapService mindmapService, IDocumentService documentService, IOutputLanguageProvider? outputLanguage = null)
    {
        OutputLanguageProvider = outputLanguage;
        _mindmapService = mindmapService;
        _documentService = documentService;
    }

    private MindmapDocument? _mindmap;
    public MindmapDocument? Mindmap
    {
        get => _mindmap;
        private set
        {
            _mindmap = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasMindmap));
            OnPropertyChanged(nameof(SourceInfoText));
        }
    }

    public bool HasMindmap => Mindmap != null;

    public string SourceInfoText => Mindmap == null
        ? string.Empty
        : Mindmap.SourceDocuments.Count > 0
            ? Loc.Format(VmStrings.Mindmap_SourceInfoWithSources, ("date", Mindmap.CreatedAt.ToString("g")), ("sources", string.Join(", ", Mindmap.SourceDocuments)))
            : Loc.Format(VmStrings.Mindmap_SourceInfo, ("date", Mindmap.CreatedAt.ToString("g")));

    private MindmapLayoutResult _layout = MindmapLayoutResult.Empty;
    public MindmapLayoutResult Layout
    {
        get => _layout;
        private set { _layout = value; OnPropertyChanged(); }
    }

    private bool _isGenerating;
    public bool IsGenerating
    {
        get => _isGenerating;
        private set { _isGenerating = value; OnPropertyChanged(); }
    }

    private string? _statusMessage;
    public string? StatusMessage
    {
        get => _statusMessage;
        private set { _statusMessage = value; OnPropertyChanged(); }
    }

    private bool _isError;
    public bool IsError
    {
        get => _isError;
        private set { _isError = value; OnPropertyChanged(); }
    }

    /// <summary>Erfolg mit Einschränkung (z. B. unvollständige KI-Antwort) – wird gelb statt grün angezeigt.</summary>
    private bool _isWarning;
    public bool IsWarning
    {
        get => _isWarning;
        private set { _isWarning = value; OnPropertyChanged(); }
    }

    /// <summary>Konkrete Tipps zur Statusmeldung (Aufzählung unter der Meldung).</summary>
    private IReadOnlyList<string> _statusTips = Array.Empty<string>();
    public IReadOnlyList<string> StatusTips
    {
        get => _statusTips;
        private set { _statusTips = value; OnPropertyChanged(); }
    }

    /// <summary>Technische Details (rohe KI-Antwort), in der Oberfläche einklappbar.</summary>
    private string? _statusDetails;
    public string? StatusDetails
    {
        get => _statusDetails;
        private set { _statusDetails = value; OnPropertyChanged(); }
    }

    private double _zoom = 1.0;
    public double Zoom
    {
        get => _zoom;
        set
        {
            var clamped = Math.Round(Math.Clamp(value, MinZoom, MaxZoom), 2);
            if (Math.Abs(_zoom - clamped) < 0.001)
                return;
            _zoom = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ZoomText));
        }
    }

    public string ZoomText => Loc.Format(VmStrings.Mindmap_Zoom, ("percent", Math.Round(Zoom * 100)));

    private MindmapNode? _selectedNode;
    public MindmapNode? SelectedNode
    {
        get => _selectedNode;
        private set
        {
            _selectedNode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedPath));
        }
    }

    /// <summary>Pfad von der Wurzel bis zum ausgewählten Knoten (für die Brotkrümel-Anzeige).</summary>
    public IReadOnlyList<MindmapNode> SelectedPath => SelectedNode == null ? Array.Empty<MindmapNode>() : GetPath(SelectedNode);

    public bool IsExpanded(MindmapNode node) => _expandedIds.Contains(node.Id);

    // ---------------- Laden / Erzeugen ----------------

    public async Task LoadForProfileAsync(string profileName)
    {
        // Profilwechsel während einer Erzeugung: diese abbrechen, damit keine fremde Mindmap angezeigt wird
        CancelGeneration();

        ClearStatus();
        SetMindmap(await _mindmapService.LoadMindmapAsync(profileName));
    }

    public async Task GenerateAsync(string profileName, IEnumerable<DocumentItem> documents)
    {
        if (IsGenerating)
            return;

        var selectedDocs = documents.Where(d => d.IsSelected && !d.IsMissing).ToList();
        if (selectedDocs.Count == 0)
        {
            ShowStatus(VmStrings.Mindmap_NoDocuments, isError: true);
            return;
        }

        IsGenerating = true;
        ClearStatus();
        _generationCts = new CancellationTokenSource();
        var token = _generationCts.Token;

        try
        {
            var chunks = new List<string>();
            foreach (var doc in selectedDocs)
                chunks.AddRange(await _documentService.ExtractChunksAsync(doc.FilePath));

            if (chunks.Count == 0)
            {
                var noText = MindmapStatusTexts.ForFailure(new MindmapGenerationResult { FailureReason = MindmapFailureReason.NoContent });
                ShowStatus(noText.Message, isError: true, noText.Tips);
                return;
            }

            var outputLanguage = await OutputLanguageSettingAsync(profileName);
            var rootTitle = FallbackTitles.Documents(FallbackTitles.Language(outputLanguage, chunks), selectedDocs, profileName);

            var result = await _mindmapService.GenerateMindmapAsync(chunks, rootTitle, outputLanguage, token);

            if (token.IsCancellationRequested)
            {
                ShowStatus(VmStrings.Mindmap_Cancelled, isError: false);
                return;
            }

            if (!result.IsSuccess)
            {
                var failure = MindmapStatusTexts.ForFailure(result);
                ShowStatus(failure.Message, isError: true, failure.Tips, failure.Details);
                return;
            }

            var mindmap = new MindmapDocument
            {
                Root = result.Root!,
                Language = result.Language,
                CreatedAt = DateTime.Now,
                SourceDocuments = selectedDocs.Select(d => d.FileName).ToList()
            };

            await _mindmapService.SaveMindmapAsync(profileName, mindmap);
            SetMindmap(mindmap);

            var success = MindmapStatusTexts.ForSuccess(mindmap.Root.DescendantsAndSelf().Count(), result.WasRepaired);
            ShowStatus(success.Message, isError: false, success.Tips, isWarning: result.WasRepaired);
        }
        catch (OperationCanceledException)
        {
            ShowStatus(VmStrings.Mindmap_Cancelled, isError: false);
        }
        catch (Exception ex)
        {
            ShowStatus(Loc.Format(VmStrings.Mindmap_Error, ("error", ex.Message)), isError: true);
        }
        finally
        {
            _generationCts?.Dispose();
            _generationCts = null;
            IsGenerating = false;
        }
    }

    public void CancelGeneration() => _generationCts?.Cancel();

    public async Task DeleteAsync(string profileName)
    {
        await _mindmapService.DeleteMindmapAsync(profileName);
        SetMindmap(null);
        ShowStatus(VmStrings.Mindmap_Deleted, isError: false);
    }

    public string GetMarkdownExport() => Mindmap == null ? string.Empty : _mindmapService.ExportToMarkdown(Mindmap, ExportCulture.For(Mindmap.Language));

    // ---------------- Interaktion ----------------

    public void ToggleNode(string nodeId)
    {
        if (!_nodesById.TryGetValue(nodeId, out var node) || node.Children.Count == 0)
            return;

        if (!_expandedIds.Remove(nodeId))
            _expandedIds.Add(nodeId);

        RebuildLayout();
    }

    public void ExpandAll()
    {
        foreach (var node in _nodesById.Values.Where(n => n.Children.Count > 0))
            _expandedIds.Add(node.Id);
        RebuildLayout();
    }

    /// <summary>Zeigt nur Wurzel und Hauptäste (Ausgangszustand).</summary>
    public void CollapseAll()
    {
        _expandedIds.Clear();
        if (Mindmap != null)
            _expandedIds.Add(Mindmap.Root.Id);
        RebuildLayout();
    }

    public void SelectNode(string? nodeId)
    {
        SelectedNode = nodeId != null && _nodesById.TryGetValue(nodeId, out var node) ? node : null;
    }

    public void ZoomIn() => Zoom += ZoomStep;
    public void ZoomOut() => Zoom -= ZoomStep;
    public void ResetZoom() => Zoom = 1.0;

    /// <summary>
    /// Formuliert eine Frage zum Knoten für den Chat. Der übergeordnete Ast wird mitgenannt,
    /// damit die Stichwortsuche im Chat die passenden Textstellen findet. Die Frage folgt der Oberflächensprache
    /// (sie ist eine Benutzerfrage); die Stichwortsuche kennt deutsche und englische Füllwörter.
    /// </summary>
    public string BuildQuestionFor(MindmapNode node)
    {
        var path = GetPath(node);
        var parent = path.Count >= 2 ? path[^2] : null;

        return parent == null
            ? Loc.Format(VmStrings.Mindmap_QuestionOverview, ("topic", node.Title))
            : Loc.Format(VmStrings.Mindmap_QuestionInContext, ("topic", node.Title), ("parent", parent.Title));
    }

    // ---------------- intern ----------------

    private void SetMindmap(MindmapDocument? mindmap)
    {
        _nodesById = new Dictionary<string, MindmapNode>();
        _parentById = new Dictionary<string, MindmapNode>();
        _expandedIds.Clear();

        if (mindmap != null)
        {
            foreach (var node in mindmap.Root.DescendantsAndSelf())
            {
                _nodesById[node.Id] = node;
                foreach (var child in node.Children)
                    _parentById[child.Id] = node;
            }

            // Ausgangszustand wie bei NotebookLM: Wurzel mit Hauptästen
            _expandedIds.Add(mindmap.Root.Id);
        }

        Mindmap = mindmap;
        SelectedNode = null;
        RebuildLayout();
    }

    private void RebuildLayout() => Layout = MindmapLayout.Calculate(Mindmap?.Root, IsExpanded);

    private IReadOnlyList<MindmapNode> GetPath(MindmapNode node)
    {
        var path = new List<MindmapNode> { node };
        var current = node;
        while (_parentById.TryGetValue(current.Id, out var parent))
        {
            path.Insert(0, parent);
            current = parent;
        }

        return path;
    }

    public void ShowStatus(string message, bool isError, IReadOnlyList<string>? tips = null, string? details = null, bool isWarning = false)
    {
        IsError = isError;
        IsWarning = !isError && isWarning;
        StatusTips = tips ?? Array.Empty<string>();
        StatusDetails = string.IsNullOrWhiteSpace(details) ? null : details;
        StatusMessage = message;
    }

    public void ClearStatus()
    {
        StatusMessage = null;
        StatusTips = Array.Empty<string>();
        StatusDetails = null;
        IsError = false;
        IsWarning = false;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
