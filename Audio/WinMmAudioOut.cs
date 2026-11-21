using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FLLiteStudio.Audio;

/// <summary>
/// Real-time triple-buffered Win32 WinMM stereo PCM audio output driver.
/// Zero managed allocations in the audio render loop.
/// </summary>
public sealed class WinMmAudioOut : IDisposable
{
    public const int SampleRate = 44100;
    private const int BufferCount = 3;
    private const uint CALLBACK_EVENT = 0x00050000;
    private const uint WHDR_DONE = 0x00000001;

    private readonly Action<float[], float[], int> _renderCallback;
    private int _framesPerBuffer;
    private IntPtr _hWaveOut;
    private AutoResetEvent? _event;
    private Thread? _thread;
    private volatile bool _running;
    private readonly object _lifecycleLock = new();

    public double CpuLoadPercent { get; private set; }
    public int BufferFrames => _framesPerBuffer;

    public WinMmAudioOut(Action<float[], float[], int> renderCallback, int framesPerBuffer = 1024)
    {
        _renderCallback = renderCallback;
        _framesPerBuffer = Math.Clamp(framesPerBuffer, 256, 4096);
    }

    public void Start()
    {
        lock (_lifecycleLock)
        {
            if (_running) return;
            _event = new AutoResetEvent(false);

            var fmt = new WAVEFORMATEX
            {
                wFormatTag = 1, // PCM
                nChannels = 2,
                nSamplesPerSec = SampleRate,
                wBitsPerSample = 16,
                nBlockAlign = 4,
                nAvgBytesPerSec = SampleRate * 4,
                cbSize = 0
            };

            uint res = waveOutOpen(
                out _hWaveOut,
                unchecked((uint)-1), // WAVE_MAPPER
                ref fmt,
                _event.SafeWaitHandle.DangerousGetHandle(),
                IntPtr.Zero,
                CALLBACK_EVENT);

            if (res != 0)
            {
                _event.Dispose();
                _event = null;
                throw new InvalidOperationException($"No se pudo abrir el dispositivo de audio Windows (WinMM código {res}).");
            }

            _running = true;
            _thread = new Thread(AudioLoop)
            {
                IsBackground = true,
                Name = "FLLite-AudioThread",
                Priority = ThreadPriority.Highest
            };
            _thread.Start();
        }
    }

    public void RestartWithBufferSize(int newFrames)
    {
        newFrames = Math.Clamp(newFrames, 256, 4096);
        if (newFrames == _framesPerBuffer && _running) return;
        Stop();
        _framesPerBuffer = newFrames;
        Start();
    }

    public void Stop()
    {
        lock (_lifecycleLock)
        {
            if (!_running) return;
            _running = false;
            _event?.Set();
            _thread?.Join(600);
            _thread = null;

            if (_hWaveOut != IntPtr.Zero)
            {
                waveOutReset(_hWaveOut);
                waveOutClose(_hWaveOut);
                _hWaveOut = IntPtr.Zero;
            }
            _event?.Dispose();
            _event = null;
        }
    }

    private void AudioLoop()
    {
        int frames = _framesPerBuffer;
        int samplesInterleaved = frames * 2;
        int bytesPerBuf = samplesInterleaved * sizeof(short);
        double budgetMs = (frames * 1000.0) / SampleRate;

        var left = new float[frames];
        var right = new float[frames];

        var pcmBuffers = new short[BufferCount][];
        var pcmHandles = new GCHandle[BufferCount];
        var hdrPtrs = new IntPtr[BufferCount];
        int hdrSize = Marshal.SizeOf<WAVEHDR>();

        try
        {
            for (int i = 0; i < BufferCount; i++)
            {
                pcmBuffers[i] = new short[samplesInterleaved];
                pcmHandles[i] = GCHandle.Alloc(pcmBuffers[i], GCHandleType.Pinned);
                hdrPtrs[i] = Marshal.AllocHGlobal(hdrSize);

                var hdr = new WAVEHDR
                {
                    lpData = pcmHandles[i].AddrOfPinnedObject(),
                    dwBufferLength = (uint)bytesPerBuf,
                    dwFlags = 0
                };
                Marshal.StructureToPtr(hdr, hdrPtrs[i], false);
                waveOutPrepareHeader(_hWaveOut, hdrPtrs[i], (uint)hdrSize);

                // Prime initial buffers with silence/audio
                FillAndWrite(i, pcmBuffers[i], hdrPtrs[i], left, right, frames, budgetMs);
            }

            var sw = new Stopwatch();
            while (_running)
            {
                _event?.WaitOne(40);
                if (!_running) break;

                for (int i = 0; i < BufferCount; i++)
                {
                    var hdr = Marshal.PtrToStructure<WAVEHDR>(hdrPtrs[i]);
                    if ((hdr.dwFlags & WHDR_DONE) != 0)
                    {
                        sw.Restart();
                        FillAndWrite(i, pcmBuffers[i], hdrPtrs[i], left, right, frames, budgetMs);
                        sw.Stop();
                        double load = Math.Clamp((sw.Elapsed.TotalMilliseconds / budgetMs) * 100.0, 0.5, 99.0);
                        CpuLoadPercent = CpuLoadPercent * 0.85 + load * 0.15;
                    }
                }
            }
        }
        finally
        {
            if (_hWaveOut != IntPtr.Zero)
                waveOutReset(_hWaveOut);

            for (int i = 0; i < BufferCount; i++)
            {
                if (hdrPtrs[i] != IntPtr.Zero)
                {
                    if (_hWaveOut != IntPtr.Zero)
                        waveOutUnprepareHeader(_hWaveOut, hdrPtrs[i], (uint)hdrSize);
                    Marshal.FreeHGlobal(hdrPtrs[i]);
                }
                if (pcmHandles[i].IsAllocated)
                    pcmHandles[i].Free();
            }
        }
    }

