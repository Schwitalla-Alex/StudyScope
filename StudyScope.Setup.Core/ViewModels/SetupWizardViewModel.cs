using StudyScope.Setup.Core.Interfaces;
using StudyScope.Setup.Core.Models;

namespace StudyScope.Setup.Core.ViewModels;

public enum WizardStep
{
    Welcome,
    Folder,
    Options,
    Progress,
    Finished
}

/// <summary>
/// Steuert den Installationsassistenten: Willkommen → Ordner → Optionen → Fortschritt → Fertig.
/// Bei einer vorhandenen Installation wird ein Update in deren Ordner durchgeführt.
/// </summary>
public sealed class SetupWizardViewModel : ViewModelBase
{
    private readonly IInstallService _installService;
    private readonly IPathProvider _paths;
    private readonly IPayloadProvider _payload;
    private readonly IShortcutService _shortcuts;
    private readonly IFolderPicker _folderPicker;
    private readonly IProcessService _processes;

    private WizardStep _currentStep = WizardStep.Welcome;
    private string _installDirectory = string.Empty;
    private string? _folderError;
    private bool _createDesktopShortcut = true;
    private bool _createMenuShortcut = true;
    private bool _launchAfterFinish = true;
    private ExistingInstallation? _existing;

    public SetupWizardViewModel(IInstallService installService, IPathProvider paths, IPayloadProvider payload,
        IShortcutService shortcuts, IFolderPicker folderPicker, IProcessService processes, InstallViewModel install)
    {
        _installService = installService;
        _paths = paths;
        _payload = payload;
        _shortcuts = shortcuts;
        _folderPicker = folderPicker;
        _processes = processes;
        Install = install;
        Install.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CanGoBack));
    }

    /// <summary>Wird ausgelöst, wenn das Setup-Fenster geschlossen werden soll.</summary>
    public event EventHandler? CloseRequested;

    public InstallViewModel Install { get; }

    public string Version => _payload.Version;
    public bool IsPayloadAvailable => _payload.IsAvailable;
    public string UserDataDirectory => _paths.UserDataDirectory;
    public string DefaultInstallDirectory => _paths.DefaultInstallDirectory;

    public ExistingInstallation? ExistingInstallation { get => _existing; private set { if (SetProperty(ref _existing, value)) OnPropertyChanged(nameof(IsUpdate)); } }
    public bool IsUpdate => _existing != null;
    public string? InstalledVersion => _existing?.Manifest.Version;

    public WizardStep CurrentStep
    {
        get => _currentStep;
        private set
        {
            if (SetProperty(ref _currentStep, value))
            {
                OnPropertyChanged(nameof(CanGoBack));
                OnPropertyChanged(nameof(CanGoNext));
                OnPropertyChanged(nameof(StepNumber));
            }
        }
    }

    /// <summary>1-basierte Schrittnummer für die Anzeige „Schritt x von 5“.</summary>
    public int StepNumber => (int)_currentStep + 1;
    public int StepCount => Enum.GetValues<WizardStep>().Length;

    public string InstallDirectory
    {
        get => _installDirectory;
        set
        {
            if (IsUpdate) return; // Update immer in den vorhandenen Ordner
            if (SetProperty(ref _installDirectory, value ?? string.Empty))
                ValidateFolder();
        }
    }

    public string? FolderError { get => _folderError; private set { if (SetProperty(ref _folderError, value)) OnPropertyChanged(nameof(CanGoNext)); } }

    public bool CreateDesktopShortcut { get => _createDesktopShortcut; set => SetProperty(ref _createDesktopShortcut, value); }

    public bool CreateMenuShortcut
    {
        get => _createMenuShortcut || !_shortcuts.IsMenuShortcutOptional;
        set { if (_shortcuts.IsMenuShortcutOptional) SetProperty(ref _createMenuShortcut, value); }
    }

    public bool IsMenuShortcutOptional => _shortcuts.IsMenuShortcutOptional;
    public string MenuShortcutLabel => _shortcuts.MenuShortcutLabel;

    public bool LaunchAfterFinish { get => _launchAfterFinish; set => SetProperty(ref _launchAfterFinish, value); }

    public bool CanGoBack => _currentStep is WizardStep.Folder or WizardStep.Options
                             || (_currentStep == WizardStep.Progress && Install.HasError && !Install.IsRunning);

    public bool CanGoNext => _currentStep switch
    {
        WizardStep.Welcome => IsPayloadAvailable,
        WizardStep.Folder => FolderError == null,
        WizardStep.Options => true,
        _ => false
    };

    /// <summary>Vorhandene Installation erkennen und Ordner vorbelegen.</summary>
    public void Initialize()
    {
        ExistingInstallation = _installService.FindExistingInstallation();
        _installDirectory = ExistingInstallation?.InstallDirectory ?? _paths.DefaultInstallDirectory;
        OnPropertyChanged(nameof(InstallDirectory));
        OnPropertyChanged(nameof(InstalledVersion));
        ValidateFolder();
        OnPropertyChanged(nameof(CanGoNext));
    }

    public async Task GoNextAsync()
    {
        if (!CanGoNext)
            return;

        switch (CurrentStep)
        {
            case WizardStep.Welcome:
                CurrentStep = WizardStep.Folder;
                break;
            case WizardStep.Folder:
                ValidateFolder();
                if (FolderError == null)
                    CurrentStep = WizardStep.Options;
                break;
            case WizardStep.Options:
                await StartInstallationAsync();
                break;
        }
    }

    public void GoBack()
    {
        if (!CanGoBack)
            return;

        CurrentStep = CurrentStep switch
        {
            WizardStep.Folder => WizardStep.Welcome,
            WizardStep.Options => WizardStep.Folder,
            WizardStep.Progress => WizardStep.Options,
            _ => CurrentStep
        };
    }

    /// <summary>Nach einem Fehler erneut versuchen.</summary>
    public Task RetryAsync() => Install.HasError ? StartInstallationAsync() : Task.CompletedTask;

    private async Task StartInstallationAsync()
    {
        CurrentStep = WizardStep.Progress;
        var ok = await Install.RunAsync(new InstallOptions
        {
            InstallDirectory = InstallDirectory,
            CreateDesktopShortcut = CreateDesktopShortcut,
            CreateMenuShortcut = CreateMenuShortcut
        });
        OnPropertyChanged(nameof(CanGoBack));
        if (ok)
            CurrentStep = WizardStep.Finished;
    }

    public async Task BrowseFolderAsync()
    {
        if (IsUpdate)
            return;
        var picked = await _folderPicker.PickFolderAsync(Directory.Exists(InstallDirectory) ? InstallDirectory : null);
        if (string.IsNullOrWhiteSpace(picked))
            return;

        // Gewählter Elternordner: einen Unterordner „StudyScope“ vorschlagen, falls der Ordner nicht leer ist
        var candidate = picked;
        if (_installService.ValidateInstallDirectory(candidate) != null)
        {
            var sub = Path.Combine(picked, "StudyScope");
            if (_installService.ValidateInstallDirectory(sub) == null)
                candidate = sub;
        }
        InstallDirectory = candidate;
    }

    public void ResetFolderToDefault() => InstallDirectory = _paths.DefaultInstallDirectory;

    /// <summary>„Fertig stellen“: StudyScope auf Wunsch starten und das Setup schließen.</summary>
    public void Finish()
    {
        if (CurrentStep == WizardStep.Finished && LaunchAfterFinish)
        {
            try
            {
                _processes.StartApp(Path.Combine(InstallDirectory, _paths.AppExecutableName), InstallDirectory);
            }
            catch (Exception)
            {
                // Start fehlgeschlagen – die Installation selbst ist trotzdem erfolgreich
            }
        }
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>„Abbrechen“: laufende Installation abbrechen, sonst das Setup schließen.</summary>
    public void Cancel()
    {
        if (Install.IsRunning)
        {
            Install.Cancel();
            return;
        }
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ValidateFolder() => FolderError = _installService.ValidateInstallDirectory(_installDirectory);
}
