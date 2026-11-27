using StudyScope.ViewModels.Resources;
using StudyScope.Core.Localization;
using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;

namespace StudyScope.ViewModels;

/// <summary>
/// Video-Reiter, Teil Export und Aufruf aus dem Bericht: interaktive HTML-Datei (gemeinsamer Player mit Bericht),
/// Größenschätzung vor dem Export, MP3 statt WAV, falls ein <see cref="IAudioTranscoder"/> verfügbar ist, und
/// „▶ Video abspielen“ aus dem Bericht (vorgemerkt, bis der Reiter gezeichnet ist und die Sprachausgabe bereitsteht).
/// </summary>
public partial class VideoViewModel
{
    private readonly IAudioTranscoder? _transcoder;

    // ---------------- HTML-Export ----------------

    private bool _isMp3Available;
    /// <summary>MP3-Umwandlung möglich (Transcoder registriert und z. B. ffmpeg gefunden).</summary>
    public bool IsMp3Available { get => _isMp3Available; private set { _isMp3Available = value; OnPropertyChanged(); OnPropertyChanged(nameof(HtmlExportEstimateText)); } }

    private bool _htmlUseMp3 = true;
    /// <summary>Tonspur im HTML als MP3 einbetten (nur wirksam, wenn <see cref="IsMp3Available"/>).</summary>
    public bool HtmlUseMp3 { get => _htmlUseMp3; set { _htmlUseMp3 = value; OnPropertyChanged(); OnPropertyChanged(nameof(HtmlExportEstimateText)); } }

    private bool UseMp3 => IsMp3Available && HtmlUseMp3 && _transcoder != null;

    public async Task RefreshMp3AvailabilityAsync()
    {
        if (_transcoder == null) { IsMp3Available = false; return; }
        try { IsMp3Available = await _transcoder.IsMp3AvailableAsync(); }
        catch { IsMp3Available = false; }
    }

    public bool CanExportHtml => HasVideo && !IsBusy;

    /// <summary>Das Video wird mit Ton exportiert (aktuelle Audiodatei oder vor dem Export kostenlos zusammensetzbar).</summary>
    public bool HtmlExportHasAudio =>
        SelectedVideo is { } video && (_videoService.HasCurrentAudio(_profileName, video) || NeedsOnlyAssembly);

    /// <summary>Hinweis für den Export ohne Ton (null = mit Ton).</summary>
    public string? HtmlExportNoAudioReason =>
        SelectedVideo is not { } video || HtmlExportHasAudio ? null : _videoService.NoAudioReason(_profileName, video);

    /// <summary>„ca. 12 MB · mit Ton (WAV) · 8 Folien · 3:12 Min.“</summary>
    public string? HtmlExportEstimateText
    {
        get
        {
            if (SelectedVideo is not { } video) return null;
            var mp3 = UseMp3;
            if (NeedsOnlyAssembly && !_videoService.HasCurrentAudio(_profileName, video))
            {
                // Noch nicht zusammengesetzt: Größe aus den Szenen-Dauern (WAV 24 kHz, 16 Bit, mono)
                var seconds = video.Scenes.Sum(s => s.AudioDurationSeconds) + 0.6 * Math.Max(0, video.Scenes.Count - 1);
                var raw = mp3 ? seconds * IAudioTranscoder.Mp3Kbps * 1000 / 8 : seconds * GeminiTtsInfo.WavBytesPerSecond;
                var bytes = (long)(raw * 4 / 3) + video.Slides.Count * 1200 + 40 * 1024;
                var estimate = new VideoHtmlSizeEstimate(bytes, (long)(raw * 4 / 3), 1, 1, mp3);
                return Loc.Format(VmStrings.Video_HtmlEstimateAssemble, ("size", MediaTexts.Size(estimate.Megabytes)), ("format", mp3 ? "MP3" : "WAV"));
            }

            var e = _videoService.EstimateHtml(_profileName, new[] { video }, mp3);
            return e.VideosWithAudio > 0
                ? Loc.Format(VmStrings.Video_HtmlEstimateAudio, ("size", MediaTexts.Size(e.Megabytes)), ("format", e.UsesMp3 ? "MP3" : "WAV"))
                : Loc.Format(VmStrings.Video_HtmlEstimateSilent, ("size", MediaTexts.Size(e.Megabytes)));
        }
    }

