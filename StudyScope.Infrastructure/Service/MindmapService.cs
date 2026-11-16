using System.Text;
using System.Text.Json;
using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;

namespace StudyScope.Infrastructure.Services;

/// <summary>
/// Erzeugt per KI eine hierarchische Mindmap aus Dokumentauszügen (ähnlich NotebookLM)
/// und speichert sie je Profil unter <c>Mindmaps/&lt;Profil&gt;.json</c> im Benutzerdaten-Ordner.
/// </summary>
public class MindmapService : IMindmapService
{
    /// <summary>Höchstzahl an Textstellen, die an die KI gehen (Kontextgröße lokaler Modelle).</summary>
    public const int MaxChunks = 16;

    /// <summary>Höchste Tiefe unterhalb der Wurzel.</summary>
    public const int MaxDepth = 4;

    /// <summary>Obergrenze für Knoten insgesamt, schützt die Darstellung vor ausufernden Antworten.</summary>
    public const int MaxNodes = 150;

    private const int MaxTitleLength = 120;

    private static readonly string[] TitleKeys = { "title", "titel", "name", "topic", "thema", "label", "text" };
    private static readonly string[] SummaryKeys = { "summary", "zusammenfassung", "description", "beschreibung", "erklaerung", "erklärung" };
    private static readonly string[] ChildrenKeys = { "children", "kinder", "unterthemen", "subtopics", "nodes", "items" };

    private readonly ILlmService _llmService;
    private readonly string _baseDirectory;

    /// <param name="baseDirectory">Basisordner für die Speicherung (Tests); Standard ist <see cref="AppPaths.UserDataDirectory"/>.</param>
    public MindmapService(ILlmService llmService, string? baseDirectory = null)
    {
        _llmService = llmService;
        _baseDirectory = baseDirectory ?? AppPaths.UserDataDirectory;
    }

    public async Task<MindmapGenerationResult> GenerateMindmapAsync(IEnumerable<string> contextChunks, string rootTitle, CancellationToken cancellationToken = default)
    {
        var chunks = SelectRepresentativeChunks(contextChunks, MaxChunks);
        if (chunks.Count == 0)
            return new MindmapGenerationResult { FailureReason = MindmapFailureReason.NoContent };

        // Hinweis: Der Prompt verlangt bewusst ein JSON-ARRAY mit genau einem Wurzelobjekt, weil der
        // LlmServiceDispatcher bei "JSON"-Prompts für einige Provider ein JSON-Array als Antwortformat vorgibt.
        var prompt =
            "Erstelle eine Mindmap der wichtigsten Themen und Zusammenhänge aus den bereitgestellten Textauszügen.\n" +
            "Antworte AUSSCHLIESSLICH als gültiges JSON-Array mit genau EINEM Wurzelobjekt, ohne Einleitung, ohne Markdown und ohne Schlusssatz.\n" +
            "Schema:\n" +
            "[{\"title\": \"Hauptthema\", \"summary\": \"Ein Satz\", \"children\": [" +
            "{\"title\": \"Unterthema\", \"summary\": \"Ein Satz\", \"children\": [" +
            "{\"title\": \"Detail\", \"summary\": \"Ein Satz\", \"children\": []}]}]}]\n" +
            "Regeln:\n" +
            "- Die Wurzel ist das übergeordnete Thema der Auszüge.\n" +
            "- 4 bis 7 Hauptäste, je Ast 2 bis 5 Unterpunkte, höchstens 3 Ebenen unter der Wurzel.\n" +
            "- Titel kurz (höchstens 6 Wörter), summary höchstens ein Satz.\n" +
            "- Insgesamt höchstens 40 Themen, damit die Antwort vollständig bleibt.\n" +
            "- Nur Inhalte verwenden, die in den Auszügen vorkommen. Sprache: Deutsch.";

        var raw = await _llmService.AskQuestionAsync(prompt, chunks, cancellationToken);
        var parsed = AnalyzeResponse(raw, rootTitle);
        return new MindmapGenerationResult
        {
            Root = parsed.Root,
            RawResponse = raw ?? string.Empty,
            WasRepaired = parsed.WasRepaired,
            FailureReason = parsed.FailureReason
        };
    }

    /// <summary>
    /// Wählt gleichmäßig verteilte Textstellen aus, damit die Mindmap das ganze Dokument abdeckt
    /// und nicht nur den Anfang (wie bei einem einfachen Take(n)).
    /// </summary>
    public static List<string> SelectRepresentativeChunks(IEnumerable<string>? chunks, int maxCount)
    {
        var list = chunks?.Where(c => !string.IsNullOrWhiteSpace(c)).ToList() ?? new List<string>();
        if (list.Count <= maxCount || maxCount <= 0)
            return maxCount <= 0 ? new List<string>() : list;

        var result = new List<string>(maxCount);
        for (var i = 0; i < maxCount; i++)
        {
            // Indizes von 0 bis Count-1 gleichmäßig verteilt (erste und letzte Textstelle sind dabei)
            var index = (int)Math.Round(i * (list.Count - 1) / (double)(maxCount - 1));
            result.Add(list[index]);
        }

        return result;
    }

