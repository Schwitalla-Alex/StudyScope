using StudyScope.Core.Models;

namespace StudyScope.Tests.Models;

/// <summary>
/// Suchanfrage (nur selbst eingegebener Text, bereinigt und gekürzt), Einstellungen und Zustand der Websuche je Profil.
/// </summary>
[TestFixture]
public class WebSearchModelTests
{
    [Test]
    public void Query_FromUserQuestion_NormalizesWhitespaceAndControlCharacters()
    {
        var query = WebSearchQuery.FromUserQuestion("  Was ist\r\nPhotosynthese?\t\u0007 Bitte  kurz. ");
        Assert.That(query.Text, Is.EqualTo("Was ist Photosynthese? Bitte kurz."));
    }

    [TestCase("")]
    [TestCase("   \r\n\t")]
    [TestCase(null)]
    public void Query_Empty_Throws(string? question) =>
        Assert.Throws<ArgumentException>(() => WebSearchQuery.FromUserQuestion(question!));

    [Test]
    public void Query_LongQuestion_IsCutAtWordBoundary()
    {
        var question = string.Join(' ', Enumerable.Range(1, 200).Select(i => "Wort" + i));
        var query = WebSearchQuery.FromUserQuestion(question);

        Assert.That(query.Text.Length, Is.LessThanOrEqualTo(WebSearchQuery.MaxLength));
        Assert.That(question.StartsWith(query.Text, StringComparison.Ordinal), Is.True);
        Assert.That(question[query.Text.Length], Is.EqualTo(' '), "an einer Wortgrenze gekürzt");
    }

    [Test]
    public void Query_LongWordWithoutSpaces_IsCutHard_WithoutBrokenEmoji()
    {
        var question = new string('a', WebSearchQuery.MaxLength - 1) + "😀" + "bbb";
        var query = WebSearchQuery.FromUserQuestion(question);

        Assert.That(query.Text.Length, Is.LessThanOrEqualTo(WebSearchQuery.MaxLength));
        Assert.That(char.IsHighSurrogate(query.Text[^1]), Is.False);
    }

    [Test]
    public void Query_ConnectionTest_IsFixedTerm() =>
        Assert.That(WebSearchQuery.ConnectionTest().Text, Is.EqualTo("StudyScope"));

    [Test]
    public void Query_HasNoPublicConstructor() =>
        Assert.That(typeof(WebSearchQuery).GetConstructors(), Is.Empty,
            "Anfragen nur über FromUserQuestion bzw. ConnectionTest – sonst könnte anderer Text ins Netz gehen");

    [TestCase(WebSearchProviderType.None, "", "", false)]
    [TestCase(WebSearchProviderType.Tavily, "", "", false)]
    [TestCase(WebSearchProviderType.Tavily, "DUMMY_TEST_KEY_X", "", true)]
    [TestCase(WebSearchProviderType.Searxng, "", "localhost:8888", false)]
    [TestCase(WebSearchProviderType.Searxng, "", "http://localhost:8888", true)]
    [TestCase(WebSearchProviderType.Searxng, "", "javascript:alert(1)", false)]
    public void Settings_IsConfigured(WebSearchProviderType provider, string key, string url, bool expected) =>
        Assert.That(new WebSearchSettings { Provider = provider, TavilyApiKey = key, SearxngUrl = url }.IsConfigured, Is.EqualTo(expected));

    [TestCase(0, 5)]
    [TestCase(-3, 5)]
    [TestCase(1, 1)]
    [TestCase(7, 7)]
    [TestCase(10, 10)]
    [TestCase(50, 10)]
    public void Settings_ClampMaxResults(int value, int expected) =>
        Assert.That(WebSearchSettings.ClampMaxResults(value), Is.EqualTo(expected));

    [Test]
    public void Settings_Default_IsNoProvider_FiveResults()
    {
        var settings = new WebSearchSettings();
        Assert.That(settings.Provider, Is.EqualTo(WebSearchProviderType.None));
        Assert.That(settings.MaxResults, Is.EqualTo(5));
        Assert.That(settings.IsConfigured, Is.False);
    }

    [Test]
    public void Settings_Clone_CopiesAllFields()
    {
        var original = new WebSearchSettings { Provider = WebSearchProviderType.Searxng, TavilyApiKey = "DUMMY_TEST_KEY_X", SearxngUrl = "http://localhost:8888", MaxResults = 8 };
        var clone = original.Clone();
        Assert.That(clone, Is.Not.SameAs(original));
        Assert.That((clone.Provider, clone.TavilyApiKey, clone.SearxngUrl, clone.MaxResults),
            Is.EqualTo((original.Provider, original.TavilyApiKey, original.SearxngUrl, original.MaxResults)));
    }

    [Test]
    public void ProfileState_DefaultOff_NeedsNotice()
    {
        var state = WebSearchProfileState.Off;
        Assert.That(state.Enabled, Is.False);
        Assert.That(state.NeedsNotice(WebSearchProviderType.Tavily), Is.True);
    }

    [Test]
    public void ProfileState_NoticeAgain_WhenProviderChanges()
    {
        var state = new WebSearchProfileState(true, "Tavily");
        Assert.That(state.NeedsNotice(WebSearchProviderType.Tavily), Is.False);
        Assert.That(state.NeedsNotice(WebSearchProviderType.Searxng), Is.True);
    }

    [TestCase("https://www.example.org/a", "example.org")]
    [TestCase("https://sub.example.org/a", "sub.example.org")]
    [TestCase("kein Link", "")]
    public void Result_Domain(string url, string expected) =>
        Assert.That(new WebSearchResult("T", url, "").Domain, Is.EqualTo(expected));
}
