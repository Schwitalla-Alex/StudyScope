using System.ComponentModel;
using System.Runtime.CompilerServices;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.ViewModels.Mindmap;

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

    public MindmapViewModel(IMindmapService mindmapService, IDocumentService documentService)
    {
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
        : $"Erstellt am {Mindmap.CreatedAt:dd.MM.yyyy HH:mm}" +
          (Mindmap.SourceDocuments.Count > 0 ? $" aus: {string.Join(", ", Mindmap.SourceDocuments)}" : string.Empty);

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

    public string ZoomText => $"{Zoom * 100:0} %";

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
            ShowStatus("⚠️ Bitte wählen Sie links mindestens ein Dokument aus, um eine Mindmap zu erstellen.", isError: true);
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
                ShowStatus("⚠️ Aus den gewählten Dokumenten konnte kein Text gelesen werden.", isError: true);
                return;
            }

            var rootTitle = selectedDocs.Count == 1
                ? Path.GetFileNameWithoutExtension(selectedDocs[0].FileName)
                : $"Unterlagen: {profileName}";

            var result = await _mindmapService.GenerateMindmapAsync(chunks, rootTitle, token);

            if (token.IsCancellationRequested)
            {
                ShowStatus("Die Erstellung der Mindmap wurde abgebrochen.", isError: false);
                return;
            }

            if (!result.IsSuccess)
            {
                ShowStatus("⚠️ Die KI hat keine gültige Mindmap geliefert. Bitte erneut versuchen oder ein anderes Modell wählen." +
                          FormatRawHint(result.RawResponse), isError: true);
                return;
            }

            var mindmap = new MindmapDocument
            {
                Root = result.Root!,
                CreatedAt = DateTime.Now,
                SourceDocuments = selectedDocs.Select(d => d.FileName).ToList()
            };

            await _mindmapService.SaveMindmapAsync(profileName, mindmap);
            SetMindmap(mindmap);

            var count = mindmap.Root.DescendantsAndSelf().Count();
            ShowStatus($"✓ Mindmap mit {count} Themen erstellt. Klicken Sie auf ein Thema für Details.", isError: false);
        }
        catch (OperationCanceledException)
        {
            ShowStatus("Die Erstellung der Mindmap wurde abgebrochen.", isError: false);
        }
        catch (Exception ex)
        {
            ShowStatus($"⚠️ Fehler beim Erstellen der Mindmap: {ex.Message}", isError: true);
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
        ShowStatus("✓ Die Mindmap dieses Profils wurde gelöscht.", isError: false);
    }

    public string GetMarkdownExport() => Mindmap == null ? string.Empty : _mindmapService.ExportToMarkdown(Mindmap);

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
    /// damit die Stichwortsuche im Chat die passenden Textstellen findet.
    /// </summary>
    public string BuildQuestionFor(MindmapNode node)
    {
        var path = GetPath(node);
        var parent = path.Count >= 2 ? path[^2] : null;

        return parent == null
            ? $"Gib mir einen Überblick über „{node.Title}“ anhand meiner Unterlagen."
            : $"Erkläre mir „{node.Title}“ im Zusammenhang mit „{parent.Title}“ anhand meiner Unterlagen.";
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

    public void ShowStatus(string message, bool isError)
    {
        IsError = isError;
        StatusMessage = message;
    }

    public void ClearStatus()
    {
        StatusMessage = null;
        IsError = false;
    }

    private static string FormatRawHint(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var text = raw.Trim().Replace('\n', ' ');
        return $" (Antwort der KI: „{(text.Length > 160 ? text[..160] + "…" : text)}“)";
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