    /// <summary>Ergebnis der Auswertung einer KI-Antwort.</summary>
    public sealed record MindmapParseResult(MindmapNode? Root, bool WasRepaired, MindmapFailureReason FailureReason);

    /// <summary>
    /// Wandelt die KI-Antwort in einen Knotenbaum um (Kurzform von <see cref="AnalyzeResponse"/>).
    /// Liefert null, wenn nichts Verwertbares enthalten ist.
    /// </summary>
    public static MindmapNode? ParseMindmap(string? rawResponse, string fallbackRootTitle) =>
        AnalyzeResponse(rawResponse, fallbackRootTitle).Root;

    /// <summary>
    /// Wertet die KI-Antwort aus. Toleriert Markdown-Codeblöcke, Begleittext, ein Objekt statt eines Arrays,
    /// mehrere Wurzeln (werden unter <paramref name="fallbackRootTitle"/> gruppiert), deutsche Schlüsselnamen
    /// und Unterpunkte als reine Texte. Ist die Antwort abgeschnitten (häufig bei lokalen Modellen, wenn das
    /// Kontextfenster voll ist), wird bis zum letzten vollständigen Thema gekürzt und der Rest ergänzt.
    /// </summary>
    public static MindmapParseResult AnalyzeResponse(string? rawResponse, string fallbackRootTitle)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
            return new MindmapParseResult(null, false, MindmapFailureReason.EmptyResponse);

        var text = StripCodeFence(rawResponse.Trim());

        // Ohne Klammer und Anführungszeichen ist es kein JSON-Versuch, sondern Text (z. B. eine Fehlermeldung)
        var start = text.IndexOfAny(new[] { '[', '{' });
        if (start < 0 || text.IndexOf('"', start) < 0)
            return new MindmapParseResult(null, false, MindmapFailureReason.NoJson);

        var candidate = text[start..];

        // 1. Versuch: vom ersten '[' bzw. '{' bis zur letzten passenden Klammer (vollständige Antwort mit Begleittext)
        var closing = candidate[0] == '[' ? ']' : '}';
        var end = candidate.LastIndexOf(closing);
        if (end > 0 && TryBuildTree(candidate[..(end + 1)], fallbackRootTitle, out var root))
        {
            return root != null
                ? new MindmapParseResult(root, false, MindmapFailureReason.None)
                : new MindmapParseResult(null, false, MindmapFailureReason.NoTopics);
        }

        // 2. Versuch: abgeschnittene oder hinten fehlerhafte Antwort reparieren
        foreach (var repaired in GetRepairCandidates(candidate))
        {
            if (TryBuildTree(repaired, fallbackRootTitle, out var repairedRoot))
            {
                return repairedRoot != null
                    ? new MindmapParseResult(repairedRoot, true, MindmapFailureReason.None)
                    : new MindmapParseResult(null, false, MindmapFailureReason.InvalidJson);
            }
        }

