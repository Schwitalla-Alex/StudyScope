using Microsoft.JSInterop;
using StudyScope.Core.Interfaces;

namespace StudyScope.UI.Services;

/// <summary>
/// Spielt Audiodateien (WAV) über ein HTML-Audio-Element ab (wwwroot/js/studyscope-audio.js). Die Datei liegt im
/// Benutzerdaten-Ordner, den die WebView nicht direkt lesen darf; sie wird daher als Stream übergeben und im Browser als
/// Blob-URL abgespielt (Springen und Tempo funktionieren so ohne Server). Das Audio-Element ist global, die Wiedergabe
/// läuft beim Reiterwechsel weiter. Der JS-Zugang kommt per <see cref="Attach"/> aus der Oberfläche.
/// </summary>
public sealed class BrowserAudioPlayer : IAudioFilePlayer, IDisposable
{
    private IJSRuntime? _js;
    private DotNetObjectReference<BrowserAudioPlayer>? _reference;
    private string? _loadedKey;

    public bool IsReady => _js != null;

    public event Action<double, bool>? PositionChanged;

    public void Attach(IJSRuntime js)
    {
        if (ReferenceEquals(_js, js))
            return;

        _js = js;
        _reference ??= DotNetObjectReference.Create(this);
        _loadedKey = null;
    }

    public async Task PlayAsync(string filePath, double startSeconds, double rate)
    {
        var js = _js ?? throw new InvalidOperationException("Der Audio-Player ist noch nicht bereit.");

        // Neu laden, wenn eine andere Datei gewählt oder dieselbe neu erzeugt wurde
        var key = $"{filePath}|{File.GetLastWriteTimeUtc(filePath).Ticks}";
        if (_loadedKey != key)
        {
            var stream = File.OpenRead(filePath);
            using var streamReference = new DotNetStreamReference(stream, leaveOpen: false);
            await js.InvokeVoidAsync("studyScopeAudio.load", streamReference, "audio/wav", _reference);
            _loadedKey = key;
        }

        await js.InvokeVoidAsync("studyScopeAudio.play", startSeconds, rate);
    }

    public Task PauseAsync() => InvokeAsync("studyScopeAudio.pause");
    public Task ResumeAsync() => InvokeAsync("studyScopeAudio.resume");
    public Task StopAsync() => InvokeAsync("studyScopeAudio.stop");
    public Task SetRateAsync(double rate) => InvokeAsync("studyScopeAudio.setRate", rate);

    private async Task InvokeAsync(string function, params object?[] args)
    {
        if (_js == null) return;
        await _js.InvokeVoidAsync(function, args);
    }

    /// <summary>Vom Skript bei timeupdate/seeked/ended aufgerufen.</summary>
    [JSInvokable]
    public void OnPosition(double seconds, bool ended) => PositionChanged?.Invoke(seconds, ended);

    public void Dispose() => _reference?.Dispose();
}
