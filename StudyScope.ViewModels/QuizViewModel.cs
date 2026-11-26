using System.ComponentModel;
using System.Runtime.CompilerServices;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.ViewModels.Mindmap;
using StudyScope.ViewModels.Quiz;
using StudyScope.Core.Localization;
using StudyScope.ViewModels.Resources;

namespace StudyScope.ViewModels;

/// <summary>Welche Ansicht der Quiz-Reiter zeigt.</summary>
public enum QuizScreen
{
    Library,
    Running,
    Result
}

/// <summary>Themenvorschlag der KI mit Auswahlhäkchen.</summary>
public sealed class QuizSuggestionItem
{
    public QuizSuggestionItem(QuizTopicSuggestion suggestion) => Suggestion = suggestion;
    public QuizTopicSuggestion Suggestion { get; }
    public bool IsChecked { get; set; } = true;
}

/// <summary>
/// Steuert den Reiter „Quiz“: Themen anlegen/vorschlagen lassen, Quiz starten (Fragenpool wird bei Bedarf
/// paketweise von der KI ergänzt), Lern- oder Prüfungsmodus, Ergebnis, Verlauf und „Falsche wiederholen“.
/// </summary>
public class QuizViewModel : INotifyPropertyChanged
{
    public const int MinQuestions = 5;
    public const int MaxQuestions = 100;
    public const int MaxHistoryEntries = 100;

    /// <summary>Höchstzahl erfolgloser KI-Aufrufe in Folge je Thema, bevor aufgegeben wird.</summary>
    private const int MaxEmptyBatches = 2;

    private readonly IQuizService _quizService;
    private readonly IDocumentService _documentService;
    private readonly Random _random;
    private readonly HashSet<string> _selectedTopicIds = new();
    private readonly HashSet<QuizQuestionType> _allowedTypes = new() { QuizQuestionType.SingleChoice, QuizQuestionType.TrueFalse, QuizQuestionType.MultipleChoice };
    private readonly HashSet<string> _formDocumentPaths = new(StringComparer.OrdinalIgnoreCase);
    private List<QuizDocumentRef> _suggestionDocuments = new();
    private CancellationTokenSource? _cts;

    public event PropertyChangedEventHandler? PropertyChanged;

    public QuizViewModel(IQuizService quizService, IDocumentService documentService, Random? random = null)
    {
        _quizService = quizService;
        _documentService = documentService;
        _random = random ?? Random.Shared;
    }

    // ---------------- Grundzustand ----------------

    private QuizLibrary _library = new();
    public QuizLibrary Library
    {
        get => _library;
        private set { _library = value; OnPropertyChanged(); OnPropertyChanged(nameof(Topics)); OnPropertyChanged(nameof(History)); }
    }

    public IReadOnlyList<QuizTopic> Topics => Library.Topics;
    public IReadOnlyList<QuizResult> History => Library.History;

    private QuizScreen _screen = QuizScreen.Library;
    public QuizScreen Screen
    {
        get => _screen;
        private set { _screen = value; OnPropertyChanged(); }
    }

    private QuizSession? _session;
    public QuizSession? Session
    {
        get => _session;
        private set { _session = value; OnPropertyChanged(); }
    }

    private QuizResult? _lastResult;
    public QuizResult? LastResult
    {
        get => _lastResult;
        private set { _lastResult = value; OnPropertyChanged(); OnPropertyChanged(nameof(WrongCount)); }
    }

    public int WrongCount => LastResult?.Entries.Count(e => !e.IsCorrect) ?? 0;

    // ---------------- Einstellungen für den Start ----------------

    private int _questionCount = 10;
    public int QuestionCount
    {
        get => _questionCount;
        set
        {
            var clamped = Math.Clamp(value, MinQuestions, MaxQuestions);
            if (_questionCount == clamped) return;
            _questionCount = clamped;
            OnPropertyChanged();
        }
    }

    private QuizMode _mode = QuizMode.Learn;
    public QuizMode Mode
    {
        get => _mode;
        set { _mode = value; OnPropertyChanged(); }
    }

    public bool IsTypeAllowed(QuizQuestionType type) => _allowedTypes.Contains(type);

