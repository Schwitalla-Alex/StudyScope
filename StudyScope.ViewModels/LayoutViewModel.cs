using System.ComponentModel;
using System.Runtime.CompilerServices;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;

namespace StudyScope.ViewModels;

/// <summary>Tastendruck, wie ihn die Oberfläche meldet (Strg+B zum Ein-/Ausklappen der Seitenleiste).</summary>
/// <param name="Key">Taste (<c>KeyboardEvent.key</c>), z. B. „b“.</param>
/// <param name="FocusTag">Tag des fokussierten Elements (z. B. „INPUT“), leer = nichts fokussiert.</param>
/// <param name="FocusInputType">Bei <c>INPUT</c> der Typ (z. B. „text“, „checkbox“).</param>
/// <param name="FocusEditable">Fokussiertes Element ist <c>contenteditable</c>.</param>
public sealed record ShortcutKey(
    string Key,
    bool Ctrl,
    bool Alt = false,
    bool Shift = false,
    bool Meta = false,
    bool Repeat = false,
    string? FocusTag = null,
    string? FocusInputType = null,
    bool FocusEditable = false);

/// <summary>
/// Linke Seitenleiste ein-/ausklappen: gemerkter Zustand (global in <c>config.json</c> unter <c>UiSettings</c>),
/// automatisches Einklappen bei schmalem Fenster ohne den gemerkten Zustand zu überschreiben, Strg+B mit den
/// Bedingungen (nicht beim Schreiben, nicht im Vortrags-/Vollbildmodus) und der Hinweis auf die aktiven Dokumente.
/// </summary>
public class LayoutViewModel : INotifyPropertyChanged
{
    /// <summary>Unterhalb dieser Fensterbreite (CSS-Pixel) ist die Seitenleiste automatisch eingeklappt.</summary>
    public const double NarrowWindowWidth = 900;

    /// <summary>Höchstens so viele Dokumentnamen im Tooltip des Hinweises.</summary>
    public const int MaxTooltipDocuments = 10;

    private readonly IStorageService _storage;

    public event PropertyChangedEventHandler? PropertyChanged;

    public LayoutViewModel(IStorageService storage) => _storage = storage;

    private bool _userCollapsed;
    /// <summary>Vom Benutzer gewählter (und gespeicherter) Zustand.</summary>
    public bool UserCollapsed { get => _userCollapsed; private set { if (_userCollapsed == value) return; _userCollapsed = value; Changed(); } }

    private bool _isNarrowWindow;
    /// <summary>Fenster ist schmaler als <see cref="NarrowWindowWidth"/>.</summary>
    public bool IsNarrowWindow { get => _isNarrowWindow; private set { if (_isNarrowWindow == value) return; _isNarrowWindow = value; Changed(); } }

    private bool _expandedWhileNarrow;
    /// <summary>Bei schmalem Fenster vorübergehend aufgeklappt (wird nicht gespeichert).</summary>
    public bool ExpandedWhileNarrow { get => _expandedWhileNarrow; private set { if (_expandedWhileNarrow == value) return; _expandedWhileNarrow = value; Changed(); } }

    /// <summary>Tatsächlich angezeigter Zustand.</summary>
    public bool IsSidebarCollapsed => UserCollapsed || (IsNarrowWindow && !ExpandedWhileNarrow);

    /// <summary>Automatisch eingeklappt, weil das Fenster schmal ist (für den Hinweis im Tooltip).</summary>
    public bool IsAutoCollapsed => IsSidebarCollapsed && !UserCollapsed;

    public string ToggleTooltip => IsSidebarCollapsed
        ? IsAutoCollapsed ? "Seitenleiste einblenden (Strg+B) – bei schmalem Fenster automatisch ausgeblendet" : "Seitenleiste einblenden (Strg+B)"
        : "Seitenleiste ausblenden (Strg+B)";

    /// <summary>Gemerkten Zustand laden (beim Start).</summary>
    public async Task LoadAsync()
    {
        var settings = await _storage.LoadUiSettingsAsync();
        UserCollapsed = settings.SidebarCollapsed;
    }

