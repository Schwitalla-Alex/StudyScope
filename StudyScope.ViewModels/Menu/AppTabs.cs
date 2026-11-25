namespace StudyScope.ViewModels.Menu;

/// <summary>Reiter der Hauptansicht.</summary>
public sealed record AppTab(string Id, string Icon, string Label)
{
    public string Text => $"{Icon} {Label}";
}

/// <summary>Die Reiter in ihrer festen Reihenfolge (Kennungen wie bisher in <c>App.razor</c>).</summary>
public static class AppTabs
{
    public const string Chat = "chat";
    public const string Cards = "cards";
    public const string Mindmap = "mindmap";
    public const string Presentation = "presentation";
    public const string Quiz = "quiz";
    public const string Tables = "tables";
    public const string Audio = "audio";
    public const string Video = "video";
    public const string Reports = "reports";

    public static IReadOnlyList<AppTab> All { get; } =
    [
        new(Chat, "💬", "Chat & Fragen"),
        new(Cards, "🎴", "Lernkarteikarten"),
        new(Mindmap, "🧠", "Mindmap"),
        new(Presentation, "📽️", "Präsentation"),
        new(Quiz, "❓", "Quiz"),
        new(Tables, "📊", "Datentabelle"),
        new(Audio, "🎧", "Audio"),
        new(Video, "🎬", "Video"),
        new(Reports, "📑", "Berichte"),
    ];

    public static int IndexOf(string id)
    {
        for (var i = 0; i < All.Count; i++)
            if (All[i].Id == id) return i;
        return -1;
    }
}

/// <summary>Aufteilung der Reiter auf die Zeile und das Menü „» Mehr“.</summary>
/// <param name="Visible">Indizes der sichtbaren Reiter in ihrer Reihenfolge.</param>
/// <param name="Overflow">Indizes der Reiter im Menü „» Mehr“ in ihrer Reihenfolge.</param>
public sealed record TabLayout(IReadOnlyList<int> Visible, IReadOnlyList<int> Overflow)
{
    public bool HasOverflow => Overflow.Count > 0;

    public static TabLayout AllVisible(int count) => new(Enumerable.Range(0, count).ToList(), []);
}

/// <summary>
/// Reiter einzeilig halten (rein rechnerisch, testbar): Passen nicht alle in die verfügbare Breite, wandern die
/// hinteren ins Menü „» Mehr“. Der aktive Reiter bleibt immer sichtbar – passt er nicht mehr an seiner Stelle,
/// verdrängt er den letzten noch sichtbaren.
/// </summary>
public static class TabOverflow
{
    /// <param name="widths">Gemessene Breiten aller Reiter (Pixel, inklusive Abstand).</param>
    /// <param name="available">Verfügbare Breite der Reiterzeile.</param>
    /// <param name="moreWidth">Breite des Knopfs „» Mehr“.</param>
    /// <param name="activeIndex">Aktiver Reiter (-1 = keiner).</param>
    public static TabLayout Compute(IReadOnlyList<double> widths, double available, double moreWidth, int activeIndex)
    {
        var count = widths.Count;
        if (count == 0) return new TabLayout([], []);
        // Unbekannte Breite (noch nicht gemessen): alles zeigen, die Zeile schneidet notfalls ab
        if (available <= 0 || widths.Any(w => double.IsNaN(w) || w < 0)) return TabLayout.AllVisible(count);
        if (widths.Sum() <= available) return TabLayout.AllVisible(count);

        var budget = available - moreWidth;
        var visible = new SortedSet<int>();
        var hasActive = activeIndex >= 0 && activeIndex < count;
        if (hasActive)
        {
            // Der aktive Reiter ist gesetzt, auch wenn er allein schon zu breit ist
            visible.Add(activeIndex);
            budget -= widths[activeIndex];
        }

        for (var i = 0; i < count; i++)
        {
            if (hasActive && i == activeIndex) continue;
            if (widths[i] > budget) break;   // Reihenfolge wahren: nach der ersten Lücke nichts mehr dazwischenschieben
            visible.Add(i);
            budget -= widths[i];
        }

        var overflow = Enumerable.Range(0, count).Where(i => !visible.Contains(i)).ToList();
        return new TabLayout(visible.ToList(), overflow);
    }
}
