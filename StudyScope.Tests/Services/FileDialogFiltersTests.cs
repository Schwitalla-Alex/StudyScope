using NUnit.Framework;
using StudyScope.Infrastructure.Services;

namespace StudyScope.Tests.Services;

/// <summary>
/// Prüft den Linux-Workaround für Photino-Dateifilter. Die Hilfsmethoden bilden das Verhalten von
/// Photino.NET 4.0.16 (GetNativeFilters, per Reflection verifiziert) und Photino.Native 4.0.22
/// (AddFilters unter Linux bzw. Windows) nach.
/// </summary>
[TestFixture]
public class FileDialogFiltersTests
{
    private static readonly (string Name, string[] Patterns)[] Dokumentfilter =
    {
        ("Unterstützte Dokumente (*.pdf, *.txt, *.csv)", new[] { "*.pdf", "*.txt", "*.csv" }),
        ("PDF-Dateien (*.pdf)", new[] { "*.pdf" }),
        ("Alle Dateien (*.*)", new[] { "*.*" })
    };

    /// <summary>Photino.NET: "Name|" + Muster mit ";" verbunden.</summary>
    private static string PhotinoNetJoin((string Name, string[] Patterns) f) =>
        $"{f.Name}|{string.Join(";", f.Patterns)}";

    /// <summary>Photino.Native Linux (strtok): Name per "|", erstes Muster per "|", danach per ";".</summary>
    private static List<string> LinuxNativeParse(string native)
    {
        var tokens = new List<string>();
        var rest = native[(native.IndexOf('|') + 1)..];          // Name abtrennen
        var bar = rest.IndexOf('|');
        tokens.Add(bar < 0 ? rest : rest[..bar]);                  // erstes Muster: bis zum nächsten "|"
        if (bar >= 0)
            tokens.AddRange(rest[(bar + 1)..].Split(';', StringSplitOptions.RemoveEmptyEntries));
        return tokens;
    }

    /// <summary>Photino.Native Windows: alles nach dem ersten "|" ist eine ";"-getrennte Musterliste.</summary>
    private static List<string> WindowsNativeParse(string native) =>
        native[(native.IndexOf('|') + 1)..].Split(';').ToList();

    [Test]
    public void OhneWorkaround_ZeigtLinuxBeiMehrerenEndungenKeineDatei()
    {
        // Dokumentiert den Photino-Fehler: ein einziges Muster "*.pdf;*.txt;*.csv"
        var patterns = LinuxNativeParse(PhotinoNetJoin(Dokumentfilter[0]));

        Assert.That(patterns, Is.EqualTo(new[] { "*.pdf;*.txt;*.csv" }));
    }

    [Test]
    public void Linux_MehrereEndungen_KommenEinzelnBeiGtkAn()
    {
        var filters = FileDialogFilters.ForPhotino(Dokumentfilter, isLinux: true);

        var patterns = LinuxNativeParse(PhotinoNetJoin(filters[0]));

        Assert.That(patterns, Is.EquivalentTo(new[] { "*.pdf", "*.PDF", "*.txt", "*.TXT", "*.csv", "*.CSV" }));
    }

    [Test]
    public void Linux_EinzelneEndung_ErhaeltGrossschreibvariante()
    {
        var filters = FileDialogFilters.ForPhotino(Dokumentfilter, isLinux: true);

        Assert.That(LinuxNativeParse(PhotinoNetJoin(filters[1])), Is.EquivalentTo(new[] { "*.pdf", "*.PDF" }));
    }

    [Test]
    public void Linux_AlleDateien_BleibtEinMuster()
    {
        var filters = FileDialogFilters.ForPhotino(Dokumentfilter, isLinux: true);

        Assert.That(filters[2].Patterns, Is.EqualTo(new[] { "*.*" }));
        Assert.That(LinuxNativeParse(PhotinoNetJoin(filters[2])), Is.EqualTo(new[] { "*.*" }));
    }

    [Test]
    public void Linux_NamenUndReihenfolgeBleibenErhalten()
    {
        var filters = FileDialogFilters.ForPhotino(Dokumentfilter, isLinux: true);

        Assert.That(filters.Select(f => f.Name), Is.EqualTo(Dokumentfilter.Select(f => f.Name)));
    }

    [Test]
    public void Windows_FilterBleibenUnveraendert()
    {
        var filters = FileDialogFilters.ForPhotino(Dokumentfilter, isLinux: false);

        Assert.That(filters, Is.EqualTo(Dokumentfilter));
        Assert.That(WindowsNativeParse(PhotinoNetJoin(filters[0])), Is.EqualTo(new[] { "*.pdf", "*.txt", "*.csv" }));
    }
}
