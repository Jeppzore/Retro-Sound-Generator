using NightmaresSound.Core;

namespace NightmaresSound.App;

public sealed class InstrumentVoiceViewModel : ObservableObject
{
    private readonly MainViewModel owner;
    private readonly int index;
    private InstrumentVoiceSettings Current => owner.Current.Instrument?.Voices.ElementAtOrDefault(index)
        ?? new InstrumentVoiceSettings { Id = owner.Current.Instrument?.Id ?? "guitar",
            Phrase = owner.Current.Instrument?.Phrase ?? InstrumentPhrase.Ascending, Enabled = false };
    public string Title => $"Voice {index + 1}";
    public IReadOnlyList<InstrumentDefinition> Instruments => InstrumentCatalog.All;
    public InstrumentDefinition SelectedInstrument
    {
        get => InstrumentCatalog.Get(Current.Id);
        set
        {
            if (value is null || value.Id == Current.Id) return;
            Change(v => v with { Id = value.Id, Style = value.Styles.Contains(v.Style) ? v.Style : value.Styles[0],
                Phrase = value.Phrases.Contains(v.Phrase) ? v.Phrase : value.Phrases[0] });
        }
    }
    public bool Enabled { get => Current.Enabled; set { if (value != Enabled) Change(v => v with { Enabled = value }); } }
    public InstrumentStyle[] Styles => SelectedInstrument.Styles;
    public InstrumentPhrase[] Phrases => SelectedInstrument.Phrases;
    public int[] Octaves { get; } = [3, 4, 5];
    public bool IsPitched => !SelectedInstrument.IsDrum;
    public bool IsPhrase => owner.IsInstrumentPhrase;
    public InstrumentStyle Style { get => Current.Style; set { if (value != Style) Change(v => v with { Style = value }); } }
    public InstrumentPhrase Phrase { get => Current.Phrase; set { if (value != Phrase) Change(v => v with { Phrase = value }); } }
    public int Octave { get => Current.Octave; set { if (value > 0 && value != Octave) Change(v => v with { Octave = value }); } }
    public IReadOnlyList<NumberParameter> Parameters { get; }

    public InstrumentVoiceViewModel(MainViewModel owner, int index)
    {
        this.owner = owner; this.index = index;
        NumberParameter P(string label, SoundConcept concept, string unit, double min, double max, double step,
            string help, Func<InstrumentVoiceSettings, double> read, Func<InstrumentVoiceSettings, double, InstrumentVoiceSettings> write,
            Func<(double Minimum, double Maximum)>? bounds = null) =>
            new(label, unit, min, max, step, help, () => read(Current), value => owner.UpdateInstrumentVoice(index, v => write(v, value)),
                bounds, concept, () => owner.IsRefreshingEditors);
        Parameters = new NumberParameter[]
        {
            P("Level", SoundConcept.Output, "", 0, 1, .01, "Voice volume before shared effects and normalization.", v => v.Level, (v,x) => v with { Level = x }),
            P("Tuning", SoundConcept.Pitch, "st", -36, 36, .5, "Transpose this voice relative to master pitch. Acoustic limits protect the sample range.", v => v.TuningSemitones, (v,x) => v with { TuningSemitones = x }, TuningBounds),
            P("Pan", SoundConcept.Output, "L / R", -1, 1, .05, "Stereo placement. Mono ignores pan.", v => v.Pan, (v,x) => v with { Pan = x })
        };
    }

    private (double Minimum, double Maximum) TuningBounds()
    {
        if (!Current.Enabled || Current.Style != InstrumentStyle.Acoustic || owner.Current.Instrument is null) return (-36, 36);
        var bounds = InstrumentCatalog.AcousticPitchBounds(owner.Current.Instrument, Current);
        return (Math.Max(-36, bounds.Minimum - owner.Current.PitchSemitones + owner.Current.Effects.VibratoSemitones),
            Math.Min(36, bounds.Maximum - owner.Current.PitchSemitones - owner.Current.Effects.VibratoSemitones));
    }

    private void Change(Func<InstrumentVoiceSettings, InstrumentVoiceSettings> change)
    {
        if (owner.IsRefreshingEditors) return;
        owner.ChangeInstrumentVoice(index, change);
    }

    public void Refresh()
    {
        foreach (string property in new[] { nameof(Enabled), nameof(SelectedInstrument), nameof(Styles), nameof(Phrases), nameof(Style),
            nameof(Phrase), nameof(Octave), nameof(IsPitched), nameof(IsPhrase) }) Notify(property);
        foreach (var parameter in Parameters) parameter.Refresh();
    }
}
