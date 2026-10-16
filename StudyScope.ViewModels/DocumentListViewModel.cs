using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;

namespace StudyScope.ViewModels;

/// <summary>
/// StudyScope.ViewModels mit der Logik für die Dokumentenliste, Filterung/Suche, 
/// das Auswählen/Abwählen und die Statusbar-Statistiken, das Suche und Zählung von Dokumenten und Textstellen, Markieren aller Dokumente, Löschen fehlender Dateien.
/// </summary>
public class DocumentListViewModel : INotifyPropertyChanged
{
    private readonly IStorageService _storageService;
    private readonly IDocumentService _pdfService;
    private string _searchText = string.Empty;
    private string _currentProfile = "Standard";

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

    public int TotalDocumentCount => Documents.Count;

    public int TotalChunkCount => Documents.Sum(d => d.ExtractedChunkCount);

    private ObservableCollection<string> _availableProfiles = new();
    public ObservableCollection<string> AvailableProfiles
    {
        get => _availableProfiles;
        private set
        {
            _availableProfiles = value;
            OnPropertyChanged();
        }
    }

    public async Task InitializeAsync()
    {
        // 1. Alle vorhandenen Profile aus dem Ordner laden
        var profiles = await _storageService.GetAllProfileNamesAsync();
        AvailableProfiles = new ObservableCollection<string>(profiles);

        // 2. Zuletzt aktives Profil laden
        var lastProfile = await _storageService.GetLastActiveProfileNameAsync();

        // 3. Wenn das zuletzt aktive Profil existiert, dieses nehmen, sonst das allererste aus der Liste
        if (!string.IsNullOrWhiteSpace(lastProfile) && AvailableProfiles.Contains(lastProfile))
        {
            CurrentProfile = lastProfile;
        }
        else
        {
            CurrentProfile = AvailableProfiles.FirstOrDefault() ?? "Standard";
        }

        // 4. Dokumente für das gewählte Profil laden
        await LoadDocumentsAsync();
    }

    public async Task SwitchProfileAsync(string newProfile)
    {
        if (string.IsNullOrWhiteSpace(newProfile) || newProfile == CurrentProfile)
            return;

        CurrentProfile = newProfile;
        await _storageService.SetLastActiveProfileNameAsync(newProfile);
        await LoadDocumentsAsync();
    }

    public async Task CreateNewProfileAsync(string newProfileName)
    {
        var trimmed = newProfileName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return;

        // 1. Zur Liste der verfügbaren Profile hinzufügen, falls noch nicht vorhanden
        if (!AvailableProfiles.Any(p => p.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            AvailableProfiles.Add(trimmed);
            OnPropertyChanged(nameof(AvailableProfiles));
        }

        // 2. Aktuelles Profil setzen
        CurrentProfile = trimmed;

        // 3. Leere Dokumentenliste für das neue Profil sofort als JSON auf die Festplatte schreiben
        await _storageService.SaveDocumentsAsync(trimmed, new List<DocumentItem>());

        // 4. Als zuletzt aktives Profil in den Einstellungen sichern
        await _storageService.SetLastActiveProfileNameAsync(trimmed);

        // 5. Ansicht aktualisieren (leert die Dokumentenliste für das neue Profil)
        await LoadDocumentsAsync();
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

        // 3. Falls das aktuell ausgewählte Profil gelöscht wurde, auf "Standard" zurücksetzen
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

        // Prüfen, ob das Dokument bereits in der Liste existiert
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
