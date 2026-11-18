using System.Globalization;
using System.Text;
using SkiaSharp;

namespace StudyScope.Infrastructure.Services;

/// <summary>
/// Zeichnet Folienbilder mit SkiaSharp (MIT-Lizenz). Wird nur über <see cref="SlideRasterizerLoader"/> erzeugt, damit die
/// native Bibliothek erst beim MP4-Export geladen wird. Schriften: Segoe UI (Windows) bzw. Noto Sans, DejaVu Sans,
/// Liberation Sans (Linux), sonst die Standardschrift. Emojis und andere Zeichen, die die Hauptschrift nicht hat, kommen
/// aus einer Ersatzschrift (<c>SKFontManager.MatchCharacter</c>, z. B. Segoe UI Emoji, Noto Color Emoji); findet sich
/// keine, wird das Zeichen weggelassen – ein fehlendes Symbol ist besser als ein leeres Kästchen im Video. Verbundene
/// Emoji-Folgen (ZWJ, Hautfarben, Variantenwähler) werden in Einzelzeichen zerlegt, weil ohne Text-Shaping keine
/// zusammengesetzten Glyphen entstehen.
/// </summary>
public sealed class SkiaSlideRenderer : ISlideTextMeasurer, IDisposable
{
    private static readonly string[] PreferredFamilies = { "Segoe UI", "Noto Sans", "DejaVu Sans", "Liberation Sans", "Arial", "Helvetica" };

    private readonly SKFontManager _fonts = SKFontManager.Default;
    private readonly SKTypeface _regular;
    private readonly SKTypeface _bold;
    private readonly Dictionary<(int CodePoint, bool Bold), SKTypeface?> _fallbacks = new();
    private readonly Dictionary<string, string> _cleaned = new();

    public string FontFamily { get; }

    public SkiaSlideRenderer()
    {
        (_regular, FontFamily) = Match(SKFontStyle.Normal);
        (_bold, _) = Match(SKFontStyle.Bold);
    }

    private (SKTypeface Typeface, string Family) Match(SKFontStyle style)
    {
        foreach (var family in PreferredFamilies)
        {
            var typeface = _fonts.MatchFamily(family, style);
            if (typeface != null && string.Equals(typeface.FamilyName, family, StringComparison.OrdinalIgnoreCase))
                return (typeface, family);
            typeface?.Dispose();
        }
        var fallback = SKTypeface.FromFamilyName(null, style) ?? SKTypeface.Default;
        return (fallback, fallback.FamilyName);
    }

    // ---------------- Messen ----------------

    public double Ascent(double sizePx, bool bold)
    {
        using var font = Font(bold ? _bold : _regular, sizePx);
        return -font.Metrics.Ascent;
    }

    public double Descent(double sizePx, bool bold)
    {
        using var font = Font(bold ? _bold : _regular, sizePx);
        return font.Metrics.Descent;
    }

    public double Width(string text, double sizePx, bool bold)
    {
        double width = 0;
        foreach (var (run, typeface) in Runs(text, bold))
        {
            using var font = Font(typeface, sizePx);
            width += font.MeasureText(run);
        }
        return width;
    }

