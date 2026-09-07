#nullable enable
using System;
using System.IO;
using System.Text;

// Reads the packaged test recordings directly, so the existing vehicle bundle
// and its confirmed model/material assets do not need to be rebuilt.
internal sealed class AudiRS6RWave
{
    public int Channels { get; private set; }
    public int Frequency { get; private set; }
    public float[] Samples { get; private set; } = Array.Empty<float>();

    public static AudiRS6RWave Read(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length < 44 || stream.Length > 16 * 1024 * 1024)
            throw new InvalidDataException("WAV size is outside the supported range.");
        using var reader = new BinaryReader(stream, Encoding.ASCII);
        if (new string(reader.ReadChars(4)) != "RIFF")
            throw new InvalidDataException("Expected a RIFF WAV file.");
        var riffEnd = 8L + reader.ReadUInt32();
        if (riffEnd > stream.Length || new string(reader.ReadChars(4)) != "WAVE")
            throw new InvalidDataException("Invalid WAV header or truncated RIFF data.");
        int format = 0, channels = 0, frequency = 0, bits = 0, alignment = 0;
        byte[]? data = null;
        while (stream.Position + 8 <= riffEnd)
        {
            var id = new string(reader.ReadChars(4));
            var size = reader.ReadUInt32();
            var end = stream.Position + size;
            if (end > riffEnd)
                throw new InvalidDataException("Truncated WAV chunk.");
            if (id == "fmt ")
            {
                if (size < 16) throw new InvalidDataException("Invalid WAV format chunk.");
                format = reader.ReadUInt16();
                channels = reader.ReadUInt16();
                frequency = reader.ReadInt32();
                reader.ReadUInt32();
                alignment = reader.ReadUInt16();
                bits = reader.ReadUInt16();
            }
            else if (id == "data")
                data = reader.ReadBytes(checked((int)size));
            stream.Position = end + (size & 1);
        }
        if (format != 1 || bits != 16 || channels < 1 || channels > 2 ||
            frequency < 8000 || frequency > 96000 || alignment != channels * 2 ||
            data == null || data.Length == 0 || data.Length % alignment != 0 ||
            data.Length / alignment > frequency * 30)
            throw new InvalidDataException("Expected 16-bit PCM mono/stereo WAV, at most 30 seconds.");
        var samples = new float[data.Length / 2];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = (short)(data[i * 2] | (data[i * 2 + 1] << 8)) / 32768f;
        return new AudiRS6RWave { Channels = channels, Frequency = frequency, Samples = samples };
    }

}
