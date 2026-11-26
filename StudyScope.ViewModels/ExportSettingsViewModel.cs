using System.ComponentModel;
using System.Runtime.CompilerServices;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.ViewModels.Resources;
using StudyScope.Core.Localization;

namespace StudyScope.ViewModels;

/// <summary>
/// Einstellungen › „🎞️ Export“: eigener ffmpeg-Pfad (global, gehört zum Rechner), „ffmpeg suchen/testen“ mit dem
/// eingegebenen (noch ungespeicherten) Pfad, Installationsanleitung. Speichern sucht danach neu, damit Video-, Audio-
/// und Berichts-Reiter den neuen Stand sehen.
/// </summary>
public class ExportSettingsViewModel : INotifyPropertyChanged
{
    private readonly IStorageService _storage;
    private readonly IFfmpegService _ffmpeg;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ExportSettingsViewModel(IStorageService storage, IFfmpegService ffmpeg)
    {
        _storage = storage;
        _ffmpeg = ffmpeg;
    }

    private string _ffmpegPath = string.Empty;
    /// <summary>Pfad zu ffmpeg (Datei oder Ordner); leer = automatisch suchen.</summary>
    public string FfmpegPath { get => _ffmpegPath; set { _ffmpegPath = value ?? string.Empty; OnPropertyChanged(); } }

    private FfmpegStatus? _status;
    /// <summary>Ergebnis der letzten Suche/Prüfung.</summary>
    public FfmpegStatus? Status { get => _status; private set { _status = value; OnPropertyChanged(); } }

    private bool _isTesting;
    public bool IsTesting { get => _isTesting; private set { _isTesting = value; OnPropertyChanged(); } }

    private string? _statusMessage;
    public string? StatusMessage { get => _statusMessage; private set { _statusMessage = value; OnPropertyChanged(); } }

    private bool _isSuccess;
    public bool IsSuccess { get => _isSuccess; private set { _isSuccess = value; OnPropertyChanged(); } }

    public bool IsLinux => OperatingSystem.IsLinux();
    public string InstallCommand => IsLinux ? FfmpegInfo.LinuxInstall : FfmpegInfo.WindowsInstall;
    public string Purpose => FfmpegInfo.Purpose;

    /// <summary>Beim Öffnen: gespeicherten Pfad und aktuellen Stand anzeigen.</summary>
    public async Task LoadAsync()
    {
        StatusMessage = null;
        try { FfmpegPath = (await _storage.LoadExportSettingsAsync()).FfmpegPath; }
        catch { FfmpegPath = string.Empty; }
        Status = await _ffmpeg.GetStatusAsync();
    }

    /// <summary>„ffmpeg suchen/testen“ mit dem eingegebenen Pfad (ohne zu speichern).</summary>
    public async Task TestAsync()
    {
        if (IsTesting) return;
        IsTesting = true;
        StatusMessage = null;
        try
        {
            Status = await _ffmpeg.TestAsync(FfmpegPath);
            if (Status.IsAvailable && string.IsNullOrWhiteSpace(FfmpegPath) == false && Status.Message.Contains("funktioniert nicht"))
                ShowMessage(Status.Message, success: false);
        }
        catch (Exception ex)
        {
            ShowMessage(Loc.Format(VmStrings.Export_CheckFailed, ("error", ex.Message)), success: false);
        }
        finally
        {
            IsTesting = false;
        }
    }

    /// <summary>Speichert den Pfad und sucht ffmpeg danach neu (zwischengespeicherter Stand wird ersetzt).</summary>
    public async Task<bool> SaveAsync()
    {
        try
        {
            await _storage.SaveExportSettingsAsync(new ExportSettings { FfmpegPath = FfmpegPath.Trim().Trim('"') });
            Status = await _ffmpeg.GetStatusAsync(refresh: true);
            ShowMessage(Status.IsAvailable ? Loc.Format(VmStrings.Export_Saved, ("status", Status.Message)) : VmStrings.Export_SavedNotFound, Status.IsAvailable);
            return true;
        }
        catch (Exception ex)
        {
            ShowMessage(Loc.Format(VmStrings.Common_SaveFailed, ("error", ex.Message)), success: false);
            return false;
        }
    }

    private void ShowMessage(string message, bool success)
    {
        IsSuccess = success;
        StatusMessage = message;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
