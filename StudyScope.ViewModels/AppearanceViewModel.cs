using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Localization;
using StudyScope.Core.Models;
using StudyScope.ViewModels.Resources;

namespace StudyScope.ViewModels;

/// <summary>
/// Einstellungen › 🎨 Darstellung: Farbschema Hell/Dunkel/System und Sprache, global in <c>config.json</c> unter
/// <c>UiSettings</c>. Das Farbschema wirkt sofort als Vorschau (<see cref="SelectedTheme"/>), gespeichert wird erst mit
/// <see cref="SaveAsync"/>; <see cref="Revert"/> (Schließen ohne Speichern) stellt den gespeicherten Stand wieder her.
/// Das Anwenden in der Oberfläche übernimmt die Razor-Seite über <see cref="ThemeAttribute"/>.
/// <para>
/// Die Sprache wechselt erst mit „Speichern“ (keine Vorschau): Dann baut die App die Oberfläche neu auf
/// (<see cref="LanguageChanged"/>) – bei einer Vorschau gingen ungespeicherte Eingaben in offenen Formularen verloren.
/// Chatverlauf, Dokumente und laufende Wiedergabe liegen in den ViewModels und bleiben erhalten.
/// </para>
/// </summary>
public class AppearanceViewModel : INotifyPropertyChanged
{
    private readonly IStorageService _storage;
    private readonly IUiLanguageService? _language;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Die Sprache der Oberfläche wurde gewechselt (nach „Speichern“, bereits angewendet).</summary>
    public event EventHandler? LanguageChanged;

    public AppearanceViewModel(IStorageService storage, IUiLanguageService? language = null)
    {
        _storage = storage;
        _language = language;
    }

    /// <summary>Auswahl im Formular mit Anzeigetext.</summary>
    public static IReadOnlyList<(ThemeMode Mode, string Label, string Hint)> Options =>
    [
        (ThemeMode.System, VmStrings.Appearance_System, VmStrings.Appearance_System_Hint),
        (ThemeMode.Light, VmStrings.Appearance_Light, VmStrings.Appearance_Light_Hint),
        (ThemeMode.Dark, VmStrings.Appearance_Dark, VmStrings.Appearance_Dark_Hint),
    ];

    /// <summary>
    /// Auswahl der Sprache: „Wie System“ und jede vorhandene Sprache in ihrem eigenen Namen („Deutsch“, „English“) –
    /// so findet jeder seine Sprache, auch wenn er die aktuelle Oberfläche nicht lesen kann. Eigenschaft statt Feld,
    /// damit „Wie System“ nach einem Wechsel in der neuen Sprache erscheint.
    /// </summary>
    public IReadOnlyList<(string Value, string Label)> LanguageOptions =>
        new[] { (UiLanguage.SystemSetting, VmStrings.Appearance_LanguageSystem) }
            .Concat((_language?.AvailableCultures ?? [UiLanguage.NeutralLanguage]).Select(c => (c, UiLanguage.NativeName(c))))
            .ToList();

    /// <summary>Sprache erzwungen über STUDYSCOPE_UI_CULTURE: Die Auswahl wird gespeichert, wirkt aber erst ohne die Variable.</summary>
    public string? LanguageOverride => _language?.OverrideCulture;

    private ThemeMode _savedTheme = ThemeMode.System;
    /// <summary>Gespeichertes Farbschema.</summary>
    public ThemeMode SavedTheme { get => _savedTheme; private set { if (_savedTheme == value) return; _savedTheme = value; Changed(); } }

    private ThemeMode _selectedTheme = ThemeMode.System;
    /// <summary>Im Formular gewähltes Farbschema (Vorschau, noch nicht gespeichert).</summary>
    public ThemeMode SelectedTheme { get => _selectedTheme; set { if (_selectedTheme == value) return; _selectedTheme = value; Changed(); } }

    private string _savedLanguage = UiLanguage.SystemSetting;
    /// <summary>Gespeicherte Sprache („system“ oder Kulturname).</summary>
    public string SavedLanguage { get => _savedLanguage; private set { if (_savedLanguage == value) return; _savedLanguage = value; Changed(); } }

