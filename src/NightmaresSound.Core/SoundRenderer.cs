namespace NightmaresSound.Core;

public sealed record RenderedSound(byte[] Pcm16, int SampleRate, int Channels, double PeakDb,
    double PreLimitPeakDb, bool WouldClip)
{
    public int FrameCount => Pcm16.Length / (2 * Channels);
    public double DurationSeconds => (double)FrameCount / SampleRate;
    public short SampleAt(int frame, int channel = 0)
    {
        var offset = (frame * Channels + channel) * 2;
        return System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(Pcm16.AsSpan(offset, 2));
    }
}

public sealed class SoundRenderer
{
    public RenderedSound Render(SoundRecipe recipe, CancellationToken cancellationToken = default)
    {
        RecipeValidator.Validate(recipe);
        cancellationToken.ThrowIfCancellationRequested();
        int rate = recipe.SampleRate, channels = recipe.Channels;
        int frames = (int)Math.Round((recipe.Instrument is null || recipe.Loop ? recipe.DurationSeconds
            : Math.Max(recipe.DurationSeconds, InstrumentCatalog.RequiredDuration(recipe.Instrument))) * rate);
        var mix = new double[frames * channels];
        if (recipe.Instrument is not null) InstrumentRenderer.Mix(mix, recipe, cancellationToken);
        for (int layerIndex = 0; recipe.Instrument is null && layerIndex < recipe.Layers.Length; layerIndex++)
        {
            var layer = recipe.Layers[layerIndex];
            if (!layer.Enabled || layer.Level == 0) continue;
            var rng = new StableRandom(unchecked(recipe.Seed + layerIndex * 7919));
            double phase = 0, filteredNoise = 0;
            double left = channels == 1 ? 1 : Math.Cos((layer.Pan + 1) * Math.PI / 4);
            double right = Math.Sin((layer.Pan + 1) * Math.PI / 4);
            int start = Math.Min(frames, (int)Math.Round(layer.StartSeconds * rate));
            double available = (double)(frames - start) / rate;
            double vibratoRate = PeriodicRate(recipe.Effects.VibratoHz, recipe);
            double tremoloRate = PeriodicRate(recipe.Effects.TremoloHz, recipe);
            for (int frame = start; frame < frames; frame++)
            {
                if ((frame & 2047) == 0) cancellationToken.ThrowIfCancellationRequested();
                double time = (double)(frame - start) / rate;
                int note = 0;
                double noteTime = time, noteDuration = available;
                if (layer.Notes.Length > 1)
                {
                    int step = (int)(time / layer.NoteSeconds);
                    if (recipe.Loop)
                    {
                        note = step % layer.Notes.Length;
                        noteTime = time - step * layer.NoteSeconds;
                        noteDuration = layer.NoteSeconds;
                    }
                    else
                    {
                        note = Math.Min(step, layer.Notes.Length - 1);
                        noteTime = time - note * layer.NoteSeconds;
                        noteDuration = note == layer.Notes.Length - 1
                            ? Math.Max(.001, available - note * layer.NoteSeconds) : layer.NoteSeconds;
                    }
                }
                double sweepProgress = recipe.Loop
                    ? (layer.Notes.Length > 1 ? noteTime / noteDuration : time / available)
                    : time / available;
                double pitch = recipe.PitchSemitones + layer.Notes[note] + layer.SweepSemitones * sweepProgress
                    + recipe.Effects.VibratoSemitones * Math.Sin(2 * Math.PI * vibratoRate * time);
                double frequency = Math.Clamp(layer.PitchHz * Math.Pow(2, pitch / 12), 10, rate * .45);
                double increment = frequency / rate;
                double noise = rng.Next() * 2 - 1;
                filteredNoise += (1 - Math.Exp(-2 * Math.PI * Math.Min(frequency * 8, rate * .4) / rate)) * (noise - filteredNoise);
                double source = Oscillator(layer.Source, phase, increment, noise, filteredNoise);
                if (layer.Source is WaveSource.WhiteNoise or WaveSource.FilteredNoise)
                    source *= recipe.NoiseAmount;
                else
                    source = source * (1 - recipe.NoiseAmount * .5) + filteredNoise * recipe.NoiseAmount * .5;
                phase += increment;
                phase -= Math.Floor(phase);
                double envelope = recipe.Loop && layer.Notes.Length == 1
                    ? 1 : Envelope(noteTime, noteDuration, layer);
                double tremolo = 1 - recipe.Effects.TremoloDepth * .5 * (1 - Math.Cos(2 * Math.PI * tremoloRate * time));
                double value = source * envelope * layer.Level * tremolo;
                mix[frame * channels] += value * left;
                if (channels == 2) mix[frame * 2 + 1] += value * right;
            }
        }
        Filter(mix, recipe, cancellationToken);
        Shape(mix, recipe.Effects, cancellationToken);
        Delay(mix, recipe, cancellationToken);
        if (recipe.Loop) BlendLoopBoundary(mix, frames, channels, rate);
        else FadeEdges(mix, frames, channels, rate);

        double rawPeak = 0;
        for (int i = 0; i < mix.Length; i++)
        {
            if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            rawPeak = Math.Max(rawPeak, Math.Abs(mix[i]));
        }
        // Normalize first; the user's output level remains an audible final gain control.
        double gain = Math.Pow(10, recipe.OutputDb / 20);
        if (recipe.Normalize && rawPeak > 1e-12) gain *= Math.Pow(10, -1.0 / 20) / rawPeak;
        double preLimitPeak = rawPeak * gain;
        var pcm = new byte[mix.Length * 2];
        double finalPeak = 0;
        for (int i = 0; i < mix.Length; i++)
        {
            if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            var value = Math.Clamp(mix[i] * gain, -1, 1);
            short sample = (short)Math.Round(value * 32767);
            finalPeak = Math.Max(finalPeak, Math.Abs(sample / 32768.0));
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2, 2), sample);
        }
        return new(pcm, rate, channels, Db(finalPeak), Db(preLimitPeak), preLimitPeak > 1);
    }

    private static double Db(double amplitude) => amplitude > 0 ? 20 * Math.Log10(amplitude) : double.NegativeInfinity;
    private static double PeriodicRate(double rate, SoundRecipe recipe) =>
        recipe.Loop && rate > 0 ? Math.Max(1, Math.Round(rate * recipe.DurationSeconds)) / recipe.DurationSeconds : rate;

    private static double Oscillator(WaveSource source, double phase, double dt, double noise, double filtered) => source switch
    {
        WaveSource.Sine => Math.Sin(2 * Math.PI * phase),
        WaveSource.Triangle => 1 - 4 * Math.Abs(phase - .5),
        WaveSource.Square => (phase < .5 ? 1 : -1) + PolyBlep(phase, dt) - PolyBlep((phase + .5) % 1, dt),
        WaveSource.Sawtooth => 2 * phase - 1 - PolyBlep(phase, dt),
        WaveSource.WhiteNoise => noise,
        WaveSource.FilteredNoise => filtered,
        _ => 0
    };

    private static double PolyBlep(double t, double dt)
    {
        if (t < dt) { t /= dt; return 2 * t - t * t - 1; }
        if (t > 1 - dt) { t = (t - 1) / dt; return t * t + 2 * t + 1; }
        return 0;
    }

    private static double Envelope(double time, double duration, LayerSettings l)
    {
        if (time < 0 || time >= duration) return 0;
        double release = Math.Min(l.ReleaseSeconds, duration * .8);
        double gate = Math.Min(duration - release, duration * l.Gate);
        double scale = Math.Min(1, gate / Math.Max(1e-9, l.AttackSeconds + l.DecaySeconds));
        double attack = l.AttackSeconds * scale, decay = l.DecaySeconds * scale;
        double BeforeRelease(double t)
        {
            if (attack > 0 && t < attack) return t / attack;
            if (decay > 0 && t < attack + decay) return 1 - (1 - l.Sustain) * (t - attack) / decay;
            return l.Sustain;
        }
        if (time <= gate) return BeforeRelease(time);
        return release > 0 ? BeforeRelease(gate) * Math.Max(0, 1 - (time - gate) / release) : 0;
    }

    private static void Filter(double[] samples, SoundRecipe r, CancellationToken token)
    {
        double lowAlpha = 1 - Math.Exp(-2 * Math.PI * Math.Min(r.Effects.LowPassHz, r.SampleRate * .45) / r.SampleRate);
        double highAlpha = 1 - Math.Exp(-2 * Math.PI * r.Effects.HighPassHz / r.SampleRate);
        var low = new double[r.Channels]; var dc = new double[r.Channels];
        // Warm the filter from the end of the loop rather than assuming initial silence.
        if (r.Loop)
        {
            for (int i = 0; i < samples.Length; i++)
            {
                if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                int c = i % r.Channels;
                low[c] += lowAlpha * (samples[i] - low[c]);
                dc[c] += highAlpha * (low[c] - dc[c]);
            }
        }
        for (int i = 0; i < samples.Length; i++)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            int c = i % r.Channels;
            low[c] += lowAlpha * (samples[i] - low[c]);
            dc[c] += highAlpha * (low[c] - dc[c]);
            samples[i] = low[c] - dc[c];
        }
    }

    private static void Shape(double[] samples, EffectSettings e, CancellationToken token)
    {
        double drive = 1 + e.Saturation * 12;
        double divisor = Math.Tanh(drive);
        double steps = Math.Pow(2, e.BitDepth - 1) - 1;
        for (int i = 0; i < samples.Length; i++)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            double value = e.Saturation > 0 ? Math.Tanh(samples[i] * drive) / divisor : samples[i];
            samples[i] = e.BitDepth < 16 ? Math.Round(value * steps) / steps : value;
        }
    }

    private static void Delay(double[] samples, SoundRecipe r, CancellationToken token)
    {
        if (r.Effects.DelayMix == 0) return;
        int offset = Math.Max(1, (int)Math.Round(r.Effects.DelaySeconds * r.SampleRate)) * r.Channels;
        double wet = r.Effects.DelayMix, feedback = r.Effects.DelayFeedback;
        if (r.Loop)
        {
            var dry = (double[])samples.Clone();
            for (int i = 0; i < samples.Length; i++) samples[i] *= 1 - wet;
            double amplitude = wet;
            int shift = offset % samples.Length;
            for (int tap = 1; amplitude >= 1e-7 && tap <= 64; tap++)
            {
                int read = (int)((samples.Length - (long)shift * tap % samples.Length) % samples.Length);
                for (int i = 0; i < samples.Length; i++)
                {
                    if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                    samples[i] += dry[read] * amplitude;
                    if (++read == samples.Length) read = 0;
                }
                amplitude *= feedback;
            }
        }
        else
        {
            var echoes = new double[samples.Length];
            for (int i = 0; i < samples.Length; i++)
            {
                if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                if (i >= offset) echoes[i] = samples[i - offset] + echoes[i - offset] * feedback;
            }
            for (int i = 0; i < samples.Length; i++) samples[i] = samples[i] * (1 - wet) + echoes[i] * wet;
        }
    }

    private static void FadeEdges(double[] samples, int frames, int channels, int rate)
    {
        int width = Math.Min((int)(rate * .004), frames / 4);
        for (int i = 0; i < width; i++)
        {
            double gain = .5 - .5 * Math.Cos(Math.PI * i / width);
            for (int c = 0; c < channels; c++)
            {
                samples[i * channels + c] *= gain;
                samples[(frames - 1 - i) * channels + c] *= gain;
            }
        }
    }

    private static void BlendLoopBoundary(double[] samples, int frames, int channels, int rate)
    {
        int width = Math.Min((int)(rate * .01), frames / 4);
        for (int c = 0; c < channels; c++)
        {
            double seam = (samples[c] + samples[(frames - 1) * channels + c]) * .5;
            for (int i = 0; i < width; i++)
            {
                double blend = .5 + .5 * Math.Cos(Math.PI * i / width);
                int first = i * channels + c, last = (frames - 1 - i) * channels + c;
                samples[first] += (seam - samples[first]) * blend;
                samples[last] += (seam - samples[last]) * blend;
            }
        }
    }
}
