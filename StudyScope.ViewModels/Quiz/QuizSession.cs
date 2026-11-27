using StudyScope.Core.Models;

namespace StudyScope.ViewModels.Quiz;

/// <summary>Eine Frage innerhalb eines laufenden Quiz samt gewählter Antworten.</summary>
public sealed class QuizSessionItem
{
    public QuizSessionItem(QuizQuestion question, string originalQuestionId, string topicId, string topicName)
    {
        Question = question;
        OriginalQuestionId = originalQuestionId;
        TopicId = topicId;
        TopicName = topicName;
    }

    /// <summary>Frage mit (ggf. gemischter) Antwortreihenfolge für diesen Durchgang.</summary>
    public QuizQuestion Question { get; }

    /// <summary>ID der Frage im Fragenpool (für Verlauf und „Falsche wiederholen“).</summary>
    public string OriginalQuestionId { get; }

    public string TopicId { get; }
    public string TopicName { get; }

    public HashSet<int> Selected { get; } = new();

    /// <summary>Antwort ist ausgewertet (Lernmodus nach „Prüfen“, Prüfungsmodus nach „Abgeben“).</summary>
    public bool IsChecked { get; internal set; }

    public bool IsAnswered => Selected.Count > 0;

    /// <summary>Richtig nur, wenn genau die richtigen Antworten gewählt sind (auch bei Mehrfachauswahl).</summary>
    public bool IsCorrect => Selected.SetEquals(Question.CorrectIndices);
}

/// <summary>
/// Ablauf eines Quizdurchgangs ohne Oberfläche: Antworten wählen, im Lernmodus sofort prüfen,
/// im Prüfungsmodus frei blättern und am Ende abgeben; Ergebnis für den Verlauf erzeugen.
/// </summary>
public sealed class QuizSession
{
    private readonly List<QuizSessionItem> _items;

    /// <param name="questions">Fragen in Spielreihenfolge mit ihrem Thema.</param>
    /// <param name="random">Zufallsquelle für das Mischen der Antworten (für Tests festlegbar).</param>
    public QuizSession(IEnumerable<(QuizQuestion Question, QuizTopic Topic)> questions, QuizMode mode, Random? random = null)
    {
        var rnd = random ?? Random.Shared;
        Mode = mode;
        _items = questions
            .Select(q => new QuizSessionItem(ShuffleOptions(q.Question, rnd), q.Question.Id, q.Topic.Id, q.Topic.Name))
            .ToList();
    }

    public QuizMode Mode { get; }
    public IReadOnlyList<QuizSessionItem> Items => _items;
    public int Count => _items.Count;
    public int CurrentIndex { get; private set; }
    public QuizSessionItem? Current => _items.Count == 0 ? null : _items[CurrentIndex];
    public bool IsFinished { get; private set; }
    public bool IsLastQuestion => CurrentIndex == _items.Count - 1;

    public int AnsweredCount => _items.Count(i => i.IsAnswered);
    public int UnansweredCount => _items.Count - AnsweredCount;
    public int CheckedCount => _items.Count(i => i.IsChecked);

    /// <summary>Richtige Antworten unter den bereits ausgewerteten Fragen.</summary>
    public int CorrectCount => _items.Count(i => i.IsChecked && i.IsCorrect);

    /// <summary>Darf die aktuelle Antwort noch geändert werden?</summary>
    public bool CanAnswer => Current != null && !IsFinished && !(Mode == QuizMode.Learn && Current.IsChecked);

    public bool CanCheck => Mode == QuizMode.Learn && Current is { IsChecked: false, IsAnswered: true } && !IsFinished;

    public bool CanGoNext => Current != null && !IsLastQuestion && (Mode == QuizMode.Exam || Current.IsChecked || IsFinished);

    public bool CanGoPrevious => CurrentIndex > 0;

    /// <summary>Wählt eine Antwort; bei Einfachauswahl und Wahr/Falsch ersetzt sie die bisherige Wahl.</summary>
    public void ToggleOption(int optionIndex)
    {
        if (!CanAnswer || Current == null || optionIndex < 0 || optionIndex >= Current.Question.Options.Count)
            return;

        var selected = Current.Selected;
        if (Current.Question.Type == QuizQuestionType.MultipleChoice)
        {
            if (!selected.Remove(optionIndex))
                selected.Add(optionIndex);
        }
        else
        {
            selected.Clear();
            selected.Add(optionIndex);
        }
    }

    /// <summary>Lernmodus: aktuelle Antwort auswerten.</summary>
    public void Check()
    {
        if (CanCheck)
            Current!.IsChecked = true;
    }

    public void Next()
    {
        if (CanGoNext)
            CurrentIndex++;
    }

    public void Previous()
    {
        if (CanGoPrevious)
            CurrentIndex--;
    }

    /// <summary>Springt zu einer Frage (Prüfungsmodus und nach dem Ende frei, im Lernmodus nur zu bereits bearbeiteten).</summary>
    public void GoTo(int index)
    {
        if (index < 0 || index >= _items.Count)
            return;

        var reachable = Mode == QuizMode.Exam || IsFinished || index <= FirstOpenIndex();
        if (reachable)
            CurrentIndex = index;
    }

    private int FirstOpenIndex()
    {
        var open = _items.FindIndex(i => !i.IsChecked);
        return open < 0 ? _items.Count - 1 : open;
    }

    /// <summary>Beendet das Quiz. Unbeantwortete Fragen zählen als falsch.</summary>
    public QuizResult Finish(IEnumerable<string> topicNames)
    {
        IsFinished = true;
        foreach (var item in _items)
            item.IsChecked = true;

        return new QuizResult
        {
            CompletedAt = DateTime.Now,
            Mode = Mode,
            TopicNames = topicNames.ToList(),
            Total = _items.Count,
            Correct = _items.Count(i => i.IsCorrect),
            Entries = _items.Select(i => new QuizResultEntry { QuestionId = i.OriginalQuestionId, TopicId = i.TopicId, IsCorrect = i.IsCorrect }).ToList()
        };
    }

    /// <summary>Mischt die Antworten (nicht bei Wahr/Falsch) und passt die Lösungsindizes an.</summary>
    private static QuizQuestion ShuffleOptions(QuizQuestion question, Random random)
    {
        if (question.Type == QuizQuestionType.TrueFalse || question.Options.Count < 2)
            return Copy(question, question.Options.ToList(), question.CorrectIndices.ToList());

        var order = Enumerable.Range(0, question.Options.Count).OrderBy(_ => random.Next()).ToList();
        var options = order.Select(i => question.Options[i]).ToList();
        var correct = question.CorrectIndices.Select(c => order.IndexOf(c)).OrderBy(i => i).ToList();
        return Copy(question, options, correct);
    }

    private static QuizQuestion Copy(QuizQuestion q, List<string> options, List<int> correct) => new()
    {
        Id = q.Id,
        Type = q.Type,
        Text = q.Text,
        Options = options,
        CorrectIndices = correct,
        Explanation = q.Explanation,
        Language = q.Language
    };
}
