using System.Globalization;

namespace StudyScope.Core.Localization;

/// <summary>
/// Setzt für die Dauer eines Exports die Sprache der Texte (<see cref="CultureInfo.CurrentUICulture"/>) und passend dazu
/// das Zahlen- und Datumsformat (<see cref="CultureInfo.CurrentCulture"/>) und stellt danach den vorigen Stand wieder her.
/// So lesen Ressourcen (<c>ExportStrings</c>, <c>CoreStrings</c>), <see cref="Loc"/> und <see cref="MediaFormat"/> im Export
/// die übergebene Sprache, ohne dass jede Hilfsfunktion die Kultur einzeln weiterreichen muss.
/// <para>
/// Format: Hat die Exportsprache dieselbe Sprache wie das bisherige Format des Benutzers (z. B. Export „de“, Format
/// „de-CH“), bleibt dessen regionales Format; sonst gilt das Format der Exportsprache (englischer Export → englisches Datum).
/// </para>
/// <para>Nur für synchrone Abschnitte gedacht (die Kultur gilt je Thread bzw. asynchronem Ablauf).</para>
/// </summary>
public sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _previousUi;
    private readonly CultureInfo _previousFormat;
    private bool _disposed;

    private CultureScope(CultureInfo culture)
    {
        _previousUi = CultureInfo.CurrentUICulture;
        _previousFormat = CultureInfo.CurrentCulture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = FormatCultureFor(culture, _previousFormat);
    }

    /// <summary>Setzt die Exportsprache bis zum <see cref="Dispose"/>.</summary>
    public static CultureScope Use(CultureInfo culture) => new(culture ?? throw new ArgumentNullException(nameof(culture)));

    /// <summary>
    /// Zahlen- und Datumsformat zur Exportsprache: das bisherige Format, wenn die Sprache übereinstimmt, sonst die
    /// Exportsprache selbst (neutrale Kulturen wie „en“ formatieren in .NET wie ihre Standardregion).
    /// </summary>
    public static CultureInfo FormatCultureFor(CultureInfo exportCulture, CultureInfo currentFormat) =>
        string.Equals(exportCulture.TwoLetterISOLanguageName, currentFormat.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase)
            ? currentFormat
            : exportCulture;

    /// <summary>
    /// Sprachkennung für <c>lang</c>-Attribute in Office-Dateien: immer mit Region („de“ → „de-DE“, „en“ → „en-US“),
    /// weil Word und PowerPoint neutrale Kennungen nicht der Rechtschreibprüfung zuordnen.
    /// </summary>
    public static string SpecificLanguageTag(CultureInfo culture)
    {
        if (!culture.IsNeutralCulture && culture.Name.Length > 0)
            return culture.Name;
        try
        {
            var specific = CultureInfo.CreateSpecificCulture(culture.Name);
            return specific.Name.Length > 0 ? specific.Name : "de-DE";
        }
        catch (CultureNotFoundException)
        {
            return "de-DE";
        }
    }

    /// <summary>Sprachkennung für HTML (<c>lang</c>), leer bei der invarianten Kultur → „de“ (neutrale Sprache der Texte).</summary>
    public static string HtmlLanguageTag(CultureInfo culture) => culture.Name.Length > 0 ? culture.Name : UiLanguage.NeutralLanguage;

    /// <summary>„rtl“ für Sprachen mit Schreibrichtung rechts nach links, sonst „ltr“.</summary>
    public static string Direction(CultureInfo culture) => culture.TextInfo.IsRightToLeft ? "rtl" : "ltr";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CultureInfo.CurrentUICulture = _previousUi;
        CultureInfo.CurrentCulture = _previousFormat;
    }
}
