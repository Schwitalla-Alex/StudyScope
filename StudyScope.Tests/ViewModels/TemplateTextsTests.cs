using StudyScope.Core.Models;
using StudyScope.ViewModels;

namespace StudyScope.Tests.ViewModels;

/// <summary>
/// Übersetzte Anzeige der Vorlagen von Datentabelle und Bericht: jede Vorlage hat Name und Beschreibung in jeder Sprache,
/// die Spaltenbeispiele passen zu den (deutschen) Spaltenvorschlägen, die die KI bekommt.
/// </summary>
[TestFixture]
public class TemplateTextsTests
{
    [TestCase("de-DE")]
    [TestCase("en")]
    public void EveryTemplate_HasNameAndDescription(string culture)
    {
        using var _ = new UiCultureScope(culture);
        foreach (var template in DataTableTemplates.All)
        {
            Assert.That(TemplateTexts.Name(template), Is.Not.Empty, template.ToString());
            Assert.That(TemplateTexts.Description(template), Is.Not.Empty, template.ToString());
        }
        foreach (var template in ReportTemplates.All)
        {
            Assert.That(TemplateTexts.Name(template), Is.Not.Empty, template.ToString());
            Assert.That(TemplateTexts.Description(template), Is.Not.Empty, template.ToString());
        }
    }

    [Test]
    public void German_MatchesCoreNames()
    {
        foreach (var template in DataTableTemplates.All)
        {
            Assert.That(TemplateTexts.Name(template), Is.EqualTo(DataTableTemplates.DisplayName(template)));
            Assert.That(TemplateTexts.Description(template), Is.EqualTo(DataTableTemplates.Description(template)));
            Assert.That(TemplateTexts.SuggestedColumns(template), Is.EqualTo(string.Join(", ", DataTableTemplates.SuggestedColumns(template))));
        }
        foreach (var template in ReportTemplates.All)
        {
            Assert.That(TemplateTexts.Name(template), Is.EqualTo(ReportTemplates.DisplayName(template)));
            Assert.That(TemplateTexts.Description(template), Is.EqualTo(ReportTemplates.Description(template)));
        }
    }

    [Test, SetUICulture("en")]
    public void English_ColumnExamples_HaveSameCountAsPromptColumns()
    {
        foreach (var template in DataTableTemplates.All)
        {
            var display = TemplateTexts.SuggestedColumns(template);
            var count = display.Length == 0 ? 0 : display.Split(", ").Length;
            Assert.That(count, Is.EqualTo(DataTableTemplates.SuggestedColumns(template).Count), template.ToString());
        }
        Assert.That(TemplateTexts.Name(DataTableTemplate.Terms), Is.EqualTo("Terms & definitions"));
        Assert.That(TemplateTexts.Name(ReportTemplate.StudyGuide), Is.EqualTo("Study guide"));
    }

    private sealed class UiCultureScope : IDisposable
    {
        private readonly System.Globalization.CultureInfo _previous = System.Globalization.CultureInfo.CurrentUICulture;
        public UiCultureScope(string name) => System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo(name);
        public void Dispose() => System.Globalization.CultureInfo.CurrentUICulture = _previous;
    }
}
