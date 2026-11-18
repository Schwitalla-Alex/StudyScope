using System.Globalization;
using System.Net;
using System.Text;
using StudyScope.Core.Common;
using StudyScope.Core.Models;

namespace StudyScope.Infrastructure.Services;

/// <summary>
/// Erzeugt den interaktiven Bericht als eigenständige HTML-Seite (CSS und JavaScript eingebettet,
/// keine externen Dateien, funktioniert offline). Dieselbe Seite wird in StudyScope angezeigt und
/// als .html exportiert. Interaktiv: Inhaltsverzeichnis, aufklappbare FAQ und Mindmap,
/// umdrehbare Karteikarten, Selbsttest mit Auswertung, Folien zum Durchblättern und sortierbare Datentabellen.
/// Beim Drucken (PDF) wird alles aufgeklappt und mit Lösungen ausgegeben.
/// </summary>
public static class ReportHtmlExporter
{
    public static string Export(Report report, ReportStudioContent studio, ReportHtmlOptions? options = null)
    {
        options ??= ReportHtmlOptions.Default;
        var videoScripts = studio.VideoScripts.Where(ReportVideoScripts.HasNarration).ToList();
        var withPlayers = videoScripts.Any(v => options.VideoPlayers.ContainsKey(v.Id));

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n<html lang=\"de\">\n<head>\n<meta charset=\"utf-8\">\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        sb.Append($"<meta name=\"generator\" content=\"StudyScope\">\n<title>{H(report.Title)}</title>\n");
        sb.Append("<style>\n").Append(Css);
        if (withPlayers) sb.Append('\n').Append(VideoPlayerHtml.Css);
        sb.Append("\n</style>\n</head>\n<body>\n<main class=\"report\">\n");

        // Kopf
        sb.Append("<header class=\"head\">");
        sb.Append($"<div class=\"badge\">{H(ReportTemplates.Icon(report.Template))} {H(ReportTemplates.DisplayName(report.Template))}</div>");
        sb.Append($"<h1>{H(report.Title)}</h1>");
        sb.Append($"<div class=\"meta\">{H(ReportTextExporter.MetaLine(report))}</div>");
        if (!string.IsNullOrWhiteSpace(report.Summary))
            sb.Append($"<p class=\"summary\">{Text(report.Summary)}</p>");
        sb.Append("</header>\n");

