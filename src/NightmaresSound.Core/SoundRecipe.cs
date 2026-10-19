using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace NightmaresSound.Core;

public enum WaveSource { Sine, Triangle, Square, Sawtooth, WhiteNoise, FilteredNoise }

public sealed record LayerSettings
{
    public bool Enabled { get; init; } = true;
    public WaveSource Source { get; init; } = WaveSource.Sine;
    public double Level { get; init; } = 0.6;
    public double StartSeconds { get; init; }
    public double PitchHz { get; init; } = 440;
    public double SweepSemitones { get; init; }
    public double AttackSeconds { get; init; } = 0.005;
    public double DecaySeconds { get; init; } = 0.08;
    public double Sustain { get; init; } = 0.3;
    public double ReleaseSeconds { get; init; } = 0.1;
    public double Gate { get; init; } = 0.65;
    public double Pan { get; init; }
    public double[] Notes { get; init; } = [0];
    public double NoteSeconds { get; init; } = 0.12;
}

public sealed record EffectSettings
{
    public double LowPassHz { get; init; } = 14000;
    public double HighPassHz { get; init; } = 20;
    public double VibratoHz { get; init; } = 5;
    public double VibratoSemitones { get; init; }
    public double TremoloHz { get; init; } = 4;
    public double TremoloDepth { get; init; }
    public double Saturation { get; init; }
    public int BitDepth { get; init; } = 16;
    public double DelaySeconds { get; init; } = 0.12;
    public double DelayMix { get; init; }
    public double DelayFeedback { get; init; } = 0.25;
}

public sealed record SoundRecipe
{
    [JsonRequired] public int Version { get; init; } = 2;
    [JsonRequired] public string CategoryId { get; init; } = "";
    [JsonRequired] public string ConfigurationId { get; init; } = "";
    public string Name { get; init; } = "Untitled";
    public int Seed { get; init; } = 1;
    public double DurationSeconds { get; init; } = 0.6;
    public double PitchSemitones { get; init; }
    public double NoiseAmount { get; init; } = 0.05;
    public double OutputDb { get; init; }
    public bool Loop { get; init; }
    public int SampleRate { get; init; } = 44100;
    public int Channels { get; init; } = 1;
    public bool Normalize { get; init; } = true;
    public InstrumentSettings? Instrument { get; init; }
    [JsonRequired] public LayerSettings[] Layers { get; init; } = [];
    [JsonRequired] public EffectSettings Effects { get; init; } = new();
}

public static class RecipeFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static string Serialize(SoundRecipe recipe)
    {
        RecipeValidator.Validate(recipe);
        return JsonSerializer.Serialize(recipe with { Version = 2 }, Options);
    }

    public static SoundRecipe Deserialize(string json)
    {
        if (json.Length > 256 * 1024) throw new InvalidDataException("Recipe is too large (maximum 256 KB).");
        try
        {
            var node = JsonNode.Parse(json) ?? throw new InvalidDataException("The recipe is empty.");
            if (node is JsonObject root && root["Version"] is JsonValue version && version.TryGetValue<int>(out int number) && number == 1)
            {
                if (root["Instrument"] is JsonObject instrument)
                {
                    if (instrument.ContainsKey("Voices")) throw new InvalidDataException("Version 1 does not support instrument voices.");
                    var voice = new JsonObject();
                    foreach (string key in new[] { "Id", "Style", "Phrase", "Octave" })
                        if (instrument.Remove(key, out var value)) voice[key] = value;
                    instrument["Voices"] = new JsonArray(voice);
                }
                root["Version"] = 2;
            }
            var recipe = node.Deserialize<SoundRecipe>(Options)
                ?? throw new InvalidDataException("The recipe is empty.");
            RecipeValidator.Validate(recipe);
            return recipe;
        }
        catch (JsonException ex) { throw new InvalidDataException("This is not a supported recipe JSON file.", ex); }
    }
}

public static class RecipeValidator
{
    /// <summary>Checks the acoustic coverage of structurally valid instrument settings without throwing.</summary>
    public static string? AcousticRangeError(SoundRecipe recipe)
    {
        if (recipe.Instrument is null || recipe.Effects is null) return null;
        for (int slot = 0; slot < recipe.Instrument.Voices.Length; slot++)
        {
            var voice = recipe.Instrument.Voices[slot];
            if (!voice.Enabled || voice.Style != InstrumentStyle.Acoustic) continue;
            foreach (var note in InstrumentCatalog.Notes(InstrumentCatalog.ForVoice(recipe.Instrument, voice)))
            {
                double pitch = note.MidiNote + recipe.PitchSemitones + voice.TuningSemitones;
                if (!AcousticLibrary.TrySelect(voice.Id, pitch - recipe.Effects.VibratoSemitones, out _)
                    || !AcousticLibrary.TrySelect(voice.Id, pitch + recipe.Effects.VibratoSemitones, out _))
                    return $"Voice {slot + 1} ({InstrumentCatalog.Get(voice.Id).Name}): this acoustic note is outside the bundled sample range. Adjust key, octave, tuning, pitch, or vibrato, or choose Retro.";
            }
        }
        return null;
    }

