using System.Text.Json;
using StudyScope.Core.Models;

namespace StudyScope.Infrastructure.Services;

/// <summary>
/// Nachsichtige Auswertung von JSON-Antworten einer KI (gemeinsam für Mindmap und Präsentation):
/// entfernt Markdown-Codeblöcke und Begleittext und repariert abgeschnittene Antworten,
/// indem nach dem letzten vollständig geschlossenen Objekt gekürzt und die offenen Klammern ergänzt werden.
/// </summary>
public static class LenientJson
{
    /// <summary>Höchstzahl an Reparaturversuchen (jeweils ein Objekt weniger).</summary>
    private const int MaxRepairAttempts = 200;

    private static readonly JsonDocumentOptions Options = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>Ergebnis: verwertetes Objekt, ob repariert wurde, und ggf. der Fehlergrund.</summary>
    public sealed record Result<T>(T? Value, bool WasRepaired, MindmapFailureReason FailureReason) where T : class;

    /// <param name="rawResponse">Antwort der KI.</param>
    /// <param name="build">Wandelt das geparste JSON in das Zielobjekt um; null = gültiges JSON ohne verwertbaren Inhalt.</param>
    public static Result<T> Analyze<T>(string? rawResponse, Func<JsonElement, T?> build) where T : class
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
            return new Result<T>(null, false, MindmapFailureReason.EmptyResponse);

        var text = StripCodeFence(rawResponse.Trim());

        // Ohne Klammer und Anführungszeichen ist es kein JSON-Versuch, sondern Text (z. B. eine Fehlermeldung)
        var start = text.IndexOfAny(new[] { '[', '{' });
        if (start < 0 || text.IndexOf('"', start) < 0)
            return new Result<T>(null, false, MindmapFailureReason.NoJson);

        var candidate = text[start..];

        // 1. Versuch: vom ersten '[' bzw. '{' bis zur letzten passenden Klammer (vollständige Antwort mit Begleittext)
        var closing = candidate[0] == '[' ? ']' : '}';
        var end = candidate.LastIndexOf(closing);
        if (end > 0 && TryBuild(candidate[..(end + 1)], build, out var value))
        {
            return value != null
                ? new Result<T>(value, false, MindmapFailureReason.None)
                : new Result<T>(null, false, MindmapFailureReason.NoTopics);
        }

        // 2. Versuch: abgeschnittene oder hinten fehlerhafte Antwort reparieren
        foreach (var repaired in GetRepairCandidates(candidate))
        {
            if (TryBuild(repaired, build, out var repairedValue))
            {
                return repairedValue != null
                    ? new Result<T>(repairedValue, true, MindmapFailureReason.None)
                    : new Result<T>(null, false, MindmapFailureReason.InvalidJson);
            }
        }

        return new Result<T>(null, false, MindmapFailureReason.InvalidJson);
    }

    private static bool TryBuild<T>(string json, Func<JsonElement, T?> build, out T? value) where T : class
    {
        value = null;
        try
        {
            using var doc = JsonDocument.Parse(json, Options);
            value = build(doc.RootElement);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string StripCodeFence(string text)
    {
        // Markdown-Codeblock (```json ... ```) entfernen; ein fehlender Abschluss (abgeschnitten) ist erlaubt
        if (!text.StartsWith("```"))
            return text;

        var firstNewline = text.IndexOf('\n');
        text = firstNewline >= 0 ? text[(firstNewline + 1)..] : string.Empty;
        var fence = text.LastIndexOf("```", StringComparison.Ordinal);
        return fence >= 0 ? text[..fence] : text;
    }

    /// <summary>
    /// Liefert reparierte Fassungen einer abgeschnittenen JSON-Antwort, beginnend mit der längsten:
    /// Der Text wird nach jeder vollständig geschlossenen Klammer gekürzt und um die noch offenen
    /// Klammern ergänzt. Klammern innerhalb von Zeichenketten werden ignoriert.
    /// </summary>
    private static IEnumerable<string> GetRepairCandidates(string json)
    {
        var stack = new List<char>();
        var cuts = new List<(int End, string Closers)>();
        var inString = false;
        var escaped = false;

        for (var i = 0; i < json.Length; i++)
        {
            var c = json[i];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }

            if (c == '"')
            {
                inString = true;
            }
            else if (c == '{' || c == '[')
            {
                stack.Add(c);
            }
            else if (c == '}' || c == ']')
            {
                var expected = c == '}' ? '{' : '[';
                if (stack.Count == 0 || stack[^1] != expected)
                    break; // ab hier ist die Struktur kaputt – nur frühere Schnittpunkte verwenden

                stack.RemoveAt(stack.Count - 1);
                var closers = new string(stack.AsEnumerable().Reverse().Select(o => o == '{' ? '}' : ']').ToArray());
                cuts.Add((i, closers));
            }
        }

        for (var k = cuts.Count - 1; k >= 0 && k >= cuts.Count - MaxRepairAttempts; k--)
            yield return json[..(cuts[k].End + 1)] + cuts[k].Closers;
    }

    // ---------------- Hilfen für das Auslesen ----------------

    /// <summary>Erste Eigenschaft, deren Name (ohne Groß-/Kleinschreibung) einem der Schlüssel entspricht.</summary>
    public static JsonElement? GetProperty(JsonElement element, IReadOnlyCollection<string> keys)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var prop in element.EnumerateObject())
        {
            if (keys.Any(k => prop.Name.Equals(k, StringComparison.OrdinalIgnoreCase)))
                return prop.Value;
        }

        return null;
    }

    public static string GetString(JsonElement element, IReadOnlyCollection<string> keys)
    {
        var value = GetProperty(element, keys);
        return value is { ValueKind: JsonValueKind.String } s ? s.GetString() ?? string.Empty : string.Empty;
    }
}
