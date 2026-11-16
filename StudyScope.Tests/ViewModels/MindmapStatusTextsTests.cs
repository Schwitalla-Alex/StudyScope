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
}
