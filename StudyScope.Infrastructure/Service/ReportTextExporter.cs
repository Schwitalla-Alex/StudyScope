using System.Text;
using StudyScope.Core.Models;

namespace StudyScope.Infrastructure.Services;

/// <summary>Bericht als Markdown oder reiner Text – inklusive der eingebetteten Studio-Inhalte.</summary>
public static class ReportTextExporter
{
    // ---------------- Markdown ----------------

    public static string ToMarkdown(Report report, ReportStudioContent studio)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {report.Title}");
        sb.AppendLine();
        sb.AppendLine($"*{MetaLine(report)}*");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(report.Summary))
        {
            sb.AppendLine(report.Summary);
            sb.AppendLine();
        }

        foreach (var section in report.Sections)
        {
            if (!string.IsNullOrWhiteSpace(section.Heading))
            {
                sb.AppendLine($"## {section.Heading}");
                sb.AppendLine();
            }

            foreach (var paragraph in section.Paragraphs)
            {
                sb.AppendLine(paragraph);
                sb.AppendLine();
            }

            if (section.Bullets.Count > 0)
            {
                foreach (var bullet in section.Bullets)
                    sb.AppendLine($"- {bullet}");
                sb.AppendLine();
            }

            if (section.Terms.Count > 0)
            {
                sb.AppendLine("**Begriffe**");
                sb.AppendLine();
                foreach (var term in section.Terms)
                    sb.AppendLine(string.IsNullOrWhiteSpace(term.Definition) ? $"- **{term.Term}**" : $"- **{term.Term}:** {term.Definition}");
                sb.AppendLine();
            }

            foreach (var q in section.Questions)
            {
                sb.AppendLine($"**F: {q.Question}**  ");
                sb.AppendLine($"A: {q.Answer}");
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(section.Takeaway))
            {
                sb.AppendLine($"> 💡 **Merksatz:** {section.Takeaway}");
                sb.AppendLine();
            }
        }

        AppendStudioMarkdown(sb, studio);
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static void AppendStudioMarkdown(StringBuilder sb, ReportStudioContent studio)
    {
        if (studio.Mindmap != null)
        {
            sb.AppendLine("## Mindmap");
            sb.AppendLine();
            AppendTreeMarkdown(sb, studio.Mindmap.Root, 0);
            sb.AppendLine();
        }

        if (studio.Flashcards.Count > 0)
        {
            sb.AppendLine($"## Karteikarten ({studio.Flashcards.Count})");
            sb.AppendLine();
            foreach (var card in studio.Flashcards)
            {
                sb.AppendLine($"- **{card.Question}**  ");
                sb.AppendLine($"  {card.Answer}");
            }
            sb.AppendLine();
        }

        if (studio.QuizQuestions.Count > 0)
        {
            sb.AppendLine("## Selbsttest");
            sb.AppendLine();
            for (var i = 0; i < studio.QuizQuestions.Count; i++)
            {
                var q = studio.QuizQuestions[i];
                sb.AppendLine($"{i + 1}. {q.Text}{TypeHint(q)}");
                for (var o = 0; o < q.Options.Count; o++)
                    sb.AppendLine($"   - {Letter(o)}) {q.Options[o]}");
            }
            sb.AppendLine();
            sb.AppendLine("**Lösungen:** " + string.Join(" · ", studio.QuizQuestions.Select((q, i) => $"{i + 1}. {Solution(q)}")));
            sb.AppendLine();
        }

        if (studio.Presentation is { Slides.Count: > 0 } presentation)
        {
            sb.AppendLine($"## Präsentation – {presentation.Title}");
            sb.AppendLine();
            for (var i = 0; i < presentation.Slides.Count; i++)
            {
                var slide = presentation.Slides[i];
                sb.AppendLine($"### Folie {i + 1}: {slide.Title}");
                if (!string.IsNullOrWhiteSpace(slide.Subtitle))
                    sb.AppendLine($"*{slide.Subtitle}*");
                foreach (var bullet in slide.Bullets)
                    sb.AppendLine($"- {bullet}");
                sb.AppendLine();
            }
        }

        foreach (var embedded in studio.DataTables.Where(t => t.Rows.Count > 0))
        {
            sb.AppendLine($"## 📊 {embedded.Table.Title}");
            sb.AppendLine();
            sb.AppendLine($"*{DataTableMeta(embedded)}*");
            sb.AppendLine();
            DataTableExporter.AppendMarkdownTable(sb, embedded.Table, embedded.Rows);
            sb.AppendLine();
        }

        foreach (var audio in studio.AudioScripts.Where(a => a.Segments.Count > 0))
        {
            sb.AppendLine($"## 🎧 {audio.Title}");
            sb.AppendLine();
            sb.AppendLine($"*{AudioMeta(audio)}*");
            sb.AppendLine();
            var speakers = audio.Speakers.Count > 1;
            foreach (var segment in audio.Segments)
            {
                sb.AppendLine(speakers ? $"**{segment.Speaker}:** {segment.Text}" : segment.Text);
                sb.AppendLine();
            }
        }
    }

    private static void AppendTreeMarkdown(StringBuilder sb, MindmapNode node, int level)
    {
        var summary = string.IsNullOrWhiteSpace(node.Summary) ? string.Empty : $" – {node.Summary}";
        sb.AppendLine($"{new string(' ', level * 2)}- **{node.Title}**{summary}");
        foreach (var child in node.Children)
            AppendTreeMarkdown(sb, child, level + 1);
    }

    // ---------------- Reiner Text ----------------

    public static string ToPlainText(Report report, ReportStudioContent studio)
    {
        var sb = new StringBuilder();
        sb.AppendLine(report.Title.ToUpperInvariant());
        sb.AppendLine(new string('=', Math.Min(80, Math.Max(10, report.Title.Length))));
        sb.AppendLine(MetaLine(report));
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(report.Summary))
        {
            sb.AppendLine(report.Summary);
            sb.AppendLine();
        }

        foreach (var section in report.Sections)
        {
            if (!string.IsNullOrWhiteSpace(section.Heading))
                Heading(sb, section.Heading);

            foreach (var paragraph in section.Paragraphs)
            {
                sb.AppendLine(paragraph);
                sb.AppendLine();
            }

            foreach (var bullet in section.Bullets)
                sb.AppendLine($"  • {bullet}");
            if (section.Bullets.Count > 0) sb.AppendLine();

            if (section.Terms.Count > 0)
            {
                sb.AppendLine("Begriffe:");
                foreach (var term in section.Terms)
                    sb.AppendLine(string.IsNullOrWhiteSpace(term.Definition) ? $"  {term.Term}" : $"  {term.Term}: {term.Definition}");
                sb.AppendLine();
            }

            foreach (var q in section.Questions)
            {
                sb.AppendLine($"F: {q.Question}");
                sb.AppendLine($"A: {q.Answer}");
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(section.Takeaway))
            {
                sb.AppendLine($"Merksatz: {section.Takeaway}");
                sb.AppendLine();
            }
        }

        if (studio.Mindmap != null)
        {
            Heading(sb, "Mindmap");
            AppendTreeText(sb, studio.Mindmap.Root, 0);
            sb.AppendLine();
        }

        if (studio.Flashcards.Count > 0)
        {
            Heading(sb, $"Karteikarten ({studio.Flashcards.Count})");
            foreach (var card in studio.Flashcards)
            {
                sb.AppendLine($"F: {card.Question}");
                sb.AppendLine($"A: {card.Answer}");
                sb.AppendLine();
            }
        }

        if (studio.QuizQuestions.Count > 0)
        {
            Heading(sb, "Selbsttest");
            for (var i = 0; i < studio.QuizQuestions.Count; i++)
            {
                var q = studio.QuizQuestions[i];
                sb.AppendLine($"{i + 1}. {q.Text}{TypeHint(q)}");
                for (var o = 0; o < q.Options.Count; o++)
                    sb.AppendLine($"   {Letter(o)}) {q.Options[o]}");
                sb.AppendLine();
            }
            sb.AppendLine("Lösungen: " + string.Join("  ", studio.QuizQuestions.Select((q, i) => $"{i + 1}. {Solution(q)}")));
            sb.AppendLine();
        }

        if (studio.Presentation is { Slides.Count: > 0 } presentation)
        {
            Heading(sb, $"Präsentation – {presentation.Title}");
            for (var i = 0; i < presentation.Slides.Count; i++)
            {
                var slide = presentation.Slides[i];
                sb.AppendLine($"Folie {i + 1}: {slide.Title}");
                foreach (var bullet in slide.Bullets)
                    sb.AppendLine($"  • {bullet}");
                sb.AppendLine();
            }
        }

        foreach (var embedded in studio.DataTables.Where(t => t.Rows.Count > 0))
        {
            Heading(sb, $"Datentabelle – {embedded.Table.Title}");
            sb.AppendLine(DataTableMeta(embedded));
            sb.AppendLine();
            DataTableExporter.AppendPlainText(sb, embedded.Table, embedded.Rows);
            sb.AppendLine();
        }

        foreach (var audio in studio.AudioScripts.Where(a => a.Segments.Count > 0))
        {
            Heading(sb, $"Audio-Skript – {audio.Title}");
            sb.AppendLine(AudioMeta(audio));
            sb.AppendLine();
            var speakers = audio.Speakers.Count > 1;
            foreach (var segment in audio.Segments)
            {
                sb.AppendLine(speakers ? $"{segment.Speaker}: {segment.Text}" : segment.Text);
                sb.AppendLine();
            }
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static void Heading(StringBuilder sb, string text)
    {
        sb.AppendLine(text);
        sb.AppendLine(new string('-', Math.Min(80, Math.Max(5, text.Length))));
    }

    private static void AppendTreeText(StringBuilder sb, MindmapNode node, int level)
    {
        var summary = string.IsNullOrWhiteSpace(node.Summary) ? string.Empty : $" – {node.Summary}";
        sb.AppendLine($"{new string(' ', level * 2)}{(level == 0 ? "" : "• ")}{node.Title}{summary}");
        foreach (var child in node.Children)
            AppendTreeText(sb, child, level + 1);
    }

    // ---------------- gemeinsam ----------------

    public static string MetaLine(Report report)
    {
        var sources = report.SourceDocuments.Count > 0 ? $" · Quellen: {string.Join(", ", report.SourceDocuments)}" : string.Empty;
        return $"{ReportTemplates.DisplayName(report.Template)} · erstellt am {report.CreatedAt:dd.MM.yyyy HH:mm}{sources}";
    }

    /// <summary>Kurzbeschreibung einer eingebetteten Datentabelle („Zeitleiste · 12 Zeilen“ bzw. „erste 50 von 120 Zeilen“).</summary>
    public static string DataTableMeta(ReportDataTable embedded)
    {
        var rows = embedded.IsTruncated
            ? $"erste {embedded.Rows.Count} von {embedded.TotalRows} Zeilen – vollständig im Reiter „Datentabelle“"
            : $"{embedded.Rows.Count} {(embedded.Rows.Count == 1 ? "Zeile" : "Zeilen")}";
        var topic = string.IsNullOrWhiteSpace(embedded.Table.Topic) ? string.Empty : $" · Thema: {embedded.Table.Topic}";
        return $"{DataTableTemplates.DisplayName(embedded.Table.Template)}{topic} · {rows}";
    }

    /// <summary>Kurzbeschreibung eines eingebetteten Audio-Skripts, mit „Audiodatei: …“, wenn eine erzeugt wurde.</summary>
    public static string AudioMeta(AudioSummary audio)
    {
        var parts = new List<string>
        {
            AudioScriptFormats.DisplayName(audio.Format),
            $"{audio.Segments.Count} {(audio.Segments.Count == 1 ? "Abschnitt" : "Abschnitte")}",
            AudioScriptFormats.FormatMinutes(audio.EstimatedMinutes)
        };
        if (!string.IsNullOrWhiteSpace(audio.Topic))
            parts.Add($"Schwerpunkt: {audio.Topic}");
        if (audio.HasAudioFile)
            parts.Add($"Audiodatei: {audio.AudioFileName}");
        return string.Join(" · ", parts);
    }

    public static string Letter(int index) => ((char)('A' + index)).ToString();

    public static string Solution(QuizQuestion q) => string.Join(", ", q.CorrectIndices.Select(i => q.Type == QuizQuestionType.TrueFalse ? q.Options[i] : Letter(i)));

    public static string TypeHint(QuizQuestion q) => q.Type switch
    {
        QuizQuestionType.TrueFalse => " (Wahr oder falsch?)",
        QuizQuestionType.MultipleChoice => " (mehrere Antworten richtig)",
        _ => string.Empty
    };
}
