using StudyScope.Core.Models;
using StudyScope.ViewModels.Quiz;

namespace StudyScope.Tests.ViewModels;

/// <summary>Tests für den Quizablauf (QuizSession) und die Zusammenstellung der Fragen (QuizComposer).</summary>
[TestFixture]
public class QuizSessionTests
{
    private static readonly QuizQuestionType[] AllTypes =
        { QuizQuestionType.SingleChoice, QuizQuestionType.TrueFalse, QuizQuestionType.MultipleChoice };

    private static QuizTopic Topic(string name, int questions = 0)
    {
        var topic = new QuizTopic { Name = name };
        for (var i = 0; i < questions; i++)
            topic.Questions.Add(Single($"{name} {i}"));
        return topic;
    }

    private static QuizQuestion Single(string text) =>
        new() { Type = QuizQuestionType.SingleChoice, Text = text, Options = { "a", "b", "c", "d" }, CorrectIndices = { 2 } };

    private static QuizQuestion Multiple(string text) =>
        new() { Type = QuizQuestionType.MultipleChoice, Text = text, Options = { "a", "b", "c", "d" }, CorrectIndices = { 0, 3 } };

    private static QuizQuestion TrueFalse(string text) =>
        new() { Type = QuizQuestionType.TrueFalse, Text = text, Options = { "Wahr", "Falsch" }, CorrectIndices = { 1 } };

    private static QuizSession Session(QuizMode mode, params QuizQuestion[] questions)
    {
        var topic = Topic("Thema");
        return new QuizSession(questions.Select(q => (q, topic)), mode, new Random(1));
    }

    private static int CorrectOption(QuizSession s) => s.Current!.Question.CorrectIndices[0];
    private static int WrongOption(QuizSession s) =>
        Enumerable.Range(0, s.Current!.Question.Options.Count).First(i => !s.Current.Question.CorrectIndices.Contains(i));

    // ---------- Antworten mischen ----------

    [Test]
    public void Options_AreShuffled_ButCorrectAnswerStaysCorrect()
    {
        var question = new QuizQuestion { Type = QuizQuestionType.SingleChoice, Text = "F", Options = { "falsch1", "falsch2", "RICHTIG", "falsch3" }, CorrectIndices = { 2 } };
        var session = Session(QuizMode.Learn, question);

        var shuffled = session.Current!.Question;
        Assert.That(shuffled.Options[shuffled.CorrectIndices[0]], Is.EqualTo("RICHTIG"));
        Assert.That(question.Options, Is.EqualTo(new[] { "falsch1", "falsch2", "RICHTIG", "falsch3" }), "Original im Pool bleibt unverändert");
        Assert.That(session.Items[0].OriginalQuestionId, Is.EqualTo(question.Id));
    }

    [Test]
    public void TrueFalse_IsNotShuffled()
    {
        var session = Session(QuizMode.Learn, TrueFalse("Aussage"));

        Assert.That(session.Current!.Question.Options, Is.EqualTo(new[] { "Wahr", "Falsch" }));
    }

    // ---------- Auswahl ----------

    [Test]
    public void SingleChoice_ReplacesSelection_MultipleChoice_Toggles()
    {
        var session = Session(QuizMode.Exam, Single("S"), Multiple("M"));

        session.ToggleOption(0);
        session.ToggleOption(1);
        Assert.That(session.Current!.Selected, Is.EqualTo(new[] { 1 }));

        session.Next();
        session.ToggleOption(0);
        session.ToggleOption(2);
        session.ToggleOption(0);
        Assert.That(session.Current!.Selected, Is.EqualTo(new[] { 2 }));
    }

    [Test]
    public void MultipleChoice_OnlyFullyCorrectCounts()
    {
        var session = Session(QuizMode.Learn, Multiple("M"));
        var correct = session.Current!.Question.CorrectIndices;

        session.ToggleOption(correct[0]);
        Assert.That(session.Current.IsCorrect, Is.False, "nur eine von zwei richtigen");

        session.ToggleOption(correct[1]);
        Assert.That(session.Current.IsCorrect, Is.True);

        session.ToggleOption(WrongOption(session));
        Assert.That(session.Current.IsCorrect, Is.False, "zusätzlich eine falsche");
    }

    // ---------- Lernmodus ----------

    [Test]
    public void LearnMode_CheckLocksAnswer_AndAllowsNext()
    {
        var session = Session(QuizMode.Learn, Single("1"), Single("2"));

        Assert.That(session.CanCheck, Is.False, "ohne Auswahl nicht prüfbar");
        Assert.That(session.CanGoNext, Is.False);

        session.ToggleOption(WrongOption(session));
        session.Check();

        Assert.That(session.Current!.IsChecked, Is.True);
        Assert.That(session.Current.IsCorrect, Is.False);
        Assert.That(session.CanAnswer, Is.False);
        Assert.That(session.CheckedCount, Is.EqualTo(1));
        Assert.That(session.CorrectCount, Is.EqualTo(0));

        session.ToggleOption(CorrectOption(session)); // gesperrt
        Assert.That(session.Current.IsCorrect, Is.False);

        session.Next();
        Assert.That(session.CurrentIndex, Is.EqualTo(1));
    }

