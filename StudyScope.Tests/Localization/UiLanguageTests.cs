using System.Globalization;
using StudyScope.Core.Localization;

namespace StudyScope.Tests.Localization;

[TestFixture]
public class UiLanguageTests
{
    private static readonly string[] DeEn = { "de", "en" };
    private static readonly string[] DeOnly = { "de" };

    private static CultureInfo C(string name) => CultureInfo.GetCultureInfo(name);

    [Test]
    public void Resolve_ExplicitCulture_WhenAvailable() =>
        Assert.That(UiLanguage.Resolve("en", C("de-DE"), DeEn).Name, Is.EqualTo("en"));

    [Test]
    public void Resolve_RegionalCulture_KeepsRegionForFormats() =>
        Assert.That(UiLanguage.Resolve("de-AT", C("en-US"), DeEn).Name, Is.EqualTo("de-AT"));

    [Test]
    public void Resolve_System_UsesInstalledWhenSupported() =>
        Assert.That(UiLanguage.Resolve("system", C("de-CH"), DeEn).Name, Is.EqualTo("de-CH"));

    [Test]
    public void Resolve_System_UnsupportedFallsBackToEnglish() =>
        Assert.That(UiLanguage.Resolve("system", C("fr-FR"), DeEn).Name, Is.EqualTo("en"));

    [Test]
    public void Resolve_OnlyNeutralAvailable_FallsBackToGerman() =>
        Assert.That(UiLanguage.Resolve("system", C("fr-FR"), DeOnly).Name, Is.EqualTo("de"));

    [TestCase(null)]
    [TestCase("")]
    [TestCase("kein-gültiger-name")]
    [TestCase("ja")]
    public void Resolve_EmptyUnknownOrMissing_UsesSystem(string? setting) =>
        Assert.That(UiLanguage.Resolve(setting, C("de-DE"), DeEn).Name, Is.EqualTo("de-DE"));

    [Test]
    public void AvailableCultures_FindsSatelliteFolders()
    {
        var dir = Path.Combine(Path.GetTempPath(), "StudyScope_lang_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "en"));
            File.WriteAllText(Path.Combine(dir, "en", "Test.App.resources.dll"), "");
            Directory.CreateDirectory(Path.Combine(dir, "fr"));            // ohne passende Datei
            Directory.CreateDirectory(Path.Combine(dir, "runtimes"));      // keine Kultur
            File.WriteAllText(Path.Combine(dir, "fr", "Andere.resources.dll"), "");

            Assert.That(UiLanguage.AvailableCultures(dir, "Test.App"), Is.EqualTo(new[] { "de", "en" }));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public void AvailableCultures_MissingDirectory_OnlyNeutral() =>
        Assert.That(UiLanguage.AvailableCultures(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), "X"),
            Is.EqualTo(new[] { "de" }));
}
