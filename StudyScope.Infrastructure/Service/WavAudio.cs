using System.Buffers.Binary;
using System.Text;
using StudyScope.Core.Localization;
using StudyScope.Infrastructure.Resources;

namespace StudyScope.Infrastructure.Services;

/// <summary>Unkomprimierte PCM-Audiodaten (16 Bit, little endian) samt Format.</summary>
public sealed record PcmAudio(byte[] Samples, int SampleRate, int Channels, int BitsPerSample)
{
    public int BytesPerSecond => SampleRate * Channels * BitsPerSample / 8;
    public double DurationSeconds => BytesPerSecond == 0 ? 0 : Samples.Length / (double)BytesPerSecond;
}

/// <summary>
/// WAV-Hilfen ohne Zusatzpaket: Antwort eines Sprachdienstes (WAV mit RIFF-Kopf oder rohes PCM „audio/l16“) in PCM
/// umwandeln und mehrere Teile zu einer WAV-Datei zusammenfügen.
/// </summary>
public static class WavAudio
{
    public const int DefaultSampleRate = 24000;

    /// <summary>
    /// Liest Audiodaten: WAV (RIFF/WAVE, PCM) wird ausgepackt, sonst gilt der Inhalt als rohes 16-Bit-PCM mono
    /// mit der Abtastrate aus dem MIME-Typ („audio/l16;rate=24000“), der Angabe <paramref name="sampleRate"/> oder 24 kHz.
    /// </summary>
    /// <exception cref="InvalidDataException">Unbekanntes oder komprimiertes Format (z. B. MP3).</exception>
    public static PcmAudio Decode(byte[] data, string? mimeType = null, int? sampleRate = null)
    {
        if (data.Length >= 12 && Encoding.ASCII.GetString(data, 0, 4) == "RIFF" && Encoding.ASCII.GetString(data, 8, 4) == "WAVE")
            return DecodeWav(data);

        var mime = (mimeType ?? string.Empty).Trim().ToLowerInvariant();
        if (mime.Length > 0 && !mime.StartsWith("audio/l16") && !mime.StartsWith("audio/pcm") && !mime.StartsWith("audio/wav")
            && !mime.StartsWith("audio/x-wav") && !mime.StartsWith("audio/wave"))
            throw new InvalidDataException(Loc.Format(InfraStrings.Wav_UnsupportedFormat, ("mime", mimeType ?? "")));

        var rate = sampleRate ?? RateFromMime(mime) ?? DefaultSampleRate;
        var samples = data.Length % 2 == 0 ? data : data[..^1];
        return new PcmAudio(samples, rate, 1, 16);
    }

    private static int? RateFromMime(string mime)
    {
        foreach (var part in mime.Split(';'))
        {
            var kv = part.Trim().Split('=');
            if (kv.Length == 2 && kv[0].Trim() == "rate" && int.TryParse(kv[1].Trim(), out var rate) && rate > 0)
                return rate;
        }
        return null;
    }

    private static PcmAudio DecodeWav(byte[] data)
    {
        int? rate = null, channels = null, bits = null;
        byte[]? samples = null;
        var pos = 12;
        while (pos + 8 <= data.Length)
        {
            var id = Encoding.ASCII.GetString(data, pos, 4);
            var size = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos + 4, 4));
            var start = pos + 8;
            // Streaming-WAV kennt die Länge vorab nicht (0 oder 0xFFFFFFFF): dann bis zum Ende lesen
            if (size < 0 || start + size > data.Length || (id == "data" && size == 0))
                size = data.Length - start;

