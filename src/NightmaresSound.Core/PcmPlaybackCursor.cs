namespace NightmaresSound.Core;

/// <summary>Streams the exported bytes, wrapping without inserting padding between loop cycles.</summary>
public sealed class PcmPlaybackCursor(RenderedSound sound, bool loop)
{
    private int position;
    public int Read(Span<byte> destination)
    {
        int count = destination.Length - destination.Length % (2 * sound.Channels);
        int written = 0;
        while (written < count && sound.Pcm16.Length > 0)
        {
            if (position == sound.Pcm16.Length)
            {
                if (!loop) break;
                position = 0;
            }
            int size = Math.Min(count - written, sound.Pcm16.Length - position);
            sound.Pcm16.AsSpan(position, size).CopyTo(destination[written..]);
            written += size; position += size;
        }
        return written;
    }
}
