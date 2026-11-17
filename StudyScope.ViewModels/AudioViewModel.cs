using System.ComponentModel;
using System.Runtime.CompilerServices;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
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
public class AudioViewModel : INotifyPropertyChanged
{
    public const string LocalAiHint =
        "Das Skript schreibt Ihre lokale KI. Zum Vorlesen nutzen Sie die Systemstimme (offline) oder Gemini TTS (Audiodatei, Podcast mit zwei Stimmen).";

    public const string GeminiPrivacyHint =
        "Datenschutz: Bei Gemini TTS wird der Skripttext an Google gesendet. In der kostenlosen Stufe kann Google die Eingaben zur Verbesserung seiner Dienste verwenden.";

    public const string NoFileExportHint =
        "Mit der Systemstimme ist kein Dateiexport möglich – sie liest nur direkt vor. Für eine Audiodatei (WAV) wählen Sie Gemini TTS.";

    private readonly IAudioScriptService _audioService;
    private readonly IDocumentService _documentService;
    private readonly IStorageService _storage;
    private readonly IReadOnlyList<ITextToSpeechService> _speechServices;
    private readonly Dictionary<SpeechProviderType, SpeechAvailability> _availability = new();
    private CancellationTokenSource? _cts;
    private string _profileName = string.Empty;
    private AudioLibrary _library = new();
    private SpeechSettings _speechSettings = new();
    private AiConfig? _chatConfig;
    private int _playGeneration;

    public event PropertyChangedEventHandler? PropertyChanged;

    public AudioViewModel(IAudioScriptService audioService, IDocumentService documentService, IStorageService storage,
        IEnumerable<ITextToSpeechService> speechServices)
    {
        _audioService = audioService;
        _documentService = documentService;
        _storage = storage;
        _speechServices = speechServices.ToList();
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
        _speechSettings = await _storage.LoadSpeechSettingsAsync(profileName) ?? new SpeechSettings();
        _chatConfig = await _storage.LoadAiConfigForProfileAsync(profileName) ?? await _storage.LoadAiConfigAsync();

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
            _speechSettings = await _storage.LoadSpeechSettingsAsync(profileName) ?? _speechSettings;
            _chatConfig = await _storage.LoadAiConfigForProfileAsync(profileName) ?? await _storage.LoadAiConfigAsync();
            await RefreshAvailabilityAsync();
            return;
        }

