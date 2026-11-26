using System.Globalization;

namespace StudyScope.Tests.Localization;

/// <summary>Die Formatkultur aus <c>STUDYSCOPE_TEST_CULTURE</c> kommt bei den Tests an (Grundlage des zweiten CI-Laufs).</summary>
[TestFixture]
public class TestFormatCultureTests
{
    [Test]
    public void EnvironmentVariable_SetsFormatCulture()
    {
        var name = Environment.GetEnvironmentVariable(TestFormatCulture.Variable);
        if (string.IsNullOrWhiteSpace(name))
            Assert.Pass("Variable nicht gesetzt – Formatkultur des Rechners");
        Assert.That(CultureInfo.CurrentCulture.Name, Is.EqualTo(CultureInfo.GetCultureInfo(name.Trim()).Name));
        Assert.That(CultureInfo.CurrentUICulture.Name, Is.EqualTo("de-DE"), "Oberflächensprache bleibt Deutsch");
    }
}