        // Inhaltsverzeichnis
        var toc = new List<(string Id, string Title)>();
        for (var i = 0; i < report.Sections.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(report.Sections[i].Heading))
                toc.Add(($"abschnitt-{i + 1}", report.Sections[i].Heading));
        }
        if (studio.Mindmap != null) toc.Add(("mindmap", "🧠 Mindmap"));
        if (studio.Flashcards.Count > 0) toc.Add(("karteikarten", "🎴 Karteikarten"));
        if (studio.QuizQuestions.Count > 0) toc.Add(("selbsttest", "❓ Selbsttest"));
        if (studio.Presentation is { Slides.Count: > 0 }) toc.Add(("praesentation", "📽️ Präsentation"));
        var dataTables = studio.DataTables.Where(t => t.Rows.Count > 0).ToList();
        if (dataTables.Count > 0) toc.Add(("datentabellen", dataTables.Count == 1 ? "📊 Datentabelle" : "📊 Datentabellen"));
        var audioScripts = studio.AudioScripts.Where(a => a.Segments.Count > 0).ToList();
        if (audioScripts.Count > 0) toc.Add(("audio", audioScripts.Count == 1 ? "🎧 Audio-Skript" : "🎧 Audio-Skripte"));
        if (videoScripts.Count > 0) toc.Add(("video", videoScripts.Count == 1 ? "🎬 Video-Skript" : "🎬 Video-Skripte"));

        if (toc.Count > 1)
        {
            sb.Append("<nav class=\"toc\"><div class=\"toc-title\">Inhalt</div><ol>");
            foreach (var (id, title) in toc)
                sb.Append($"<li><a href=\"#{id}\">{H(title)}</a></li>");
            sb.Append("</ol></nav>\n");
        }

        // Abschnitte
        for (var i = 0; i < report.Sections.Count; i++)
            AppendSection(sb, report.Sections[i], i + 1);

        // Studio-Inhalte
        if (studio.Mindmap != null) AppendMindmap(sb, studio.Mindmap);
        if (studio.Flashcards.Count > 0) AppendFlashcards(sb, studio.Flashcards);
        if (studio.QuizQuestions.Count > 0) AppendQuiz(sb, studio.QuizQuestions);
        if (studio.Presentation is { Slides.Count: > 0 } presentation) AppendPresentation(sb, presentation);
        if (dataTables.Count > 0) AppendDataTables(sb, dataTables);
        if (audioScripts.Count > 0) AppendAudioScripts(sb, audioScripts);
        if (videoScripts.Count > 0) AppendVideoScripts(sb, videoScripts, options);

        sb.Append("<footer class=\"foot\">Erstellt mit StudyScope</footer>\n");
        sb.Append("</main>\n<script>\n").Append(Script);
        if (withPlayers) sb.Append('\n').Append(VideoPlayerHtml.Script);
        sb.Append("\n</script>\n</body>\n</html>\n");
        return sb.ToString();
    }

    private static void AppendSection(StringBuilder sb, ReportSection section, int number)
    {
        sb.Append($"<section class=\"section\" id=\"abschnitt-{number}\">");
        if (!string.IsNullOrWhiteSpace(section.Heading))
            sb.Append($"<h2>{H(section.Heading)}</h2>");

        foreach (var paragraph in section.Paragraphs)
            sb.Append($"<p>{Text(paragraph)}</p>");

        if (section.Bullets.Count > 0)
        {
            sb.Append("<ul class=\"bullets\">");
            foreach (var bullet in section.Bullets)
                sb.Append($"<li>{H(bullet)}</li>");
            sb.Append("</ul>");
        }

        if (section.Terms.Count > 0)
        {
            sb.Append("<div class=\"terms\"><div class=\"label\">Begriffe</div><dl>");
            foreach (var term in section.Terms)
                sb.Append($"<dt>{H(term.Term)}</dt><dd>{Text(term.Definition)}</dd>");
            sb.Append("</dl></div>");
        }

        if (section.Questions.Count > 0)
        {
            sb.Append("<div class=\"faq\">");
            foreach (var q in section.Questions)
                sb.Append($"<details><summary>{H(q.Question)}</summary><div class=\"answer\">{Text(q.Answer)}</div></details>");
            sb.Append("</div>");
        }

        if (!string.IsNullOrWhiteSpace(section.Takeaway))
            sb.Append($"<div class=\"takeaway\"><b>💡 Merksatz:</b> {Text(section.Takeaway)}</div>");

        sb.Append("</section>\n");
    }

    private static void AppendMindmap(StringBuilder sb, MindmapDocument mindmap)
    {
        sb.Append("<section class=\"section studio\" id=\"mindmap\"><h2>🧠 Mindmap</h2>");
        sb.Append("<div class=\"hint\">Klicken Sie auf ein Thema, um es auf- oder zuzuklappen. <button type=\"button\" class=\"link\" data-action=\"mm-all\">Alle aufklappen</button></div>");
        sb.Append("<div class=\"mindmap\">");
        AppendNode(sb, mindmap.Root, 0, 0);
        sb.Append("</div></section>\n");
    }

    private static void AppendNode(StringBuilder sb, MindmapNode node, int depth, int branch)
    {
        var summary = string.IsNullOrWhiteSpace(node.Summary) ? string.Empty : $"<span class=\"mm-sum\">{H(node.Summary)}</span>";
        var css = $"mm-node d{Math.Min(depth, 3)} b{branch % 8}";

        if (node.Children.Count == 0)
        {
            sb.Append($"<div class=\"{css} leaf\"><span class=\"mm-title\">{H(node.Title)}</span>{summary}</div>");
            return;
        }

        sb.Append($"<details class=\"{css}\"{(depth == 0 ? " open" : string.Empty)}><summary><span class=\"mm-title\">{H(node.Title)}</span>");
        sb.Append($"<span class=\"mm-count\">{node.Children.Count}</span>{summary}</summary><div class=\"mm-children\">");
        for (var i = 0; i < node.Children.Count; i++)
            AppendNode(sb, node.Children[i], depth + 1, depth == 0 ? i : branch);
        sb.Append("</div></details>");
    }

    private static void AppendFlashcards(StringBuilder sb, IReadOnlyList<Flashcard> cards)
    {
        sb.Append($"<section class=\"section studio\" id=\"karteikarten\"><h2>🎴 Karteikarten <span class=\"count\">{cards.Count}</span></h2>");
        sb.Append("<div class=\"hint\">Klicken Sie auf eine Karte, um die Antwort zu sehen. <button type=\"button\" class=\"link\" data-action=\"fc-all\">Alle umdrehen</button></div>");
        sb.Append("<div class=\"cards\">");
        foreach (var card in cards)
        {
            sb.Append("<div class=\"card\" tabindex=\"0\" role=\"button\" aria-label=\"Karte umdrehen\"><div class=\"card-inner\">");
            sb.Append($"<div class=\"card-face front\"><div class=\"card-label\">Frage</div><div class=\"card-text\">{Text(card.Question)}</div></div>");
            sb.Append($"<div class=\"card-face back\"><div class=\"card-label\">Antwort</div><div class=\"card-text\">{Text(card.Answer)}</div></div>");
            sb.Append("</div></div>");
        }
        sb.Append("</div></section>\n");
    }

    private static void AppendQuiz(StringBuilder sb, IReadOnlyList<QuizQuestion> questions)
    {
        sb.Append("<section class=\"section studio\" id=\"selbsttest\"><h2>❓ Selbsttest</h2>");
        sb.Append($"<div class=\"hint\">Wählen Sie Ihre Antwort und klicken Sie auf „Prüfen“. <span class=\"score\" data-total=\"{questions.Count}\"></span></div>");

        for (var i = 0; i < questions.Count; i++)
        {
            var q = questions[i];
            var multiple = q.Type == QuizQuestionType.MultipleChoice;
            var inputType = multiple ? "checkbox" : "radio";
            sb.Append($"<div class=\"quiz\" data-correct=\"{string.Join(",", q.CorrectIndices)}\">");
            sb.Append($"<div class=\"quiz-q\"><span class=\"quiz-nr\">{i + 1}</span>{H(q.Text)}<span class=\"quiz-type\">{H(ReportTextExporter.TypeHint(q).Trim(' ', '(', ')'))}</span></div>");
            for (var o = 0; o < q.Options.Count; o++)
            {
                sb.Append($"<label class=\"quiz-opt\" data-index=\"{o}\"><input type=\"{inputType}\" name=\"q{i}\" value=\"{o}\">");
                sb.Append($"<span class=\"quiz-letter\">{ReportTextExporter.Letter(o)}</span><span>{H(q.Options[o])}</span></label>");
            }
            sb.Append("<div class=\"quiz-actions\"><button type=\"button\" class=\"btn\" data-action=\"quiz-check\">Prüfen</button><span class=\"quiz-result\"></span></div>");
            if (!string.IsNullOrWhiteSpace(q.Explanation))
                sb.Append($"<div class=\"quiz-expl\">💡 {Text(q.Explanation)}</div>");
            sb.Append($"<div class=\"quiz-solution\">Lösung: {H(ReportTextExporter.Solution(q))}</div>");
            sb.Append("</div>");
        }

        sb.Append("</section>\n");
    }

    private static void AppendPresentation(StringBuilder sb, PresentationDocument presentation)
    {
        var total = presentation.Slides.Count;
        sb.Append($"<section class=\"section studio\" id=\"praesentation\"><h2>📽️ Präsentation <span class=\"count\">{total} Folien</span></h2>");
        sb.Append("<div class=\"deck\" tabindex=\"0\">");
        for (var i = 0; i < total; i++)
        {
            var slide = presentation.Slides[i];
            var isTitle = slide.Kind == SlideKind.Title;
            sb.Append($"<div class=\"slide{(isTitle ? " title-slide" : string.Empty)}{(i == 0 ? " active" : string.Empty)}\">");
            sb.Append($"<div class=\"slide-title\">{H(slide.Title)}</div><div class=\"slide-accent\"></div>");
            if (isTitle && !string.IsNullOrWhiteSpace(slide.Subtitle))
                sb.Append($"<div class=\"slide-sub\">{H(slide.Subtitle)}</div>");
            if (slide.Bullets.Count > 0)
            {
                sb.Append("<ul>");
                foreach (var bullet in slide.Bullets)
                    sb.Append($"<li>{H(bullet)}</li>");
                sb.Append("</ul>");
            }
            sb.Append($"<div class=\"slide-nr\">{i + 1} / {total}</div>");
            if (!string.IsNullOrWhiteSpace(slide.Notes))
                sb.Append($"<div class=\"slide-notes\"><b>Notizen:</b> {H(slide.Notes)}</div>");
            sb.Append("</div>");
        }
        sb.Append("</div>");
        sb.Append("<div class=\"deck-nav\"><button type=\"button\" class=\"btn light\" data-action=\"slide-prev\">◀ Zurück</button>");
        sb.Append($"<span class=\"deck-pos\">1 / {total}</span>");
        sb.Append("<button type=\"button\" class=\"btn light\" data-action=\"slide-next\">Weiter ▶</button></div>");
        sb.Append("</section>\n");
    }

    /// <summary>
    /// Datentabellen als sortierbare HTML-Tabellen: Die Kopfzeile trägt die Spaltenart (Zahl/Datum/Text), Zellen mit
    /// Zahl oder Datum einen numerischen Sortierschlüssel (data-sort) – dieselbe Erkennung wie in StudyScope.
    /// </summary>
    private static void AppendDataTables(StringBuilder sb, IReadOnlyList<ReportDataTable> tables)
    {
        sb.Append($"<section class=\"section studio\" id=\"datentabellen\"><h2>📊 {(tables.Count == 1 ? "Datentabelle" : "Datentabellen")}</h2>");
        sb.Append("<div class=\"hint\">Klicken Sie auf eine Spaltenüberschrift, um nach dieser Spalte zu sortieren (erneuter Klick: absteigend).</div>");

        for (var t = 0; t < tables.Count; t++)
        {
            var embedded = tables[t];
            var table = embedded.Table;
            var header = DataTableExporter.Header(table);
            var rows = embedded.Rows.Select(r => DataTableExporter.Values(table, r)).ToList();
            var kinds = header.Select((_, c) => c == header.Count - 1
                ? DataTableColumnKind.Text
                : DataTableValues.DetectKind(rows.Select(v => v[c]))).ToList();

            sb.Append($"<div class=\"dtable\" id=\"tabelle-{t + 1}\"><h3>{H(table.Title)}</h3>");
            sb.Append($"<div class=\"meta-small\">{H(ReportTextExporter.DataTableMeta(embedded))}</div>");
            sb.Append("<div class=\"dt-wrap\"><table class=\"dt\"><thead><tr>");
            for (var c = 0; c < header.Count; c++)
            {
                var kind = kinds[c] switch { DataTableColumnKind.Number => "num", DataTableColumnKind.Date => "date", _ => "text" };
                sb.Append($"<th data-kind=\"{kind}\" tabindex=\"0\" role=\"button\" title=\"Sortieren\"{(c == header.Count - 1 ? " class=\"src\"" : string.Empty)}>");
                sb.Append($"{H(header[c])}<span class=\"arrow\"></span></th>");
            }
            sb.Append("</tr></thead><tbody>");

            foreach (var values in rows)
            {
                sb.Append("<tr>");
                for (var c = 0; c < values.Count; c++)
                {
                    var key = DataTableValues.SortKey(values[c], kinds[c]);
                    var sortAttr = key is { } k ? $" data-sort=\"{k.ToString("R", CultureInfo.InvariantCulture)}\"" : string.Empty;
                    sb.Append($"<td{sortAttr}{(c == values.Count - 1 ? " class=\"src\"" : string.Empty)}>{Text(values[c])}</td>");
                }
                sb.Append("</tr>");
            }

            sb.Append("</tbody></table></div></div>");
        }

        sb.Append("</section>\n");
    }

    /// <summary>Sprechskripte als einklappbare Blöcke (die Audiodatei wird nicht eingebettet, nur ihr Name genannt).</summary>
    private static void AppendAudioScripts(StringBuilder sb, IReadOnlyList<AudioSummary> scripts)
    {
        sb.Append($"<section class=\"section studio\" id=\"audio\"><h2>🎧 {(scripts.Count == 1 ? "Audio-Skript" : "Audio-Skripte")}</h2>");
        sb.Append("<div class=\"hint\">Sprechtext der Audio-Beiträge – zum Aufklappen anklicken.</div>");
        foreach (var audio in scripts)
        {
            var speakers = audio.Speakers.Count > 1;
            sb.Append("<details class=\"audio-script\"><summary>");
            sb.Append($"<span class=\"audio-title\">{H(audio.Title)}</span><span class=\"meta-small\">{H(ReportTextExporter.AudioMeta(audio))}</span></summary>");
            sb.Append("<div class=\"audio-body\">");
            foreach (var segment in audio.Segments)
            {
                sb.Append("<p>");
                if (speakers)
                    sb.Append($"<b class=\"audio-speaker\">{H(segment.Speaker)}:</b> ");
                sb.Append(Text(segment.Text)).Append("</p>");
            }
            sb.Append("</div></details>");
        }
        sb.Append("</section>\n");
    }

    /// <summary>
    /// Sprechtexte der Videos als einklappbare Blöcke, je Folie ein Absatz. Mit eingebettetem Player (Häkchen beim
    /// HTML-Export) steht er über dem Skript; in der App gibt es den Knopf „▶ Video abspielen“ (Wechsel in den Reiter „Video“).
    /// </summary>
    private static void AppendVideoScripts(StringBuilder sb, IReadOnlyList<VideoDocument> videos, ReportHtmlOptions options)
    {
        var anyPlayer = videos.Any(v => options.VideoPlayers.ContainsKey(v.Id));
        sb.Append($"<section class=\"section studio\" id=\"video\"><h2>🎬 {(videos.Count == 1 ? (anyPlayer ? "Video" : "Video-Skript") : (anyPlayer ? "Videos" : "Video-Skripte"))}</h2>");
        sb.Append(anyPlayer
            ? "<div class=\"hint\">Mit ▶ abspielen; der Sprechtext je Folie steht darunter zum Aufklappen.</div>"
            : "<div class=\"hint\">Sprechtext der Videos je Folie – zum Aufklappen anklicken.</div>");
        foreach (var video in videos)
        {
            if (options.VideoPlayers.TryGetValue(video.Id, out var player))
            {
                sb.Append($"<div class=\"video-player-wrap\"><div class=\"video-player-title\">{H(video.Title)}</div>");
                VideoPlayerHtml.AppendPlayer(sb, player);
                sb.Append("</div>");
            }

            sb.Append("<details class=\"audio-script video-script\"><summary>");
            sb.Append($"<span class=\"audio-title\">{H(video.Title)}</span><span class=\"meta-small\">{H(ReportTextExporter.VideoMeta(video))}</span>");
            if (options.InApp)
            {
                sb.Append($"<span><button type=\"button\" class=\"video-play\" data-video-id=\"{H(video.Id)}\" title=\"Wechselt in den Reiter „Video“ und spielt dieses Video ab\" ");
                sb.Append("onclick=\"event.preventDefault();event.stopPropagation();window.parent.postMessage({type:'studyscope-play-video',id:this.dataset.videoId},'*');\">▶ Video abspielen</button></span>");
            }
            sb.Append("</summary>");
            sb.Append("<div class=\"audio-body\">");
            for (var i = 0; i < video.Scenes.Count; i++)
            {
                if (!video.Scenes[i].HasNarration) continue;
                sb.Append($"<p><b class=\"video-scene\">{H(ReportVideoScripts.SceneHeading(video, i))}</b><br>{Text(video.Scenes[i].Narration.Trim())}</p>");
            }
            sb.Append("</div></details>");
        }
        sb.Append("</section>\n");
    }

    // ---------------- Hilfen ----------------

    private static string H(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

    /// <summary>Text mit Zeilenumbrüchen.</summary>
    private static string Text(string? text) => H(text).Replace("\r", string.Empty).Replace("\n", "<br>");

    private const string Css = """
:root { --accent: #007bff; --text: #212529; --muted: #6c757d; --line: #dee2e6; --soft: #f8f9fa; }
* { box-sizing: border-box; }
body { margin: 0; background: #f1f3f5; color: var(--text); font: 15px/1.6 "Segoe UI", -apple-system, Roboto, "Helvetica Neue", Arial, sans-serif; }
.report { max-width: 880px; margin: 24px auto; background: #fff; border: 1px solid var(--line); border-radius: 10px; padding: 36px 44px; }
h1 { font-size: 28px; line-height: 1.25; margin: 6px 0 4px; }
h2 { font-size: 20px; margin: 0 0 10px; color: var(--accent); }
.badge { display: inline-block; font-size: 12px; font-weight: 600; background: #e7f1ff; color: #0b5ed7; padding: 2px 10px; border-radius: 12px; }
.meta { color: var(--muted); font-size: 13px; padding-bottom: 14px; border-bottom: 3px solid var(--accent); }
.summary { font-size: 16px; margin-top: 16px; }
.toc { background: var(--soft); border: 1px solid var(--line); border-radius: 8px; padding: 12px 18px; margin: 20px 0; }
.toc-title { font-weight: 700; font-size: 13px; text-transform: uppercase; letter-spacing: .5px; color: var(--muted); }
.toc ol { margin: 6px 0 0; padding-left: 22px; columns: 2; column-gap: 28px; }
.toc a { color: var(--text); text-decoration: none; } .toc a:hover { color: var(--accent); text-decoration: underline; }
.section { margin-top: 30px; scroll-margin-top: 10px; }
.studio { border-top: 1px solid var(--line); padding-top: 24px; }
.bullets li { margin: 3px 0; } .bullets li::marker { color: var(--accent); }
.label { font-size: 12px; font-weight: 700; text-transform: uppercase; letter-spacing: .5px; color: var(--muted); margin-bottom: 6px; }
.terms { margin: 14px 0; }
.terms dl { margin: 0; display: grid; grid-template-columns: minmax(120px, 30%) 1fr; border: 1px solid var(--line); border-radius: 8px; overflow: hidden; }
.terms dt, .terms dd { margin: 0; padding: 8px 12px; border-top: 1px solid var(--line); }
.terms dt { font-weight: 600; background: var(--soft); }
.terms dt:first-of-type, .terms dd:first-of-type { border-top: none; }
.faq details { border: 1px solid var(--line); border-radius: 8px; margin: 8px 0; background: #fff; }
.faq summary { cursor: pointer; padding: 10px 14px; font-weight: 600; list-style: none; }
.faq summary::before { content: "▸"; color: var(--accent); margin-right: 8px; display: inline-block; transition: transform .15s; }
.faq details[open] summary::before { transform: rotate(90deg); }
.faq .answer { padding: 0 14px 12px 32px; }
.takeaway { background: #eaf3ff; border-left: 4px solid var(--accent); border-radius: 6px; padding: 10px 14px; margin: 16px 0; }
.hint { color: var(--muted); font-size: 13px; margin-bottom: 12px; }
.count { font-size: 13px; color: var(--muted); font-weight: 400; margin-left: 6px; }
.link { background: none; border: none; color: var(--accent); cursor: pointer; padding: 0; font: inherit; text-decoration: underline; }
.btn { background: var(--accent); color: #fff; border: none; border-radius: 6px; padding: 6px 14px; font: inherit; font-weight: 600; cursor: pointer; }
.btn.light { background: #fff; color: #495057; border: 1px solid #ced4da; }
.btn:disabled { opacity: .45; cursor: default; }

.mindmap { font-size: 14px; }
.mm-node { margin: 4px 0 4px 0; }
.mm-children { margin-left: 22px; padding-left: 12px; border-left: 2px solid var(--line); }
.mm-node > summary, .mm-node.leaf { cursor: pointer; list-style: none; padding: 5px 10px; border-radius: 8px; border: 1.5px solid var(--line); background: #fff; display: block; }
.mm-node.leaf { cursor: default; }
.mm-node > summary::-webkit-details-marker { display: none; }
.mm-title { font-weight: 600; }
.mm-sum { display: block; color: var(--muted); font-size: 13px; }
.mm-count { float: right; font-size: 11px; color: var(--muted); border: 1px solid var(--line); border-radius: 10px; padding: 0 7px; }
.mm-node[open] > summary .mm-count { background: var(--soft); }
.mm-node.d0 > summary { background: #343a40; color: #fff; border-color: #343a40; } .mm-node.d0 > summary .mm-sum { color: #ced4da; }
.mm-node.d0 > summary .mm-count, .mm-node.d0[open] > summary .mm-count { color: #ced4da; background: transparent; border-color: #6c757d; }
.b0 > summary, .b0.leaf { border-color: #4c6ef5; } .b1 > summary, .b1.leaf { border-color: #12b886; } .b2 > summary, .b2.leaf { border-color: #f76707; } .b3 > summary, .b3.leaf { border-color: #ae3ec9; }
.b4 > summary, .b4.leaf { border-color: #e64980; } .b5 > summary, .b5.leaf { border-color: #2f9e44; } .b6 > summary, .b6.leaf { border-color: #e8a900; } .b7 > summary, .b7.leaf { border-color: #1c7ed6; }
.mm-node.d0.b0 > summary { border-color: #343a40; }

.cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(230px, 1fr)); gap: 12px; }
.card { perspective: 900px; cursor: pointer; min-height: 140px; outline: none; }
.card-inner { position: relative; width: 100%; height: 100%; min-height: 140px; transition: transform .45s; transform-style: preserve-3d; }
.card.flipped .card-inner { transform: rotateY(180deg); }
.card-face { position: absolute; inset: 0; backface-visibility: hidden; -webkit-backface-visibility: hidden; border-radius: 10px; padding: 12px 14px; border: 1.5px solid var(--line); background: #fff; overflow: auto; }
.card-face.back { transform: rotateY(180deg); background: #e9f7ef; border-color: #28a745; }
.card:focus-visible .card-face { box-shadow: 0 0 0 3px #9ec5fe; }
.card-label { font-size: 11px; font-weight: 700; text-transform: uppercase; color: var(--muted); margin-bottom: 4px; }
.card-text { font-size: 14px; }

.quiz { border: 1px solid var(--line); border-radius: 10px; padding: 12px 16px; margin: 12px 0; }
.quiz-q { font-weight: 600; margin-bottom: 8px; }
.quiz-nr { display: inline-block; min-width: 24px; height: 24px; border-radius: 12px; background: var(--accent); color: #fff; text-align: center; font-size: 12px; line-height: 24px; margin-right: 8px; }
.quiz-type { display: block; font-weight: 400; font-size: 12px; color: var(--muted); margin-left: 32px; }
.quiz-opt { display: flex; gap: 10px; align-items: center; padding: 7px 10px; margin: 5px 0; border: 1.5px solid var(--line); border-radius: 8px; cursor: pointer; }
.quiz-opt:hover { border-color: #9ec5fe; }
.quiz-opt input { margin: 0; }
.quiz-letter { font-size: 12px; font-weight: 700; color: var(--muted); width: 14px; }
.quiz-opt.correct { border-color: #28a745; background: #e9f7ef; }
.quiz-opt.wrong { border-color: #dc3545; background: #fdecee; }
.quiz-opt.missed { border-color: #28a745; border-style: dashed; }
.quiz-actions { margin-top: 8px; display: flex; gap: 12px; align-items: center; }
.quiz-result { font-weight: 700; }
.quiz-result.ok { color: #1e7b34; } .quiz-result.no { color: #b02a37; }
.quiz-expl, .quiz-solution { display: none; margin-top: 8px; font-size: 14px; background: var(--soft); border-radius: 6px; padding: 8px 12px; }
.quiz.checked .quiz-expl { display: block; }
.score { font-weight: 700; color: var(--text); }

.deck { position: relative; outline: none; }
.slide { display: none; aspect-ratio: 16 / 9; border: 1px solid var(--line); border-radius: 8px; padding: 5% 6% 0; position: relative; background: #fff; container-type: inline-size; overflow: hidden; }
.slide.active { display: block; }
.slide-title { font-size: 3.6cqw; font-weight: 700; line-height: 1.2; }
.slide-accent { width: 9cqw; height: .45cqw; background: var(--accent); margin: 1.2cqw 0 2.2cqw; }
.slide ul { margin: 0; padding-left: 3cqw; } .slide li { font-size: 2.5cqw; margin-bottom: 1cqw; } .slide li::marker { color: var(--accent); }
.slide.title-slide { background: #343a40; color: #fff; display: none; flex-direction: column; justify-content: center; padding-top: 0; }
.slide.title-slide.active { display: flex; }
.slide.title-slide .slide-title { font-size: 4.8cqw; }
.slide-sub { font-size: 2.2cqw; color: #ced4da; }
.slide-nr { position: absolute; right: 6%; bottom: 3%; font-size: 1.3cqw; color: var(--muted); }
.slide-notes { display: none; }
.deck-nav { display: flex; gap: 12px; justify-content: center; align-items: center; margin-top: 10px; }
.deck-pos { color: var(--muted); font-size: 13px; min-width: 60px; text-align: center; }
.audio-script { border: 1px solid var(--line); border-radius: 8px; margin: 10px 0; background: #fff; }
.audio-script > summary { cursor: pointer; padding: 10px 14px; display: flex; flex-direction: column; gap: 2px; }
.audio-title { font-weight: 600; }
.audio-body { padding: 0 16px 8px; border-top: 1px solid var(--line); }
.audio-body p { margin: 8px 0; }
.audio-speaker { color: var(--accent); }
.video-scene { color: var(--accent); }
.video-play { margin-top: 6px; background: var(--accent); color: #fff; border: none; border-radius: 6px; padding: 5px 12px; font: inherit; font-size: 13px; font-weight: 600; cursor: pointer; }
.video-play:hover { background: #0069d9; }
.video-player-wrap { margin: 12px 0 6px; }
.video-player-title { font-weight: 600; margin-bottom: 6px; }
.dtable { margin: 18px 0 26px; }
.dtable h3 { font-size: 16px; margin: 0 0 2px; }
.meta-small { color: var(--muted); font-size: 12.5px; margin-bottom: 8px; }
.dt-wrap { overflow-x: auto; border: 1px solid var(--line); border-radius: 8px; }
table.dt { border-collapse: collapse; width: 100%; font-size: 13.5px; }
table.dt th { background: #e9f2ff; text-align: left; padding: 7px 10px; border-bottom: 2px solid #9ec5fe; cursor: pointer; user-select: none; white-space: nowrap; }
table.dt th:hover { background: #d8e8ff; }
table.dt th:focus-visible { outline: 2px solid var(--accent); outline-offset: -2px; }
table.dt th .arrow { color: var(--accent); font-size: 11px; margin-left: 4px; }
table.dt td { padding: 6px 10px; border-top: 1px solid var(--line); vertical-align: top; }
table.dt tbody tr:nth-child(even) td { background: var(--soft); }
table.dt .src { color: var(--muted); font-size: 12px; }
.foot { margin-top: 36px; padding-top: 12px; border-top: 1px solid var(--line); color: var(--muted); font-size: 12px; text-align: center; }

@media (max-width: 640px) { .report { margin: 0; border-radius: 0; padding: 20px; } .toc ol { columns: 1; } .terms dl { grid-template-columns: 1fr; } }

/* Druck / PDF: alles aufgeklappt, Lösungen sichtbar, Folien untereinander */
@media print {
  @page { size: A4; margin: 18mm 16mm; }
  body { background: #fff; font-size: 11pt; }
  .report { border: none; margin: 0; padding: 0; max-width: none; }
  .hint, .quiz-actions, .deck-nav, .link, .toc, .video-play, .video-player-wrap { display: none !important; }
  .section { break-inside: auto; }
  h2 { break-after: avoid; }
  .faq details, .quiz, .terms dl, .takeaway { break-inside: avoid; }
  .faq details > *, .mm-node > *, .audio-script > * { display: block; }
  .quiz-solution, .quiz-expl { display: block; }
  .quiz input { display: none; }
  .card { min-height: 0; break-inside: avoid; perspective: none; }
  .card-inner { transform: none !important; min-height: 0; }
  .card-face { position: static; backface-visibility: visible; transform: none !important; border-radius: 6px; }
  .card-face.back { margin-top: 4px; }
  .slide, .slide.title-slide { display: block !important; break-inside: avoid; margin-bottom: 8mm; -webkit-print-color-adjust: exact; print-color-adjust: exact; }
  .slide.title-slide { display: flex !important; }
  .dt-wrap { overflow: visible; border: none; }
  table.dt { font-size: 9.5pt; }
  table.dt thead { display: table-header-group; }
  table.dt tr { break-inside: avoid; }
  table.dt th .arrow { display: none; }
  table.dt th, table.dt td { border: 1px solid #bbb; }
  table.dt th, table.dt tbody tr:nth-child(even) td { -webkit-print-color-adjust: exact; print-color-adjust: exact; }
  .slide-notes { display: block; position: absolute; left: 6%; right: 6%; bottom: 7%; font-size: 1.6cqw; color: var(--muted); }
  .mm-node.d0 > summary, .card-face.back, .takeaway, .terms dt { -webkit-print-color-adjust: exact; print-color-adjust: exact; }
}
""";

    private const string Script = """
(function () {
  // Karteikarten umdrehen (Klick, Enter, Leertaste)
  document.querySelectorAll('.card').forEach(function (card) {
    var flip = function () { card.classList.toggle('flipped'); };
    card.addEventListener('click', flip);
    card.addEventListener('keydown', function (e) { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); flip(); } });
  });

  // Selbsttest auswerten
  var updateScore = function () {
    var score = document.querySelector('.score');
    if (!score) return;
    var checked = document.querySelectorAll('.quiz.checked').length;
    var right = document.querySelectorAll('.quiz.checked.right').length;
    score.textContent = checked ? (right + ' von ' + checked + ' richtig (' + score.dataset.total + ' Fragen)') : '';
  };

  document.addEventListener('click', function (e) {
    var button = e.target.closest('[data-action]');
    if (!button) return;
    var action = button.dataset.action;

    if (action === 'quiz-check') {
      var quiz = button.closest('.quiz');
      var correct = quiz.dataset.correct.split(',').map(Number);
      var options = quiz.querySelectorAll('.quiz-opt');
      var selected = [];
      options.forEach(function (opt) { if (opt.querySelector('input').checked) selected.push(Number(opt.dataset.index)); });
      if (!selected.length) { quiz.querySelector('.quiz-result').textContent = 'Bitte eine Antwort wählen.'; return; }
      var right = selected.length === correct.length && selected.every(function (i) { return correct.indexOf(i) >= 0; });
      options.forEach(function (opt) {
        var i = Number(opt.dataset.index), isSel = selected.indexOf(i) >= 0, isCor = correct.indexOf(i) >= 0;
        opt.classList.remove('correct', 'wrong', 'missed');
        if (isSel && isCor) opt.classList.add('correct'); else if (isSel) opt.classList.add('wrong'); else if (isCor) opt.classList.add('missed');
        opt.querySelector('input').disabled = true;
      });
      var result = quiz.querySelector('.quiz-result');
      result.textContent = right ? '✓ Richtig!' : '✗ Leider falsch.';
      result.className = 'quiz-result ' + (right ? 'ok' : 'no');
      quiz.classList.add('checked');
      if (right) quiz.classList.add('right');
      button.disabled = true;
      updateScore();
    }

    if (action === 'fc-all') {
      var cards = document.querySelectorAll('.card');
      var anyFront = Array.prototype.some.call(cards, function (c) { return !c.classList.contains('flipped'); });
      cards.forEach(function (c) { c.classList.toggle('flipped', anyFront); });
    }

    if (action === 'mm-all') {
      var nodes = document.querySelectorAll('.mindmap details');
      var anyClosed = Array.prototype.some.call(nodes, function (d) { return !d.open; });
      nodes.forEach(function (d) { d.open = anyClosed; });
      button.textContent = anyClosed ? 'Alle zuklappen' : 'Alle aufklappen';
    }

    if (action === 'slide-prev' || action === 'slide-next') {
      moveSlide(action === 'slide-next' ? 1 : -1);
    }
  });

  // Folien blättern (Knöpfe und Pfeiltasten, wenn die Folie fokussiert ist)
  var slides = document.querySelectorAll('.slide');
  var current = 0;
  var moveSlide = function (step) {
    if (!slides.length) return;
    slides[current].classList.remove('active');
    current = Math.max(0, Math.min(slides.length - 1, current + step));
    slides[current].classList.add('active');
    var pos = document.querySelector('.deck-pos');
    if (pos) pos.textContent = (current + 1) + ' / ' + slides.length;
  };
  var deck = document.querySelector('.deck');
  if (deck) {
    deck.addEventListener('click', function () { moveSlide(1); });
    deck.addEventListener('keydown', function (e) {
      if (e.key === 'ArrowRight' || e.key === ' ') { e.preventDefault(); moveSlide(1); }
      if (e.key === 'ArrowLeft') { e.preventDefault(); moveSlide(-1); }
    });
  }

  // Datentabellen sortieren: Klick (oder Enter) auf die Kopfzeile, erneut = absteigend; leere Zellen bleiben unten
  var sortTable = function (th) {
    var table = th.closest('table');
    var index = Array.prototype.indexOf.call(th.parentNode.children, th);
    var desc = th.dataset.dir === 'asc';
    var kind = th.dataset.kind;
    table.querySelectorAll('th').forEach(function (h) { h.dataset.dir = ''; h.querySelector('.arrow').textContent = ''; });
    th.dataset.dir = desc ? 'desc' : 'asc';
    th.querySelector('.arrow').textContent = desc ? '▼' : '▲';
    var body = table.tBodies[0];
    var rows = Array.prototype.slice.call(body.rows).map(function (row, i) { return { row: row, i: i, cell: row.cells[index] }; });
    var text = function (x) { return x.cell.textContent.trim(); };
    rows.sort(function (a, b) {
      var ta = text(a), tb = text(b);
      if (!ta || !tb) return (!ta && !tb) ? a.i - b.i : (!ta ? 1 : -1);
      var r;
      if ((kind === 'num' || kind === 'date') && a.cell.dataset.sort && b.cell.dataset.sort) r = Number(a.cell.dataset.sort) - Number(b.cell.dataset.sort);
      else r = ta.localeCompare(tb, 'de', { sensitivity: 'base', numeric: true });
      if (desc) r = -r;
      return r || a.i - b.i;
    });
    rows.forEach(function (x) { body.appendChild(x.row); });
  };
  document.querySelectorAll('table.dt th').forEach(function (th) {
    th.addEventListener('click', function () { sortTable(th); });
    th.addEventListener('keydown', function (e) { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); sortTable(th); } });
  });

  // Beim Drucken alles aufklappen, danach Zustand wiederherstellen
  var opened = [];
  window.addEventListener('beforeprint', function () {
    opened = [];
    document.querySelectorAll('details').forEach(function (d) { if (!d.open) { d.open = true; opened.push(d); } });
  });
  window.addEventListener('afterprint', function () { opened.forEach(function (d) { d.open = false; }); });
})();
""";
}
