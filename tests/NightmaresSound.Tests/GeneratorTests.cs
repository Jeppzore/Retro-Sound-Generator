using System.Text;
using System.Text.Json;
using NightmaresSound.Core;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace NightmaresSound.Tests;

public sealed class GeneratorTests
{
    public static IEnumerable<object[]> ProfileIds => SoundCatalog.Profiles.Select(p => new object[] { p.Id });

    [Fact]
    public void CatalogPreservesEffectCategoriesAndAddsSevenInstruments()
    {
        Assert.Equal(14, SoundCatalog.Categories.Count);
        Assert.Equal(33, SoundCatalog.Profiles.Count);
        Assert.Equal(33, SoundCatalog.Profiles.Select(p => p.Id).Distinct().Count());
        Assert.Equal(7, SoundCatalog.Profiles.Count(p => p.CategoryId == "instruments"));
        foreach (var category in SoundCatalog.Categories.Where(c => c.Id != "instruments"))
            Assert.Equal(2, SoundCatalog.Profiles.Count(p => p.CategoryId == category.Id));
    }

    [Theory, MemberData(nameof(ProfileIds))]
    public void EveryConfigurationProducesDeterministicNonSilentAudio(string id)
    {
        var recipe = SoundCatalog.Create(id);
        var renderer = new SoundRenderer();
        var first = renderer.Render(recipe);
        var second = renderer.Render(RecipeFile.Deserialize(RecipeFile.Serialize(recipe)));
        Assert.Equal(first.Pcm16, second.Pcm16);
        Assert.Equal((int)Math.Round(recipe.DurationSeconds * recipe.SampleRate), first.FrameCount);
        Assert.True(double.IsFinite(first.PeakDb));
        Assert.InRange(first.PeakDb, -1.01, -.99);
        if (!recipe.Loop)
        {
            Assert.Equal(0, first.SampleAt(0));
            Assert.Equal(0, first.SampleAt(first.FrameCount - 1));
        }
    }

    [Theory, MemberData(nameof(ProfileIds))]
    public void RandomizationStaysValidAndPreservesTheSelectionAndOutput(string id)
    {
        var current = SoundCatalog.Create(id) with { SampleRate = 48000, Channels = 2, Loop = true, Normalize = false, OutputDb = -9 };
        for (int seed = 0; seed < 100; seed++)
        {
            var result = RecipeRandomizer.Randomize(current, seed);
            RecipeValidator.Validate(result);
            Assert.Equal(current.CategoryId, result.CategoryId);
            Assert.Equal(current.ConfigurationId, result.ConfigurationId);
            Assert.Equal(48000, result.SampleRate); Assert.Equal(2, result.Channels); Assert.True(result.Loop);
            Assert.False(result.Normalize); Assert.Equal(-9, result.OutputDb); Assert.Equal(seed, result.Seed);
            Assert.Equal(RecipeFile.Serialize(result), RecipeFile.Serialize(RecipeRandomizer.Randomize(current, seed)));
        }
        Assert.NotEqual(RecipeFile.Serialize(RecipeRandomizer.Randomize(current, 1)), RecipeFile.Serialize(RecipeRandomizer.Randomize(current, 2)));
    }

    [Theory]
    [InlineData(WaveSource.Sine)] [InlineData(WaveSource.Triangle)] [InlineData(WaveSource.Square)]
    [InlineData(WaveSource.Sawtooth)] [InlineData(WaveSource.WhiteNoise)] [InlineData(WaveSource.FilteredNoise)]
    public void AllSourcesAndExtremeSettingsRemainBounded(WaveSource source)
    {
        var basis = SoundCatalog.Create("electric");
        var recipe = basis with
        {
            DurationSeconds = .03, Loop = true, Channels = 2, SampleRate = 48000, PitchSemitones = 36,
            NoiseAmount = 1, OutputDb = 6,
            Layers = [new() { Source = source, PitchHz = 12000, SweepSemitones = 48, Pan = -.2 }],
            Effects = new() { LowPassHz = 20000, HighPassHz = 8000, VibratoHz = 20, VibratoSemitones = 2,
                TremoloHz = 30, TremoloDepth = 1, Saturation = 1, BitDepth = 4, DelaySeconds = 1, DelayMix = .8, DelayFeedback = .75 }
        };
        var result = new SoundRenderer().Render(recipe);
        Assert.Equal(1440, result.FrameCount);
        Assert.True(double.IsFinite(result.PeakDb));
        Assert.True(result.WouldClip);
        Assert.InRange(result.PeakDb, -.01, 0);
        for (int c = 0; c < result.Channels; c++)
            Assert.Equal(result.SampleAt(0, c), result.SampleAt(result.FrameCount - 1, c));
    }