    public void ToggleType(QuizQuestionType type)
    {
        if (!_allowedTypes.Remove(type))
            _allowedTypes.Add(type);
        OnPropertyChanged(nameof(AllowedTypes));
    }

    public IReadOnlyCollection<QuizQuestionType> AllowedTypes => _allowedTypes;

    public bool IsTopicSelected(string topicId) => _selectedTopicIds.Contains(topicId);

    public void ToggleTopic(string topicId)
    {
        if (!_selectedTopicIds.Remove(topicId))
            _selectedTopicIds.Add(topicId);
        OnPropertyChanged(nameof(SelectedTopicCount));
    }

    public void SelectAllTopics(bool select)
    {
        _selectedTopicIds.Clear();
        if (select)
            foreach (var t in Topics) _selectedTopicIds.Add(t.Id);
        OnPropertyChanged(nameof(SelectedTopicCount));
    }

    public int SelectedTopicCount => Topics.Count(t => _selectedTopicIds.Contains(t.Id));

    /// <summary>Fragen im Pool eines Themas, die zu den gewählten Fragetypen passen.</summary>
    public int AvailableQuestions(QuizTopic topic) => topic.Questions.Count(q => _allowedTypes.Contains(q.Type));

    /// <summary>Anteil richtiger Antworten eines Themas über den ganzen Verlauf (null = noch nie gespielt).</summary>
    public (int Answered, double Percent)? TopicStatistics(string topicId)
    {
        var entries = History.SelectMany(r => r.Entries).Where(e => e.TopicId == topicId).ToList();
        if (entries.Count == 0) return null;
        return (entries.Count, Math.Round(entries.Count(e => e.IsCorrect) * 100.0 / entries.Count, 0));
    }