        await LoadForProfileAsync(profileName);
    }

    private bool _isLoaded;

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
    public bool IsLocalChatAi => _chatConfig?.SelectedProvider is AiProviderType.OllamaLocal or AiProviderType.LmStudioLocal;

    public SpeechSettings SpeechSettings => _speechSettings;

    /// <summary>Alle Anbieter der Sprachausgabe (auch nicht verfügbare – sie werden ausgegraut angezeigt).</summary>
    public IReadOnlyList<ITextToSpeechService> SpeechServices => _speechServices;

    public SpeechProviderType SelectedProvider => _speechSettings.Provider;

    public ITextToSpeechService? CurrentSpeechService => _speechServices.FirstOrDefault(s => s.Provider == SelectedProvider);

    public SpeechAvailability AvailabilityOf(SpeechProviderType provider) =>
        _availability.TryGetValue(provider, out var a)
            ? a
            : _speechServices.Any(s => s.Provider == provider)
                ? SpeechAvailability.Checking()
                : SpeechAvailability.Unavailable("Diese Sprachausgabe ist in dieser Version nicht enthalten.");

    public SpeechAvailability CurrentAvailability => AvailabilityOf(SelectedProvider);

    /// <summary>Auswählbar ist ein Anbieter, der verfügbar ist oder noch geprüft wird (der aktuelle bleibt immer gewählt).</summary>
    public bool CanSelectProvider(SpeechProviderType provider) =>
        provider == SelectedProvider || AvailabilityOf(provider).Status != SpeechAvailabilityStatus.Unavailable;

    public bool ShowPrivacyHint => SelectedProvider == SpeechProviderType.GeminiTts;

    private IReadOnlyList<SpeechVoice> _systemVoices = Array.Empty<SpeechVoice>();
    /// <summary>Stimmen der Systemstimme (deutsche und Offline-Stimmen zuerst).</summary>
    public IReadOnlyList<SpeechVoice> SystemVoices { get => _systemVoices; private set { _systemVoices = value; OnPropertyChanged(); } }

    /// <summary>Prüft alle Anbieter (vor dem Abspielen/Erzeugen, damit Knöpfe vorab deaktiviert werden können).</summary>
    public async Task RefreshAvailabilityAsync()
    {
        var context = new SpeechContext(_speechSettings, _chatConfig);
        foreach (var service in _speechServices)
        {
            try
            {
                _availability[service.Provider] = await service.GetAvailabilityAsync(context);
                if (service is ILiveSpeechService && service.Provider == SpeechProviderType.SystemVoice)
                    SystemVoices = await service.GetVoicesAsync();
            }
            catch (Exception ex)
            {
                _availability[service.Provider] = SpeechAvailability.Unavailable($"Prüfung fehlgeschlagen: {ex.Message}");
            }
        }

        OnSpeechChanged();
    }

    public async Task SetProviderAsync(SpeechProviderType provider)
    {
        if (provider == SelectedProvider || !CanSelectProvider(provider)) return;

        await StopAsync();
        _speechSettings.Provider = provider;
        await SaveSpeechSettingsAsync();
        OnSpeechChanged();
    }

    /// <summary>Stimme der Systemstimme für Sprecher 1 (0) bzw. 2 (1); leer = automatisch.</summary>
    public async Task SetSystemVoiceAsync(int speakerIndex, string voiceId)
    {
        if (speakerIndex == 0) _speechSettings.SystemVoicePrimary = voiceId ?? string.Empty;
        else _speechSettings.SystemVoiceSecondary = voiceId ?? string.Empty;
        await SaveSpeechSettingsAsync();
        OnSpeechChanged();
        await RestartCurrentSegmentIfPlayingAsync();
    }

    public double Rate => _speechSettings.Rate;

    public async Task SetRateAsync(double rate)
    {
        var clamped = Math.Round(SpeechSettings.ClampRate(rate), 2);
        if (Math.Abs(clamped - _speechSettings.Rate) < 0.001) return;
        _speechSettings.Rate = clamped;
        OnPropertyChanged(nameof(Rate));
        await SaveSpeechSettingsAsync();
        await RestartCurrentSegmentIfPlayingAsync();
    }

    private async Task SaveSpeechSettingsAsync()
    {
        try
        {
            await _storage.SaveSpeechSettingsAsync(_profileName, _speechSettings);
        }
        catch (Exception ex)
        {
            ShowStatus($"⚠️ Die Einstellung der Sprachausgabe konnte nicht gespeichert werden: {ex.Message}", isError: true);
        }
    }

    /// <summary>Sprecherrollen des gewählten Beitrags (bzw. des Formulars): Podcast „Moderatorin“/„Experte“, sonst „Sprecherin“.</summary>
    public IReadOnlyList<string> SpeakerRoles => AudioScriptFormats.Speakers(SelectedSummary?.Format ?? Format);

    public string SpeakerRoleName(int speakerIndex) =>
        SpeakerRoles.Count == 0 ? $"Sprecher {speakerIndex + 1}" : SpeakerRoles[Math.Clamp(speakerIndex, 0, SpeakerRoles.Count - 1)];

    /// <summary>
    /// Gewählte Stimme eines Sprechers (bei automatischer Wahl die tatsächlich verwendete): passend zur Rolle
    /// (Moderatorin/Sprecherin weiblich, Experte männlich), soweit installiert; Sprecher 2 möglichst andere Stimme.
    /// </summary>
    public string VoiceForSpeakerIndex(int speakerIndex)
    {
        var primary = SystemVoiceRules.ResolveVoice(SystemVoices, _speechSettings.SystemVoicePrimary, 0, null,
            SystemVoiceRules.SpeakerGender(SpeakerRoleName(0)));
        return speakerIndex == 0
            ? primary
            : SystemVoiceRules.ResolveVoice(SystemVoices, _speechSettings.SystemVoiceSecondary, 1, primary,
                SystemVoiceRules.SpeakerGender(SpeakerRoleName(1)));
    }

    /// <summary>
    /// Hinweis zur automatischen Stimme des zweiten Sprechers (Podcast), wenn keine passende zweite deutsche Stimme
    /// installiert ist – samt Installationsweg unter Windows. Bei manueller Auswahl kein Hinweis.
    /// </summary>
    public string? VoiceAssignmentHint
    {
        get
        {
            if (SelectedProvider != SpeechProviderType.SystemVoice || SpeakerRoles.Count < 2 || SystemVoices.Count == 0)
                return null;
            if (!string.IsNullOrWhiteSpace(_speechSettings.SystemVoiceSecondary) && SystemVoices.Any(v => v.Id == _speechSettings.SystemVoiceSecondary))
                return null;

            var role = SpeakerRoleName(1);
            return SystemVoiceRules.SecondVoiceHint(SystemVoices, VoiceForSpeakerIndex(0), VoiceForSpeakerIndex(1), role,
                SystemVoiceRules.SpeakerGender(role), OperatingSystem.IsLinux());
        }
    }

    /// <summary>Sprecher-Platz (0/1) eines Segments: nach Rolle des Formats, sonst nach Reihenfolge des Auftretens.</summary>
    public static int SpeakerSlot(AudioSummary summary, string speaker)
    {
        var roles = AudioScriptFormats.Speakers(summary.Format);
        var roleIndex = roles.ToList().FindIndex(r => string.Equals(r, speaker, StringComparison.OrdinalIgnoreCase));
        if (roleIndex >= 0)
            return roleIndex % 2;

        var index = summary.Speakers.FindIndex(s => string.Equals(s, speaker, StringComparison.OrdinalIgnoreCase));
        return index <= 0 ? 0 : index % 2;
    }

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
            if (!HasScript) return "Bitte zuerst ein Skript erstellen oder auswählen.";
            if (IsBusy) return "Bitte warten, bis das Skript fertig erstellt ist.";
            var service = CurrentSpeechService;
            if (service == null) return "Die gewählte Sprachausgabe ist nicht verfügbar.";
            if (!service.SupportsLivePlayback)
                return $"{service.DisplayName} liest nicht direkt vor – erzeugen Sie eine Audiodatei oder wählen Sie die Systemstimme.";
            return AvailabilityReason(CurrentAvailability);
        }
    }

    /// <summary>Audiodatei erzeugen (nur Anbieter mit Dateiexport).</summary>
    public bool CanGenerateAudio => GenerateAudioDisabledReason == null;

    public string? GenerateAudioDisabledReason
    {
        get
        {
            if (!HasScript) return "Bitte zuerst ein Skript erstellen oder auswählen.";
            if (IsBusy) return "Bitte warten, bis das Skript fertig erstellt ist.";
            var service = CurrentSpeechService;
            if (service == null) return "Die gewählte Sprachausgabe ist nicht verfügbar.";
            if (!service.SupportsFileExport) return NoFileExportHint;
            return AvailabilityReason(CurrentAvailability);
        }
    }

    private static string? AvailabilityReason(SpeechAvailability availability) => availability.Status switch
    {
        SpeechAvailabilityStatus.Checking => "Die Sprachausgabe wird noch geprüft …",
        SpeechAvailabilityStatus.Unavailable => string.IsNullOrWhiteSpace(availability.Hint) ? availability.Message : $"{availability.Message} {availability.Hint}",
        _ => null
    };

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

    /// <summary>Fortschritt 0…1 nach Zeichen bis zum aktuellen Segment.</summary>
    public double Progress
    {
        get
        {
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
            var position = _currentSegmentIndex < 0 ? 0 : _currentSegmentIndex;
            var remainingWords = summary.Segments.Skip(position).Sum(s => AudioScriptFormats.CountWords(s.Text));
            var minutes = AudioScriptFormats.EstimateMinutes(remainingWords) / Math.Max(SpeechSettings.MinRate, Rate);
            return _currentSegmentIndex < 0
                ? $"{summary.Segments.Count} Abschnitte · {AudioScriptFormats.FormatMinutes(minutes)}"
                : $"Abschnitt {position + 1} von {summary.Segments.Count} · noch {AudioScriptFormats.FormatMinutes(minutes)}";
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
        if (!_isPlaying || _isPaused || CurrentSpeechService is not ILiveSpeechService live) return;
        await live.PauseAsync();
        _isPaused = true;
        OnPlayerChanged();
    }

    public Task TogglePlayPauseAsync() => _isPlaying && !_isPaused ? PauseAsync() : PlayAsync();

    /// <summary>Beendet die Wiedergabe; die Position bleibt erhalten.</summary>
    public async Task StopAsync()
    {
        var wasPlaying = _isPlaying;
        _playGeneration++;
        _isPlaying = false;
        _isPaused = false;
        if (wasPlaying)
        {
            foreach (var live in _speechServices.OfType<ILiveSpeechService>())
            {
                try { await live.StopAsync(); }
                catch { /* Stopp ist immer erlaubt */ }
            }
        }
        OnPlayerChanged();
    }

    public Task NextAsync() => GoToSegmentAsync(_currentSegmentIndex + 1);
    public Task PreviousAsync() => GoToSegmentAsync(_currentSegmentIndex - 1);

    /// <summary>Springt zu einem Segment; läuft die Wiedergabe, geht sie dort weiter.</summary>
    public async Task GoToSegmentAsync(int index)
    {
        if (!HasScript || index < 0 || index >= SegmentCount) return;

        var resume = _isPlaying;
        await StopAsync();
        _currentSegmentIndex = index;
        OnPlayerChanged();

        if (resume && CanPlay)
            await RunPlaybackAsync((ILiveSpeechService)CurrentSpeechService!);
    }

    private async Task RestartCurrentSegmentIfPlayingAsync()
    {
        if (_isPlaying && !_isPaused)
            await GoToSegmentAsync(_currentSegmentIndex);
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
            ShowStatus("⚠️ Bitte wählen Sie links mindestens ein Dokument aus – daraus entsteht der Audio-Beitrag.", isError: true);
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
                ShowStatus("Die Erstellung des Skripts wurde abgebrochen.", isError: false);
                return;
            }

            if (!result.IsSuccess)
            {
                var failure = MindmapStatusTexts.ForFailure(result.FailureReason, result.RawResponse, "Audio-Skript", "✨ Skript erstellen",
                    "kein Audio-Skript", "einem Audio-Skript", "ein Audio-Skript");
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

            var info = $"{summary.Segments.Count} Abschnitte, {AudioScriptFormats.FormatMinutes(summary.EstimatedMinutes)}";
            if (result.WasRepaired)
                ShowStatus($"✓ Skript erstellt ({info}). Die Antwort der KI war allerdings unvollständig – das Ende fehlt eventuell.", isError: false,
                    new[] { "Das Skript lässt sich direkt bearbeiten und ergänzen.", "Für vollständige Antworten eine kürzere Länge wählen oder einen Schwerpunkt angeben." },
                    isWarning: true);
            else if (result.WasPlainText)
                ShowStatus($"✓ Skript erstellt ({info}). Die KI hat normalen Text geliefert; er wurde in Abschnitte aufgeteilt.", isError: false,
                    new[] { "Prüfen Sie Sprecher und Abschnitte und passen Sie sie bei Bedarf an." }, isWarning: true);
            else
                ShowStatus($"✓ Skript „{summary.Title}“ erstellt ({info}).", isError: false);
        }
        catch (OperationCanceledException)
        {
            ShowStatus("Die Erstellung des Skripts wurde abgebrochen.", isError: false);
        }
        catch (Exception ex)
        {
            ShowStatus($"⚠️ Fehler beim Erstellen des Skripts: {ex.Message}", isError: true);
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

        ShowStatus($"✓ Beitrag „{item.Title}“ gelöscht.", isError: false);
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
        summary.Segments.Insert(position, new AudioSegment { Speaker = speaker, Text = "Neuer Abschnitt." });
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
            ShowStatus($"⚠️ Speichern fehlgeschlagen: {ex.Message}", isError: true);
        }
    }

    // ---------------- Export ----------------

    public static string Extension(AudioScriptExportFormat format) => format == AudioScriptExportFormat.Markdown ? "md" : "txt";

    public static string FormatDescription(AudioScriptExportFormat format) =>
        format == AudioScriptExportFormat.Markdown ? "Markdown (*.md)" : "Textdatei (*.txt)";

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
            ShowStatus($"✓ Skript exportiert als {FormatDescription(format)}: {Path.GetFileName(filePath)}", isError: false);
            return true;
        }
        catch (Exception ex)
        {
            ShowStatus($"⚠️ Fehler beim Export: {ex.Message}", isError: true,
                new[] { "Ist die Datei noch in einem anderen Programm geöffnet? Dann bitte schließen und erneut exportieren." });
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
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
