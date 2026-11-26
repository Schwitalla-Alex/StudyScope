using System.Globalization;
using System.Text;
using StudyScope.Core.Localization;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.Resources;

namespace StudyScope.Infrastructure.Services;

/// <summary>
/// Bericht als Markdown oder reiner Text – inklusive der eingebetteten Studio-Inhalte. Feste Texte stehen in der
/// übergebenen Sprache (<see cref="ExportStrings"/>). Die öffentlichen Hilfen (<see cref="MetaLine"/>,
/// <see cref="DataTableMeta"/>, <see cref="AudioMeta"/>, <see cref="VideoMeta"/>, <see cref="TypeHint"/>) nutzen die Sprache,
/// die der Aufrufer per <see cref="CultureScope"/> gesetzt hat (Word- und HTML-Export verwenden sie mit).
/// </summary>
public static class ReportTextExporter
{
    // ---------------- Markdown ----------------

    public static string ToMarkdown(Report report, ReportStudioContent studio, CultureInfo culture)
    {
        using var scope = CultureScope.Use(culture);
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
                sb.AppendLine($"**{ExportStrings.Report_Terms}**");
                sb.AppendLine();
                foreach (var term in section.Terms)
                    sb.AppendLine(string.IsNullOrWhiteSpace(term.Definition) ? $"- **{term.Term}**" : $"- **{term.Term}:** {term.Definition}");
                sb.AppendLine();
            }

            foreach (var q in section.Questions)
            {
                sb.AppendLine($"**{Loc.Format(ExportStrings.Export_QuestionLine, ("text", q.Question))}**  ");
                sb.AppendLine(Loc.Format(ExportStrings.Export_AnswerLine, ("text", q.Answer)));
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(section.Takeaway))
            {
                sb.AppendLine($"> 💡 **{ExportStrings.Report_Takeaway}** {section.Takeaway}");
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
            sb.AppendLine($"## {ExportStrings.Report_Mindmap}");
            sb.AppendLine();
            AppendTreeMarkdown(sb, studio.Mindmap.Root, 0);
            sb.AppendLine();
        }

        if (studio.Flashcards.Count > 0)
        {
            sb.AppendLine($"## {Loc.Format(ExportStrings.Report_FlashcardsCount, ("count", studio.Flashcards.Count))}");
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
            sb.AppendLine($"## {ExportStrings.Report_SelfTest}");
            sb.AppendLine();
            for (var i = 0; i < studio.QuizQuestions.Count; i++)
            {
                var q = studio.QuizQuestions[i];
                sb.AppendLine($"{i + 1}. {q.Text}{TypeHint(q)}");
                for (var o = 0; o < q.Options.Count; o++)
                    sb.AppendLine($"   - {Letter(o)}) {q.Options[o]}");
            }
            sb.AppendLine();
            sb.AppendLine($"**{Loc.Format(ExportStrings.Report_SolutionsLine, ("solutions", SolutionList(studio.QuizQuestions, " · ")))}**");
            sb.AppendLine();
        }

        if (studio.Presentation is { Slides.Count: > 0 } presentation)
        {
            sb.AppendLine($"## {Loc.Format(ExportStrings.Report_PresentationHeading, ("title", presentation.Title))}");
            sb.AppendLine();
            for (var i = 0; i < presentation.Slides.Count; i++)
            {
                var slide = presentation.Slides[i];
                sb.AppendLine($"### {Loc.Format(ExportStrings.Export_SlideHeading, ("number", i + 1), ("title", slide.Title))}");
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
                sb.AppendLine(speakers ? $"**{ExportNames.Speaker(segment.Speaker)}:** {segment.Text}" : segment.Text);
                sb.AppendLine();
            }
        }

