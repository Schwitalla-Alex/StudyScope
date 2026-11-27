using System.ComponentModel;
using System.Runtime.CompilerServices;
using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;

namespace StudyScope.ViewModels;

/// <summary>Was ein Tastendruck bzw. eine Mausraddrehung mit dem Zoom tut.</summary>
public enum ZoomAction
{
    None,
    In,
    Out,
    Reset
}

/// <summary>
/// Zoom der ganzen Oberfläche (Stufen in <see cref="UiZoom"/>), global in <c>config.json</c> unter
/// <c>UiSettings.Zoom</c>. Angewendet wird über den Zoom des WebViews (<see cref="IWindowZoom"/>, Photino) –
/// echte Skalierung durch die Engine, Maus- und Elementkoordinaten bleiben in CSS-Pixeln stimmig.
/// <para>
/// Jede Änderung wirkt und speichert sofort – über Tastatur, Mausrad, Statusleiste und den Abschnitt „Darstellung“
/// gleich (keine Vorschau wie beim Farbschema). Strg + Mausrad und Strg + Plus/Minus/0 fängt
/// <c>studyscope-layout.js</c> ab, damit der eingebaute Zoom von WebView2 nicht unbemerkt daneben läuft und Linux
/// (WebKitGTK zoomt nicht von selbst) genauso reagiert. Zoomt der WebView doch selbst (z. B. Geste im Bericht),
/// übernimmt <see cref="SyncFromWindowAsync"/> den Wert beim nächsten Größenereignis.
/// </para>
/// </summary>
public class ZoomViewModel : INotifyPropertyChanged
{
    /// <summary>Tasten (<c>KeyboardEvent.key</c>) zum Vergrößern; „=“ ist Strg + Plus auf US-Tastaturen ohne Umschalt.</summary>
    public static IReadOnlyList<string> ZoomInKeys { get; } = ["+", "="];

    /// <summary>Tasten zum Verkleinern.</summary>
    public static IReadOnlyList<string> ZoomOutKeys { get; } = ["-"];

    /// <summary>Tasten für die Standardgröße.</summary>
    public static IReadOnlyList<string> ResetKeys { get; } = ["0"];

    /// <summary>Tasten des Ziffernblocks (<c>KeyboardEvent.code</c>): Vergrößern, Verkleinern, Standardgröße.</summary>
    public static IReadOnlyList<string> NumpadCodes { get; } = ["NumpadAdd", "NumpadSubtract", "Numpad0"];

    private readonly IStorageService _storage;
    private readonly IWindowZoom _window;
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public event PropertyChangedEventHandler? PropertyChanged;

    public ZoomViewModel(IStorageService storage, IWindowZoom window)
    {
        _storage = storage;
        _window = window;
    }

    private int _percent = UiZoom.Default;
    /// <summary>Aktuelle Stufe in Prozent.</summary>
    public int Percent
    {
        get => _percent;
        private set
        {
            if (_percent == value) return;
            _percent = value;
            Changed();
            Changed(nameof(PercentText));
            Changed(nameof(IsDefault));
            Changed(nameof(CanZoomIn));
            Changed(nameof(CanZoomOut));
        }
    }

    /// <summary>Anzeige im Format der Oberfläche, z. B. „125 %“.</summary>
    public string PercentText => UiZoom.Format(Percent);

    public bool IsDefault => Percent == UiZoom.Default;
    public bool CanZoomIn => Percent < UiZoom.Max;
    public bool CanZoomOut => Percent > UiZoom.Min;

    /// <summary>Auswahlliste der Stufen (Eigenschaft, damit das Zahlenformat einem Sprachwechsel folgt).</summary>
    public IReadOnlyList<(int Value, string Label)> Options => UiZoom.Levels.Select(l => (l, UiZoom.Format(l))).ToList();

    /// <summary>
    /// Gespeicherte Stufe laden. Das Fenster bekommt sie schon beim Start über Photino (<c>Program.cs</c>), damit
    /// nichts aufblitzt; <see cref="ApplyToWindow"/> gleicht nach dem ersten Zeichnen ab.
    /// </summary>
    public async Task LoadAsync()
    {
        var settings = await _storage.LoadUiSettingsAsync();
        Percent = UiZoom.Normalize(settings.Zoom);
    }

