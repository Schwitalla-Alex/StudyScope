using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.ViewModels.Resources;
using StudyScope.Core.Localization;
using StudyScope.ViewModels.Audio;
using StudyScope.ViewModels.Mindmap;

namespace StudyScope.ViewModels;

/// <summary>Woher die Folien eines neuen Videos kommen.</summary>
public enum VideoSlideSource
{
    /// <summary>Die vorhandene Präsentation des Profils (Kopie im Video).</summary>
    ProfilePresentation,

    /// <summary>Die KI erzeugt neue Folien nur für dieses Video; die Präsentation des Profils bleibt unverändert.</summary>
    NewPresentation
}

/// <summary>Passen die Quelldokumente der Präsentation zu den angehakten Dokumenten?</summary>
public enum PresentationSourceMatch
{
    /// <summary>Keine Präsentation vorhanden.</summary>
    None,

    /// <summary>Ältere Präsentation ohne gespeicherte Quellen (neutraler Hinweis).</summary>
    Unknown,

    /// <summary>Quellen entsprechen genau den angehakten Dokumenten.</summary>
    Match,

    /// <summary>Quellen weichen von den angehakten Dokumenten ab (deutliche Warnung).</summary>
    Mismatch
}

/// <summary>Ergebnis des Vergleichs samt Anzeigetext.</summary>
public sealed record PresentationSourceInfo(PresentationSourceMatch Match, IReadOnlyList<string> Sources, IReadOnlyList<string> Selected, string Message);

/// <summary>Exportformate der Sprechtexte.</summary>
public enum VideoScriptExportFormat
{
    Text,
    Markdown
}

/// <summary>
/// Steuert den Reiter „Video“: vertontes Erklärvideo aus einer Präsentation. Sprechtexte je Folie per KI (oder aus den
/// Sprechernotizen), mehrere Videos je Profil, Bearbeiten der Sprechtexte, Hinweis bei geänderter Präsentation samt
/// „Folien aktualisieren“, Sprachausgabe wie im Audio-Reiter (Systemstimme live, Gemini TTS als WAV je Szene, nur
/// geänderte Szenen werden neu erzeugt) und Wiedergabe wie ein Video: Zeitsteuerung, Sprünge, Stichpunkte nacheinander.
/// Die Zeitsteuerung läuft ohne Oberfläche (über <see cref="TimeProvider"/> und <see cref="Tick"/>), damit sie testbar ist.
/// </summary>
public partial class VideoViewModel : INotifyPropertyChanged
{
    public const int DefaultSlideCount = 6;

    /// <summary>Die Sprechtexte spricht immer die „Sprecherin“ (eine Stimme).</summary>
    public static readonly IReadOnlyList<string> SpeakerRoles = new[] { AudioScriptFormats.NarratorName };

    private readonly IVideoScriptService _videoService;
    private readonly IPresentationService _presentationService;
    private readonly IDocumentService _documentService;
    private readonly SpeechProviderState _speech;
    private readonly IAudioFilePlayer? _filePlayer;
    private readonly PlaybackCoordinator? _coordinator;
    private readonly TimeProvider _time;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _audioCts;
    private string _profileName = string.Empty;
    private VideoLibrary _library = new();
    private bool _isLoaded;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <param name="timeProvider">Uhr für die Zeitsteuerung der Systemstimme (Tests: eigene Uhr).</param>
    /// <param name="delay">Warten bei Szenen ohne Sprechtext (Tests: sofort).</param>
    /// <summary>Einstellung „Sprache der KI-Ergebnisse“ des Profils (fehlt in Tests → wie Oberfläche).</summary>
    public IOutputLanguageProvider? OutputLanguageProvider { get; set; }

    private async Task<string?> OutputLanguageSettingAsync(string profileName) =>
        OutputLanguageProvider == null ? null : await OutputLanguageProvider.GetSettingAsync(profileName);

    public VideoViewModel(IVideoScriptService videoService, IPresentationService presentationService, IDocumentService documentService,
        IStorageService storage, IEnumerable<ITextToSpeechService> speechServices, IAudioFilePlayer? filePlayer = null,
        PlaybackCoordinator? coordinator = null, TimeProvider? timeProvider = null, Func<TimeSpan, CancellationToken, Task>? delay = null,
        IAudioTranscoder? transcoder = null, IVideoMp4Exporter? mp4 = null, IOutputLanguageProvider? outputLanguage = null)
    {
        OutputLanguageProvider = outputLanguage;
        _transcoder = transcoder;
        _mp4 = mp4;
        _videoService = videoService;
        _presentationService = presentationService;
        _documentService = documentService;
        _speech = new SpeechProviderState(storage, speechServices);
        _filePlayer = filePlayer;
        _coordinator = coordinator;
        _time = timeProvider ?? TimeProvider.System;
        _delay = delay ?? ((span, token) => Task.Delay(span, token));
        if (_filePlayer != null)
            _filePlayer.PositionChanged += OnFilePositionChanged;
        _coordinator?.Register(PlaybackCoordinator.VideoParticipant, () => _isPlaying, StopAsync);
    }

    // ---------------- Videos ----------------

    /// <summary>Alle Videos des Profils, neueste zuerst.</summary>
    public IReadOnlyList<VideoDocument> Items => _library.Items.OrderByDescending(i => i.CreatedAt).ToList();

    private VideoDocument? _selectedVideo;
    public VideoDocument? SelectedVideo
    {
        get => _selectedVideo;
        private set
        {
            _selectedVideo = value;
            _currentSceneIndex = -1;
            _position = 0;
            _measured.Clear();
            _isEnded = false;
            OnPropertyChanged();
            OnVideoChanged();
        }
    }

    public void SelectVideo(string id)
    {
        if (SelectedVideo?.Id == id && !IsFormOpen) return;
        _ = StopAsync();
        SelectedVideo = _library.Items.FirstOrDefault(i => i.Id == id);
        IsFormOpen = false;
    }

    // ---------------- Präsentation des Profils ----------------

