using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using NightmaresSound.App;
using NightmaresSound.Core;
using Xunit;

namespace NightmaresSound.DesktopTests;

public sealed partial class DesktopTests
{
    [Fact]
    public Task AcousticEnsembleEndpointsRemainValidWithFractionalVoiceTuning() => OnUi(async () =>
    {
        using var vm = new MainViewModel(new SilentPreview());
        await vm.InitializeAsync();
        foreach (var instrument in InstrumentCatalog.All.Where(i => i.HasAcoustic))
        foreach (var mode in Enum.GetValues<InstrumentMode>())
        {
            vm.SelectedProfile = SoundCatalog.Profiles.First(p => p.Id == "clink");
            vm.SelectedInstrument = InstrumentCatalog.Get("harp");
            var voice = vm.InstrumentVoices[1];
            voice.SelectedInstrument = instrument;
            voice.Style = InstrumentStyle.Acoustic;
            voice.Enabled = true;
            vm.InstrumentMode = mode;
            var pitch = vm.SimpleParameters[1];
            var vibrato = vm.EffectGroups.SelectMany(g => g.Parameters).Single(p => p.Label == "Vibrato depth");
            foreach (double tuning in new[] { .01, .33, -.33, 1.1 })
            {
                vibrato.SliderValue = 0;
                voice.Parameters[1].SliderValue = 0;
                pitch.SliderValue = 0;
                voice.Parameters[1].SliderValue = tuning;
                vibrato.SliderValue = .33;
                foreach (bool minimum in new[] { true, false })
                {
                    double endpoint = minimum ? pitch.Minimum : pitch.Maximum;
                    pitch.SliderValue = endpoint;
                    Assert.False(pitch.HasErrors, instrument.Id + " pitch: " + string.Join(", ", pitch.GetErrors(nameof(NumberParameter.Text)).Cast<string>()));
                    Assert.Equal(endpoint, pitch.Value);
                    double tuneEndpoint = minimum ? voice.Parameters[1].Minimum : voice.Parameters[1].Maximum;
                    voice.Parameters[1].SliderValue = tuneEndpoint;
                    Assert.False(voice.Parameters[1].HasErrors, instrument.Id + " tuning");
                    RecipeValidator.Validate(vm.Current);
                }
            }
        }
        await WaitForRender(vm);
    });

    [Fact]
    public Task AudioTeardownFailureCannotCrashClosingAndDetachesCallbacks() => OnUi(async () =>
    {
        var preview = new FailingTeardownPreview();
        var vm = new MainViewModel(preview);
        await vm.InitializeAsync();
        Assert.Null(Record.Exception(vm.Dispose));
        Assert.Null(Record.Exception(vm.Dispose));
        Assert.Equal(0, preview.Subscribers);
        Assert.Equal(1, preview.DisposeCalls);
        Assert.False(vm.PlayCommand.CanExecute(null));
        Assert.False(vm.ExportCommand.CanExecute(null));
    });

    private sealed class FailingTeardownPreview : IAudioPreview
    {
        public int Subscribers { get; private set; }
        public int DisposeCalls { get; private set; }
        public event Action<string?>? Stopped { add => Subscribers++; remove => Subscribers--; }
        public void Play(RenderedSound sound, bool loop) { }
        public void Stop() => throw new System.IO.IOException("Device disconnected during stop.");
        public void Dispose() { DisposeCalls++; throw new System.IO.IOException("Device disconnected during disposal."); }
    }

