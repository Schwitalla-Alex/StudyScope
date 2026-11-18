using System.Diagnostics;
using System.Text;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;

namespace StudyScope.Infrastructure.Services;

/// <summary>
/// Findet und startet ein vorhandenes ffmpeg (wird nicht mitgeliefert). Jeder Lauf ist ein eigener Prozess mit
/// <see cref="ProcessStartInfo.ArgumentList"/> (Pfade mit Leerzeichen/Umlauten unverändert), Fortschritt aus
/// <c>-progress pipe:1</c>, Fehlerausgabe gesammelt für <see cref="FfmpegException.Detail"/>; Abbruch beendet den
/// Prozess samt Unterprozessen.
/// </summary>
public class FfmpegService : IFfmpegService
{
    private const int ProbeTimeoutMs = 15000;

    private readonly IStorageService _storage;
    private readonly FfmpegLocator _locator;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private FfmpegStatus? _cached;

    public FfmpegService(IStorageService storage, FfmpegLocator? locator = null)
    {
        _storage = storage;
        _locator = locator ?? FfmpegLocator.Default;
    }

    public async Task<FfmpegStatus> GetStatusAsync(bool refresh = false, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cached != null && !refresh) return _cached;
            string? custom = null;
            try { custom = (await _storage.LoadExportSettingsAsync())?.FfmpegPath; }
            catch { /* ohne Einstellungen automatisch suchen */ }
            _cached = await ProbeAsync(custom, cancellationToken);
            return _cached;
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task<FfmpegStatus> TestAsync(string? customPath, CancellationToken cancellationToken = default) =>
        ProbeAsync(customPath, cancellationToken);

    /// <summary>Prüft die Kandidaten der Reihe nach mit <c>-version</c> und liest die Encoder des ersten lauffähigen.</summary>
    private async Task<FfmpegStatus> ProbeAsync(string? customPath, CancellationToken cancellationToken)
    {
        var hint = FfmpegInfo.InstallHint(OperatingSystem.IsLinux());
        var hasCustom = !string.IsNullOrWhiteSpace(customPath);
        var customCandidates = hasCustom ? _locator.CustomCandidates(customPath!) : Array.Empty<string>();

        foreach (var candidate in _locator.Candidates(customPath))
        {
            var version = await TryRunAsync(candidate, new[] { "-hide_banner", "-version" }, cancellationToken);
            var parsed = FfmpegArguments.ParseVersion(version);
            if (parsed == null) continue;

            var encoders = await TryRunAsync(candidate, new[] { "-hide_banner", "-encoders" }, cancellationToken) ?? string.Empty;
            var h264 = FfmpegArguments.HasEncoder(encoders, "libx264");
            var aac = FfmpegArguments.HasEncoder(encoders, "aac");
            var mp3 = FfmpegArguments.HasEncoder(encoders, "libmp3lame");

            var notCustom = hasCustom && !customCandidates.Contains(candidate, StringComparer.OrdinalIgnoreCase)
                ? $" Der eingetragene Pfad „{customPath!.Trim()}“ funktioniert nicht – verwendet wird das automatisch gefundene ffmpeg."
                : string.Empty;
            var missing = new List<string>();
            if (!h264) missing.Add("H.264 (libx264)");
            if (!aac) missing.Add("AAC");
            if (!mp3) missing.Add("MP3 (libmp3lame)");
            var abilities = $"MP4 {(h264 && aac ? "✓" : "✗")} · MP3 {(mp3 ? "✓" : "✗")}";
            var message = $"ffmpeg {parsed} gefunden: {candidate} ({abilities}).{notCustom}";
            var missingHint = missing.Count == 0
                ? null
                : $"Diesem ffmpeg fehlt {string.Join(", ", missing)}. Installieren Sie eine vollständige Ausgabe – {hint}";
            return new FfmpegStatus(true, candidate, parsed, h264, aac, mp3, message, missingHint);
        }

        return hasCustom
            ? FfmpegStatus.NotFound($"ffmpeg wurde unter „{customPath!.Trim()}“ nicht gefunden oder startet nicht – und auch sonst nicht auf diesem Rechner.", hint)
            : FfmpegStatus.NotFound("ffmpeg wurde auf diesem Rechner nicht gefunden (PATH" + (OperatingSystem.IsWindows() ? ", WinGet-Ordner" : ", /usr/bin") + ").", hint);
    }

    /// <summary>Startet ffmpeg kurz zur Prüfung; liefert die Ausgabe oder null (startet nicht, Zeitüberschreitung, Fehlercode).</summary>
    private static async Task<string?> TryRunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process { StartInfo = CreateStartInfo(executable, arguments) };
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProbeTimeoutMs);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                Kill(process);
                cancellationToken.ThrowIfCancellationRequested();
                return null;
            }
            return process.ExitCode == 0 ? await output + await error : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null; // Datei nicht ausführbar, falsche Architektur, keine Berechtigung …
        }
    }

    private static ProcessStartInfo CreateStartInfo(string executable, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        return info;
    }

    public async Task RunAsync(IReadOnlyList<string> arguments, double totalSeconds, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(cancellationToken: cancellationToken);
        if (!status.IsAvailable || status.ExecutablePath == null)
            throw new FfmpegException(status.Message + " " + status.Hint);

        cancellationToken.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = CreateStartInfo(status.ExecutablePath, arguments), EnableRaisingEvents = true };
        var errorTail = new Queue<string>();
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new FfmpegException($"ffmpeg konnte nicht gestartet werden: {ex.Message}", status.ExecutablePath, ex);
        }

        using var registration = cancellationToken.Register(() => Kill(process));

        var readOutput = Task.Run(async () =>
        {
            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync()) != null)
            {
                if (FfmpegArguments.ParseProgressSeconds(line) is { } seconds && totalSeconds > 0)
                    progress?.Report(Math.Clamp(seconds / totalSeconds, 0, 1));
                else if (FfmpegArguments.IsProgressEnd(line))
                    progress?.Report(1);
            }
        }, CancellationToken.None);
        var readError = Task.Run(async () =>
        {
            string? line;
            while ((line = await process.StandardError.ReadLineAsync()) != null)
            {
                lock (errorTail)
                {
                    errorTail.Enqueue(line);
                    while (errorTail.Count > 40) errorTail.Dequeue();
                }
            }
        }, CancellationToken.None);

        await process.WaitForExitAsync(CancellationToken.None);
        await Task.WhenAll(readOutput, readError);

        cancellationToken.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
        {
            string detail;
            lock (errorTail) detail = string.Join(Environment.NewLine, errorTail);
            throw new FfmpegException(
                $"ffmpeg hat mit einem Fehler abgebrochen (Code {process.ExitCode}). Es wurde keine halbe Datei gespeichert. " +
                "Prüfen Sie freien Speicherplatz und Schreibrechte im Zielordner; die technische Meldung steht in den Details.",
                detail);
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // bereits beendet
        }
    }
}
