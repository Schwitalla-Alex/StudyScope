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
            return new MindmapGenerationResult();

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
            "- Nur Inhalte verwenden, die in den Auszügen vorkommen. Sprache: Deutsch.";

        var raw = await _llmService.AskQuestionAsync(prompt, chunks, cancellationToken);
        return new MindmapGenerationResult
        {
            Root = ParseMindmap(raw, rootTitle),
            RawResponse = raw ?? string.Empty
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

    /// <summary>
    /// Wandelt die KI-Antwort in einen Knotenbaum um. Toleriert Markdown-Codeblöcke, Begleittext,
    /// ein Objekt statt eines Arrays, mehrere Wurzeln (werden unter <paramref name="fallbackRootTitle"/> gruppiert),
    /// deutsche Schlüsselnamen und Unterpunkte als reine Texte. Liefert null, wenn nichts Verwertbares enthalten ist.
    /// </summary>
    public static MindmapNode? ParseMindmap(string? rawResponse, string fallbackRootTitle)
    {
        var json = ExtractJson(rawResponse);
        if (json == null)
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var budget = new NodeBudget(MaxNodes);
            var rootElement = doc.RootElement;

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
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractJson(string? rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
            return null;

        var text = rawResponse.Trim();

        // Markdown-Codeblock (```json ... ```) entfernen
        if (text.StartsWith("```"))
        {
            var firstNewline = text.IndexOf('\n');
            text = firstNewline >= 0 ? text[(firstNewline + 1)..] : string.Empty;
            var fence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0)
                text = text[..fence];
        }

        // Ab dem ersten '[' oder '{' bis zur passenden letzten Klammer derselben Art
        var start = text.IndexOfAny(new[] { '[', '{' });
        if (start < 0)
            return null;

        var closing = text[start] == '[' ? ']' : '}';
        var end = text.LastIndexOf(closing);
        return end > start ? text.Substring(start, end - start + 1) : null;
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
