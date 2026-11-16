using StudyScope.Core.Models;

namespace StudyScope.ViewModels.Mindmap;

/// <summary>Position eines sichtbaren Knotens in der Zeichenfläche (logische Pixel).</summary>
public sealed record MindmapLayoutNode(
    MindmapNode Node,
    double X,
    double Y,
    double Width,
    double Height,
    int Depth,
    int BranchIndex,
    string DisplayTitle,
    bool HasChildren,
    bool IsExpanded)
{
    public double CenterY => Y + Height / 2;
    public double Right => X + Width;
}

/// <summary>Verbindungslinie von der rechten Kante des Elternknotens zur linken Kante des Kindknotens.</summary>
public sealed record MindmapLayoutEdge(double FromX, double FromY, double ToX, double ToY, int BranchIndex);

public sealed record MindmapLayoutResult(
    IReadOnlyList<MindmapLayoutNode> Nodes,
    IReadOnlyList<MindmapLayoutEdge> Edges,
    double Width,
    double Height)
{
    public static readonly MindmapLayoutResult Empty = new(Array.Empty<MindmapLayoutNode>(), Array.Empty<MindmapLayoutEdge>(), 0, 0);
}

/// <summary>
/// Berechnet ein horizontales Baum-Layout wie bei NotebookLM: Wurzel links, Unterthemen rechts daneben,
/// jeder Elternknoten vertikal mittig zu seinen Kindern. Reine Berechnung ohne UI – dadurch testbar.
/// </summary>
public static class MindmapLayout
{
    public const double NodeHeight = 38;
    public const double VerticalGap = 12;
    public const double HorizontalGap = 70;
    public const double Padding = 24;
    public const double MinNodeWidth = 90;
    public const double MaxNodeWidth = 260;

    /// <summary>Geschätzte Zeichenbreite bei 13px Schrift.</summary>
    private const double CharWidth = 7.4;

    /// <summary>Platz für Innenabstand und den Auf-/Zuklapp-Knopf.</summary>
    private const double NodeChrome = 40;

    public static MindmapLayoutResult Calculate(MindmapNode? root, Func<MindmapNode, bool> isExpanded)
    {
        if (root == null)
            return MindmapLayoutResult.Empty;

        // 1. Sichtbare Knoten mit Tiefe und Hauptast bestimmen
        var visible = new List<(MindmapNode Node, int Depth, int Branch, MindmapNode? Parent)>();
        Collect(root, 0, -1, null, isExpanded, visible);

        // 2. Spaltenbreite je Ebene = breitester Knoten dieser Ebene → saubere Spalten
        var widths = visible.ToDictionary(v => v.Node, v => EstimateWidth(v.Node.Title));
        var maxDepth = visible.Max(v => v.Depth);
        var columnX = new double[maxDepth + 1];
        columnX[0] = Padding;
        for (var d = 1; d <= maxDepth; d++)
        {
            var previousWidth = visible.Where(v => v.Depth == d - 1).Max(v => widths[v.Node]);
            columnX[d] = columnX[d - 1] + previousWidth + HorizontalGap;
        }

        // 3. Vertikale Positionen: Blätter untereinander, Eltern mittig zu ihren Kindern
        var y = new Dictionary<MindmapNode, double>();
        var cursor = Padding;
        PlaceVertically(root, isExpanded, y, ref cursor);

        var nodes = new List<MindmapLayoutNode>(visible.Count);
        var byNode = new Dictionary<MindmapNode, MindmapLayoutNode>();
        foreach (var (node, depth, branch, _) in visible)
        {
            var width = widths[node];
            var layoutNode = new MindmapLayoutNode(
                node,
                columnX[depth],
                y[node],
                width,
                NodeHeight,
                depth,
                branch,
                Truncate(node.Title, width),
                node.Children.Count > 0,
                node.Children.Count > 0 && isExpanded(node));
            nodes.Add(layoutNode);
            byNode[node] = layoutNode;
        }

        var edges = visible
            .Where(v => v.Parent != null)
            .Select(v =>
            {
                var parent = byNode[v.Parent!];
                var child = byNode[v.Node];
                return new MindmapLayoutEdge(parent.Right, parent.CenterY, child.X, child.CenterY, v.Branch);
            })
            .ToList();

        var totalWidth = nodes.Max(n => n.Right) + Padding;
        var totalHeight = nodes.Max(n => n.Y + n.Height) + Padding;
        return new MindmapLayoutResult(nodes, edges, totalWidth, totalHeight);
    }

    private static void Collect(
        MindmapNode node, int depth, int branch, MindmapNode? parent,
        Func<MindmapNode, bool> isExpanded,
        List<(MindmapNode, int, int, MindmapNode?)> result)
    {
        result.Add((node, depth, branch, parent));
        if (node.Children.Count == 0 || !isExpanded(node))
            return;

        for (var i = 0; i < node.Children.Count; i++)
        {
            // Hauptäste (Ebene 1) bestimmen die Farbe ihres ganzen Teilbaums
            var childBranch = depth == 0 ? i : branch;
            Collect(node.Children[i], depth + 1, childBranch, node, isExpanded, result);
        }
    }

    private static void PlaceVertically(MindmapNode node, Func<MindmapNode, bool> isExpanded, Dictionary<MindmapNode, double> y, ref double cursor)
    {
        if (node.Children.Count == 0 || !isExpanded(node))
        {
            y[node] = cursor;
            cursor += NodeHeight + VerticalGap;
            return;
        }

        foreach (var child in node.Children)
            PlaceVertically(child, isExpanded, y, ref cursor);

        y[node] = (y[node.Children[0]] + y[node.Children[^1]]) / 2;
    }

    public static double EstimateWidth(string title) =>
        Math.Clamp(title.Length * CharWidth + NodeChrome, MinNodeWidth, MaxNodeWidth);

    /// <summary>Kürzt den Titel auf die Knotenbreite; der volle Titel steht im Tooltip.</summary>
    public static string Truncate(string title, double width)
    {
        // kleine Toleranz gegen Rundungsfehler (sonst würde z. B. „Mitochondrien“ unnötig gekürzt)
        var maxChars = (int)Math.Floor((width - NodeChrome) / CharWidth + 0.001);
        return title.Length <= maxChars || maxChars < 2 ? title : title[..(maxChars - 1)].TrimEnd() + "…";
    }
}