    /// <summary>
    /// Umschalten. Bei breitem Fenster wird der neue Zustand gespeichert; bei schmalem Fenster gilt das Auf-/Zuklappen
    /// nur vorübergehend, der gemerkte Zustand bleibt unverändert.
    /// </summary>
    public async Task ToggleAsync()
    {
        if (IsNarrowWindow && !UserCollapsed)
        {
            ExpandedWhileNarrow = !ExpandedWhileNarrow;
            return;
        }

        UserCollapsed = !UserCollapsed;
        // Aus dem gemerkten Zustand „eingeklappt“ heraus aufgeklappt: auch im schmalen Fenster sichtbar lassen
        ExpandedWhileNarrow = IsNarrowWindow && !UserCollapsed;
        await _storage.SaveUiSettingsAsync(new UiSettings { SidebarCollapsed = UserCollapsed });
    }

    /// <summary>Fensterbreite (CSS-Pixel) melden; wird das Fenster wieder breit, gilt wieder der gemerkte Zustand.</summary>
    public void SetWindowWidth(double width)
    {
        if (width <= 0) return;
        var narrow = width < NarrowWindowWidth;
        if (!narrow) ExpandedWhileNarrow = false;
        IsNarrowWindow = narrow;
    }

    /// <summary>
    /// Soll dieser Tastendruck die Seitenleiste umschalten? Nur Strg+B ohne Alt/Umschalt/Win, nicht gehalten, nicht
    /// während in ein Eingabefeld/Textbereich geschrieben wird und nicht im Vortrags-/Vollbildmodus.
    /// </summary>
    public static bool IsToggleShortcut(ShortcutKey key, bool isPresenting)
    {
        if (isPresenting) return false;
        if (!key.Ctrl || key.Alt || key.Shift || key.Meta || key.Repeat) return false;
        if (!string.Equals(key.Key, "b", StringComparison.OrdinalIgnoreCase)) return false;
        return !IsTextEntry(key);
    }

    /// <summary>Hat ein Feld den Fokus, in das geschrieben wird (Textfeld, Textbereich, Auswahlliste, contenteditable)?</summary>
    public static bool IsTextEntry(ShortcutKey key)
    {
        if (key.FocusEditable) return true;
        var tag = key.FocusTag?.ToUpperInvariant();
        if (tag is "TEXTAREA" or "SELECT") return true;
        if (tag != "INPUT") return false;
        var type = (key.FocusInputType ?? "text").ToLowerInvariant();
        return type is not ("checkbox" or "radio" or "button" or "submit" or "reset" or "range" or "color" or "file" or "image");
    }

    /// <summary>Strg+B auswerten: schaltet um und meldet true, wenn der Tastendruck verarbeitet wurde.</summary>
    public async Task<bool> HandleShortcutAsync(ShortcutKey key, bool isPresenting)
    {
        if (!IsToggleShortcut(key, isPresenting)) return false;
        await ToggleAsync();
        return true;
    }

    /// <summary>Hinweis in der eingeklappten Leiste, z. B. „📄 1/2“.</summary>
    public static string DocumentBadge(IReadOnlyCollection<DocumentItem> documents) => "📄 " + DocumentCount(documents);

    /// <summary>Aktive/alle Dokumente, z. B. „1/2“ (aktiv = angehakt und vorhanden).</summary>
    public static string DocumentCount(IReadOnlyCollection<DocumentItem> documents) =>
        $"{ActiveDocuments(documents).Count()}/{documents.Count}";

    /// <summary>Tooltip des Hinweises: Profil und Namen der aktiven Dokumente.</summary>
    public static string DocumentBadgeTooltip(string profile, IReadOnlyCollection<DocumentItem> documents)
    {
        var active = ActiveDocuments(documents).Select(d => d.FileName).ToList();
        var lines = new List<string> { $"Profil: {profile}" };
        if (documents.Count == 0)
            lines.Add("Keine Dokumente geladen");
        else if (active.Count == 0)
            lines.Add($"Keines von {documents.Count} Dokumenten aktiv");
        else
        {
            lines.Add($"Aktiv ({active.Count} von {documents.Count}):");
            lines.AddRange(active.Take(MaxTooltipDocuments).Select(name => "• " + name));
            if (active.Count > MaxTooltipDocuments)
                lines.Add($"… und {active.Count - MaxTooltipDocuments} weitere");
        }
        lines.Add("Klicken zum Einblenden");
        return string.Join("\n", lines);
    }

    private static IEnumerable<DocumentItem> ActiveDocuments(IEnumerable<DocumentItem> documents) =>
        documents.Where(d => d.IsSelected && !d.IsMissing);

    private void Changed([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSidebarCollapsed)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAutoCollapsed)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ToggleTooltip)));
    }
}
