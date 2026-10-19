using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace NightmaresSound.Core;

public sealed record AcousticZone(string InstrumentId, string File, int RootMidi, int MinMidi, int MaxMidi,
    string SourceUrl, string Creator, string License, string LicenseUrl, string LicenseEvidence,
    string SourceSha256, string BundledSha256, string Processing);

internal sealed record AcousticAudio(int Rate, double[] Samples);

public static class AcousticLibrary
{
    private static readonly Lazy<IReadOnlyList<AcousticZone>> Manifest = new(() =>
    {
        using var stream = Open("manifest.json");
        return JsonSerializer.Deserialize<AcousticZone[]>(stream)
            ?? throw new InvalidDataException("The bundled instrument manifest is missing.");
    });
    private static readonly ConcurrentDictionary<string, Lazy<AcousticAudio>> Cache = new();
    public static IReadOnlyList<AcousticZone> Zones => Manifest.Value;
    private static Stream Open(string file) => typeof(AcousticLibrary).Assembly
        .GetManifestResourceStream("NightmaresSound.Core.Samples." + file)
        ?? throw new InvalidDataException($"Bundled instrument resource missing: {file}");

    public static AcousticZone Select(string instrumentId, double midi)
    {
        if (!TrySelect(instrumentId, midi, out var zone))
            throw new InvalidDataException("This acoustic note is outside the bundled sample range. Adjust key, octave, or pitch, or choose Retro.");
        return zone!;
    }

    public static bool TrySelect(string instrumentId, double midi, out AcousticZone? zone)
    {
        zone = Zones.Where(z => z.InstrumentId == instrumentId && midi >= z.MinMidi && midi <= z.MaxMidi)
            .OrderBy(z => Math.Abs(z.RootMidi - midi)).FirstOrDefault();
        if (zone is not null && Math.Abs(zone.RootMidi - midi) <= 4) return true;
        zone = null;
        return false;
    }

    internal static AcousticAudio Load(AcousticZone zone) => Cache.GetOrAdd(zone.File,
        file => new Lazy<AcousticAudio>(() => Read(file))).Value;

    private static AcousticAudio Read(string file)
    {
        using var reader = new BinaryReader(Open(file), Encoding.ASCII);
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF") throw new InvalidDataException("Invalid bundled WAV.");
        reader.ReadInt32();
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE") throw new InvalidDataException("Invalid bundled WAV.");
        int rate = 0;
        while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
        {
            string chunk = Encoding.ASCII.GetString(reader.ReadBytes(4));
            int length = reader.ReadInt32();
            if (length < 0 || reader.BaseStream.Position + length > reader.BaseStream.Length) throw new InvalidDataException("Invalid WAV chunk.");
            long next = reader.BaseStream.Position + length + (length & 1);
            if (chunk == "fmt ")
            {
                if (length < 16 || reader.ReadInt16() != 1 || reader.ReadInt16() != 1) throw new InvalidDataException("Bundled samples must be mono PCM16.");
                rate = reader.ReadInt32(); reader.ReadInt32(); reader.ReadInt16();
                if (reader.ReadInt16() != 16 || rate is < 8000 or > 192000) throw new InvalidDataException("Unsupported bundled sample format.");
            }
            else if (chunk == "data")
            {
                if (rate == 0 || length % 2 != 0) throw new InvalidDataException("Invalid bundled PCM.");
                var values = new double[length / 2];
                for (int i = 0; i < values.Length; i++) values[i] = reader.ReadInt16() / 32768.0;
                return new(rate, values);
            }
            reader.BaseStream.Position = next;
        }
        throw new InvalidDataException("Bundled WAV has no audio.");
    }
}
