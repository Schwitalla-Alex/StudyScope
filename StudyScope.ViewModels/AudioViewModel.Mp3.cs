using StudyScope.Core.Interfaces;
using StudyScope.Core.Localization;
using StudyScope.ViewModels.Resources;

namespace StudyScope.ViewModels;

/// <summary>Audio-Reiter, Teil MP3-Export der Audiodatei über ein vorhandenes ffmpeg (<see cref="IAudioTranscoder"/>).</summary>
public partial class AudioViewModel
{
    private IAudioTranscoder? _transcoder;
    private string? _mp3UnavailableReason = VmStrings.Media_FfmpegSearching;

    /// <summary>Transcoder nachträglich setzen (Composition-Root); ohne Transcoder ist MP3 mit Begründung gesperrt.</summary>
    public void UseTranscoder(IAudioTranscoder? transcoder)
    {
        _transcoder = transcoder;
        _mp3UnavailableReason = transcoder == null ? VmStrings.Media_Mp3Unavailable : VmStrings.Media_FfmpegSearching;
    }

    /// <summary>Sucht ffmpeg (bzw. übernimmt den zwischengespeicherten Stand) und aktualisiert die Freigabe des MP3-Knopfs.</summary>
    public async Task RefreshMp3AvailabilityAsync()
    {
        if (_transcoder == null)
        {
            _mp3UnavailableReason = VmStrings.Media_Mp3Unavailable;
        }
        else
        {
            try { _mp3UnavailableReason = await _transcoder.Mp3UnavailableReasonAsync(); }
            catch (Exception ex) { _mp3UnavailableReason = Loc.Format(VmStrings.Media_FfmpegCheckFailed, ("error", ex.Message)); }
        }
        OnPropertyChanged(nameof(CanExportMp3));
        OnPropertyChanged(nameof(Mp3DisabledReason));
    }

    /// <summary>Warum „MP3 exportieren“ gesperrt ist (null = möglich).</summary>
    public string? Mp3DisabledReason
    {
        get
        {
            if (SelectedAudioFilePath == null) return VmStrings.Audio_Mp3NeedsFile;
            if (IsBusy) return VmStrings.Media_BusyOperation;
            return _mp3UnavailableReason;
        }
    }

    public bool CanExportMp3 => Mp3DisabledReason == null;

    private bool _isExportingMp3;
    public bool IsExportingMp3 { get => _isExportingMp3; private set { _isExportingMp3 = value; OnPropertyChanged(); } }

    private double _mp3Progress;
    public double Mp3Progress { get => _mp3Progress; private set { _mp3Progress = value; OnPropertyChanged(); } }

    private CancellationTokenSource? _mp3Cts;
    public void CancelMp3Export() => _mp3Cts?.Cancel();

    /// <summary>„ca. 3,8 MB (MP3, 64 kbit/s)“ für die gewählte Audiodatei.</summary>
    public string? Mp3EstimateText
    {
        get
        {
            var summary = SelectedSummary;
            if (summary == null || SelectedAudioFilePath == null) return null;
            var seconds = summary.AudioDurationSeconds > 0 ? summary.AudioDurationSeconds : summary.EstimatedMinutes * 60;
            var mb = seconds * IAudioTranscoder.Mp3Kbps * 1000 / 8 / 1_000_000;
            return Loc.Format(VmStrings.Audio_Mp3Estimate, ("size", MediaTexts.MegabyteNumber(Math.Max(0.1, mb))), ("kbps", IAudioTranscoder.Mp3Kbps));
        }
    }

    /// <summary>Speichert die Audiodatei als MP3 (temporär schreiben, dann umbenennen; Abbrechen beendet ffmpeg).</summary>
    public async Task<bool> ExportMp3Async(string filePath)
    {
        var source = SelectedAudioFilePath;
        if (!CanExportMp3 || source == null || _transcoder == null || SelectedSummary is not { } summary) return false;

        await StopAsync();
        IsBusy = true;
        IsExportingMp3 = true;
        Mp3Progress = 0;
        ClearStatus();
        _mp3Cts = new CancellationTokenSource();
        try
        {
            var seconds = summary.AudioDurationSeconds > 0 ? summary.AudioDurationSeconds : summary.EstimatedMinutes * 60;
            await _transcoder.ConvertToMp3FileAsync(source, filePath, seconds, new Progress<double>(p => Mp3Progress = p), _mp3Cts.Token);
            ShowStatus(Loc.Format(VmStrings.Audio_Mp3Exported, ("file", Path.GetFileName(filePath))), isError: false);
            return true;
        }
        catch (OperationCanceledException)
        {
            ShowStatus(VmStrings.Audio_Mp3Cancelled, isError: false);
            return false;
        }
        catch (FfmpegException ex)
        {
            ShowStatus($"⚠️ {ex.Message}", isError: true, details: ex.Detail);
            return false;
        }
        catch (Exception ex)
        {
            ShowStatus(Loc.Format(VmStrings.Audio_Mp3Failed, ("error", ex.Message)), isError: true, new[] { VmStrings.Media_NoHalfFile });
            return false;
        }
        finally
        {
            _mp3Cts?.Dispose();
            _mp3Cts = null;
            IsExportingMp3 = false;
            Mp3Progress = 0;
            IsBusy = false;
            OnPropertyChanged(nameof(CanExportMp3));
            OnPropertyChanged(nameof(Mp3DisabledReason));
        }
    }
}
