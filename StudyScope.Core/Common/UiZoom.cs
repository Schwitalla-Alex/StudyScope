using System.Globalization;

namespace StudyScope.Core.Common;

/// <summary>
/// Stufen für den Zoom der ganzen Oberfläche (☰ › Einstellungen › Darstellung, Strg + Mausrad, Strg + Plus/Minus/0).
/// Rein rechnerisch; angewendet wird der Wert über den Zoom des WebViews (<see cref="Interfaces.IWindowZoom"/>).
/// Höchststufe 150 %: Bei der Mindestfenstergröße 1000 × 700 bleiben dann noch ≈ 667 × 467 CSS-Pixel.
/// </summary>
public static class UiZoom
{
    /// <summary>Standardgröße in Prozent.</summary>
    public const int Default = 100;

    /// <summary>Wählbare Stufen in Prozent, aufsteigend.</summary>
    public static IReadOnlyList<int> Levels { get; } = [80, 90, 100, 110, 125, 150];

    public static int Min => Levels[0];
    public static int Max => Levels[^1];

    /// <summary>
    /// Gültige Stufe zu einem gespeicherten oder vom WebView gemeldeten Wert: fehlend/ungültig (≤ 0) → 100,
    /// Zwischenwerte → nächstgelegene Stufe (bei Gleichstand die kleinere), außerhalb → Rand.
    /// </summary>
    public static int Normalize(int percent)
    {
        if (percent <= 0) return Default;
        var best = Levels[0];
        foreach (var level in Levels)
            if (Math.Abs(level - percent) < Math.Abs(best - percent)) best = level;
        return best;
    }

    /// <summary>Nächstgrößere Stufe; an der Obergrenze bleibt es.</summary>
    public static int StepUp(int percent)
    {
        var current = Normalize(percent);
        foreach (var level in Levels)
            if (level > current) return level;
        return Max;
    }

    /// <summary>Nächstkleinere Stufe; an der Untergrenze bleibt es.</summary>
    public static int StepDown(int percent)
    {
        var current = Normalize(percent);
        for (var i = Levels.Count - 1; i >= 0; i--)
            if (Levels[i] < current) return Levels[i];
        return Min;
    }

    /// <summary>Anzeige als Prozentwert im Format der Oberfläche („125 %“ bzw. „125%“).</summary>
    public static string Format(int percent, CultureInfo? culture = null) =>
        (percent / 100.0).ToString("P0", culture ?? CultureInfo.CurrentCulture);
}