            if (id == "fmt " && size >= 16)
            {
                var format = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(start, 2));
                if (format != 1 && format != -2) // 1 = PCM, 0xFFFE = Extensible
                    throw new InvalidDataException(Loc.Format(InfraStrings.Wav_Compressed, ("format", format.ToString(System.Globalization.CultureInfo.InvariantCulture))));
                channels = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(start + 2, 2));
                rate = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(start + 4, 4));
                bits = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(start + 14, 2));
            }
            else if (id == "data")
            {
                samples = data.AsSpan(start, size).ToArray();
            }

            pos = start + size + (size % 2);
        }

        if (samples == null)
            throw new InvalidDataException(InfraStrings.Wav_NoData);
        if (bits is not null and not 16)
            throw new InvalidDataException(Loc.Format(InfraStrings.Wav_UnsupportedBits, ("bits", bits.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))));

        return new PcmAudio(samples, rate ?? DefaultSampleRate, channels ?? 1, 16);
    }

    /// <summary>Stille in derselben Form wie <paramref name="format"/>.</summary>
    public static byte[] Silence(PcmAudio format, double seconds)
    {
        var bytes = (int)(format.BytesPerSecond * seconds);
        bytes -= bytes % (format.Channels * 2);
        return new byte[Math.Max(0, bytes)];
    }

    /// <summary>Schreibt den 44-Byte-Kopf einer PCM-WAV-Datei.</summary>
    public static void WriteHeader(Stream stream, int sampleRate, int channels, int bitsPerSample, long dataLength)
    {
        var header = new byte[44];
        var span = header.AsSpan();
        Encoding.ASCII.GetBytes("RIFF").CopyTo(span);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], (uint)Math.Min(uint.MaxValue, 36 + dataLength));
        Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(span[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], sampleRate * channels * bitsPerSample / 8);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], (short)(channels * bitsPerSample / 8));
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], (short)bitsPerSample);
        Encoding.ASCII.GetBytes("data").CopyTo(span[36..]);
        BinaryPrimitives.WriteUInt32LittleEndian(span[40..], (uint)Math.Min(uint.MaxValue, dataLength));
        stream.Write(header);
    }
}

/// <summary>
/// Schreibt eine WAV-Datei Stück für Stück: Kopf als Platzhalter, PCM-Teile anhängen, am Ende Längen eintragen.
/// Alle Teile müssen dasselbe Format haben (das erste legt es fest).
/// </summary>
public sealed class WavFileWriter : IDisposable
{
    private readonly FileStream _stream;
    private long _dataLength;

    public WavFileWriter(string path)
    {
        _stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        _stream.Write(new byte[44]);
    }

    public PcmAudio? Format { get; private set; }

    public double DurationSeconds => Format == null || Format.BytesPerSecond == 0 ? 0 : _dataLength / (double)Format.BytesPerSecond;

    /// <exception cref="InvalidDataException">Teil hat ein anderes Format als die vorherigen.</exception>
    public void Append(PcmAudio audio)
    {
        if (Format == null)
            Format = audio with { Samples = Array.Empty<byte>() };
        else if (Format.SampleRate != audio.SampleRate || Format.Channels != audio.Channels || Format.BitsPerSample != audio.BitsPerSample)
            throw new InvalidDataException(
                Loc.Format(InfraStrings.Wav_FormatMismatch, ("rate", Format.SampleRate.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("channels", Format.Channels.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    ("otherRate", audio.SampleRate.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("otherChannels", audio.Channels.ToString(System.Globalization.CultureInfo.InvariantCulture))));

        _stream.Write(audio.Samples);
        _dataLength += audio.Samples.Length;
    }

    public void AppendSilence(double seconds)
    {
        if (Format == null) return;
        var silence = WavAudio.Silence(Format, seconds);
        _stream.Write(silence);
        _dataLength += silence.Length;
    }

    /// <summary>Trägt die Längen in den Kopf ein und schreibt alles auf die Platte.</summary>
    public void Complete()
    {
        var format = Format ?? new PcmAudio(Array.Empty<byte>(), WavAudio.DefaultSampleRate, 1, 16);
        _stream.Position = 0;
        WavAudio.WriteHeader(_stream, format.SampleRate, format.Channels, format.BitsPerSample, _dataLength);
        _stream.Flush(true);
    }

    public void Dispose() => _stream.Dispose();
}