    /// <summary>Entfernt Zeichen ohne passende Schrift sowie Steuerzeichen von Emoji-Folgen.</summary>
    public string Clean(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        if (_cleaned.TryGetValue(text, out var cached)) return cached;
        var sb = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            if (IsJoiner(rune.Value)) continue;
            if (rune.Value is '\t' or '\r' or '\n') { sb.Append(' '); continue; }
            if (Rune.IsControl(rune)) continue;
            if (TypefaceFor(rune.Value, bold: false) != null)
                sb.Append(rune.ToString());
        }
        var result = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        _cleaned[text] = result;
        return result;
    }

    /// <summary>ZWJ, Variantenwähler und Hautfarben-Modifikatoren (ohne Shaping nicht kombinierbar).</summary>
    private static bool IsJoiner(int codePoint) =>
        codePoint is 0x200D or 0xFE0E or 0xFE0F || codePoint is >= 0x1F3FB and <= 0x1F3FF;

    private SKTypeface? TypefaceFor(int codePoint, bool bold)
    {
        var primary = bold ? _bold : _regular;
        if (codePoint == ' ' || HasGlyph(primary, codePoint)) return primary;
        if (_fallbacks.TryGetValue((codePoint, bold), out var cached)) return cached;
        var match = _fonts.MatchCharacter(primary.FamilyName, bold ? SKFontStyle.Bold : SKFontStyle.Normal, null, codePoint);
        if (match != null && !HasGlyph(match, codePoint))
        {
            match.Dispose();
            match = null;
        }
        _fallbacks[(codePoint, bold)] = match;
        return match;
    }

    private static bool HasGlyph(SKTypeface typeface, int codePoint)
    {
        using var font = new SKFont(typeface);
        return font.ContainsGlyph(codePoint);
    }

    /// <summary>Text in Abschnitte je Schrift (Hauptschrift bzw. Ersatzschrift); Zeichen ohne Schrift entfallen.</summary>
    private List<(string Run, SKTypeface Typeface)> Runs(string text, bool bold)
    {
        var runs = new List<(string, SKTypeface)>();
        var sb = new StringBuilder();
        SKTypeface? current = null;
        foreach (var rune in (text ?? string.Empty).EnumerateRunes())
        {
            if (IsJoiner(rune.Value)) continue;
            var typeface = TypefaceFor(rune.Value, bold);
            if (typeface == null) continue;
            if (current != null && !ReferenceEquals(typeface, current))
            {
                runs.Add((sb.ToString(), current));
                sb.Clear();
            }
            current = typeface;
            sb.Append(rune.ToString());
        }
        if (current != null && sb.Length > 0) runs.Add((sb.ToString(), current));
        return runs;
    }

    private static SKFont Font(SKTypeface typeface, double size) =>
        new(typeface, (float)size) { Edging = SKFontEdging.Antialias, Subpixel = true, Hinting = SKFontHinting.Slight };

    // ---------------- Zeichnen ----------------

    /// <summary>
    /// Zeichnet ein Bild: aktuelle Folie mit Einblend-Fortschritt je Stichpunkt; während der Überblendung zuerst die
    /// vorige Folie, darüber die neue mit Deckkraft <see cref="VideoFrameState.Blend"/>.
    /// </summary>
    public SKBitmap Render(VideoFrameState state, Func<int, SlideFrameLayout> layoutOf, int width, int height)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);
        if (state.Previous >= 0 && state.Blend < 1)
        {
            DrawSlide(canvas, layoutOf(state.Previous), state.PreviousReveal);
            using var paint = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(255 * Math.Clamp(state.Blend, 0, 1))) };
            canvas.SaveLayer(paint);
            DrawSlide(canvas, layoutOf(state.Scene), state.Reveal);
            canvas.Restore();
        }
        else
        {
            DrawSlide(canvas, layoutOf(state.Scene), state.Reveal);
        }
        canvas.Flush();
        return bitmap;
    }

    public void DrawSlide(SKCanvas canvas, SlideFrameLayout layout, IReadOnlyList<double> reveal)
    {
        using (var background = new SKPaint { Color = new SKColor(layout.Background), IsAntialias = false })
            canvas.DrawRect(0, 0, layout.Width, layout.Height, background);

        foreach (var rect in layout.Rects)
        {
            using var paint = new SKPaint { Color = new SKColor(rect.Color), IsAntialias = true };
            canvas.DrawRect((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height, paint);
        }

        foreach (var line in layout.Lines)
        {
            var progress = line.Bullet >= 0 && line.Bullet < reveal.Count ? Math.Clamp(reveal[line.Bullet], 0, 1) : 1;
            if (progress <= 0) continue;
            var dy = (1 - progress) * layout.RevealOffsetPx;
            var color = new SKColor(line.Color);
            if (progress < 1) color = color.WithAlpha((byte)Math.Round(color.Alpha * progress));
            DrawText(canvas, line.Text, line.X, line.Baseline + dy, line.Size, line.Bold, color, line.AlignRight);
        }
    }

    private void DrawText(SKCanvas canvas, string text, double x, double baseline, double size, bool bold, SKColor color, bool alignRight)
    {
        var runs = Runs(text, bold);
        if (alignRight)
            x -= runs.Sum(r => { using var f = Font(r.Typeface, size); return (double)f.MeasureText(r.Run); });
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        foreach (var (run, typeface) in runs)
        {
            using var font = Font(typeface, size);
            canvas.DrawText(run, (float)x, (float)baseline, SKTextAlign.Left, font, paint);
            x += font.MeasureText(run);
        }
    }

    /// <summary>Speichert als PNG (geringe Kompression – schneller, ffmpeg liest es ohnehin nur einmal).</summary>
    public static void SavePng(SKBitmap bitmap, string path)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    public static string Describe(SkiaSlideRenderer renderer) =>
        string.Create(CultureInfo.InvariantCulture, $"Schrift {renderer.FontFamily}");

    public void Dispose()
    {
        foreach (var typeface in _fallbacks.Values.Where(t => t != null).Distinct())
            if (!ReferenceEquals(typeface, _regular) && !ReferenceEquals(typeface, _bold)) typeface!.Dispose();
        _regular.Dispose();
        _bold.Dispose();
    }
}

/// <summary>
/// Lädt SkiaSharp erst bei Bedarf. Alle Zugriffe auf SkiaSharp-Typen stehen in eigenen, nicht eingebetteten Methoden:
/// Fehlt die native Bibliothek (libSkiaSharp) oder passt sie nicht zum System, entsteht der Fehler erst hier – die App
/// startet normal, nur der MP4-Export ist mit Hinweis gesperrt. <c>STUDYSCOPE_DISABLE_SKIA=1</c> simuliert das (Tests).
/// </summary>
public static class SlideRasterizerLoader
{
    private static readonly object Gate = new();
    private static (bool Ok, string? Error)? _probe;

    /// <summary>Prüft einmalig, ob SkiaSharp lauffähig ist (Ergebnis wird gemerkt).</summary>
    public static (bool Ok, string? Error) Probe()
    {
        lock (Gate)
        {
            if (_probe is { } cached) return cached;
            _probe = ProbeCore();
            return _probe.Value;
        }
    }

    /// <summary>Für Tests: Ergebnis vergessen (z. B. nach Ändern der Umgebungsvariable).</summary>
    public static void Reset()
    {
        lock (Gate) _probe = null;
    }

    private static (bool, string?) ProbeCore()
    {
        if (Environment.GetEnvironmentVariable("STUDYSCOPE_DISABLE_SKIA") == "1")
            return (false, "SkiaSharp ist deaktiviert (STUDYSCOPE_DISABLE_SKIA=1).");
        try
        {
            return TouchNative();
        }
        catch (Exception ex) when (ex is DllNotFoundException or TypeInitializationException or BadImageFormatException
                                       or FileNotFoundException or EntryPointNotFoundException or TypeLoadException)
        {
            return (false, ex.GetBaseException().Message);
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (bool, string?) TouchNative()
    {
        using var bitmap = new SKBitmap(2, 2);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        return (true, null);
    }

    /// <summary>Erzeugt den Zeichner (nur nach erfolgreichem <see cref="Probe"/>).</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static SkiaSlideRenderer Create() => new();
}
