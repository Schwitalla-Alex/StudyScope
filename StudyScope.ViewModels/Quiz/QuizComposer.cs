using StudyScope.Core.Models;

namespace StudyScope.ViewModels.Quiz;

/// <summary>
/// Stellt die Fragen eines Quiz aus den Fragenpools der gewählten Themen zusammen:
/// gleichmäßige Verteilung auf die Themen, bevorzugt noch nie gestellte und zuletzt falsch
/// beantwortete Fragen, am Ende gemischt.
/// </summary>
public static class QuizComposer
{
    /// <summary>Wie viele Fragen jedes Thema beisteuern soll (gleichmäßig, Rest an die ersten Themen).</summary>
    public static Dictionary<string, int> DistributeEvenly(IReadOnlyList<QuizTopic> topics, int total)
    {
        var result = new Dictionary<string, int>();
        if (topics.Count == 0)
            return result;

        var baseCount = total / topics.Count;
        var remainder = total % topics.Count;
        for (var i = 0; i < topics.Count; i++)
            result[topics[i].Id] = baseCount + (i < remainder ? 1 : 0);
        return result;
    }

    public static List<(QuizQuestion Question, QuizTopic Topic)> Compose(
        IReadOnlyList<QuizTopic> topics,
        int total,
        IReadOnlyCollection<QuizQuestionType> allowedTypes,
        IEnumerable<QuizResult> history,
        Random? random = null)
    {
        var rnd = random ?? Random.Shared;

        // Letztes Ergebnis je Frage (Verlauf ist neueste zuerst)
        var lastOutcome = new Dictionary<string, bool>();
        foreach (var result in history.OrderByDescending(r => r.CompletedAt))
        foreach (var entry in result.Entries)
            lastOutcome.TryAdd(entry.QuestionId, entry.IsCorrect);

        // Je Thema eine priorisierte Warteschlange
        var queues = topics.ToDictionary(
            t => t.Id,
            t => new Queue<QuizQuestion>(t.Questions
                .Where(q => allowedTypes.Contains(q.Type))
                .Select(q => (q, priority: !lastOutcome.TryGetValue(q.Id, out var correct) ? 0 : correct ? 2 : 1, tie: rnd.Next()))
                .OrderBy(x => x.priority)
                .ThenBy(x => x.tie)
                .Select(x => x.q)));

        var quotas = DistributeEvenly(topics, total);
        var picked = new List<(QuizQuestion, QuizTopic)>();

        foreach (var topic in topics)
        {
            var queue = queues[topic.Id];
            for (var i = 0; i < quotas[topic.Id] && queue.Count > 0; i++)
                picked.Add((queue.Dequeue(), topic));
        }

        // Fehlende Fragen (Thema hatte zu wenige) aus den übrigen Themen auffüllen
        var missing = total - picked.Count;
        while (missing > 0)
        {
            var added = false;
            foreach (var topic in topics)
            {
                if (missing == 0) break;
                if (queues[topic.Id].Count == 0) continue;
                picked.Add((queues[topic.Id].Dequeue(), topic));
                missing--;
                added = true;
            }

            if (!added) break;
        }

        return picked.OrderBy(_ => rnd.Next()).ToList();
    }
}
