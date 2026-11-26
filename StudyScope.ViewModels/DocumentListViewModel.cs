using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.ViewModels.Resources;
using StudyScope.Core.Localization;

namespace StudyScope.ViewModels;

/// <summary>
/// Logik für Dokumentenverwaltung, Profilsteuerung, Zählung und Filterung.
/// </summary>
public class DocumentListViewModel : INotifyPropertyChanged
{
    private readonly IStorageService _storageService;
    private readonly IDocumentService _pdfService;
    private string _searchText = string.Empty;
    private string _currentProfile = "Standard";
    private string _currentAiModelDisplayName = VmStrings.Docs_LoadingModel;
    private ObservableCollection<string> _availableProfiles = new();

    public DocumentListViewModel(IStorageService storageService, IDocumentService pdfService)
    {
        _storageService = storageService;
        _pdfService = pdfService;
        Documents = new ObservableCollection<DocumentItem>();
    }

    public ObservableCollection<DocumentItem> Documents { get; }

    public string CurrentProfile
    {
        get => _currentProfile;
        set
        {
            if (_currentProfile != value)
            {
                _currentProfile = value;
                OnPropertyChanged();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText != value)
            {
                _searchText = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FilteredDocuments));
            }
        }
    }

    public IEnumerable<DocumentItem> FilteredDocuments
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SearchText))
                return Documents;

            return Documents.Where(d => d.FileName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }
    }

    public string CurrentAiModelDisplayName
    {
        get => _currentAiModelDisplayName;
        private set
        {
            if (_currentAiModelDisplayName != value)
            {
                _currentAiModelDisplayName = value;
                OnPropertyChanged();
            }
        }
    }

    public ObservableCollection<string> AvailableProfiles
    {
        get => _availableProfiles;
        private set
        {
            _availableProfiles = value;
            OnPropertyChanged();
        }
    }

    public int TotalDocumentCount => Documents.Count;

    public int TotalChunkCount => Documents.Sum(d => d.ExtractedChunkCount);

    /// <summary>Anzahl der aktivierten, vorhandenen Dokumente – unabhängig vom Suchbegriff.</summary>
    public int ActiveDocumentCount => Documents.Count(d => d.IsSelected && !d.IsMissing);

    public async Task RefreshCurrentAiModelAsync()
    {
        var aiConfig = await _storageService.LoadAiConfigForProfileAsync(CurrentProfile);

        if (aiConfig is { SelectedProvider: AiProviderType.FakeAi, SelectedModelName: AiConfig.FakeModelName })
        {
            // Der gespeicherte Modellname der Fake-KI ist deutsch („Fake-KI (Testmodus)“) – angezeigt wird der übersetzte
            CurrentAiModelDisplayName = VmStrings.Provider_FakeAi;
        }
        else if (aiConfig != null && !string.IsNullOrWhiteSpace(aiConfig.SelectedModelName))
        {
            CurrentAiModelDisplayName = aiConfig.SelectedModelName;
        }
        else if (aiConfig != null && !string.IsNullOrWhiteSpace(aiConfig.ModelName))
        {
            CurrentAiModelDisplayName = aiConfig.ModelName;
        }
        else
        {
            CurrentAiModelDisplayName = VmStrings.Docs_NoAiConfigured;
        }

        OnPropertyChanged(nameof(CurrentAiModelDisplayName));
        OnPropertyChanged(nameof(CurrentProfile));
    }

    public async Task InitializeAsync()
    {
        // 1. Alle vorhandenen Profile aus dem Speicher laden
        var profiles = await _storageService.GetAllProfileNamesAsync();
        AvailableProfiles = new ObservableCollection<string>(profiles);

        // 2. Zuletzt aktives Profil ermitteln
        var lastProfile = await _storageService.GetLastActiveProfileNameAsync();

        // 3. Gültiges Profil setzen
        if (!string.IsNullOrWhiteSpace(lastProfile) && AvailableProfiles.Any(p => p.Equals(lastProfile, StringComparison.OrdinalIgnoreCase)))
        {
            CurrentProfile = lastProfile;
        }
        else
        {
            CurrentProfile = AvailableProfiles.FirstOrDefault() ?? "Standard";
            await _storageService.SetLastActiveProfileNameAsync(CurrentProfile);
        }

        // 4. Dokumente und KI des aktiven Profils laden
        await LoadDocumentsAsync();
        await RefreshCurrentAiModelAsync();
    }

    public async Task SwitchProfileAsync(string newProfile)
    {
        if (string.IsNullOrWhiteSpace(newProfile))
            return;

        CurrentProfile = newProfile;

        // Sofort als aktives Profil sichern
        await _storageService.SetLastActiveProfileNameAsync(newProfile);

        // Daten des Zielprofils laden
        await LoadDocumentsAsync();
        await RefreshCurrentAiModelAsync();
    }

    /// <summary>
    /// Prüft einen Profilnamen für die Oberfläche. Liefert eine verständliche Meldung oder <c>null</c>, wenn er gültig ist.
    /// </summary>
    public string? ValidateProfileName(string? profileName)
    {
        var formatError = ProfileNameValidator.Validate(profileName);
        if (formatError != null)
            return formatError;

        // Vorhandene Profile (auch in anderer Schreibweise) nicht erneut anlegen – sonst würde deren Dokumentliste überschrieben
        var trimmed = profileName!.Trim();
        var existing = ProfileExists(trimmed)
            ? AvailableProfiles.FirstOrDefault(p => p.Equals(trimmed, StringComparison.OrdinalIgnoreCase)) ?? "Standard"
            : null;
        return existing == null ? null : Loc.Format(VmStrings.Docs_ProfileExists, ("name", existing));
    }

    private bool ProfileExists(string profileName) =>
        profileName.Equals("Standard", StringComparison.OrdinalIgnoreCase) ||
        AvailableProfiles.Any(p => p.Equals(profileName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Legt ein Profil an und aktiviert es. Ungültige und bereits vorhandene Namen werden abgelehnt
    /// (Rückgabe <c>false</c>, nichts wird gespeichert – vorhandene Profile behalten ihre Dokumente).
    /// </summary>
    public async Task<bool> CreateNewProfileAsync(string newProfileName)
    {
        if (ValidateProfileName(newProfileName) != null)
            return false;

        var trimmed = newProfileName.Trim();

        // 1. In Liste der verfügbaren Profile aufnehmen
        AvailableProfiles.Add(trimmed);
        OnPropertyChanged(nameof(AvailableProfiles));

        CurrentProfile = trimmed;

        // 2. Initiales Profil ausschließlich über den Storage-Service anlegen (kein harter File-Zugriff)
        await _storageService.SaveDocumentsAsync(trimmed, new List<DocumentItem>());
        await _storageService.SetLastActiveProfileNameAsync(trimmed);

        // 3. Ansicht aktualisieren
        await LoadDocumentsAsync();
        await RefreshCurrentAiModelAsync();
        return true;
    }

    public async Task DeleteProfileAsync(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName) ||
            profileName.Equals("Standard", StringComparison.OrdinalIgnoreCase))
        {
            return; // Standard-Profil darf nicht gelöscht werden
        }

        // 1. Aus dem Speicher löschen
        await _storageService.DeleteProfileAsync(profileName);

        // 2. Aus der Liste entfernen
        AvailableProfiles.Remove(profileName);

        // 3. Falls das aktuell ausgewählte Profil gelöscht wurde, auf "Standard" zurückfallen
        if (CurrentProfile.Equals(profileName, StringComparison.OrdinalIgnoreCase))
        {
            await SwitchProfileAsync("Standard");
        }
    }

    public async Task LoadDocumentsAsync()
    {
        var loaded = await _storageService.LoadDocumentsAsync(CurrentProfile) ?? new List<DocumentItem>();
        Documents.Clear();
        foreach (var item in loaded)
        {
            Documents.Add(item);
        }

        UpdateCounters();
    }

    public async Task AddDocumentAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return;

        if (Documents.Any(d => d.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
            return;

        var chunkCount = await _pdfService.GetChunkCountAsync(filePath);

        var docItem = new DocumentItem
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            IsSelected = true,
            IsMissing = false,
            ExtractedChunkCount = chunkCount
        };

        Documents.Add(docItem);
        UpdateCounters();
        OnPropertyChanged(nameof(Documents));
        await SaveCurrentDocumentsAsync();
    }

    public async Task SaveCurrentDocumentsAsync()
    {
        await _storageService.SaveDocumentsAsync(CurrentProfile, Documents);
    }

    public async Task RemoveDocumentAsync(DocumentItem document)
    {
        if (Documents.Remove(document))
        {
            UpdateCounters();
            await SaveCurrentDocumentsAsync();
        }
    }

    /// <summary>Aktiviert alle Dokumente und speichert den Zustand im Profil.</summary>
    public Task SelectAllAsync() => SetAllSelectedAsync(true);

    /// <summary>Deaktiviert alle Dokumente und speichert den Zustand im Profil.</summary>
    public Task DeselectAllAsync() => SetAllSelectedAsync(false);

    private async Task SetAllSelectedAsync(bool selected)
    {
        foreach (var doc in Documents)
        {
            doc.IsSelected = selected;
        }
        UpdateCounters();
        await SaveCurrentDocumentsAsync();
    }

    /// <summary>
    /// Aktiviert bzw. deaktiviert ein einzelnes Dokument (Häkchen in der Liste),
    /// aktualisiert die Zähler (Statusleiste) und speichert den Zustand im Profil.
    /// </summary>
    public async Task SetDocumentSelectedAsync(DocumentItem document, bool selected)
    {
        if (document.IsSelected == selected)
            return;

        document.IsSelected = selected;
        UpdateCounters();
        await SaveCurrentDocumentsAsync();
    }

    private void UpdateCounters()
    {
        OnPropertyChanged(nameof(TotalDocumentCount));
        OnPropertyChanged(nameof(TotalChunkCount));
        OnPropertyChanged(nameof(ActiveDocumentCount));
        OnPropertyChanged(nameof(FilteredDocuments));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}