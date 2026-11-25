using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StudyScope.ViewModels.Menu;

/// <summary>Eintrag eines Menüs (Hamburger-Menü, „» Mehr“ der Reiterleiste). Mit <see cref="Children"/> ein Untermenü.</summary>
/// <param name="Id">Kennung, die beim Auslösen gemeldet wird.</param>
/// <param name="SeparatorBefore">Trennlinie vor dem Eintrag.</param>
/// <param name="IsDanger">Rot dargestellt (z. B. „Anwendung schließen“).</param>
/// <param name="IsCurrent">Markiert (z. B. der aktive Reiter im Überlaufmenü).</param>
public sealed record MenuEntry(
    string Id,
    string Text,
    string Icon = "",
    IReadOnlyList<MenuEntry>? Children = null,
    bool SeparatorBefore = false,
    bool IsDanger = false,
    bool IsCurrent = false)
{
    public bool HasChildren => Children is { Count: > 0 };
}

/// <summary>Ergebnis eines Tastendrucks im Menü.</summary>
/// <param name="Handled">Taste verarbeitet – die Oberfläche unterdrückt die Standardaktion des Browsers.</param>
/// <param name="ActivatedId">Ausgelöster Eintrag (Menü ist dann geschlossen).</param>
/// <param name="Closed">Menü wurde geschlossen.</param>
/// <param name="ReturnFocus">Fokus zurück auf den Menüknopf (Esc, Auslösen) – nicht bei Tab, dort wandert der Fokus weiter.</param>
public sealed record MenuKeyResult(bool Handled, string? ActivatedId = null, bool Closed = false, bool ReturnFocus = false)
{
    public static readonly MenuKeyResult Ignored = new(false);
    public static readonly MenuKeyResult Moved = new(true);
}

