using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using StudyScope.Core.Common;
using StudyScope.Core.Localization;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.Resources;

namespace StudyScope.Infrastructure.Services;

/// <summary>
/// Gemeinsamer HTML-Video-Player für den HTML-Export eines Videos und den Video-Abschnitt im HTML-Bericht (nur einmal
/// geschrieben). Die Folien werden als HTML mit demselben Stil wie <c>SlideView.razor</c> ausgegeben (Folien-CSS der App
/// als eingebettete Ressource), die Zeitachse kommt aus <see cref="VideoTimeline"/>. Ein Skript für alle Player der Seite:
/// Überblendung, Stichpunkt-Einblendung, Play/Pause, Vor/Zurück, Fortschrittsleiste, Vollbild, Tastatur. Der Ton liegt
/// base64-kodiert in einem <c>&lt;script type="application/octet-stream"&gt;</c> und wird erst beim ersten Abspielen zu
/// einer Blob-URL (schneller als eine riesige data:-URL im Attribut, Springen funktioniert in allen Browsern).
/// Bedientexte stehen in der Sprache, die der Aufrufer per <see cref="CultureScope"/> gesetzt hat; Texte, die das Skript
/// erst im Browser wechselt, liegen als <c>data-t-*</c>-Attribute am Player.
/// </summary>
public static class VideoPlayerHtml
{
    /// <summary>Grundgröße der Seite ohne Ton und Folien (CSS, Skript, Rahmen) für die Größenschätzung.</summary>
    public const long BaseOverheadBytes = 40 * 1024;

    /// <summary>Ungefährer Platz je Folie im HTML (Markup und Zeitachse).</summary>
    public const long BytesPerSlide = 1200;

