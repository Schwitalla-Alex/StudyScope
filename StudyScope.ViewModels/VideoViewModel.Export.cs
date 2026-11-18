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
                return $"{estimate.SizeText} · mit Ton ({(mp3 ? "MP3" : "WAV")}) · Audio wird vorher kostenlos zusammengesetzt";
            }

            var e = _videoService.EstimateHtml(_profileName, new[] { video }, mp3);
            return e.VideosWithAudio > 0
                ? $"{e.SizeText} · mit Ton ({(e.UsesMp3 ? "MP3" : "WAV")})"
                : $"{e.SizeText} · ohne Ton";
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
        BusyText = "HTML-Datei wird erstellt …";
        ClearStatus();
        var temp = filePath + ".tmp";
        try
        {
            if (NeedsOnlyAssembly && !_videoService.HasCurrentAudio(_profileName, video))
            {
                BusyText = "Audio wird zusammengesetzt …";
                var assembly = await _videoService.AssembleAudioAsync(_profileName, video);
                video.AudioFileName = assembly.FileName;
                video.AudioDurationSeconds = assembly.DurationSeconds;
                video.AudioSceneStarts = assembly.SceneStarts.ToList();
                video.AudioCreatedAt = DateTime.Now;
                if (string.IsNullOrWhiteSpace(video.AudioCreatedWith))
                    video.AudioCreatedWith = "Gemini TTS";
                video.AudioScenesHash = video.ComputeScenesHash();
                await SaveAsync();
                BusyText = "HTML-Datei wird erstellt …";
            }

            var mp3 = UseMp3 && _videoService.HasCurrentAudio(_profileName, video);
            if (mp3) BusyText = "Ton wird nach MP3 umgewandelt …";
            var data = await _videoService.BuildPlayerDataAsync(_profileName, video, mp3 ? _transcoder : null);
            BusyText = "HTML-Datei wird geschrieben …";
            var html = _videoService.ExportToHtml(data);
            await File.WriteAllTextAsync(temp, html, new System.Text.UTF8Encoding(false));
            File.Move(temp, filePath, overwrite: true);

            var size = new FileInfo(filePath).Length / 1_000_000.0;
            var sizeText = size < 0.1 ? "unter 0,1" : size.ToString(size < 10 ? "0.0" : "0", System.Globalization.CultureInfo.GetCultureInfo("de-DE"));
            ShowStatus(data.HasAudio
                    ? $"✓ HTML-Datei gespeichert ({sizeText} MB, mit Ton als {(data.AudioMimeType == "audio/mpeg" ? "MP3" : "WAV")}): {filePath}"
                    : $"✓ HTML-Datei gespeichert ({sizeText} MB, ohne Ton – die Folien laufen im festen Takt): {filePath}",
                isError: false);
            return true;
        }
        catch (Exception ex)
        {
            ShowStatus($"⚠️ Fehler beim HTML-Export: {ex.Message}", isError: true,
                new[] { "Es wurde keine halbe Datei gespeichert." });
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
            ShowStatus("⚠️ Das Video aus dem Bericht gibt es nicht mehr (gelöscht oder in einem anderen Profil).", isError: false, isWarning: true);
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
