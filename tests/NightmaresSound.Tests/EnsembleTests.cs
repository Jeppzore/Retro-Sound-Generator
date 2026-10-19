using System.Text.Json.Nodes;
using NightmaresSound.Core;
using Xunit;

namespace NightmaresSound.Tests;

public sealed class EnsembleTests
{
    [Fact]
    public void AcousticRangePreflightMatchesStrictValidationAndSkipsDisabledVoices()
    {
        var recipe = Ensemble(new() { Id = "harp" }, new() { Id = "recorder", Phrase = InstrumentPhrase.Melody, Style = InstrumentStyle.Acoustic });
        recipe = recipe with { Loop = true, Instrument = recipe.Instrument! with { Mode = InstrumentMode.Phrase } };
        Assert.Null(RecipeValidator.AcousticRangeError(recipe));
        recipe = recipe with { Instrument = recipe.Instrument! with { Key = 11 } };
        Assert.Contains("Voice 2", RecipeValidator.AcousticRangeError(recipe)!);
        Assert.Throws<InvalidDataException>(() => RecipeValidator.Validate(recipe));
        recipe = recipe with { Instrument = recipe.Instrument! with { Voices = [recipe.Instrument.Voices[0], recipe.Instrument.Voices[1] with { Enabled = false }] } };
        Assert.Null(RecipeValidator.AcousticRangeError(recipe));
        RecipeValidator.Validate(recipe);
        Assert.False(AcousticLibrary.TrySelect("recorder", double.NaN, out var zone));
        Assert.Null(zone);
        Assert.True(AcousticLibrary.TrySelect("recorder", 60, out zone));
        Assert.Equal(AcousticLibrary.Select("recorder", 60), zone);
    }

    private static SoundRecipe Ensemble(params InstrumentVoiceSettings[] voices)
    {
        var recipe = SoundCatalog.Create("instrument-" + voices[0].Id);
        return recipe with { Instrument = recipe.Instrument! with { Voices = voices }, Normalize = false, NoiseAmount = 0, Channels = 2 };
    }

    [Fact]
    public void EveryLegacyInstrumentMigratesWithoutChangingAudio()
    {
        foreach (var instrument in InstrumentCatalog.All)
        foreach (var style in instrument.Styles)
        {
            var recipe = SoundCatalog.Create("instrument-" + instrument.Id);
            recipe = recipe with { Instrument = recipe.Instrument! with { Style = style, Mode = InstrumentMode.Phrase } };
            var root = JsonNode.Parse(RecipeFile.Serialize(recipe))!.AsObject();
            root["Version"] = 1;
            var group = root["Instrument"]!.AsObject();
            var voice = group["Voices"]![0]!.AsObject();
            foreach (string field in new[] { "Id", "Style", "Phrase", "Octave" }) group[field] = voice[field]!.DeepClone();
            group.Remove("Voices");
            var migrated = RecipeFile.Deserialize(root.ToJsonString());
            Assert.Equal(2, migrated.Version);
            Assert.Single(migrated.Instrument!.Voices);
            Assert.Equal(new SoundRenderer().Render(recipe).Pcm16, new SoundRenderer().Render(migrated).Pcm16);
            Assert.Equal(2, JsonNode.Parse(RecipeFile.Serialize(migrated))!["Version"]!.GetValue<int>());
        }
        var synthesis = JsonNode.Parse(RecipeFile.Serialize(SoundCatalog.Create("clink")))!;
        synthesis["Version"] = 1;
        Assert.Equal(new SoundRenderer().Render(SoundCatalog.Create("clink")).Pcm16,
            new SoundRenderer().Render(RecipeFile.Deserialize(synthesis.ToJsonString())).Pcm16);
    }

    [Fact]
    public void VoicesMixBeforeSharedEffectsAndPanSeparatesStereoChannels()
    {
        var left = new InstrumentVoiceSettings { Id = "harp", Pan = -1, Level = .3 };
        var right = new InstrumentVoiceSettings { Id = "lute", Pan = 1, Level = .4, TuningSemitones = 7 };
        var recipe = Ensemble(left, right);
        var renderer = new SoundRenderer();
        var combined = renderer.Render(recipe);
        var leftOnly = renderer.Render(recipe with { Instrument = recipe.Instrument! with { Voices = [left, right with { Enabled = false }] } });
        var rightOnly = renderer.Render(recipe with { Instrument = recipe.Instrument! with { Voices = [left with { Enabled = false }, right] } });
        for (int frame = 0; frame < combined.FrameCount; frame++)
        {
            Assert.Equal(leftOnly.SampleAt(frame, 0), combined.SampleAt(frame, 0));
            Assert.Equal(rightOnly.SampleAt(frame, 1), combined.SampleAt(frame, 1));
            Assert.Equal(0, leftOnly.SampleAt(frame, 1));
            Assert.Equal(0, rightOnly.SampleAt(frame, 0));
        }
        var mono = recipe with { Channels = 1 };
        Assert.Equal(renderer.Render(mono).Pcm16, renderer.Render(mono with { Instrument = mono.Instrument! with
            { Voices = [left with { Pan = 1 }, right with { Pan = -1 }] } }).Pcm16);
    }