    private static readonly Lazy<string> SlidesCssResource = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("StudyScope.Infrastructure.studyscope-slides.css")
            ?? throw new InvalidOperationException("Folien-CSS fehlt als eingebettete Ressource.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    });

    /// <summary>Folien-CSS der App (wwwroot/css/studyscope-slides.css) – dieselbe Datei, damit die Folien gleich aussehen.</summary>
    public static string SlidesCss => SlidesCssResource.Value;

    /// <summary>CSS für Folien und Player (einmal je Seite in den &lt;style&gt;-Block).</summary>
    public static string Css => SlidesCss + "\n" + PlayerCss;

    /// <summary>Skript für alle Player der Seite (einmal je Seite in einen &lt;script&gt;-Block).</summary>
    public static string Script => PlayerScript;

    /// <summary>Base64-Länge zu einer Bytezahl.</summary>
    public static long Base64Length(long bytes) => (bytes + 2) / 3 * 4;

    /// <summary>Hängt einen Player für ein Video an (Folien, Steuerleiste, Hinweis ohne Ton, Daten und ggf. Ton).</summary>
    public static void AppendPlayer(StringBuilder sb, VideoPlayerData data)
    {
        var video = data.Video;
        var timeline = data.Timeline;
        var count = Math.Min(video.Scenes.Count, video.Slides.Count);

        var play = H(ExportStrings.Player_Play);
        var slideOf = Loc.Format(ExportStrings.Player_SlideOf, ("number", "{n}"), ("total", "{t}"));
        sb.Append($"<div class=\"ssv-player\" tabindex=\"0\" role=\"region\" aria-label=\"{H(Loc.Format(ExportStrings.Player_Region, ("title", video.Title)))}\"");
        sb.Append($" data-t-play=\"{play}\" data-t-pause=\"{H(ExportStrings.Player_Pause)}\" data-t-slide=\"{H(slideOf)}\">");
        sb.Append("<div class=\"ssv-stage\">");
        for (var i = 0; i < count; i++)
            AppendSlide(sb, video.Slides[i], i + 1, count, video.PresentationTitle, i == 0);
        sb.Append($"<button type=\"button\" class=\"ssv-big-play\" data-act=\"play\" title=\"{play}\" aria-label=\"{play}\">▶</button>");
        sb.Append("</div>");

        sb.Append("<div class=\"ssv-bar\">");
        sb.Append($"<button type=\"button\" data-act=\"prev\" title=\"{H(ExportStrings.Player_PreviousSlideTitle)}\" aria-label=\"{H(ExportStrings.Player_PreviousSlide)}\">⏮</button>");
        sb.Append($"<button type=\"button\" data-act=\"play\" class=\"ssv-play\" title=\"{H(ExportStrings.Player_PlayPauseTitle)}\" aria-label=\"{play}\">▶</button>");
        sb.Append($"<button type=\"button\" data-act=\"next\" title=\"{H(ExportStrings.Player_NextSlideTitle)}\" aria-label=\"{H(ExportStrings.Player_NextSlide)}\">⏭</button>");
        sb.Append($"<input type=\"range\" class=\"ssv-seek\" min=\"0\" max=\"1000\" step=\"1\" value=\"0\" aria-label=\"{H(ExportStrings.Player_Progress)}\">");
        sb.Append($"<span class=\"ssv-time\">0:00 / {Clock(timeline.Total)}</span>");
        sb.Append($"<span class=\"ssv-scene\">{H(Loc.Format(ExportStrings.Player_SlideOf, ("number", 1), ("total", count)))}</span>");
        sb.Append(data.HasAudio ? string.Empty : $"<span class=\"ssv-mute\" title=\"{H(ExportStrings.Player_NoAudio)}\">🔇</span>");
        sb.Append($"<button type=\"button\" data-act=\"fs\" title=\"{H(ExportStrings.Player_FullScreenTitle)}\" aria-label=\"{H(ExportStrings.Player_FullScreen)}\">⛶</button>");
        sb.Append("</div>");

        // Hinweis in der Sprache des Exports neu bilden (NoAudioReason der Player-Daten steht in der Oberflächensprache)
        if (!data.HasAudio)
            sb.Append($"<div class=\"ssv-hint\">🔇 {H(VideoHtmlExporter.NoAudioReason(video, audioPath: null))}</div>");

        sb.Append("<script type=\"application/json\" class=\"ssv-data\">").Append(TimelineJson(timeline, count, data.HasAudio)).Append("</script>");
        if (data.HasAudio)
        {
            sb.Append($"<script type=\"application/octet-stream\" class=\"ssv-audio\" data-mime=\"{H(data.AudioMimeType)}\">");
            sb.Append(Convert.ToBase64String(data.Audio!));
            sb.Append("</script>");
        }
        sb.Append("</div>");
    }

    /// <summary>
    /// Zeitachse als JSON für das Skript. Von Hand mit <see cref="Utf8JsonWriter"/> (ohne Reflection, enthält nur Zahlen
    /// und Wahrheitswerte – ein „&lt;/script&gt;“ kann darin nicht vorkommen).
    /// </summary>
    public static string TimelineJson(VideoTimeline timeline, int count, bool hasAudio)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteStartArray("starts");
            foreach (var s in timeline.Starts.Take(count)) w.WriteNumberValue(Round(s));
            w.WriteEndArray();
            w.WriteStartArray("durations");
            foreach (var d in timeline.Durations.Take(count)) w.WriteNumberValue(Round(d));
            w.WriteEndArray();
            w.WriteNumber("total", Round(timeline.Total));
            w.WriteNumber("firstDelay", VideoTimeline.FirstBulletDelaySeconds);
            w.WriteNumber("share", VideoTimeline.BulletRevealShare);
            w.WriteBoolean("hasAudio", hasAudio);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }


