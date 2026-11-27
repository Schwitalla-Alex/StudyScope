using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;

namespace StudyScope.ViewModels;

public enum FlashcardStatusFilter
{
    All,
    DifficultOnly,
    NewOnly,
    KnownOnly
}

/// <summary>Wie neu erzeugte Karteikarten übernommen werden.</summary>
public enum FlashcardGenerationMode
{
    /// <summary>Zum vorhandenen Stapel hinzufügen (Standard); doppelte Fragen werden weggelassen.</summary>
    Append,

    /// <summary>Vorhandenen Stapel samt Lernstand ersetzen.</summary>
    Replace
}

/// <summary>Ergebnis einer Karten-Erzeugung.</summary>
/// <param name="Received">Verwertbare Karten in der KI-Antwort.</param>
/// <param name="Added">Tatsächlich übernommene Karten.</param>
/// <param name="SkippedDuplicates">Weggelassene Wiederholungen (schon im Stapel oder mehrfach in der Antwort).</param>
/// <param name="Mode">Gewählter Modus.</param>
public sealed record FlashcardGenerationResult(int Received, int Added, int SkippedDuplicates, FlashcardGenerationMode Mode)
{
    public static FlashcardGenerationResult Nothing(FlashcardGenerationMode mode) => new(0, 0, 0, mode);
}

/// <summary>
///     Karteikarten-Systems:
///     FlashcardViewModel & Dependency Injection.Das ViewModel steuert die Zustände für die spätere Blazor-Oberfläche:Extrahieren der Textauszüge aus den aktuell aktivierten PDFs via IPdfService 
///     Aufruf von IFlashcardService.GenerateFlashcardsAsync(...) 
///     Speichern & Laden der Decks je Thema / Profil 
///     Index-Verwaltung für den Lernmodus(nächste Karte, vorherige Karte, Karte aufdecken)  
///     Bewertungs-Logik: „Gewusst“ / „Nicht gewusst“   
/// </summary>
public class FlashcardViewModel : INotifyPropertyChanged
{
    private readonly IFlashcardService _flashcardService;
    private readonly IDocumentService _pdfService;
    private readonly IStorageService _storageService;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<Flashcard> Flashcards { get; } = new();