/// <summary>
/// Zustand und Tastaturbedienung eines Ausklappmenüs mit höchstens einer Untermenü-Ebene, ohne Oberfläche (testbar):
/// ↑/↓ mit Umlauf, Pos1/Ende, → bzw. Enter/Leertaste öffnet ein Untermenü, ← und Esc schließen eine Ebene,
/// Enter/Leertaste löst aus, Tab schließt. Welcher Eintrag den Fokus hat, liefert <see cref="FocusedElementId"/>.
/// </summary>
public class MenuNavigator : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Präfix der Element-IDs in der Oberfläche, z. B. „ss-appmenu“ → „ss-appmenu-item-2“.</summary>
    public string ElementPrefix { get; }

    public MenuNavigator(string elementPrefix) => ElementPrefix = elementPrefix;

    private IReadOnlyList<MenuEntry> _items = [];
    public IReadOnlyList<MenuEntry> Items { get => _items; private set { _items = value; Changed(); } }

    private bool _isOpen;
    public bool IsOpen { get => _isOpen; private set { if (_isOpen == value) return; _isOpen = value; Changed(); } }

    private int _activeIndex = -1;
    /// <summary>Hervorgehobener Eintrag der obersten Ebene (-1 = keiner).</summary>
    public int ActiveIndex { get => _activeIndex; private set { if (_activeIndex == value) return; _activeIndex = value; Changed(); } }

    private int? _submenuIndex;
    /// <summary>Eintrag der obersten Ebene, dessen Untermenü offen ist.</summary>
    public int? SubmenuIndex { get => _submenuIndex; private set { if (_submenuIndex == value) return; _submenuIndex = value; Changed(); } }

    private int _subActiveIndex = -1;
    /// <summary>Hervorgehobener Eintrag im offenen Untermenü (-1 = keiner).</summary>
    public int SubActiveIndex { get => _subActiveIndex; private set { if (_subActiveIndex == value) return; _subActiveIndex = value; Changed(); } }

    /// <summary>Einträge des offenen Untermenüs (leer, wenn keins offen ist).</summary>
    public IReadOnlyList<MenuEntry> SubmenuItems =>
        SubmenuIndex is int i && i < Items.Count && Items[i].Children is { } c ? c : [];

    /// <summary>Element-ID des Eintrags, der den Tastaturfokus haben soll (null = Menü zu).</summary>
    public string? FocusedElementId
    {
        get
        {
            if (!IsOpen) return null;
            if (SubmenuIndex is int s && SubActiveIndex >= 0) return SubItemElementId(s, SubActiveIndex);
            return ActiveIndex >= 0 ? ItemElementId(ActiveIndex) : null;
        }
    }

    public string ItemElementId(int index) => $"{ElementPrefix}-item-{index}";
    public string SubItemElementId(int parent, int index) => $"{ElementPrefix}-item-{parent}-{index}";

    /// <summary>Einträge setzen; ein offenes Menü bleibt offen, die Hervorhebung wird begrenzt.</summary>
    public void SetItems(IReadOnlyList<MenuEntry> items)
    {
        Items = items;
        if (ActiveIndex >= items.Count) ActiveIndex = items.Count - 1;
        if (SubmenuIndex is int s && (s >= items.Count || !items[s].HasChildren)) CloseSubmenu();
    }

    /// <summary>Öffnen; der erste Eintrag (bzw. mit <paramref name="focusLast"/> der letzte) wird hervorgehoben.</summary>
    public void Open(bool focusLast = false)
    {
        if (Items.Count == 0) return;
        CloseSubmenu();
        ActiveIndex = focusLast ? Items.Count - 1 : 0;
        IsOpen = true;
    }

    public void Close()
    {
        CloseSubmenu();
        ActiveIndex = -1;
        IsOpen = false;
    }

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    private void CloseSubmenu()
    {
        SubmenuIndex = null;
        SubActiveIndex = -1;
    }

    /// <summary>
    /// Klick auf einen Eintrag der obersten Ebene: öffnet bzw. schließt sein Untermenü oder löst ihn aus
    /// (liefert dann die Kennung, das Menü ist geschlossen).
    /// </summary>
    public string? Click(int index)
    {
        if (index < 0 || index >= Items.Count) return null;
        ActiveIndex = index;
        if (Items[index].HasChildren)
        {
            if (SubmenuIndex == index) CloseSubmenu();
            else OpenSubmenu(index, focusFirst: false);
            return null;
        }
        Close();
        return Items[index].Id;
    }

    /// <summary>Klick auf einen Eintrag des offenen Untermenüs: löst ihn aus, das Menü ist danach geschlossen.</summary>
    public string? ClickSub(int parent, int index)
    {
        if (parent < 0 || parent >= Items.Count || Items[parent].Children is not { } children) return null;
        if (index < 0 || index >= children.Count) return null;
        Close();
        return children[index].Id;
    }

    /// <summary>Maus über einem Eintrag der obersten Ebene: hervorheben, Untermenü öffnen bzw. ein anderes schließen.</summary>
    public void Hover(int index)
    {
        if (!IsOpen || index < 0 || index >= Items.Count) return;
        ActiveIndex = index;
        if (Items[index].HasChildren) { if (SubmenuIndex != index) OpenSubmenu(index, focusFirst: false); }
        else CloseSubmenu();
    }

    /// <summary>Maus über einem Eintrag des Untermenüs.</summary>
    public void HoverSub(int index)
    {
        if (SubmenuIndex is null || index < 0 || index >= SubmenuItems.Count) return;
        SubActiveIndex = index;
    }

    private void OpenSubmenu(int index, bool focusFirst)
    {
        SubmenuIndex = index;
        SubActiveIndex = focusFirst ? 0 : -1;
    }

    /// <summary>Tastendruck (<c>KeyboardEvent.key</c>) bei offenem Menü.</summary>
    public MenuKeyResult HandleKey(string key)
    {
        if (!IsOpen) return MenuKeyResult.Ignored;

        if (key == "Tab")
        {
            Close();
            return new MenuKeyResult(Handled: false, Closed: true);
        }

        if (SubmenuIndex is int parent && SubActiveIndex >= 0)
            return HandleSubmenuKey(parent, key);

        switch (key)
        {
            case "ArrowDown":
                ActiveIndex = Wrap(ActiveIndex + 1, Items.Count);
                CloseSubmenuIfOther();
                return MenuKeyResult.Moved;
            case "ArrowUp":
                ActiveIndex = Wrap(ActiveIndex < 0 ? Items.Count - 1 : ActiveIndex - 1, Items.Count);
                CloseSubmenuIfOther();
                return MenuKeyResult.Moved;
            case "Home":
                ActiveIndex = 0;
                CloseSubmenuIfOther();
                return MenuKeyResult.Moved;
            case "End":
                ActiveIndex = Items.Count - 1;
                CloseSubmenuIfOther();
                return MenuKeyResult.Moved;
            case "ArrowRight":
                if (ActiveIndex >= 0 && Items[ActiveIndex].HasChildren) OpenSubmenu(ActiveIndex, focusFirst: true);
                return MenuKeyResult.Moved;
            case "ArrowLeft":
                // Oberste Ebene: nichts zu schließen; Taste trotzdem verbrauchen, damit nichts scrollt
                return MenuKeyResult.Moved;
            case "Enter":
            case " ":
                if (ActiveIndex < 0) return MenuKeyResult.Moved;
                if (Items[ActiveIndex].HasChildren)
                {
                    OpenSubmenu(ActiveIndex, focusFirst: true);
                    return MenuKeyResult.Moved;
                }
                var id = Items[ActiveIndex].Id;
                Close();
                return new MenuKeyResult(true, id, Closed: true, ReturnFocus: true);
            case "Escape":
                Close();
                return new MenuKeyResult(true, Closed: true, ReturnFocus: true);
            default:
                return MenuKeyResult.Ignored;
        }
    }

    private MenuKeyResult HandleSubmenuKey(int parent, string key)
    {
        var children = SubmenuItems;
        switch (key)
        {
            case "ArrowDown":
                SubActiveIndex = Wrap(SubActiveIndex + 1, children.Count);
                return MenuKeyResult.Moved;
            case "ArrowUp":
                SubActiveIndex = Wrap(SubActiveIndex - 1, children.Count);
                return MenuKeyResult.Moved;
            case "Home":
                SubActiveIndex = 0;
                return MenuKeyResult.Moved;
            case "End":
                SubActiveIndex = children.Count - 1;
                return MenuKeyResult.Moved;
            case "ArrowLeft":
            case "Escape":
                // Eine Ebene zurück: Fokus wieder auf den Eintrag mit dem Untermenü
                CloseSubmenu();
                ActiveIndex = parent;
                return MenuKeyResult.Moved;
            case "ArrowRight":
                return MenuKeyResult.Moved;
            case "Enter":
            case " ":
                var id = children[SubActiveIndex].Id;
                Close();
                return new MenuKeyResult(true, id, Closed: true, ReturnFocus: true);
            default:
                return MenuKeyResult.Ignored;
        }
    }

    private void CloseSubmenuIfOther()
    {
        if (SubmenuIndex is int s && s != ActiveIndex) CloseSubmenu();
    }

    private static int Wrap(int index, int count) => count == 0 ? -1 : ((index % count) + count) % count;

    private void Changed([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FocusedElementId)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SubmenuItems)));
    }
}