    public static void Validate(SoundRecipe r)
    {
        ArgumentNullException.ThrowIfNull(r);
        if (r.Version != 2) throw new InvalidDataException($"Recipe version {r.Version} is not supported.");
        var profile = SoundCatalog.Profiles.FirstOrDefault(p => p.Id == r.ConfigurationId);
        if (profile is null || profile.CategoryId != r.CategoryId)
            throw new InvalidDataException("The recipe category or starting configuration is unknown.");
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 120)
            throw new InvalidDataException("Give the recipe a name of 1–120 characters.");
        Range(r.DurationSeconds, 0.03, 30, "Duration");
        Range(r.PitchSemitones, -36, 36, "Master pitch");
        Range(r.NoiseAmount, 0, 1, "Noise amount");
        Range(r.OutputDb, -60, 6, "Output level");
        if (r.Instrument is not null)
        {
            InstrumentCatalog.Validate(r.Instrument);
            if (r.Effects is null) throw new InvalidDataException("Effects settings are missing.");
            if (r.CategoryId != "instruments" || r.ConfigurationId != "instrument-" + r.Instrument.Id)
                throw new InvalidDataException("The instrument must match its starting configuration.");
            if (AcousticRangeError(r) is { } rangeError) throw new InvalidDataException(rangeError);
        }
        else if (r.CategoryId == "instruments") throw new InvalidDataException("Instrument settings are missing.");
        if (r.SampleRate is not (44100 or 48000) || r.Channels is not (1 or 2))
            throw new InvalidDataException("Choose 44.1 or 48 kHz, and mono or stereo.");
        if (r.Layers is null || r.Layers.Length is < 1 or > 3 || r.Layers.Any(l => l is null))
            throw new InvalidDataException("Recipes need 1–3 valid layers.");
        foreach (var l in r.Layers)
        {
            if (!Enum.IsDefined(l.Source)) throw new InvalidDataException("Unknown oscillator source.");
            Range(l.Level, 0, 1, "Layer level");
            Range(l.StartSeconds, 0, 30, "Layer start");
            Range(l.PitchHz, 20, 12000, "Layer pitch");
            Range(l.SweepSemitones, -48, 48, "Pitch sweep");
            Range(l.AttackSeconds, 0, 30, "Attack"); Range(l.DecaySeconds, 0, 30, "Decay");
            Range(l.Sustain, 0, 1, "Sustain"); Range(l.ReleaseSeconds, 0, 30, "Release");
            Range(l.Gate, 0.01, 1, "Gate"); Range(l.Pan, -1, 1, "Pan");
            Range(l.NoteSeconds, 0.03, 2, "Note length");
            if (l.Notes is null || l.Notes.Length is < 1 or > 16)
                throw new InvalidDataException("A sequence needs 1–16 semitone offsets, separated by commas.");
            foreach (var note in l.Notes) Range(note, -48, 48, "Sequence note");
        }
        var e = r.Effects ?? throw new InvalidDataException("Effects settings are missing.");
        Range(e.LowPassHz, 40, 20000, "Low-pass cutoff"); Range(e.HighPassHz, 0, 8000, "High-pass cutoff");
        Range(e.VibratoHz, 0, 20, "Vibrato speed"); Range(e.VibratoSemitones, 0, 2, "Vibrato depth");
        Range(e.TremoloHz, 0, 30, "Tremolo speed"); Range(e.TremoloDepth, 0, 1, "Tremolo depth");
        Range(e.Saturation, 0, 1, "Saturation");
        if (e.BitDepth is < 4 or > 16) throw new InvalidDataException("Bit reduction must be between 4 and 16 bits.");
        Range(e.DelaySeconds, 0.01, 1, "Delay time"); Range(e.DelayMix, 0, 0.8, "Delay mix");
        Range(e.DelayFeedback, 0, 0.75, "Delay feedback");
    }

    private static void Range(double value, double min, double max, string name)
    {
        if (!double.IsFinite(value) || value < min || value > max)
            throw new InvalidDataException($"{name} must be between {min} and {max}.");
    }
}
