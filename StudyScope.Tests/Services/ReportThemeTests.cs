using StudyScope.Core.Models;
using StudyScope.Infrastructure.Services;
using StudyScope.Tests.Common;

namespace StudyScope.Tests.Services;

/// <summary>
/// Bericht und Farbschema: In der App (InApp) folgt der Bericht im iframe der Darstellung der App, exportierte
/// HTML-Dateien bleiben unverändert hell (kein Dunkel-Block, kein Skript für das Farbschema).
/// </summary>
[TestFixture]
public class ReportThemeTests
{
    private static Report Report() => new()
    {
        Title = "Testbericht",
        Embeds = new ReportEmbeds { Mindmap = false, Flashcards = false, Quiz = false, Presentation = false, DataTables = false, AudioScripts = false, VideoScripts = false },
        Sections = new List<ReportSection> { new() { Heading = "Überblick", Paragraphs = new List<string> { "Text." } } }
    };

    [Test]
    public void InApp_ContainsDarkBlockAndThemeSync()
    {
        var html = ReportHtmlExporter.Export(Report(), new ReportStudioContent(), TestCultures.De, new ReportHtmlOptions { InApp = true });

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain(":root[data-theme-effective=\"dark\"]"));
            Assert.That(html, Does.Contain("parent.document.documentElement"));
            // Skript im Kopf, damit der Bericht nicht erst hell erscheint
            Assert.That(html.IndexOf("parent.document.documentElement", StringComparison.Ordinal),
                Is.LessThan(html.IndexOf("<body>", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void FileExport_StaysLight()
    {
        var html = ReportHtmlExporter.Export(Report(), new ReportStudioContent(), TestCultures.De);

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Not.Contain("data-theme"));
            Assert.That(html, Does.Not.Contain("parent.document"));
            Assert.That(html, Is.EqualTo(ReportHtmlExporter.Export(Report(), new ReportStudioContent(), TestCultures.De, ReportHtmlOptions.Default)));
        });
    }

    [Test]
    public void DarkBlock_AppliesOnlyOnScreen_SoPrintStaysLight()
    {
        var css = ReportHtmlExporter.AppDarkCss.Trim();
        Assert.That(css, Does.StartWith("@media screen {"));
        Assert.That(css, Does.EndWith("}"));
        // Jede Dunkel-Regel steht innerhalb des screen-Blocks
        var outside = css["@media screen {".Length..^1];
        Assert.That(outside.Count(c => c == '{'), Is.EqualTo(outside.Count(c => c == '}')));
    }

    [Test]
    public void DarkBlock_KeepsSlidesLight() =>
        Assert.That(ReportHtmlExporter.AppDarkCss, Does.Contain(":root[data-theme-effective=\"dark\"] .slide {").And.Contain("color: #212529"));
}
