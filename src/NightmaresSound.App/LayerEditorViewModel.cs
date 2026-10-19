using System.Globalization;
using NightmaresSound.Core;

namespace NightmaresSound.App;

public sealed class LayerEditorViewModel : ObservableObject
{
    private readonly MainViewModel owner;
    private readonly int index;
    private LayerSettings Current => owner.Current.Layers.ElementAtOrDefault(index) ?? new() { Enabled = false };
    public string Title => $"Layer {index + 1}";
    public WaveSource[] Sources { get; } = Enum.GetValues<WaveSource>();
    public bool Enabled { get => Current.Enabled; set { if (value != Enabled) Change(l => l with { Enabled = value }); } }
    public WaveSource Source { get => Current.Source; set { if (value != Source) Change(l => l with { Source = value }); } }
    public string Notes
    {
        get => string.Join(", ", Current.Notes.Select(n => n.ToString("0.##", CultureInfo.InvariantCulture)));
        set
        {
            double[] notes;
            try { notes = value.Split(',', StringSplitOptions.TrimEntries).Select(n => double.Parse(n, CultureInfo.InvariantCulture)).ToArray(); }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            { throw new ArgumentException("Use comma-separated semitone offsets, e.g. 0, 4, 7, 12."); }
            if (notes.Length is < 1 or > 16 || notes.Any(n => !double.IsFinite(n) || n < -48 || n > 48))
                throw new ArgumentException("Use 1–16 notes, each between −48 and 48 semitones.");
            if (!notes.SequenceEqual(Current.Notes)) Change(l => l with { Notes = notes });
        }
    }
    public IReadOnlyList<NumberParameter> Parameters { get; }

    public LayerEditorViewModel(MainViewModel owner, int index)
    {
        this.owner = owner; this.index = index;
        NumberParameter P(string name, SoundConcept concept, string unit, double min, double max, double step, string help,
            Func<LayerSettings, double> read, Func<LayerSettings, double, LayerSettings> write) =>
            new(name, unit, min, max, step, help, () => read(Current), v => Change(l => write(l, v)), concept: concept, isRefreshing: () => owner.IsRefreshingEditors);
        Parameters = new NumberParameter[]
        {
            P("Level", SoundConcept.Output, "", 0, 1, .01, "Volume of this layer before mixing.", l => l.Level, (l,v) => l with { Level = v }),
            P("Start time", SoundConcept.Timing, "s", 0, 30, .01, "Delay before the layer starts. A start beyond the sound duration is silent.", l => l.StartSeconds, (l,v) => l with { StartSeconds = v }),
            P("Pitch", SoundConcept.Pitch, "Hz", 20, 12000, 1, "Base frequency; the simple Pitch control transposes every layer.", l => l.PitchHz, (l,v) => l with { PitchHz = v }),
            P("Pitch sweep", SoundConcept.Pitch, "st", -48, 48, .5, "Pitch change over the sound, or over each note in a loop. 12 semitones is an octave.", l => l.SweepSemitones, (l,v) => l with { SweepSemitones = v }),
            P("Attack", SoundConcept.Timing, "s", 0, 30, .005, "Time to rise to full volume. The envelope scales to fit short notes.", l => l.AttackSeconds, (l,v) => l with { AttackSeconds = v }),
            P("Decay", SoundConcept.Timing, "s", 0, 30, .005, "Time to fall from the peak to the sustain level.", l => l.DecaySeconds, (l,v) => l with { DecaySeconds = v }),
            P("Sustain", SoundConcept.Timing, "", 0, 1, .01, "Held level after the initial attack and decay.", l => l.Sustain, (l,v) => l with { Sustain = v }),
            P("Release", SoundConcept.Timing, "s", 0, 30, .005, "Time to fade after the gate closes. Included within the sound duration.", l => l.ReleaseSeconds, (l,v) => l with { ReleaseSeconds = v }),
            P("Gate", SoundConcept.Timing, "", .01, 1, .01, "Fraction of each note held before release. Single-note loop beds bypass the envelope.", l => l.Gate, (l,v) => l with { Gate = v }),
            P("Pan", SoundConcept.Output, "L / R", -1, 1, .05, "−1 is left, +1 is right. Used for stereo output only.", l => l.Pan, (l,v) => l with { Pan = v }),
            P("Note length", SoundConcept.Timing, "s", .03, 2, .01, "Spacing between sequence notes. Sequences repeat in loop mode.", l => l.NoteSeconds, (l,v) => l with { NoteSeconds = v })
        };
    }

    private void Change(Func<LayerSettings, LayerSettings> change) => owner.UpdateLayer(index, change);
    public void Refresh()
    {
        Notify(nameof(Enabled)); Notify(nameof(Source)); Notify(nameof(Notes));
        foreach (var parameter in Parameters) parameter.Refresh();
    }
}
