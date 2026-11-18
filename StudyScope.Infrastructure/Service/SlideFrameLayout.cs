using System.Globalization;
using StudyScope.Core.Models;

namespace StudyScope.Infrastructure.Services;

/// <summary>Misst Text für das Folien-Layout (echt: SkiaSharp; Tests: feste Zeichenbreite).</summary>
public interface ISlideTextMeasurer
{
    /// <summary>Breite in Pixeln (nach <see cref="Clean"/>).</summary>
    double Width(string text, double sizePx, bool bold);

    /// <summary>Oberlänge (positiv) und Unterlänge in Pixeln.</summary>
    double Ascent(double sizePx, bool bold);
    double Descent(double sizePx, bool bold);

    /// <summary>Entfernt Zeichen, die keine Schrift darstellen kann (statt leerer Kästchen).</summary>
    string Clean(string text);
}

/// <summary>Eine Textzeile: Grundlinie, Größe, Farbe; <see cref="Bullet"/> = Index des Stichpunkts (−1 = kein Stichpunkt).</summary>
public sealed record SlideTextLine(string Text, double X, double Baseline, double Size, bool Bold, uint Color, int Bullet, bool AlignRight = false);

public sealed record SlideRect(double X, double Y, double Width, double Height, uint Color);

/// <summary>
/// Layout einer Folie in Pixeln (Standard 1920 × 1080), rein rechnerisch nach dem Folien-CSS der App
/// (<c>studyscope-slides.css</c>, Maße in cqw = 1 % der Folienbreite). Schriftgröße der Stichpunkte zuerst wie in der App
/// (<see cref="SlideLayout.GetDensity"/>); passt der Text gemessen nicht über die Fußzeile, wird schrittweise mit den
/// Stufen aus <see cref="PptxWriter.BulletSizeStepsPt"/> verkleinert. Lange Wörter werden innerhalb des Worts umbrochen.
/// </summary>
public sealed class SlideFrameLayout
{
    // Farben wie im CSS (ARGB)
    public const uint ColorText = 0xFF212529;
    public const uint ColorAccent = 0xFF007BFF;
    public const uint ColorMuted = 0xFF6C757D;
    public const uint ColorTitleBackground = 0xFF343A40;
    public const uint ColorWhite = 0xFFFFFFFF;
    public const uint ColorSubtitle = 0xFFCED4DA;

    // Maße aus dem CSS (cqw)
    private const double PadTop = 3.75, PadSide = 5.6;
    private const double H1Size = 3.3, H1LineHeight = 1.2, H1MinHeight = 7.1;
    private const double AccentWidth = 9, AccentHeight = 0.38, AccentTop = 1.1, AccentBottom = 2.2;
    private const double ListIndent = 2.8, BulletOffset = 2.4, LiLineHeight = 1.25;
    private const double FooterBottom = 2.8, FooterSize = 1.15, FooterLineHeight = 1.2;
    private const double TitleH1Size = 4.6, TitleAccentWidth = 11.25, TitleAccentHeight = 0.45, TitleAccentMargin = 1.6;
    private const double SubtitleSize = 2.1, SubtitleLineHeight = 1.35;

    /// <summary>Versatz eines noch nicht eingeblendeten Stichpunkts nach unten (CSS translateY(0.8cqw)).</summary>
    public const double RevealOffsetCqw = 0.8;

    public int Width { get; }
    public int Height { get; }
    public uint Background { get; private init; }
    public IReadOnlyList<SlideRect> Rects { get; private init; } = Array.Empty<SlideRect>();
    public IReadOnlyList<SlideTextLine> Lines { get; private init; } = Array.Empty<SlideTextLine>();
    public int BulletCount { get; private init; }

    /// <summary>Gewählte Schriftgröße der Stichpunkte in cqw (0 bei Titelfolien).</summary>
    public double BulletSizeCqw { get; private init; }

    /// <summary>Die Schrift musste gegenüber der App verkleinert werden, damit alles auf die Folie passt.</summary>
    public bool WasShrunk { get; private init; }

    /// <summary>Unterkante des Inhalts in Pixeln und die erlaubte Grenze (über der Fußzeile).</summary>
    public double ContentBottom { get; private init; }
    public double ContentLimit { get; private init; }

    public double RevealOffsetPx => RevealOffsetCqw * Width / 100.0;

