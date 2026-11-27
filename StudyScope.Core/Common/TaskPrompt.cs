using System.Diagnostics.CodeAnalysis;

namespace StudyScope.Core.Common;

/// <summary>Strukturierter Auftrag eines Dienstes an die KI (alles außer einer Chatfrage).</summary>
public enum TaskKind
{
    Flashcards,
    Mindmap,
    Presentation,
    QuizTopics,
    QuizQuestions,
    Report,
    DataTable,
    AudioScript,
    VideoScript
}

/// <summary>
/// Kennzeile für Aufträge der Dienste: Jeder Dienst setzt vor seinen Prompt die feste erste Zeile
/// <c>[StudyScope-Auftrag: Mindmap]</c>; der Chat nie. Dispatcher und Fake-KI erkennen einen Auftrag nur an dieser
/// Zeile – nie an Wörtern wie „JSON“ oder „Mindmap“, die auch in einer Chatfrage stehen können. Der Dispatcher entfernt
/// die Zeile vor dem Senden, die KI erhält den Prompt unverändert.
/// <para>
/// Technische Schuld: ein Behelf, weil <c>ILlmService</c> keinen Auftragstyp kennt. Später durch einen Auftragstyp in
/// <c>ILlmService</c> ersetzen (z. B. <c>LlmRequest</c> mit <c>Kind</c>) – dann entfallen Kennzeile und <see cref="Defuse"/>.
/// </para>
/// </summary>
public static class TaskPrompt
{
    /// <summary>Beginn der Kennzeile.</summary>
    public const string Prefix = "[StudyScope-Auftrag: ";

    /// <summary>Gemeinsamer Beginn aller eigenen Kennungen (Auftrag, Web-Markierung) – in Benutzertext entschärft.</summary>
    public const string ReservedPrefix = "[StudyScope-";

    /// <summary>Prompt mit Kennzeile: <c>[StudyScope-Auftrag: Mindmap]</c>, Zeilenumbruch, Prompt.</summary>
    public static string Mark(TaskKind kind, string prompt) => $"{Prefix}{kind}]\n{prompt}";

    /// <summary>
    /// Erkennt einen Auftrag nur an der ersten Zeile (exakt <see cref="Prefix"/> + bekannte Art + „]“).
    /// <paramref name="body"/> = Prompt ohne Kennzeile; ohne Kennzeile der unveränderte Prompt.
    /// </summary>
    public static bool TryParse(string? prompt, [NotNullWhen(true)] out TaskKind? kind, out string body)
    {
        kind = null;
        body = prompt ?? string.Empty;
        if (string.IsNullOrEmpty(prompt) || !prompt.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var lineEnd = prompt.IndexOf('\n');
        var firstLine = (lineEnd < 0 ? prompt : prompt[..lineEnd]).TrimEnd('\r');
        if (!firstLine.EndsWith(']'))
            return false;

        var name = firstLine[Prefix.Length..^1];
        if (!Enum.TryParse<TaskKind>(name, ignoreCase: false, out var parsed) || !Enum.IsDefined(parsed) || name != parsed.ToString())
            return false;

        kind = parsed;
        body = lineEnd < 0 ? string.Empty : prompt[(lineEnd + 1)..];
        return true;
    }

    /// <summary>Der Prompt ist ein Auftrag eines Dienstes (hat eine gültige Kennzeile).</summary>
    public static bool IsTask(string? prompt) => TryParse(prompt, out _, out _);

    /// <summary>Prompt ohne Kennzeile (unverändert, wenn keine da ist).</summary>
    public static string Body(string prompt) => TryParse(prompt, out _, out var body) ? body : prompt;

    /// <summary>
    /// Entschärft eigene Kennungen in Benutzertext (Chatfrage): <c>[StudyScope-</c> → <c>(StudyScope-</c>, ohne
    /// Groß-/Kleinschreibung. So löst niemand per Frage einen Auftrag oder den Web-Modus aus. Text ohne diese Folge
    /// bleibt unverändert (gleiches Objekt).
    /// </summary>
    public static string Defuse(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf(ReservedPrefix, StringComparison.OrdinalIgnoreCase) < 0)
            return text;
        // Nur die Klammer ersetzen, die Schreibweise bleibt wie getippt
        return System.Text.RegularExpressions.Regex.Replace(text, @"\[(?=StudyScope-)", "(",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
