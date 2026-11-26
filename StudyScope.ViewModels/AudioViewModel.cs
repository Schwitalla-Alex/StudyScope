using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Globalization;
using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.ViewModels.Resources;
using StudyScope.Core.Localization;
using StudyScope.ViewModels.Audio;
using StudyScope.ViewModels.Mindmap;

namespace StudyScope.ViewModels;

/// <summary>Exportformate des Sprechskripts.</summary>
public enum AudioScriptExportFormat
{
    Text,
    Markdown
}

/// <summary>
/// Steuert den Reiter „Audio“: Sprechskript per Chat-KI des Profils erstellen (Zusammenfassung, Podcast, Kurz-Briefing),
/// mehrere Beiträge je Profil verwalten und bearbeiten, Sprachausgabe wählen (je Profil gespeichert, unabhängig von der
/// Chat-KI) samt Verfügbarkeitsprüfung, und das Skript segmentweise vorlesen (Abspielen/Pause, Vor/Zurück, Tempo).
/// Knöpfe werden vorab deaktiviert und begründet – keine Fehlermeldung erst nach dem Klick.
/// </summary>
public partial class AudioViewModel : INotifyPropertyChanged
{
    public static string LocalAiHint => SpeechProviderState.LocalAiHint;
    public static string GeminiPrivacyHint => SpeechProviderState.GeminiPrivacyHint;
    public static string NoFileExportHint => SpeechProviderState.NoFileExportHint;

    private readonly IAudioScriptService _audioService;
    private readonly IDocumentService _documentService;
    private readonly SpeechProviderState _speech;
    private readonly IAudioFilePlayer? _filePlayer;
    private readonly PlaybackCoordinator? _coordinator;
    private CancellationTokenSource? _audioCts;
    private CancellationTokenSource? _cts;
    private string _profileName = string.Empty;
    private AudioLibrary _library = new();
    private int _playGeneration;

    private SpeechSettings _speechSettings => _speech.Settings;

    public event PropertyChangedEventHandler? PropertyChanged;

    public AudioViewModel(IAudioScriptService audioService, IDocumentService documentService, IStorageService storage,
        IEnumerable<ITextToSpeechService> speechServices, IAudioFilePlayer? filePlayer = null, PlaybackCoordinator? coordinator = null,
        IAudioTranscoder? transcoder = null)
    {
        UseTranscoder(transcoder);
        _audioService = audioService;
        _documentService = documentService;
        _speech = new SpeechProviderState(storage, speechServices);
        _filePlayer = filePlayer;
        if (_filePlayer != null)
            _filePlayer.PositionChanged += OnFilePositionChanged;
        _coordinator = coordinator;
        _coordinator?.Register(PlaybackCoordinator.AudioParticipant, () => _isPlaying, StopAsync);
    }

    // ---------------- Beiträge ----------------

    /// <summary>Alle Beiträge des Profils, neueste zuerst.</summary>
    public IReadOnlyList<AudioSummary> Items => _library.Items.OrderByDescending(i => i.CreatedAt).ToList();

    private AudioSummary? _selectedSummary;
    public AudioSummary? SelectedSummary
    {
        get => _selectedSummary;
        private set
        {
            _selectedSummary = value;
            _currentSegmentIndex = -1;
            OnPropertyChanged();
            OnPlayerChanged();
        }
    }

    public void SelectSummary(string id)
    {
        if (SelectedSummary?.Id == id && !IsFormOpen) return;
        _ = StopAsync();
        SelectedSummary = _library.Items.FirstOrDefault(i => i.Id == id);
        IsFormOpen = false;
    }

    // ---------------- Formular „Neuer Beitrag“ ----------------

    private bool _isFormOpen;
    public bool IsFormOpen { get => _isFormOpen; set { _isFormOpen = value; OnPropertyChanged(); } }

    private AudioScriptFormat _format = AudioScriptFormat.Summary;
    public AudioScriptFormat Format { get => _format; set { _format = value; OnPropertyChanged(); } }

    private AudioScriptLength _length = AudioScriptLength.Medium;
    public AudioScriptLength Length { get => _length; set { _length = value; OnPropertyChanged(); } }

    private string _topic = string.Empty;
    /// <summary>Optionaler Schwerpunkt bzw. Thema.</summary>
    public string Topic { get => _topic; set { _topic = value; OnPropertyChanged(); } }

    private string _customTitle = string.Empty;
    public string CustomTitle { get => _customTitle; set { _customTitle = value; OnPropertyChanged(); } }

    public void OpenForm()
    {
        _ = StopAsync();
        IsFormOpen = true;
        ClearStatus();
    }

