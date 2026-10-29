using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;

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
    private string _currentAiModelDisplayName = "Lade Modell...";
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

    public async Task RefreshCurrentAiModelAsync()
    {
        var aiConfig = await _storageService.LoadAiConfigForProfileAsync(CurrentProfile);

        if (aiConfig != null && !string.IsNullOrWhiteSpace(aiConfig.SelectedModelName))
        {
            CurrentAiModelDisplayName = aiConfig.SelectedModelName;
        }
        else if (aiConfig != null && !string.IsNullOrWhiteSpace(aiConfig.ModelName))
        {
            CurrentAiModelDisplayName = aiConfig.ModelName;
        }
        else
        {
            CurrentAiModelDisplayName = "Keine KI konfiguriert";
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

    public async Task CreateNewProfileAsync(string newProfileName)
    {
        var trimmed = newProfileName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return;

        // 1. In Liste der verfügbaren Profile aufnehmen
        if (!AvailableProfiles.Any(p => p.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            AvailableProfiles.Add(trimmed);
            OnPropertyChanged(nameof(AvailableProfiles));
        }

        CurrentProfile = trimmed;

        // 2. Initiales Profil ausschließlich über den Storage-Service anlegen (kein harter File-Zugriff)
        await _storageService.SaveDocumentsAsync(trimmed, new List<DocumentItem>());
        await _storageService.SetLastActiveProfileNameAsync(trimmed);

        // 3. Ansicht aktualisieren
        await LoadDocumentsAsync();
        await RefreshCurrentAiModelAsync();
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

    public void SelectAll()
    {
        foreach (var doc in Documents)
        {
            doc.IsSelected = true;
        }
        UpdateCounters();
    }

    public void DeselectAll()
    {
        foreach (var doc in Documents)
        {
            doc.IsSelected = false;
        }
        UpdateCounters();
    }

    private void UpdateCounters()
    {
        OnPropertyChanged(nameof(TotalDocumentCount));
        OnPropertyChanged(nameof(TotalChunkCount));
        OnPropertyChanged(nameof(FilteredDocuments));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}