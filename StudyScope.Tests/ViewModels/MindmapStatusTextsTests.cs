using StudyScope.Core.Models;
using StudyScope.ViewModels.Mindmap;

namespace StudyScope.Tests.ViewModels;

/// <summary>
/// Tests für die Meldungstexte der Mindmap: verständlicher Hauptsatz, Tipps und rohe KI-Antwort nur in den Details.
/// </summary>
[TestFixture]
public class MindmapStatusTextsTests
{
    [Test]
    public void ServiceError_ShowsServiceMessageAndSettingsTip()
    {
        var text = MindmapStatusTexts.ForFailure(new MindmapGenerationResult
        {
            RawResponse = "Gemini-Fehler: Es ist kein API-Schlüssel in den Einstellungen hinterlegt.",
            FailureReason = MindmapFailureReason.NoJson
        });

        Assert.That(text.Message, Does.Contain("Fehler gemeldet"));
        Assert.That(text.Tips[0], Does.Contain("kein API-Schlüssel"));
        Assert.That(text.Tips.Any(t => t.Contains("Einstellungen")), Is.True);
    }

    [Test]
    public void PlainTextAnswer_SuggestsRetryAndOtherModel()
    {
        var text = MindmapStatusTexts.ForFailure(new MindmapGenerationResult
        {
            RawResponse = "Gerne! Die wichtigsten Themen sind A, B und C.",
            FailureReason = MindmapFailureReason.NoJson
        });

        Assert.That(text.Message, Does.Contain("normalem Text"));
        Assert.That(text.Details, Does.Contain("Gerne!"));
    }

    [Test]
    public void InvalidJson_KeepsRawOutOfMessageAndShortensDetails()
    {
        var raw = "[{\"title\": \"" + new string('x', 5000);

        var text = MindmapStatusTexts.ForFailure(new MindmapGenerationResult { RawResponse = raw, FailureReason = MindmapFailureReason.InvalidJson });

        Assert.That(text.Message, Does.Not.Contain("title"));
        Assert.That(text.Tips, Has.Count.EqualTo(3));
        Assert.That(text.Details!.Length, Is.LessThan(2100));
        Assert.That(text.Details, Does.EndWith("…"));
    }

    [Test]
    public void EmptyResponse_HasNoDetails()
    {
        var text = MindmapStatusTexts.ForFailure(new MindmapGenerationResult { FailureReason = MindmapFailureReason.EmptyResponse });

        Assert.That(text.Message, Does.Contain("keine Antwort"));
        Assert.That(text.Details, Is.Null);
    }

    [Test]
    public void UnknownReason_IsDerivedFromRawResponse()
    {
        var withRaw = MindmapStatusTexts.ForFailure(new MindmapGenerationResult { RawResponse = "{ kaputt" });
        var withoutRaw = MindmapStatusTexts.ForFailure(new MindmapGenerationResult());

        Assert.That(withRaw.Message, Does.Contain("unvollständig oder fehlerhaft"));
        Assert.That(withoutRaw.Message, Does.Contain("keine Antwort"));
    }

    [Test]
    public void Success_MentionsRepairOnlyWhenRepaired()
    {
        Assert.That(MindmapStatusTexts.ForSuccess(24, wasRepaired: false).Message, Does.Contain("24 Themen").And.Not.Contain("unvollständig"));
        Assert.That(MindmapStatusTexts.ForSuccess(12, wasRepaired: true).Message, Does.Contain("unvollständig"));
    }

    [TestCase("Netzwerkfehler zu Google Gemini", true)]
    [TestCase("OpenAI-Error 401: Unauthorized", true)]
    [TestCase("Die Themen sind A und B.", false)]
    public void LooksLikeServiceError(string text, bool expected)
    {
        Assert.That(MindmapStatusTexts.LooksLikeServiceError(text), Is.EqualTo(expected));
    }

    /// <summary>Jeder Gegenstand hat in jeder Sprache eine eigene Wortform (nicht den Rückfall „other“ des ICU-select).</summary>
    [TestCase("de-DE", "kein Ergebnis", "dem erwarteten Ergebnis", "ein Ergebnis")]
    [TestCase("en", "nothing", "the expected result", "a result")]
    public void EverySubject_HasOwnWordForm(string culture, string fallbackNone, string fallbackWith, string fallbackFor)
    {
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo(culture);
        try
        {
            foreach (var subject in Enum.GetValues<GenerationSubject>())
            {
                var invalid = MindmapStatusTexts.ForFailure(MindmapFailureReason.InvalidJson, "x", subject, "Knopf").Message;
                var plain = MindmapStatusTexts.ForFailure(MindmapFailureReason.NoJson, "Gerne!", subject, "Knopf").Message;
                var service = MindmapStatusTexts.ForFailure(MindmapFailureReason.NoJson, "error 401", subject, "Knopf").Message;
                var topics = MindmapStatusTexts.ForFailure(MindmapFailureReason.NoTopics, "[]", subject, "Knopf").Message;

                Assert.That(invalid, Does.Not.Contain(fallbackNone), $"{subject} (unvollständig)");
                Assert.That(service, Does.Not.Contain(fallbackNone), $"{subject} (Dienstfehler)");
                Assert.That(plain, Does.Not.Contain(fallbackWith), $"{subject} (Text statt JSON)");
                Assert.That(topics, Does.Not.Contain(fallbackFor), $"{subject} (keine Themen)");
            }
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previous;
        }
    }

    [Test]
    public void WordForms_FollowTheLanguage()
    {
        Assert.That(MindmapStatusTexts.ForFailure(MindmapFailureReason.InvalidJson, "x", GenerationSubject.Quiz, "▶ Quiz starten").Message, Does.Contain("kein Quiz"));
        Assert.That(MindmapStatusTexts.ForFailure(MindmapFailureReason.NoJson, "Gerne!", GenerationSubject.Report, "Knopf").Message, Does.Contain("einem Bericht"));
        Assert.That(MindmapStatusTexts.ForFailure(MindmapFailureReason.InvalidJson, "x", GenerationSubject.Quiz, "▶ Quiz starten").Tips[0], Does.Contain("„▶ Quiz starten“"));
    }

    [Test]
    [SetUICulture("en")]
    public void WordForms_English()
    {
        Assert.That(MindmapStatusTexts.ForFailure(MindmapFailureReason.InvalidJson, "x", GenerationSubject.AudioScript, "Generate").Message, Does.Contain("no audio script"));
        Assert.That(MindmapStatusTexts.ForFailure(MindmapFailureReason.NoJson, "Sure!", GenerationSubject.AudioScript, "Generate").Message, Does.Contain("an audio script"));
        Assert.That(MindmapStatusTexts.ForSuccess(1, wasRepaired: false).Message, Does.Contain("1 topic generated"));
        Assert.That(MindmapStatusTexts.ForSuccess(24, wasRepaired: true).Tips[0], Does.Contain("“Regenerate”"));
    }

    [Test]
    public void Success_SingularForOneTopic()
    {
        Assert.That(MindmapStatusTexts.ForSuccess(1, wasRepaired: false).Message, Does.Contain("mit 1 Thema erstellt"));
    }
}