    /// <summary>Eine Folie wie <c>SlideView.razor</c> (Klassen aus <see cref="SlideLayout.CssClass"/>).</summary>
    private static void AppendSlide(StringBuilder sb, PresentationSlide slide, int number, int total, string presentationTitle, bool active)
    {
        sb.Append($"<div class=\"{SlideLayout.CssClass(slide)}{(active ? " ssv-active" : string.Empty)}\" aria-hidden=\"{(active ? "false" : "true")}\"><div class=\"pp-slide-content\">");
        sb.Append($"<h1>{H(slide.Title)}</h1><div class=\"pp-accent\"></div>");
        if (slide.Kind == SlideKind.Title)
        {
            if (!string.IsNullOrWhiteSpace(slide.Subtitle))
                sb.Append($"<div class=\"pp-subtitle\">{H(slide.Subtitle)}</div>");
        }
        else
        {
            sb.Append("<ul>");
            foreach (var bullet in slide.Bullets)
                sb.Append($"<li>{H(bullet)}</li>");
            sb.Append("</ul>");
        }
        sb.Append("</div>");
        if (slide.Kind != SlideKind.Title)
            sb.Append($"<div class=\"pp-footer\"><span>{H(presentationTitle)}</span><span>{number} / {total}</span></div>");
        sb.Append("</div>");
    }

    /// <summary>„m:ss“.</summary>
    public static string Clock(double seconds)
    {
        var s = Math.Max(0, (int)Math.Floor(seconds));
        return $"{s / 60}:{s % 60:00}";
    }

    private static double Round(double value) => Math.Round(value, 3);

