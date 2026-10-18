namespace NightmaresSound.Core;

public sealed record SoundCategory(string Id, string Name);
public sealed record SoundProfile(string Id, string CategoryId, string Name, string Description,
    SoundRecipe Default, double DurationVariation, double PitchVariation);

public static class SoundCatalog
{
    public static IReadOnlyList<SoundCategory> Categories { get; } = new SoundCategory[]
    {
        new("loot", "Loot and rewards"), new("combat", "Combat feedback"),
        new("power", "Power attacks and boons"), new("enemy", "Enemy telegraphs"),
        new("corruption", "Corruption and run phases"), new("weather", "Weather and environment"),
        new("teleport", "Teleporters and traversal"), new("quest", "NPCs, dialogue and quests"),
        new("profession", "Profession audio"), new("trade", "Trading and crafting"),
        new("inventory", "Inventory and equipment"), new("ui", "UI and menus"),
        new("world", "World interactions")
    };

    public static IReadOnlyList<SoundProfile> Profiles { get; } = BuildProfiles();

    public static SoundRecipe Create(string id)
    {
        var recipe = Profiles.First(p => p.Id == id).Default;
        return recipe with { Layers = recipe.Layers.Select(l => l with { Notes = [.. l.Notes] }).ToArray() };
    }

    private static IReadOnlyList<SoundProfile> BuildProfiles()
    {
        var items = new List<SoundProfile>();
        LayerSettings Tone(double hz, WaveSource source = WaveSource.Sine, double level = .65) =>
            new() { PitchHz = hz, Source = source, Level = level };
        LayerSettings Noise(double level = .6) => Tone(150, WaveSource.FilteredNoise, level);
        void Add(string category, string id, string name, string description, double duration, double noise,
            double cutoff, LayerSettings[] layers, EffectSettings? effects = null, bool loop = false,
            double durationVariation = .35, double pitchVariation = 5)
        {
            var padded = layers.Concat(Enumerable.Repeat(new LayerSettings { Enabled = false, Level = .3 }, 3 - layers.Length)).ToArray();
            var recipe = new SoundRecipe
            {
                CategoryId = category, ConfigurationId = id, Name = name, DurationSeconds = duration,
                NoiseAmount = noise, Loop = loop, Layers = padded,
                Effects = (effects ?? new()) with { LowPassHz = cutoff }
            };
            items.Add(new(id, category, name, description, recipe, durationVariation, pitchVariation));
        }

        Add("loot", "clink", "Material clink", "A short physical drop with bright, inharmonic overtones.", .38, .2, 10000,
            [Tone(1300) with { Sustain = 0, ReleaseSeconds = .16 }, Tone(2137, level: .28) with { Sustain = 0, DecaySeconds = .04 }, Noise(.25) with { Gate = .12 }]);
        Add("loot", "shimmer", "Rare reward shimmer", "An ascending crystalline signature for valuable drops.", 1.3, .03, 16000,
            [Tone(660) with { Notes = [0, 4, 7, 12], NoteSeconds = .16, ReleaseSeconds = .25 }, Tone(1320, level: .18) with { StartSeconds = .14, Pan = .4 }],
            new() { DelayMix = .3, DelaySeconds = .19 });
        Add("combat", "whoosh", "Weapon whoosh", "Filtered air and a falling tone for swings and evades.", .32, .9, 3500,
            [Noise() with { AttackSeconds = .035, SweepSemitones = -18, Sustain = .1, Gate = .4 }, Tone(190, WaveSource.Triangle, .25) with { SweepSemitones = -20 }]);
        Add("combat", "impact", "Layered impact", "A low body, sharp transient, and noisy debris.", .5, .65, 6500,
            [Tone(135) with { SweepSemitones = -24, Sustain = 0, Gate = .3 }, Noise(.7) with { Gate = .16, ReleaseSeconds = .09 }, Tone(490, WaveSource.Square, .13) with { StartSeconds = .018, Gate = .16 }],
            new() { Saturation = .4 });
        Add("power", "electric", "Electric burst", "A bright jagged strike with repeating arcs.", .8, .6, 12000,
            [Tone(780, WaveSource.Sawtooth) with { SweepSemitones = -16, Notes = [0, 7, -5], NoteSeconds = .08 }, Noise(.4) with { Gate = .25 }],
            new() { Saturation = .5, DelayMix = .22, DelaySeconds = .07, TremoloDepth = .6, TremoloHz = 24 });
        Add("power", "charge", "Charge and release", "A rising power build followed by a delayed heavy burst.", 1.6, .3, 9000,
            [Tone(130, WaveSource.Triangle) with { AttackSeconds = .4, SweepSemitones = 24, Gate = .5 }, Noise(.8) with { StartSeconds = .85, Gate = .25 }, Tone(95, level: .7) with { StartSeconds = .85, SweepSemitones = -18, Gate = .3 }],
            new() { Saturation = .25, DelayMix = .18 });
        Add("enemy", "warning", "Warning pulse", "Two tense pulses for aggro and dangerous attacks.", .65, .08, 6000,
            [Tone(390, WaveSource.Square, .5) with { Notes = [0, 1], NoteSeconds = .2, Gate = .5, Sustain = .4 }],
            new() { Saturation = .15 }, pitchVariation: 3);
        Add("enemy", "spell-charge", "Rising spell charge", "An unstable rising telegraph before a spell fires.", 1.1, .25, 7000,
            [Tone(240, WaveSource.Sawtooth, .5) with { AttackSeconds = .22, SweepSemitones = 19, Sustain = .65 }, Noise(.2)],
            new() { VibratoSemitones = .4, VibratoHz = 9, TremoloDepth = .2 });
        Add("corruption", "ominous", "Ominous transition", "A low dissonant descent for escalating danger.", 2.5, .3, 2200,
            [Tone(110, WaveSource.Sawtooth, .5) with { AttackSeconds = .2, SweepSemitones = -12, ReleaseSeconds = .7 }, Tone(116.54, WaveSource.Triangle, .4) with { StartSeconds = .04, Pan = -.4 }, Noise(.2)],
            new() { Saturation = .2, DelayMix = .25, DelaySeconds = .32 }, pitchVariation: 3);
        Add("corruption", "cleanse", "Cleansing resolution", "A soft lift and bright release for liberation or cleansing.", 2.2, .06, 15000,
            [Tone(440) with { Notes = [0, 7, 12, 16], NoteSeconds = .24, AttackSeconds = .04, ReleaseSeconds = .5 }, Tone(220, WaveSource.Triangle, .2) with { AttackSeconds = .2, ReleaseSeconds = .5 }],
            new() { DelayMix = .3, DelaySeconds = .27 });
        Add("weather", "rain", "Rain texture loop", "A continuous filtered noise bed with slow movement.", 6, .95, 7000,
            [Noise(.75), Tone(4000, WaveSource.WhiteNoise, .15) with { Pan = .6 }],
            new() { HighPassHz = 500, TremoloHz = .7, TremoloDepth = .12 }, true, .25, 2);
        Add("weather", "drone", "Dark drone loop", "A low detuned atmosphere for caves and corrupted areas.", 8, .15, 1600,
            [Tone(55, WaveSource.Triangle, .5) with { Pan = -.3 }, Tone(56.2, WaveSource.Sine, .45) with { Pan = .3 }, Noise(.16)],
            new() { VibratoHz = .2, VibratoSemitones = .12, DelayMix = .25, DelaySeconds = .4 }, true, .25, 3);
        Add("teleport", "teleport-idle", "Teleporter idle loop", "A repeating spectral hum around an active teleporter.", 4, .08, 9000,
            [Tone(220, WaveSource.Triangle, .45), Tone(660, level: .25) with { Pan = .5 }],
            new() { TremoloHz = 1.5, TremoloDepth = .35, VibratoHz = .8, VibratoSemitones = .2, DelayMix = .2 }, true);
        Add("teleport", "displace", "Displacement and arrival", "A rising sweep that resolves into a soft landing.", 1.4, .45, 10000,
            [Tone(130, WaveSource.Sawtooth, .45) with { SweepSemitones = 36, AttackSeconds = .1, Gate = .45 }, Noise(.4) with { Gate = .4 }, Tone(330) with { StartSeconds = .7, Notes = [0, 7], NoteSeconds = .16, ReleaseSeconds = .3 }],
            new() { DelayMix = .25 });
        Add("quest", "dialogue", "Dialogue tick", "A restrained short tone for advancing text.", .09, .03, 5500,
            [Tone(840, WaveSource.Triangle, .45) with { Gate = .2, AttackSeconds = .002, DecaySeconds = .015, ReleaseSeconds = .02 }], durationVariation: .2, pitchVariation: 3);
        Add("quest", "quest-complete", "Quest completion", "An uplifting three-note progression cue.", 1.5, .02, 12000,
            [Tone(523.25, WaveSource.Triangle) with { Notes = [0, 4, 7, 12], NoteSeconds = .21, ReleaseSeconds = .3 }, Tone(261.63, level: .22) with { StartSeconds = .42 }],
            new() { DelayMix = .2, DelaySeconds = .23 });
        Add("profession", "forge", "Forge strike", "A heavy metal strike with a ringing upper layer.", .9, .45, 10000,
            [Tone(320, WaveSource.Triangle) with { Sustain = 0, ReleaseSeconds = .35 }, Tone(857, level: .35) with { Sustain = 0, ReleaseSeconds = .4, Pan = .3 }, Noise(.6) with { Gate = .07 }],
            new() { Saturation = .3, DelayMix = .15, DelaySeconds = .08 });
        Add("profession", "bubbles", "Bubbling loop", "Rounded pitch pulses and a soft liquid-like texture.", 4, .12, 3000,
            [Tone(200) with { Notes = [0, 12, -5, 7, 3, 15], NoteSeconds = .17, SweepSemitones = 12 }, Noise(.15)],
            new() { TremoloHz = 6, TremoloDepth = .8, DelayMix = .18 }, true);
        Add("trade", "transaction", "Transaction chime", "A small two-note confirmation for buying and selling.", .45, .02, 11000,
            [Tone(880) with { Notes = [0, 7], NoteSeconds = .11, Sustain = .15, ReleaseSeconds = .12 }]);
        Add("trade", "craft-reveal", "Crafting reveal", "A physical tap followed by a magical result.", 1.2, .25, 9000,
            [Noise(.5) with { Gate = .1 }, Tone(330, WaveSource.Triangle) with { StartSeconds = .15, Notes = [0, 4, 12], NoteSeconds = .18, ReleaseSeconds = .3 }],
            new() { DelayMix = .22 });
        Add("inventory", "equipment", "Equipment movement", "A rustle and muted clink for equipping an item.", .3, .7, 4300,
            [Noise(.6) with { AttackSeconds = .015, Gate = .25 }, Tone(720, WaveSource.Triangle, .22) with { StartSeconds = .04, Sustain = 0, Gate = .3 }]);
        Add("inventory", "slot", "Slot click", "A tiny tactile tick for item movement and hotkeys.", .07, .25, 7000,
            [Tone(1150, WaveSource.Triangle, .4) with { SweepSemitones = -8, ReleaseSeconds = .02 }, Noise(.2) with { Gate = .1 }], durationVariation: .2, pitchVariation: 3);
        Add("ui", "confirm", "Confirmation", "A clean ascending pair of notes.", .28, .01, 10000,
            [Tone(660, WaveSource.Triangle, .5) with { Notes = [0, 7], NoteSeconds = .08, ReleaseSeconds = .07 }], durationVariation: .2, pitchVariation: 3);
        Add("ui", "reject", "Rejection", "A muted descending pair for denied actions.", .3, .05, 3200,
            [Tone(330, WaveSource.Square, .35) with { Notes = [0, -4], NoteSeconds = .09, ReleaseSeconds = .07 }], durationVariation: .2, pitchVariation: 3);
        Add("world", "chest", "Chest opening", "A latch, wooden movement, and delayed reveal tone.", 1.3, .5, 6000,
            [Noise(.5) with { Gate = .1 }, Tone(100, WaveSource.Triangle, .4) with { StartSeconds = .15, SweepSemitones = 8, Gate = .35 }, Tone(660) with { StartSeconds = .55, Notes = [0, 7, 12], NoteSeconds = .14, ReleaseSeconds = .2 }],
            new() { DelayMix = .12 });
        Add("world", "mineral", "Mineral break", "A crunchy fracture with scattered mineral overtones.", .65, .8, 8500,
            [Noise(.8) with { Gate = .2 }, Tone(900, WaveSource.Triangle, .3) with { Notes = [0, -7, 5], NoteSeconds = .07, Sustain = 0 }, Tone(1567, level: .2) with { StartSeconds = .08, Sustain = 0 }],
            new() { Saturation = .3, DelayMix = .18, DelaySeconds = .065 });
        return items.AsReadOnly();
    }
}

