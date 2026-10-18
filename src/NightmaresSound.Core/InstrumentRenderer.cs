namespace NightmaresSound.Core;

internal static class InstrumentRenderer
{
    public static void Mix(double[] mix, SoundRecipe recipe, CancellationToken token)
    {
        var settings = recipe.Instrument!;
        var definition = InstrumentCatalog.Get(settings.Id);
        var notes = InstrumentCatalog.Notes(settings);
        for (int noteIndex = 0; noteIndex < notes.Count; noteIndex++)
        {
            token.ThrowIfCancellationRequested();
            var note = notes[noteIndex];
            var rng = new StableRandom(unchecked(recipe.Seed + noteIndex * 7919));
            double midi = note.MidiNote + recipe.PitchSemitones;
            double hz = definition.IsDrum ? 82.4 * Math.Pow(2, recipe.PitchSemitones / 12)
                : 440 * Math.Pow(2, (midi - 69) / 12);
            var sample = settings.Style == InstrumentStyle.Acoustic ? AcousticLibrary.Select(settings.Id, midi) : null;
            var audio = sample is null ? null : AcousticLibrary.Load(sample);
            double position = 0, phase = 0;
            int start = (int)Math.Round(note.StartSeconds * recipe.SampleRate);
            int length = (int)Math.Ceiling((note.GateSeconds + definition.ReleaseSeconds) * recipe.SampleRate);
            for (int frame = 0; frame < length; frame++)
            {
                if ((frame & 1023) == 0) token.ThrowIfCancellationRequested();
                int destination = start + frame;
                int totalFrames = mix.Length / recipe.Channels;
                if (!recipe.Loop && destination >= totalFrames) break;
                destination %= totalFrames;
                double t = (double)frame / recipe.SampleRate;
                double vibrato = recipe.Effects.VibratoSemitones * Math.Sin(2 * Math.PI * recipe.Effects.VibratoHz * t);
                if (definition.IsSustained && settings.Style == InstrumentStyle.Retro)
                    vibrato += .06 * Math.Min(1, t * 4) * Math.Sin(2 * Math.PI * 5.2 * t);
                double frequency = hz * Math.Pow(2, vibrato / 12);
                double noise = rng.Next() * 2 - 1;
                double value;
                if (audio is not null && sample is not null)
                {
                    int index = (int)position;
                    if (index + 1 >= audio.Samples.Length) break;
                    double fraction = position - index;
                    value = audio.Samples[index] * (1 - fraction) + audio.Samples[index + 1] * fraction;
                    position += (double)audio.Rate / recipe.SampleRate * Math.Pow(2, (midi + vibrato - sample.RootMidi) / 12);
                    // Smooth both the note-off and the end of the recording; no hard sample truncation.
                    value *= Math.Min(1, (audio.Samples.Length - position) / (audio.Rate * .02));
                }
                else
                {
                    value = Voice(settings.Id, phase, frequency, t, recipe.SampleRate, noise);
                    phase += frequency / recipe.SampleRate;
                }
                double attack = definition.Id == "fiddle" ? .07 : definition.Id == "recorder" ? .025 : .002;
                double envelope = Math.Min(1, t / attack);
                if (!definition.IsSustained && audio is null)
                    envelope *= Math.Exp(-t * (definition.Id switch { "harp" => 1.7, "dulcimer" => 2.5, "frame-drum" => 10, "lute" => 5, _ => 3.5 }));
                if (t > note.GateSeconds)
                    envelope *= Math.Pow(Math.Max(0, 1 - (t - note.GateSeconds) / definition.ReleaseSeconds), 2);
                double tremolo = 1 - recipe.Effects.TremoloDepth * .5 * (1 - Math.Cos(2 * Math.PI * recipe.Effects.TremoloHz * t));
                value = (value + noise * recipe.NoiseAmount * .03 * Math.Exp(-t * 30)) * envelope * note.Velocity * .55 * tremolo;
                for (int c = 0; c < recipe.Channels; c++) mix[destination * recipe.Channels + c] += value;
            }
        }
    }

    private static double Voice(string id, double phase, double hz, double t, int rate, double noise)
    {
        double sum = 0;
        for (int harmonic = 1; harmonic <= 16; harmonic++)
        {
            double ratio = id switch
            {
                "frame-drum" => harmonic switch { 1 => 1, 2 => 1.59, 3 => 2.14, _ => harmonic * 1.17 },
                "dulcimer" => harmonic * Math.Sqrt(1 + .0008 * harmonic * harmonic),
                _ => harmonic
            };
            if (hz * ratio >= rate * .44) break;
            double amplitude = id switch
            {
                "recorder" => harmonic == 1 ? 1 : harmonic % 2 == 0 ? .035 / harmonic : .22 / harmonic,
                "fiddle" => Math.Pow(harmonic, -1.1) * (.7 + .3 * Math.Cos(harmonic * 1.3)),
                "harp" => Math.Pow(harmonic, -2) * Math.Exp(-t * harmonic * .25),
                "lute" => Math.Sin(harmonic * .9) / Math.Pow(harmonic, 1.5) * Math.Exp(-t * harmonic * .65),
                "dulcimer" => Math.Pow(harmonic, -1.25) * Math.Exp(-t * harmonic * .2),
                "frame-drum" => Math.Pow(harmonic, -1.6) * Math.Exp(-t * harmonic * 3),
                _ => Math.Sin(harmonic * .65) / Math.Pow(harmonic, 1.45) * Math.Exp(-t * harmonic * .45)
            };
            sum += amplitude * Math.Sin(2 * Math.PI * phase * ratio);
            if (id == "lute") sum += amplitude * .25 * Math.Sin(2 * Math.PI * hz * ratio * 1.003 * t);
        }
        return sum + (id == "recorder" ? noise * .025 : id == "frame-drum" ? noise * .3 * Math.Exp(-t * 65) : noise * .08 * Math.Exp(-t * 100));
    }
}