    private static string H(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

    private const string PlayerCss = """
.ssv-player { position: relative; background: #111; border-radius: 8px; overflow: hidden; outline: none; color: #fff; font-family: "Segoe UI", -apple-system, Roboto, sans-serif; }
.ssv-player:focus-visible { box-shadow: 0 0 0 3px #66b2ff; }
.ssv-stage { position: relative; width: 100%; aspect-ratio: 16 / 9; overflow: hidden; background: #000; cursor: pointer; }
.ssv-stage > .pp-slide { position: absolute; inset: 0; opacity: 0; transition: opacity 0.6s ease; }
.ssv-stage > .pp-slide.ssv-active { opacity: 1; }
.ssv-big-play { position: absolute; left: 50%; top: 50%; transform: translate(-50%, -50%); /* ss:physisch – mittig */ width: 76px; height: 76px; border-radius: 50%; border: none; background: rgba(0, 123, 255, 0.9); color: #fff; font-size: 30px; line-height: 76px; padding: 0 0 0 6px; cursor: pointer; box-shadow: 0 4px 18px rgba(0, 0, 0, 0.35); }
.ssv-big-play:hover { background: #0069d9; }
.ssv-player.ssv-started .ssv-big-play { display: none; }
.ssv-bar { display: flex; align-items: center; gap: 8px; padding: 6px 10px; background: #212529; font-size: 13px; }
.ssv-bar button { background: transparent; border: none; color: #fff; font-size: 17px; cursor: pointer; padding: 2px 6px; border-radius: 4px; line-height: 1.2; }
.ssv-bar button:hover { background: rgba(255, 255, 255, 0.15); }
.ssv-seek { flex: 1; min-width: 60px; accent-color: #007bff; cursor: pointer; }
.ssv-time, .ssv-scene { white-space: nowrap; color: #ced4da; font-variant-numeric: tabular-nums; }
.ssv-hint { padding: 6px 12px; background: #fff3cd; color: #664d03; font-size: 13px; line-height: 1.4; }
.ssv-loading .ssv-play { opacity: 0.5; }
.ssv-player:fullscreen { display: flex; flex-direction: column; justify-content: center; background: #000; border-radius: 0; }
.ssv-player:fullscreen .ssv-stage { width: min(100vw, calc((100vh - 44px) * 16 / 9)); margin: 0 auto; }
.ssv-player:fullscreen .ssv-hint { display: none; }
@media (max-width: 520px) { .ssv-scene { display: none; } }
@media print { .ssv-player { display: none !important; } }
""";

    private const string PlayerScript = """
(function () {
  if (window.StudyScopeVideoPlayers) return;
  var all = [];
  window.StudyScopeVideoPlayers = all;

  function clock(s) { s = Math.max(0, Math.floor(s)); var m = Math.floor(s / 60), r = s % 60; return m + ':' + (r < 10 ? '0' : '') + r; }

  function Player(root) {
    var data = JSON.parse(root.querySelector('script.ssv-data').textContent);
    var texts = root.dataset;
    var slides = Array.prototype.slice.call(root.querySelectorAll('.ssv-stage > .pp-slide'));
    var count = slides.length;
    var seek = root.querySelector('.ssv-seek');
    var timeText = root.querySelector('.ssv-time');
    var sceneText = root.querySelector('.ssv-scene');
    var playButtons = root.querySelectorAll('[data-act="play"]');
    var audioBlock = root.querySelector('script.ssv-audio');
    // Zustände wie in der App: stopped (alle Stichpunkte), playing, paused, ended (letzte Folie bleibt stehen)
    var st = { pos: 0, state: 'stopped', scene: -1, audio: null, loading: false, clockStart: 0, clockBase: 0, raf: 0, seeking: false };
    var self = this;

    function sceneAt(t) { var idx = 0; for (var i = 0; i < count; i++) { if (data.starts[i] <= t + 0.05) idx = i; else break; } return idx; }
    function visibleBullets(n, elapsed, dur) {
      if (n <= 0) return 0;
      if (elapsed < data.firstDelay) return 0;
      if (n === 1) return 1;
      var win = Math.max(0.5, dur * data.share - data.firstDelay);
      var shown = 1 + Math.floor((elapsed - data.firstDelay) / (win / n));
      return Math.min(n, Math.max(1, shown));
    }

    function render() {
      if (count === 0) return;
      var i = sceneAt(st.pos);
      if (i !== st.scene) {
        slides.forEach(function (s, k) { s.classList.toggle('ssv-active', k === i); s.setAttribute('aria-hidden', k === i ? 'false' : 'true'); });
        st.scene = i;
      }
      var items = slides[i].querySelectorAll('li');
      var timed = st.state === 'playing' || st.state === 'paused';
      var n = timed ? visibleBullets(items.length, st.pos - data.starts[i], data.durations[i]) : items.length;
      for (var k = 0; k < items.length; k++) items[k].classList.toggle('pp-bullet-hidden', k >= n);
      if (!st.seeking) seek.value = data.total > 0 ? Math.round(Math.min(1, st.pos / data.total) * 1000) : 0;
      timeText.textContent = clock(st.pos) + ' / ' + clock(data.total);
      sceneText.textContent = (texts.tSlide || '{n} / {t}').replace('{n}', i + 1).replace('{t}', count);
      var playing = st.state === 'playing';
      playButtons.forEach(function (b) {
        if (b.classList.contains('ssv-big-play')) return;
        b.textContent = playing ? '⏸' : '▶';
        b.setAttribute('aria-label', playing ? (texts.tPause || '⏸') : (texts.tPlay || '▶'));
      });
    }

    function now() { return performance.now() / 1000; }

    function loop() {
      st.raf = 0;
      if (st.state !== 'playing') return;
      if (st.audio) st.pos = st.audio.currentTime;
      else st.pos = st.clockBase + (now() - st.clockStart);
      if (st.pos >= data.total) { finish(); return; }
      render();
      st.raf = requestAnimationFrame(loop);
    }

    function finish() {
      st.pos = data.total; st.state = 'ended';
      if (st.audio) st.audio.pause();
      render();
    }

    function ensureAudio() {
      if (!audioBlock || st.audio) return Promise.resolve(st.audio);
      if (st.loading) return st.loading;
      root.classList.add('ssv-loading');
      var mime = audioBlock.getAttribute('data-mime') || 'audio/wav';
      st.loading = fetch('data:' + mime + ';base64,' + audioBlock.textContent.trim())
        .then(function (r) { return r.blob(); })
        .then(function (blob) {
          var a = new Audio(URL.createObjectURL(blob));
          a.preload = 'auto';
          a.addEventListener('ended', function () { if (st.state === 'playing') finish(); });
          st.audio = a;
          root.classList.remove('ssv-loading');
          return a;
        });
      return st.loading;
    }

    function play() {
      all.forEach(function (p) { if (p !== self) p.pause(); });
      if (st.state === 'ended') st.pos = 0;
      root.classList.add('ssv-started');
      st.state = 'playing';
      render();
      if (audioBlock) {
        ensureAudio().then(function (a) {
          if (st.state !== 'playing') return;
          a.currentTime = Math.min(st.pos, Math.max(0, data.total - 0.05));
          var p = a.play();
          if (p && p.catch) p.catch(function () { st.state = 'paused'; render(); });
          if (!st.raf) st.raf = requestAnimationFrame(loop);
        });
      } else {
        st.clockBase = st.pos; st.clockStart = now();
        if (!st.raf) st.raf = requestAnimationFrame(loop);
      }
    }

    this.pause = function () {
      if (st.state !== 'playing') return;
      if (st.audio) { st.audio.pause(); st.pos = st.audio.currentTime; }
      else st.pos = st.clockBase + (now() - st.clockStart);
      st.state = 'paused';
      render();
    };

    function toggle() { if (st.state === 'playing') self.pause(); else play(); }

    function goTo(t) {
      t = Math.max(0, Math.min(t, Math.max(0, data.total - 0.01)));
      st.pos = t;
      if (st.state === 'ended') st.state = 'paused';
      if (st.state === 'playing') {
        if (st.audio) st.audio.currentTime = t;
        else { st.clockBase = t; st.clockStart = now(); }
      }
      render();
    }

    function goScene(i) { if (count === 0) return; i = Math.max(0, Math.min(count - 1, i)); goTo(data.starts[i]); }

    // Abgelehnte Vollbild-Anfragen (z. B. ohne Benutzergeste) still ignorieren
    function fullscreen() {
      var p = document.fullscreenElement ? document.exitFullscreen() : (root.requestFullscreen ? root.requestFullscreen() : null);
      if (p && p.catch) p.catch(function () { });
    }

    // Ton schon vorbereiten, wenn die Maus den Player berührt (das Umwandeln großer Dateien dauert einen Moment)
    root.addEventListener('pointerenter', function () { if (audioBlock) ensureAudio(); });
    root.addEventListener('focusin', function () { if (audioBlock) ensureAudio(); });

    root.addEventListener('click', function (e) {
      var b = e.target.closest('[data-act]');
      if (b) {
        var act = b.getAttribute('data-act');
        if (act === 'play') toggle();
        else if (act === 'prev') goScene(sceneAt(st.pos) - 1);
        else if (act === 'next') goScene(sceneAt(st.pos) + 1);
        else if (act === 'fs') fullscreen();
        return;
      }
      if (e.target.closest('.ssv-stage')) toggle();
    });
    seek.addEventListener('input', function () { st.seeking = true; goTo(seek.value / 1000 * data.total); });
    seek.addEventListener('change', function () { st.seeking = false; goTo(seek.value / 1000 * data.total); });
    root.addEventListener('keydown', function (e) {
      if (e.target === seek && (e.key === 'ArrowLeft' || e.key === 'ArrowRight')) return;
      var k = e.key;
      if (k === ' ' || k === 'k' || k === 'K') toggle();
      else if (k === 'ArrowRight' || k === 'PageDown') goScene(sceneAt(st.pos) + 1);
      else if (k === 'ArrowLeft' || k === 'PageUp') goScene(sceneAt(st.pos) - 1);
      else if (k === 'Home') goScene(0);
      else if (k === 'End') goScene(count - 1);
      else if (k === 'f' || k === 'F') fullscreen();
      else return;
      e.preventDefault();
    });

    // Für Tests und Fehlersuche
    this.state = function () { return { pos: st.pos, state: st.state, scene: st.scene, total: data.total, hasAudio: !!audioBlock, audioReady: !!st.audio }; };
    this.play = play;
    this.goTo = goTo;
    this.root = root;
    render();
  }

  function init() {
    var roots = document.querySelectorAll('.ssv-player');
    for (var i = 0; i < roots.length; i++) {
      if (roots[i].__ssv) continue;
      roots[i].__ssv = new Player(roots[i]);
      all.push(roots[i].__ssv);
    }
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
})();
""";
}
