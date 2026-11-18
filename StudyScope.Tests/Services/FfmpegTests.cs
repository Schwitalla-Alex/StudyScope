using System.Diagnostics;
using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.Services;

namespace StudyScope.Tests.Services;

/// <summary>
/// ffmpeg ohne echtes ffmpeg: Argumente (Liste statt String, Pfade mit Leerzeichen/Umlauten), concat-Liste,
/// Fortschritt, Version/Encoder, Suche (PATH, WinGet, eigener Pfad), Status bei fehlendem ffmpeg, Export-Einstellungen.
/// </summary>
[TestFixture]
public class FfmpegTests
{
    private const string OddDir = @"C:\Users\Test Nutzer\Übungen & Videos";

    // ---------- Argumente ----------

    [Test]
    public void Mp4Arguments_WithAudio_PathsAreSingleArguments_H264Aac_ExactDuration()
    {
        var list = Path.Combine(OddDir, "frames.ffconcat");
        var audio = Path.Combine(OddDir, "Tonspur ä.wav");
        var output = Path.Combine(OddDir, ".Zellen erklärt.mp4.part");

        var args = FfmpegArguments.Mp4(list, audio, output, 19.04);

        Assert.That(args, Does.Contain(list).And.Contain(audio));
        Assert.That(args[^1], Is.EqualTo(output), "Ausgabe zuletzt");
        Assert.That(args, Does.Not.Contain("\"").And.None.Contains("\""), "kein Quoting nötig");
        Assert.That(string.Join(" ", args), Does.Contain("-f concat -safe 0 -i").And.Contain("-map 0:v:0 -map 1:a:0")
            .And.Contain("-c:v libx264").And.Contain("format=yuv420p").And.Contain("-c:a aac").And.Contain("-t 19.04")
            .And.Contain("-movflags +faststart -f mp4").And.Contain("-nostdin").And.Contain("-progress pipe:1"));
        Assert.That(args.IndexOf("-i"), Is.LessThan(args.IndexOf(audio)));
    }

    [Test]
    public void Mp4Arguments_WithoutAudio_NoAudioMap()
    {
        var args = FfmpegArguments.Mp4("a.ffconcat", null, "out.mp4", 12);

        Assert.That(args, Does.Contain("-an").And.Not.Contain("1:a:0").And.Not.Contain("aac"));
        Assert.That(args.Count(a => a == "-i"), Is.EqualTo(1));
    }

    [Test]
    public void Mp3Arguments_LameMono64k_ExplicitFormat()
    {
        var args = FfmpegArguments.Mp3(Path.Combine(OddDir, "in put.wav"), Path.Combine(OddDir, "aus.mp3.part"));

        Assert.That(string.Join(" ", args), Does.Contain("-codec:a libmp3lame -b:a 64k").And.Contain("-ac 1").And.Contain("-f mp3"));
        Assert.That(args, Does.Contain(Path.Combine(OddDir, "in put.wav")));
    }

    [Test]
    [SetCulture("de-DE")]
    public void ConcatList_InvariantNumbers_LastFrameRepeated_QuotesEscaped()
    {
        var text = FfmpegArguments.ConcatList(new[] { ("f00000.png", 2.5), ("f00001.png", 0.04), ("it's.png", 1.0 / 3) });

        Assert.That(text, Does.StartWith("ffconcat version 1.0\n"));
        Assert.That(text, Does.Contain("duration 2.5\n").And.Contain("duration 0.04\n").And.Contain("duration 0.333333\n"));
        Assert.That(text, Does.Not.Contain(","), "kein Dezimalkomma");
        Assert.That(text, Does.Contain("file 'it'\\''s.png'"));
        Assert.That(text.TrimEnd().Split('\n')[^1], Is.EqualTo("file 'it'\\''s.png'"), "letztes Bild ohne Dauer wiederholt");
    }

    [TestCase("out_time_us=1500000", 1.5)]
    [TestCase("out_time_ms=2500000", 2.5)]
    [TestCase("out_time=00:01:02.500000", 62.5)]
    [TestCase("out_time_us=N/A", null)]
    [TestCase("frame=12", null)]
    [TestCase("", null)]
    public void ParseProgress(string line, double? expected)
    {
        Assert.That(FfmpegArguments.ParseProgressSeconds(line), Is.EqualTo(expected));
    }

    [Test]
    public void ParseVersion_And_Encoders()
    {
        Assert.That(FfmpegArguments.ParseVersion("ffmpeg version 9.0.2-full_build-www.gyan.dev Copyright (c) 2000-2026"), Is.EqualTo("9.0.2-full_build-www.gyan.dev"));
        Assert.That(FfmpegArguments.ParseVersion("ffmpeg version n7.1 Copyright"), Is.EqualTo("n7.1"));
        Assert.That(FfmpegArguments.ParseVersion("irgendein Programm"), Is.Null);

        const string encoders = """
            Encoders:
             V..... = Video
             ------
             V....D libx264              libx264 H.264 / AVC
             V....D libx264rgb           libx264 H.264 RGB
             A....D aac                  AAC (Advanced Audio Coding)
             A....D aac_mf               AAC via MediaFoundation
            """;
        Assert.That(FfmpegArguments.HasEncoder(encoders, "libx264"), Is.True);
        Assert.That(FfmpegArguments.HasEncoder(encoders, "aac"), Is.True);
        Assert.That(FfmpegArguments.HasEncoder(encoders, "libmp3lame"), Is.False);
        Assert.That(FfmpegArguments.HasEncoder("A....D aac_mf  AAC", "aac"), Is.False, "nur ganzer Name");
    }

