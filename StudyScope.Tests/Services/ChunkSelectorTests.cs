using StudyScope.Core.Models;
using StudyScope.Infrastructure.Services;

namespace StudyScope.Tests.Services;

/// <summary>Tests für die gemeinsame Auswahl der Textstellen (Mindmap, Quiz, Berichte, Datentabellen).</summary>
[TestFixture]
public class ChunkSelectorTests
{
    [Test]
    public void SelectRepresentative_SpreadsEvenly_IncludingFirstAndLast()
    {
        var chunks = Enumerable.Range(0, 100).Select(i => $"Stelle {i}").ToList();

        var selected = ChunkSelector.SelectRepresentative(chunks, 5);

        Assert.That(selected, Is.EqualTo(new[] { "Stelle 0", "Stelle 25", "Stelle 50", "Stelle 74", "Stelle 99" }));
    }

    [Test]
    public void SelectRepresentative_MatchesMindmapServiceWrapper()
    {
        var chunks = Enumerable.Range(0, 37).Select(i => $"Stelle {i}").Append(" ").ToList();

        Assert.That(MindmapService.SelectRepresentativeChunks(chunks, 16), Is.EqualTo(ChunkSelector.SelectRepresentative(chunks, 16)));
        Assert.That(ChunkSelector.SelectRepresentative(chunks, 0), Is.Empty);
        Assert.That(ChunkSelector.SelectRepresentative((IEnumerable<string>?)null, 3), Is.Empty);
    }

    [Test]
    public void SelectRepresentative_Generic_UsesTextAndKeepsItems()
    {
        var chunks = new[]
        {
            new SourceChunk("a.txt", "eins"),
            new SourceChunk("a.txt", "  "),
            new SourceChunk("b.txt", "zwei")
        };

        var selected = ChunkSelector.SelectRepresentative(chunks, 5, c => c.Text);

        Assert.That(selected.Select(c => c.SourceDocument), Is.EqualTo(new[] { "a.txt", "b.txt" }));
    }

    [Test]
    public void SelectByKeywords_PrefersMatches_AndRotatesPerBatch()
    {
        var chunks = Enumerable.Range(0, 30).Select(i => i % 3 == 0 ? $"Text {i} über Enzyme und Enzyme" : $"Text {i} über anderes").ToList();

        var first = ChunkSelector.SelectByKeywords(chunks, "Enzyme", 0, 4);
        var second = ChunkSelector.SelectByKeywords(chunks, "Enzyme", 1, 4);

        Assert.That(first, Has.Count.EqualTo(4));
        Assert.That(first, Has.All.Contain("Enzyme"));
        Assert.That(second, Is.Not.EqualTo(first));
    }

    [Test]
    public void SelectByKeywords_IsTheSameAsQuizTopicSelection()
    {
        var chunks = Enumerable.Range(0, 40).Select(i => i % 5 == 0 ? $"Abschnitt {i}: Photosynthese im Blatt" : $"Abschnitt {i}: Zellatmung").ToList();
        var topic = new QuizTopic { Name = "Photosynthese", Description = "Licht und Blatt" };

        for (var batch = 0; batch < 3; batch++)
        {
            Assert.That(QuizService.SelectChunksForTopic(chunks, topic, batch),
                Is.EqualTo(ChunkSelector.SelectByKeywords(chunks, "Photosynthese Licht und Blatt", batch, QuizService.ChunksPerRequest)));
        }
    }

    [Test]
    public void SelectByKeywords_WithoutKeywords_FallsBackToSpreadChunks()
    {
        var chunks = Enumerable.Range(0, 40).Select(i => $"Stelle {i}").ToList();

        var selected = ChunkSelector.SelectByKeywords(chunks, string.Empty, 0, 4);

        Assert.That(selected, Has.Count.EqualTo(4));
        Assert.That(selected, Is.Unique);
    }

    [Test]
    public void Keywords_IgnoreShortWordsAndPunctuation()
    {
        Assert.That(ChunkSelector.Keywords("Die (Zell-)Atmung: ATP!"), Is.EquivalentTo(new[] { "zell", "atmung" }));
        Assert.That(ChunkSelector.Score("Atmung und Zellatmung", new[] { "atmung" }), Is.EqualTo(2));
    }
}
