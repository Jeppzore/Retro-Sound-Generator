using System.Security.Cryptography;
using System.Text.Json.Nodes;
using NightmaresSound.Core;
using Xunit;

namespace NightmaresSound.Tests;

public sealed class InstrumentTests
{
    public static IEnumerable<object[]> Voices => InstrumentCatalog.All.SelectMany(i => i.Styles.SelectMany(style =>
        new[] { InstrumentMode.SingleNote, InstrumentMode.Phrase }.Select(mode => new object[] { i.Id, style, mode })));

    [Theory, MemberData(nameof(Voices))]
    public void VoicesRoundTripAndProduceDeterministicPcmWithCompleteTails(string id, InstrumentStyle style, InstrumentMode mode)
    {
        var r = SoundCatalog.Create("instrument-" + id);
        var settings = r.Instrument! with { Style = style, Mode = mode };
        r = r with { Instrument = settings, DurationSeconds = InstrumentCatalog.RequiredDuration(settings), Channels = 2, SampleRate = 48000 };
        var renderer = new SoundRenderer();
        var sound = renderer.Render(r);
        Assert.Equal(sound.Pcm16, renderer.Render(RecipeFile.Deserialize(RecipeFile.Serialize(r))).Pcm16);
        Assert.Equal((int)Math.Round(r.DurationSeconds * r.SampleRate), sound.FrameCount);
        Assert.InRange(sound.PeakDb, -1.02, -.98);
        Assert.False(sound.WouldClip);
        Assert.Equal(0, sound.SampleAt(0));
        Assert.Equal(0, sound.SampleAt(sound.FrameCount - 1));
        var cursor = new PcmPlaybackCursor(sound, false);
        var buffer = new byte[sound.Pcm16.Length];
        Assert.Equal(buffer.Length, cursor.Read(buffer));
        using var stream = new MemoryStream();
        WavExporter.Write(stream, sound);
        Assert.Equal(buffer, stream.ToArray().AsSpan(44).ToArray());
    }

    [Fact]
    public void AllPhrasesUseScaleDegreesAndKeepAnEightBeatGrid()
    {
        foreach (var instrument in InstrumentCatalog.All)
        foreach (var phrase in instrument.Phrases)
        foreach (var scale in Enum.GetValues<MusicalScale>())
        foreach (int tempo in new[] { 40, 90, 200 })
        {
            var s = InstrumentCatalog.Defaults(instrument.Id) with { Mode = InstrumentMode.Phrase, Phrase = phrase, Scale = scale, Tempo = tempo };
            InstrumentCatalog.Validate(s);
            var notes = InstrumentCatalog.Notes(s);
            Assert.All(notes, n => Assert.InRange(n.StartSeconds, 0, 8 * 60.0 / tempo));
            Assert.All(notes, n => Assert.True(n.StartSeconds + n.GateSeconds + instrument.ReleaseSeconds <= InstrumentCatalog.RequiredDuration(s) + 1e-9));
            if (phrase == InstrumentPhrase.Strum)
            {
                Assert.Equal(16, notes.Count);
                Assert.Equal(.025, notes[1].StartSeconds - notes[0].StartSeconds, 8);
            }
        }
        var dorian = InstrumentCatalog.Defaults("recorder") with { Mode = InstrumentMode.Phrase };
        var minor = dorian with { Scale = MusicalScale.NaturalMinor };
        var mixolydian = dorian with { Scale = MusicalScale.Mixolydian };
        Assert.NotEqual(InstrumentCatalog.Notes(dorian).Select(n => n.MidiNote), InstrumentCatalog.Notes(mixolydian).Select(n => n.MidiNote));
        // Ascending and descending patterns are mirrored within each bar.
        var up = InstrumentCatalog.Notes(InstrumentCatalog.Defaults("harp") with { Mode = InstrumentMode.Phrase });
        var down = InstrumentCatalog.Notes(InstrumentCatalog.Defaults("harp") with { Mode = InstrumentMode.Phrase, Phrase = InstrumentPhrase.Descending });
        Assert.Equal(up.Take(4).Select(n => n.MidiNote).Reverse(), down.Take(4).Select(n => n.MidiNote));
        Assert.Equal(2, dorian.Key); Assert.Equal(4, dorian.Octave); Assert.Equal(90, dorian.Tempo);
        Assert.Equal(MusicalScale.Dorian, dorian.Scale);
        Assert.NotEqual(InstrumentCatalog.Notes(dorian).Select(n => n.MidiNote), InstrumentCatalog.Notes(minor).Select(n => n.MidiNote));
    }

    [Fact]
    public void InstrumentLoopsHaveExactMusicalLengthAndContinuousSeams()
    {
        foreach (var instrument in InstrumentCatalog.All)
        {
            var r = SoundCatalog.Create("instrument-" + instrument.Id);
            r = r with { Instrument = r.Instrument! with { Mode = InstrumentMode.Phrase }, Loop = true, DurationSeconds = 8 * 60.0 / 90 };
            var sound = new SoundRenderer().Render(r);
            Assert.Equal((int)Math.Round(8 * 60.0 / 90 * r.SampleRate), sound.FrameCount);
            Assert.Equal(sound.SampleAt(0), sound.SampleAt(sound.FrameCount - 1));
        }
    }

