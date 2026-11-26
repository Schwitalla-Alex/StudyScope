using System.Globalization;
using StudyScope.Core.Resources;

namespace StudyScope.Core.Localization;

/// <summary>
/// Dauer, Größe und Beträge für die Anzeige in der Sprache der Oberfläche (Texte aus <see cref="CoreStrings"/>,
/// Zahlen in <see cref="CultureInfo.CurrentCulture"/>). Gemeinsam für App und Dienste, damit Hinweise wie
/// „Ohne Ton … (ca. 3 Min.)“ überall gleich lauten.
/// </summary>
public static class MediaFormat
{
    /// <summary>„ca. 7 Min.“ bzw. „unter 1 Min.“ (gerundet).</summary>
    public static string Minutes(double minutes) =>
        minutes < 1
            ? CoreStrings.Media_MinutesUnderOne
            : Loc.Format(CoreStrings.Media_MinutesApprox, ("count", Math.Max(1, (int)Math.Round(minutes))));

    /// <summary>Dauer aus Sekunden, aufgerundet („unter 1 Min.“ unter 45 s) – für Exportdauern, damit sie eher früher fertig sind.</summary>
    public static string MinutesRoundedUp(double seconds) =>
        seconds < 45
            ? CoreStrings.Media_MinutesUnderOne
            : Loc.Format(CoreStrings.Media_MinutesApprox, ("count", Math.Max(1, (int)Math.Ceiling(seconds / 60))));

    /// <summary>Zahl in Megabyte: unter 10 mit einer Nachkommastelle, sonst ganzzahlig.</summary>
    public static string MegabyteNumber(double megabytes) =>
        megabytes.ToString(megabytes < 10 ? "0.0" : "0", CultureInfo.CurrentCulture);

    /// <summary>„ca. 4,2 MB“ bzw. „unter 0,1 MB“.</summary>
    public static string Size(double megabytes) =>
        megabytes < 0.1
            ? Loc.Format(CoreStrings.Media_SizeUnder, ("size", 0.1.ToString("0.0", CultureInfo.CurrentCulture)))
            : Loc.Format(CoreStrings.Media_SizeApprox, ("size", MegabyteNumber(megabytes)));

    /// <summary>Zahl in Megabyte für Meldungen „(… MB)“: sehr kleine Dateien als „unter 0,1“.</summary>
    public static string SizeNumber(double megabytes) =>
        megabytes < 0.1
            ? Loc.Format(CoreStrings.Media_Under, ("value", 0.1.ToString("0.0", CultureInfo.CurrentCulture)))
            : MegabyteNumber(megabytes);

    /// <summary>Betrag mit zwei Nachkommastellen, sehr kleine Beträge als „unter 0,01“.</summary>
    public static string Amount(double value) =>
        value < 0.01
            ? Loc.Format(CoreStrings.Media_Under, ("value", 0.01.ToString("0.00", CultureInfo.CurrentCulture)))
            : value.ToString("0.00", CultureInfo.CurrentCulture);
}
