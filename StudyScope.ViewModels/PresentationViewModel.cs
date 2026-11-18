using System.ComponentModel;
using System.Runtime.CompilerServices;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.ViewModels.Mindmap;

namespace StudyScope.ViewModels;

/// <summary>
/// Steuert den Reiter „Präsentation“: Erstellen (per KI aus Dokumenten oder aus der Mindmap),
/// Blättern, Vortragsmodus, PowerPoint-Export und Speichern je Profil.
/// </summary>
public class PresentationViewModel : INotifyPropertyChanged
{
    public const int DefaultSlideCount = 8;

    private readonly IPresentationService _presentationService;
    private readonly IMindmapService _mindmapService;
    private readonly IDocumentService _documentService;
    private CancellationTokenSource? _generationCts;

    public event PropertyChangedEventHandler? PropertyChanged;

    public PresentationViewModel(IPresentationService presentationService, IMindmapService mindmapService, IDocumentService documentService)
    {
        _presentationService = presentationService;
        _mindmapService = mindmapService;
        _documentService = documentService;
    }

    // ---------------- Zustand ----------------

    private PresentationDocument? _presentation;
    public PresentationDocument? Presentation
    {
        get => _presentation;
        private set
        {
            _presentation = value;
            _currentIndex = 0;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasPresentation));
            OnPropertyChanged(nameof(Slides));
            OnPropertyChanged(nameof(SourceInfoText));
            NotifyNavigation();
        }
    }

    public bool HasPresentation => Presentation != null && Presentation.Slides.Count > 0;

    public IReadOnlyList<PresentationSlide> Slides => Presentation?.Slides ?? (IReadOnlyList<PresentationSlide>)Array.Empty<PresentationSlide>();

    public string SourceInfoText
    {
        get
        {
            if (Presentation == null)
                return string.Empty;

            var origin = Presentation.Origin == PresentationOrigin.Mindmap ? "aus der Mindmap" : "von der KI";
            var sources = Presentation.SourceDocuments.Count > 0 ? $" – Quellen: {string.Join(", ", Presentation.SourceDocuments)}" : string.Empty;
            return $"{Slides.Count} Folien, erstellt am {Presentation.CreatedAt:dd.MM.yyyy HH:mm} {origin}{sources}";
        }
    }

    private int _currentIndex;
    public int CurrentIndex
    {
        get => _currentIndex;
        private set
        {
            var clamped = Slides.Count == 0 ? 0 : Math.Clamp(value, 0, Slides.Count - 1);
            if (_currentIndex == clamped)
                return;
            _currentIndex = clamped;
            NotifyNavigation();
        }
    }

    public PresentationSlide? CurrentSlide => Slides.Count == 0 ? null : Slides[CurrentIndex];
    public bool HasPrevious => CurrentIndex > 0;
    public bool HasNext => CurrentIndex < Slides.Count - 1;
    public string ProgressText => Slides.Count == 0 ? "0 / 0" : $"{CurrentIndex + 1} / {Slides.Count}";

    private bool _isPresenting;
    /// <summary>Vortragsmodus (Folie bildschirmfüllend, Steuerung per Tastatur).</summary>
    public bool IsPresenting
    {
        get => _isPresenting;
        private set { _isPresenting = value; OnPropertyChanged(); }
    }

    private int _slideCount = DefaultSlideCount;
    /// <summary>Gewünschte Anzahl Inhaltsfolien bei der Erstellung durch die KI.</summary>
    public int SlideCount
    {
        get => _slideCount;
        set
        {
            var clamped = Math.Clamp(value, 3, 20);
            if (_slideCount == clamped)
                return;
            _slideCount = clamped;
            OnPropertyChanged();
        }
    }

    private bool _isGenerating;
    public bool IsGenerating
    {
        get => _isGenerating;
        private set { _isGenerating = value; OnPropertyChanged(); }
    }

    // ---------------- Statusmeldung (wie bei der Mindmap) ----------------

    private string? _statusMessage;
    public string? StatusMessage { get => _statusMessage; private set { _statusMessage = value; OnPropertyChanged(); } }

    private bool _isError;
    public bool IsError { get => _isError; private set { _isError = value; OnPropertyChanged(); } }

    private bool _isWarning;
    public bool IsWarning { get => _isWarning; private set { _isWarning = value; OnPropertyChanged(); } }

    private IReadOnlyList<string> _statusTips = Array.Empty<string>();
    public IReadOnlyList<string> StatusTips { get => _statusTips; private set { _statusTips = value; OnPropertyChanged(); } }

    private string? _statusDetails;
    public string? StatusDetails { get => _statusDetails; private set { _statusDetails = value; OnPropertyChanged(); } }

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

    // ---------------- Laden / Erstellen ----------------

    public async Task LoadForProfileAsync(string profileName)
    {
        // Profilwechsel während einer Erzeugung: diese abbrechen, damit keine fremde Präsentation erscheint
        CancelGeneration();
        StopPresenting();
        ClearStatus();
        Presentation = await _presentationService.LoadPresentationAsync(profileName);
    }

    public async Task GenerateFromDocumentsAsync(string profileName, IEnumerable<DocumentItem> documents)
    {
        if (IsGenerating)
            return;

        var selectedDocs = documents.Where(d => d.IsSelected && !d.IsMissing).ToList();
        if (selectedDocs.Count == 0)
        {
            ShowStatus("⚠️ Bitte wählen Sie links mindestens ein Dokument aus, um eine Präsentation zu erstellen.", isError: true);
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

            var fallbackTitle = selectedDocs.Count == 1
                ? Path.GetFileNameWithoutExtension(selectedDocs[0].FileName)
                : $"Unterlagen: {profileName}";

            var result = await _presentationService.GenerateFromDocumentsAsync(chunks, fallbackTitle, SlideCount, token);

            if (token.IsCancellationRequested)
            {
                ShowStatus("Die Erstellung der Präsentation wurde abgebrochen.", isError: false);
                return;
            }

            if (!result.IsSuccess)
            {
                var failure = MindmapStatusTexts.ForFailure(result.FailureReason, result.RawResponse, "Präsentation", "Mit KI erstellen");
                ShowStatus(failure.Message, isError: true, failure.Tips, failure.Details);
                return;
            }

            var presentation = result.Presentation!;
            presentation.CreatedAt = DateTime.Now;
            presentation.SourceDocuments = selectedDocs.Select(d => d.FileName).ToList();

            await _presentationService.SavePresentationAsync(profileName, presentation);
            Presentation = presentation;

            if (result.WasRepaired)
            {
                ShowStatus($"✓ Präsentation mit {presentation.Slides.Count} Folien erstellt. Die Antwort der KI war allerdings unvollständig oder fehlerhaft, daher fehlen eventuell einige Folien.",
                    isError: false, new[] { "Für eine vollständige Präsentation erneut erstellen oder weniger Folien bzw. Dokumente wählen." }, isWarning: true);
            }
            else
            {
                ShowStatus($"✓ Präsentation mit {presentation.Slides.Count} Folien erstellt. Mit „▶ Vortragen“ starten Sie den Vortragsmodus.", isError: false);
            }
        }
        catch (OperationCanceledException)
        {
            ShowStatus("Die Erstellung der Präsentation wurde abgebrochen.", isError: false);
        }
        catch (Exception ex)
        {
            ShowStatus($"⚠️ Fehler beim Erstellen der Präsentation: {ex.Message}", isError: true);
        }
        finally
        {
            _generationCts?.Dispose();
            _generationCts = null;
            IsGenerating = false;
        }
    }

    /// <summary>Leitet die Präsentation aus der gespeicherten Mindmap des Profils ab (ohne KI-Aufruf).</summary>
    public async Task CreateFromMindmapAsync(string profileName)
    {
        if (IsGenerating)
            return;

        ClearStatus();
        var mindmap = await _mindmapService.LoadMindmapAsync(profileName);
        if (mindmap == null)
        {
            ShowStatus("⚠️ Für dieses Profil gibt es noch keine Mindmap.", isError: true,
                new[] { "Erstellen Sie zuerst im Reiter „🧠 Mindmap“ eine Mindmap.", "Oder lassen Sie die Präsentation mit „✨ Mit KI erstellen“ direkt aus den Dokumenten erzeugen." });
            return;
        }

        var presentation = _presentationService.CreateFromMindmap(mindmap);
        presentation.CreatedAt = DateTime.Now;
        await _presentationService.SavePresentationAsync(profileName, presentation);
        Presentation = presentation;
        ShowStatus($"✓ Präsentation mit {presentation.Slides.Count} Folien aus der Mindmap erstellt.", isError: false);
    }

    public void CancelGeneration() => _generationCts?.Cancel();

    public async Task DeleteAsync(string profileName)
    {
        StopPresenting();
        await _presentationService.DeletePresentationAsync(profileName);
        Presentation = null;
        ShowStatus("✓ Die Präsentation dieses Profils wurde gelöscht.", isError: false);
    }

    public async Task<bool> ExportToPptxAsync(string filePath)
    {
        if (Presentation == null)
            return false;

        try
        {
            await _presentationService.ExportToPptxAsync(Presentation, filePath);
            ShowStatus($"✓ Erfolgreich als PowerPoint exportiert: {Path.GetFileName(filePath)}", isError: false);
            return true;
        }
        catch (Exception ex)
        {
            ShowStatus($"⚠️ Fehler beim PowerPoint-Export: {ex.Message}", isError: true,
                new[] { "Ist die Datei noch in PowerPoint geöffnet? Dann bitte schließen und erneut exportieren." });
            return false;
        }
    }

    /// <summary>Dateiname ohne unzulässige Zeichen, z. B. für den Speichern-Dialog.</summary>
    public string SuggestFileName(string extension)
    {
        var name = string.IsNullOrWhiteSpace(Presentation?.Title) ? "Präsentation" : Presentation!.Title;
        foreach (var c in Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }))
            name = name.Replace(c, '_');
        name = name.Length > 60 ? name[..60] : name;
        return $"{name.Trim()}_{DateTime.Now:yyyyMMdd}.{extension}";
    }

    // ---------------- Blättern und Vortragen ----------------

    public void Next() => CurrentIndex++;
    public void Previous() => CurrentIndex--;
    public void First() => CurrentIndex = 0;
    public void Last() => CurrentIndex = Slides.Count - 1;
    public void GoTo(int index) => CurrentIndex = index;

    public void StartPresenting(bool fromBeginning)
    {
        if (!HasPresentation)
            return;
        if (fromBeginning)
            CurrentIndex = 0;
        IsPresenting = true;
    }

    public void StopPresenting()
    {
        if (IsPresenting)
            IsPresenting = false;
    }

    /// <summary>
    /// Tastatursteuerung (wie in PowerPoint): → / Leertaste / Bild ↓ = weiter, ← / Bild ↑ = zurück,
    /// Pos1/Ende = erste/letzte Folie, Esc = Vortrag beenden. Rückgabe: Taste wurde verarbeitet.
    /// </summary>
    public bool HandleKey(string key)
    {
        switch (key)
        {
            case "ArrowRight":
            case "ArrowDown":
            case "PageDown":
            case " ":
            case "Enter":
                Next();
                return true;
            case "ArrowLeft":
            case "ArrowUp":
            case "PageUp":
            case "Backspace":
                Previous();
                return true;
            case "Home":
                First();
                return true;
            case "End":
                Last();
                return true;
            case "Escape":
                StopPresenting();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Dichte einer Folie anhand der Textmenge – die Anzeige verkleinert dann die Schrift (siehe <see cref="SlideLayout"/>).</summary>
    public static SlideDensity GetDensity(PresentationSlide slide) => SlideLayout.GetDensity(slide);

    private void NotifyNavigation()
    {
        OnPropertyChanged(nameof(CurrentIndex));
        OnPropertyChanged(nameof(CurrentSlide));
        OnPropertyChanged(nameof(HasPrevious));
        OnPropertyChanged(nameof(HasNext));
        OnPropertyChanged(nameof(ProgressText));
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
