namespace StudyScope.Infrastructure.Services;

/// <summary>
/// Bereitet Dateifilter für die nativen Photino-Dialoge auf.
///
/// Hintergrund (Photino.NET 4.0.16 / Photino.Native 4.0.22): Photino.NET macht aus jedem Filter die Zeichenkette
/// "Name|*.pdf;*.txt". Der Windows-Dialog versteht ";"-getrennte Muster, die Linux-Implementierung
/// (Photino.Linux.Dialog.cpp, AddFilters) trennt dagegen nur das ERSTE Muster per "|" ab und übergibt den Rest
/// "*.pdf;*.txt" als ein einziges GTK-Muster – das auf keine Datei passt. Ein Filter mit mehreren Endungen
/// zeigt unter Linux deshalb gar keine Dateien an.
///
/// Workaround für Linux: Das erste Muster wird mit "|" an das zweite gehängt ("*.pdf|*.txt"). Photino.NET macht
/// daraus "Name|*.pdf|*.txt;*.csv", und der native Code zerlegt das korrekt in "*.pdf", "*.txt", "*.csv".
/// Zusätzlich werden unter Linux Großbuchstaben-Varianten ergänzt, weil GTK-3-Muster Groß-/Kleinschreibung beachten.
/// </summary>
public static class FileDialogFilters
{
    public static (string Name, string[] Patterns)[] ForPhotino(
        IEnumerable<(string Name, string[] Patterns)> filters,
        bool isLinux)
    {
        if (!isLinux)
            return filters.ToArray();

        return filters.Select(f => (f.Name, EncodeForLinux(f.Patterns))).ToArray();
    }

    private static string[] EncodeForLinux(string[] patterns)
    {
        var all = patterns
            .SelectMany(p => new[] { p, p.ToUpperInvariant() })
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (all.Count <= 1)
            return all.ToArray();

        // Erstes und zweites Muster mit "|" verbinden, Rest bleibt ";"-getrennt (siehe Klassenkommentar)
        return new[] { $"{all[0]}|{all[1]}" }.Concat(all.Skip(2)).ToArray();
    }
}