    // ---------------- Fortschritt und Status ----------------

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set { _isBusy = value; OnPropertyChanged(); } }

    private string? _progressText;
    public string? ProgressText { get => _progressText; private set { _progressText = value; OnPropertyChanged(); } }

    private double _progressPercent;
    public double ProgressPercent { get => _progressPercent; private set { _progressPercent = value; OnPropertyChanged(); } }

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

    private string _profileName = string.Empty;

    public async Task LoadForProfileAsync(string profileName)
    {
        Cancel();
        _profileName = profileName;
        ClearStatus();
        CloseTopicForm();
        Suggestions = Array.Empty<QuizSuggestionItem>();
        _selectedTopicIds.Clear();
        Session = null;
        LastResult = null;
        Screen = QuizScreen.Library;
        Library = await _quizService.LoadLibraryAsync(profileName);
        OnPropertyChanged(nameof(SelectedTopicCount));
    }

    private Task SaveAsync() => _quizService.SaveLibraryAsync(_profileName, Library);

    // ---------------- Themen selbst anlegen / bearbeiten ----------------

    private bool _isTopicFormOpen;
    public bool IsTopicFormOpen { get => _isTopicFormOpen; private set { _isTopicFormOpen = value; OnPropertyChanged(); } }

    private string? _editingTopicId;
    public bool IsEditingTopic => _editingTopicId != null;

    private string _formName = string.Empty;
    public string FormName { get => _formName; set { _formName = value; OnPropertyChanged(); } }

    private string _formDescription = string.Empty;
    public string FormDescription { get => _formDescription; set { _formDescription = value; OnPropertyChanged(); } }

    private string? _formError;
    public string? FormError { get => _formError; private set { _formError = value; OnPropertyChanged(); } }

    public bool IsFormDocumentSelected(string filePath) => _formDocumentPaths.Contains(filePath);

    public void ToggleFormDocument(string filePath)
    {
        if (!_formDocumentPaths.Remove(filePath))
            _formDocumentPaths.Add(filePath);
        OnPropertyChanged("FormDocuments");
    }

    /// <summary>Öffnet das Formular für ein neues Thema; die links aktivierten Dokumente sind vorausgewählt.</summary>
    public void OpenNewTopicForm(IEnumerable<DocumentItem> documents)
    {
        _editingTopicId = null;
        FormName = string.Empty;
        FormDescription = string.Empty;
        FormError = null;
        _formDocumentPaths.Clear();
        foreach (var doc in documents.Where(d => d.IsSelected && !d.IsMissing))
            _formDocumentPaths.Add(doc.FilePath);
        IsTopicFormOpen = true;
        OnPropertyChanged(nameof(IsEditingTopic));
    }

    public void OpenEditTopicForm(string topicId)
    {
        var topic = Topics.FirstOrDefault(t => t.Id == topicId);
        if (topic == null) return;

        _editingTopicId = topic.Id;
        FormName = topic.Name;
        FormDescription = topic.Description;
        FormError = null;
        _formDocumentPaths.Clear();
        foreach (var doc in topic.Documents)
            _formDocumentPaths.Add(doc.FilePath);
        IsTopicFormOpen = true;
        OnPropertyChanged(nameof(IsEditingTopic));
    }

    public void CloseTopicForm()
    {
        IsTopicFormOpen = false;
        _editingTopicId = null;
        FormError = null;
        OnPropertyChanged(nameof(IsEditingTopic));
    }

    /// <param name="documents">Alle Dokumente des Profils (für Dateinamen zu den gewählten Pfaden).</param>
    public async Task<bool> SaveTopicFormAsync(IEnumerable<DocumentItem> documents)
    {
        var name = FormName.Trim();
        if (name.Length == 0)
        {
            FormError = VmStrings.Quiz_FormNameMissing;
            return false;
        }

        if (Topics.Any(t => t.Id != _editingTopicId && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            FormError = Loc.Format(VmStrings.Quiz_FormNameExists, ("name", name));
            return false;
        }

        var docs = documents.Where(d => _formDocumentPaths.Contains(d.FilePath))
            .Select(d => new QuizDocumentRef { FileName = d.FileName, FilePath = d.FilePath })
            .ToList();

        // Beim Bearbeiten: Dokumente, die nicht mehr in der Liste stehen, aber gewählt bleiben, behalten
        var existing = Topics.FirstOrDefault(t => t.Id == _editingTopicId);
        if (existing != null)
        {
            docs.AddRange(existing.Documents.Where(d => _formDocumentPaths.Contains(d.FilePath) && docs.All(x => x.FilePath != d.FilePath)));
        }

        if (docs.Count == 0)
        {
            FormError = VmStrings.Quiz_FormNoDocuments;
            return false;
        }

        if (existing != null)
        {
            existing.Name = name;
            existing.Description = FormDescription.Trim();
            existing.Documents = docs;
        }
        else
        {
            var topic = new QuizTopic { Name = name, Description = FormDescription.Trim(), Documents = docs, Origin = QuizTopicOrigin.Manual };
            Library.Topics.Add(topic);
            _selectedTopicIds.Add(topic.Id);
        }

        await SaveAsync();
        CloseTopicForm();
        NotifyTopicsChanged();
        ShowStatus(Loc.Format(existing != null ? VmStrings.Quiz_TopicSaved : VmStrings.Quiz_TopicCreated, ("name", name)), isError: false);
        return true;
    }

    public async Task DeleteTopicAsync(string topicId)
    {
        var topic = Topics.FirstOrDefault(t => t.Id == topicId);
        if (topic == null) return;

        Library.Topics.Remove(topic);
        _selectedTopicIds.Remove(topicId);
        await SaveAsync();
        NotifyTopicsChanged();
        ShowStatus(Loc.Format(VmStrings.Quiz_TopicDeleted, ("name", topic.Name)), isError: false);
    }

    /// <summary>Verwirft den Fragenpool eines Themas – beim nächsten Quiz erzeugt die KI neue Fragen.</summary>
    public async Task ResetTopicQuestionsAsync(string topicId)
    {
        var topic = Topics.FirstOrDefault(t => t.Id == topicId);
        if (topic == null) return;

        topic.Questions.Clear();
        await SaveAsync();
        NotifyTopicsChanged();
        ShowStatus(Loc.Format(VmStrings.Quiz_QuestionsReset, ("name", topic.Name)), isError: false);
    }

    // ---------------- Themen per KI vorschlagen ----------------

    private IReadOnlyList<QuizSuggestionItem> _suggestions = Array.Empty<QuizSuggestionItem>();
    public IReadOnlyList<QuizSuggestionItem> Suggestions
    {
        get => _suggestions;
        private set { _suggestions = value; OnPropertyChanged(); }
    }

    public async Task SuggestTopicsAsync(IEnumerable<DocumentItem> documents)
    {
        if (IsBusy) return;

        var selectedDocs = documents.Where(d => d.IsSelected && !d.IsMissing).ToList();
        if (selectedDocs.Count == 0)
        {
            ShowStatus(VmStrings.Quiz_SuggestNoDocuments, isError: true);
            return;
        }

        IsBusy = true;
        ClearStatus();
        ProgressText = VmStrings.Quiz_SuggestProgress;
        ProgressPercent = 0;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            var chunks = await LoadChunksAsync(selectedDocs.Select(d => new QuizDocumentRef { FileName = d.FileName, FilePath = d.FilePath }));
            var result = await _quizService.SuggestTopicsAsync(chunks, token);
            if (token.IsCancellationRequested)
            {
                ShowStatus(VmStrings.Quiz_SuggestCancelled, isError: false);
                return;
            }

            if (!result.IsSuccess)
            {
                var failure = MindmapStatusTexts.ForFailure(result.FailureReason, result.RawResponse, GenerationSubject.TopicList, VmStrings.Quiz_SuggestTopics);
                ShowStatus(failure.Message, isError: true, failure.Tips, failure.Details);
                return;
            }

            _suggestionDocuments = selectedDocs.Select(d => new QuizDocumentRef { FileName = d.FileName, FilePath = d.FilePath }).ToList();
            var known = Topics.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Suggestions = result.Topics
                .Where(t => !known.Contains(t.Name))
                .Select(t => new QuizSuggestionItem(t))
                .ToList();

            if (Suggestions.Count == 0)
                ShowStatus(VmStrings.Quiz_SuggestOnlyKnown, isError: false);
        }
        catch (OperationCanceledException)
        {
            ShowStatus(VmStrings.Quiz_SuggestCancelled, isError: false);
        }
        catch (Exception ex)
        {
            ShowStatus(Loc.Format(VmStrings.Quiz_SuggestError, ("error", ex.Message)), isError: true);
        }
        finally
        {
            EndBusy();
        }
    }

    public void ToggleSuggestion(QuizSuggestionItem item)
    {
        item.IsChecked = !item.IsChecked;
        OnPropertyChanged(nameof(Suggestions));
    }

    public void DismissSuggestions() => Suggestions = Array.Empty<QuizSuggestionItem>();

    /// <summary>Übernimmt die angehakten Vorschläge als Themen (mit den Dokumenten, aus denen sie stammen).</summary>
    public async Task AcceptSuggestionsAsync()
    {
        var chosen = Suggestions.Where(s => s.IsChecked).ToList();
        if (chosen.Count == 0)
        {
            DismissSuggestions();
            return;
        }

        foreach (var item in chosen)
        {
            var topic = new QuizTopic
            {
                Name = item.Suggestion.Name,
                Description = item.Suggestion.Description,
                Origin = QuizTopicOrigin.Ai,
                Documents = _suggestionDocuments.Select(d => new QuizDocumentRef { FileName = d.FileName, FilePath = d.FilePath }).ToList()
            };
            Library.Topics.Add(topic);
            _selectedTopicIds.Add(topic.Id);
        }

        await SaveAsync();
        DismissSuggestions();
        NotifyTopicsChanged();
        ShowStatus(Loc.Format(VmStrings.Quiz_SuggestionsAccepted, ("count", chosen.Count)), isError: false);
    }

    // ---------------- Quiz starten ----------------

    public async Task StartQuizAsync()
    {
        if (IsBusy) return;

        var topics = Topics.Where(t => _selectedTopicIds.Contains(t.Id)).ToList();
        if (topics.Count == 0)
        {
            ShowStatus(VmStrings.Quiz_NoTopicSelected, isError: true,
                new[] { Loc.Format(VmStrings.Quiz_NoTopicSelected_Tip, ("newTopic", VmStrings.Quiz_NewTopic), ("suggest", VmStrings.Quiz_SuggestTopics)) });
            return;
        }

        if (_allowedTypes.Count == 0)
        {
            ShowStatus(VmStrings.Quiz_NoTypeSelected, isError: true);
            return;
        }

        IsBusy = true;
        ClearStatus();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var types = _allowedTypes.ToList();
        QuizQuestionGenerationResult? lastFailure = null;
        var missingDocuments = new List<string>();
        var wasRepaired = false;

        try
        {
            var quotas = QuizComposer.DistributeEvenly(topics, QuestionCount);
            var totalMissing = topics.Sum(t => Math.Max(0, quotas[t.Id] - AvailableQuestions(t)));
            var generated = 0;

            foreach (var topic in topics)
            {
                var needed = quotas[topic.Id] - AvailableQuestions(topic);
                if (needed <= 0)
                    continue;

                var chunks = await LoadChunksAsync(topic.Documents, missingDocuments);
                if (chunks.Count == 0)
                {
                    lastFailure = new QuizQuestionGenerationResult { FailureReason = MindmapFailureReason.NoContent };
                    continue;
                }

                var batch = 0;
                var emptyInARow = 0;
                while (needed > 0 && emptyInARow < MaxEmptyBatches)
                {
                    token.ThrowIfCancellationRequested();
                    ProgressText = Loc.Format(VmStrings.Quiz_GenerateProgress, ("topic", topic.Name), ("done", generated), ("total", totalMissing));
                    ProgressPercent = totalMissing == 0 ? 0 : 100.0 * generated / totalMissing;

                    var request = Math.Min(needed, _quizService.BatchSize);
                    var result = await _quizService.GenerateQuestionsAsync(topic, chunks, request, types, batch++, token);
                    token.ThrowIfCancellationRequested();

                    var fresh = result.Questions.Where(q => types.Contains(q.Type)).ToList();
                    if (fresh.Count == 0)
                    {
                        emptyInARow++;
                        if (!result.IsSuccess) lastFailure = result;
                        continue;
                    }

                    emptyInARow = 0;
                    wasRepaired |= result.WasRepaired;
                    topic.Questions.AddRange(fresh);
                    needed -= fresh.Count;
                    generated += fresh.Count;

                    // Nach jedem Paket speichern: bereits erzeugte Fragen bleiben auch bei Abbruch erhalten
                    await SaveAsync();
                }
            }

            var composed = QuizComposer.Compose(topics, QuestionCount, types, History, _random);
            if (composed.Count == 0)
            {
                var failure = lastFailure != null
                    ? MindmapStatusTexts.ForFailure(lastFailure.FailureReason, lastFailure.RawResponse, GenerationSubject.Quiz, VmStrings.Quiz_Start)
                    : new MindmapStatusText(VmStrings.Quiz_NoQuestions, new[] { VmStrings.Quiz_NoQuestions_Tip }, null);
                var tips = failure.Tips.Concat(MissingDocumentTips(missingDocuments)).ToList();
                ShowStatus(failure.Message, isError: true, tips, failure.Details);
                return;
            }

            Session = new QuizSession(composed, Mode, _random);
            Screen = QuizScreen.Running;
            NotifyTopicsChanged();

            var hints = MissingDocumentTips(missingDocuments).ToList();
            if (composed.Count < QuestionCount)
            {
                ShowStatus(Loc.Format(VmStrings.Quiz_Partial, ("count", composed.Count), ("requested", QuestionCount)), isError: false,
                    hints.Concat(new[] { VmStrings.Quiz_Partial_Tip }).ToList(), isWarning: true);
            }
            else if (hints.Count > 0 || wasRepaired)
            {
                ShowStatus(VmStrings.Quiz_RunningNote, isError: false,
                    hints.Concat(wasRepaired ? new[] { VmStrings.Quiz_RepairedNote } : Array.Empty<string>()).ToList(),
                    isWarning: true);
            }
        }
        catch (OperationCanceledException)
        {
            ShowStatus(VmStrings.Quiz_GenerateCancelled, isError: false);
            NotifyTopicsChanged();
        }
        catch (Exception ex)
        {
            ShowStatus(Loc.Format(VmStrings.Quiz_StartError, ("error", ex.Message)), isError: true);
        }
        finally
        {
            EndBusy();
        }
    }

    private static IEnumerable<string> MissingDocumentTips(List<string> missing) =>
        missing.Count == 0
            ? Enumerable.Empty<string>()
            : new[] { Loc.Format(VmStrings.Quiz_MissingDocuments, ("files", string.Join(", ", missing.Distinct()))) };

    private async Task<List<string>> LoadChunksAsync(IEnumerable<QuizDocumentRef> documents, List<string>? missing = null)
    {
        var chunks = new List<string>();
        foreach (var doc in documents)
        {
            if (!File.Exists(doc.FilePath))
            {
                missing?.Add(doc.FileName);
                continue;
            }

            chunks.AddRange(await _documentService.ExtractChunksAsync(doc.FilePath));
        }
        return chunks;
    }

    // ---------------- Während des Quiz ----------------

    public void SelectOption(int index)
    {
        Session?.ToggleOption(index);
        OnPropertyChanged(nameof(Session));
    }

    public void CheckAnswer()
    {
        Session?.Check();
        OnPropertyChanged(nameof(Session));
    }

    public void NextQuestion()
    {
        Session?.Next();
        OnPropertyChanged(nameof(Session));
    }

    public void PreviousQuestion()
    {
        Session?.Previous();
        OnPropertyChanged(nameof(Session));
    }

    public void GoToQuestion(int index)
    {
        Session?.GoTo(index);
        OnPropertyChanged(nameof(Session));
    }

    /// <summary>Beendet das Quiz, speichert das Ergebnis im Verlauf und zeigt die Auswertung.</summary>
    public async Task FinishQuizAsync()
    {
        if (Session == null || Session.IsFinished) return;

        var topicNames = Session.Items.Select(i => i.TopicName).Distinct().ToList();
        var result = Session.Finish(topicNames);

        Library.History.Insert(0, result);
        if (Library.History.Count > MaxHistoryEntries)
            Library.History.RemoveRange(MaxHistoryEntries, Library.History.Count - MaxHistoryEntries);

        await SaveAsync();
        LastResult = result;
        ClearStatus();
        Screen = QuizScreen.Result;
        OnPropertyChanged(nameof(History));
        NotifyTopicsChanged();
    }

    /// <summary>Bricht das laufende Quiz ohne Eintrag im Verlauf ab.</summary>
    public void AbortQuiz()
    {
        Session = null;
        Screen = QuizScreen.Library;
        ClearStatus();
    }

    /// <summary>Startet ein neues Quiz nur mit den zuletzt falsch beantworteten Fragen.</summary>
    public void RepeatWrongQuestions()
    {
        if (LastResult == null) return;

        var wrongIds = LastResult.Entries.Where(e => !e.IsCorrect).Select(e => e.QuestionId).ToHashSet();
        var items = Topics
            .SelectMany(t => t.Questions.Where(q => wrongIds.Contains(q.Id)).Select(q => (q, t)))
            .OrderBy(_ => _random.Next())
            .ToList();

        if (items.Count == 0)
        {
            ShowStatus(VmStrings.Quiz_NoWrongQuestions, isError: false);
            return;
        }

        Session = new QuizSession(items, LastResult.Mode, _random);
        Screen = QuizScreen.Running;
        ClearStatus();
    }

    public void BackToLibrary()
    {
        Session = null;
        Screen = QuizScreen.Library;
    }

    public async Task ClearHistoryAsync()
    {
        Library.History.Clear();
        await SaveAsync();
        OnPropertyChanged(nameof(History));
        NotifyTopicsChanged();
        ShowStatus(VmStrings.Quiz_HistoryCleared, isError: false);
    }

    /// <summary>Kurze Bewertung des Ergebnisses.</summary>
    public static string Verdict(double percent) => percent switch
    {
        >= 90 => VmStrings.Quiz_Verdict_Excellent,
        >= 75 => VmStrings.Quiz_Verdict_VeryGood,
        >= 50 => VmStrings.Quiz_Verdict_Good,
        _ => VmStrings.Quiz_Verdict_KeepGoing
    };

    // ---------------- intern ----------------

    private void EndBusy()
    {
        _cts?.Dispose();
        _cts = null;
        IsBusy = false;
        ProgressText = null;
        ProgressPercent = 0;
    }

    private void NotifyTopicsChanged()
    {
        OnPropertyChanged(nameof(Topics));
        OnPropertyChanged(nameof(SelectedTopicCount));
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
