using StudyScope.Core.Localization;
using StudyScope.Setup.Core.Interfaces;
using StudyScope.Setup.Core.Models;
using StudyScope.Setup.Core.Resources;
using StudyScope.Setup.Core.Services;

namespace StudyScope.Setup.Core.ViewModels;

public enum UninstallStep
{
    Confirm,
    Progress,
    Finished
}

/// <summary>
/// Deinstallation: Bestätigung (optional mit Löschen der Benutzerdaten, standardmäßig aus, mit Sicherheitsabfrage)
/// → Fortschritt → Fertig.
/// </summary>
public sealed class UninstallViewModel : ViewModelBase
{
    private readonly IInstallService _installService;
    private readonly IPathProvider _paths;

    private UninstallStep _currentStep = UninstallStep.Confirm;
    private ExistingInstallation? _existing;
    private bool _deleteUserData;
    private bool _isDeleteConfirmationVisible;
    private int _progressPercent;
    private string _statusMessage = string.Empty;
    private string? _errorMessage;
    private bool _isRunning;

    public UninstallViewModel(IInstallService installService, IPathProvider paths)
    {
        _installService = installService;
        _paths = paths;
    }

    public event EventHandler? CloseRequested;

    public UninstallStep CurrentStep { get => _currentStep; private set => SetProperty(ref _currentStep, value); }

    public ExistingInstallation? ExistingInstallation
    {
        get => _existing;
        private set { if (SetProperty(ref _existing, value)) { OnPropertyChanged(nameof(IsInstalled)); OnPropertyChanged(nameof(InstallDirectory)); OnPropertyChanged(nameof(InstalledVersion)); } }
    }

    public bool IsInstalled => _existing != null;
    public string InstallDirectory => _existing?.InstallDirectory ?? string.Empty;
    public string? InstalledVersion => _existing?.Manifest.Version;
    public string UserDataDirectory => _paths.UserDataDirectory;

    /// <summary>Profile und Einstellungen mitlöschen – standardmäßig aus.</summary>
    public bool DeleteUserData { get => _deleteUserData; set => SetProperty(ref _deleteUserData, value); }

    /// <summary>Sicherheitsabfrage vor dem endgültigen Löschen der Benutzerdaten.</summary>
    public bool IsDeleteConfirmationVisible { get => _isDeleteConfirmationVisible; private set => SetProperty(ref _isDeleteConfirmationVisible, value); }

    public int ProgressPercent { get => _progressPercent; private set => SetProperty(ref _progressPercent, value); }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public string? ErrorMessage { get => _errorMessage; private set { if (SetProperty(ref _errorMessage, value)) OnPropertyChanged(nameof(HasError)); } }
    public bool HasError => _errorMessage != null;
    public bool IsRunning { get => _isRunning; private set => SetProperty(ref _isRunning, value); }

    /// <summary>Wurden die Benutzerdaten tatsächlich mitgelöscht? (für die Abschlussseite)</summary>
    public bool UserDataDeleted { get; private set; }

    public void Initialize() => ExistingInstallation = _installService.FindExistingInstallation();

    /// <summary>„Deinstallieren“: bei angehaktem Löschen erst die Sicherheitsabfrage zeigen.</summary>
    public Task RequestUninstallAsync()
    {
        if (!IsInstalled || IsRunning)
            return Task.CompletedTask;

        if (DeleteUserData)
        {
            IsDeleteConfirmationVisible = true;
            return Task.CompletedTask;
        }
        return RunAsync(deleteUserData: false);
    }

    /// <summary>Sicherheitsabfrage bestätigt: deinstallieren und Benutzerdaten löschen.</summary>
    public Task ConfirmDeleteUserDataAsync()
    {
        if (!IsDeleteConfirmationVisible)
            return Task.CompletedTask;
        IsDeleteConfirmationVisible = false;
        return RunAsync(deleteUserData: true);
    }

    /// <summary>Sicherheitsabfrage abgebrochen: nichts passiert, die Auswahl bleibt bestehen.</summary>
    public void CancelDeleteConfirmation() => IsDeleteConfirmationVisible = false;

    private async Task RunAsync(bool deleteUserData)
    {
        ErrorMessage = null;
        IsRunning = true;
        CurrentStep = UninstallStep.Progress;

        var progress = new DirectProgress<InstallProgress>(p =>
        {
            ProgressPercent = p.Percent;
            StatusMessage = p.Message;
        });

        try
        {
            await _installService.UninstallAsync(new UninstallOptions
            {
                InstallDirectory = InstallDirectory,
                DeleteUserData = deleteUserData
            }, progress);

            UserDataDeleted = deleteUserData;
            CurrentStep = UninstallStep.Finished;
        }
        catch (InstallException ex)
        {
            ErrorMessage = ex.Message;
            CurrentStep = UninstallStep.Confirm;
        }
        catch (Exception ex)
        {
            ErrorMessage = Loc.Format(SetupCoreStrings.Error_Unexpected, ("error", ex.Message));
            CurrentStep = UninstallStep.Confirm;
        }
        finally
        {
            IsRunning = false;
        }
    }

    public void Close()
    {
        if (!IsRunning)
            CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