    private SlideFrameLayout(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public static SlideFrameLayout Compute(PresentationSlide slide, int number, int total, string? presentationTitle,
        ISlideTextMeasurer measurer, int width = 1920, int height = 1080)
    {
        return slide.Kind == SlideKind.Title
            ? ComputeTitle(slide, measurer, width, height)
            : ComputeContent(slide, number, total, presentationTitle ?? string.Empty, measurer, width, height);
    }

    /// <summary>Stichpunkt-Größen in cqw: Dichte-Stufe der App, danach kleinere Stufen aus der PowerPoint-Leiter.</summary>
    public static IReadOnlyList<(double Size, double Margin)> BulletSizeLadder(SlideDensity density)
    {
        var (size, margin) = density switch
        {
            SlideDensity.VeryDense => (1.75, 0.7),
            SlideDensity.Dense => (2.15, 0.9),
            _ => (2.6, 1.2)
        };
        var ladder = new List<(double, double)> { (size, margin) };
        foreach (var pt in PptxWriter.BulletSizeStepsPt)
        {
            var cqw = pt / PptxWriter.SlideWidthPt * 100;
            if (cqw < size - 0.01)
                ladder.Add((cqw, margin * cqw / size));
        }
        return ladder;
    }

    private static SlideFrameLayout ComputeContent(PresentationSlide slide, int number, int total, string presentationTitle,
        ISlideTextMeasurer m, int width, int height)
    {
        var u = width / 100.0;
        var left = PadSide * u;
        var contentWidth = width - 2 * PadSide * u;
        var lines = new List<SlideTextLine>();
        var rects = new List<SlideRect>();

        // Überschrift: unten bündig in einem Block von mindestens 7,1 cqw; bei mehr als zwei Zeilen etwas kleiner
        var h1Size = H1Size * u;
        var titleText = m.Clean(slide.Title ?? string.Empty);
        var h1Lines = Wrap(titleText, contentWidth, h1Size, true, m);
        while (h1Lines.Count > 2 && h1Size > 2.2 * u)
        {
            h1Size *= 0.9;
            h1Lines = Wrap(titleText, contentWidth, h1Size, true, m);
        }
        var h1LineHeight = H1LineHeight * h1Size;
        var h1Block = Math.Max(H1MinHeight * u, h1Lines.Count * h1LineHeight);
        var h1Top = PadTop * u + h1Block - h1Lines.Count * h1LineHeight;
        for (var i = 0; i < h1Lines.Count; i++)
            lines.Add(new SlideTextLine(h1Lines[i], left, Baseline(h1Top + i * h1LineHeight, h1LineHeight, h1Size, true, m), h1Size, true, ColorText, -1));

        var accentY = PadTop * u + h1Block + AccentTop * u;
        rects.Add(new SlideRect(left, accentY, AccentWidth * u, AccentHeight * u, ColorAccent));
        var listTop = accentY + AccentHeight * u + AccentBottom * u;

        // Fußzeile: Titel links (gekürzt), Nummer rechts
        var footerSize = FooterSize * u;
        var footerLineHeight = FooterLineHeight * footerSize;
        var footerTop = height - FooterBottom * u - footerLineHeight;
        var footerBaseline = Baseline(footerTop, footerLineHeight, footerSize, false, m);
        var numberText = $"{number} / {total}";
        var numberWidth = m.Width(numberText, footerSize, false);
        var footerTitle = Ellipsize(m.Clean(presentationTitle), contentWidth - numberWidth - 2 * u, footerSize, false, m);
        lines.Add(new SlideTextLine(footerTitle, left, footerBaseline, footerSize, false, ColorMuted, -1));
        lines.Add(new SlideTextLine(numberText, width - PadSide * u, footerBaseline, footerSize, false, ColorMuted, -1, AlignRight: true));
        var limit = footerTop - 1.0 * u;

        // Stichpunkte: erste Stufe, die passt; sonst die kleinste
        var bullets = (slide.Bullets ?? new List<string>()).Select(b => m.Clean(b)).ToList();
        var ladder = BulletSizeLadder(SlideLayout.GetDensity(slide));
        List<SlideTextLine> bulletLines = new();
        double chosen = ladder[0].Size, bottom = listTop;
        for (var step = 0; step < ladder.Count; step++)
        {
            (chosen, var margin) = ladder[step];
            (bulletLines, bottom) = LayoutBullets(bullets, chosen * u, margin * u, left + ListIndent * u, contentWidth - ListIndent * u,
                BulletOffset * u, listTop, m);
            if (bottom <= limit) break;
        }
        lines.AddRange(bulletLines);

        return new SlideFrameLayout(width, height)
        {
            Background = ColorWhite,
            Rects = rects,
            Lines = lines,
            BulletCount = bullets.Count,
            BulletSizeCqw = chosen,
            WasShrunk = chosen < ladder[0].Size - 0.001,
            ContentBottom = bottom,
            ContentLimit = limit
        };
    }

    private static (List<SlideTextLine> Lines, double Bottom) LayoutBullets(IReadOnlyList<string> bullets, double size, double margin,
        double textLeft, double textWidth, double bulletOffset, double top, ISlideTextMeasurer m)
    {
        var result = new List<SlideTextLine>();
        var lineHeight = LiLineHeight * size;
        var y = top;
        var bottom = top;
        for (var b = 0; b < bullets.Count; b++)
        {
            var wrapped = Wrap(bullets[b], textWidth, size, false, m);
            if (wrapped.Count == 0) wrapped.Add(string.Empty);
            var firstBaseline = Baseline(y, lineHeight, size, false, m);
            result.Add(new SlideTextLine("•", textLeft - bulletOffset, firstBaseline, size, false, ColorAccent, b));
            for (var i = 0; i < wrapped.Count; i++)
                result.Add(new SlideTextLine(wrapped[i], textLeft, Baseline(y + i * lineHeight, lineHeight, size, false, m), size, false, ColorText, b));
            bottom = y + wrapped.Count * lineHeight;
            y = bottom + margin;
        }
        return (result, bottom);
    }

    private static SlideFrameLayout ComputeTitle(PresentationSlide slide, ISlideTextMeasurer m, int width, int height)
    {
        var u = width / 100.0;
        var left = PadSide * u;
        var contentWidth = width - 2 * PadSide * u;
        var h1Size = TitleH1Size * u;
        var titleText = m.Clean(slide.Title ?? string.Empty);
        var h1Lines = Wrap(titleText, contentWidth, h1Size, true, m);
        while (h1Lines.Count > 3 && h1Size > 2.6 * u)
        {
            h1Size *= 0.9;
            h1Lines = Wrap(titleText, contentWidth, h1Size, true, m);
        }
        var subSize = SubtitleSize * u;
        var subText = m.Clean(slide.Subtitle ?? string.Empty);
        var subLines = string.IsNullOrWhiteSpace(subText) ? new List<string>() : Wrap(subText, contentWidth, subSize, false, m);

        var h1LineHeight = H1LineHeight * h1Size;
        var subLineHeight = SubtitleLineHeight * subSize;
        var total = h1Lines.Count * h1LineHeight + (2 * TitleAccentMargin + TitleAccentHeight) * u + subLines.Count * subLineHeight;
        var y = (height - total) / 2;

        var lines = new List<SlideTextLine>();
        for (var i = 0; i < h1Lines.Count; i++)
            lines.Add(new SlideTextLine(h1Lines[i], left, Baseline(y + i * h1LineHeight, h1LineHeight, h1Size, true, m), h1Size, true, ColorWhite, -1));
        y += h1Lines.Count * h1LineHeight + TitleAccentMargin * u;
        var rects = new List<SlideRect> { new(left, y, TitleAccentWidth * u, TitleAccentHeight * u, ColorAccent) };
        y += (TitleAccentHeight + TitleAccentMargin) * u;
        for (var i = 0; i < subLines.Count; i++)
            lines.Add(new SlideTextLine(subLines[i], left, Baseline(y + i * subLineHeight, subLineHeight, subSize, false, m), subSize, false, ColorSubtitle, -1));

        return new SlideFrameLayout(width, height)
        {
            Background = ColorTitleBackground,
            Rects = rects,
            Lines = lines,
            ContentBottom = y + subLines.Count * subLineHeight,
            ContentLimit = height
        };
    }

    /// <summary>Grundlinie in einer CSS-Zeilenbox: der Zeilenabstand verteilt sich gleichmäßig über und unter der Schrift.</summary>
    private static double Baseline(double lineTop, double lineHeight, double size, bool bold, ISlideTextMeasurer m)
    {
        var ascent = m.Ascent(size, bold);
        var descent = m.Descent(size, bold);
        return lineTop + (lineHeight - (ascent + descent)) / 2 + ascent;
    }

    /// <summary>Zeilenumbruch an Leerzeichen; Wörter, die allein nicht passen, werden zeichenweise (nach Graphemen) geteilt.</summary>
    public static List<string> Wrap(string text, double maxWidth, double size, bool bold, ISlideTextMeasurer m)
    {
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (m.Width(candidate, size, bold) <= maxWidth)
            {
                current = candidate;
                continue;
            }
            if (current.Length > 0)
            {
                lines.Add(current);
                current = string.Empty;
            }
            if (m.Width(word, size, bold) <= maxWidth)
            {
                current = word;
                continue;
            }
            // Überlanges Wort: so viele Zeichen wie passen, Rest in die nächste Zeile
            var piece = string.Empty;
            var elements = StringInfo.GetTextElementEnumerator(word);
            while (elements.MoveNext())
            {
                var element = (string)elements.Current;
                if (piece.Length > 0 && m.Width(piece + element, size, bold) > maxWidth)
                {
                    lines.Add(piece);
                    piece = string.Empty;
                }
                piece += element;
            }
            current = piece;
        }
        if (current.Length > 0) lines.Add(current);
        return lines;
    }

    private static string Ellipsize(string text, double maxWidth, double size, bool bold, ISlideTextMeasurer m)
    {
        if (m.Width(text, size, bold) <= maxWidth) return text;
        var info = new StringInfo(text);
        for (var n = info.LengthInTextElements - 1; n > 0; n--)
        {
            var candidate = info.SubstringByTextElements(0, n).TrimEnd() + "…";
            if (m.Width(candidate, size, bold) <= maxWidth) return candidate;
        }
        return string.Empty;
    }
}
