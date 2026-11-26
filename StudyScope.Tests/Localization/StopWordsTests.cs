using StudyScope.Core.Common;
using StudyScope.Core.Localization;
using StudyScope.ViewModels;

namespace StudyScope.Tests.Localization;

[TestFixture]
public class StopWordsTests
{
    [Test]
    public void Files_ExistForGermanAndEnglish() =>
        Assert.That(LanguageData.Languages(StopWords.FileName), Is.SupersetOf(new[] { "de", "en" }));

    [Test]
    public void Comments_AndEmptyLines_AreSkipped()
    {
        var lines = LanguageData.ReadLines("de", StopWords.FileName);
        Assert.That(lines, Has.None.StartsWith("#").And.None.Empty);
        Assert.That(lines, Does.Contain("erkläre"));
    }

    [Test]
    public void UnknownLanguage_GivesNoLines() =>
        Assert.That(LanguageData.ReadLines("xx", StopWords.FileName), Is.Empty);

    [Test]
    public void AllLanguages_ApplyTogether()
    {
        Assert.That(StopWords.Contains("erkläre"), Is.True);
        Assert.That(StopWords.Contains("Explain"), Is.True);
        Assert.That(StopWords.Contains("photosynthese"), Is.False);
    }

    [Test]
    public void ExtractSearchWords_RemovesEnglishStopWords()
    {
        var words = ChatViewModel.ExtractSearchWords("Please explain the connection between photosynthesis and respiration based on my materials.");
        Assert.That(words, Is.EquivalentTo(new[] { "photosynthesis", "respiration", "my" }.Where(w => w.Length > 2)));
    }

    [Test]
    public void ExtractSearchWords_GermanUnchanged()
    {
        var words = ChatViewModel.ExtractSearchWords("Erkläre mir die Photosynthese anhand meiner Unterlagen.");
        Assert.That(words, Is.EquivalentTo(new[] { "photosynthese" }));
    }
}