    [Theory]
    [InlineData(44100)]
    [InlineData(48000)]
    public void ThreeVoicesRoundTripPreserveTailsAndExportTheExactLoop(int rate)
    {
        var recipe = Ensemble(new() { Id = "recorder", Phrase = InstrumentPhrase.Melody, Style = InstrumentStyle.Acoustic },
            new() { Id = "harp", Style = InstrumentStyle.Acoustic }, new() { Id = "frame-drum", Phrase = InstrumentPhrase.Dance });
        recipe = recipe with { SampleRate = rate, Instrument = recipe.Instrument! with { Mode = InstrumentMode.Phrase }, Normalize = true,
            Effects = recipe.Effects with { DelayMix = .2, Saturation = .1, VibratoSemitones = .1 } };
        var renderer = new SoundRenderer();
        var sound = renderer.Render(recipe);
        Assert.Equal((int)Math.Round(InstrumentCatalog.RequiredDuration(recipe.Instrument!) * rate), sound.FrameCount);
        Assert.Equal(sound.Pcm16, renderer.Render(RecipeFile.Deserialize(RecipeFile.Serialize(recipe))).Pcm16);
        recipe = recipe with { Loop = true, DurationSeconds = 8 * 60.0 / recipe.Instrument!.Tempo };
        sound = renderer.Render(recipe);
        Assert.Equal((int)Math.Round(recipe.DurationSeconds * rate), sound.FrameCount);
        Assert.Equal(sound.SampleAt(0), sound.SampleAt(sound.FrameCount - 1));
        using var stream = new MemoryStream();
        WavExporter.Write(stream, sound);
        Assert.Equal(sound.Pcm16, stream.ToArray().AsSpan(44).ToArray());
        var randomized = RecipeRandomizer.Randomize(recipe, 123);
        Assert.Equal(recipe.Instrument, randomized.Instrument);
    }

    [Fact]
    public void EveryInstrumentAndStyleCanBeAnAdditionalVoiceAndMutedVoicesAreSilent()
    {
        foreach (var instrument in InstrumentCatalog.All)
        foreach (var style in instrument.Styles)
        {
            var recipe = Ensemble(new() { Enabled = false },
                new() { Id = instrument.Id, Phrase = instrument.Phrases[0], Style = style }, new() { Enabled = false });
            var renderer = new SoundRenderer();
            var sound = renderer.Render(recipe);
            Assert.True(double.IsFinite(sound.PeakDb), instrument.Id);
            Assert.Equal(sound.Pcm16, renderer.Render(recipe).Pcm16);
            var muted = recipe with { Instrument = recipe.Instrument! with { Voices = recipe.Instrument.Voices.Select(v => v with { Enabled = false }).ToArray() } };
            Assert.All(renderer.Render(muted).Pcm16, value => Assert.Equal((byte)0, value));
        }
    }

    [Fact]
    public void InvalidVoiceCollectionsAndAcousticConflictsAreRejected()
    {
        var recipe = Ensemble(new() { Id = "harp" }, new() { Id = "recorder", Phrase = InstrumentPhrase.Melody, Style = InstrumentStyle.Acoustic });
        foreach (var voices in new InstrumentVoiceSettings[][] { [], null!, [null!], [new(), new(), new(), new()],
            [new() { Id = "harp", Level = double.NaN }], [new() { Id = "harp", Pan = 2 }],
            [new() { Id = "harp", TuningSemitones = 37 }], [new() { Id = "harp", Style = InstrumentStyle.Acoustic, TuningSemitones = -36 }] })
            Assert.Throws<InvalidDataException>(() => RecipeValidator.Validate(recipe with { Instrument = recipe.Instrument! with { Voices = voices } }));
        Assert.Throws<InvalidDataException>(() => RecipeValidator.Validate(recipe with { PitchSemitones = 36 }));
        var root = JsonNode.Parse(RecipeFile.Serialize(recipe))!;
        root["Instrument"]!["Voices"] = null;
        Assert.Throws<InvalidDataException>(() => RecipeFile.Deserialize(root.ToJsonString()));
        root["Version"] = 99;
        Assert.Throws<InvalidDataException>(() => RecipeFile.Deserialize(root.ToJsonString()));
    }
}