    [Test]
    public void LearnMode_GoTo_OnlyToAlreadyReachedQuestions()
    {
        var session = Session(QuizMode.Learn, Single("1"), Single("2"), Single("3"));

        session.GoTo(2);
        Assert.That(session.CurrentIndex, Is.EqualTo(0));

        session.ToggleOption(0);
        session.Check();
        session.GoTo(1);
        Assert.That(session.CurrentIndex, Is.EqualTo(1));
    }

    // ---------- Prüfungsmodus ----------

    [Test]
    public void ExamMode_FreeNavigation_AnswersChangeable_UntilFinish()
    {
        var session = Session(QuizMode.Exam, Single("1"), Single("2"), Single("3"));

        Assert.That(session.CanCheck, Is.False);
        session.GoTo(2);
        Assert.That(session.CurrentIndex, Is.EqualTo(2));

        session.ToggleOption(CorrectOption(session));
        session.Previous();
        session.ToggleOption(WrongOption(session));
        session.ToggleOption(CorrectOption(session)); // geändert
        Assert.That(session.AnsweredCount, Is.EqualTo(2));
        Assert.That(session.UnansweredCount, Is.EqualTo(1));
        Assert.That(session.CheckedCount, Is.EqualTo(0), "im Prüfungsmodus nichts vorab ausgewertet");

        var result = session.Finish(new[] { "Thema" });

        Assert.That(result.Total, Is.EqualTo(3));
        Assert.That(result.Correct, Is.EqualTo(2));
        Assert.That(result.Percent, Is.EqualTo(66.7));
        Assert.That(result.Mode, Is.EqualTo(QuizMode.Exam));
        Assert.That(result.Entries.Count(e => !e.IsCorrect), Is.EqualTo(1), "unbeantwortet zählt als falsch");
        Assert.That(session.IsFinished, Is.True);
        Assert.That(session.CanAnswer, Is.False);
    }

    // ---------- Zusammenstellung ----------

    [Test]
    public void DistributeEvenly_SplitsRemainderToFirstTopics()
    {
        var topics = new[] { Topic("A"), Topic("B"), Topic("C") };

        var quotas = QuizComposer.DistributeEvenly(topics, 10);

        Assert.That(topics.Select(t => quotas[t.Id]), Is.EqualTo(new[] { 4, 3, 3 }));
    }

    [Test]
    public void Compose_DistributesAndFillsFromOtherTopics()
    {
        var a = Topic("A", 2);  // zu wenige
        var b = Topic("B", 20);

        var composed = QuizComposer.Compose(new[] { a, b }, 10, AllTypes, Array.Empty<QuizResult>(), new Random(1));

        Assert.That(composed, Has.Count.EqualTo(10));
        Assert.That(composed.Count(x => x.Topic == a), Is.EqualTo(2));
        Assert.That(composed.Count(x => x.Topic == b), Is.EqualTo(8));
        Assert.That(composed.Select(x => x.Question.Id).Distinct().Count(), Is.EqualTo(10));
    }

    [Test]
    public void Compose_RespectsAllowedTypes()
    {
        var topic = Topic("A");
        topic.Questions.AddRange(new[] { Single("s1"), TrueFalse("t1"), Multiple("m1"), TrueFalse("t2") });

        var composed = QuizComposer.Compose(new[] { topic }, 10, new[] { QuizQuestionType.TrueFalse }, Array.Empty<QuizResult>());

        Assert.That(composed.Select(x => x.Question.Text).OrderBy(t => t), Is.EqualTo(new[] { "t1", "t2" }));
    }

    [Test]
    public void Compose_PrefersNewThenWrongThenCorrectQuestions()
    {
        var topic = Topic("A", 6);
        var q = topic.Questions;
        var history = new List<QuizResult>
        {
            new()
            {
                CompletedAt = DateTime.Now.AddDays(-1),
                Entries =
                {
                    new QuizResultEntry { QuestionId = q[0].Id, IsCorrect = true },
                    new QuizResultEntry { QuestionId = q[1].Id, IsCorrect = true },
                    new QuizResultEntry { QuestionId = q[2].Id, IsCorrect = false },
                    new QuizResultEntry { QuestionId = q[3].Id, IsCorrect = true }
                }
            },
            // neuerer Durchgang: q[3] jetzt falsch → zählt als „zuletzt falsch“
            new() { CompletedAt = DateTime.Now, Entries = { new QuizResultEntry { QuestionId = q[3].Id, IsCorrect = false } } }
        };

        var composed = QuizComposer.Compose(new[] { topic }, 4, AllTypes, history, new Random(5));
        var ids = composed.Select(x => x.Question.Id).ToHashSet();

        // q[4], q[5] nie gestellt; q[2], q[3] zuletzt falsch; q[0], q[1] richtig → werden nicht genommen
        Assert.That(ids, Is.EquivalentTo(new[] { q[2].Id, q[3].Id, q[4].Id, q[5].Id }));
    }
}