        return new MindmapParseResult(null, false, MindmapFailureReason.InvalidJson);
    }

    /// <summary>
    /// Parst JSON und baut den Baum. Rückgabe false = kein gültiges JSON;
    /// true mit <paramref name="root"/> = null = gültiges JSON ohne verwertbare Themen.
    /// </summary>
    private static bool TryBuildTree(string json, string fallbackRootTitle, out MindmapNode? root)
    {
        root = null;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            root = BuildTree(doc.RootElement, fallbackRootTitle);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static MindmapNode? BuildTree(JsonElement rootElement, string fallbackRootTitle)
    {
        var budget = new NodeBudget(MaxNodes);

        if (rootElement.ValueKind == JsonValueKind.Object)
            return ParseNode(rootElement, 0, budget);

        if (rootElement.ValueKind != JsonValueKind.Array)
            return null;

        var roots = rootElement.EnumerateArray().ToList();
        if (roots.Count == 1)
            return ParseNode(roots[0], 0, budget);

        // Mehrere Wurzeln: unter einem gemeinsamen Hauptthema zusammenfassen
        var root = new MindmapNode { Title = Shorten(string.IsNullOrWhiteSpace(fallbackRootTitle) ? "Mindmap" : fallbackRootTitle) };
        budget.TryTake();
        foreach (var element in roots)
        {
            var child = ParseNode(element, 1, budget);
            if (child != null)
                root.Children.Add(child);
        }

        return root.Children.Count > 0 ? root : null;
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

    /// <summary>Höchstzahl an Reparaturversuchen (jeweils ein Thema weniger).</summary>
    private const int MaxRepairAttempts = 200;

    /// <summary>
    /// Liefert reparierte Fassungen einer abgeschnittenen JSON-Antwort, beginnend mit der längsten:
    /// Der Text wird nach jeder vollständig geschlossenen Klammer (also nach einem vollständigen Thema) gekürzt
    /// und um die noch offenen Klammern ergänzt. Klammern innerhalb von Zeichenketten werden ignoriert.
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

    private static MindmapNode? ParseNode(JsonElement element, int depth, NodeBudget budget)
    {
        string title;
        string summary = string.Empty;
        JsonElement? children = null;

        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                title = element.GetString() ?? string.Empty;
                break;

            case JsonValueKind.Object:
                title = GetString(element, TitleKeys);
                summary = GetString(element, SummaryKeys);
                children = GetProperty(element, ChildrenKeys);
                break;

            default:
                return null;
        }

        if (string.IsNullOrWhiteSpace(title) || !budget.TryTake())
            return null;

        var node = new MindmapNode { Title = Shorten(title), Summary = summary.Trim() };

        if (children is { ValueKind: JsonValueKind.Array } childArray && depth < MaxDepth)
        {
            foreach (var childElement in childArray.EnumerateArray())
            {
                var child = ParseNode(childElement, depth + 1, budget);
                if (child != null)
                    node.Children.Add(child);
            }
        }

        return node;
    }

    private static JsonElement? GetProperty(JsonElement element, string[] keys)
    {
        foreach (var prop in element.EnumerateObject())
        {
            if (keys.Any(k => prop.Name.Equals(k, StringComparison.OrdinalIgnoreCase)))
                return prop.Value;
        }

        return null;
    }

    private static string GetString(JsonElement element, string[] keys)
    {
        var value = GetProperty(element, keys);
        return value is { ValueKind: JsonValueKind.String } s ? s.GetString() ?? string.Empty : string.Empty;
    }

    private static string Shorten(string title)
    {
        var t = title.Trim().Replace('\n', ' ').Replace('\r', ' ');
        return t.Length <= MaxTitleLength ? t : t[..(MaxTitleLength - 1)].TrimEnd() + "…";
    }

    private sealed class NodeBudget
    {
        private int _remaining;
        public NodeBudget(int max) => _remaining = max;

        public bool TryTake()
        {
            if (_remaining <= 0)
                return false;
            _remaining--;
            return true;
        }
    }

    // ---------------- Speicherung ----------------

    public async Task<MindmapDocument?> LoadMindmapAsync(string profileName)
    {
        var path = GetFilePath(profileName);
        if (!File.Exists(path))
            return null;

        try
        {
            await using var stream = File.OpenRead(path);
            var mindmap = await JsonSerializer.DeserializeAsync<MindmapDocument>(stream);
            if (mindmap?.Root == null || string.IsNullOrWhiteSpace(mindmap.Root.Title))
                return null;

            EnsureUniqueIds(mindmap.Root);
            return mindmap;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    public async Task SaveMindmapAsync(string profileName, MindmapDocument mindmap)
    {
        var path = GetFilePath(profileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, mindmap, new JsonSerializerOptions { WriteIndented = true });
    }

    public Task DeleteMindmapAsync(string profileName)
    {
        var path = GetFilePath(profileName);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    private string GetFilePath(string profileName) =>
        Path.Combine(_baseDirectory, "Mindmaps", $"{JsonStorageService.ToSafeProfileFileName(profileName)}.json");

    /// <summary>Vergibt fehlende oder doppelte IDs neu (z. B. bei von Hand bearbeiteten Dateien).</summary>
    private static void EnsureUniqueIds(MindmapNode root)
    {
        var seen = new HashSet<string>();
        foreach (var node in root.DescendantsAndSelf())
        {
            node.Children ??= new List<MindmapNode>();
            if (string.IsNullOrWhiteSpace(node.Id) || !seen.Add(node.Id))
            {
                node.Id = Guid.NewGuid().ToString("N");
                seen.Add(node.Id);
            }
        }
    }

    // ---------------- Export ----------------

    public string ExportToMarkdown(MindmapDocument mindmap)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {mindmap.Root.Title}");
        if (!string.IsNullOrWhiteSpace(mindmap.Root.Summary))
        {
            sb.AppendLine();
            sb.AppendLine(mindmap.Root.Summary);
        }

        sb.AppendLine();
        if (mindmap.SourceDocuments.Count > 0)
        {
            sb.AppendLine($"*Quellen: {string.Join(", ", mindmap.SourceDocuments)} – erstellt am {mindmap.CreatedAt:dd.MM.yyyy HH:mm}*");
            sb.AppendLine();
        }

        foreach (var branch in mindmap.Root.Children)
            AppendMarkdown(sb, branch, 0);

        return sb.ToString();
    }

    private static void AppendMarkdown(StringBuilder sb, MindmapNode node, int level)
    {
        if (level == 0)
        {
            // Hauptäste als Überschriften – so erkennt z. B. Markmap die Struktur
            sb.AppendLine($"## {node.Title}");
            if (!string.IsNullOrWhiteSpace(node.Summary))
                sb.AppendLine(node.Summary);
            sb.AppendLine();
        }
        else
        {
            var indent = new string(' ', (level - 1) * 2);
            var summary = string.IsNullOrWhiteSpace(node.Summary) ? string.Empty : $" – {node.Summary}";
            sb.AppendLine($"{indent}- **{node.Title}**{summary}");
        }

        foreach (var child in node.Children)
            AppendMarkdown(sb, child, level + 1);

        if (level == 0 && node.Children.Count > 0)
            sb.AppendLine();
    }
}
