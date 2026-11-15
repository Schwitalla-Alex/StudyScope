using System.Collections.ObjectModel;
using StudyScope.Setup.Core.Interfaces;
using StudyScope.Setup.Core.Models;
using StudyScope.Setup.Core.Services;

namespace StudyScope.Setup.Core.ViewModels;

/// <summary>Führt die Installation aus und stellt Fortschritt, Protokoll und Fehler für die Oberfläche bereit.</summary>
public sealed class InstallViewModel : ViewModelBase
{
    private readonly IInstallService _installService;
    private CancellationTokenSource? _cts;

    private int _progressPercent;
    private string _statusMessage = string.Empty;
    private string? _errorMessage;
    private bool _isRunning;
    private bool _isCompleted;

    public InstallViewModel(IInstallService installService) => _installService = installService;

    public int ProgressPercent { get => _progressPercent; private set => SetProperty(ref _progressPercent, value); }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public string? ErrorMessage { get => _errorMessage; private set { if (SetProperty(ref _errorMessage, value)) OnPropertyChanged(nameof(HasError)); } }
    public bool HasError => _errorMessage != null;
    public bool IsRunning { get => _isRunning; private set => SetProperty(ref _isRunning, value); }
    public bool IsCompleted { get => _isCompleted; private set => SetProperty(ref _isCompleted, value); }

    /// <summary>Die letzten Schritte für die Detailanzeige.</summary>
    public ObservableCollection<string> Log { get; } = new();

    /// <summary>Startet die Installation. Liefert true bei Erfolg; Fehler stehen danach in <see cref="ErrorMessage"/>.</summary>
    public async Task<bool> RunAsync(InstallOptions options)
    {
        if (IsRunning)
            return false;

        _cts = new CancellationTokenSource();
        ErrorMessage = null;
        IsCompleted = false;
        ProgressPercent = 0;
        Log.Clear();
        IsRunning = true;

        var progress = new DirectProgress<InstallProgress>(p =>
        {
            ProgressPercent = p.Percent;
            StatusMessage = p.Message;
            lock (Log)
            {
                Log.Add(p.Message);
                while (Log.Count > 200) Log.RemoveAt(0);
            }
        });

        try
        {
            await _installService.InstallAsync(options, progress, _cts.Token);
            IsCompleted = true;
            return true;
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "Die Installation wurde abgebrochen.";
            StatusMessage = ErrorMessage;
            return false;
        }
        catch (InstallException ex)
        {
            ErrorMessage = ex.Message;
            StatusMessage = "Die Installation ist fehlgeschlagen.";
            return false;
        }
        catch (Exception ex)
        {
            ErrorMessage = "Unerwarteter Fehler: " + ex.Message;
            StatusMessage = "Die Installation ist fehlgeschlagen.";
            return false;
        }
        finally
        {
            IsRunning = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    public void Cancel() => _cts?.Cancel();
}