    /// <summary>
    /// Speichert das gewählte Video als eigenständige HTML-Datei (atomar über eine temporäre Datei). Fehlt nur das
    /// Zusammensetzen der Szenen-Audiodateien, geschieht das vorher (kostenlos).
    /// </summary>
    public async Task<bool> ExportHtmlAsync(string filePath)
    {
        if (!CanExportHtml || SelectedVideo is not { } video) return false;

        await StopAsync();
        IsBusy = true;
        BusyText = VmStrings.Video_HtmlBusy;
        ClearStatus();
        var temp = filePath + ".tmp";
        try
        {
            await AssembleIfNeededAsync(video);
            BusyText = VmStrings.Video_HtmlBusy;

            var mp3 = UseMp3 && _videoService.HasCurrentAudio(_profileName, video);
            if (mp3) BusyText = VmStrings.Video_HtmlMp3Busy;
            var data = await _videoService.BuildPlayerDataAsync(_profileName, video, mp3 ? _transcoder : null);
            BusyText = VmStrings.Video_HtmlWriting;
            var html = _videoService.ExportToHtml(data, ExportCulture.For(video.Language));
            await File.WriteAllTextAsync(temp, html, new System.Text.UTF8Encoding(false));
            File.Move(temp, filePath, overwrite: true);

            var size = new FileInfo(filePath).Length / 1_000_000.0;
            var sizeText = MediaTexts.SizeNumber(size);
            ShowStatus(data.HasAudio
                    ? Loc.Format(VmStrings.Video_HtmlSavedAudio, ("size", sizeText), ("format", data.AudioMimeType == "audio/mpeg" ? "MP3" : "WAV"), ("file", filePath))
                    : Loc.Format(VmStrings.Video_HtmlSavedSilent, ("size", sizeText), ("file", filePath)),
                isError: false);
            return true;
        }
        catch (Exception ex)
        {
            ShowStatus(Loc.Format(VmStrings.Video_HtmlFailed, ("error", ex.Message)), isError: true,
                new[] { VmStrings.Media_NoHalfFile });
            return false;
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* Aufräumen nach Fehler */ }
            IsBusy = false;
            BusyText = string.Empty;
            OnPropertyChanged(nameof(SelectedVideo));
            OnVideoChanged();
        }
    }

    /// <summary>Sind alle Szenen vertont, fehlt aber die Gesamtdatei, wird sie kostenlos zusammengesetzt (vor HTML/MP4-Export).</summary>
    private async Task AssembleIfNeededAsync(VideoDocument video)
    {
        if (!NeedsOnlyAssembly || _videoService.HasCurrentAudio(_profileName, video)) return;
        BusyText = VmStrings.Video_AudioAssembling;
        var assembly = await _videoService.AssembleAudioAsync(_profileName, video);
        video.AudioFileName = assembly.FileName;
        video.AudioDurationSeconds = assembly.DurationSeconds;
        video.AudioSceneStarts = assembly.SceneStarts.ToList();
        video.AudioCreatedAt = DateTime.Now;
        if (string.IsNullOrWhiteSpace(video.AudioCreatedWith))
            video.AudioCreatedWith = "Gemini TTS";
        video.AudioScenesHash = video.ComputeScenesHash();
        await SaveAsync();
    }

    // ---------------- MP4 und MP3 (ffmpeg) ----------------

    private readonly IVideoMp4Exporter? _mp4;

    private Mp4Availability? _mp4Status;
    /// <summary>Stand der MP4-Voraussetzungen (null = wird noch geprüft).</summary>
    public Mp4Availability? Mp4Status
    {
        get => _mp4Status;
        private set
        {
            _mp4Status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanExportMp4));
            OnPropertyChanged(nameof(Mp4DisabledReason));
        }
    }

    private string? _mp3UnavailableReason = VmStrings.Media_FfmpegSearching;

    /// <summary>ffmpeg (neu) suchen: MP3 für HTML und Tonspur, MP4 samt Folienzeichner.</summary>
    public async Task RefreshExportAvailabilityAsync(bool refresh = false)
    {
        await RefreshMp3AvailabilityAsync();
        if (_transcoder == null)
        {
            _mp3UnavailableReason = VmStrings.Media_Mp3Unavailable;
        }
        else
        {
            try { _mp3UnavailableReason = await _transcoder.Mp3UnavailableReasonAsync(); }
            catch (Exception ex) { _mp3UnavailableReason = Loc.Format(VmStrings.Media_FfmpegCheckFailed, ("error", ex.Message)); }
        }
        OnPropertyChanged(nameof(Mp3TrackDisabledReason));

        if (_mp4 == null)
        {
            Mp4Status = Mp4Availability.Unavailable(VmStrings.Video_Mp4Unavailable, null);
            return;
        }
        try { Mp4Status = await _mp4.GetAvailabilityAsync(refresh); }
        catch (Exception ex) { Mp4Status = Mp4Availability.Unavailable(Loc.Format(VmStrings.Video_Mp4CheckFailed, ("error", ex.Message)), null); }
    }

    /// <summary>Warum „🎞️ MP4“ gesperrt ist (null = möglich).</summary>
    public string? Mp4DisabledReason
    {
        get
        {
            if (!HasVideo) return VmStrings.Video_NoVideo;
            if (IsBusy) return VmStrings.Media_BusyOperation;
            if (Mp4Status == null) return VmStrings.Media_FfmpegSearching;
            return Mp4Status.IsAvailable ? null : Mp4Status.Message;
        }
    }

    public bool CanExportMp4 => Mp4DisabledReason == null;

    /// <summary>Installations- bzw. Einrichtungshinweis, wenn MP4 nicht geht.</summary>
    public string? Mp4Hint => Mp4Status is { IsAvailable: false } s ? s.Hint : null;

    /// <summary>
    /// Zeitachse für den Export: Zeitmarken der Audiodatei; fehlt nur das Zusammensetzen, aus den Szenen-Dauern
    /// (wie <c>AssembleAudioAsync</c>: 0,6 s Pause, Szene ohne Text 3 s); sonst geschätzt bei Tempo 1.
    /// </summary>
    private VideoTimeline ExportTimeline(VideoDocument video)
    {
        if (_videoService.HasCurrentAudio(_profileName, video))
            return VideoTimeline.FromStarts(video.AudioSceneStarts, video.AudioDurationSeconds, video.Scenes.Count);
        if (NeedsOnlyAssembly)
        {
            var starts = new List<double>();
            var t = 0.0;
            for (var i = 0; i < video.Scenes.Count; i++)
            {
                if (i > 0) t += 0.6;
                starts.Add(t);
                t += video.Scenes[i].HasNarration ? video.Scenes[i].AudioDurationSeconds : VideoTimeline.MinSceneSeconds;
            }
            return VideoTimeline.FromStarts(starts, t, video.Scenes.Count);
        }
        return VideoTimeline.FromEstimates(video.Scenes, 1.0);
    }

    /// <summary>Schätzung vor dem MP4-Export (Bilder, Dauer, Größe, Exportdauer).</summary>
    public Mp4ExportEstimate? Mp4Estimate =>
        SelectedVideo is { } video && _mp4 != null ? _mp4.Estimate(video, ExportTimeline(video), HtmlExportHasAudio) : null;

    /// <summary>„8 Folien · 3:12 Min. · Datei ca. 4,2 MB · Export dauert ca. 1 Min. · mit Ton (AAC)“</summary>
    public string? Mp4EstimateText
    {
        get
        {
            if (SelectedVideo is not { } video || Mp4Estimate is not { } e) return null;
            var seconds = Math.Max(0, (int)Math.Round(e.DurationSeconds));
            return Loc.Format(VmStrings.Video_Mp4Estimate,
                ("slides", Loc.Format(VmStrings.Video_SlideCount, ("count", video.Slides.Count))),
                ("duration", $"{seconds / 60}:{seconds % 60:00}"),
                ("size", MediaTexts.Size(e.Megabytes)),
                ("time", MediaTexts.ExportTime(e.ExportSeconds)),
                ("audio", HtmlExportHasAudio ? VmStrings.Video_Mp4WithAudio : VmStrings.Video_Mp4Silent));
        }
    }

    private bool _isExportingMedia;
    /// <summary>MP4- bzw. MP3-Export läuft (Fortschrittsbalken mit Abbrechen).</summary>
    public bool IsExportingMedia { get => _isExportingMedia; private set { _isExportingMedia = value; OnPropertyChanged(); } }

    private double _exportProgress;
    public double ExportProgress { get => _exportProgress; private set { _exportProgress = value; OnPropertyChanged(); } }

    private string _exportProgressText = string.Empty;
    public string ExportProgressText { get => _exportProgressText; private set { _exportProgressText = value; OnPropertyChanged(); } }

    private CancellationTokenSource? _exportCts;
    public void CancelMediaExport() => _exportCts?.Cancel();

    /// <summary>
    /// Exportiert das gewählte Video als MP4: mit Ton bei aktueller Gemini-Audiodatei (Zusammensetzen wird bei Bedarf
    /// nachgeholt), sonst ohne Ton im festen Takt. Abbrechen beendet ffmpeg; keine halbe Datei.
    /// </summary>
    public async Task<bool> ExportMp4Async(string filePath)
    {
        if (!CanExportMp4 || SelectedVideo is not { } video || _mp4 == null) return false;
        return await RunMediaExportAsync(async token =>
        {
            await AssembleIfNeededAsync(video);
            var withAudio = _videoService.HasCurrentAudio(_profileName, video);
            var request = new Mp4ExportRequest(video, ExportTimeline(video),
                withAudio ? _videoService.GetAudioFilePath(_profileName, video) : null, filePath);
            var result = await _mp4.ExportAsync(request, new SyncProgress<Mp4ExportProgress>(p =>
            {
                ExportProgress = p.Fraction;
                ExportProgressText = p.Text;
            }), token);
            var mb = result.Bytes / 1_000_000.0;
            ShowStatus(result.HasAudio
                    ? Loc.Format(VmStrings.Video_Mp4SavedAudio, ("size", MediaTexts.SizeNumber(mb)), ("file", filePath))
                    : Loc.Format(VmStrings.Video_Mp4SavedSilent, ("size", MediaTexts.SizeNumber(mb)), ("file", filePath)),
                isError: false);
        }, VmStrings.Video_Mp4Busy);
    }

    /// <summary>Warum „Tonspur als MP3“ gesperrt ist (null = möglich).</summary>
    public string? Mp3TrackDisabledReason
    {
        get
        {
            if (SelectedVideo is not { } video) return VmStrings.Video_SelectVideo;
            if (IsBusy) return VmStrings.Media_BusyOperation;
            if (!_videoService.HasCurrentAudio(_profileName, video))
                return NeedsOnlyAssembly ? Loc.Format(VmStrings.Video_AssembleFirst, ("button", VmStrings.Video_AssembleAudio)) : VmStrings.Video_GenerateAudioFirst;
            return _mp3UnavailableReason;
        }
    }

    public bool CanExportAudioTrackMp3 => Mp3TrackDisabledReason == null;

    /// <summary>Speichert die Tonspur des Videos als MP3.</summary>
    public async Task<bool> ExportAudioTrackMp3Async(string filePath)
    {
        if (!CanExportAudioTrackMp3 || SelectedVideo is not { } video || _transcoder == null) return false;
        var source = _videoService.GetAudioFilePath(_profileName, video)!;
        return await RunMediaExportAsync(async token =>
        {
            await _transcoder.ConvertToMp3FileAsync(source, filePath, video.AudioDurationSeconds,
                new SyncProgress<double>(p => { ExportProgress = p; ExportProgressText = Loc.Format(VmStrings.Video_Mp3TrackProgress, ("percent", (int)(p * 100))); }), token);
            ShowStatus(Loc.Format(VmStrings.Video_Mp3TrackSaved, ("file", filePath)), isError: false);
        }, VmStrings.Video_Mp3TrackBusy);
    }

    private async Task<bool> RunMediaExportAsync(Func<CancellationToken, Task> work, string busyText)
    {
        await StopAsync();
        IsBusy = true;
        BusyText = busyText;
        IsExportingMedia = true;
        ExportProgress = 0;
        ExportProgressText = busyText;
        ClearStatus();
        _exportCts = new CancellationTokenSource();
        try
        {
            await work(_exportCts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            ShowStatus(VmStrings.Video_ExportCancelled, isError: false);
            return false;
        }
        catch (FfmpegException ex)
        {
            ShowStatus($"⚠️ {ex.Message}", isError: true, details: ex.Detail);
            return false;
        }
        catch (Exception ex)
        {
            ShowStatus(Loc.Format(VmStrings.Media_ExportFailed, ("error", ex.Message)), isError: true, new[] { VmStrings.Media_NoHalfFile });
            return false;
        }
        finally
        {
            _exportCts?.Dispose();
            _exportCts = null;
            IsExportingMedia = false;
            ExportProgress = 0;
            ExportProgressText = string.Empty;
            IsBusy = false;
            BusyText = string.Empty;
            OnPropertyChanged(nameof(SelectedVideo));
            OnVideoChanged();
        }
    }

    /// <summary>Fortschritt direkt melden (die Oberfläche zeichnet über ihr PropertyChanged-Abo ohnehin im eigenen Kontext neu).</summary>
    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    // ---------------- Abspielen aus dem Bericht ----------------

    private string? _pendingPlaybackId;

    /// <summary>Gemerktes Video, das abgespielt werden soll, sobald der Reiter „Video“ bereit ist.</summary>
    public string? PendingPlaybackId => _pendingPlaybackId;

    /// <summary>„▶ Video abspielen“ im Bericht: Video vormerken; der Reiter startet es nach dem ersten Zeichnen.</summary>
    public void RequestPlayback(string videoId) => _pendingPlaybackId = videoId;

    /// <summary>
    /// Startet ein vorgemerktes Video: auswählen, Formular schließen, abspielen. Wartet kurz, bis Player bzw. Systemstimme
    /// bereit sind (sie werden erst beim Zeichnen des Reiters verbunden); bleibt das Abspielen gesperrt, erscheint der Grund.
    /// </summary>
    public async Task StartPendingPlaybackAsync(int maxWaitSteps = 20)
    {
        var id = _pendingPlaybackId;
        _pendingPlaybackId = null;
        if (id == null) return;

        if (_library.Items.All(v => v.Id != id))
        {
            ShowStatus(VmStrings.Video_ReportVideoMissing, isError: false, isWarning: true);
            return;
        }

        await StopAsync();
        IsFormOpen = false;
        SelectVideo(id);
        // Immer von vorn (auch wenn das Video schon gewählt war und mitten drin stand)
        _currentSceneIndex = -1;
        _position = 0;
        _isEnded = false;

        for (var i = 0; i < maxWaitSteps && !CanPlay; i++)
        {
            if (!HasVideo || IsBusy) break;
            await _delay(TimeSpan.FromMilliseconds(150), CancellationToken.None);
            await RefreshAvailabilityAsync();
        }
        await PlayAsync();
    }
}
