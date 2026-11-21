using System.IO;
using System.Text;
using System.Text.Json;

namespace FLLiteStudio.Core;

public static class ProjectIO
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static void SaveProject(ProjectModel project, string filePath)
    {
        string json = JsonSerializer.Serialize(project, JsonOpts);
        File.WriteAllText(filePath, json, Encoding.UTF8);
    }

    public static ProjectModel LoadProject(string filePath)
    {
        string json = File.ReadAllText(filePath, Encoding.UTF8);
        var proj = JsonSerializer.Deserialize<ProjectModel>(json, JsonOpts)
            ?? throw new InvalidDataException("Archivo de proyecto vacío o inválido.");
        proj.EnsureIntegrity();
        return proj;
    }

    public static string SerializeSnapshot(ProjectModel project) => JsonSerializer.Serialize(project, JsonOpts);

    public static ProjectModel? DeserializeSnapshot(string json)
    {
        var proj = JsonSerializer.Deserialize<ProjectModel>(json, JsonOpts);
        proj?.EnsureIntegrity();
        return proj;
    }

    // ========================================================================
    // Native WAV Loader (16/24/32-bit PCM & 32-bit IEEE Float, Mono/Stereo)
    // ========================================================================
    public static LoadedSample LoadWavFile(string filePath)
    {
        using var fs = File.OpenRead(filePath);
        using var br = new BinaryReader(fs);

        string riff = new(br.ReadChars(4));
        if (riff != "RIFF") throw new InvalidDataException("El archivo no es un WAV válido (falta cabecera RIFF).");
        br.ReadInt32();
        string wave = new(br.ReadChars(4));
        if (wave != "WAVE") throw new InvalidDataException("El archivo no es un WAV válido (falta cabecera WAVE).");

        ushort audioFormat = 1;
        ushort numChannels = 1;
        int sampleRate = 44100;
        ushort bitsPerSample = 16;
        byte[]? rawData = null;

        while (fs.Position + 8 <= fs.Length)
        {
            string chunkId = new(br.ReadChars(4));
            int chunkSize = br.ReadInt32();
            long nextPos = fs.Position + chunkSize + (chunkSize & 1);

            if (chunkId == "fmt ")
            {
                audioFormat = br.ReadUInt16();
                numChannels = br.ReadUInt16();
                sampleRate = br.ReadInt32();
                br.ReadInt32(); // byteRate
                br.ReadUInt16(); // blockAlign
                bitsPerSample = br.ReadUInt16();
            }
            else if (chunkId == "data")
            {
                rawData = br.ReadBytes(chunkSize);
            }

            if (nextPos > fs.Length) break;
            fs.Position = nextPos;
        }

        if (rawData == null || numChannels == 0)
            throw new InvalidDataException("El archivo WAV no contiene datos de audio.");

        int bytesPerSample = Math.Max(1, bitsPerSample / 8);
        int frameCount = rawData.Length / (numChannels * bytesPerSample);
        var left = new float[frameCount];
        var right = new float[frameCount];

        int pos = 0;
        for (int i = 0; i < frameCount; i++)
        {
            float sL = ReadSample(rawData, ref pos, audioFormat, bitsPerSample);
            float sR = numChannels >= 2 ? ReadSample(rawData, ref pos, audioFormat, bitsPerSample) : sL;
            for (int extra = 2; extra < numChannels; extra++)
                ReadSample(rawData, ref pos, audioFormat, bitsPerSample);

            left[i] = sL;
            right[i] = sR;
        }

        var peaks = ComputePeaks(left, 200);
        return new LoadedSample
        {
            Name = Path.GetFileName(filePath),
            SampleRate = sampleRate,
            Left = left,
            Right = right,
            Peaks = peaks
        };
    }

    private static float ReadSample(byte[] buf, ref int pos, ushort fmt, ushort bits)
    {
        if (fmt == 3 && bits == 32 && pos + 4 <= buf.Length)
        {
            float v = BitConverter.ToSingle(buf, pos);
            pos += 4;
            return Math.Clamp(v, -1f, 1f);
        }
        if (bits == 16 && pos + 2 <= buf.Length)
        {
            short v = BitConverter.ToInt16(buf, pos);
            pos += 2;
            return v / 32768f;
        }
        if (bits == 24 && pos + 3 <= buf.Length)
        {
            int v = buf[pos] | (buf[pos + 1] << 8) | (buf[pos + 2] << 16);
            if ((v & 0x800000) != 0) v |= unchecked((int)0xFF000000);
            pos += 3;
            return v / 8388608f;
        }
        if (bits == 32 && pos + 4 <= buf.Length)
        {
            int v = BitConverter.ToInt32(buf, pos);
            pos += 4;
            return v / 2147483648f;
        }
        if (bits == 8 && pos + 1 <= buf.Length)
        {
            byte b = buf[pos++];
            return (b - 128) / 128f;
        }
        pos += Math.Max(1, bits / 8);
        return 0f;
    }

    private static float[] ComputePeaks(float[] data, int buckets)
    {
        var peaks = new float[buckets];
        if (data.Length == 0) return peaks;
        int step = Math.Max(1, data.Length / buckets);
        for (int b = 0; b < buckets; b++)
        {
            int start = b * step;
            int end = Math.Min(data.Length, start + step);
            float max = 0f;
            int stride = Math.Max(1, step / 16);
            for (int i = start; i < end; i += stride)
            {
                float a = MathF.Abs(data[i]);
                if (a > max) max = a;
            }
            peaks[b] = max;
        }
        return peaks;
    }

    // ========================================================================
    // Native Stereo WAV Exporter (16-bit & 24-bit PCM)
    // ========================================================================
    public static void WriteWavFile(string filePath, float[] left, float[] right, int sampleRate, int bitDepth = 16)
    {
        int frames = Math.Min(left.Length, right.Length);
        int bytesPerSample = bitDepth == 24 ? 3 : 2;
        int blockAlign = 2 * bytesPerSample;
        int dataSize = frames * blockAlign;

        using var fs = File.Create(filePath);
        using var bw = new BinaryWriter(fs);

        bw.Write(Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + dataSize);
        bw.Write(Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);
        bw.Write((ushort)1); // PCM
        bw.Write((ushort)2); // Stereo
        bw.Write(sampleRate);
        bw.Write(sampleRate * blockAlign);
        bw.Write((ushort)blockAlign);
        bw.Write((ushort)bitDepth);
        bw.Write(Encoding.ASCII.GetBytes("data"));
        bw.Write(dataSize);

        if (bitDepth == 24)
        {
            for (int i = 0; i < frames; i++)
            {
                Write24(bw, left[i]);
                Write24(bw, right[i]);
            }
        }
        else
        {
            for (int i = 0; i < frames; i++)
            {
                short sL = (short)Math.Clamp(left[i] * 32767f, -32768f, 32767f);
                short sR = (short)Math.Clamp(right[i] * 32767f, -32768f, 32767f);
                bw.Write(sL);
                bw.Write(sR);
            }
        }
    }

    private static void Write24(BinaryWriter bw, float sample)
    {
        int val = (int)Math.Clamp(sample * 8388607f, -8388608f, 8388607f);
        bw.Write((byte)(val & 0xFF));
        bw.Write((byte)((val >> 8) & 0xFF));
        bw.Write((byte)((val >> 16) & 0xFF));
    }

    // ========================================================================
    // Standard MIDI File (.mid) Type-1 Exporter
    // ========================================================================
    public static void ExportPatternMidi(string filePath, ProjectModel project, PatternModel pattern)
    {
        const int ppq = 480;
        const int ticksPerStep = ppq / 4;

        var tracks = new List<byte[]>();

        // Meta Tempo Track
        int usPerBeat = (int)Math.Round(60_000_000.0 / Math.Max(40, project.Bpm));
        tracks.Add(MakeTrackChunk(new byte[]
        {
            0x00, 0xFF, 0x51, 0x03,
            (byte)((usPerBeat >> 16) & 0xFF),
            (byte)((usPerBeat >> 8) & 0xFF),
            (byte)(usPerBeat & 0xFF),
            0x00, 0xFF, 0x2F, 0x00
        }));

        for (int idx = 0; idx < project.Channels.Count; idx++)
        {
            var ch = project.Channels[idx];
            if (!pattern.Channels.TryGetValue(ch.Id, out var cd)) continue;

            var events = new List<(int tick, byte[] data)>();
            int midiCh = ch.Type == InstrumentType.DrumSynth ? 9 : (idx % 15);

            for (int s = 0; s < Math.Min(pattern.LengthSteps, cd.Steps.Length); s++)
            {
                if (!cd.Steps[s]) continue;
                int t = s * ticksPerStep;
                byte vel = (byte)Math.Clamp((int)(cd.StepVelocity[s] * 127f), 1, 127);
                events.Add((t, new byte[] { (byte)(0x90 | midiCh), 60, vel }));
                events.Add((t + (int)(ticksPerStep * 0.9), new byte[] { (byte)(0x80 | midiCh), 60, 0 }));
            }

            foreach (var n in cd.Notes)
            {
                int t0 = (int)Math.Round(n.StartStep * ticksPerStep);
                int t1 = (int)Math.Round((n.StartStep + Math.Max(0.125, n.DurationSteps)) * ticksPerStep);
                byte vel = (byte)Math.Clamp((int)(n.Velocity * 127f), 1, 127);
                byte p = (byte)Math.Clamp(n.Pitch, 0, 127);
                events.Add((t0, new byte[] { (byte)(0x90 | midiCh), p, vel }));
                events.Add((t1, new byte[] { (byte)(0x80 | midiCh), p, 0 }));
            }

            if (events.Count == 0) continue;
            events.Sort((a, b) => a.tick.CompareTo(b.tick));

            var bytes = new List<byte>();
            byte[] nameBytes = Encoding.ASCII.GetBytes(ch.Name);
            bytes.Add(0x00); bytes.Add(0xFF); bytes.Add(0x03); bytes.Add((byte)Math.Min(127, nameBytes.Length));
            bytes.AddRange(nameBytes.Take(127));

            int prev = 0;
            foreach (var (tick, data) in events)
            {
                WriteVlq(bytes, tick - prev);
                bytes.AddRange(data);
                prev = tick;
            }
            bytes.AddRange(new byte[] { 0x00, 0xFF, 0x2F, 0x00 });
            tracks.Add(MakeTrackChunk(bytes.ToArray()));
        }

        using var fs = File.Create(filePath);
        using var bw = new BinaryWriter(fs);
        bw.Write(Encoding.ASCII.GetBytes("MThd"));
        WriteBE32(bw, 6);
        WriteBE16(bw, 1);
        WriteBE16(bw, (ushort)tracks.Count);
        WriteBE16(bw, ppq);
        foreach (var tr in tracks) bw.Write(tr);
    }

    private static byte[] MakeTrackChunk(byte[] body)
    {
        var res = new byte[8 + body.Length];
        res[0] = (byte)'M'; res[1] = (byte)'T'; res[2] = (byte)'r'; res[3] = (byte)'k';
        res[4] = (byte)((body.Length >> 24) & 0xFF);
        res[5] = (byte)((body.Length >> 16) & 0xFF);
        res[6] = (byte)((body.Length >> 8) & 0xFF);
        res[7] = (byte)(body.Length & 0xFF);
        Buffer.BlockCopy(body, 0, res, 8, body.Length);
        return res;
    }

    private static void WriteVlq(List<byte> dst, int value)
    {
        int v = Math.Max(0, value);
        var stack = new Stack<byte>();
        stack.Push((byte)(v & 0x7F));
        while ((v >>= 7) > 0) stack.Push((byte)((v & 0x7F) | 0x80));
        while (stack.Count > 0) dst.Add(stack.Pop());
    }

    private static void WriteBE32(BinaryWriter bw, int v)
    {
        bw.Write((byte)((v >> 24) & 0xFF));
        bw.Write((byte)((v >> 16) & 0xFF));
        bw.Write((byte)((v >> 8) & 0xFF));
        bw.Write((byte)(v & 0xFF));
    }

    private static void WriteBE16(BinaryWriter bw, ushort v)
    {
        bw.Write((byte)((v >> 8) & 0xFF));
        bw.Write((byte)(v & 0xFF));
    }
}
