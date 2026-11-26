using System.ComponentModel;
using System.Runtime.CompilerServices;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.ViewModels.Resources;

namespace StudyScope.ViewModels;

/// <summary>
/// Einstellungen › 🎨 Darstellung: Farbschema Hell/Dunkel/System, global in <c>config.json</c> unter <c>UiSettings</c>.
/// Die Auswahl wirkt sofort als Vorschau (<see cref="SelectedTheme"/>), gespeichert wird erst mit <see cref="SaveAsync"/>;
/// <see cref="Revert"/> (Schließen ohne Speichern) stellt den gespeicherten Stand wieder her. Das Anwenden in der
/// Oberfläche übernimmt die Razor-Seite über <see cref="ThemeAttribute"/>.
/// </summary>
public class AppearanceViewModel : INotifyPropertyChanged
{
    private readonly IStorageService _storage;

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppearanceViewModel(IStorageService storage) => _storage = storage;

    /// <summary>Auswahl im Formular mit Anzeigetext.</summary>
    public static IReadOnlyList<(ThemeMode Mode, string Label, string Hint)> Options =>
    [
        (ThemeMode.System, VmStrings.Appearance_System, VmStrings.Appearance_System_Hint),
        (ThemeMode.Light, VmStrings.Appearance_Light, VmStrings.Appearance_Light_Hint),
        (ThemeMode.Dark, VmStrings.Appearance_Dark, VmStrings.Appearance_Dark_Hint),
    ];

    private ThemeMode _savedTheme = ThemeMode.System;
    /// <summary>Gespeichertes Farbschema.</summary>
    public ThemeMode SavedTheme { get => _savedTheme; private set { if (_savedTheme == value) return; _savedTheme = value; Changed(); } }

    private ThemeMode _selectedTheme = ThemeMode.System;
    /// <summary>Im Formular gewähltes Farbschema (Vorschau, noch nicht gespeichert).</summary>
    public ThemeMode SelectedTheme { get => _selectedTheme; set { if (_selectedTheme == value) return; _selectedTheme = value; Changed(); } }

    /// <summary>Ungespeicherte Änderung vorhanden.</summary>
    public bool IsDirty => SelectedTheme != SavedTheme;

    private string _statusMessage = string.Empty;
    public string StatusMessage { get => _statusMessage; private set { if (_statusMessage == value) return; _statusMessage = value; Changed(); } }

    /// <summary>Gespeicherten Stand laden (beim Start und beim Öffnen des Abschnitts).</summary>
    public async Task LoadAsync()
    {
        var settings = await _storage.LoadUiSettingsAsync();
        SavedTheme = Enum.IsDefined(settings.Theme) ? settings.Theme : ThemeMode.System;
        SelectedTheme = SavedTheme;
        StatusMessage = string.Empty;
    }

    /// <summary>Auswahl speichern; die übrigen Oberflächen-Einstellungen (Seitenleiste) bleiben erhalten.</summary>
    public async Task SaveAsync()
    {
        var settings = await _storage.LoadUiSettingsAsync();
        settings.Theme = SelectedTheme;
        await _storage.SaveUiSettingsAsync(settings);
        SavedTheme = SelectedTheme;
        StatusMessage = VmStrings.Appearance_Saved;
    }

    /// <summary>Vorschau verwerfen: zurück zum gespeicherten Stand.</summary>
    public void Revert()
    {
        SelectedTheme = SavedTheme;
        StatusMessage = string.Empty;
    }

    /// <summary>Wert für das Skript bzw. <c>data-theme</c>: „system“, „light“ oder „dark“.</summary>
    public static string ThemeAttribute(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => "light",
        ThemeMode.Dark => "dark",
        _ => "system"
    };

    private void Changed([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirty)));
    }
}
