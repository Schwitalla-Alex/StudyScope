using System.Globalization;
using StudyScope.Core.Common;

namespace StudyScope.Tests.Common;

/// <summary>Stufen des Oberflächen-Zooms: Normalisieren, Schritte, Grenzen, Anzeige.</summary>
[TestFixture]
public class UiZoomTests
{
    [Test]
    public void Levels_AreAscending_AndContainDefault()
    {
        Assert.That(UiZoom.Levels, Is.Ordered.Ascending);
        Assert.That(UiZoom.Levels, Is.Unique);
        Assert.That(UiZoom.Levels, Does.Contain(UiZoom.Default));
        Assert.That(UiZoom.Min, Is.EqualTo(80));
        Assert.That(UiZoom.Max, Is.EqualTo(150));
    }

    [TestCase(0, 100)]      // fehlt in älteren Dateien
    [TestCase(-20, 100)]
    [TestCase(100, 100)]
    [TestCase(125, 125)]
    [TestCase(133, 125)]    // WebView2-Stufe 133 % → nächste eigene Stufe
    [TestCase(175, 150)]    // über der Obergrenze
    [TestCase(500, 150)]
    [TestCase(67, 80)]      // unter der Untergrenze
    [TestCase(25, 80)]
    [TestCase(85, 80)]      // Gleichstand → kleinere Stufe
    [TestCase(117, 110)]
    [TestCase(118, 125)]
    public void Normalize_SnapsToLevel(int input, int expected) =>
        Assert.That(UiZoom.Normalize(input), Is.EqualTo(expected));

    [Test]
    public void StepUp_WalksAllLevels_AndStopsAtMax()
    {
        var seen = new List<int>();
        var p = UiZoom.Min;
        for (var i = 0; i < 10; i++) { seen.Add(p); p = UiZoom.StepUp(p); }
        Assert.That(seen.Distinct(), Is.EqualTo(UiZoom.Levels));
        Assert.That(UiZoom.StepUp(UiZoom.Max), Is.EqualTo(UiZoom.Max));
    }

    [Test]
    public void StepDown_WalksAllLevels_AndStopsAtMin()
    {
        Assert.That(UiZoom.StepDown(150), Is.EqualTo(125));
        Assert.That(UiZoom.StepDown(100), Is.EqualTo(90));
        Assert.That(UiZoom.StepDown(UiZoom.Min), Is.EqualTo(UiZoom.Min));
        Assert.That(UiZoom.StepDown(0), Is.EqualTo(90));   // ungültig gilt als 100
    }

    [Test]
    public void Format_FollowsCulture()
    {
        // Deutsch mit Leerzeichen vor % (welches genau, hängt von der ICU-Fassung ab)
        Assert.That(UiZoom.Format(125, CultureInfo.GetCultureInfo("de-DE")), Does.Match(@"^125\s%$"));
        Assert.That(UiZoom.Format(125, CultureInfo.GetCultureInfo("en-US")), Is.EqualTo("125%"));
    }
}
