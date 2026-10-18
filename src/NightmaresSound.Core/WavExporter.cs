using System.Text;

namespace NightmaresSound.Core;

public static class WavExporter
{
    public static void Write(Stream destination, RenderedSound sound)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(sound);
        if (!destination.CanWrite) throw new ArgumentException("The output stream is not writable.", nameof(destination));
        using var writer = new BinaryWriter(destination, Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF"u8); writer.Write(36 + sound.Pcm16.Length); writer.Write("WAVE"u8);
        writer.Write("fmt "u8); writer.Write(16); writer.Write((short)1);
        writer.Write((short)sound.Channels); writer.Write(sound.SampleRate);
        writer.Write(sound.SampleRate * sound.Channels * 2);
        writer.Write((short)(sound.Channels * 2)); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(sound.Pcm16.Length); writer.Write(sound.Pcm16);
    }

    public static void Save(string path, RenderedSound sound) => AtomicFile.Write(path, stream => Write(stream, sound));
}

public static class AtomicFile
{
    /// <summary>Replace the target only after the complete temporary file has been written.</summary>
    public static void Write(string path, Action<Stream> write)
    {
        string fullPath = Path.GetFullPath(path);
        string temp = Path.Combine(Path.GetDirectoryName(fullPath)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(stream);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