    [Theory]
    [InlineData(44100, 1)] [InlineData(44100, 2)] [InlineData(48000, 1)] [InlineData(48000, 2)]
    public void WavContainsExactPreviewSamplesAndCorrectPcmHeaders(int rate, int channels)
    {
        var recipe = SoundCatalog.Create("impact") with { SampleRate = rate, Channels = channels };
        var sound = new SoundRenderer().Render(recipe);
        using var stream = new MemoryStream();
        WavExporter.Write(stream, sound);
        stream.Position = 0;
        using var reader = new BinaryReader(stream, Encoding.ASCII, true);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(reader.ReadBytes(4)));
        Assert.Equal(stream.Length - 8, reader.ReadInt32());
        Assert.Equal("WAVEfmt ", Encoding.ASCII.GetString(reader.ReadBytes(8)));
        Assert.Equal(16, reader.ReadInt32()); Assert.Equal(1, reader.ReadInt16()); Assert.Equal(channels, reader.ReadInt16());
        Assert.Equal(rate, reader.ReadInt32()); Assert.Equal(rate * channels * 2, reader.ReadInt32());
        Assert.Equal(channels * 2, reader.ReadInt16()); Assert.Equal(16, reader.ReadInt16());
        Assert.Equal("data", Encoding.ASCII.GetString(reader.ReadBytes(4)));
        Assert.Equal(sound.Pcm16.Length, reader.ReadInt32());
        Assert.Equal(sound.Pcm16, reader.ReadBytes(sound.Pcm16.Length));
        Assert.Equal(stream.Length, stream.Position);
    }

    [Theory]
    [InlineData("rain")] [InlineData("drone")] [InlineData("teleport-idle")] [InlineData("bubbles")]
    public void LoopsWithEffectsHaveContinuousSeamsAndStreamWithoutPadding(string id)
    {
        var recipe = SoundCatalog.Create(id) with { Channels = 2 };
        var sound = new SoundRenderer().Render(recipe);
        for (int c = 0; c < 2; c++)
        {
            Assert.Equal(sound.SampleAt(0, c), sound.SampleAt(sound.FrameCount - 1, c));
            Assert.InRange(Math.Abs(sound.SampleAt(1, c) - sound.SampleAt(sound.FrameCount - 2, c)), 0, 8);
        }
        var cursor = new PcmPlaybackCursor(sound, true);
        var bytes = new byte[sound.Pcm16.Length * 3 + 28];
        int offset = 0;
        while (offset < bytes.Length)
        {
            int count = Math.Min(1020, bytes.Length - offset);
            int read = cursor.Read(bytes.AsSpan(offset, count));
            Assert.Equal(count, read); offset += read;
        }
        for (int cycle = 0; cycle < 3; cycle++)
            Assert.Equal(sound.Pcm16, bytes.AsSpan(cycle * sound.Pcm16.Length, sound.Pcm16.Length).ToArray());
        Assert.Equal(sound.Pcm16.AsSpan(0, 28).ToArray(), bytes.AsSpan(sound.Pcm16.Length * 3).ToArray());
    }

    [Fact]
    public void OneShotPlaybackEndsExactlyAtTheExportedLength()
    {
        var sound = new SoundRenderer().Render(SoundCatalog.Create("confirm"));
        var cursor = new PcmPlaybackCursor(sound, false);
        var buffer = new byte[sound.Pcm16.Length + 20];
        Assert.Equal(sound.Pcm16.Length, cursor.Read(buffer));
        Assert.Equal(sound.Pcm16, buffer.AsSpan(0, sound.Pcm16.Length).ToArray());
        Assert.Equal(0, cursor.Read(buffer));
    }

    [Fact]
    public void SeedChangesNoiseAndOutputGainRemainsEffectiveAfterNormalization()
    {
        var recipe = SoundCatalog.Create("rain") with { DurationSeconds = .2 };
        var renderer = new SoundRenderer();
        var first = renderer.Render(recipe);
        Assert.NotEqual(first.Pcm16, renderer.Render(recipe with { Seed = 200 }).Pcm16);
        Assert.InRange(renderer.Render(recipe with { OutputDb = -12 }).PeakDb, -13.02, -12.98);
    }

    [Fact]
    public void InvalidRecipesAreRejectedBeforeRendering()
    {
        var r = SoundCatalog.Create("clink");
        SoundRecipe[] invalid =
        [
            r with { Version = 3 }, r with { DurationSeconds = double.NaN }, r with { DurationSeconds = 31 },
            r with { SampleRate = 22050 }, r with { Channels = 3 }, r with { Name = "" },
            r with { CategoryId = "ui" }, r with { ConfigurationId = "missing" },
            r with { Layers = [] }, r with { Layers = null! }, r with { Effects = null! },
            r with { Layers = [new() { Source = (WaveSource)99 }] },
            r with { Layers = [new() { Notes = [double.PositiveInfinity] }] },
            r with { Layers = [new() { Notes = [] }] },
            r with { Effects = new() { DelayFeedback = 1 } },
            r with { Effects = new() { BitDepth = 24 } }
        ];
        foreach (var value in invalid)
            Assert.Throws<InvalidDataException>(() => new SoundRenderer().Render(value));
        foreach (var text in new[] { "null", "{}", "{ broken", new string(' ', 262145), "{\"Version\":9}" })
            Assert.Throws<InvalidDataException>(() => RecipeFile.Deserialize(text));
        string json = RecipeFile.Serialize(r);
        Assert.Throws<InvalidDataException>(() => RecipeFile.Deserialize(json.Replace("\"Sine\"", "\"Unknown\"")));
        Assert.Throws<InvalidDataException>(() => RecipeFile.Deserialize(json.Replace("\"Version\": 2", "\"UnknownField\": 1")));
    }

    [Fact]
    public void RecipeRoundTripIncludesAllControlsAndDoesNotTouchTheOriginal()
    {
        var recipe = RecipeRandomizer.Randomize(SoundCatalog.Create("charge"), 1234) with { Loop = true, Channels = 2, Normalize = false, Name = "My charge" };
        var json = RecipeFile.Serialize(recipe);
        var reopened = RecipeFile.Deserialize(json);
        Assert.Equal(json, RecipeFile.Serialize(reopened));
        reopened.Layers[0].Notes[0] = 25;
        Assert.NotEqual(reopened.Layers[0].Notes[0], recipe.Layers[0].Notes[0]);
    }

    [Fact]
    public void HistorySupportsGroupedGesturesCapacityAndBranching()
    {
        var history = new RecipeHistory();
        var first = SoundCatalog.Create("clink");
        var current = first;
        // A slider gesture commits only its initial and final state.
        var endOfGesture = current with { PitchSemitones = 10 };
        history.Commit(current, endOfGesture); current = endOfGesture;
        Assert.Equal(1, history.UndoCount);
        current = history.Undo(current); Assert.Equal(0, current.PitchSemitones);
        current = history.Redo(current); Assert.Equal(10, current.PitchSemitones);
        for (int i = 0; i < 150; i++)
        {
            var next = current with { Seed = i + 1 };
            history.Commit(current, next); current = next;
        }
        Assert.Equal(100, history.UndoCount);
        current = history.Undo(current); Assert.True(history.CanRedo);
        var branch = current with { Seed = 999 };
        history.Commit(current, branch); Assert.False(history.CanRedo);
        history.Commit(branch, RecipeFile.Deserialize(RecipeFile.Serialize(branch)));
        Assert.Equal(100, history.UndoCount);
    }

    [Fact]
    public void CancellationIsObservedBeforeAndDuringRendering()
    {
        using var source = new CancellationTokenSource(); source.Cancel();
        Assert.Throws<OperationCanceledException>(() => new SoundRenderer().Render(SoundCatalog.Create("rain"), source.Token));
        using var during = new CancellationTokenSource();
        during.CancelAfter(5);
        Assert.Throws<OperationCanceledException>(() => new SoundRenderer().Render(SoundCatalog.Create("drone") with { DurationSeconds = 30, Channels = 2 }, during.Token));
    }

    [Fact]
    public async Task RapidEditsPublishOnlyTheLatestRenderEvenIfOldWorkFinishesLast()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var coordinator = new RenderCoordinator((r, _) =>
        {
            if (r.Seed == 1) { started.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(5))); }
            return new([(byte)r.Seed, 0], r.SampleRate, 1, -1, -1, false);
        });
        var recipe = SoundCatalog.Create("clink");
        var old = coordinator.UpdateAsync(recipe with { Seed = 1 }, 0);
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        try
        {
            var latest = await coordinator.UpdateAsync(recipe with { Seed = 2 }, 0);
            Assert.NotNull(latest); Assert.Equal(2, latest.Pcm16[0]);
        }
        finally { release.Set(); }
        Assert.Null(await old);
    }

    [Fact]
    public async Task DebouncingAndDisposalCancelPendingWork()
    {
        int calls = 0;
        var coordinator = new RenderCoordinator((r, _) => { calls++; return new([0, 0], r.SampleRate, 1, -1, -1, false); });
        var recipe = SoundCatalog.Create("clink");
        var first = coordinator.UpdateAsync(recipe, 100);
        var second = coordinator.UpdateAsync(recipe, 100);
        Assert.Null(await first); Assert.NotNull(await second); Assert.Equal(1, calls);
        var pending = coordinator.UpdateAsync(recipe, 100);
        coordinator.Dispose();
        Assert.Null(await pending);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => coordinator.UpdateAsync(recipe));
    }

    [Fact]
    public void FailedExportsDoNotReplaceExistingFilesOrLeaveTemporaryFiles()
    {
        string dir = Path.Combine(Path.GetTempPath(), "NightmaresSoundTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "existing.dat");
            File.WriteAllText(path, "keep this");
            Assert.Throws<IOException>(() => AtomicFile.Write(path, stream => { stream.WriteByte(42); throw new IOException("Disk failure"); }));
            Assert.Equal("keep this", File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(dir));
            var sound = new SoundRenderer().Render(SoundCatalog.Create("slot"));
            using var readOnly = new MemoryStream([], writable: false);
            Assert.Throws<ArgumentException>(() => WavExporter.Write(readOnly, sound));
            Assert.Throws<DirectoryNotFoundException>(() => WavExporter.Save(Path.Combine(dir, "missing", "output.dat"), sound));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
