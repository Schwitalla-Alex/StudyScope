// Alle Tests laufen mit deutscher Oberflächensprache (Original der Texte), unabhängig von der Sprache des Rechners
// bzw. des CI-Runners. Tests für andere Sprachen setzen [SetUICulture] am Test selbst.
// Die Formatkultur (Zahlen, Datum) bleibt die des Rechners – so fallen kulturabhängige Fehler in der CI auf.
// Mit STUDYSCOPE_TEST_CULTURE=<kultur> (z. B. de-DE) lässt sich die Formatkultur für einen ganzen Lauf festlegen;
// die CI nutzt das für einen zweiten Lauf, weil ihre Runner sonst nur en-US bzw. die invariante Kultur prüfen.
[assembly: SetUICulture("de-DE")]

/// <summary>Setzt die Formatkultur aller Tests aus <c>STUDYSCOPE_TEST_CULTURE</c> (fehlt sie, bleibt die des Rechners).</summary>
[SetUpFixture]
public sealed class TestFormatCulture
{
    public const string Variable = "STUDYSCOPE_TEST_CULTURE";

    [OneTimeSetUp]
    public void Apply()
    {
        var name = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(name))
            return;
        var culture = System.Globalization.CultureInfo.GetCultureInfo(name.Trim());
        // Der Ausführungskontext gibt die Kultur an alle Tests darunter weiter (am Test gesetztes [SetCulture] hat Vorrang)
        NUnit.Framework.Internal.TestExecutionContext.CurrentContext.CurrentCulture = culture;
    }
}