        foreach (var video in studio.VideoScripts.Where(ReportVideoScripts.HasNarration))
        {
            sb.AppendLine($"## 🎬 {video.Title}");
            sb.AppendLine();
            sb.AppendLine($"*{VideoMeta(video)}*");
            sb.AppendLine();
            for (var i = 0; i < video.Scenes.Count; i++)
            {
                if (!video.Scenes[i].HasNarration) continue;
                sb.AppendLine($"**{ReportVideoScripts.SceneHeading(video, i)}**");
                sb.AppendLine();
                sb.AppendLine(video.Scenes[i].Narration.Trim());
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

    public static string ToPlainText(Report report, ReportStudioContent studio, CultureInfo culture)
    {
        using var scope = CultureScope.Use(culture);
        var sb = new StringBuilder();
        sb.AppendLine(report.Title.ToUpper(culture));
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
                sb.AppendLine($"{ExportStrings.Report_Terms}:");
                foreach (var term in section.Terms)
                    sb.AppendLine(string.IsNullOrWhiteSpace(term.Definition) ? $"  {term.Term}" : $"  {term.Term}: {term.Definition}");
                sb.AppendLine();
            }

            foreach (var q in section.Questions)
            {
                sb.AppendLine(Loc.Format(ExportStrings.Export_QuestionLine, ("text", q.Question)));
                sb.AppendLine(Loc.Format(ExportStrings.Export_AnswerLine, ("text", q.Answer)));
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(section.Takeaway))
            {
                sb.AppendLine($"{ExportStrings.Report_Takeaway} {section.Takeaway}");
                sb.AppendLine();
            }
        }

        if (studio.Mindmap != null)
        {
            Heading(sb, ExportStrings.Report_Mindmap);
            AppendTreeText(sb, studio.Mindmap.Root, 0);
            sb.AppendLine();
        }

        if (studio.Flashcards.Count > 0)
        {
            Heading(sb, Loc.Format(ExportStrings.Report_FlashcardsCount, ("count", studio.Flashcards.Count)));
            foreach (var card in studio.Flashcards)
            {
                sb.AppendLine(Loc.Format(ExportStrings.Export_QuestionLine, ("text", card.Question)));
                sb.AppendLine(Loc.Format(ExportStrings.Export_AnswerLine, ("text", card.Answer)));
                sb.AppendLine();
            }
        }

        if (studio.QuizQuestions.Count > 0)
        {
            Heading(sb, ExportStrings.Report_SelfTest);
            for (var i = 0; i < studio.QuizQuestions.Count; i++)
            {
                var q = studio.QuizQuestions[i];
                sb.AppendLine($"{i + 1}. {q.Text}{TypeHint(q)}");
                for (var o = 0; o < q.Options.Count; o++)
                    sb.AppendLine($"   {Letter(o)}) {q.Options[o]}");
                sb.AppendLine();
            }
            sb.AppendLine(Loc.Format(ExportStrings.Report_SolutionsLine, ("solutions", SolutionList(studio.QuizQuestions, "  "))));
            sb.AppendLine();
        }

        if (studio.Presentation is { Slides.Count: > 0 } presentation)
        {
            Heading(sb, Loc.Format(ExportStrings.Report_PresentationHeading, ("title", presentation.Title)));
            for (var i = 0; i < presentation.Slides.Count; i++)
            {
                var slide = presentation.Slides[i];
                sb.AppendLine(Loc.Format(ExportStrings.Export_SlideHeading, ("number", i + 1), ("title", slide.Title)));
                foreach (var bullet in slide.Bullets)
                    sb.AppendLine($"  • {bullet}");
                sb.AppendLine();
            }
        }

        foreach (var embedded in studio.DataTables.Where(t => t.Rows.Count > 0))
        {
            Heading(sb, Loc.Format(ExportStrings.Report_DataTableHeading, ("title", embedded.Table.Title)));
            sb.AppendLine(DataTableMeta(embedded));
            sb.AppendLine();
            DataTableExporter.AppendPlainText(sb, embedded.Table, embedded.Rows);
            sb.AppendLine();
        }

        foreach (var audio in studio.AudioScripts.Where(a => a.Segments.Count > 0))
        {
            Heading(sb, Loc.Format(ExportStrings.Report_AudioScriptHeading, ("title", audio.Title)));
            sb.AppendLine(AudioMeta(audio));
            sb.AppendLine();
            var speakers = audio.Speakers.Count > 1;
            foreach (var segment in audio.Segments)
            {
                sb.AppendLine(speakers ? $"{ExportNames.Speaker(segment.Speaker)}: {segment.Text}" : segment.Text);
                sb.AppendLine();
            }
        }

        foreach (var video in studio.VideoScripts.Where(ReportVideoScripts.HasNarration))
        {
            Heading(sb, Loc.Format(ExportStrings.Report_VideoScriptHeading, ("title", video.Title)));
            sb.AppendLine(VideoMeta(video));
            sb.AppendLine();
            for (var i = 0; i < video.Scenes.Count; i++)
            {
                if (!video.Scenes[i].HasNarration) continue;
                sb.AppendLine(ReportVideoScripts.SceneHeading(video, i));
                sb.AppendLine(video.Scenes[i].Narration.Trim());
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

    // ---------------- gemeinsam (Sprache aus CultureScope des Aufrufers) ----------------

    /// <summary>„Lernübersicht · erstellt am 26.11.2026 14:30 · Quellen: …“.</summary>
    public static string MetaLine(Report report)
    {
        var parts = new List<string> { ExportNames.Template(report.Template), ExportNames.CreatedAt(report.CreatedAt) };
        if (report.SourceDocuments.Count > 0)
            parts.Add(ExportNames.Sources(report.SourceDocuments));
        return string.Join(" · ", parts);
    }

    /// <summary>Kurzbeschreibung einer eingebetteten Datentabelle („Zeitleiste · 12 Zeilen“ bzw. „erste 50 von 120 Zeilen“).</summary>
    public static string DataTableMeta(ReportDataTable embedded)
    {
        var parts = new List<string> { ExportNames.Template(embedded.Table.Template) };
        if (!string.IsNullOrWhiteSpace(embedded.Table.Topic))
            parts.Add(Loc.Format(ExportStrings.Export_Topic, ("topic", embedded.Table.Topic)));
        parts.Add(embedded.IsTruncated
            ? Loc.Format(ExportStrings.Export_RowsTruncated, ("shown", embedded.Rows.Count), ("total", embedded.TotalRows))
            : Loc.Format(ExportStrings.Export_RowCount, ("count", embedded.Rows.Count)));
        return string.Join(" · ", parts);
    }

    /// <summary>Kurzbeschreibung eines eingebetteten Audio-Skripts, mit „Audiodatei: …“, wenn eine erzeugt wurde.</summary>
    public static string AudioMeta(AudioSummary audio)
    {
        var parts = new List<string>
        {
            ExportNames.Format(audio.Format),
            Loc.Format(ExportStrings.Export_SegmentCount, ("count", audio.Segments.Count)),
            MediaFormat.Minutes(audio.EstimatedMinutes)
        };
        if (!string.IsNullOrWhiteSpace(audio.Topic))
            parts.Add(Loc.Format(ExportStrings.Export_Focus, ("topic", audio.Topic)));
        if (audio.HasAudioFile)
            parts.Add(Loc.Format(ExportStrings.Export_AudioFile, ("file", audio.AudioFileName)));
        return string.Join(" · ", parts);
    }

    /// <summary>
    /// Kurzbeschreibung eines eingebetteten Video-Skripts: Folienzahl, Dauer (aus der aktuellen Audiodatei, sonst
    /// geschätzt), Vertonung und der Hinweis „Video: Titel“ statt eingebetteter Folien bzw. Audiodatei.
    /// </summary>
    public static string VideoMeta(VideoDocument video)
    {
        var hasCurrentAudio = video.HasAudioFile && !video.IsAudioOutdated && video.AudioDurationSeconds > 0;
        var minutes = hasCurrentAudio ? video.AudioDurationSeconds / 60 : AudioScriptFormats.EstimateMinutes(video.WordCount);
        var parts = new List<string>
        {
            Loc.Format(ExportStrings.Export_SlideCount, ("count", video.Scenes.Count)),
            MediaFormat.Minutes(minutes)
        };
        if (hasCurrentAudio && !string.IsNullOrWhiteSpace(video.AudioCreatedWith))
            parts.Add(Loc.Format(ExportStrings.Export_NarratedWith, ("provider", video.AudioCreatedWith)));
        parts.Add(Loc.Format(ExportStrings.Export_VideoInApp, ("title", video.Title)));
        return string.Join(" · ", parts);
    }

    public static string Letter(int index) => ((char)('A' + index)).ToString();

    public static string Solution(QuizQuestion q) => string.Join(", ", q.CorrectIndices.Select(i => q.Type == QuizQuestionType.TrueFalse ? q.Options[i] : Letter(i)));

    /// <summary>„1. A · 2. B, C“ (Trennzeichen je Format).</summary>
    public static string SolutionList(IReadOnlyList<QuizQuestion> questions, string separator) =>
        string.Join(separator, questions.Select((q, i) => $"{i + 1}. {Solution(q)}"));

    public static string TypeHint(QuizQuestion q) => q.Type switch
    {
        QuizQuestionType.TrueFalse => " " + ExportStrings.Report_TypeTrueFalse,
        QuizQuestionType.MultipleChoice => " " + ExportStrings.Report_TypeMultiple,
        _ => string.Empty
    };
}
