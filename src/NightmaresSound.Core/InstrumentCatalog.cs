using System.Text.Json.Serialization;

namespace NightmaresSound.Core;

public enum InstrumentStyle { Retro, Acoustic }
public enum InstrumentMode { SingleNote, Phrase }
public enum MusicalScale { Major, NaturalMinor, Dorian, Mixolydian }
public enum InstrumentPhrase { Ascending, Descending, Strum, Melody, March, Dance }

public sealed record InstrumentVoiceSettings
{
    public bool Enabled { get; init; } = true;
    public string Id { get; init; } = "guitar";
    public InstrumentStyle Style { get; init; }
    public InstrumentPhrase Phrase { get; init; } = InstrumentPhrase.Ascending;
    public int Octave { get; init; } = 4;
    public double TuningSemitones { get; init; }
    public double Level { get; init; } = 1;
    public double Pan { get; init; }
}

public sealed record InstrumentSettings
{
    [JsonRequired] public InstrumentVoiceSettings[] Voices { get; init; } = [new()];
    public InstrumentMode Mode { get; init; }
    public int Key { get; init; } = 2;
    public MusicalScale Scale { get; init; } = MusicalScale.Dorian;
    public int Tempo { get; init; } = 90;

    // Source-compatible access to the first voice; schema 2 persists only Voices.
    [JsonIgnore] public string Id { get => Voices[0].Id; init => Voices = [Voices[0] with { Id = value }, .. Voices.Skip(1)]; }
    [JsonIgnore] public InstrumentStyle Style { get => Voices[0].Style; init => Voices = [Voices[0] with { Style = value }, .. Voices.Skip(1)]; }
    [JsonIgnore] public InstrumentPhrase Phrase { get => Voices[0].Phrase; init => Voices = [Voices[0] with { Phrase = value }, .. Voices.Skip(1)]; }
    [JsonIgnore] public int Octave { get => Voices[0].Octave; init => Voices = [Voices[0] with { Octave = value }, .. Voices.Skip(1)]; }
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
        if (settings.Voices is null || settings.Voices.Length is < 1 or > 3 || settings.Voices.Any(v => v is null))
            throw new InvalidDataException("Instruments need 1–3 valid voices.");
        if (!Enum.IsDefined(settings.Mode) || !Enum.IsDefined(settings.Scale))
            throw new InvalidDataException("Unsupported instrument mode or scale.");
        if (settings.Key is < 0 or > 11 || settings.Tempo is < 40 or > 200)
            throw new InvalidDataException("Choose a key, octave 3–5, and tempo 40–200 BPM.");
        foreach (var voice in settings.Voices)
        {
            var instrument = Get(voice.Id);
            if (!Enum.IsDefined(voice.Style) || !instrument.Phrases.Contains(voice.Phrase))
                throw new InvalidDataException("Unsupported instrument style or phrase.");
            if (voice.Octave is < 3 or > 5 || !double.IsFinite(voice.TuningSemitones) || Math.Abs(voice.TuningSemitones) > 36
                || !double.IsFinite(voice.Level) || voice.Level is < 0 or > 1 || !double.IsFinite(voice.Pan) || Math.Abs(voice.Pan) > 1)
                throw new InvalidDataException("Choose octave 3–5, tuning −36 to 36, level 0–1, and pan −1 to 1.");
            if (voice.Style == InstrumentStyle.Acoustic && !instrument.HasAcoustic)
                throw new InvalidDataException("This instrument has no bundled acoustic recordings. Choose Retro.");
        }
    }
    public static InstrumentSettings ForVoice(InstrumentSettings settings, InstrumentVoiceSettings voice) => settings with { Voices = [voice] };

    public static (double Minimum, double Maximum) AcousticPitchBounds(InstrumentSettings settings, InstrumentVoiceSettings voice)
    {
        var zones = AcousticLibrary.Zones.Where(z => z.InstrumentId == voice.Id).ToArray();
        var notes = Notes(ForVoice(settings, voice));
        return (zones.Min(z => Math.Max(z.MinMidi, z.RootMidi - 4)) - notes.Min(n => n.MidiNote),
            zones.Max(z => Math.Min(z.MaxMidi, z.RootMidi + 4)) - notes.Max(n => n.MidiNote));
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
        return settings.Voices.Where(v => v.Enabled).Select(v =>
        {
            var notes = Notes(ForVoice(settings, v));
            return Math.Max(settings.Mode == InstrumentMode.Phrase ? 8 * 60.0 / settings.Tempo : 0,
                notes.Max(n => n.StartSeconds + n.GateSeconds + Get(v.Id).ReleaseSeconds));
        }).DefaultIfEmpty(.03).Max();
    }
}