    private void FillAndWrite(int idx, short[] dst, IntPtr hdrPtr, float[] left, float[] right, int frames, double _)
    {
        Array.Clear(left, 0, frames);
        Array.Clear(right, 0, frames);
        _renderCallback(left, right, frames);

        for (int i = 0; i < frames; i++)
        {
            float l = Math.Clamp(left[i], -1f, 1f);
            float r = Math.Clamp(right[i], -1f, 1f);
            dst[i * 2] = (short)(l * 32767f);
            dst[i * 2 + 1] = (short)(r * 32767f);
        }

        waveOutWrite(_hWaveOut, hdrPtr, (uint)Marshal.SizeOf<WAVEHDR>());
    }

    public void Dispose() => Stop();

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEFORMATEX
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEHDR
    {
        public IntPtr lpData;
        public uint dwBufferLength;
        public uint dwBytesRecorded;
        public IntPtr dwUser;
        public uint dwFlags;
        public uint dwLoops;
        public IntPtr lpNext;
        public IntPtr reserved;
    }

    [DllImport("winmm.dll")]
    private static extern uint waveOutOpen(out IntPtr hWaveOut, uint uDeviceID, ref WAVEFORMATEX lpFormat, IntPtr dwCallback, IntPtr dwInstance, uint dwFlags);

    [DllImport("winmm.dll")]
    private static extern uint waveOutPrepareHeader(IntPtr hWaveOut, IntPtr lpWaveOutHdr, uint uSize);

    [DllImport("winmm.dll")]
    private static extern uint waveOutUnprepareHeader(IntPtr hWaveOut, IntPtr lpWaveOutHdr, uint uSize);

    [DllImport("winmm.dll")]
    private static extern uint waveOutWrite(IntPtr hWaveOut, IntPtr lpWaveOutHdr, uint uSize);

    [DllImport("winmm.dll")]
    private static extern uint waveOutReset(IntPtr hWaveOut);

    [DllImport("winmm.dll")]
    private static extern uint waveOutClose(IntPtr hWaveOut);
}

/// <summary>
/// Optional Native WinMM USB MIDI Keyboard Input Listener.
/// </summary>
public sealed class WinMmMidiIn : IDisposable
{
    private const uint CALLBACK_FUNCTION = 0x00030000;
    private const uint MIM_DATA = 0x3C3;

    private delegate void MidiInProc(IntPtr hMidiIn, uint wMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2);

    private IntPtr _hMidiIn;
    private MidiInProc? _callbackRef; // Keep delegate alive while open
    public event Action<int, float>? NoteOnReceived;

    public int DeviceCount => (int)midiInGetNumDevs();

    public bool TryOpenFirstDevice()
    {
        Close();
        if (midiInGetNumDevs() == 0) return false;
        _callbackRef = OnMidiMessage;
        uint res = midiInOpen(out _hMidiIn, 0, _callbackRef, IntPtr.Zero, CALLBACK_FUNCTION);
        if (res != 0)
        {
            _hMidiIn = IntPtr.Zero;
            return false;
        }
        midiInStart(_hMidiIn);
        return true;
    }

    private void OnMidiMessage(IntPtr hMidiIn, uint wMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2)
    {
        if (wMsg != MIM_DATA) return;
        uint msg = (uint)dwParam1.ToInt64();
        byte status = (byte)(msg & 0xFF);
        byte note = (byte)((msg >> 8) & 0x7F);
        byte vel = (byte)((msg >> 16) & 0x7F);
        if ((status & 0xF0) == 0x90 && vel > 0)
        {
            NoteOnReceived?.Invoke(note, Math.Clamp(vel / 127f, 0.1f, 1f));
        }
    }

    public void Close()
    {
        if (_hMidiIn != IntPtr.Zero)
        {
            midiInStop(_hMidiIn);
            midiInClose(_hMidiIn);
            _hMidiIn = IntPtr.Zero;
        }
    }

    public void Dispose() => Close();

    [DllImport("winmm.dll")]
    private static extern uint midiInGetNumDevs();

    [DllImport("winmm.dll")]
    private static extern uint midiInOpen(out IntPtr lphMidiIn, uint uDeviceID, MidiInProc dwCallback, IntPtr dwCallbackInstance, uint dwFlags);

    [DllImport("winmm.dll")]
    private static extern uint midiInStart(IntPtr hMidiIn);

    [DllImport("winmm.dll")]
    private static extern uint midiInStop(IntPtr hMidiIn);

    [DllImport("winmm.dll")]
    private static extern uint midiInClose(IntPtr hMidiIn);
}