    [Fact]
    public Task RefreshOfOneParameterCannotWriteAnotherParameterOrCreateHistory() => OnUi(async () =>
    {
        using var vm = new MainViewModel(new SilentPreview());
        await vm.InitializeAsync();
        vm.SelectedInstrument = InstrumentCatalog.Get("recorder");
        vm.InstrumentStyle = InstrumentStyle.Acoustic;
        vm.SimpleParameters[1].SliderValue = 5;
        bool attempted = false;
        vm.SimpleParameters[0].PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(NumberParameter.Minimum) || attempted) return;
            attempted = true;
            vm.SimpleParameters[1].SliderValue = 0;
        };
        vm.SimpleParameters[1].SliderValue = 6;
        Assert.True(attempted);
        Assert.Equal(6, vm.Current.PitchSemitones);
        vm.UndoCommand.Execute(null);
        Assert.Equal(5, vm.Current.PitchSemitones);
        await WaitForRender(vm);
    });

    [Theory]
    [InlineData(872, 442, 96)]
    [InlineData(1000, 580, 144)]
    public Task ThreeVoiceControlsRenderWithoutBindingErrors(int width, int height, int dpi) => OnUi(async () =>
    {
        var window = new MainWindow();
        var vm = (MainViewModel)window.DataContext;
        try
        {
            await vm.InitializeAsync();
            vm.SelectedInstrument = InstrumentCatalog.Get("harp");
            vm.InstrumentVoices[1].SelectedInstrument = InstrumentCatalog.Get("recorder");
            vm.InstrumentVoices[1].Style = InstrumentStyle.Acoustic;
            vm.InstrumentVoices[1].Enabled = true;
            vm.InstrumentVoices[2].SelectedInstrument = InstrumentCatalog.Get("frame-drum");
            vm.InstrumentVoices[2].Enabled = true;
            vm.InstrumentMode = InstrumentMode.Phrase;
            await WaitForRender(vm);
            var content = (FrameworkElement)window.Content;
            content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
            foreach (var element in Descendants<FrameworkElement>(content))
            {
                Assert.False(Validation.GetHasError(element), element.GetType().Name);
                var values = element.GetLocalValueEnumerator();
                while (values.MoveNext())
                    Assert.False(System.Windows.Data.BindingOperations.GetBindingExpressionBase(element, values.Current.Property)?.HasError == true);
            }
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width * dpi / 96, height * dpi / 96, dpi, dpi, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(content);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            string folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/ui-checks"));
            System.IO.Directory.CreateDirectory(folder);
            using (var file = System.IO.File.Create(System.IO.Path.Combine(folder, $"ensemble-{dpi}.png"))) encoder.Save(file);
            foreach (var group in Descendants<GroupBox>(content).Where(g => g.Header is string title && title.StartsWith("Voice ")))
                CaptureThemeElement(group, $"ensemble-{dpi}-{group.Header}", window.Background);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task EveryVisibleSliderHandlesThumbDeltasTrackCommandsAndKeyboardEndpoints() => OnUi(async () =>
    {
        var window = new MainWindow();
        var vm = (MainViewModel)window.DataContext;
        try
        {
            await vm.InitializeAsync();
            var content = ArrangeThemeWindow(window);
            var inputSource = System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle())!;
            var sliders = Descendants<BoundedSlider>(content).Where(s => s.DataContext is NumberParameter && s.ActualWidth > 0).ToArray();
            Assert.True(sliders.Length >= 49);
            foreach (var slider in sliders)
            {
                var parameter = (NumberParameter)slider.DataContext;
                var thumb = Assert.Single(Descendants<Thumb>(slider));
                thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
                foreach (double delta in new[] { 1.0, 50, -10, 10000, -10000, .1, -.1 })
                    thumb.RaiseEvent(new DragDeltaEventArgs(delta, 0) { RoutedEvent = Thumb.DragDeltaEvent });
                thumb.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
                foreach (var command in new[] { Slider.IncreaseLarge, Slider.DecreaseLarge, Slider.IncreaseSmall, Slider.DecreaseSmall, Slider.MinimizeValue, Slider.MaximizeValue })
                    command.Execute(null, slider);
                foreach (Key key in new[] { Key.Home, Key.Right, Key.PageUp, Key.End, Key.Left, Key.PageDown })
                {
                    slider.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, inputSource, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                    slider.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, inputSource, 0, key) { RoutedEvent = Keyboard.KeyDownEvent });
                    slider.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, inputSource, 0, key) { RoutedEvent = Keyboard.PreviewKeyUpEvent });
                }
                Assert.NotNull(slider.GetBindingExpression(Slider.ValueProperty));
                Assert.False(parameter.HasErrors, parameter.Label);
                Assert.InRange(parameter.Value, parameter.Minimum, parameter.Maximum);
                RecipeValidator.Validate(vm.Current);
            }
            await WaitForRender(vm);
            Assert.Equal(new SoundRenderer().Render(vm.Current).Pcm16, vm.Sound!.Pcm16);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task EnsembleEditsConstrainEveryAcousticVoiceAndPreserveHistory() => OnUi(async () =>
    {
        using var vm = new MainViewModel(new SilentPreview());
        await vm.InitializeAsync();
        vm.SelectedInstrument = InstrumentCatalog.Get("harp");
        var second = vm.InstrumentVoices[1];
        second.SelectedInstrument = InstrumentCatalog.Get("recorder");
        second.Style = InstrumentStyle.Acoustic;
        second.Enabled = true;
        vm.InstrumentMode = InstrumentMode.Phrase;
        var pitch = vm.SimpleParameters[1];
        Assert.Equal(-2, pitch.Minimum); Assert.Equal(8, pitch.Maximum);
        vm.BeginGesture();
        second.Parameters[1].SliderValue = .1;
        second.Parameters[1].SliderValue = .5;
        vm.EndGesture();
        Assert.Equal(7.5, pitch.Maximum);
        vm.UndoCommand.Execute(null);
        Assert.Equal(0, second.Parameters[1].Value);
        vm.RedoCommand.Execute(null);
        Assert.Equal(.5, second.Parameters[1].Value);
        string before = RecipeFile.Serialize(vm.Current);
        Assert.Null(Record.Exception(() => second.Octave = 3));
        Assert.Contains("acoustic", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, RecipeFile.Serialize(vm.Current));
        vm.InstrumentVoices[2].SelectedInstrument = InstrumentCatalog.Get("frame-drum");
        vm.InstrumentVoices[2].Enabled = true;
        vm.Loop = true;
        vm.InstrumentTempoParameter.SliderValue = 200;
        Assert.Equal(2.4, vm.Current.DurationSeconds);
        foreach (var voice in vm.InstrumentVoices) voice.Enabled = false;
        await WaitForRender(vm);
        Assert.All(vm.Sound!.Pcm16, value => Assert.Equal((byte)0, value));
        vm.SelectedProfile = SoundCatalog.Profiles.First(p => p.Id == "instrument-harp");
        // Selecting a different starting point resets the ensemble.
        vm.SelectedProfile = SoundCatalog.Profiles.First(p => p.Id == "instrument-lute");
        Assert.Single(vm.Current.Instrument!.Voices);
        Assert.True(vm.InstrumentVoices[0].Enabled);
        Assert.False(vm.InstrumentVoices[1].Enabled);
        await WaitForRender(vm);
    });

    [Fact]
    public Task ClosingDuringRapidLayeredEditsCancelsPendingRendering() => OnUi(async () =>
    {
        var window = new MainWindow();
        var vm = (MainViewModel)window.DataContext;
        await vm.InitializeAsync();
        vm.SelectedInstrument = InstrumentCatalog.Get("harp");
        vm.InstrumentVoices[1].Enabled = true;
        vm.InstrumentVoices[2].Enabled = true;
        vm.InstrumentMode = InstrumentMode.Phrase;
        for (int i = 0; i < 50; i++) vm.InstrumentVoices[i % 3].Parameters[2].SliderValue = i % 2 == 0 ? -1 : 1;
        Assert.True(vm.IsRendering);
        Assert.False(vm.ExportCommand.CanExecute(null));
        window.Close();
        await Task.Delay(250);
    });

    [Fact]
    public Task LostSliderCaptureCommitsGestureBeforeTheNextEdit() => OnUi(async () =>
    {
        var window = new MainWindow();
        var vm = (MainViewModel)window.DataContext;
        try
        {
            await vm.InitializeAsync();
            var content = ArrangeThemeWindow(window);
            var slider = Descendants<BoundedSlider>(content).First(s => ReferenceEquals(s.DataContext, vm.SimpleParameters[1]));
            vm.BeginGesture();
            vm.SimpleParameters[1].SliderValue = 5;
            slider.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.LostMouseCaptureEvent });
            vm.SimpleParameters[4].SliderValue = -12;
            vm.UndoCommand.Execute(null);
            Assert.Equal(5, vm.Current.PitchSemitones);
            Assert.Equal(0, vm.Current.OutputDb);
            vm.UndoCommand.Execute(null);
            Assert.Equal(0, vm.Current.PitchSemitones);
            await WaitForRender(vm);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task BoundSlidersSurviveDisjointRangeChangesAndRepeatedPresetTransitions() => OnUi(async () =>
    {
        var window = new MainWindow();
        var vm = (MainViewModel)window.DataContext;
        try
        {
            await vm.InitializeAsync();
            ArrangeThemeWindow(window);
            for (int pass = 0; pass < 8; pass++)
            {
                vm.SelectedInstrument = InstrumentCatalog.Get("recorder");
                vm.InstrumentStyle = InstrumentStyle.Acoustic;
                vm.InstrumentOctave = pass % 2 == 0 ? 4 : 5;
                foreach (var parameter in vm.SimpleParameters.Concat(vm.EffectGroups.SelectMany(g => g.Parameters)).Append(vm.InstrumentTempoParameter))
                {
                    double original = parameter.Value;
                    parameter.SliderValue = parameter.Minimum;
                    parameter.SliderValue = parameter.Maximum;
                    Assert.False(parameter.HasErrors, parameter.Label);
                    parameter.SliderValue = Math.Clamp(original, parameter.Minimum, parameter.Maximum);
                    RecipeValidator.Validate(vm.Current);
                }
                vm.SelectedProfile = SoundCatalog.Profiles.First(p => p.Id == "clink");
                var expected = RecipeFile.Serialize(vm.Current);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Equal(expected, RecipeFile.Serialize(vm.Current));
            }
            await WaitForRender(vm);
            Assert.Equal(new SoundRenderer().Render(vm.Current).Pcm16, vm.Sound!.Pcm16);
        }
        finally { window.Close(); }
    });
}
