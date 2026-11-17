using System.Collections.Concurrent;
using Microsoft.JSInterop;
using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.ViewModels.Audio;

namespace StudyScope.UI.Services;

/// <summary>
/// „Systemstimme“: liest über die Web Speech API der WebView vor (wwwroot/js/studyscope-speech.js). Kostenlos und offline
/// mit den Stimmen des Betriebssystems, aber ohne Dateiexport. Der JS-Zugang wird von der Oberfläche über
/// <see cref="Attach"/> gesetzt (IJSRuntime gehört zur Blazor-Sitzung, nicht zum DI-Stamm).
/// </summary>
public sealed class BrowserSpeechService : ILiveSpeechService, IDisposable
{
    private readonly ConcurrentDictionary<int, TaskCompletionSource<bool>> _pending = new();
    private IJSRuntime? _js;
    private DotNetObjectReference<BrowserSpeechService>? _reference;
    private Task<(bool Supported, IReadOnlyList<SpeechVoice> Voices)>? _check;
    private int _nextId;

    public SpeechProviderType Provider => SpeechProviderType.SystemVoice;
    public string DisplayName => "Systemstimme (kostenlos, offline)";
    public bool SupportsLivePlayback => true;
    public bool SupportsFileExport => false;

    public bool IsAttached => _js != null;

    public void Attach(IJSRuntime js)
    {
        if (ReferenceEquals(_js, js))
            return;

        _js = js;
        _reference ??= DotNetObjectReference.Create(this);
        _check = null;
    }

    /// <summary>Stimmenliste neu einlesen (z. B. nach Installation einer Stimme).</summary>
    public void Refresh() => _check = null;

    private Task<(bool Supported, IReadOnlyList<SpeechVoice> Voices)> CheckAsync() => _check ??= RunCheckAsync();

    private async Task<(bool, IReadOnlyList<SpeechVoice>)> RunCheckAsync()
    {
        try
        {
            var result = await _js!.InvokeAsync<CheckResult>("studyScopeSpeech.check");
            var voices = (result.Voices ?? Array.Empty<VoiceInfo>())
                .Select(v => new SpeechVoice(v.Id ?? v.Name ?? string.Empty, v.Name ?? v.Id ?? string.Empty, v.Lang ?? string.Empty, v.Local, v.IsDefault))
                .Where(v => v.Id.Length > 0)
                .ToList();
            return (result.Supported, SystemVoiceRules.Order(voices));
        }
        catch (Exception)
        {
            // Skript nicht geladen oder WebView ohne JS-Zugriff: wie „nicht verfügbar“ behandeln
            _check = null;
            return (false, Array.Empty<SpeechVoice>());
        }
    }

    public async Task<SpeechAvailability> GetAvailabilityAsync(SpeechContext context, CancellationToken cancellationToken = default)
    {
        if (_js == null)
            return SpeechAvailability.Checking();

        var (supported, voices) = await CheckAsync();
        return SystemVoiceRules.Evaluate(supported, voices, OperatingSystem.IsLinux());
    }

    public async Task<IReadOnlyList<SpeechVoice>> GetVoicesAsync(CancellationToken cancellationToken = default)
    {
        if (_js == null)
            return Array.Empty<SpeechVoice>();
        return (await CheckAsync()).Voices;
    }

    public async Task<bool> SpeakAsync(string text, string voiceId, double rate, CancellationToken cancellationToken = default)
    {
        if (_js == null || _reference == null)
            throw new InvalidOperationException("Die Sprachausgabe ist noch nicht bereit. Bitte den Reiter neu öffnen.");

        // Eine laufende Äußerung gilt als abgebrochen
        CompleteAll(false);

        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        await using var registration = cancellationToken.Register(() =>
        {
            if (_pending.TryRemove(id, out var pending))
                pending.TrySetResult(false);
        });

        await _js.InvokeVoidAsync("studyScopeSpeech.speak", _reference, id, text, voiceId ?? string.Empty, SpeechSettings.ClampRate(rate));
        return await tcs.Task;
    }

    /// <summary>Rückmeldung aus JS: Äußerung zu Ende (vollständig), abgebrochen oder mit Fehler.</summary>
    [JSInvokable]
    public void OnSpeechEnded(int id, bool completed, string? error)
    {
        if (!_pending.TryRemove(id, out var tcs))
            return;

        if (string.IsNullOrEmpty(error))
            tcs.TrySetResult(completed);
        else
            tcs.TrySetException(new LlmServiceException(ErrorMessage(error), "Systemstimme", detail: error));
    }

    private static string ErrorMessage(string code) => code switch
    {
        "not-supported" => "Ihre Systemumgebung stellt keine Sprachausgabe bereit.",
        "voice-unavailable" => "Die gewählte Stimme ist nicht verfügbar. Bitte eine andere Stimme wählen.",
        "language-unavailable" => "Für die Sprache ist keine Stimme installiert. Bitte eine deutsche Stimme installieren oder eine andere Stimme wählen.",
        "synthesis-unavailable" or "audio-hardware" => "Die Sprachausgabe konnte nicht gestartet werden. Ist ein Audiogerät angeschlossen und aktiviert?",
        "network" => "Die gewählte Online-Stimme ist nicht erreichbar. Bitte eine Offline-Stimme wählen oder die Internetverbindung prüfen.",
        "not-allowed" => "Die Sprachausgabe wurde vom System blockiert. Bitte erneut auf „Abspielen“ klicken.",
        _ => $"Die Sprachausgabe ist fehlgeschlagen ({code})."
    };

    private void CompleteAll(bool result)
    {
        foreach (var id in _pending.Keys.ToList())
        {
            if (_pending.TryRemove(id, out var tcs))
                tcs.TrySetResult(result);
        }
    }

    public async Task PauseAsync()
    {
        if (_js != null) await _js.InvokeVoidAsync("studyScopeSpeech.pause");
    }

    public async Task ResumeAsync()
    {
        if (_js != null) await _js.InvokeVoidAsync("studyScopeSpeech.resume");
    }

    public async Task StopAsync()
    {
        CompleteAll(false);
        if (_js == null) return;
        try
        {
            await _js.InvokeVoidAsync("studyScopeSpeech.stop");
        }
        catch (JSDisconnectedException)
        {
            // Fenster wird gerade geschlossen
        }
    }

    public void Dispose()
    {
        CompleteAll(false);
        _reference?.Dispose();
    }

    private sealed class CheckResult
    {
        public bool Supported { get; set; }
        public VoiceInfo[]? Voices { get; set; }
    }

    private sealed class VoiceInfo
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Lang { get; set; }
        public bool Local { get; set; }
        public bool IsDefault { get; set; }
    }
}