    private string _selectedLanguage = UiLanguage.SystemSetting;
    /// <summary>Im Formular gewählte Sprache (wirkt erst nach „Speichern“).</summary>
    public string SelectedLanguage
    {
        get => _selectedLanguage;
        set { var v = NormalizeLanguage(value); if (_selectedLanguage == v) return; _selectedLanguage = v; Changed(); }
    }

    /// <summary>Ungespeicherte Änderung vorhanden.</summary>
    public bool IsDirty => SelectedTheme != SavedTheme || SelectedLanguage != SavedLanguage;

    private string _statusMessage = string.Empty;
    public string StatusMessage { get => _statusMessage; private set { if (_statusMessage == value) return; _statusMessage = value; Changed(); } }

    /// <summary>Gespeicherten Stand laden (beim Start und beim Öffnen des Abschnitts).</summary>
    public async Task LoadAsync()
    {
        var settings = await _storage.LoadUiSettingsAsync();
        SavedTheme = Enum.IsDefined(settings.Theme) ? settings.Theme : ThemeMode.System;
        SelectedTheme = SavedTheme;
        SavedLanguage = NormalizeLanguage(settings.Language);
        SelectedLanguage = SavedLanguage;
        // Nach einem Sprachwechsel lädt der neu aufgebaute Abschnitt erneut – die Bestätigung bleibt dann stehen
        if (!_keepStatusOnce) StatusMessage = string.Empty;
        _keepStatusOnce = false;
    }

    private bool _keepStatusOnce;

    /// <summary>
    /// Auswahl speichern; die übrigen Oberflächen-Einstellungen (Seitenleiste) bleiben erhalten. Ändert sich dadurch
    /// die wirksame Sprache, wird sie sofort angewendet und danach <see cref="LanguageChanged"/> ausgelöst.
    /// </summary>
    public async Task SaveAsync()
    {
        var settings = await _storage.LoadUiSettingsAsync();
        settings.Theme = SelectedTheme;
        settings.Language = SelectedLanguage;
        await _storage.SaveUiSettingsAsync(settings);
        SavedTheme = SelectedTheme;

        var languageChanged = SavedLanguage != SelectedLanguage;
        SavedLanguage = SelectedLanguage;
        var switched = false;
        if (languageChanged && _language != null)
        {
            var culture = _language.Resolve(SavedLanguage);
            if (!string.Equals(culture.Name, CultureInfo.CurrentUICulture.Name, StringComparison.OrdinalIgnoreCase))
            {
                _language.Apply(culture);
                switched = true;
            }
        }

        // Meldung erst nach dem Wechsel: Sie erscheint so schon in der neuen Sprache
        StatusMessage = VmStrings.Appearance_Saved;
        if (switched)
        {
            _keepStatusOnce = true;
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Vorschau verwerfen: zurück zum gespeicherten Stand.</summary>
    public void Revert()
    {
        SelectedTheme = SavedTheme;
        SelectedLanguage = SavedLanguage;
        StatusMessage = string.Empty;
    }

    /// <summary>Wert für das Skript bzw. <c>data-theme</c>: „system“, „light“ oder „dark“.</summary>
    public static string ThemeAttribute(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => "light",
        ThemeMode.Dark => "dark",
        _ => "system"
    };

    /// <summary>„system“ oder ein bekannter Kulturname; leer bzw. unbekannt gilt als „system“.</summary>
    public static string NormalizeLanguage(string? value)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v) || string.Equals(v, UiLanguage.SystemSetting, StringComparison.OrdinalIgnoreCase))
            return UiLanguage.SystemSetting;
        try
        {
            var culture = CultureInfo.GetCultureInfo(v, predefinedOnly: true);
            return string.IsNullOrEmpty(culture.Name) ? UiLanguage.SystemSetting : culture.Name;
        }
        catch (CultureNotFoundException)
        {
            return UiLanguage.SystemSetting;
        }
    }

    private void Changed([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirty)));
    }
}
