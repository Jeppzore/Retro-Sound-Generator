using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using NightmaresSound.Core;

namespace NightmaresSound.App;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly RecipeHistory history = new();
    private readonly RenderCoordinator renderer = new();
    private readonly IAudioPreview audio;
    private SoundRecipe? gestureStart;
    private SoundCategory category = SoundCatalog.Categories[0];
    private SoundProfile profile = SoundCatalog.Profiles[0];
    private string search = "", status = "Preparing waveform…", error = "";
    private string? exportFolder;
    private bool rendering, playing;
    private volatile bool disposed;
    private int revision;
    private RenderedSound? sound;
    private readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NightmaresSound", "settings.json");

    public SoundRecipe Current { get; private set; } = SoundCatalog.Create("clink");
    public IReadOnlyList<SoundCategory> Categories => SoundCatalog.Categories;
    public ObservableCollection<SoundProfile> VisibleProfiles { get; } = [];
    public IReadOnlyList<NumberParameter> SimpleParameters { get; }
    public IReadOnlyList<ParameterGroup> EffectGroups { get; }
    public IReadOnlyList<LayerEditorViewModel> Layers { get; }
    public IReadOnlyList<InstrumentVoiceViewModel> InstrumentVoices { get; }
    public InstrumentVoiceViewModel PrimaryInstrumentVoice => InstrumentVoices[0];
    public IEnumerable<InstrumentVoiceViewModel> AdditionalInstrumentVoices => InstrumentVoices.Skip(1);
    internal bool IsRefreshingEditors { get; private set; }
    public NumberParameter InstrumentTempoParameter { get; }
    public IReadOnlyList<InstrumentDefinition> Instruments => InstrumentCatalog.All;
    public bool IsInstrument => Current.Instrument is not null;
    public bool IsSynthesis => !IsInstrument;
    public bool IsPitchedInstrument => Current.Instrument?.Voices.Any(v => v.Enabled && !InstrumentCatalog.Get(v.Id).IsDrum) == true;
    public bool IsInstrumentPhrase => Current.Instrument?.Mode == InstrumentMode.Phrase;
    public InstrumentDefinition SelectedInstrument
    {
        get => InstrumentCatalog.Get(Current.Instrument?.Id ?? "guitar");
        set
        {
            if (value is null || value.Id == Current.Instrument?.Id) return;
            EndGesture();
            var next = SoundCatalog.Create("instrument-" + value.Id);
            Apply(next with { SampleRate = SampleRate, Channels = Channels, Normalize = Normalize, OutputDb = Current.OutputDb });
        }
    }
    public InstrumentStyle[] InstrumentStyles => SelectedInstrument.Styles;
    public InstrumentMode[] InstrumentModes { get; } = Enum.GetValues<InstrumentMode>();
    public MusicalScale[] MusicalScales { get; } = Enum.GetValues<MusicalScale>();
    public InstrumentPhrase[] InstrumentPhrases => SelectedInstrument.Phrases;
    public string[] MusicalKeys => InstrumentCatalog.Keys;
    public int[] InstrumentOctaves { get; } = [3, 4, 5];
    public InstrumentStyle InstrumentStyle { get => Current.Instrument?.Style ?? InstrumentStyle.Retro; set => ChangeInstrument(s => s with { Style = value }); }
    public InstrumentMode InstrumentMode { get => Current.Instrument?.Mode ?? InstrumentMode.SingleNote; set => ChangeInstrument(s => s with { Mode = value }); }
    public InstrumentPhrase InstrumentPhrase { get => Current.Instrument?.Phrase ?? InstrumentPhrase.Ascending; set => ChangeInstrument(s => s with { Phrase = value }); }
    public MusicalScale InstrumentScale { get => Current.Instrument?.Scale ?? MusicalScale.Dorian; set => ChangeInstrument(s => s with { Scale = value }); }
    public int InstrumentKey { get => Current.Instrument?.Key ?? 2; set { if (value >= 0) ChangeInstrument(s => s with { Key = value }); } }
    public int InstrumentOctave { get => Current.Instrument?.Octave ?? 4; set { if (value > 0) ChangeInstrument(s => s with { Octave = value }); } }
    public int InstrumentTempo { get => Current.Instrument?.Tempo ?? 90; set => ChangeInstrument(s => s with { Tempo = value }); }
    public string InstrumentHelp => SelectedInstrument.Id switch
    {
        "harp" => "Retro synthesis or acoustic folk harp. Acoustic range: C3–C6, including master pitch and vibrato. Single note plays the key's root.",
        "recorder" => "Retro synthesis or acoustic bass recorder. Acoustic range: C4–G♯5, including master pitch and vibrato. Use octave 4 for phrases in C–G♯.",
        "fiddle" => "Retro synthesis or acoustic solo violin. Acoustic range: C4–C♯6, including master pitch and vibrato. Use octave 4 for phrases in every key.",
        "frame-drum" => "Retro synthesis or an acoustic frame drum. Choose a single hit or a two-bar rhythm. The acoustic hit permits ±4 semitones of tuning.",
        _ => "Retro synthesis. No suitable CC0 sample bank is bundled for this instrument. Single note plays the key's root."
    };
    private void ChangeInstrument(Func<InstrumentSettings, InstrumentSettings> change)
    {
        if (Current.Instrument is null || IsRefreshingEditors) return;
        var settings = change(Current.Instrument);
        if (settings == Current.Instrument) return;
        EndGesture();
        var next = Current with { Instrument = settings, DurationSeconds = InstrumentCatalog.RequiredDuration(settings) };
        if (RejectAcousticEdit(next)) return;
        try { Apply(next); }
        catch (InvalidDataException ex) { throw new ArgumentException(ex.Message, ex); }
    }

    private bool RejectAcousticEdit(SoundRecipe next)
    {
        if (RecipeValidator.AcousticRangeError(next) is not { } message) return false;
        ShowErrorMessage(message);
        RefreshEditors();
        // WPF can defer a source-to-target update while a ComboBox setter runs.
        // Repeat the refresh after the rejected source update finishes.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.HasShutdownStarted)
            dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.DataBind, new Action(() =>
            {
                if (!disposed) RefreshEditors();
            }));
        return true;
    }
    public int[] SampleRates { get; } = [44100, 48000];
    public int[] ChannelCounts { get; } = [1, 2];
    public string SearchText { get => search; set { search = value; Notify(); FilterProfiles(); } }
    public SoundCategory SelectedCategory
    {
        get => category;
        set
        {
            if (value is null || category == value) return;
            EndGesture();
            search = ""; Notify(nameof(SearchText));
            LoadProfile(SoundCatalog.Profiles.First(p => p.CategoryId == value.Id));
        }
    }
    public SoundProfile SelectedProfile
    {
        get => profile;
        set { if (value is not null && value.Id != Current.ConfigurationId) { EndGesture(); LoadProfile(value); } }
    }
    public string ProfileDescription => profile.Description;
    public string RecipeName
    {
        get => Current.Name;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 120) throw new ArgumentException("Use a name of 1–120 characters.");
            if (value != Current.Name) Apply(Current with { Name = value });
        }
    }
    public int Seed { get => Current.Seed; set { if (value != Current.Seed) Apply(Current with { Seed = value }); } }
    public bool Loop { get => Current.Loop; set { if (value != Current.Loop) Apply(Current with { Loop = value }); } }
    public bool Normalize { get => Current.Normalize; set { if (value != Current.Normalize) Apply(Current with { Normalize = value }); } }
    public int SampleRate { get => Current.SampleRate; set { if (value != Current.SampleRate) Apply(Current with { SampleRate = value }); } }
    public int Channels { get => Current.Channels; set { if (value != Current.Channels) Apply(Current with { Channels = value }); } }
    public RenderedSound? Sound => sound;
    public bool IsRendering => rendering;
    public bool IsPlaying => playing;
    public string Status => status;
    public string ErrorMessage => error;
    public string PlaybackLabel => playing ? (Loop ? "Playing loop · Stop to finish" : "Playing") : "Stopped · press Play to listen";
    public string OutputSummary => sound is null ? "Preparing audio…" :
        $"{sound.DurationSeconds:0.###} s  ·  {sound.SampleRate / 1000.0:0.0} kHz  ·  {(sound.Channels == 1 ? "Mono" : "Stereo")}\n16-bit PCM WAV  ·  Peak {(double.IsNegativeInfinity(sound.PeakDb) ? "−∞" : sound.PeakDb.ToString("0.0"))} dBFS";
    public string ClipWarning => sound?.WouldClip == true
        ? $"Clipping: {sound.PreLimitPeakDb:0.0} dBFS before limiting. " + (Normalize ? "Lower Output level." : "Lower Output level or enable normalization.")
        : "";
    public string EmptySearchHint => VisibleProfiles.Count == 0 ? "No matches. Try another search." : "";

    public ICommand RandomizeCommand { get; }
    public ICommand PlayCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand SaveRecipeCommand { get; }
    public ICommand OpenRecipeCommand { get; }
    public ICommand ExportCommand { get; }

    public MainViewModel(IAudioPreview? preview = null)
    {
        audio = preview ?? new AudioPreview();
        NumberParameter P(string name, SoundConcept concept, string unit, double min, double max, double step, string help,
            Func<SoundRecipe, double> read, Func<SoundRecipe, double, SoundRecipe> write,
            Func<(double Minimum, double Maximum)>? bounds = null) =>
            new(name, unit, min, max, step, help, () => read(Current), v => Apply(write(Current, v)), bounds, concept, () => IsRefreshingEditors);
        NumberParameter E(string name, SoundConcept concept, string unit, double min, double max, double step, string help,
            Func<EffectSettings, double> read, Func<EffectSettings, double, EffectSettings> write,
            Func<(double Minimum, double Maximum)>? bounds = null) =>
            P(name, concept, unit, min, max, step, help, r => read(r.Effects), (r,v) => r with { Effects = write(r.Effects, v) }, bounds);
        InstrumentTempoParameter = new("Tempo", "BPM", 40, 200, 1, "Phrase tempo, from 40 to 200 beats per minute.",
            () => InstrumentTempo, v => InstrumentTempo = (int)Math.Round(v), concept: SoundConcept.Timing, isRefreshing: () => IsRefreshingEditors);
        SimpleParameters = new NumberParameter[]
        {
            P("Duration", SoundConcept.Timing, "s", .03, 30, .01, "Total file length. Instruments reserve their release tail; phrase loops have a fixed two-bar length.", r => r.DurationSeconds, (r,v) => r with { DurationSeconds = v }, DurationBounds),
            P("Pitch", SoundConcept.Pitch, "st", -36, 36, .5, "Transpose every layer. Acoustic limits account for all phrase notes and vibrato.", r => r.PitchSemitones, (r,v) => r with { PitchSemitones = v }, PitchBounds),
            E("Brightness", SoundConcept.Timbre, "Hz", 40, 20000, 10, "Low-pass cutoff. Lower values make the sound darker.", e => e.LowPassHz, (e,v) => e with { LowPassHz = v }),
            P("Noise amount", SoundConcept.Timbre, "", 0, 1, .01, "Controls noise layers and adds texture to tonal layers. Zero mutes noise sources.", r => r.NoiseAmount, (r,v) => r with { NoiseAmount = v }),
            P("Output level", SoundConcept.Output, "dB", -60, 6, .5, "Final gain, after optional normalization to −1 dBFS. Positive gain can clip.", r => r.OutputDb, (r,v) => r with { OutputDb = v })
        };
        EffectGroups = new ParameterGroup[]
        {
            new("Filters and texture", new NumberParameter[]
            {
                E("Low-pass cutoff", SoundConcept.Timbre, "Hz", 40, 20000, 10, "The same setting as Brightness above.", e => e.LowPassHz, (e,v) => e with { LowPassHz = v }),
                E("High-pass cutoff", SoundConcept.Timbre, "Hz", 0, 8000, 10, "Removes low frequencies. Zero bypasses this filter.", e => e.HighPassHz, (e,v) => e with { HighPassHz = v }),
                E("Saturation", SoundConcept.Timbre, "", 0, 1, .01, "Adds driven harmonics and soft distortion.", e => e.Saturation, (e,v) => e with { Saturation = v }),
                E("Bit reduction", SoundConcept.Timbre, "bits", 4, 16, 1, "Lower values add retro quantization. The exported file is always 16-bit PCM.", e => e.BitDepth, (e,v) => e with { BitDepth = (int)Math.Round(v) })
            }),
            new("Movement and echo", new NumberParameter[]
            {
                E("Vibrato speed", SoundConcept.Timing, "Hz", 0, 20, .1, "Speed of pitch movement.", e => e.VibratoHz, (e,v) => e with { VibratoHz = v }),
                E("Vibrato depth", SoundConcept.Timbre, "st", 0, 2, .01, "Amount of pitch movement. Acoustic limits keep every note within the bundled range.", e => e.VibratoSemitones, (e,v) => e with { VibratoSemitones = v }, VibratoBounds),
                E("Tremolo speed", SoundConcept.Timing, "Hz", 0, 30, .1, "Speed of rhythmic volume movement.", e => e.TremoloHz, (e,v) => e with { TremoloHz = v }),
                E("Tremolo depth", SoundConcept.Timbre, "", 0, 1, .01, "Amount of volume movement. Zero disables tremolo.", e => e.TremoloDepth, (e,v) => e with { TremoloDepth = v }),
                E("Delay time", SoundConcept.Timing, "s", .01, 1, .01, "Time between echoes. Loop echoes wrap around the file boundary.", e => e.DelaySeconds, (e,v) => e with { DelaySeconds = v }),
                E("Delay mix", SoundConcept.Timbre, "", 0, .8, .01, "Balance between the original sound and echoes. Zero disables delay.", e => e.DelayMix, (e,v) => e with { DelayMix = v }),
                E("Delay feedback", SoundConcept.Timbre, "", 0, .75, .01, "Amount returned to the next echo.", e => e.DelayFeedback, (e,v) => e with { DelayFeedback = v })
            })
        };
        Layers = Enumerable.Range(0, 3).Select(i => new LayerEditorViewModel(this, i)).ToArray();
        InstrumentVoices = Enumerable.Range(0, 3).Select(i => new InstrumentVoiceViewModel(this, i)).ToArray();
        RandomizeCommand = new UiCommand(() => Handle(() =>
        {
            EndGesture();
            Apply(RecipeRandomizer.Randomize(Current, RandomNumberGenerator.GetInt32(int.MaxValue)));
        }));
        PlayCommand = new UiCommand(Play, () => !disposed && sound is not null && !rendering);
        StopCommand = new UiCommand(() => { StopAudio(); SetStatus("Playback stopped."); }, () => !disposed && playing);
        UndoCommand = new UiCommand(() => { EndGesture(); Apply(history.Undo(Current), recordHistory: false); }, () => history.CanUndo);
        RedoCommand = new UiCommand(() => { EndGesture(); Apply(history.Redo(Current), recordHistory: false); }, () => history.CanRedo);
        SaveRecipeCommand = new UiCommand(() => Handle(SaveRecipe));
        OpenRecipeCommand = new UiCommand(() => Handle(OpenRecipe));
        ExportCommand = new AsyncUiCommand(ExportAsync, ShowError, () => !disposed && sound is not null && !rendering);
        audio.Stopped += OnAudioStopped;
        ReadSettings();
        FilterProfiles();
    }

    private void OnAudioStopped(string? message)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (disposed || dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
        dispatcher.BeginInvoke(() =>
        {
            if (disposed) return;
            playing = false; Notify(nameof(IsPlaying)); Notify(nameof(PlaybackLabel));
            if (message is not null) ShowError(new IOException($"Playback stopped: {message}. WAV export is still available."));
            else SetStatus("Playback finished.");
            CommandManager.InvalidateRequerySuggested();
        });
    }

    public Task InitializeAsync() => RenderAsync();
    private (double Minimum, double Maximum) DurationBounds()
    {
        if (Current.Instrument is null) return (.03, 30);
        if (Current.Loop && Current.Instrument.Mode == InstrumentMode.Phrase)
        {
            double duration = 8 * 60.0 / Current.Instrument.Tempo;
            return (duration, duration);
        }
        return (Current.Loop ? .03 : InstrumentCatalog.RequiredDuration(Current.Instrument), 30);
    }
    private (double Minimum, double Maximum) AcousticPitchBounds()
    {
        var settings = Current.Instrument!;
        var bounds = settings.Voices.Where(v => v.Enabled && v.Style == InstrumentStyle.Acoustic)
            .Select(v => { var b = InstrumentCatalog.AcousticPitchBounds(settings, v);
                return (Minimum: b.Minimum - v.TuningSemitones, Maximum: b.Maximum - v.TuningSemitones); }).ToArray();
        return (bounds.Select(b => b.Minimum).DefaultIfEmpty(-38).Max(), bounds.Select(b => b.Maximum).DefaultIfEmpty(38).Min());
    }
    private (double Minimum, double Maximum) PitchBounds()
    {
        if (Current.Instrument is null) return (-36, 36);
        var (minimum, maximum) = AcousticPitchBounds();
        return (Math.Max(-36, minimum + Current.Effects.VibratoSemitones), Math.Min(36, maximum - Current.Effects.VibratoSemitones));
    }
    private (double Minimum, double Maximum) VibratoBounds()
    {
        if (Current.Instrument is null) return (0, 2);
        var (minimum, maximum) = AcousticPitchBounds();
        return (0, Math.Max(0, Math.Min(2, Math.Min(Current.PitchSemitones - minimum, maximum - Current.PitchSemitones))));
    }
    public void BeginGesture() => gestureStart ??= Current;
    public void EndGesture()
    {
        if (gestureStart is null) return;
        history.Commit(gestureStart, Current);
        gestureStart = null;
        CommandManager.InvalidateRequerySuggested();
    }

    internal void UpdateLayer(int index, Func<LayerSettings, LayerSettings> change)
    {
        var layers = Enumerable.Range(0, 3).Select(i => Current.Layers.ElementAtOrDefault(i) ?? new() { Enabled = false }).ToArray();
        layers[index] = change(layers[index]);
        Apply(Current with { Layers = layers });
    }

    internal void UpdateInstrumentVoice(int index, Func<InstrumentVoiceSettings, InstrumentVoiceSettings> change)
    {
        if (Current.Instrument is null || IsRefreshingEditors) return;
        Apply(WithInstrumentVoice(index, change));
    }

    internal void ChangeInstrumentVoice(int index, Func<InstrumentVoiceSettings, InstrumentVoiceSettings> change)
    {
        if (Current.Instrument is null || IsRefreshingEditors) return;
        EndGesture();
        var next = WithInstrumentVoice(index, change);
        if (RejectAcousticEdit(next)) return;
        try { Apply(next); }
        catch (InvalidDataException ex) { throw new ArgumentException(ex.Message, ex); }
    }

    private SoundRecipe WithInstrumentVoice(int index, Func<InstrumentVoiceSettings, InstrumentVoiceSettings> change)
    {
        var instrument = Current.Instrument!;
        var voices = Enumerable.Range(0, 3).Select(i => instrument.Voices.ElementAtOrDefault(i)
            ?? new InstrumentVoiceSettings { Id = instrument.Id, Phrase = instrument.Phrase, Enabled = false }).ToArray();
        voices[index] = change(voices[index]);
        return Current with { Instrument = instrument with { Voices = voices }, ConfigurationId = "instrument-" + voices[0].Id };
    }

    private void LoadProfile(SoundProfile value) => Apply(SoundCatalog.Create(value.Id) with
    {
        SampleRate = Current.SampleRate, Channels = Current.Channels,
        Normalize = Current.Normalize, OutputDb = Current.OutputDb
    });

    private void Apply(SoundRecipe next, bool recordHistory = true)
    {
        if (disposed) return;
        if (next.Instrument is not null && !next.Loop)
            next = next with { DurationSeconds = Math.Max(next.DurationSeconds, InstrumentCatalog.RequiredDuration(next.Instrument)) };
        if (next.Instrument?.Mode == InstrumentMode.Phrase && next.Loop)
            next = next with { DurationSeconds = 8 * 60.0 / next.Instrument.Tempo };
        RecipeValidator.Validate(next);
        if (recordHistory && gestureStart is null) history.Commit(Current, next);
        StopAudio();
        Current = next;
        profile = SoundCatalog.Profiles.First(p => p.Id == next.ConfigurationId);
        category = SoundCatalog.Categories.First(c => c.Id == next.CategoryId);
        RefreshEditors();
        _ = RenderAsync();
    }

    private void RefreshEditors()
    {
        IsRefreshingEditors = true;
        try
        {
            foreach (var property in new[] { nameof(SelectedCategory), nameof(SelectedProfile), nameof(ProfileDescription), nameof(RecipeName), nameof(Seed), nameof(Loop), nameof(Normalize), nameof(SampleRate), nameof(Channels) }) Notify(property);
            foreach (var property in new[] { nameof(IsInstrument), nameof(IsSynthesis), nameof(IsPitchedInstrument), nameof(IsInstrumentPhrase),
                nameof(SelectedInstrument), nameof(InstrumentStyles), nameof(InstrumentPhrases), nameof(InstrumentStyle), nameof(InstrumentMode),
                nameof(InstrumentPhrase), nameof(InstrumentScale), nameof(InstrumentKey), nameof(InstrumentOctave), nameof(InstrumentTempo), nameof(InstrumentHelp) }) Notify(property);
            foreach (var p in SimpleParameters.Concat(EffectGroups.SelectMany(g => g.Parameters))) p.Refresh();
            InstrumentTempoParameter.Refresh();
            foreach (var l in Layers) l.Refresh();
            foreach (var voice in InstrumentVoices) voice.Refresh();
            FilterProfiles();
            CommandManager.InvalidateRequerySuggested();
        }
        finally { IsRefreshingEditors = false; }
    }

    private void FilterProfiles()
    {
        var terms = search.Trim();
        var matches = SoundCatalog.Profiles.Where(p => terms.Length == 0 ? p.CategoryId == category.Id :
            $"{p.Name} {p.Description} {SoundCatalog.Categories.First(c => c.Id == p.CategoryId).Name}"
                .Contains(terms, StringComparison.OrdinalIgnoreCase)).ToList();
        // Do not rebuild a selection-bound collection on every slider movement.
        if (!VisibleProfiles.SequenceEqual(matches))
        {
            VisibleProfiles.Clear();
            foreach (var p in matches) VisibleProfiles.Add(p);
        }
        Notify(nameof(SelectedProfile)); Notify(nameof(EmptySearchHint));
    }

    private async Task RenderAsync()
    {
        int version = ++revision;
        rendering = true; error = "";
        Notify(nameof(IsRendering)); Notify(nameof(ErrorMessage));
        SetStatus("Updating waveform…"); CommandManager.InvalidateRequerySuggested();
        try
        {
            var result = await renderer.UpdateAsync(Current);
            if (disposed || version != revision || result is null) return;
            sound = result;
            SetStatus("Ready. Play to listen, or export the current sound.");
        }
        catch (Exception ex) { if (!disposed && version == revision) { sound = null; ShowError(ex); } }
        finally
        {
            if (!disposed && version == revision)
            {
                rendering = false;
                Notify(nameof(IsRendering)); Notify(nameof(Sound)); Notify(nameof(OutputSummary)); Notify(nameof(ClipWarning));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    private void Play()
    {
        EndGesture();
        if (sound is null || rendering) return;
        Handle(() =>
        {
            audio.Play(sound, Loop);
            playing = true; Notify(nameof(IsPlaying)); Notify(nameof(PlaybackLabel));
            CommandManager.InvalidateRequerySuggested();
            SetStatus(Loop ? "Playing the exported loop buffer. Press Stop to finish." : "Playing the current sound.");
        }, "Playback unavailable. WAV export is still available. ");
    }

    private void StopAudio()
    {
        try { audio.Stop(); }
        catch (Exception ex) { ShowError(ex); }
        playing = false; Notify(nameof(IsPlaying)); Notify(nameof(PlaybackLabel));
        CommandManager.InvalidateRequerySuggested();
    }

    private void SaveRecipe()
    {
        EndGesture();
        var dialog = new SaveFileDialog { Filter = "Sound recipe (*.json)|*.json", DefaultExt = ".json", FileName = SafeName(Current.Name) + ".json", OverwritePrompt = true };
        if (dialog.ShowDialog() != true) return;
        var bytes = Encoding.UTF8.GetBytes(RecipeFile.Serialize(Current));
        AtomicFile.Write(dialog.FileName, stream => stream.Write(bytes));
        SetStatus($"Recipe saved: {dialog.FileName}");
    }

    private void OpenRecipe()
    {
        EndGesture();
        var dialog = new OpenFileDialog { Filter = "Sound recipe (*.json)|*.json", CheckFileExists = true };
        if (dialog.ShowDialog() != true) return;
        if (new FileInfo(dialog.FileName).Length > 256 * 1024) throw new InvalidDataException("Recipe is too large (maximum 256 KB).");
        var next = RecipeFile.Deserialize(File.ReadAllText(dialog.FileName));
        search = ""; Notify(nameof(SearchText));
        Apply(next);
    }

    private async Task ExportAsync()
    {
        EndGesture();
        if (sound is null || rendering) return;
        var snapshot = sound;
        var dialog = new SaveFileDialog
        {
            Filter = "16-bit PCM WAV (*.wav)|*.wav", DefaultExt = ".wav", AddExtension = true,
            FileName = SafeName(Current.Name) + ".wav", OverwritePrompt = true,
            InitialDirectory = exportFolder is not null && Directory.Exists(exportFolder) ? exportFolder : ""
        };
        if (dialog.ShowDialog() != true) return;
        SetStatus("Writing 16-bit PCM WAV…");
        await Task.Run(() => WavExporter.Save(dialog.FileName, snapshot));
        exportFolder = Path.GetDirectoryName(dialog.FileName);
        SetStatus($"WAV exported: {dialog.FileName}");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new UserSettings(exportFolder));
            AtomicFile.Write(settingsPath, stream => stream.Write(bytes));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { SetStatus($"WAV exported. Could not remember the folder: {ex.Message}"); }
    }

    private void ReadSettings()
    {
        try
        {
            if (File.Exists(settingsPath) && new FileInfo(settingsPath).Length < 16384)
                exportFolder = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(settingsPath))?.ExportFolder;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { /* A bad preference must not block startup. */ }
    }

    private static string SafeName(string value) => string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    private void Handle(Action action, string prefix = "")
    {
        try { action(); }
        catch (Exception ex) { ShowError(new InvalidOperationException(prefix + ex.Message, ex)); }
    }
    private void ShowError(Exception ex) => ShowErrorMessage(ex.Message);
    private void ShowErrorMessage(string message) { error = message; Notify(nameof(ErrorMessage)); SetStatus("Action could not be completed. See the message above."); }
    private void SetStatus(string value) { status = value; Notify(nameof(Status)); }
    public void Dispose()
    {
        if (disposed) return;
        EndGesture();
        disposed = true;
        audio.Stopped -= OnAudioStopped;
        renderer.Dispose();
        StopAudio();
        try { audio.Dispose(); }
        catch (Exception ex) { ShowError(ex); }
        rendering = false;
        sound = null;
        CommandManager.InvalidateRequerySuggested();
    }
    private sealed record UserSettings(string? ExportFolder);
}
