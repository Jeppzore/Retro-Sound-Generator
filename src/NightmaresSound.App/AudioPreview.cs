using NAudio.Wave;
using NightmaresSound.Core;

namespace NightmaresSound.App;

public sealed class PcmLoopProvider(RenderedSound sound, bool loop) : IWaveProvider
{
    private readonly PcmPlaybackCursor cursor = new(sound, loop);
    public WaveFormat WaveFormat { get; } = new(sound.SampleRate, 16, sound.Channels);
    public int Read(Span<byte> buffer) => cursor.Read(buffer);
    public int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
}

public interface IAudioPreview : IDisposable
{
    event Action<string?>? Stopped;
    void Play(RenderedSound sound, bool loop);
    void Stop();
}

public sealed class AudioPreview : IAudioPreview
{
    private IWavePlayer? player;
    public event Action<string?>? Stopped;

    public void Play(RenderedSound sound, bool loop)
    {
        Stop();
        IWavePlayer? device = null;
        try
        {
            device = new WasapiPlayerBuilder().Build();
            player = device;
            device.PlaybackStopped += (_, args) =>
            {
                if (!ReferenceEquals(player, device)) return;
                player = null;
                string? message = args.Exception?.Message;
                try { device.Dispose(); }
                catch (Exception ex) { message ??= ex.Message; }
                Stopped?.Invoke(message);
            };
            device.Init(new PcmLoopProvider(sound, loop));
            device.Play();
        }
        catch
        {
            player = null;
            device?.Dispose();
            throw;
        }
    }

    public void Stop()
    {
        var device = player;
        player = null;
        if (device is null) return;
        try { device.Stop(); }
        finally { device.Dispose(); }
    }

    public void Dispose() => Stop();
}
