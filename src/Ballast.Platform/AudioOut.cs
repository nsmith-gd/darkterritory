using SDL;
using static SDL.SDL3;

namespace Ballast.Platform;

/// <summary>
/// The default playback device through an SDL3 audio stream: 48 kHz stereo float, fed by pushing blocks
/// from the main loop. No device (a headless box, CI) just means <see cref="Open"/> returns null and
/// the game runs silent.
/// </summary>
public sealed unsafe class AudioOut : IDisposable
{
    readonly SDL_AudioStream* _stream;

    AudioOut(SDL_AudioStream* stream, int sampleRate)
    {
        _stream = stream;
        SampleRate = sampleRate;
    }

    public int SampleRate { get; }

    public static AudioOut? Open(int sampleRate, out string? error)
    {
        error = null;
        if (!SDL_InitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO))
        {
            error = SDL_GetError();
            return null;
        }
        var spec = new SDL_AudioSpec { format = SDL_AudioFormat.SDL_AUDIO_F32LE, channels = 2, freq = sampleRate };
        var stream = SDL_OpenAudioDeviceStream(SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, &spec, null, IntPtr.Zero);
        if (stream is null)
        {
            error = SDL_GetError();
            return null;
        }
        SDL_ResumeAudioStreamDevice(stream);
        return new AudioOut(stream, sampleRate);
    }

    /// <summary>Seconds of audio queued and not yet played.</summary>
    public double QueuedSeconds => SDL_GetAudioStreamQueued(_stream) / (double)(SampleRate * 2 * sizeof(float));

    public void Write(ReadOnlySpan<float> interleaved)
    {
        fixed (float* p = interleaved)
            SDL_PutAudioStreamData(_stream, (IntPtr)p, interleaved.Length * sizeof(float));
    }

    public void Dispose() => SDL_DestroyAudioStream(_stream);
}

/// <summary>
/// A recording device (the microphone: the default, or the one the settings name) as 48 kHz mono float, read by polling. As with output, no
/// device just means <see cref="Open"/> returns null: you can still hear everyone.
/// </summary>
public sealed unsafe class AudioIn : IDisposable
{
    readonly SDL_AudioStream* _stream;

    AudioIn(SDL_AudioStream* stream) => _stream = stream;

    /// <summary>The microphones there are, by name (none without audio): the settings screen's MICROPHONE.</summary>
    public static IReadOnlyList<string> Devices()
    {
        if (!SDL_InitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO))
            return [];
        using var ids = SDL_GetAudioRecordingDevices();
        var names = new List<string>();
        for (int i = 0; ids is not null && i < ids.Count; i++)
            if (SDL_GetAudioDeviceName(ids[i]) is { Length: > 0 } name)
                names.Add(name);
        return names;
    }

    /// <summary>The microphone called <paramref name="device"/>, or the default one (no name, or none of that name now).</summary>
    public static AudioIn? Open(int sampleRate, out string? error, string? device = null)
    {
        error = null;
        if (!SDL_InitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO))
        {
            error = SDL_GetError();
            return null;
        }
        var id = SDL_AUDIO_DEVICE_DEFAULT_RECORDING;
        if (!string.IsNullOrEmpty(device))
        {
            using var ids = SDL_GetAudioRecordingDevices();
            for (int i = 0; ids is not null && i < ids.Count; i++)
                if (SDL_GetAudioDeviceName(ids[i]) == device)
                    id = ids[i];
        }
        var spec = new SDL_AudioSpec { format = SDL_AudioFormat.SDL_AUDIO_F32LE, channels = 1, freq = sampleRate };
        var stream = SDL_OpenAudioDeviceStream(id, &spec, null, IntPtr.Zero);
        if (stream is null)
        {
            error = SDL_GetError();
            return null;
        }
        SDL_ResumeAudioStreamDevice(stream);
        return new AudioIn(stream);
    }

    /// <summary>Reads whatever has been captured, up to the buffer's size; returns the sample count.</summary>
    public int Read(Span<float> into)
    {
        fixed (float* p = into)
        {
            int bytes = SDL_GetAudioStreamData(_stream, (IntPtr)p, into.Length * sizeof(float));
            return Math.Max(0, bytes) / sizeof(float);
        }
    }

    public void Dispose() => SDL_DestroyAudioStream(_stream);
}