    private FlashcardStatusFilter _selectedFilter = FlashcardStatusFilter.All;
    public FlashcardStatusFilter SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (_selectedFilter != value)
            {
                _selectedFilter = value;
                CurrentIndex = 0;
                IsAnswerRevealed = false;
                OnPropertyChanged();
                NotifyFilterChanged();
            }
        }
    }

    public List<Flashcard> FilteredFlashcards => SelectedFilter switch
    {
        FlashcardStatusFilter.DifficultOnly => Flashcards.Where(c => c.Status == FlashcardStatus.Difficult).ToList(),
        FlashcardStatusFilter.NewOnly => Flashcards.Where(c => c.Status == FlashcardStatus.New).ToList(),
        FlashcardStatusFilter.KnownOnly => Flashcards.Where(c => c.Status == FlashcardStatus.Known).ToList(),
        _ => Flashcards.ToList()
    };

    private int _currentIndex = 0;
    public int CurrentIndex
    {
        get => _currentIndex;
        set
        {
            if (_currentIndex != value)
            {
                _currentIndex = value;
                OnPropertyChanged();
                NotifyFilterChanged();
            }
        }
    }

    private bool _isAnswerRevealed = false;
    public bool IsAnswerRevealed
    {
        get => _isAnswerRevealed;
        set
        {
            _isAnswerRevealed = value;
            OnPropertyChanged();
        }
    }

    private bool _isGenerating = false;
    public bool IsGenerating
    {
        get => _isGenerating;
        set
        {
            _isGenerating = value;
            OnPropertyChanged();
        }
    }

    public Flashcard? CurrentCard
    {
        get
        {
            var list = FilteredFlashcards;
            return (list.Any() && CurrentIndex >= 0 && CurrentIndex < list.Count)
                ? list[CurrentIndex]
                : null;
        }
    }

    private string _currentAiModelDisplayName = "Fake-KI (Testmodus)";
    public string CurrentAiModelDisplayName
    {
        get => _currentAiModelDisplayName;
        private set
        {
            if (_currentAiModelDisplayName != value)
            {
                _currentAiModelDisplayName = value;
                OnPropertyChanged();
            }
        }
    }

    public bool HasPreviousCard => CurrentIndex > 0;
    public bool HasNextCard => CurrentIndex < FilteredFlashcards.Count - 1;
    public string ProgressText => FilteredFlashcards.Any() ? $"{CurrentIndex + 1} / {FilteredFlashcards.Count}" : "0 / 0";

    // Zähler für die Filter-Buttons
    public int TotalCount => Flashcards.Count;
    public int DifficultCount => Flashcards.Count(c => c.Status == FlashcardStatus.Difficult);
    public int NewCount => Flashcards.Count(c => c.Status == FlashcardStatus.New);
    public int KnownCount => Flashcards.Count(c => c.Status == FlashcardStatus.Known);

    /// <summary>Einstellung „Sprache der KI-Ergebnisse“ des Profils (fehlt in Tests → wie Oberfläche).</summary>
    public IOutputLanguageProvider? OutputLanguageProvider { get; set; }

    private async Task<string?> OutputLanguageSettingAsync(string profileName) =>
        OutputLanguageProvider == null ? null : await OutputLanguageProvider.GetSettingAsync(profileName);

    /// <summary>Häufigste Sprache der Karten (gleich oft oder ohne Angabe: null).</summary>
    public static string? DominantLanguage(IEnumerable<string?> languages)
    {
        var ranked = languages.Where(l => !string.IsNullOrWhiteSpace(l))
            .GroupBy(l => l!, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Language: g.Key, Count: g.Count()))
            .OrderByDescending(g => g.Count)
            .ToList();
        if (ranked.Count == 0 || (ranked.Count > 1 && ranked[0].Count == ranked[1].Count)) return null;
        return ranked[0].Language;
    }

    public FlashcardViewModel(
        IFlashcardService flashcardService,
        IDocumentService pdfService,
        IStorageService storageService,
        IOutputLanguageProvider? outputLanguage = null)
    {
        OutputLanguageProvider = outputLanguage;
        _flashcardService = flashcardService;
        _pdfService = pdfService;
        _storageService = storageService;
    }

    //public async Task SwitchProfileAsync(string newProfile)
    //{
    //    if (string.IsNullOrWhiteSpace(newProfile) || newProfile == CurrentProfile)
    //        return;

    //    CurrentProfile = newProfile;
    //    await _storageService.SetLastActiveProfileNameAsync(newProfile);

    //    // 1. Dokumente laden
    //    await LoadDocumentsAsync();

    //    // 2. Profilbezogenes KI-Modell laden und Anzeigenamen aktualisieren
    //    await RefreshCurrentAiModelAsync();
    //}

    //public async Task RefreshCurrentAiModelAsync()
    //{
    //    var aiConfig = await _storageService.LoadAiConfigForProfileAsync(CurrentProfile);

    //    CurrentAiModelDisplayName = !string.IsNullOrWhiteSpace(aiConfig.SelectedModelName)
    //        ? aiConfig.SelectedModelName
    //        : (!string.IsNullOrWhiteSpace(aiConfig.ModelName) ? aiConfig.ModelName : "Nicht konfiguriert");

    //    OnPropertyChanged(nameof(CurrentAiModelDisplayName));
    //}

    //public async Task<bool> SaveAndConnectAsync()
    //{
    //    IsSaving = true;
    //    ConnectionStatusMessage = "Speichern & Prüfen...";

    //    try
    //    {
    //        var config = new AiConfig
    //        {
    //            ApiKey = ApiKey.Trim(),
    //            SelectedProvider = SelectedProvider,
    //            LocalEndpointUrl = LocalEndpointUrl.Trim(),
    //            SelectedModelName = SelectedModelName,
    //            ModelName = SelectedModelName
    //        };

    //        // 1. Aktives Profil ermitteln
    //        var activeProfile = await _storageService.GetLastActiveProfileNameAsync();

    //        // 2. Profilspezifisch speichern (und als globale Fallback-Config)
    //        await _storageService.SaveAiConfigForProfileAsync(activeProfile, config);
    //        await _storageService.SaveAiConfigAsync(config);

    //        // 3. Verbindung testen
    //        var isConnected = await _llmService.ValidateConnectionAsync();
    //        if (!isConnected)
    //        {
    //            ConnectionSuccess = false;
    //            ConnectionStatusMessage = "Gespeichert, aber Verbindung zum Provider konnte nicht verifiziert werden.";
    //            return false;
    //        }

    //        ConnectionSuccess = true;
    //        ConnectionStatusMessage = "Erfolgreich verbunden und im Profil gespeichert!";
    //        return true;
    //    }
    //    catch (Exception ex)
    //    {
    //        ConnectionSuccess = false;
    //        ConnectionStatusMessage = $"Verbindungsfehler: {ex.Message}";
    //        return false;
    //    }
    //    finally
    //    {
    //        IsSaving = false;
    //    }
    //}

    public async Task LoadCardsForProfileAsync(string profileName)
    {
        Flashcards.Clear();
        var cards = await _flashcardService.LoadFlashcardsAsync(profileName);
        foreach (var card in cards)
        {
            Flashcards.Add(card);
        }

        CurrentIndex = 0;
        IsAnswerRevealed = false;
        NotifyFilterChanged();
    }

    private string CleanJsonOutput(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
            return string.Empty;

        var cleaned = rawResponse.Trim();

        // Markdown-Codeblöcke entfernen (```json ... ``` oder ``` ... ```)
        if (cleaned.StartsWith("```"))
        {
            var firstNewline = cleaned.IndexOf('\n');
            if (firstNewline != -1)
            {
                cleaned = cleaned.Substring(firstNewline + 1);
            }

            if (cleaned.EndsWith("```"))
            {
                cleaned = cleaned.Substring(0, cleaned.LastIndexOf("```")).Trim();
            }
        }

        // Zusätzliche Einleitungs- oder Schlusstexte vor der ersten '[' und nach der letzten ']' abschneiden
        var firstBracket = cleaned.IndexOf('[');
        var lastBracket = cleaned.LastIndexOf(']');

        if (firstBracket != -1 && lastBracket != -1 && lastBracket > firstBracket)
        {
            cleaned = cleaned.Substring(firstBracket, lastBracket - firstBracket + 1);
        }

        return cleaned;
    }

    /// <summary>
    /// Erzeugt neue Karten aus den aktivierten Dokumenten. Standard: zum vorhandenen Stapel hinzufügen,
    /// wobei Fragen, die es schon gibt (ohne Groß-/Kleinschreibung, Leerzeichen und Satzzeichen am Ende), weggelassen werden.
    /// <see cref="FlashcardGenerationMode.Replace"/> ersetzt den Stapel samt Lernstand (die Oberfläche warnt vorher).
    /// Liefert die KI keine verwertbaren Karten oder nur Wiederholungen, bleibt der vorhandene Stapel unverändert.
    /// Fehler des KI-Dienstes werden weitergereicht.
    /// </summary>
    public async Task<FlashcardGenerationResult> GenerateCardsFromDocumentsAsync(
        string profileName,
        IEnumerable<DocumentItem> documents,
        int cardCount = 5,
        FlashcardGenerationMode mode = FlashcardGenerationMode.Append)
    {
        var selectedDocs = documents.Where(d => d.IsSelected && !d.IsMissing).ToList();
        if (!selectedDocs.Any())
            return FlashcardGenerationResult.Nothing(mode);

        IsGenerating = true;
        try
        {
            var allChunks = new List<string>();
            foreach (var doc in selectedDocs)
            {
                var chunks = await _pdfService.ExtractChunksAsync(doc.FilePath);
                allChunks.AddRange(chunks);
            }

            // Hinzufügen: Sprache des vorhandenen Stapels beibehalten (keine gemischten Stapel aus Versehen)
            var deckLanguage = mode == FlashcardGenerationMode.Append ? DominantLanguage(Flashcards.Select(c => c.Language)) : null;
            var setting = deckLanguage ?? await OutputLanguageSettingAsync(profileName);
            var generatedCards = await _flashcardService.GenerateFlashcardsAsync(allChunks, cardCount, setting);

            // Keine verwertbare Antwort: vorhandene Karten (samt Lernstand) nicht durch einen leeren Stapel ersetzen
            if (generatedCards == null || generatedCards.Count == 0)
                return FlashcardGenerationResult.Nothing(mode);

            // Beim Ersetzen zählen nur Wiederholungen innerhalb der neuen Karten, beim Hinzufügen auch der vorhandene Stapel
            var known = new HashSet<string>(mode == FlashcardGenerationMode.Append
                ? Flashcards.Select(c => NormalizeQuestion(c.Question))
                : Enumerable.Empty<string>());
            var newCards = generatedCards.Where(c => known.Add(NormalizeQuestion(c.Question))).ToList();
            var skipped = generatedCards.Count - newCards.Count;

            if (newCards.Count == 0)
                return new FlashcardGenerationResult(generatedCards.Count, 0, skipped, mode);

            var firstNewIndex = 0;
            if (mode == FlashcardGenerationMode.Replace)
                Flashcards.Clear();
            else
                firstNewIndex = Flashcards.Count;

            foreach (var card in newCards)
            {
                Flashcards.Add(card);
            }

            await _flashcardService.SaveFlashcardsAsync(profileName, Flashcards);

            // In der Gesamtansicht direkt zur ersten neuen Karte springen
            CurrentIndex = SelectedFilter == FlashcardStatusFilter.All ? firstNewIndex : 0;
            IsAnswerRevealed = false;
            NotifyFilterChanged();
            return new FlashcardGenerationResult(generatedCards.Count, newCards.Count, skipped, mode);
        }
        finally
        {
            IsGenerating = false;
        }
    }

    /// <summary>Vergleichsform einer Frage: ohne Groß-/Kleinschreibung, mehrfache Leerzeichen und Satzzeichen am Ende.</summary>
    public static string NormalizeQuestion(string? question)
    {
        var collapsed = string.Join(' ', (question ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.TrimEnd('?', '!', '.', ':', ';', ' ').ToLowerInvariant();
    }

    public void FlipCard() => IsAnswerRevealed = !IsAnswerRevealed;

    public void NextCard()
    {
        if (HasNextCard)
        {
            CurrentIndex++;
            IsAnswerRevealed = false;
        }
    }

    public void PreviousCard()
    {
        if (HasPreviousCard)
        {
            CurrentIndex--;
            IsAnswerRevealed = false;
        }
    }

    public async Task RateCurrentCardAsync(string profileName, FlashcardStatus status)
    {
        var card = CurrentCard;
        if (card == null)
            return;

        // 1. Status der Karte aktualisieren und persistent speichern
        card.Status = status;
        await _flashcardService.SaveFlashcardsAsync(profileName, Flashcards);

        // 2. Zur nächsten Karte im aktuellen Filter weitergehen
        var list = FilteredFlashcards;
        if (SelectedFilter == FlashcardStatusFilter.All)
        {
            // Wenn alle Karten angezeigt werden: einfach weiterblättern
            if (HasNextCard)
            {
                NextCard();
            }
        }
        else
        {
            // Wenn ein Status-Filter aktiv ist, verschwindet die Karte möglicherweise aus der Ansicht:
            // Liegt der aktuelle Index noch im gültigen Bereich, bleibt er auf der nachgerückten Karte;
            // falls er am Ende der Liste stand, wird er auf das neue Listenende gesetzt.
            if (CurrentIndex >= list.Count && list.Count > 0)
            {
                CurrentIndex = list.Count - 1;
            }
        }

        IsAnswerRevealed = false;
        NotifyFilterChanged();
    }

    public async Task ClearFlashcardsAsync(string profileName)
    {
        Flashcards.Clear();
        FilteredFlashcards.Clear();

        // Persistierung: Leere Liste in der JSON-Datei speichern
        await _flashcardService.SaveFlashcardsAsync(profileName, Flashcards);

        // UI-Zähler und Bindings aktualisieren
        NotifyFilterChanged();
        OnPropertyChanged(nameof(CurrentCard));
    }

    private void NotifyFilterChanged()
    {
        OnPropertyChanged(nameof(FilteredFlashcards));
        OnPropertyChanged(nameof(CurrentCard));
        OnPropertyChanged(nameof(HasPreviousCard));
        OnPropertyChanged(nameof(HasNextCard));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(DifficultCount));
        OnPropertyChanged(nameof(NewCount));
        OnPropertyChanged(nameof(KnownCount));
    }

    public string GetMarkdownExport(string profileName)
    {
        return _flashcardService.ExportToMarkdown(profileName, FilteredFlashcards, ExportCulture.Current);
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}