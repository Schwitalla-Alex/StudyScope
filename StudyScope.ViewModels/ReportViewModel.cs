using System.ComponentModel;
using System.Runtime.CompilerServices;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.ViewModels.Mindmap;

namespace StudyScope.ViewModels;

/// <summary>Exportformate eines Berichts (PDF läuft über den Druckdialog der Oberfläche).</summary>
public enum ReportExportFormat
{
    Word,
    Markdown,
    Text,
    Html
}

/// <summary>
/// Steuert den Reiter „Berichte“: Bericht nach Vorlage aus den aktivierten Dokumenten erstellen,
/// mehrere Berichte je Profil verwalten, interaktive Ansicht mit eingebetteten Studio-Inhalten
/// (Mindmap, Karteikarten, Quizfragen, Präsentation) und Export als Word, Markdown, Text oder HTML.
/// </summary>
public class ReportViewModel : INotifyPropertyChanged
{
    /// <summary>Höchstzahl eingebetteter Karteikarten bzw. Quizfragen (sonst wird der Bericht unübersichtlich).</summary>
    public const int MaxEmbeddedFlashcards = 40;
    public const int MaxEmbeddedQuizQuestions = 10;

    private readonly IReportService _reportService;
    private readonly IDocumentService _documentService;
    private readonly IMindmapService _mindmapService;
    private readonly IFlashcardService _flashcardService;
    private readonly IQuizService _quizService;
    private readonly IPresentationService _presentationService;
    private CancellationTokenSource? _cts;
    private string _profileName = string.Empty;

    // Studio-Inhalte des Profils (bei jedem Laden aktualisiert)
    private MindmapDocument? _mindmap;
    private List<Flashcard> _flashcards = new();
    private QuizLibrary _quiz = new();
    private PresentationDocument? _presentation;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ReportViewModel(
        IReportService reportService,
        IDocumentService documentService,
        IMindmapService mindmapService,
        IFlashcardService flashcardService,
        IQuizService quizService,
        IPresentationService presentationService)
    {
        _reportService = reportService;
        _documentService = documentService;
        _mindmapService = mindmapService;
        _flashcardService = flashcardService;
        _quizService = quizService;
        _presentationService = presentationService;
    }

    // ---------------- Berichte ----------------

    private ReportLibrary _library = new();

    /// <summary>Alle Berichte des Profils, neueste zuerst.</summary>
    public IReadOnlyList<Report> Reports => _library.Reports.OrderByDescending(r => r.CreatedAt).ToList();

    private Report? _selectedReport;
    public Report? SelectedReport
    {
        get => _selectedReport;
        private set
        {
            _selectedReport = value;
            OnPropertyChanged();
            RebuildHtml();
        }
    }

    private string _currentHtml = string.Empty;
    /// <summary>Interaktive HTML-Ansicht des gewählten Berichts (wird in der Oberfläche eingebettet).</summary>
    public string CurrentHtml
    {
        get => _currentHtml;
        private set { _currentHtml = value; OnPropertyChanged(); }
    }

    // ---------------- Formular „Neuer Bericht“ ----------------

    private bool _isFormOpen;
    public bool IsFormOpen { get => _isFormOpen; set { _isFormOpen = value; OnPropertyChanged(); } }

    private ReportTemplate _template = ReportTemplate.StudyGuide;
    public ReportTemplate Template { get => _template; set { _template = value; OnPropertyChanged(); } }

    private string _customInstruction = string.Empty;
    public string CustomInstruction { get => _customInstruction; set { _customInstruction = value; OnPropertyChanged(); } }

    private string _customTitle = string.Empty;
    /// <summary>Optionaler eigener Titel (sonst wählt die KI einen).</summary>
    public string CustomTitle { get => _customTitle; set { _customTitle = value; OnPropertyChanged(); } }

    /// <summary>Einbettungen für den nächsten Bericht.</summary>
    public ReportEmbeds NewEmbeds { get; private set; } = new();

    public void ToggleNewEmbed(string part)
    {
        Toggle(NewEmbeds, part);
        OnPropertyChanged(nameof(NewEmbeds));
    }

    // ---------------- Verfügbare Studio-Inhalte ----------------