    [Fact]
    public void ShortDurationCannotCropInstrumentReleaseAndVoicesRemainDistinct()
    {
        var hashes = new HashSet<string>();
        foreach (var i in InstrumentCatalog.All)
        {
            var r = SoundCatalog.Create("instrument-" + i.Id) with { DurationSeconds = .03 };
            var sound = new SoundRenderer().Render(r);
            Assert.True(sound.DurationSeconds >= InstrumentCatalog.RequiredDuration(r.Instrument!) - 1.0 / r.SampleRate);
            Assert.True(hashes.Add(Convert.ToHexString(SHA256.HashData(sound.Pcm16))));
        }
    }

    [Fact]
    public void SampleManifestMatchesEmbeddedOfflineAssetsAndVerifiedLicenses()
    {
        var assembly = typeof(AcousticLibrary).Assembly;
        Assert.Equal(4, InstrumentCatalog.All.Count(i => i.HasAcoustic));
        foreach (var zone in AcousticLibrary.Zones)
        {
            Assert.Equal("CC0-1.0", zone.License);
            Assert.StartsWith("https://raw.githubusercontent.com/sgossner/", zone.SourceUrl);
            Assert.Contains("/blob/", zone.LicenseUrl);
            Assert.InRange(zone.RootMidi, zone.MinMidi, zone.MaxMidi);
            Assert.Equal(64, zone.SourceSha256.Length);
            Assert.Contains("Nearest-root", zone.Processing);
            using var stream = assembly.GetManifestResourceStream("NightmaresSound.Core.Samples." + zone.File);
            Assert.NotNull(stream);
            Assert.Equal(zone.BundledSha256.ToUpperInvariant(), Convert.ToHexString(SHA256.HashData(stream)));
            using var license = assembly.GetManifestResourceStream("NightmaresSound.Core.Samples." + zone.LicenseEvidence);
            Assert.NotNull(license);
            Assert.Contains("Creative Commons", new StreamReader(license).ReadToEnd());
        }
    }

    [Fact]
    public void AcousticPitchedVoicesUseSoundingMidiOctaves()
    {
        foreach (string id in new[] { "harp", "recorder", "fiddle" })
        {
            var r = SoundCatalog.Create("instrument-" + id);
            r = r with { Instrument = r.Instrument! with { Style = InstrumentStyle.Acoustic, Key = 0 } };
            var sound = new SoundRenderer().Render(r);
            // The fundamental must be near middle C, not the octave suggested by
            // VCSL's different filename convention. Scan around C4, C3, and C5.
            double Energy(double frequency)
            {
                double real = 0, imaginary = 0;
                int start = (int)(sound.SampleRate * .1), count = (int)(sound.SampleRate * .4);
                for (int i = 0; i < count; i++)
                {
                    double value = sound.SampleAt(start + i) / 32768.0 * (.5 - .5 * Math.Cos(2 * Math.PI * i / count));
                    double phase = 2 * Math.PI * frequency * i / sound.SampleRate;
                    real += value * Math.Cos(phase); imaginary += value * Math.Sin(phase);
                }
                return real * real + imaginary * imaginary;
            }
            double Band(double root) => Enumerable.Range(-5, 11).Max(offset => Energy(root + offset));
            Assert.True(Band(261.6256) > Band(130.8128) * 4, id + " is mapped an octave too high");
            Assert.True(Band(261.6256) > Band(523.2511) * .03, id + " has no C4 fundamental");
        }
    }

    [Fact]
    public void MissingInstrumentFieldKeepsLegacyRecipesAndAudioUnchanged()
    {
        var renderer = new SoundRenderer();
        foreach (var profile in SoundCatalog.Profiles.Where(p => p.CategoryId != "instruments"))
        {
            var r = SoundCatalog.Create(profile.Id);
            var json = JsonNode.Parse(RecipeFile.Serialize(r))!.AsObject();
            json.Remove("Instrument");
            var old = RecipeFile.Deserialize(json.ToJsonString());
            Assert.Null(old.Instrument);
            Assert.Equal(renderer.Render(r).Pcm16, renderer.Render(old).Pcm16);
        }
    }

    [Fact]
    public void InvalidInstrumentSettingsAreRejectedWithoutSilentFallback()
    {
        var r = SoundCatalog.Create("instrument-guitar");
        InstrumentSettings[] invalid = [r.Instrument! with { Id = "missing" }, r.Instrument! with { Tempo = 0 },
            r.Instrument! with { Key = 12 }, r.Instrument! with { Octave = 9 }, r.Instrument! with { Style = InstrumentStyle.Acoustic },
            r.Instrument! with { Phrase = InstrumentPhrase.March }, r.Instrument! with { Mode = (InstrumentMode)99 }];
        foreach (var s in invalid) Assert.Throws<InvalidDataException>(() => RecipeValidator.Validate(r with { Instrument = s }));
        Assert.Throws<InvalidDataException>(() => RecipeValidator.Validate(r with { Instrument = null }));
        Assert.Throws<InvalidDataException>(() => RecipeValidator.Validate(SoundCatalog.Create("clink") with { Instrument = r.Instrument }));
        var harp = SoundCatalog.Create("instrument-harp");
        Assert.Throws<InvalidDataException>(() => RecipeValidator.Validate(harp with { Instrument = harp.Instrument! with { Style = InstrumentStyle.Acoustic }, PitchSemitones = -36 }));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => new SoundRenderer().Render(r, cancellation.Token));
    }
}
