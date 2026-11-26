using StudyScope.Infrastructure.Resources;
using StudyScope.Core.Localization;
using System.Globalization;
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
        var chunks = ChunkSelector.SelectRepresentative(contextChunks, MaxChunks);
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
    public static List<string> SelectRepresentativeChunks(IEnumerable<string>? chunks, int maxCount) =>
        ChunkSelector.SelectRepresentative(chunks, maxCount);

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
        var result = LenientJson.Analyze(rawResponse, element => BuildTree(element, fallbackRootTitle));
        return new MindmapParseResult(result.Value, result.WasRepaired, result.FailureReason);
    }

    private static bool IsNode(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ||
        LenientJson.GetProperty(element, TitleKeys) is { ValueKind: JsonValueKind.String };

    private static bool IsNodeOrNodeList(JsonElement element) =>
        (element.ValueKind == JsonValueKind.Object && IsNode(element)) || LenientJson.IsArrayOf(element, IsNode);

    private static MindmapNode? BuildTree(JsonElement rootElement, string fallbackRootTitle)
    {
        var budget = new NodeBudget(MaxNodes);

        // Hülle aus dem JSON-Modus (z. B. {"mindmap": {...}} oder {"items": [...]}) auspacken
        rootElement = LenientJson.Unwrap(rootElement, IsNodeOrNodeList);

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

    private static JsonElement? GetProperty(JsonElement element, string[] keys) => LenientJson.GetProperty(element, keys);

    private static string GetString(JsonElement element, string[] keys) => LenientJson.GetString(element, keys);

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

    public string ExportToMarkdown(MindmapDocument mindmap, CultureInfo culture)
    {
        using var scope = CultureScope.Use(culture);
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
            sb.AppendLine($"*{Loc.Format(ExportStrings.Mindmap_SourcesLine, ("sources", string.Join(", ", mindmap.SourceDocuments)), ("date", ExportNames.DateTime(mindmap.CreatedAt)))}*");
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