    // ---------------- Fortschritt und Status ----------------

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set { _isBusy = value; OnPropertyChanged(); OnPlayerChanged(); }
    }

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
        await StopAsync();
        _profileName = profileName;
        ClearStatus();

        _library = await _audioService.LoadLibraryAsync(profileName);
        await _speech.LoadAsync(profileName);

        _isLoaded = true;
        IsFormOpen = _library.Items.Count == 0;
        OnPropertyChanged(nameof(Items));
        SelectedSummary = Items.FirstOrDefault();
        OnSpeechChanged();
        await RefreshAvailabilityAsync();
    }

    /// <summary>
    /// Beim Öffnen des Reiters: nur neu laden, wenn das Profil gewechselt hat – eine laufende Wiedergabe geht beim
    /// Reiterwechsel weiter. Die Verfügbarkeit wird immer neu geprüft (Einstellungen können sich geändert haben).
    /// </summary>
    public async Task EnsureLoadedForProfileAsync(string profileName)
    {
        if (_isLoaded && string.Equals(_profileName, profileName, StringComparison.Ordinal))
        {
            await _speech.ReloadAsync();
            await RefreshAvailabilityAsync();
            return;
        }

        await LoadForProfileAsync(profileName);
    }

    private bool _isLoaded;

    /// <summary>Nach dem Schließen der Einstellungen: Sprachausgabe und Chat-KI neu lesen und neu prüfen.</summary>
    public async Task ReloadSpeechSettingsAsync()
    {
        if (!_isLoaded) return;
        await _speech.ReloadAsync();
        await RefreshAvailabilityAsync();
    }

    /// <summary>
    /// Profilwechsel (auch wenn der Reiter nicht offen ist): eine laufende Wiedergabe des alten Profils wird beendet.
    /// Geladen wird das neue Profil erst beim Öffnen des Reiters.
    /// </summary>
    public async Task HandleProfileChangedAsync(string profileName)
    {
        if (string.Equals(_profileName, profileName, StringComparison.Ordinal))
            return;
        Cancel();
        await StopAsync();
    }

    // ---------------- Chat-KI und Sprachausgabe ----------------

    /// <summary>Chat-KI des Profils ist lokal (Ollama/LM Studio) – dann wird <see cref="LocalAiHint"/> angezeigt.</summary>
    public bool IsLocalChatAi => _speech.IsLocalChatAi;

    public SpeechSettings SpeechSettings => _speech.Settings;

    /// <summary>Alle Anbieter der Sprachausgabe (auch nicht verfügbare – sie werden ausgegraut angezeigt).</summary>
    public IReadOnlyList<ITextToSpeechService> SpeechServices => _speech.Services;

    public SpeechProviderType SelectedProvider => _speech.SelectedProvider;

    public ITextToSpeechService? CurrentSpeechService => _speech.CurrentService;

    public SpeechAvailability AvailabilityOf(SpeechProviderType provider) => _speech.AvailabilityOf(provider);

    public SpeechAvailability CurrentAvailability => _speech.CurrentAvailability;

    /// <summary>Auswählbar ist ein Anbieter, der verfügbar ist oder noch geprüft wird (der aktuelle bleibt immer gewählt).</summary>
    public bool CanSelectProvider(SpeechProviderType provider) => _speech.CanSelectProvider(provider);

    public bool ShowPrivacyHint => _speech.ShowPrivacyHint;

    /// <summary>Stimmen der Systemstimme (deutsche und Offline-Stimmen zuerst).</summary>
    public IReadOnlyList<SpeechVoice> SystemVoices => _speech.SystemVoices;

    /// <summary>Prüft alle Anbieter (vor dem Abspielen/Erzeugen, damit Knöpfe vorab deaktiviert werden können).</summary>
    public async Task RefreshAvailabilityAsync()
    {
        await _speech.RefreshAvailabilityAsync();
        OnSpeechChanged();
        await RefreshMp3AvailabilityAsync();
    }

    public async Task SetProviderAsync(SpeechProviderType provider)
    {
        if (provider == SelectedProvider || !CanSelectProvider(provider)) return;

        await StopAsync();
        try
        {
            await _speech.SetProviderAsync(provider);
        }
        catch (Exception ex)
        {
            ShowSaveError(ex);
        }
        OnSpeechChanged();
    }

    /// <summary>Stimme der Systemstimme für Sprecher 1 (0) bzw. 2 (1); leer = automatisch.</summary>
    public async Task SetSystemVoiceAsync(int speakerIndex, string voiceId)
    {
        try
        {
            await _speech.SetSystemVoiceAsync(speakerIndex, voiceId);
        }
        catch (Exception ex)
        {
            ShowSaveError(ex);
        }
        OnSpeechChanged();
        await RestartCurrentSegmentIfPlayingAsync();
    }

    public double Rate => _speech.Settings.Rate;

    public async Task SetRateAsync(double rate)
    {
        var clamped = Math.Round(SpeechSettings.ClampRate(rate), 2);
        if (Math.Abs(clamped - _speechSettings.Rate) < 0.001) return;
        try
        {
            await _speech.SetRateAsync(clamped);
        }
        catch (Exception ex)
        {
            ShowSaveError(ex);
        }
        OnPropertyChanged(nameof(Rate));
        await RestartCurrentSegmentIfPlayingAsync();
    }

    private void ShowSaveError(Exception ex) =>
        ShowStatus(Loc.Format(VmStrings.Media_SettingSaveFailed, ("error", ex.Message)), isError: true);

    /// <summary>Sprecherrollen des gewählten Beitrags (bzw. des Formulars): Podcast „Moderatorin“/„Experte“, sonst „Sprecherin“.</summary>
    public IReadOnlyList<string> SpeakerRoles => AudioScriptFormats.Speakers(SelectedSummary?.Format ?? Format);

    public string SpeakerRoleName(int speakerIndex) => SpeechProviderState.RoleName(SpeakerRoles, speakerIndex);

    /// <summary>
    /// Gewählte Stimme eines Sprechers (bei automatischer Wahl die tatsächlich verwendete): passend zur Rolle
    /// (Moderatorin/Sprecherin weiblich, Experte männlich), soweit installiert; Sprecher 2 möglichst andere Stimme.
    /// </summary>
    public string VoiceForSpeakerIndex(int speakerIndex) => _speech.VoiceForSpeakerIndex(speakerIndex, SpeakerRoles);

    /// <summary>
    /// Hinweis zur automatischen Stimme des zweiten Sprechers (Podcast), wenn keine passende zweite deutsche Stimme
    /// installiert ist – samt Installationsweg unter Windows. Bei manueller Auswahl kein Hinweis.
    /// </summary>
    public string? VoiceAssignmentHint => _speech.VoiceAssignmentHint(SpeakerRoles);

    /// <summary>Sprecher-Platz (0/1) eines Segments (siehe <see cref="AudioScriptFormats.SpeakerSlot"/>).</summary>
    public static int SpeakerSlot(AudioSummary summary, string speaker) => AudioScriptFormats.SpeakerSlot(summary, speaker);

    private string VoiceForSpeaker(AudioSummary summary, string speaker) => VoiceForSpeakerIndex(SpeakerSlot(summary, speaker));

    // ---------------- Freigabe der Knöpfe ----------------

    public bool HasScript => SelectedSummary is { Segments.Count: > 0 } && !IsFormOpen;

    public bool CanGenerateScript => !IsBusy;

    /// <summary>Abspielen über die gewählte Sprachausgabe (nur Anbieter, die direkt vorlesen).</summary>
    public bool CanPlay => PlayDisabledReason == null;

    /// <summary>Warum „Abspielen“ deaktiviert ist (null = freigegeben).</summary>
    public string? PlayDisabledReason
    {
        get
        {
            if (!HasScript) return VmStrings.Audio_NoScript;
            if (IsBusy) return VmStrings.Media_Busy;
            var service = CurrentSpeechService;
            if (service == null) return VmStrings.Media_SpeechUnavailable;
            if (!service.SupportsLivePlayback)
            {
                // Erzeugte Audiodatei abspielen (dafür ist kein Schlüssel nötig)
                if (SelectedAudioFilePath == null)
                    return Loc.Format(VmStrings.Audio_NoLivePlayback, ("provider", MediaTexts.ProviderName(service.Provider)), ("button", VmStrings.Audio_GenerateFile));
                if (_filePlayer == null)
                    return VmStrings.Media_PlayerUnavailable;
                return _filePlayer.IsReady ? null : VmStrings.Media_PlayerLoading;
            }
            return SpeechProviderState.AvailabilityReason(CurrentAvailability);
        }
    }

    /// <summary>Audiodatei erzeugen (nur Anbieter mit Dateiexport).</summary>
    public bool CanGenerateAudio => GenerateAudioDisabledReason == null;

    public string? GenerateAudioDisabledReason
    {
        get
        {
            if (!HasScript) return VmStrings.Audio_NoScript;
            if (IsBusy) return VmStrings.Media_Busy;
            var service = CurrentSpeechService;
            if (service == null) return VmStrings.Media_SpeechUnavailable;
            if (!service.SupportsFileExport || service is not IAudioFileSpeechService) return NoFileExportHint;
            return SpeechProviderState.AvailabilityReason(CurrentAvailability);
        }
    }

    public bool CanExportScript => HasScript;

    // ---------------- Player ----------------

    private int _currentSegmentIndex = -1;
    /// <summary>Gerade gesprochenes (bzw. als Nächstes zu sprechendes) Segment; -1 = keines.</summary>
    public int CurrentSegmentIndex => _currentSegmentIndex;

    private bool _isPlaying;
    public bool IsPlaying => _isPlaying;

    private bool _isPaused;
    public bool IsPaused => _isPaused;

    public int SegmentCount => SelectedSummary?.Segments.Count ?? 0;

    /// <summary>Fortschritt 0…1 nach Zeichen bis zum aktuellen Segment (bei der Audiodatei nach Abspielposition).</summary>
    public double Progress
    {
        get
        {
            if (UsesFilePlayback && SelectedSummary is { AudioDurationSeconds: > 0 } withFile && (_isPlaying || _filePosition > 0))
                return Math.Clamp(_filePosition / withFile.AudioDurationSeconds, 0, 1);

            var summary = SelectedSummary;
            if (summary == null || summary.Segments.Count == 0 || _currentSegmentIndex < 0) return 0;
            var total = Math.Max(1, summary.CharacterCount);
            var before = summary.Segments.Take(_currentSegmentIndex).Sum(s => s.Text.Length);
            return Math.Clamp(before / (double)total, 0, 1);
        }
    }

    public string ProgressText
    {
        get
        {
            var summary = SelectedSummary;
            if (summary == null || summary.Segments.Count == 0) return string.Empty;
            if (UsesFilePlayback && summary.AudioDurationSeconds > 0)
            {
                var segmentInfo = _currentSegmentIndex < 0
                    ? Loc.Format(VmStrings.Audio_SegmentCount, ("count", summary.Segments.Count))
                    : Loc.Format(VmStrings.Audio_SegmentPosition, ("number", _currentSegmentIndex + 1), ("total", summary.Segments.Count));
                return Loc.Format(VmStrings.Audio_ProgressFile, ("position", FormatClock(_filePosition)), ("duration", FormatClock(summary.AudioDurationSeconds)), ("segments", segmentInfo));
            }
            var position = _currentSegmentIndex < 0 ? 0 : _currentSegmentIndex;
            var remainingWords = summary.Segments.Skip(position).Sum(s => AudioScriptFormats.CountWords(s.Text));
            var minutes = AudioScriptFormats.EstimateMinutes(remainingWords) / Math.Max(SpeechSettings.MinRate, Rate);
            return _currentSegmentIndex < 0
                ? Loc.Format(VmStrings.Audio_ProgressLive, ("segments", Loc.Format(VmStrings.Audio_SegmentCount, ("count", summary.Segments.Count))), ("minutes", MediaTexts.Minutes(minutes)))
                : Loc.Format(VmStrings.Audio_ProgressLiveRemaining,
                    ("segments", Loc.Format(VmStrings.Audio_SegmentPosition, ("number", position + 1), ("total", summary.Segments.Count))),
                    ("minutes", MediaTexts.Minutes(minutes)));
        }
    }

    /// <summary>Titel des gerade vorgelesenen Beitrags für die Statusleiste (null = keine Wiedergabe).</summary>
    public string? NowPlayingTitle => _isPlaying ? _playingTitle : null;
    private string? _playingTitle;

    public bool CanGoPrevious => HasScript && _currentSegmentIndex > 0;
    public bool CanGoNext => HasScript && _currentSegmentIndex < SegmentCount - 1;

    /// <summary>Abspielen bzw. nach einer Pause fortsetzen.</summary>
    public async Task PlayAsync()
    {
        if (!CanPlay)
        {
            if (PlayDisabledReason is { } reason)
                ShowStatus($"⚠️ {reason}", isError: false, isWarning: true);
            return;
        }

        // Läuft gerade ein Video, wird es beendet (Player und Systemstimme gibt es nur einmal)
        if (!_isPlaying && _coordinator != null)
            await _coordinator.StopOthersAsync(PlaybackCoordinator.AudioParticipant);

        if (UsesFilePlayback)
        {
            await PlayFileAsync();
            return;
        }

        var live = (ILiveSpeechService)CurrentSpeechService!;
        if (_isPlaying && _isPaused)
        {
            await live.ResumeAsync();
            _isPaused = false;
            OnPlayerChanged();
            return;
        }

        if (_isPlaying) return;

        if (_currentSegmentIndex < 0 || _currentSegmentIndex >= SegmentCount)
            _currentSegmentIndex = 0;
        await RunPlaybackAsync(live);
    }

    public async Task PauseAsync()
    {
        if (!_isPlaying || _isPaused) return;
        if (_fileMode)
        {
            await _filePlayer!.PauseAsync();
            _isPaused = true;
            OnPlayerChanged();
            return;
        }

        if (CurrentSpeechService is not ILiveSpeechService live) return;
        await live.PauseAsync();
        _isPaused = true;
        OnPlayerChanged();
    }

    public Task TogglePlayPauseAsync() => _isPlaying && !_isPaused ? PauseAsync() : PlayAsync();

    /// <summary>Beendet die Wiedergabe; die Position bleibt erhalten.</summary>
    public async Task StopAsync()
    {
        var wasPlaying = _isPlaying;
        var wasFile = _fileMode;
        _playGeneration++;
        _isPlaying = false;
        _isPaused = false;
        _fileMode = false;
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

    public Task NextAsync() => GoToSegmentAsync(_currentSegmentIndex + 1);
    public Task PreviousAsync() => GoToSegmentAsync(_currentSegmentIndex - 1);

    /// <summary>Springt zu einem Segment; läuft die Wiedergabe, geht sie dort weiter.</summary>
    public async Task GoToSegmentAsync(int index)
    {
        if (!HasScript || index < 0 || index >= SegmentCount) return;

        if (UsesFilePlayback)
        {
            _currentSegmentIndex = index;
            _filePosition = SegmentStart(index);
            if (_isPlaying && _fileMode)
                await _filePlayer!.PlayAsync(SelectedAudioFilePath!, _filePosition, Rate);
            _isPaused = false;
            OnPlayerChanged();
            return;
        }

        var resume = _isPlaying;
        await StopAsync();
        _currentSegmentIndex = index;
        OnPlayerChanged();

        if (resume && CanPlay)
            await RunPlaybackAsync((ILiveSpeechService)CurrentSpeechService!);
    }

    private async Task RestartCurrentSegmentIfPlayingAsync()
    {
        if (_fileMode)
        {
            if (_filePlayer != null)
                await _filePlayer.SetRateAsync(Rate);
            return;
        }

        if (_isPlaying && !_isPaused)
            await GoToSegmentAsync(_currentSegmentIndex);
    }

    // ---------------- Wiedergabe der Audiodatei ----------------

    private bool _fileMode;
    private double _filePosition;

    /// <summary>Pfad der Audiodatei des gewählten Beitrags (null = keine bzw. nicht vorhanden).</summary>
    public string? SelectedAudioFilePath =>
        SelectedSummary is { HasAudioFile: true } summary ? _audioService.GetAudioFilePath(_profileName, summary) : null;

    /// <summary>
    /// Abspielen über die Audiodatei: wenn die gewählte Sprachausgabe nicht direkt vorliest (Gemini TTS) und für den
    /// Beitrag eine Datei vorhanden ist. Mit der Systemstimme wird immer live vorgelesen.
    /// </summary>
    public bool UsesFilePlayback => CurrentSpeechService is { SupportsLivePlayback: false } && SelectedAudioFilePath != null;

    private double SegmentStart(int index)
    {
        var starts = SelectedSummary?.AudioSegmentStarts;
        if (starts == null || starts.Count == 0 || index < 0) return 0;
        return starts[Math.Min(index, starts.Count - 1)];
    }

    private async Task PlayFileAsync()
    {
        if (_isPlaying && _isPaused)
        {
            await _filePlayer!.ResumeAsync();
            _isPaused = false;
            OnPlayerChanged();
            return;
        }

        if (_isPlaying) return;

        var start = _currentSegmentIndex > 0 ? SegmentStart(_currentSegmentIndex) : 0;
        if (_currentSegmentIndex < 0) _currentSegmentIndex = 0;
        _playGeneration++;
        _fileMode = true;
        _isPlaying = true;
        _isPaused = false;
        _filePosition = start;
        _playingTitle = SelectedSummary!.Title;
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

    /// <summary>Position der Audiodatei (vom Player): Hervorhebung nachführen, am Ende zurücksetzen.</summary>
    private void OnFilePositionChanged(double seconds, bool ended)
    {
        if (!_fileMode) return;

        if (ended)
        {
            _isPlaying = false;
            _isPaused = false;
            _fileMode = false;
            _filePosition = 0;
            _currentSegmentIndex = -1;
            OnPlayerChanged();
            return;
        }

        _filePosition = seconds;
        var starts = SelectedSummary?.AudioSegmentStarts ?? new List<double>();
        var index = 0;
        for (var i = 0; i < starts.Count && i < SegmentCount; i++)
        {
            if (starts[i] <= seconds + 0.05) index = i;
            else break;
        }

        _currentSegmentIndex = index;
        OnPlayerChanged();
    }

    private static string FormatClock(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, Math.Round(seconds)));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }

    // ---------------- Audiodatei erzeugen ----------------

    /// <summary>Schätzung vor dem Erzeugen (nur Gemini TTS): Dauer, Dateigröße, Kosten.</summary>
    public GeminiTtsEstimate? AudioEstimate =>
        SelectedSummary is { } summary && SelectedProvider == SpeechProviderType.GeminiTts
            ? GeminiTtsInfo.Estimate(summary.WordCount, summary.CharacterCount, DateTime.Today)
            : null;

    /// <summary>„ca. 8 Min. Audio · ca. 23 MB (WAV) · geschätzt 0,11 US-Dollar …“</summary>
    public string? AudioEstimateText
    {
        get
        {
            return AudioEstimate is { } e ? MediaTexts.GeminiEstimate(e) : null;
        }
    }

    private bool _isGeneratingAudio;
    public bool IsGeneratingAudio { get => _isGeneratingAudio; private set { _isGeneratingAudio = value; OnPropertyChanged(); } }

    private double _audioProgress;
    /// <summary>Fortschritt der Erzeugung 0…1.</summary>
    public double AudioProgress { get => _audioProgress; private set { _audioProgress = value; OnPropertyChanged(); } }

    private string _audioProgressText = string.Empty;
    public string AudioProgressText { get => _audioProgressText; private set { _audioProgressText = value; OnPropertyChanged(); } }

    public void CancelAudioGeneration() => _audioCts?.Cancel();

    /// <summary>
    /// Erzeugt die Audiodatei (WAV) mit der gewählten Sprachausgabe. Fortschritt je Teil; bei Abbruch oder Fehler bleibt
    /// keine halbe Datei zurück und eine vorhandene Audiodatei unverändert.
    /// </summary>
    public async Task GenerateAudioAsync()
    {
        if (!CanGenerateAudio)
        {
            if (GenerateAudioDisabledReason is { } reason)
                ShowStatus($"⚠️ {reason}", isError: false, isWarning: true);
            return;
        }

        var summary = SelectedSummary!;
        var service = (IAudioFileSpeechService)CurrentSpeechService!;
        await StopAsync();

        IsBusy = true;
        IsGeneratingAudio = true;
        AudioProgress = 0;
        AudioProgressText = VmStrings.Audio_FilePreparing;
        ClearStatus();
        _audioCts = new CancellationTokenSource();
        var token = _audioCts.Token;

        try
        {
            var path = _audioService.GetTargetAudioFilePath(_profileName, summary);
            var progress = new SynchronousProgress<SpeechSynthesisProgress>(p =>
            {
                AudioProgress = p.Fraction;
                AudioProgressText = p.CompletedParts < p.TotalParts
                    ? Loc.Format(VmStrings.Audio_FilePart, ("number", p.CompletedParts + 1), ("total", p.TotalParts))
                    : VmStrings.Audio_FileSaving;
            });

            var oldPath = SelectedAudioFilePath;
            var result = await service.SynthesizeToFileAsync(summary, _speech.Context, path, progress, token);

            if (oldPath != null && !string.Equals(Path.GetFullPath(oldPath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                _audioService.DeleteAudioFile(_profileName, summary);
            summary.AudioFileName = Path.GetFileName(path);
            summary.AudioDurationSeconds = result.DurationSeconds;
            summary.AudioSegmentStarts = result.SegmentStartSeconds.ToList();
            summary.AudioCreatedAt = DateTime.Now;
            summary.AudioScriptHash = summary.ComputeScriptHash();
            summary.AudioCreatedWith = result.CreatedWith;
            await SaveAsync();

            _currentSegmentIndex = -1;
            _filePosition = 0;
            var size = File.Exists(path) ? new FileInfo(path).Length / 1_000_000.0 : 0;
            ShowStatus(Loc.Format(VmStrings.Audio_FileCreated, ("duration", FormatClock(result.DurationSeconds)), ("size", size.ToString("0.0", CultureInfo.CurrentCulture))), isError: false);
        }
        catch (OperationCanceledException)
        {
            ShowStatus(VmStrings.Audio_FileCancelled, isError: false);
        }
        catch (LlmServiceException ex)
        {
            ShowStatus($"⚠️ {ex.Message}", isError: true,
                new[] { VmStrings.Audio_FileUnchangedTip }, ex.Detail);
        }
        catch (Exception ex)
        {
            ShowStatus(Loc.Format(VmStrings.Audio_FileFailed, ("error", ex.Message)), isError: true,
                new[] { VmStrings.Audio_FileUnchangedTip });
        }
        finally
        {
            _audioCts?.Dispose();
            _audioCts = null;
            IsGeneratingAudio = false;
            AudioProgress = 0;
            AudioProgressText = string.Empty;
            IsBusy = false;
            OnScriptChanged();
        }
    }

    /// <summary>Meldet Fortschritt sofort (ohne SynchronizationContext-Umweg wie bei Progress&lt;T&gt;).</summary>
    private sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private bool _isTestingConnection;
    public bool IsTestingConnection { get => _isTestingConnection; private set { _isTestingConnection = value; OnPropertyChanged(); } }

    /// <summary>Prüft Schlüssel und Modell der Sprachausgabe ohne Kosten (nur Anbieter mit Dateiexport).</summary>
    public async Task TestSpeechConnectionAsync()
    {
        if (CurrentSpeechService is not IAudioFileSpeechService service || IsTestingConnection) return;
        IsTestingConnection = true;
        try
        {
            var message = await service.TestConnectionAsync(_speech.Context);
            ShowStatus(message, isError: false);
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

    /// <summary>Löscht nur die Audiodatei des gewählten Beitrags (das Skript bleibt).</summary>
    public async Task DeleteAudioFileAsync()
    {
        var summary = SelectedSummary;
        if (summary is not { HasAudioFile: true }) return;
        await StopAsync();
        _audioService.DeleteAudioFile(_profileName, summary);
        if (summary.HasAudioFile)
        {
            ShowStatus(VmStrings.Audio_FileDeleteFailed, isError: true);
            return;
        }
        await SaveAsync();
        OnScriptChanged();
        ShowStatus(VmStrings.Audio_FileDeleted, isError: false);
    }

    public bool CanExportAudio => SelectedAudioFilePath != null && !IsBusy;

    public async Task<bool> ExportAudioAsync(string filePath)
    {
        var source = SelectedAudioFilePath;
        if (source == null) return false;
        try
        {
            await using (var input = File.OpenRead(source))
            await using (var output = File.Create(filePath))
                await input.CopyToAsync(output);
            ShowStatus(Loc.Format(VmStrings.Audio_FileExported, ("file", Path.GetFileName(filePath))), isError: false);
            return true;
        }
        catch (Exception ex)
        {
            ShowStatus(Loc.Format(VmStrings.Audio_FileExportFailed, ("error", ex.Message)), isError: true,
                new[] { VmStrings.Media_FileOpenTip });
            return false;
        }
    }

    private async Task RunPlaybackAsync(ILiveSpeechService live)
    {
        var summary = SelectedSummary!;
        var generation = ++_playGeneration;
        _playingTitle = summary.Title;
        _isPlaying = true;
        _isPaused = false;
        OnPlayerChanged();

        try
        {
            while (generation == _playGeneration && _currentSegmentIndex >= 0 && _currentSegmentIndex < summary.Segments.Count)
            {
                var segment = summary.Segments[_currentSegmentIndex];
                var voice = VoiceForSpeaker(summary, segment.Speaker);

                foreach (var piece in SystemVoiceRules.SplitForSpeech(segment.Text))
                {
                    var completed = await live.SpeakAsync(piece, voice, Rate);
                    if (!completed || generation != _playGeneration)
                        return;
                }

                if (generation != _playGeneration)
                    return;

                if (_currentSegmentIndex >= summary.Segments.Count - 1)
                {
                    // Ende erreicht: zurück an den Anfang, ohne Hervorhebung
                    _currentSegmentIndex = -1;
                    break;
                }

                _currentSegmentIndex++;
                OnPlayerChanged();
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

    // ---------------- Skript erstellen ----------------

    public async Task GenerateAsync(IEnumerable<DocumentItem> documents)
    {
        if (IsBusy) return;

        var selectedDocs = documents.Where(d => d.IsSelected && !d.IsMissing).ToList();
        if (selectedDocs.Count == 0)
        {
            ShowStatus(VmStrings.Audio_SelectDocument, isError: true);
            return;
        }

        await StopAsync();
        IsBusy = true;
        ClearStatus();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            var chunks = new List<SourceChunk>();
            foreach (var doc in selectedDocs)
            {
                foreach (var text in await _documentService.ExtractChunksAsync(doc.FilePath))
                    chunks.Add(new SourceChunk(doc.FileName, text));
            }

            var fallbackTitle = selectedDocs.Count == 1
                ? $"{AudioScriptFormats.DisplayName(Format)}: {Path.GetFileNameWithoutExtension(selectedDocs[0].FileName)}"
                : $"{AudioScriptFormats.DisplayName(Format)}: {_profileName}";

            var result = await _audioService.GenerateAsync(chunks, Format, Length, Topic, fallbackTitle, token);
            if (token.IsCancellationRequested)
            {
                ShowStatus(VmStrings.Audio_ScriptCancelled, isError: false);
                return;
            }

            if (!result.IsSuccess)
            {
                var failure = MindmapStatusTexts.ForFailure(result.FailureReason, result.RawResponse, GenerationSubject.AudioScript, VmStrings.Audio_GenerateScript);
                ShowStatus(failure.Message, isError: true, failure.Tips, failure.Details);
                return;
            }

            var summary = result.Summary!;
            if (!string.IsNullOrWhiteSpace(CustomTitle))
                summary.Title = CustomTitle.Trim();
            summary.CreatedAt = DateTime.Now;
            if (summary.SourceDocuments.Count == 0)
                summary.SourceDocuments = selectedDocs.Select(d => d.FileName).ToList();

            _library.Items.Add(summary);
            await SaveAsync();

            CustomTitle = string.Empty;
            IsFormOpen = false;
            OnPropertyChanged(nameof(Items));
            SelectedSummary = summary;

            var info = Loc.Format(VmStrings.Audio_ScriptInfo,
                ("segments", Loc.Format(VmStrings.Audio_SegmentCount, ("count", summary.Segments.Count))),
                ("minutes", MediaTexts.Minutes(summary.EstimatedMinutes)));
            if (result.WasRepaired)
                ShowStatus(Loc.Format(VmStrings.Audio_ScriptCreatedRepaired, ("info", info)), isError: false,
                    new[] { VmStrings.Audio_TipEditScript, VmStrings.Audio_TipShorter },
                    isWarning: true);
            else if (result.WasPlainText)
                ShowStatus(Loc.Format(VmStrings.Audio_ScriptCreatedPlain, ("info", info)), isError: false,
                    new[] { VmStrings.Audio_TipCheckSpeakers }, isWarning: true);
            else
                ShowStatus(Loc.Format(VmStrings.Audio_ScriptCreated, ("title", summary.Title), ("info", info)), isError: false);
        }
        catch (OperationCanceledException)
        {
            ShowStatus(VmStrings.Audio_ScriptCancelled, isError: false);
        }
        catch (Exception ex)
        {
            ShowStatus(Loc.Format(VmStrings.Audio_ScriptFailed, ("error", ex.Message)), isError: true);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsBusy = false;
        }
    }

    // ---------------- Beiträge verwalten und bearbeiten ----------------

    public async Task<bool> RenameAsync(string id, string newTitle)
    {
        var item = _library.Items.FirstOrDefault(i => i.Id == id);
        var title = (newTitle ?? string.Empty).Trim();
        if (item == null || title.Length == 0) return false;

        item.Title = title;
        await SaveAsync();
        OnPropertyChanged(nameof(Items));
        if (SelectedSummary?.Id == id) OnPropertyChanged(nameof(SelectedSummary));
        return true;
    }

    /// <summary>Löscht einen Beitrag samt seiner Audiodatei.</summary>
    public async Task DeleteAsync(string id)
    {
        var item = _library.Items.FirstOrDefault(i => i.Id == id);
        if (item == null) return;

        if (SelectedSummary?.Id == id)
            await StopAsync();

        _audioService.DeleteAudioFile(_profileName, item);
        _library.Items.Remove(item);
        await SaveAsync();
        OnPropertyChanged(nameof(Items));

        if (SelectedSummary?.Id == id)
            SelectedSummary = Items.FirstOrDefault();
        if (_library.Items.Count == 0)
            IsFormOpen = true;

        ShowStatus(Loc.Format(VmStrings.Audio_Deleted, ("title", item.Title)), isError: false);
    }

    public async Task<bool> SetSegmentTextAsync(int index, string text)
    {
        var summary = SelectedSummary;
        if (summary == null || index < 0 || index >= summary.Segments.Count) return false;

        var value = (text ?? string.Empty).Replace("\r", string.Empty).Trim();
        if (value.Length == 0)
        {
            await DeleteSegmentAsync(index);
            return true;
        }

        if (value.Length > AudioSummary.MaxSegmentLength)
            value = value[..AudioSummary.MaxSegmentLength];
        if (summary.Segments[index].Text == value) return true;

        summary.Segments[index].Text = value;
        await SaveAsync();
        OnScriptChanged();
        return true;
    }

    public async Task<bool> SetSegmentSpeakerAsync(int index, string speaker)
    {
        var summary = SelectedSummary;
        var name = (speaker ?? string.Empty).Trim();
        if (summary == null || index < 0 || index >= summary.Segments.Count || name.Length == 0) return false;
        if (summary.Segments[index].Speaker == name) return true;

        summary.Segments[index].Speaker = name.Length > 60 ? name[..60] : name;
        await SaveAsync();
        OnScriptChanged();
        return true;
    }

    /// <summary>Sprecher, die beim Bearbeiten zur Auswahl stehen (Format-Sprecher und vorhandene).</summary>
    public IReadOnlyList<string> SpeakerChoices =>
        SelectedSummary == null
            ? Array.Empty<string>()
            : AudioScriptFormats.Speakers(SelectedSummary.Format).Concat(SelectedSummary.Speakers)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Fügt nach <paramref name="index"/> ein neues Segment ein (-1 = am Anfang) und liefert seinen Index.</summary>
    public async Task<int> AddSegmentAfterAsync(int index)
    {
        var summary = SelectedSummary;
        if (summary == null || summary.Segments.Count >= AudioSummary.MaxSegments) return -1;

        var position = Math.Clamp(index + 1, 0, summary.Segments.Count);
        var speakers = AudioScriptFormats.Speakers(summary.Format);
        var previous = position > 0 ? summary.Segments[position - 1].Speaker : string.Empty;
        var speaker = speakers.Count > 1 ? speakers.FirstOrDefault(s => s != previous) ?? speakers[0] : speakers[0];

        await StopAsync();
        summary.Segments.Insert(position, new AudioSegment { Speaker = speaker, Text = VmStrings.Audio_NewSegmentText });
        await SaveAsync();
        OnScriptChanged();
        return position;
    }

    public async Task DeleteSegmentAsync(int index)
    {
        var summary = SelectedSummary;
        if (summary == null || index < 0 || index >= summary.Segments.Count) return;

        await StopAsync();
        summary.Segments.RemoveAt(index);
        if (_currentSegmentIndex >= summary.Segments.Count)
            _currentSegmentIndex = summary.Segments.Count - 1;
        await SaveAsync();
        OnScriptChanged();
    }

    private async Task SaveAsync()
    {
        try
        {
            await _audioService.SaveLibraryAsync(_profileName, _library);
        }
        catch (Exception ex)
        {
            ShowStatus(Loc.Format(VmStrings.Audio_SaveFailed, ("error", ex.Message)), isError: true);
        }
    }

    // ---------------- Export ----------------

    public static string Extension(AudioScriptExportFormat format) => format == AudioScriptExportFormat.Markdown ? "md" : "txt";

    public static string FormatDescription(AudioScriptExportFormat format) =>
        format == AudioScriptExportFormat.Markdown ? VmStrings.Media_FormatMarkdown : VmStrings.Media_FormatText;

    public string ExportScript(AudioScriptExportFormat format)
    {
        var summary = SelectedSummary;
        if (summary == null) return string.Empty;
        return format == AudioScriptExportFormat.Markdown ? _audioService.ExportToMarkdown(summary) : _audioService.ExportToText(summary);
    }

    public async Task<bool> ExportScriptAsync(AudioScriptExportFormat format, string filePath)
    {
        if (SelectedSummary == null) return false;
        try
        {
            await File.WriteAllTextAsync(filePath, ExportScript(format));
            ShowStatus(Loc.Format(VmStrings.Audio_ScriptExported, ("format", FormatDescription(format)), ("file", Path.GetFileName(filePath))), isError: false);
            return true;
        }
        catch (Exception ex)
        {
            ShowStatus(Loc.Format(VmStrings.Media_ExportFailed, ("error", ex.Message)), isError: true,
                new[] { VmStrings.Media_FileOpenTip });
            return false;
        }
    }

    public string SuggestFileName(string extension)
    {
        var name = string.IsNullOrWhiteSpace(SelectedSummary?.Title) ? "Audio-Zusammenfassung" : SelectedSummary!.Title;
        foreach (var c in Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }))
            name = name.Replace(c, '_');
        name = name.Length > 60 ? name[..60] : name;
        return $"{name.Trim()}_{DateTime.Now:yyyyMMdd}.{extension}";
    }

    // ---------------- Benachrichtigungen ----------------

    private void OnScriptChanged()
    {
        OnPropertyChanged(nameof(SelectedSummary));
        OnPlayerChanged();
    }

    private void OnSpeechChanged()
    {
        OnPropertyChanged(nameof(SelectedProvider));
        OnPropertyChanged(nameof(CurrentSpeechService));
        OnPropertyChanged(nameof(CurrentAvailability));
        OnPropertyChanged(nameof(ShowPrivacyHint));
        OnPropertyChanged(nameof(IsLocalChatAi));
        OnPropertyChanged(nameof(Rate));
        OnPropertyChanged(nameof(VoiceAssignmentHint));
        OnPropertyChanged(nameof(SystemVoices));
        OnPlayerChanged();
    }

    private void OnPlayerChanged()
    {
        OnPropertyChanged(nameof(CurrentSegmentIndex));
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(NowPlayingTitle));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(PlayDisabledReason));
        OnPropertyChanged(nameof(CanGenerateAudio));
        OnPropertyChanged(nameof(GenerateAudioDisabledReason));
        OnPropertyChanged(nameof(HasScript));
        OnPropertyChanged(nameof(UsesFilePlayback));
        OnPropertyChanged(nameof(CanExportAudio));
        OnPropertyChanged(nameof(AudioEstimateText));
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