/// <summary>Explicit PRNG for reproducible recipes across runtime versions.</summary>
internal struct StableRandom(int seed)
{
    private uint state = unchecked((uint)seed) ^ 0x9E3779B9u;
    public double Next()
    {
        state += 0x6D2B79F5u;
        uint value = state;
        value = (value ^ (value >> 15)) * (value | 1);
        value ^= value + (value ^ (value >> 7)) * (value | 61);
        return ((value ^ (value >> 14)) >> 8) / 16777216.0;
    }
    public double Between(double min, double max) => min + Next() * (max - min);
}

public static class RecipeRandomizer
{
    public static SoundRecipe Randomize(SoundRecipe current, int seed)
    {
        RecipeValidator.Validate(current);
        var profile = SoundCatalog.Profiles.First(p => p.Id == current.ConfigurationId);
        var basis = profile.Default;
        var rng = new StableRandom(seed);
        var duration = Math.Clamp(basis.DurationSeconds * rng.Between(1 - profile.DurationVariation, 1 + profile.DurationVariation), .03, 30);
        var scale = duration / basis.DurationSeconds;
        var layers = new List<LayerSettings>();
        foreach (var layer in basis.Layers)
        {
            layers.Add(layer with
            {
                Level = Math.Clamp(layer.Level * rng.Between(.75, 1.2), 0, 1),
                StartSeconds = Math.Min(duration * .85, layer.StartSeconds * scale),
                PitchHz = Math.Clamp(layer.PitchHz * rng.Between(.92, 1.08), 20, 12000),
                SweepSemitones = Math.Clamp(layer.SweepSemitones * rng.Between(.8, 1.2), -48, 48),
                AttackSeconds = Math.Min(30, layer.AttackSeconds * scale * rng.Between(.8, 1.2)),
                DecaySeconds = Math.Min(30, layer.DecaySeconds * scale),
                ReleaseSeconds = Math.Min(30, layer.ReleaseSeconds * scale),
                Pan = Math.Clamp(layer.Pan + rng.Between(-.2, .2), -1, 1),
                NoteSeconds = Math.Clamp(layer.NoteSeconds * scale, .03, 2), Notes = [.. layer.Notes]
            });
        }
        var result = basis with
        {
            Seed = seed, Name = current.Name, DurationSeconds = duration, Layers = layers.ToArray(),
            PitchSemitones = rng.Between(-profile.PitchVariation, profile.PitchVariation),
            NoiseAmount = Math.Clamp(basis.NoiseAmount * rng.Between(.65, 1.35), 0, 1),
            Effects = basis.Effects with
            {
                LowPassHz = Math.Clamp(basis.Effects.LowPassHz * rng.Between(.75, 1.2), 40, 20000),
                DelayMix = Math.Clamp(basis.Effects.DelayMix * rng.Between(.65, 1.3), 0, .8)
            },
            SampleRate = current.SampleRate, Channels = current.Channels, Loop = current.Loop,
            OutputDb = current.OutputDb, Normalize = current.Normalize
        };
        RecipeValidator.Validate(result);
        return result;
    }
}
