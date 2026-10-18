namespace NightmaresSound.Core;

public enum InstrumentStyle { Retro, Acoustic }
public enum InstrumentMode { SingleNote, Phrase }
public enum MusicalScale { Major, NaturalMinor, Dorian, Mixolydian }
public enum InstrumentPhrase { Ascending, Descending, Strum, Melody, March, Dance }

public sealed record InstrumentSettings
{
    public string Id { get; init; } = "guitar";
    public InstrumentStyle Style { get; init; }
    public InstrumentMode Mode { get; init; }
    public InstrumentPhrase Phrase { get; init; } = InstrumentPhrase.Ascending;
    public int Key { get; init; } = 2;
    public MusicalScale Scale { get; init; } = MusicalScale.Dorian;
    public int Octave { get; init; } = 4;
    public int Tempo { get; init; } = 90;
}

public sealed record InstrumentDefinition(string Id, string Name, string Description, double ReleaseSeconds)
{
    public bool IsDrum => Id == "frame-drum";
    public bool IsSustained => Id is "recorder" or "fiddle";
    public bool HasAcoustic => AcousticLibrary.Zones.Any(z => z.InstrumentId == Id);
    public InstrumentStyle[] Styles => HasAcoustic ? [InstrumentStyle.Retro, InstrumentStyle.Acoustic] : [InstrumentStyle.Retro];
    public InstrumentPhrase[] Phrases => IsDrum ? [InstrumentPhrase.March, InstrumentPhrase.Dance]
        : IsSustained ? [InstrumentPhrase.Melody]
        : Id is "guitar" or "lute" ? [InstrumentPhrase.Ascending, InstrumentPhrase.Descending, InstrumentPhrase.Strum]
        : [InstrumentPhrase.Ascending, InstrumentPhrase.Descending];
}

public sealed record InstrumentNote(double StartSeconds, double GateSeconds, int MidiNote, double Velocity);

public static class InstrumentCatalog
{
    public static IReadOnlyList<InstrumentDefinition> All { get; } = new InstrumentDefinition[]
    {
        new("guitar", "Guitar", "Warm plucked strings with a short wooden attack.", 1.2),
        new("lute", "Lute", "Delicate paired strings with a nasal, rounded resonance.", .85),
        new("harp", "Harp", "Soft plucks with a long, clear ringing tail.", 2.2),
        new("dulcimer", "Hammered dulcimer", "Bright hammer strikes and shimmering string overtones.", 1.8),
        new("recorder", "Recorder", "A breathy wooden flute voice for modal melodies.", .18),
        new("fiddle", "Fiddle", "A bowed string voice with a slow attack and gentle movement.", .3),
        new("frame-drum", "Frame drum", "A deep membrane hit with a dry hand transient.", .65)
    };
    public static string[] Keys { get; } = ["C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B"];
    public static InstrumentDefinition Get(string id) => All.FirstOrDefault(i => i.Id == id)
        ?? throw new InvalidDataException("Unknown instrument.");
    public static InstrumentSettings Defaults(string id)
    {
        var instrument = Get(id);
        return new() { Id = id, Phrase = instrument.Phrases[0] };
    }
    public static void Validate(InstrumentSettings settings)
    {
        var instrument = Get(settings.Id);
        if (!Enum.IsDefined(settings.Style) || !Enum.IsDefined(settings.Mode) || !Enum.IsDefined(settings.Scale)
            || !instrument.Phrases.Contains(settings.Phrase)) throw new InvalidDataException("Unsupported instrument style, mode, scale, or phrase.");
        if (settings.Key is < 0 or > 11 || settings.Octave is < 3 or > 5 || settings.Tempo is < 40 or > 200)
            throw new InvalidDataException("Choose a key, octave 3–5, and tempo 40–200 BPM.");
        if (settings.Style == InstrumentStyle.Acoustic && !instrument.HasAcoustic)
            throw new InvalidDataException("This instrument has no bundled acoustic recordings. Choose Retro.");
    }
    public static IReadOnlyList<InstrumentNote> Notes(InstrumentSettings s)
    {
        var instrument = Get(s.Id);
        int root = 12 * (s.Octave + 1) + s.Key;
        double beat = 60.0 / s.Tempo;
        if (s.Mode == InstrumentMode.SingleNote) return [new(0, .65, instrument.IsDrum ? 48 : root, 1)];
        int[] scale = s.Scale switch
        {
            MusicalScale.Major => [0, 2, 4, 5, 7, 9, 11],
            MusicalScale.NaturalMinor => [0, 2, 3, 5, 7, 8, 10],
            MusicalScale.Dorian => [0, 2, 3, 5, 7, 9, 10],
            _ => [0, 2, 4, 5, 7, 9, 10]
        };
        int Degree(int d) => root + scale[d % 7] + 12 * (d / 7);
        var notes = new List<InstrumentNote>();
        if (instrument.IsDrum)
        {
            double[] beats = s.Phrase == InstrumentPhrase.Dance ? [0, 1, 1.5, 2.5, 3, 4, 5, 5.5, 6.5, 7] : [0, 1, 2, 3, 4, 5, 6, 7];
            foreach (double b in beats) notes.Add(new(b * beat, .1, 48, b % 4 == 0 ? 1 : .65));
        }
        else if (s.Phrase == InstrumentPhrase.Strum)
        {
            for (int chord = 0; chord < 4; chord++)
                for (int stringIndex = 0; stringIndex < 4; stringIndex++)
                    notes.Add(new(chord * 2 * beat + stringIndex * .025, beat * 1.5, Degree(new[] { 0, 2, 4, 7 }[stringIndex]), .8));
        }
        else
        {
            int[] degrees = s.Phrase switch
            {
                InstrumentPhrase.Descending => [7, 4, 2, 0, 7, 4, 2, 0],
                InstrumentPhrase.Melody => [0, 2, 3, 4, 5, 1, 6, 0],
                _ => [0, 2, 4, 7, 0, 2, 4, 7]
            };
            for (int n = 0; n < degrees.Length; n++) notes.Add(new(n * beat, beat * .8, Degree(degrees[n]), n % 4 == 0 ? 1 : .8));
        }
        return notes;
    }
    public static double RequiredDuration(InstrumentSettings settings)
    {
        var notes = Notes(settings);
        return Math.Max(settings.Mode == InstrumentMode.Phrase ? 8 * 60.0 / settings.Tempo : 0,
            notes.Max(n => n.StartSeconds + n.GateSeconds + Get(settings.Id).ReleaseSeconds));
    }
}