    // ---------- Suche ----------

    private static FfmpegLocator Locator(HashSet<string> files, HashSet<string> dirs, Dictionary<string, string> env, bool windows,
        params string[] packages) =>
        new(files.Contains, dirs.Contains, name => env.TryGetValue(name, out var v) ? v : null, _ => packages, windows);

    [Test]
    [Platform("Win")]
    public void Locator_Windows_CustomFirst_ThenPath_ThenWinGet()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"D:\Werkzeuge\ffmpeg\bin\ffmpeg.exe",
            @"C:\Tools\ffmpeg.exe",
            @"C:\Users\Test\AppData\Local\Microsoft\WinGet\Links\ffmpeg.exe",
            @"C:\Users\Test\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_x\ffmpeg-9.0-full_build\bin\ffmpeg.exe"
        };
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"D:\Werkzeuge\ffmpeg" };
        var env = new Dictionary<string, string> { ["PATH"] = @"C:\Windows;""C:\Tools"";C:\Leer", ["LOCALAPPDATA"] = @"C:\Users\Test\AppData\Local" };
        var locator = Locator(files, dirs, env, true, @"C:\Users\Test\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_x\ffmpeg-9.0-full_build\bin\ffmpeg.exe");

        var candidates = locator.Candidates(@"D:\Werkzeuge\ffmpeg");

        Assert.That(candidates, Is.EqualTo(new[]
        {
            @"D:\Werkzeuge\ffmpeg\bin\ffmpeg.exe",
            @"C:\Tools\ffmpeg.exe",
            @"C:\Users\Test\AppData\Local\Microsoft\WinGet\Links\ffmpeg.exe",
            @"C:\Users\Test\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_x\ffmpeg-9.0-full_build\bin\ffmpeg.exe"
        }));
        Assert.That(locator.Candidates(null), Has.Count.EqualTo(3), "ohne eigenen Pfad");
    }

    [Test]
    public void Locator_Linux_PathAndUsrBin_NoDuplicates()
    {
        var files = new HashSet<string> { "/usr/bin/ffmpeg", "/snap/bin/ffmpeg" };
        var env = new Dictionary<string, string> { ["PATH"] = "/usr/local/sbin:/usr/bin:/bin" };
        var locator = Locator(files, new HashSet<string>(), env, false);

        Assert.That(locator.ExecutableName, Is.EqualTo("ffmpeg"));
        var candidates = locator.Candidates(null).Select(c => c.Replace('\\', '/')).ToList();
        Assert.That(candidates, Is.EqualTo(new[] { "/usr/bin/ffmpeg", "/snap/bin/ffmpeg" }));
    }

    [Test]
    public async Task Service_NothingFound_NotAvailable_WithInstallHint()
    {
        var locator = Locator(new HashSet<string>(), new HashSet<string>(), new Dictionary<string, string>(), OperatingSystem.IsWindows());
        var storage = new JsonStorageService(Path.Combine(Path.GetTempPath(), "StudyScopeFfmpeg_" + Guid.NewGuid().ToString("N")));
        var service = new FfmpegService(storage, locator);

        var status = await service.GetStatusAsync();
        var custom = await service.TestAsync(@"X:\gibt es nicht\ffmpeg.exe");

        Assert.That(status.IsAvailable, Is.False);
        Assert.That(status.Message, Does.Contain("nicht gefunden"));
        Assert.That(status.Hint, Does.Contain(OperatingSystem.IsLinux() ? FfmpegInfo.LinuxInstall : FfmpegInfo.WindowsInstall));
        Assert.That(custom.Message, Does.Contain("gibt es nicht"));
        Assert.That(status.CanEncodeMp4, Is.False);
        var ex = Assert.ThrowsAsync<FfmpegException>(() => service.RunAsync(new[] { "-version" }, 1));
        Assert.That(ex!.Message, Does.Contain("nicht gefunden"));
    }

    // ---------- Export-Einstellungen in config.json ----------

    [Test]
    public async Task ExportSettings_SavedGlobally_KeptBySavingAiConfig()
    {
        var dir = Path.Combine(Path.GetTempPath(), "StudyScopeExportSettings_" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new JsonStorageService(dir);
            Assert.That((await storage.LoadExportSettingsAsync()).FfmpegPath, Is.Empty, "Standard");

            await storage.SaveAiConfigAsync(new AiConfig { SelectedProvider = AiProviderType.OllamaLocal });
            await storage.SaveExportSettingsAsync(new ExportSettings { FfmpegPath = OddDir });
            await storage.SaveAiConfigAsync(new AiConfig { SelectedProvider = AiProviderType.LmStudioLocal });

            Assert.That((await storage.LoadExportSettingsAsync()).FfmpegPath, Is.EqualTo(OddDir));
            Assert.That((await storage.LoadAiConfigAsync()).SelectedProvider, Is.EqualTo(AiProviderType.LmStudioLocal));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}

/// <summary>
/// Folienbilder und Bildplan des MP4-Exports ohne ffmpeg: Layout nach dem Folien-CSS (cqw → Pixel), Umbruch langer
/// Wörter, Verkleinern voller Folien, Zeitpunkte der Stichpunkte wie in der App, Überblendung, Zusammenfassen gleicher
/// Bilder, echter Zeichner (SkiaSharp) und der Exporter mit einem ffmpeg-Ersatz (Aufräumen, Abbruch).
/// </summary>
[TestFixture]
public class VideoMp4Tests
{
    /// <summary>Feste Zeichenbreite: 0,5 × Schriftgröße je Zeichen, Ober-/Unterlänge 0,8/0,2.</summary>
    private sealed class FixedMeasurer : ISlideTextMeasurer
    {
        public double Width(string text, double sizePx, bool bold) => (text ?? string.Empty).Length * 0.5 * sizePx;
        public double Ascent(double sizePx, bool bold) => 0.8 * sizePx;
        public double Descent(double sizePx, bool bold) => 0.2 * sizePx;
        public string Clean(string text) => text ?? string.Empty;
    }

    private static readonly FixedMeasurer Fixed = new();
    private const double U = 19.2; // 1 cqw bei 1920 px

    [Test]
    public void Layout_ContentSlide_PixelsFromCqw()
    {
        var slide = new PresentationSlide { Title = "Aufbau", Bullets = { "Zellkern", "Membran" } };

        var layout = SlideFrameLayout.Compute(slide, 2, 5, "Zellbiologie", Fixed);

        Assert.That(layout.Background, Is.EqualTo(SlideFrameLayout.ColorWhite));
        var accent = layout.Rects.Single();
        Assert.That(accent.X, Is.EqualTo(5.6 * U).Within(0.01));
        Assert.That(accent.Width, Is.EqualTo(9 * U).Within(0.01));
        Assert.That(accent.Y, Is.EqualTo((3.75 + 7.1 + 1.1) * U).Within(0.01), "Überschriftenblock mindestens 7,1 cqw");
        Assert.That(accent.Color, Is.EqualTo(SlideFrameLayout.ColorAccent));

        var dots = layout.Lines.Where(l => l.Text == "•").ToList();
        Assert.That(dots, Has.Count.EqualTo(2));
        Assert.That(dots[0].X, Is.EqualTo((5.6 + 2.8 - 2.4) * U).Within(0.01));
        var first = layout.Lines.Single(l => l.Text == "Zellkern");
        Assert.That(first.X, Is.EqualTo((5.6 + 2.8) * U).Within(0.01));
        Assert.That(first.Size, Is.EqualTo(2.6 * U).Within(0.01), "Schriftgröße wie in der App");
        Assert.That(first.Bullet, Is.EqualTo(0));
        var second = layout.Lines.Single(l => l.Text == "Membran");
        Assert.That(second.Baseline - first.Baseline, Is.EqualTo((1.25 * 2.6 + 1.2) * U).Within(0.01), "Zeilenhöhe + Abstand");

        var number = layout.Lines.Single(l => l.Text == "2 / 5");
        Assert.That(number.AlignRight, Is.True);
        Assert.That(number.X, Is.EqualTo(1920 - 5.6 * U).Within(0.01));
        Assert.That(layout.Lines.Single(l => l.Text == "Zellbiologie").Color, Is.EqualTo(SlideFrameLayout.ColorMuted));
        Assert.That(layout.WasShrunk, Is.False);
        Assert.That(layout.RevealOffsetPx, Is.EqualTo(0.8 * U).Within(0.01));
    }

    [Test]
    public void Layout_LongWord_BrokenInsideWord_EveryLineFits()
    {
        var word = new string('x', 120);
        var slide = new PresentationSlide { Title = "Wort", Bullets = { $"Ein {word} Ende" } };

        var layout = SlideFrameLayout.Compute(slide, 1, 1, "", Fixed);

        var lines = layout.Lines.Where(l => l.Bullet == 0 && l.Text != "•").ToList();
        Assert.That(lines.Count, Is.GreaterThanOrEqualTo(3));
        var maxWidth = 1920 - 2 * 5.6 * U - 2.8 * U;
        Assert.That(lines.All(l => Fixed.Width(l.Text, l.Size, false) <= maxWidth + 0.01), Is.True);
        Assert.That(string.Concat(lines.Select(l => l.Text.Replace(" ", ""))), Is.EqualTo($"Ein{word}Ende"), "nichts geht verloren");
    }

    [Test]
    public void Layout_FullSlide_ShrinksWithPptxLadder_UntilItFits()
    {
        var slide = new PresentationSlide { Title = "Voll", Bullets = Enumerable.Range(0, 15).Select(i => $"Stichpunkt {i} " + string.Join(" ", Enumerable.Repeat("Wort", 12))).ToList() };

        var layout = SlideFrameLayout.Compute(slide, 1, 1, "", Fixed);

        Assert.That(SlideLayout.GetDensity(slide), Is.EqualTo(SlideDensity.VeryDense));
        Assert.That(layout.WasShrunk, Is.True);
        Assert.That(layout.ContentBottom, Is.LessThanOrEqualTo(layout.ContentLimit));
        var ladder = SlideFrameLayout.BulletSizeLadder(SlideDensity.VeryDense).Select(s => s.Size).ToList();
        Assert.That(ladder, Does.Contain(layout.BulletSizeCqw));

        // Passt auch die kleinste Stufe nicht, bleibt es bei ihr (wie in der App läuft der Text dann bis an die Fußzeile)
        var huge = new PresentationSlide { Title = "Zu voll", Bullets = Enumerable.Range(0, 30).Select(i => string.Join(" ", Enumerable.Repeat("Wort", 40))).ToList() };
        var hugeLayout = SlideFrameLayout.Compute(huge, 1, 1, "", Fixed);
        Assert.That(hugeLayout.BulletSizeCqw, Is.EqualTo(ladder[^1]));
    }

    [Test]
    public void BulletLadder_StartsWithAppSize_ThenSmallerPptxSteps()
    {
        var normal = SlideFrameLayout.BulletSizeLadder(SlideDensity.Normal).Select(s => Math.Round(s.Size, 3)).ToList();
        var dense = SlideFrameLayout.BulletSizeLadder(SlideDensity.Dense).Select(s => Math.Round(s.Size, 3)).ToList();

        Assert.That(normal[0], Is.EqualTo(2.6));
        Assert.That(normal[1], Is.EqualTo(Math.Round(24 / PptxWriter.SlideWidthPt * 100, 3)), "24 pt");
        Assert.That(normal, Is.Ordered.Descending);
        Assert.That(dense[0], Is.EqualTo(2.15));
        Assert.That(dense.Skip(1).All(s => s < 2.15), Is.True);
        Assert.That(PptxWriter.FitBulletSize(new[] { "kurz" }), Is.EqualTo(PptxWriter.BulletSizeStepsPt[0] * 100), "PowerPoint unverändert");
    }

    [Test]
    public void Layout_TitleSlide_DarkCenteredNoFooter()
    {
        var slide = new PresentationSlide { Kind = SlideKind.Title, Title = "Zellbiologie", Subtitle = "Grundlagen" };

        var layout = SlideFrameLayout.Compute(slide, 1, 3, "Zellbiologie", Fixed);

        Assert.That(layout.Background, Is.EqualTo(SlideFrameLayout.ColorTitleBackground));
        Assert.That(layout.Lines.Select(l => l.Text), Is.EqualTo(new[] { "Zellbiologie", "Grundlagen" }), "keine Fußzeile");
        Assert.That(layout.Lines[0].Size, Is.EqualTo(4.6 * U).Within(0.01));
        Assert.That(layout.Lines[1].Color, Is.EqualTo(SlideFrameLayout.ColorSubtitle));
        var top = layout.Lines[0].Baseline - 0.8 * 4.6 * U;
        Assert.That(top + (layout.ContentBottom - top) / 2, Is.EqualTo(540).Within(30), "senkrecht mittig");
        Assert.That(layout.BulletCount, Is.Zero);
    }

    // ---------- Bildplan ----------

    [Test]
    public void BulletRevealStart_MatchesVisibleBulletsOfTheApp()
    {
        foreach (var (count, duration) in new[] { (1, 3.0), (3, 6.0), (5, 12.5), (4, 1.0) })
        {
            for (var step = 0; step * 0.01 < duration; step++)
            {
                var elapsed = step * 0.01 + 0.0005; // nicht genau auf eine Grenze fallen (Gleitkomma)
                var visible = VideoTimeline.VisibleBullets(count, elapsed, duration);
                var started = Enumerable.Range(0, count).Count(k => VideoFramePlan.BulletRevealStart(k, count, duration) <= elapsed + 1e-9);
                Assert.That(started, Is.EqualTo(visible), $"n={count}, d={duration}, t={elapsed:0.00}");
            }
        }
    }

    [Test]
    public void CssEase_KnownValues()
    {
        Assert.That(VideoFramePlan.CssEase(-1), Is.Zero);
        Assert.That(VideoFramePlan.CssEase(0), Is.Zero);
        Assert.That(VideoFramePlan.CssEase(1), Is.EqualTo(1));
        Assert.That(VideoFramePlan.CssEase(0.5), Is.EqualTo(0.8024).Within(0.001), "ease bei 50 %");
        var values = Enumerable.Range(0, 101).Select(i => VideoFramePlan.CssEase(i / 100.0)).ToList();
        Assert.That(values, Is.Ordered.Ascending);
    }

    private static VideoTimeline Timeline() => VideoTimeline.FromStarts(new[] { 0.0, 4.0, 9.0 }, 13.0, 3);

    [Test]
    public void Plan_ExactFrameCount_SceneStartsOnExactFrames_StillsMerged()
    {
        var plan = VideoFramePlan.Build(Timeline(), new[] { 0, 3, 2 });

        Assert.That(plan.Sum(s => s.FrameCount), Is.EqualTo(13 * 25));
        Assert.That(plan[0].StartFrame, Is.Zero);
        for (var i = 1; i < plan.Count; i++)
            Assert.That(plan[i].StartFrame, Is.EqualTo(plan[i - 1].StartFrame + plan[i - 1].FrameCount), "lückenlos");
        Assert.That(plan.First(s => s.State.Scene == 1).StartFrame, Is.EqualTo(100), "Folie 2 genau bei 4,00 s");
        Assert.That(plan.First(s => s.State.Scene == 2).StartFrame, Is.EqualTo(225), "Folie 3 genau bei 9,00 s");
        Assert.That(plan.Count, Is.LessThan(13 * 25 / 2), "Standbilder zusammengefasst");
        Assert.That(plan.Last().State.Reveal, Is.All.EqualTo(1), "am Ende alle Stichpunkte sichtbar");
    }

    [Test]
    public void Plan_CrossfadeFromPreviousSlide_ForSixTenthsOfASecond()
    {
        var plan = VideoFramePlan.Build(Timeline(), new[] { 0, 3, 2 });

        var fade = plan.Where(s => s.State.Scene == 1 && s.State.Previous == 0).ToList();
        Assert.That(fade, Is.Not.Empty);
        Assert.That(fade.First().StartSeconds, Is.EqualTo(4.0).Within(1e-9));
        Assert.That(fade.Last().StartSeconds, Is.LessThan(4.0 + VideoFramePlan.TransitionSeconds));
        Assert.That(fade.Select(s => s.State.Blend), Is.Ordered.Ascending);
        Assert.That(plan.Where(s => s.State.Scene == 1 && s.StartSeconds >= 4.6).All(s => s.State.Previous == -1), Is.True);

        var state = VideoFramePlan.StateAt(4.0 + 0.4 + 0.3, Timeline(), new[] { 0, 3, 2 });
        Assert.That(state.Reveal[0], Is.GreaterThan(0).And.LessThan(1), "erster Stichpunkt blendet gerade ein");
        Assert.That(state.Reveal[1], Is.Zero);
    }

    [Test]
    public void Plan_SilentVideoWithoutBullets_OnlyStillsAndFades()
    {
        var timeline = VideoTimeline.FromStarts(new[] { 0.0, 3.0 }, 6.0, 2);

        var plan = VideoFramePlan.Build(timeline, new[] { 0, 0 });

        Assert.That(plan.Count, Is.EqualTo(1 + 15 + 1).Within(1), "Standbild, 0,6 s Überblendung bei 25 Bildern/s, Standbild");
    }

    // ---------- Zeichner (SkiaSharp) ----------

    private static void RequireSkia()
    {
        var (ok, error) = SlideRasterizerLoader.Probe();
        if (!ok) Assert.Ignore($"SkiaSharp ist hier nicht lauffähig: {error}");
    }

    [Test]
    public void Renderer_DrawsBackgroundAccentAndRevealsBullets()
    {
        RequireSkia();
        using var renderer = SlideRasterizerLoader.Create();
        var slide = new PresentationSlide { Title = "Größe", Bullets = { "ÄÖÜ äöü ß – Zellkern" } };
        var layout = SlideFrameLayout.Compute(slide, 2, 3, "Zellbiologie", renderer);

        using var hidden = renderer.Render(new VideoFrameState(0, new[] { 0.0 }, -1, Array.Empty<double>(), 1), _ => layout, 1920, 1080);
        using var shown = renderer.Render(new VideoFrameState(0, new[] { 1.0 }, -1, Array.Empty<double>(), 1), _ => layout, 1920, 1080);

        Assert.That(hidden.GetPixel(5, 5).ToString(), Is.EqualTo("#ffffffff"));
        var accent = layout.Rects.Single();
        var px = shown.GetPixel((int)(accent.X + 10), (int)(accent.Y + accent.Height / 2));
        Assert.That((px.Red, px.Green, px.Blue), Is.EqualTo(((byte)0x00, (byte)0x7B, (byte)0xFF)));

        var line = layout.Lines.Single(l => l.Bullet == 0 && l.Text != "•");
        int Dark(SkiaSharp.SKBitmap b)
        {
            var count = 0;
            for (var x = (int)line.X; x < line.X + 600; x += 2)
                for (var y = (int)(line.Baseline - line.Size * 0.7); y < line.Baseline; y += 2)
                    if (b.GetPixel(x, y).Red < 128) count++;
            return count;
        }
        Assert.That(Dark(hidden), Is.Zero, "verborgener Stichpunkt");
        Assert.That(Dark(shown), Is.GreaterThan(50), "sichtbarer Stichpunkt mit Umlauten");
        Assert.That(renderer.Clean("Zelle \U0010FFFD ok"), Is.EqualTo("Zelle ok"), "Zeichen ohne Schrift entfallen");
        Assert.That(renderer.Clean("äöüß – „Zitat“"), Is.EqualTo("äöüß – „Zitat“"));
        Assert.That(renderer.Clean("A\u200DB\uFE0F"), Is.EqualTo("AB"), "ZWJ/Variantenwähler entfernt");
    }

    [Test]
    public void Renderer_TitleSlideBackground_AndCrossfadeMixesSlides()
    {
        RequireSkia();
        using var renderer = SlideRasterizerLoader.Create();
        var title = SlideFrameLayout.Compute(new PresentationSlide { Kind = SlideKind.Title, Title = "T" }, 1, 2, "", renderer);
        var content = SlideFrameLayout.Compute(new PresentationSlide { Title = "C" }, 2, 2, "", renderer);
        SlideFrameLayout LayoutOf(int i) => i == 0 ? title : content;

        using var half = renderer.Render(new VideoFrameState(1, Array.Empty<double>(), 0, Array.Empty<double>(), 0.5), LayoutOf, 1920, 1080);
        using var start = renderer.Render(new VideoFrameState(0, Array.Empty<double>(), -1, Array.Empty<double>(), 1), LayoutOf, 1920, 1080);

        Assert.That(start.GetPixel(5, 5).ToString(), Is.EqualTo("#ff343a40"));
        var mid = half.GetPixel(5, 5);
        Assert.That(mid.Red, Is.InRange(0x34 + 40, 0xFF - 40), "halb überblendet zwischen dunkel und weiß");
    }

    [Test]
    public void Loader_DisabledByEnvironment_ReportsReason()
    {
        Environment.SetEnvironmentVariable("STUDYSCOPE_DISABLE_SKIA", "1");
        SlideRasterizerLoader.Reset();
        try
        {
            var (ok, error) = SlideRasterizerLoader.Probe();
            Assert.That(ok, Is.False);
            Assert.That(error, Does.Contain("deaktiviert"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("STUDYSCOPE_DISABLE_SKIA", null);
            SlideRasterizerLoader.Reset();
        }
    }

    // ---------- Exporter mit ffmpeg-Ersatz ----------

    internal sealed class FakeFfmpeg : IFfmpegService
    {
        public FfmpegStatus Status { get; set; } = new(true, "ffmpeg", "9.0-test", true, true, true, "ffmpeg 9.0-test gefunden (Test).", null);
        public List<IReadOnlyList<string>> Runs { get; } = new();
        public string? ConcatText { get; private set; }
        public string? ConcatDirectory { get; private set; }
        public int FramesOnDisk { get; private set; }
        public bool Cancel { get; set; }

        public Task<FfmpegStatus> GetStatusAsync(bool refresh = false, CancellationToken cancellationToken = default) => Task.FromResult(Status);
        public Task<FfmpegStatus> TestAsync(string? customPath, CancellationToken cancellationToken = default) => Task.FromResult(Status);

        public async Task RunAsync(IReadOnlyList<string> arguments, double totalSeconds, IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Runs.Add(arguments);
            var concat = arguments.FirstOrDefault(a => a.EndsWith(".ffconcat"));
            if (concat != null)
            {
                ConcatText = await File.ReadAllTextAsync(concat, cancellationToken);
                ConcatDirectory = Path.GetDirectoryName(concat);
                FramesOnDisk = Directory.GetFiles(ConcatDirectory!, "*.png").Length;
            }
            // halbe Ausgabe schreiben, dann ggf. abbrechen
            await File.WriteAllTextAsync(arguments[^1], "halb", cancellationToken);
            if (Cancel) throw new OperationCanceledException();
            progress?.Report(1);
            await File.WriteAllTextAsync(arguments[^1], "fertiges MP4", cancellationToken);
        }
    }

    private static VideoDocument SampleVideo()
    {
        var video = new VideoDocument
        {
            Title = "Test",
            PresentationTitle = "Zellbiologie",
            Slides =
            {
                new PresentationSlide { Kind = SlideKind.Title, Title = "Zellbiologie" },
                new PresentationSlide { Title = "Aufbau", Bullets = { "Membran", "Kern" } }
            }
        };
        foreach (var slide in video.Slides)
            video.Scenes.Add(new VideoScene { SlideId = slide.Id, Narration = "Ein kurzer Sprechtext." });
        return video;
    }

    [Test]
    public void Estimate_FramesSizeDuration_AudioMakesItBigger()
    {
        var exporter = new VideoMp4Exporter(new FakeFfmpeg());
        var timeline = VideoTimeline.FromStarts(new[] { 0.0, 5.0 }, 65, 2);

        var silent = exporter.Estimate(SampleVideo(), timeline, withAudio: false);
        var withAudio = exporter.Estimate(SampleVideo(), timeline, withAudio: true);

        Assert.That(silent.FrameCount, Is.EqualTo(VideoFramePlan.Build(timeline, new[] { 0, 2 }).Count));
        Assert.That(silent.DurationSeconds, Is.EqualTo(65));
        Assert.That(withAudio.FileBytes - silent.FileBytes, Is.EqualTo((long)(65 * FfmpegArguments.AacKbps * 1000 / 8)).Within(2));
        Assert.That(withAudio.SizeText, Does.StartWith("ca. ").And.EndWith(" MB"));
        Assert.That(new Mp4ExportEstimate(10, 60, 1, 30).ExportTimeText, Is.EqualTo("unter 1 Min."));
        Assert.That(new Mp4ExportEstimate(10, 60, 1, 130).ExportTimeText, Is.EqualTo("ca. 3 Min."));
    }

    [Test]
    public async Task Availability_MissingFfmpegOrEncoder_OrSkia_Explained()
    {
        var fake = new FakeFfmpeg { Status = FfmpegStatus.NotFound("ffmpeg wurde nicht gefunden.", FfmpegInfo.InstallHint(false)) };
        var exporter = new VideoMp4Exporter(fake);
        var a = await exporter.GetAvailabilityAsync();
        Assert.That(a.IsAvailable, Is.False);
        Assert.That(a.Hint, Does.Contain(FfmpegInfo.WindowsInstall));

        fake.Status = new FfmpegStatus(true, "ffmpeg", "x", false, true, true, "gefunden", "vollständige Ausgabe installieren");
        Assert.That((await exporter.GetAvailabilityAsync()).Message, Does.Contain("H.264"));

        fake.Status = new FfmpegStatus(true, "ffmpeg", "x", true, true, true, "gefunden", null);
        Environment.SetEnvironmentVariable("STUDYSCOPE_DISABLE_SKIA", "1");
        SlideRasterizerLoader.Reset();
        try
        {
            var skia = await exporter.GetAvailabilityAsync();
            Assert.That(skia.IsAvailable, Is.False);
            Assert.That(skia.Message, Does.Contain("SkiaSharp"));
            Assert.ThrowsAsync<FfmpegException>(() => exporter.ExportAsync(new Mp4ExportRequest(SampleVideo(), VideoTimeline.FromEstimates(SampleVideo().Scenes, 1), null, "x.mp4")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("STUDYSCOPE_DISABLE_SKIA", null);
            SlideRasterizerLoader.Reset();
        }
    }

    [Test]
    public async Task Export_WritesFramesAndConcat_RenamesAtEnd_CleansUp()
    {
        RequireSkia();
        var fake = new FakeFfmpeg();
        var exporter = new VideoMp4Exporter(fake);
        var dir = Path.Combine(Path.GetTempPath(), "StudyScope Mp4 Täst " + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(dir);
        try
        {
            var target = Path.Combine(dir, "Zellen erklärt.mp4");
            var timeline = VideoTimeline.FromStarts(new[] { 0.0, 3.0 }, 6.5, 2);
            var progress = new List<double>();

            var result = await exporter.ExportAsync(new Mp4ExportRequest(SampleVideo(), timeline, Path.Combine(dir, "ton.wav"), target),
                new SyncProgress(p => progress.Add(p.Fraction)));

            var plan = VideoFramePlan.Build(timeline, new[] { 0, 2 });
            Assert.That(await File.ReadAllTextAsync(target), Is.EqualTo("fertiges MP4"));
            Assert.That(result.FrameCount, Is.EqualTo(plan.Count));
            Assert.That(result.HasAudio, Is.True);
            Assert.That(fake.FramesOnDisk, Is.EqualTo(plan.Count));
            Assert.That(fake.ConcatText!.Split('\n').Count(l => l.StartsWith("duration ")), Is.EqualTo(plan.Count));
            Assert.That(Directory.Exists(fake.ConcatDirectory), Is.False, "temporäre Bilder gelöscht");
            Assert.That(Directory.GetFiles(dir, "*.part", SearchOption.AllDirectories).Concat(Directory.GetFiles(dir, ".*")), Is.Empty);
            var args = fake.Runs.Single();
            Assert.That(args, Does.Contain(Path.Combine(dir, "ton.wav")));
            Assert.That(args[^1], Does.EndWith(".part").And.StartWith(dir));
            Assert.That(progress.Last(), Is.EqualTo(1));
            Assert.That(progress, Is.Ordered.Ascending);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task Export_Cancelled_NoTargetNoPartNoTempFrames()
    {
        RequireSkia();
        var fake = new FakeFfmpeg { Cancel = true };
        var exporter = new VideoMp4Exporter(fake);
        var dir = Path.Combine(Path.GetTempPath(), "StudyScopeMp4Cancel_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var target = Path.Combine(dir, "video.mp4");
            await File.WriteAllTextAsync(target, "alte Datei");

            Assert.CatchAsync<OperationCanceledException>(() =>
                exporter.ExportAsync(new Mp4ExportRequest(SampleVideo(), VideoTimeline.FromEstimates(SampleVideo().Scenes, 1), null, target)));

            Assert.That(await File.ReadAllTextAsync(target), Is.EqualTo("alte Datei"), "vorhandene Datei bleibt unverändert");
            Assert.That(Directory.GetFiles(dir), Is.EqualTo(new[] { target }));
            Assert.That(Directory.Exists(fake.ConcatDirectory), Is.False);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private sealed class SyncProgress(Action<Mp4ExportProgress> report) : IProgress<Mp4ExportProgress>
    {
        public void Report(Mp4ExportProgress value) => report(value);
    }
}

/// <summary>
/// Integration mit einem echten ffmpeg – nur, wenn es auf dem Rechner gefunden wird (sonst Ignore mit Grund):
/// MP3 (Datei, Abbruch ohne Reste) und MP4 (Dauer, Auflösung, Tonspur per ffprobe).
/// </summary>
[TestFixture]
[Category("Integration")]
public class FfmpegIntegrationTests
{
    private string _dir = null!;
    private FfmpegService _ffmpeg = null!;
    private FfmpegStatus _status = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "StudyScope ffmpeg Prüfung " + Guid.NewGuid().ToString("N")[..6] + " äöü");
        Directory.CreateDirectory(_dir);
        _ffmpeg = new FfmpegService(new JsonStorageService(Path.Combine(_dir, "daten")));
        _status = await _ffmpeg.GetStatusAsync();
        if (!_status.IsAvailable)
            Assert.Ignore($"Kein ffmpeg gefunden – Integrationstest übersprungen ({_status.Message})");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private string Wav(double seconds)
    {
        var path = Path.Combine(_dir, "Ton mit Leerzeichen ä.wav");
        VideoScriptServiceTests.WriteWav(path, seconds);
        return path;
    }

    [Test]
    public async Task Mp3_RealFfmpeg_FileWritten_ProgressReported()
    {
        if (!_status.CanEncodeMp3) Assert.Ignore("ffmpeg ohne libmp3lame");
        var transcoder = new FfmpegAudioTranscoder(_ffmpeg);
        var target = Path.Combine(_dir, "Ausgabe ü.mp3");
        var progress = new List<double>();

        await transcoder.ConvertToMp3FileAsync(Wav(3), target, 3, new Progress<double>(progress.Add));
        var bytes = await transcoder.ConvertToMp3Async(Wav(1));

        var head = (await File.ReadAllBytesAsync(target)).Take(3).ToArray();
        Assert.That(head[0] == 'I' && head[1] == 'D' && head[2] == '3' || head[0] == 0xFF, Is.True, "MP3-Kopf");
        Assert.That(File.Exists(target + ".part"), Is.False);
        Assert.That(bytes.Length, Is.GreaterThan(1000));
    }

    [Test]
    public async Task Mp3_Cancelled_KillsProcess_NoFileLeft()
    {
        if (!_status.CanEncodeMp3) Assert.Ignore("ffmpeg ohne libmp3lame");
        var transcoder = new FfmpegAudioTranscoder(_ffmpeg);
        var target = Path.Combine(_dir, "abgebrochen.mp3");
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(150);

        Assert.CatchAsync<OperationCanceledException>(() => transcoder.ConvertToMp3FileAsync(Wav(600), target, 600, null, cts.Token));

        Assert.That(File.Exists(target), Is.False);
        Assert.That(File.Exists(target + ".part"), Is.False);
    }

    [Test]
    public async Task Mp4_RealFfmpeg_DurationResolutionAudio()
    {
        if (!_status.CanEncodeMp4) Assert.Ignore("ffmpeg ohne H.264/AAC");
        var (ok, error) = SlideRasterizerLoader.Probe();
        if (!ok) Assert.Ignore($"SkiaSharp nicht lauffähig: {error}");
        var ffprobe = Path.Combine(Path.GetDirectoryName(_status.ExecutablePath)!, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
        if (!File.Exists(ffprobe)) Assert.Ignore("ffprobe liegt nicht neben ffmpeg");

        var video = new VideoDocument
        {
            Title = "Integration",
            Slides = { new PresentationSlide { Kind = SlideKind.Title, Title = "Größe" }, new PresentationSlide { Title = "Ä", Bullets = { "Ö", "Ü" } } }
        };
        foreach (var slide in video.Slides) video.Scenes.Add(new VideoScene { SlideId = slide.Id, Narration = "Text" });
        var target = Path.Combine(_dir, "Video mit Ton ß.mp4");
        var timeline = VideoTimeline.FromStarts(new[] { 0.0, 2.0 }, 4.0, 2);

        var result = await new VideoMp4Exporter(_ffmpeg).ExportAsync(new Mp4ExportRequest(video, timeline, Wav(4.0), target));

        var info = new ProcessStartInfo(ffprobe) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "-v", "error", "-show_entries", "format=duration:stream=codec_type,width,height", "-of", "default=nw=1", target })
            info.ArgumentList.Add(a);
        using var p = Process.Start(info)!;
        var output = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();

        Assert.That(result.Bytes, Is.GreaterThan(1000));
        Assert.That(output, Does.Contain("width=1920").And.Contain("height=1080").And.Contain("codec_type=audio"));
        var duration = double.Parse(output.Split('\n').First(l => l.StartsWith("duration=")).Split('=')[1], System.Globalization.CultureInfo.InvariantCulture);
        Assert.That(duration, Is.EqualTo(4.0).Within(0.1));
    }
}