    private PresentationDocument? _currentPresentation;
    /// <summary>Die aktuelle Präsentation des Profils (Grundlage neuer Videos und Vergleich für „geändert“).</summary>
    public PresentationDocument? CurrentPresentation
    {
        get => _currentPresentation;
        private set { _currentPresentation = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasProfilePresentation)); OnPropertyChanged(nameof(PresentationHasUsableNotes)); OnVideoChanged(); }
    }

    public bool HasProfilePresentation => CurrentPresentation is { Slides.Count: > 0 };

    /// <summary>Die Präsentation hat brauchbare Sprechernotizen – dann wird „Sprechernotizen direkt verwenden“ angeboten.</summary>
    public bool PresentationHasUsableNotes => VideoNarration.HasUsableNotes(CurrentPresentation);

    /// <summary>Die Präsentation des Profils wurde geändert, seit das gewählte Video die Folien übernommen hat.</summary>
    public bool IsPresentationChanged =>
        SelectedVideo is { OwnSlides: false } video && HasProfilePresentation &&
        !string.IsNullOrEmpty(video.PresentationHash) && video.PresentationHash != VideoDocument.ComputePresentationHash(CurrentPresentation);

    /// <summary>Die Präsentation des Profils wurde gelöscht (das Video nutzt weiter seine Kopie der Folien).</summary>
    public bool IsPresentationMissing => SelectedVideo is { OwnSlides: false } && !HasProfilePresentation;

    /// <summary>
    /// Vergleicht die Quelldokumente der Präsentation mit den angehakten Dokumenten (<c>IsSelected &amp;&amp; !IsMissing</c>,
    /// wie beim Erzeugen). Die Präsentation des Profils kann früher aus anderen Dokumenten entstanden sein – dann
    /// zeigt der Reiter eine Warnung und bietet an, die Folien stattdessen aus den angehakten Dokumenten zu erzeugen.
    /// </summary>
    public static PresentationSourceInfo ComparePresentationSources(PresentationDocument? presentation, IEnumerable<DocumentItem> documents)
    {
        var selected = documents.Where(d => d.IsSelected && !d.IsMissing).Select(d => d.FileName)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (presentation is not { Slides.Count: > 0 })
            return new PresentationSourceInfo(PresentationSourceMatch.None, Array.Empty<string>(), selected, string.Empty);

        var sources = (presentation.SourceDocuments ?? new List<string>()).Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (sources.Count == 0)
            return new PresentationSourceInfo(PresentationSourceMatch.Unknown, sources, selected,
                VmStrings.Video_SourcesUnknown);

        var same = sources.Count == selected.Count && sources.All(s => selected.Contains(s, StringComparer.OrdinalIgnoreCase));
        return same
            ? new PresentationSourceInfo(PresentationSourceMatch.Match, sources, selected, Loc.Format(VmStrings.Video_SourcesMatch, ("sources", string.Join(", ", sources))))
            : new PresentationSourceInfo(PresentationSourceMatch.Mismatch, sources, selected,
                Loc.Format(VmStrings.Video_SourcesMismatch, ("sources", string.Join(", ", sources)),
                    ("selected", selected.Count == 0 ? VmStrings.Video_NoDocumentChecked : string.Join(", ", selected))));
    }

    /// <summary>Vergleich für die aktuelle Präsentation des Profils.</summary>
    public PresentationSourceInfo PresentationSources(IEnumerable<DocumentItem> documents) =>
        ComparePresentationSources(CurrentPresentation, documents);

    /// <summary>„Stattdessen neu aus den angehakten Dokumenten erzeugen“: neue Folien nur für dieses Video.</summary>
    public Task GenerateFromSelectedDocumentsAsync(IEnumerable<DocumentItem> documents)
    {
        SlideSource = VideoSlideSource.NewPresentation;
        UseNotesDirectly = false;
        return GenerateAsync(documents);
    }

    // ---------------- Formular „Neues Video“ ----------------

    private bool _isFormOpen;
    public bool IsFormOpen { get => _isFormOpen; set { _isFormOpen = value; OnPropertyChanged(); OnVideoChanged(); } }

    private VideoSlideSource _slideSource = VideoSlideSource.ProfilePresentation;
    public VideoSlideSource SlideSource { get => _slideSource; set { _slideSource = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanUseNotes)); } }

    private bool _useNotesDirectly;
    /// <summary>Sprechernotizen direkt als Sprechtext verwenden (ohne KI-Aufruf).</summary>
    public bool UseNotesDirectly { get => _useNotesDirectly; set { _useNotesDirectly = value; OnPropertyChanged(); } }

    /// <summary>„Sprechernotizen direkt verwenden“ ist nur bei der Präsentation des Profils mit brauchbaren Notizen möglich.</summary>
    public bool CanUseNotes => SlideSource == VideoSlideSource.ProfilePresentation && PresentationHasUsableNotes;

    private int _slideCount = DefaultSlideCount;
    /// <summary>Anzahl Inhaltsfolien bei neu erzeugten Folien.</summary>
    public int SlideCount
    {
        get => _slideCount;
        set { _slideCount = Math.Clamp(value, 3, 20); OnPropertyChanged(); }
    }

    private string _customTitle = string.Empty;
    public string CustomTitle { get => _customTitle; set { _customTitle = value; OnPropertyChanged(); } }

    /// <summary>
    /// Vorauswahl der Grundlage: ohne Präsentation „Neue Folien“; stammt die Präsentation nachweislich aus anderen als
    /// den angehakten Dokumenten, ebenfalls „Neue Folien“ (die Warnung bleibt sichtbar, man kann bewusst zurückwechseln);
    /// sonst – auch bei unbekannten Quellen älterer Präsentationen – die Präsentation des Profils.
    /// </summary>
    public VideoSlideSource DefaultSlideSource(IEnumerable<DocumentItem>? documents)
    {
        if (!HasProfilePresentation) return VideoSlideSource.NewPresentation;
        if (documents != null && PresentationSources(documents).Match == PresentationSourceMatch.Mismatch)
            return VideoSlideSource.NewPresentation;
        return VideoSlideSource.ProfilePresentation;
    }

    /// <param name="documents">Dokumente des Profils (für die Vorauswahl der Grundlage; null = nur nach vorhandener Präsentation).</param>
    public void OpenForm(IEnumerable<DocumentItem>? documents = null)
    {
        _ = StopAsync();
        SlideSource = DefaultSlideSource(documents);
        UseNotesDirectly = false;
        IsFormOpen = true;
        ClearStatus();
    }

    // ---------------- Fortschritt und Status ----------------

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set { _isBusy = value; OnPropertyChanged(); OnVideoChanged(); } }

    private string _busyText = string.Empty;
    public string BusyText { get => _busyText; private set { _busyText = value; OnPropertyChanged(); } }

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

    /// <param name="documents">Dokumente des Profils für die Vorauswahl der Grundlage im Formular.</param>
    public async Task LoadForProfileAsync(string profileName, IEnumerable<DocumentItem>? documents = null)
    {
        Cancel();
        CancelAudioGeneration();
        await StopAsync();
        _profileName = profileName;
        ClearStatus();

        _library = await _videoService.LoadLibraryAsync(profileName);
        await _speech.LoadAsync(profileName);
        CurrentPresentation = await LoadPresentationSafeAsync(profileName);

        _isLoaded = true;
        IsFormOpen = _library.Items.Count == 0;
        OnPropertyChanged(nameof(Items));
        SelectedVideo = Items.FirstOrDefault();
        if (IsFormOpen)
            SlideSource = DefaultSlideSource(documents);
        OnSpeechChanged();
        await RefreshAvailabilityAsync();
        await RefreshExportAvailabilityAsync();
    }

    /// <summary>
    /// Beim Öffnen des Reiters: nur bei Profilwechsel neu laden (eine laufende Wiedergabe geht beim Reiterwechsel weiter).
    /// Die Präsentation wird immer neu gelesen (sie kann im Reiter „Präsentation“ geändert worden sein).
    /// </summary>
    public async Task EnsureLoadedForProfileAsync(string profileName, IEnumerable<DocumentItem>? documents = null)
    {
        if (_isLoaded && string.Equals(_profileName, profileName, StringComparison.Ordinal))
        {
            CurrentPresentation = await LoadPresentationSafeAsync(profileName);
            await _speech.ReloadAsync();
            await RefreshAvailabilityAsync();
            await RefreshExportAvailabilityAsync();
            return;
        }

        await LoadForProfileAsync(profileName, documents);
    }

    private async Task<PresentationDocument?> LoadPresentationSafeAsync(string profileName)
    {
        try
        {
            return await _presentationService.LoadPresentationAsync(profileName);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Nach dem Schließen der Einstellungen: Sprachausgabe und Chat-KI neu lesen und neu prüfen.</summary>
    public async Task ReloadSpeechSettingsAsync()
    {
        if (!_isLoaded) return;
        await _speech.ReloadAsync();
        await RefreshAvailabilityAsync();
    }

    /// <summary>Profilwechsel (auch wenn der Reiter nicht offen ist): eine laufende Wiedergabe wird beendet.</summary>
    public async Task HandleProfileChangedAsync(string profileName)
    {
        if (string.Equals(_profileName, profileName, StringComparison.Ordinal))
            return;
        Cancel();
        CancelAudioGeneration();
        await StopAsync();
        IsPresenting = false;
    }

    // ---------------- Sprachausgabe ----------------

    public bool IsLocalChatAi => _speech.IsLocalChatAi;
    public SpeechSettings SpeechSettings => _speech.Settings;
    public IReadOnlyList<ITextToSpeechService> SpeechServices => _speech.Services;
    public SpeechProviderType SelectedProvider => _speech.SelectedProvider;
    public ITextToSpeechService? CurrentSpeechService => _speech.CurrentService;
    public SpeechAvailability AvailabilityOf(SpeechProviderType provider) => _speech.AvailabilityOf(provider);
    public SpeechAvailability CurrentAvailability => _speech.CurrentAvailability;
    public bool CanSelectProvider(SpeechProviderType provider) => _speech.CanSelectProvider(provider);
    public bool ShowPrivacyHint => _speech.ShowPrivacyHint;
    public IReadOnlyList<SpeechVoice> SystemVoices => _speech.SystemVoices;
    public double Rate => _speech.Settings.Rate;

    /// <summary>Tatsächlich verwendete Systemstimme der Sprecherin.</summary>
    public string NarratorVoice => _speech.VoiceForSpeakerIndex(0, SpeakerRoles);

    public async Task RefreshAvailabilityAsync()
    {
        await _speech.RefreshAvailabilityAsync();
        OnSpeechChanged();
    }

    public async Task SetProviderAsync(SpeechProviderType provider)
    {
        if (provider == SelectedProvider || !CanSelectProvider(provider)) return;
        await StopAsync();
        await RunSavingSettingsAsync(() => _speech.SetProviderAsync(provider));
        _measured.Clear();
        OnSpeechChanged();
    }

    public async Task SetSystemVoiceAsync(string voiceId)
    {
        await RunSavingSettingsAsync(() => _speech.SetSystemVoiceAsync(0, voiceId));
        OnSpeechChanged();
        await RestartSceneIfPlayingAsync();
    }

    public async Task SetRateAsync(double rate)
    {
        var clamped = Math.Round(SpeechSettings.ClampRate(rate), 2);
        if (Math.Abs(clamped - Rate) < 0.001) return;
        await RunSavingSettingsAsync(() => _speech.SetRateAsync(clamped));
        OnPropertyChanged(nameof(Rate));
        OnPlayerChanged();
        if (_fileMode)
        {
            if (_filePlayer != null) await _filePlayer.SetRateAsync(Rate);
            return;
        }
        await RestartSceneIfPlayingAsync();
    }

    private async Task RunSavingSettingsAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            ShowStatus(Loc.Format(VmStrings.Media_SettingSaveFailed, ("error", ex.Message)), isError: true);
        }
    }

    private bool _isTestingConnection;
    public bool IsTestingConnection { get => _isTestingConnection; private set { _isTestingConnection = value; OnPropertyChanged(); } }

    /// <summary>Prüft Schlüssel und Modell ohne Kosten (nur Anbieter mit Dateiexport).</summary>
    public async Task TestSpeechConnectionAsync()
    {
        if (CurrentSpeechService is not IAudioFileSpeechService service || IsTestingConnection) return;
        IsTestingConnection = true;
        try
        {
            ShowStatus(await service.TestConnectionAsync(_speech.Context), isError: false);
        }
        catch (LlmServiceException ex)
        {
            ShowStatus($"⚠️ {ex.Message}", isError: true, null, ex.Detail);
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    // ---------------- Erstellen ----------------

    public bool CanGenerate => !IsBusy && (SlideSource == VideoSlideSource.NewPresentation || HasProfilePresentation);

    /// <summary>Erstellt ein neues Video nach den Angaben im Formular.</summary>
    public async Task GenerateAsync(IEnumerable<DocumentItem> documents)
    {
        if (IsBusy) return;

        if (SlideSource == VideoSlideSource.ProfilePresentation && !HasProfilePresentation)
        {
            ShowStatus(VmStrings.Video_NoPresentation, isError: true);
            return;
        }

        var selectedDocs = documents.Where(d => d.IsSelected && !d.IsMissing).ToList();
        if (SlideSource == VideoSlideSource.NewPresentation && selectedDocs.Count == 0)
        {
            ShowStatus(VmStrings.Video_SelectDocument, isError: true);
            return;
        }

        await StopAsync();
        IsBusy = true;
        ClearStatus();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            PresentationDocument presentation;
            var ownSlides = false;
            if (SlideSource == VideoSlideSource.NewPresentation)
            {
                BusyText = VmStrings.Video_BusySlides;
                var chunks = new List<string>();
                foreach (var doc in selectedDocs)
                    chunks.AddRange(await _documentService.ExtractChunksAsync(doc.FilePath));
                var fallback = selectedDocs.Count == 1 ? Path.GetFileNameWithoutExtension(selectedDocs[0].FileName) : $"Unterlagen: {_profileName}";

                var slides = await _presentationService.GenerateFromDocumentsAsync(chunks, fallback, SlideCount, await OutputLanguageSettingAsync(_profileName), token);
                token.ThrowIfCancellationRequested();
                if (!slides.IsSuccess)
                {
                    var failure = MindmapStatusTexts.ForFailure(slides.FailureReason, slides.RawResponse, GenerationSubject.Presentation, VmStrings.Video_Generate);
                    ShowStatus(failure.Message, isError: true, failure.Tips, failure.Details);
                    return;
                }

                presentation = slides.Presentation!;
                presentation.SourceDocuments = selectedDocs.Select(d => d.FileName).ToList();
                ownSlides = true;
            }
            else
            {
                presentation = CurrentPresentation!;
            }

            VideoDocument video;
            VideoScriptResult? result = null;
            var fallbackTitle = string.IsNullOrWhiteSpace(presentation.Title) ? "Videoübersicht" : presentation.Title;
            if (UseNotesDirectly && CanUseNotes)
            {
                video = _videoService.CreateFromNotes(presentation, fallbackTitle);
            }
            else
            {
                BusyText = VmStrings.Video_BusyNarration;
                result = await _videoService.GenerateAsync(presentation, fallbackTitle, await OutputLanguageSettingAsync(_profileName), token);
                token.ThrowIfCancellationRequested();
                if (!result.IsSuccess)
                {
                    var failure = MindmapStatusTexts.ForFailure(result.FailureReason, result.RawResponse, GenerationSubject.VideoScript, VmStrings.Video_Generate);
                    ShowStatus(failure.Message, isError: true, failure.Tips, failure.Details);
                    return;
                }
                video = result.Video!;
            }

            video.OwnSlides = ownSlides;
            if (ownSlides) video.PresentationHash = string.Empty;
            if (!string.IsNullOrWhiteSpace(CustomTitle)) video.Title = CustomTitle.Trim();
            video.CreatedAt = DateTime.Now;

            _library.Items.Add(video);
            await SaveAsync();
            CustomTitle = string.Empty;
            IsFormOpen = false;
            OnPropertyChanged(nameof(Items));
            SelectedVideo = video;

            var info = Loc.Format(VmStrings.Video_CreatedInfo,
                ("scenes", Loc.Format(VmStrings.Video_SceneCount, ("count", video.Scenes.Count))),
                ("minutes", MediaTexts.Minutes(AudioScriptFormats.EstimateMinutes(video.WordCount))));
            if (result is { WasRepaired: true } || result is { FilledFromNotes: > 0 })
            {
                var missing = result.FilledFromNotes > 0
                    ? Loc.Format(VmStrings.Video_FilledFromNotes, ("count", result.FilledFromNotes))
                    : VmStrings.Video_ResponseIncomplete;
                ShowStatus(Loc.Format(VmStrings.Video_CreatedWithNote, ("title", video.Title), ("info", info), ("note", missing)), isError: false,
                    new[] { VmStrings.Video_TipEditNarration }, isWarning: true);
            }
            else
            {
                ShowStatus(Loc.Format(VmStrings.Video_Created, ("title", video.Title), ("info", info)), isError: false);
            }
        }
        catch (OperationCanceledException)
        {
            ShowStatus(VmStrings.Video_Cancelled, isError: false);
        }
        catch (Exception ex)
        {
            ShowStatus(Loc.Format(VmStrings.Video_Failed, ("error", ex.Message)), isError: true);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            BusyText = string.Empty;
            IsBusy = false;
        }
    }

    /// <summary>Übernimmt die aktuellen Folien der Präsentation (Sprechtexte bleiben, wo die Folie noch da ist).</summary>
    public async Task RefreshSlidesAsync()
    {
        var video = SelectedVideo;
        if (video == null || !HasProfilePresentation || IsBusy) return;
        await StopAsync();

        var removed = _videoService.RefreshSlides(video, CurrentPresentation!);
        foreach (var scene in removed)
            _videoService.DeleteSceneAudio(_profileName, video, scene);
        await SaveAsync();

        _currentSceneIndex = -1;
        _position = 0;
        _measured.Clear();
        OnPropertyChanged(nameof(SelectedVideo));
        OnVideoChanged();
        ShowStatus(Loc.Format(VmStrings.Video_SlidesRefreshed, ("count", video.Scenes.Count)), isError: false);
    }

    // ---------------- Verwalten und Bearbeiten ----------------

    public async Task<bool> RenameAsync(string id, string newTitle)
    {
        var item = _library.Items.FirstOrDefault(i => i.Id == id);
        var title = (newTitle ?? string.Empty).Trim();
        if (item == null || title.Length == 0) return false;
        item.Title = title;
        await SaveAsync();
        OnPropertyChanged(nameof(Items));
        if (SelectedVideo?.Id == id) OnPropertyChanged(nameof(SelectedVideo));
        return true;
    }

    /// <summary>Löscht ein Video samt seiner Audiodateien.</summary>
    public async Task DeleteAsync(string id)
    {
        var item = _library.Items.FirstOrDefault(i => i.Id == id);
        if (item == null) return;
        if (SelectedVideo?.Id == id)
        {
            await StopAsync();
            IsPresenting = false;
        }

        _videoService.DeleteAllAudio(_profileName, item);
        _library.Items.Remove(item);
        await SaveAsync();
        OnPropertyChanged(nameof(Items));
        if (SelectedVideo?.Id == id)
            SelectedVideo = Items.FirstOrDefault();
        if (_library.Items.Count == 0)
            IsFormOpen = true;
        ShowStatus(Loc.Format(VmStrings.Video_Deleted, ("title", item.Title)), isError: false);
    }

    /// <summary>Ändert den Sprechtext einer Szene (leer erlaubt: die Folie wird dann ohne Ton gezeigt).</summary>
    public async Task<bool> SetNarrationAsync(int index, string text)
    {
        var video = SelectedVideo;
        if (video == null || index < 0 || index >= video.Scenes.Count) return false;
        var value = (text ?? string.Empty).Replace("\r", string.Empty).Trim();
        if (value.Length > VideoDocument.MaxNarrationLength) value = value[..VideoDocument.MaxNarrationLength];
        if (video.Scenes[index].Narration == value) return true;

        var wasCurrent = _isPlaying && index == _currentSceneIndex;
        if (wasCurrent) await StopAsync();
        video.Scenes[index].Narration = value;
        _measured.Remove(index);
        await SaveAsync();
        OnPropertyChanged(nameof(SelectedVideo));
        OnVideoChanged();
        return true;
    }

    private async Task SaveAsync()
    {
        try
        {
            await _videoService.SaveLibraryAsync(_profileName, _library);
        }
        catch (Exception ex)
        {
            ShowStatus(Loc.Format(VmStrings.Audio_SaveFailed, ("error", ex.Message)), isError: true);
        }
    }

    public static string Extension(VideoScriptExportFormat format) => format == VideoScriptExportFormat.Markdown ? "md" : "txt";

    public static string FormatDescription(VideoScriptExportFormat format) =>
        format == VideoScriptExportFormat.Markdown ? VmStrings.Video_FormatMarkdown : VmStrings.Media_FormatText;

    public async Task<bool> ExportScriptAsync(VideoScriptExportFormat format, string filePath)
    {
        var video = SelectedVideo;
        if (video == null) return false;
        try
        {
            var text = format == VideoScriptExportFormat.Markdown ? _videoService.ExportToMarkdown(video, ExportCulture.Current) : _videoService.ExportToText(video, ExportCulture.Current);
            await File.WriteAllTextAsync(filePath, text, new System.Text.UTF8Encoding(true));
            ShowStatus(Loc.Format(VmStrings.Video_ScriptExported, ("file", Path.GetFileName(filePath))), isError: false);
            return true;
        }
        catch (Exception ex)
        {
            ShowStatus(Loc.Format(VmStrings.Media_ExportFailed, ("error", ex.Message)), isError: true);
            return false;
        }
    }

    public string SuggestFileName(string extension)
    {
        var title = SelectedVideo?.Title ?? "Video";
        var safe = new string(title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();
        return $"{(safe.Length == 0 ? "Video" : safe)}.{extension}";
    }

    // ---------------- Freigabe der Knöpfe ----------------

    public bool HasVideo => SelectedVideo is { Scenes.Count: > 0 } && !IsFormOpen;

    public bool CanPlay => PlayDisabledReason == null;

    public string? PlayDisabledReason
    {
        get
        {
            if (!HasVideo) return VmStrings.Video_NoVideo;
            if (IsBusy) return VmStrings.Media_Busy;
            var service = CurrentSpeechService;
            if (service == null) return VmStrings.Media_SpeechUnavailable;
            if (!service.SupportsLivePlayback)
            {
                if (SelectedVideo!.HasAudioFile && SelectedVideo.IsAudioOutdated)
                    return VmStrings.Video_AudioOutdated;
                if (SelectedAudioFilePath == null)
                    return Loc.Format(VmStrings.Video_NoLivePlayback, ("provider", MediaTexts.ProviderName(service.Provider)), ("button", VmStrings.Video_GenerateAudio));
                if (_filePlayer == null) return VmStrings.Media_PlayerUnavailable;
                return _filePlayer.IsReady ? null : VmStrings.Media_PlayerLoading;
            }
            return SpeechProviderState.AvailabilityReason(CurrentAvailability);
        }
    }

    public bool CanGenerateAudio => GenerateAudioDisabledReason == null;

    public string? GenerateAudioDisabledReason
    {
        get
        {
            if (!HasVideo) return VmStrings.Video_NoVideo;
            if (IsBusy) return VmStrings.Media_Busy;
            var service = CurrentSpeechService;
            if (service == null) return VmStrings.Media_SpeechUnavailable;
            if (!service.SupportsFileExport || service is not IAudioFileSpeechService) return SpeechProviderState.NoFileExportHint;
            if (SelectedVideo!.Scenes.All(s => !s.HasNarration)) return VmStrings.Video_NoNarration;
            return SpeechProviderState.AvailabilityReason(CurrentAvailability);
        }
    }

    // ---------------- Audio (Gemini TTS) ----------------

    /// <summary>Pfad der zusammengesetzten Audiodatei des gewählten Videos (null = keine bzw. nicht vorhanden).</summary>
    public string? SelectedAudioFilePath =>
        SelectedVideo is { HasAudioFile: true } video ? _videoService.GetAudioFilePath(_profileName, video) : null;

    /// <summary>Abspielen über die Audiodatei: Anbieter ohne Live-Wiedergabe und aktuelle Datei vorhanden.</summary>
    public bool UsesFilePlayback =>
        CurrentSpeechService is { SupportsLivePlayback: false } && SelectedVideo is { IsAudioOutdated: false } && SelectedAudioFilePath != null;

    /// <summary>Szenen, die beim nächsten Erzeugen berechnet werden (nur geänderte bzw. noch fehlende).</summary>
    public IReadOnlyList<VideoScene> ScenesNeedingAudio => SelectedVideo?.ScenesNeedingAudio ?? new List<VideoScene>();

    /// <summary>Alle Szenen mit Audio sind aktuell, nur die Gesamtdatei fehlt oder ist veraltet (Zusammensetzen ist kostenlos).</summary>
    public bool NeedsOnlyAssembly =>
        SelectedVideo is { } video && ScenesNeedingAudio.Count == 0 && video.Scenes.Any(s => s.HasNarration) && (!video.HasAudioFile || video.IsAudioOutdated || SelectedAudioFilePath == null);

    /// <summary>Kostenschätzung für die zu erzeugenden Szenen (bei <paramref name="all"/> für alle Szenen).</summary>
    public GeminiTtsEstimate? EstimateFor(bool all)
    {
        if (SelectedVideo is not { } video || SelectedProvider != SpeechProviderType.GeminiTts) return null;
        var scenes = all ? video.Scenes.Where(s => s.HasNarration).ToList() : ScenesNeedingAudio.ToList();
        if (scenes.Count == 0) return null;
        return GeminiTtsInfo.Estimate(scenes.Sum(s => AudioScriptFormats.CountWords(s.Narration)), scenes.Sum(s => s.Narration.Length), DateTime.Today);
    }

    /// <summary>„3 von 8 Szenen · ca. 1 Min. Audio · ca. 2,9 MB (WAV) · geschätzt …“</summary>
    public string? EstimateText(bool all)
    {
        if (SelectedVideo is not { } video || EstimateFor(all) is not { } e) return null;
        var count = all ? video.Scenes.Count(s => s.HasNarration) : ScenesNeedingAudio.Count;
        var total = video.Scenes.Count(s => s.HasNarration);
        var scenes = count == total
            ? Loc.Format(VmStrings.Video_SceneCount, ("count", count))
            : Loc.Format(VmStrings.Video_ScenesOfTotal, ("count", count), ("total", total));
        return Loc.Format(VmStrings.Video_EstimateScenes, ("scenes", scenes), ("estimate", MediaTexts.GeminiEstimate(e)));
    }

    private bool _isGeneratingAudio;
    public bool IsGeneratingAudio { get => _isGeneratingAudio; private set { _isGeneratingAudio = value; OnPropertyChanged(); } }

    private double _audioProgress;
    public double AudioProgress { get => _audioProgress; private set { _audioProgress = value; OnPropertyChanged(); } }

    private string _audioProgressText = string.Empty;
    public string AudioProgressText { get => _audioProgressText; private set { _audioProgressText = value; OnPropertyChanged(); } }

    public void CancelAudioGeneration() => _audioCts?.Cancel();

    /// <summary>
    /// Erzeugt das Audio mit Gemini TTS: je Szene eine eigene Anfrage (exakte Folienwechsel), nur geänderte bzw. fehlende
    /// Szenen (bei <paramref name="all"/> alle). Jede Szenen-Datei entsteht vollständig oder gar nicht; fertige Szenen
    /// bleiben bei Abbruch erhalten. Danach werden die Szenen kostenlos zu einer Datei mit Zeitmarken zusammengesetzt.
    /// </summary>
    public async Task GenerateAudioAsync(bool all = false)
    {
        if (!CanGenerateAudio)
        {
            if (GenerateAudioDisabledReason is { } reason)
                ShowStatus($"⚠️ {reason}", isError: false, isWarning: true);
            return;
        }

        var video = SelectedVideo!;
        var service = (IAudioFileSpeechService)CurrentSpeechService!;
        await StopAsync();

        var todo = (all ? video.Scenes.Where(s => s.HasNarration) : video.Scenes.Where(s => s.NeedsAudio)).ToList();
        IsBusy = true;
        IsGeneratingAudio = true;
        AudioProgress = 0;
        AudioProgressText = todo.Count == 0 ? VmStrings.Video_AudioAssembling : VmStrings.Video_AudioPreparing;
        ClearStatus();
        _audioCts = new CancellationTokenSource();
        var token = _audioCts.Token;
        var done = 0;
        string createdWith = video.AudioCreatedWith;

        try
        {
            foreach (var scene in todo)
            {
                token.ThrowIfCancellationRequested();
                var number = video.Scenes.IndexOf(scene) + 1;
                var sceneIndex = done;
                var progress = new SynchronousProgress<SpeechSynthesisProgress>(p =>
                {
                    AudioProgress = (sceneIndex + p.Fraction) / (todo.Count + 0.2);
                    AudioProgressText = Loc.Format(VmStrings.Video_AudioSceneProgress, ("number", sceneIndex + 1), ("total", todo.Count), ("slide", number));
                });
                progress.Report(new SpeechSynthesisProgress(0, 1));

                var summary = new AudioSummary
                {
                    Id = scene.Id,
                    Title = video.Title,
                    Format = AudioScriptFormat.Summary,
                    Segments = new List<AudioSegment> { new() { Speaker = AudioScriptFormats.NarratorName, Text = scene.Narration.Trim() } }
                };
                var path = _videoService.GetTargetSceneAudioPath(_profileName, video, scene);
                var result = await service.SynthesizeToFileAsync(summary, _speech.Context, path, progress, token);

                scene.AudioFileName = Path.GetFileName(path);
                scene.AudioTextHash = scene.ComputeTextHash();
                scene.AudioDurationSeconds = result.DurationSeconds;
                createdWith = result.CreatedWith;
                done++;
                await SaveAsync(); // fertige Szenen bleiben auch bei späterem Abbruch erhalten
            }

            token.ThrowIfCancellationRequested();
            AudioProgress = todo.Count / (todo.Count + 0.2);
            AudioProgressText = VmStrings.Video_AudioAssembling;
            var assembly = await _videoService.AssembleAudioAsync(_profileName, video, token);
            video.AudioFileName = assembly.FileName;
            video.AudioDurationSeconds = assembly.DurationSeconds;
            video.AudioSceneStarts = assembly.SceneStarts.ToList();
            video.AudioCreatedAt = DateTime.Now;
            video.AudioCreatedWith = createdWith;
            video.AudioScenesHash = video.ComputeScenesHash();
            await SaveAsync();

            _currentSceneIndex = -1;
            _position = 0;
            var fresh = todo.Count == 0 ? VmStrings.Video_AudioNothingNew : Loc.Format(VmStrings.Video_AudioScenesNew, ("count", todo.Count));
            ShowStatus(Loc.Format(VmStrings.Video_AudioDone, ("duration", FormatClock(assembly.DurationSeconds)), ("fresh", fresh)), isError: false);
        }
        catch (OperationCanceledException)
        {
            ShowStatus(Loc.Format(VmStrings.Video_AudioCancelled, ("done", done), ("total", todo.Count)), isError: false);
        }
        catch (LlmServiceException ex)
        {
            ShowStatus($"⚠️ {ex.Message}", isError: true,
                new[] { Loc.Format(VmStrings.Video_AudioPartialTip, ("done", done), ("total", todo.Count)) }, ex.Detail);
        }
        catch (Exception ex)
        {
            ShowStatus(Loc.Format(VmStrings.Video_AudioFailed, ("error", ex.Message)), isError: true,
                new[] { VmStrings.Video_AudioUnchangedTip });
        }
        finally
        {
            _audioCts?.Dispose();
            _audioCts = null;
            IsGeneratingAudio = false;
            AudioProgress = 0;
            AudioProgressText = string.Empty;
            IsBusy = false;
            OnPropertyChanged(nameof(SelectedVideo));
            OnVideoChanged();
        }
    }

    /// <summary>Löscht alle Audiodateien des gewählten Videos (die Sprechtexte bleiben).</summary>
    public async Task DeleteAudioAsync()
    {
        var video = SelectedVideo;
        if (video == null || IsBusy) return;
        await StopAsync();
        _videoService.DeleteAllAudio(_profileName, video);
        if (video.HasAudioFile)
        {
            ShowStatus(VmStrings.Video_AudioDeleteFailed, isError: true);
            return;
        }
        await SaveAsync();
        OnPropertyChanged(nameof(SelectedVideo));
        OnVideoChanged();
        ShowStatus(VmStrings.Video_AudioDeleted, isError: false);
    }

    private sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    // ---------------- Wiedergabe: Zustand ----------------

    private int _currentSceneIndex = -1;
    private double _position;
    private bool _isPlaying;
    private bool _isPaused;
    private bool _isEnded;
    private bool _fileMode;
    private int _playGeneration;
    private string? _playingTitle;
    private readonly Dictionary<int, double> _measured = new();
    private int _revealedBullets;

    // Zeitmessung der Systemstimme (aktive Zeit ohne Pausen)
    private long _sceneStartTimestamp;
    private double _pausedInScene;
    private long? _pauseStartTimestamp;

    /// <summary>Gezeigte Szene; -1 = keine (vor dem Start).</summary>
    public int CurrentSceneIndex => _currentSceneIndex;

    public bool IsPlaying => _isPlaying;
    public bool IsPaused => _isPaused;

    /// <summary>Das Video ist bis zum Ende gelaufen (letzte Folie bleibt stehen).</summary>
    public bool IsEnded => _isEnded;

    public int SceneCount => SelectedVideo?.Scenes.Count ?? 0;

    /// <summary>Position in Sekunden auf der aktuellen Zeitachse.</summary>
    public double Position => _position;

    /// <summary>Zeitachse: exakte Zeitmarken der Audiodatei bzw. Schätzung/Messung bei der Systemstimme.</summary>
    public VideoTimeline Timeline
    {
        get
        {
            var video = SelectedVideo;
            if (video == null) return VideoTimeline.FromEstimates(Array.Empty<VideoScene>(), Rate);
            return UsesFilePlayback && video.AudioSceneStarts.Count == video.Scenes.Count
                ? VideoTimeline.FromStarts(video.AudioSceneStarts, video.AudioDurationSeconds, video.Scenes.Count)
                : VideoTimeline.FromEstimates(video.Scenes, Rate, _measured);
        }
    }

    public double Progress => Timeline.Progress(_position);

    public string ProgressText
    {
        get
        {
            var video = SelectedVideo;
            if (video == null || video.Scenes.Count == 0) return string.Empty;
            var timeline = Timeline;
            var scene = _currentSceneIndex < 0
                ? Loc.Format(VmStrings.Video_SlideCount, ("count", video.Scenes.Count))
                : Loc.Format(VmStrings.Video_SlidePosition, ("number", _currentSceneIndex + 1), ("total", video.Scenes.Count));
            return Loc.Format(UsesFilePlayback ? VmStrings.Video_ProgressFile : VmStrings.Video_ProgressApprox,
                ("position", FormatClock(_position)), ("duration", FormatClock(timeline.Total)), ("scene", scene));
        }
    }

    /// <summary>Die gerade gezeigte Folie (vor dem Start die erste).</summary>
    public PresentationSlide? CurrentSlide => SelectedVideo?.SlideAt(Math.Max(0, _currentSceneIndex));

    public int DisplayedSceneIndex => Math.Max(0, _currentSceneIndex);

    /// <summary>Anzahl sichtbarer Stichpunkte der aktuellen Folie (außerhalb der Wiedergabe alle).</summary>
    public int VisibleBulletCount
    {
        get
        {
            var slide = CurrentSlide;
            if (slide == null) return 0;
            if ((!_isPlaying && !_isPaused) || _isEnded) return slide.Bullets.Count;
            return Math.Max(_revealedBullets, ComputeVisibleBullets());
        }
    }

    private int ComputeVisibleBullets()
    {
        var slide = CurrentSlide;
        if (slide == null) return 0;
        var timeline = Timeline;
        var index = DisplayedSceneIndex;
        return VideoTimeline.VisibleBullets(slide.Bullets.Count, _position - timeline.StartOf(index), timeline.DurationOf(index));
    }

    /// <summary>Neue Szene: Stichpunkte beginnen wieder verborgen.</summary>
    private void ResetReveal() => _revealedBullets = 0;

    public string? NowPlayingTitle => _isPlaying ? _playingTitle : null;

    public bool CanGoPrevious => HasVideo && _currentSceneIndex > 0;
    public bool CanGoNext => HasVideo && _currentSceneIndex < SceneCount - 1;

    private bool _isPresenting;
    /// <summary>Vollbild-Wiedergabe (Folie bildschirmfüllend mit Steuerleiste).</summary>
    public bool IsPresenting { get => _isPresenting; private set { _isPresenting = value; OnPropertyChanged(); } }

    // ---------------- Wiedergabe: Steuerung ----------------

    /// <summary>Startet die Vollbild-Wiedergabe ab Szene <paramref name="fromScene"/> (null = aktuelle bzw. Anfang).</summary>
    public async Task StartPresentingAsync(int? fromScene = null)
    {
        if (!HasVideo) return;
        IsPresenting = true;
        if (fromScene is { } index)
            await GoToSceneAsync(index);
        if (CanPlay && (!_isPlaying || _isPaused))
            await PlayAsync();
    }

    /// <summary>Beendet die Vollbild-Wiedergabe (und die Wiedergabe).</summary>
    public async Task ExitPresentingAsync()
    {
        await StopAsync();
        IsPresenting = false;
    }

    /// <summary>Abspielen bzw. nach einer Pause fortsetzen; am Ende beginnt das Video von vorn.</summary>
    public async Task PlayAsync()
    {
        if (!CanPlay)
        {
            if (PlayDisabledReason is { } reason)
                ShowStatus($"⚠️ {reason}", isError: false, isWarning: true);
            return;
        }

        if (!_isPlaying && _coordinator != null)
            await _coordinator.StopOthersAsync(PlaybackCoordinator.VideoParticipant);

        if (_isPlaying && _isPaused)
        {
            await ResumeAsync();
            return;
        }
        if (_isPlaying) return;

        if (_isEnded || _currentSceneIndex < 0 || _currentSceneIndex >= SceneCount)
        {
            _currentSceneIndex = 0;
            _position = 0;
        }
        _isEnded = false;

        if (UsesFilePlayback)
        {
            // Nach einem Sprung in die Fortschrittsleiste genau dort weiter, sonst am Anfang der Szene
            var sceneStart = Timeline.StartOf(_currentSceneIndex);
            await PlayFileAsync(Math.Max(sceneStart, _position));
        }
        else
        {
            await RunLiveAsync((ILiveSpeechService)CurrentSpeechService!);
        }
    }

    private async Task ResumeAsync()
    {
        if (_fileMode)
        {
            await _filePlayer!.ResumeAsync();
        }
        else if (CurrentSpeechService is ILiveSpeechService live)
        {
            await live.ResumeAsync();
            if (_pauseStartTimestamp is { } paused)
                _pausedInScene += _time.GetElapsedTime(paused).TotalSeconds;
            _pauseStartTimestamp = null;
        }
        _isPaused = false;
        OnPlayerChanged();
    }

    public async Task PauseAsync()
    {
        if (!_isPlaying || _isPaused) return;
        if (_fileMode)
        {
            await _filePlayer!.PauseAsync();
        }
        else
        {
            if (CurrentSpeechService is ILiveSpeechService live)
                await live.PauseAsync();
            _pauseStartTimestamp = _time.GetTimestamp();
        }
        _isPaused = true;
        OnPlayerChanged();
    }

    public Task TogglePlayPauseAsync() => _isPlaying && !_isPaused ? PauseAsync() : PlayAsync();

    /// <summary>Beendet die Wiedergabe; Folie und Position bleiben erhalten.</summary>
    public async Task StopAsync()
    {
        var wasPlaying = _isPlaying;
        var wasFile = _fileMode;
        _playGeneration++;
        _isPlaying = false;
        _isPaused = false;
        _fileMode = false;
        _pauseStartTimestamp = null;
        if (wasPlaying && wasFile && _filePlayer != null)
        {
            try { await _filePlayer.StopAsync(); }
            catch { /* Stopp ist immer erlaubt */ }
        }
        else if (wasPlaying)
        {
            await _speech.StopAllLiveAsync();
        }
        OnPlayerChanged();
    }

    public Task NextAsync() => GoToSceneAsync(_currentSceneIndex + 1);
    public Task PreviousAsync() => GoToSceneAsync(_currentSceneIndex - 1);

    /// <summary>Springt zum Anfang einer Szene; läuft die Wiedergabe, geht sie dort weiter.</summary>
    public async Task GoToSceneAsync(int index)
    {
        if (!HasVideo || index < 0 || index >= SceneCount) return;
        await SeekToAsync(Timeline.StartOf(index), index);
    }

    /// <summary>Springt zu einem Bruchteil der Gesamtlänge (Klick in die Fortschrittsleiste).</summary>
    public Task SeekAsync(double fraction)
    {
        if (!HasVideo) return Task.CompletedTask;
        var timeline = Timeline;
        var position = timeline.PositionAt(fraction);
        var scene = timeline.SceneAt(position);
        // Systemstimme: kann nur am Anfang einer Szene einsetzen
        return SeekToAsync(UsesFilePlayback ? position : timeline.StartOf(scene), scene);
    }

    private async Task SeekToAsync(double position, int scene)
    {
        _isEnded = false;
        ResetReveal();
        if (UsesFilePlayback)
        {
            _currentSceneIndex = scene;
            _position = position;
            if (_isPlaying && _fileMode)
            {
                await _filePlayer!.PlayAsync(SelectedAudioFilePath!, position, Rate);
                _isPaused = false;
            }
            OnPlayerChanged();
            return;
        }

        var resume = _isPlaying;
        await StopAsync();
        _currentSceneIndex = scene;
        _position = Timeline.StartOf(scene);
        OnPlayerChanged();
        if (resume && CanPlay)
            await RunLiveAsync((ILiveSpeechService)CurrentSpeechService!);
    }

    private async Task RestartSceneIfPlayingAsync()
    {
        if (_isPlaying && !_isPaused && !_fileMode)
            await GoToSceneAsync(_currentSceneIndex);
    }

    /// <summary>Tastatur im Vollbild: Leertaste/K Pause, Pfeile und Bild auf/ab je Szene, Pos1/Ende.</summary>
    public async Task<bool> HandleKeyAsync(string key)
    {
        switch (key)
        {
            case " ":
            case "k":
            case "K":
                await TogglePlayPauseAsync();
                return true;
            case "ArrowRight":
            case "PageDown":
                await NextAsync();
                return true;
            case "ArrowLeft":
            case "PageUp":
                await PreviousAsync();
                return true;
            case "Home":
                await GoToSceneAsync(0);
                return true;
            case "End":
                await GoToSceneAsync(SceneCount - 1);
                return true;
            default:
                return false;
        }
    }

    // ---------------- Wiedergabe: Systemstimme ----------------

    /// <summary>Aktive Sekunden seit Beginn der aktuellen Szene (ohne Pausen).</summary>
    private double ElapsedInScene()
    {
        var total = _time.GetElapsedTime(_sceneStartTimestamp).TotalSeconds - _pausedInScene;
        if (_pauseStartTimestamp is { } paused)
            total -= _time.GetElapsedTime(paused).TotalSeconds;
        return Math.Max(0, total);
    }

    /// <summary>
    /// Aktualisiert Position und Stichpunkte während der Live-Wiedergabe (die Oberfläche ruft das regelmäßig auf).
    /// Die Position bleibt innerhalb der Szene, bis die Stimme die Szene beendet hat.
    /// </summary>
    public void Tick()
    {
        if (!_isPlaying || _fileMode || _currentSceneIndex < 0) return;
        var elapsed = ElapsedInScene();
        var timeline = Timeline;
        var duration = timeline.DurationOf(_currentSceneIndex);

        // Spricht die Stimme länger als geschätzt, wächst die Szene mit (Fortschritt bleibt nicht stehen)
        if (elapsed > duration - 0.3 && SelectedVideo?.Scenes[_currentSceneIndex].HasNarration == true)
        {
            _measured[_currentSceneIndex] = (elapsed + 1.0) * Math.Max(SpeechSettings.MinRate, Rate);
            timeline = Timeline;
            duration = timeline.DurationOf(_currentSceneIndex);
        }

        _position = timeline.StartOf(_currentSceneIndex) + Math.Min(elapsed, Math.Max(0, duration - 0.05));
        _revealedBullets = Math.Max(_revealedBullets, ComputeVisibleBullets());
        OnPositionChanged();
    }

    private async Task RunLiveAsync(ILiveSpeechService live)
    {
        var video = SelectedVideo!;
        var generation = ++_playGeneration;
        _playingTitle = video.Title;
        _isPlaying = true;
        _isPaused = false;
        _fileMode = false;
        OnPlayerChanged();

        try
        {
            while (generation == _playGeneration && _currentSceneIndex >= 0 && _currentSceneIndex < video.Scenes.Count)
            {
                var index = _currentSceneIndex;
                var scene = video.Scenes[index];
                _sceneStartTimestamp = _time.GetTimestamp();
                _pausedInScene = 0;
                ResetReveal();
                _pauseStartTimestamp = null;
                _position = Timeline.StartOf(index);
                OnPlayerChanged();

                if (scene.HasNarration)
                {
                    foreach (var piece in SystemVoiceRules.SplitForSpeech(scene.Narration))
                    {
                        var completed = await live.SpeakAsync(piece, NarratorVoice, Rate);
                        if (!completed || generation != _playGeneration)
                            return;
                    }
                }
                else
                {
                    // Folie ohne Sprechtext: einige Sekunden stehen lassen (Pausen werden berücksichtigt)
                    while (generation == _playGeneration && (_isPaused || ElapsedInScene() < VideoTimeline.MinSceneSeconds))
                    {
                        await _delay(TimeSpan.FromMilliseconds(100), CancellationToken.None);
                        Tick();
                    }
                }

                if (generation != _playGeneration)
                    return;

                // Gemessene Dauer (auf Tempo 1 umgerechnet) ersetzt die Schätzung – die Fortschrittsleiste stimmt danach genauer
                if (scene.HasNarration)
                    _measured[index] = ElapsedInScene() * Math.Max(SpeechSettings.MinRate, Rate);

                if (index >= video.Scenes.Count - 1)
                {
                    _isEnded = true;
                    _position = Timeline.Total;
                    break;
                }

                _currentSceneIndex = index + 1;
            }
        }
        catch (Exception ex)
        {
            if (generation == _playGeneration)
                ShowStatus($"⚠️ {ex.Message}", isError: true);
        }
        finally
        {
            if (generation == _playGeneration)
            {
                _isPlaying = false;
                _isPaused = false;
                OnPlayerChanged();
            }
        }
    }

    // ---------------- Wiedergabe: Audiodatei ----------------

    private async Task PlayFileAsync(double start)
    {
        _playGeneration++;
        _fileMode = true;
        _isPlaying = true;
        _isPaused = false;
        _position = start;
        _currentSceneIndex = Timeline.SceneAt(start);
        _playingTitle = SelectedVideo!.Title;
        OnPlayerChanged();

        try
        {
            await _filePlayer!.PlayAsync(SelectedAudioFilePath!, start, Rate);
        }
        catch (Exception ex)
        {
            _fileMode = false;
            _isPlaying = false;
            ShowStatus(Loc.Format(VmStrings.Media_FilePlaybackFailed, ("error", ex.Message)), isError: true);
            OnPlayerChanged();
        }
    }

    /// <summary>Position der Audiodatei (vom Player): Folie und Stichpunkte nachführen, am Ende stehen bleiben.</summary>
    private void OnFilePositionChanged(double seconds, bool ended)
    {
        if (!_fileMode) return;

        if (ended)
        {
            _isPlaying = false;
            _isPaused = false;
            _fileMode = false;
            _isEnded = true;
            _position = Timeline.Total;
            _currentSceneIndex = Math.Max(0, SceneCount - 1);
            OnPlayerChanged();
            return;
        }

        _position = seconds;
        var scene = Timeline.SceneAt(seconds);
        if (scene != _currentSceneIndex)
        {
            _currentSceneIndex = scene;
            ResetReveal();
            OnPlayerChanged();
            return;
        }
        OnPositionChanged();
    }

    private static string FormatClock(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, Math.Round(seconds)));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }

    // ---------------- Benachrichtigungen ----------------

    private void OnSpeechChanged()
    {
        OnPropertyChanged(nameof(SelectedProvider));
        OnPropertyChanged(nameof(CurrentSpeechService));
        OnPropertyChanged(nameof(CurrentAvailability));
        OnPropertyChanged(nameof(ShowPrivacyHint));
        OnPropertyChanged(nameof(IsLocalChatAi));
        OnPropertyChanged(nameof(SystemVoices));
        OnPropertyChanged(nameof(NarratorVoice));
        OnPropertyChanged(nameof(Rate));
        OnVideoChanged();
    }

    private void OnVideoChanged()
    {
        OnPropertyChanged(nameof(HasVideo));
        OnPropertyChanged(nameof(IsPresentationChanged));
        OnPropertyChanged(nameof(IsPresentationMissing));
        OnPropertyChanged(nameof(CanGenerate));
        OnPropertyChanged(nameof(CanUseNotes));
        OnPropertyChanged(nameof(ScenesNeedingAudio));
        OnPropertyChanged(nameof(NeedsOnlyAssembly));
        OnPlayerChanged();
    }

    private void OnPlayerChanged()
    {
        OnPropertyChanged(nameof(CurrentSceneIndex));
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(IsEnded));
        OnPropertyChanged(nameof(NowPlayingTitle));
        OnPropertyChanged(nameof(CurrentSlide));
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(PlayDisabledReason));
        OnPropertyChanged(nameof(CanGenerateAudio));
        OnPropertyChanged(nameof(GenerateAudioDisabledReason));
        OnPropertyChanged(nameof(UsesFilePlayback));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
        OnPositionChanged();
    }

    private void OnPositionChanged()
    {
        OnPropertyChanged(nameof(Position));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(VisibleBulletCount));
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