    public bool HasMindmap => _mindmap != null;
    public int FlashcardCount => _flashcards.Count;
    public int QuizQuestionCount => _quiz.Topics.Sum(t => t.Questions.Count);
    public int PresentationSlideCount => _presentation?.Slides.Count ?? 0;

    // ---------------- Fortschritt und Status ----------------

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set { _isBusy = value; OnPropertyChanged(); } }

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

    public void Cancel() => _cts?.Cancel();

    // ---------------- Laden ----------------

    public async Task LoadForProfileAsync(string profileName)
    {
        Cancel();
        _profileName = profileName;
        ClearStatus();

        _library = await _reportService.LoadLibraryAsync(profileName);
        await LoadStudioContentAsync(profileName);

        // Standard-Einbettungen: alles, was vorhanden ist
        NewEmbeds = new ReportEmbeds
        {
            Mindmap = HasMindmap,
            Flashcards = FlashcardCount > 0,
            Quiz = QuizQuestionCount > 0,
            Presentation = PresentationSlideCount > 0
        };

        IsFormOpen = _library.Reports.Count == 0;
        OnPropertyChanged(nameof(Reports));
        OnPropertyChanged(nameof(NewEmbeds));
        SelectedReport = Reports.FirstOrDefault();
    }

    private async Task LoadStudioContentAsync(string profileName)
    {
        // Jede Quelle einzeln absichern: ein defekter Teil soll den Bericht nicht verhindern
        _mindmap = await TryLoad(() => _mindmapService.LoadMindmapAsync(profileName), null);
        _flashcards = await TryLoad(() => _flashcardService.LoadFlashcardsAsync(profileName), new List<Flashcard>()) ?? new List<Flashcard>();
        _quiz = await TryLoad(() => _quizService.LoadLibraryAsync(profileName), new QuizLibrary()) ?? new QuizLibrary();
        _presentation = await TryLoad(() => _presentationService.LoadPresentationAsync(profileName), null);

        OnPropertyChanged(nameof(HasMindmap));
        OnPropertyChanged(nameof(FlashcardCount));
        OnPropertyChanged(nameof(QuizQuestionCount));
        OnPropertyChanged(nameof(PresentationSlideCount));
    }

    // T übernimmt die NULL-Zulässigkeit der Ladefunktion (z. B. MindmapDocument? oder List<Flashcard>),
    // sonst warnt der Compiler bei Diensten, die laut Signatur nie null liefern (CS8619).
    private static async Task<T> TryLoad<T>(Func<Task<T>> load, T fallback)
    {
        try { return await load(); }
        catch { return fallback; }
    }

    /// <summary>
    /// Studio-Inhalte für einen Bericht. Karteikarten und Quizfragen werden bevorzugt aus denselben
    /// Dokumenten genommen wie der Bericht; gibt es dazu nichts, werden alle des Profils verwendet.
    /// </summary>
    public ReportStudioContent GetStudioContent(Report report)
    {
        var sources = report.SourceDocuments.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var cards = _flashcards.Where(c => sources.Contains(c.SourceDocument)).ToList();
        if (cards.Count == 0) cards = _flashcards;

        var topics = _quiz.Topics.Where(t => t.Documents.Any(d => sources.Contains(d.FileName))).ToList();
        if (topics.Count == 0) topics = _quiz.Topics;

        // Abwechselnd aus den Themen, damit der Selbsttest den Stoff breit abdeckt
        var questions = new List<QuizQuestion>();
        var queues = topics.Select(t => new Queue<QuizQuestion>(t.Questions)).ToList();
        while (questions.Count < MaxEmbeddedQuizQuestions && queues.Any(q => q.Count > 0))
        {
            foreach (var queue in queues.Where(q => q.Count > 0))
            {
                if (questions.Count >= MaxEmbeddedQuizQuestions) break;
                questions.Add(queue.Dequeue());
            }
        }

        return new ReportStudioContent
        {
            Mindmap = _mindmap,
            Flashcards = cards.Take(MaxEmbeddedFlashcards).ToList(),
            QuizQuestions = questions,
            Presentation = _presentation
        };
    }

    // ---------------- Erstellen ----------------

    public async Task GenerateAsync(IEnumerable<DocumentItem> documents)
    {
        if (IsBusy) return;

        var selectedDocs = documents.Where(d => d.IsSelected && !d.IsMissing).ToList();
        if (selectedDocs.Count == 0)
        {
            ShowStatus("⚠️ Bitte wählen Sie links mindestens ein Dokument aus – daraus entsteht der Bericht.", isError: true);
            return;
        }

        if (Template == ReportTemplate.Custom && string.IsNullOrWhiteSpace(CustomInstruction))
        {
            ShowStatus("⚠️ Bitte beschreiben Sie bei „Eigene Vorgabe“, was der Bericht enthalten soll.", isError: true);
            return;
        }

        IsBusy = true;
        ClearStatus();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            var chunks = new List<string>();
            foreach (var doc in selectedDocs)
                chunks.AddRange(await _documentService.ExtractChunksAsync(doc.FilePath));

            var fallbackTitle = selectedDocs.Count == 1
                ? $"{ReportTemplates.DisplayName(Template)}: {Path.GetFileNameWithoutExtension(selectedDocs[0].FileName)}"
                : $"{ReportTemplates.DisplayName(Template)}: {_profileName}";

            var result = await _reportService.GenerateReportAsync(chunks, Template, CustomInstruction, fallbackTitle, token);
            if (token.IsCancellationRequested)
            {
                ShowStatus("Die Erstellung des Berichts wurde abgebrochen.", isError: false);
                return;
            }

            if (!result.IsSuccess)
            {
                var failure = MindmapStatusTexts.ForFailure(result.FailureReason, result.RawResponse, "Bericht", "✨ Bericht erstellen",
                    "kein Bericht", "einem Bericht", "einen Bericht");
                ShowStatus(failure.Message, isError: true, failure.Tips, failure.Details);
                return;
            }

            var report = result.Report!;
            if (!string.IsNullOrWhiteSpace(CustomTitle))
                report.Title = CustomTitle.Trim();
            report.CreatedAt = DateTime.Now;
            report.SourceDocuments = selectedDocs.Select(d => d.FileName).ToList();
            report.Embeds = new ReportEmbeds
            {
                Mindmap = NewEmbeds.Mindmap,
                Flashcards = NewEmbeds.Flashcards,
                Quiz = NewEmbeds.Quiz,
                Presentation = NewEmbeds.Presentation
            };

            _library.Reports.Add(report);
            await _reportService.SaveLibraryAsync(_profileName, _library);

            CustomTitle = string.Empty;
            IsFormOpen = false;
            OnPropertyChanged(nameof(Reports));
            SelectedReport = report;

            if (result.WasRepaired)
                ShowStatus("✓ Bericht erstellt. Die Antwort der KI war allerdings unvollständig oder fehlerhaft, daher fehlen eventuell einige Abschnitte.", isError: false,
                    new[] { "Erneut erstellen – die KI antwortet jedes Mal etwas anders.", "Nur bei sehr umfangreichen Unterlagen: weniger Dokumente aktivieren, damit die Antwort kürzer wird." }, isWarning: true);
            else
                ShowStatus($"✓ Bericht „{report.Title}“ mit {report.Sections.Count} Abschnitten erstellt.", isError: false);
        }
        catch (OperationCanceledException)
        {
            ShowStatus("Die Erstellung des Berichts wurde abgebrochen.", isError: false);
        }
        catch (Exception ex)
        {
            ShowStatus($"⚠️ Fehler beim Erstellen des Berichts: {ex.Message}", isError: true);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsBusy = false;
        }
    }

    // ---------------- Verwalten ----------------

    public void SelectReport(string reportId)
    {
        SelectedReport = _library.Reports.FirstOrDefault(r => r.Id == reportId);
        IsFormOpen = false;
    }

    public void OpenForm()
    {
        IsFormOpen = true;
        ClearStatus();
    }

    public async Task<bool> RenameReportAsync(string reportId, string newTitle)
    {
        var report = _library.Reports.FirstOrDefault(r => r.Id == reportId);
        var title = newTitle.Trim();
        if (report == null || title.Length == 0)
            return false;

        report.Title = title;
        await _reportService.SaveLibraryAsync(_profileName, _library);
        OnPropertyChanged(nameof(Reports));
        if (SelectedReport?.Id == reportId) RebuildHtml();
        return true;
    }

    public async Task DeleteReportAsync(string reportId)
    {
        var report = _library.Reports.FirstOrDefault(r => r.Id == reportId);
        if (report == null) return;

        _library.Reports.Remove(report);
        await _reportService.SaveLibraryAsync(_profileName, _library);
        OnPropertyChanged(nameof(Reports));

        if (SelectedReport?.Id == reportId)
            SelectedReport = Reports.FirstOrDefault();
        if (_library.Reports.Count == 0)
            IsFormOpen = true;

        ShowStatus($"✓ Bericht „{report.Title}“ gelöscht.", isError: false);
    }

    /// <summary>Schaltet eine Einbettung des gewählten Berichts um (mindmap, flashcards, quiz, presentation).</summary>
    public async Task ToggleEmbedAsync(string part)
    {
        if (SelectedReport == null) return;

        Toggle(SelectedReport.Embeds, part);
        await _reportService.SaveLibraryAsync(_profileName, _library);
        OnPropertyChanged(nameof(SelectedReport));
        RebuildHtml();
    }

    private static void Toggle(ReportEmbeds embeds, string part)
    {
        switch (part)
        {
            case "mindmap": embeds.Mindmap = !embeds.Mindmap; break;
            case "flashcards": embeds.Flashcards = !embeds.Flashcards; break;
            case "quiz": embeds.Quiz = !embeds.Quiz; break;
            case "presentation": embeds.Presentation = !embeds.Presentation; break;
        }
    }

    private void RebuildHtml() =>
        CurrentHtml = SelectedReport == null ? string.Empty : _reportService.ExportToHtml(SelectedReport, GetStudioContent(SelectedReport));

    // ---------------- Export ----------------

    public static string Extension(ReportExportFormat format) => format switch
    {
        ReportExportFormat.Word => "docx",
        ReportExportFormat.Markdown => "md",
        ReportExportFormat.Text => "txt",
        _ => "html"
    };

    public static string FormatDescription(ReportExportFormat format) => format switch
    {
        ReportExportFormat.Word => "Word-Dokument (*.docx)",
        ReportExportFormat.Markdown => "Markdown (*.md)",
        ReportExportFormat.Text => "Textdatei (*.txt)",
        _ => "Interaktive Webseite (*.html)"
    };

    public async Task<bool> ExportAsync(ReportExportFormat format, string filePath)
    {
        if (SelectedReport == null) return false;

        try
        {
            var studio = GetStudioContent(SelectedReport);
            switch (format)
            {
                case ReportExportFormat.Word:
                    await _reportService.ExportToDocxAsync(SelectedReport, studio, filePath);
                    break;
                case ReportExportFormat.Markdown:
                    await File.WriteAllTextAsync(filePath, _reportService.ExportToMarkdown(SelectedReport, studio));
                    break;
                case ReportExportFormat.Text:
                    await File.WriteAllTextAsync(filePath, _reportService.ExportToPlainText(SelectedReport, studio));
                    break;
                default:
                    await File.WriteAllTextAsync(filePath, _reportService.ExportToHtml(SelectedReport, studio));
                    break;
            }

            ShowStatus($"✓ Exportiert als {FormatDescription(format)}: {Path.GetFileName(filePath)}", isError: false);
            return true;
        }
        catch (Exception ex)
        {
            ShowStatus($"⚠️ Fehler beim Export: {ex.Message}", isError: true,
                new[] { "Ist die Datei noch in einem anderen Programm geöffnet? Dann bitte schließen und erneut exportieren." });
            return false;
        }
    }

    /// <summary>Dateiname aus dem Berichtstitel ohne unzulässige Zeichen.</summary>
    public string SuggestFileName(ReportExportFormat format)
    {
        var name = string.IsNullOrWhiteSpace(SelectedReport?.Title) ? "Bericht" : SelectedReport!.Title;
        foreach (var c in Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }))
            name = name.Replace(c, '_');
        name = name.Length > 60 ? name[..60] : name;
        return $"{name.Trim()}_{DateTime.Now:yyyyMMdd}.{Extension(format)}";
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