    /// <summary>Fenster auf die geladene Stufe setzen, falls es abweicht (nach dem ersten Zeichnen).</summary>
    public void ApplyToWindow()
    {
        if (_window.Get() != Percent) _window.Set(Percent);
    }

    /// <summary>Stufe setzen (wird auf eine gültige Stufe gerundet), sofort anwenden und speichern.</summary>
    public async Task SetAsync(int percent)
    {
        var level = UiZoom.Normalize(percent);
        if (_window.Get() != level) _window.Set(level);
        if (level == Percent) return;
        Percent = level;
        await SaveAsync(level);
    }

    public Task ZoomInAsync() => SetAsync(UiZoom.StepUp(Percent));
    public Task ZoomOutAsync() => SetAsync(UiZoom.StepDown(Percent));
    public Task ResetAsync() => SetAsync(UiZoom.Default);

    /// <summary>
    /// Zoom-Kürzel? Strg + Plus („+“, „=“, Ziffernblock +), Strg + Minus, Strg + 0. Umschalt ist erlaubt (US-Tastatur
    /// braucht es für „+“), Alt nicht (Strg + Alt ist AltGr), Win/Cmd nicht. Gilt auch in Textfeldern und bei offenem
    /// Menü – wie im Browser. Dieselben Tasten stehen in <c>studyscope-layout.js</c> (Test vergleicht).
    /// </summary>
    public static ZoomAction EvaluateShortcut(string? key, string? code, bool ctrl, bool alt = false, bool meta = false)
    {
        if (!ctrl || alt || meta) return ZoomAction.None;
        if (code == NumpadCodes[0] || ZoomInKeys.Contains(key)) return ZoomAction.In;
        if (code == NumpadCodes[1] || ZoomOutKeys.Contains(key)) return ZoomAction.Out;
        if (code == NumpadCodes[2] || ResetKeys.Contains(key)) return ZoomAction.Reset;
        return ZoomAction.None;
    }

    /// <summary>Strg + Mausrad: nach oben (negatives <c>deltaY</c>) vergrößert, nach unten verkleinert.</summary>
    public static ZoomAction EvaluateWheel(double deltaY) =>
        deltaY < 0 ? ZoomAction.In : deltaY > 0 ? ZoomAction.Out : ZoomAction.None;

    /// <summary>Tastendruck aus der Oberfläche auswerten; true = war ein Zoom-Kürzel.</summary>
    public async Task<bool> HandleShortcutAsync(string? key, string? code, bool ctrl, bool alt = false, bool meta = false)
    {
        var action = EvaluateShortcut(key, code, ctrl, alt, meta);
        await RunAsync(action);
        return action != ZoomAction.None;
    }

    /// <summary>Strg + Mausrad (vom Skript bereits gebündelt: höchstens eine Stufe je Raste bzw. Touchpad-Schub).</summary>
    public Task HandleWheelAsync(double deltaY) => RunAsync(EvaluateWheel(deltaY));

    /// <summary>
    /// Hat der WebView selbst gezoomt (an der App vorbei), den Wert übernehmen: auf die nächste Stufe runden, das
    /// Fenster darauf setzen und speichern. Aufruf bei Größenänderungen des WebViews.
    /// </summary>
    public async Task SyncFromWindowAsync()
    {
        var actual = _window.Get();
        if (actual <= 0 || actual == Percent) return;
        await SetAsync(actual);
    }

    private Task RunAsync(ZoomAction action) => action switch
    {
        ZoomAction.In => ZoomInAsync(),
        ZoomAction.Out => ZoomOutAsync(),
        ZoomAction.Reset => ResetAsync(),
        _ => Task.CompletedTask
    };

    /// <summary>Nur das eigene Feld ändern (vorher laden); schnelle Folgen (Mausrad) nacheinander schreiben.</summary>
    private async Task SaveAsync(int level)
    {
        await _saveLock.WaitAsync();
        try
        {
            var settings = await _storage.LoadUiSettingsAsync();
            if (settings.Zoom == level) return;
            settings.Zoom = level;
            await _storage.SaveUiSettingsAsync(settings);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
