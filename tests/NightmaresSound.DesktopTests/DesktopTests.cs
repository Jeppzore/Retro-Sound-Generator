using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NightmaresSound.App;
using NightmaresSound.Core;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace NightmaresSound.DesktopTests;

public sealed class DesktopTests
{
    private static readonly Lazy<Dispatcher> Ui = new(() =>
    {
        var ready = new TaskCompletionSource<Dispatcher>();
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            var app = new NightmaresSound.App.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.InitializeComponent();
            ready.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        }) { IsBackground = true, Name = "WPF test dispatcher" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task.GetAwaiter().GetResult();
    });

    private static Task OnUi(Func<Task> action) => Ui.Value.InvokeAsync(action).Task.Unwrap();

    [Fact]
    public Task ControlsShareStateAndUndoGroupsAnEntireGesture() => OnUi(async () =>
    {
        using var vm = new MainViewModel(new SilentPreview());
        await vm.InitializeAsync();
        Assert.False(vm.IsPlaying);
        vm.BeginGesture();
        vm.SimpleParameters[2].Value = 2000;
        vm.SimpleParameters[2].Value = 3000;
        vm.EndGesture();
        Assert.Equal(3000, vm.EffectGroups[0].Parameters[0].Value);
        vm.UndoCommand.Execute(null);
        Assert.Equal(10000, vm.SimpleParameters[2].Value);
        vm.RedoCommand.Execute(null);
        Assert.Equal(3000, vm.SimpleParameters[2].Value);
        vm.EffectGroups[0].Parameters[0].Value = 4500;
        Assert.Equal(4500, vm.SimpleParameters[2].Value);
        Assert.Throws<ArgumentException>(() => vm.SimpleParameters[0].Value = double.NaN);
        Assert.Throws<ArgumentException>(() => vm.Layers[0].Notes = "oops");
        Assert.Equal(.38, vm.Current.DurationSeconds);
        await WaitForRender(vm);
    });

    [Fact]
    public Task SearchSelectionRandomizationAndUndoStayInSync() => OnUi(async () =>
    {
        using var vm = new MainViewModel(new SilentPreview());
        await vm.InitializeAsync();
        vm.SearchText = "loop";
        Assert.Contains(vm.VisibleProfiles, p => p.Id == "rain");
        vm.SelectedProfile = vm.VisibleProfiles.First(p => p.Id == "rain");
        Assert.Equal("weather", vm.SelectedCategory.Id);
        Assert.True(vm.Loop);
        vm.SampleRate = 48000; vm.Channels = 2;
        var before = RecipeFile.Serialize(vm.Current);
        vm.RandomizeCommand.Execute(null);
        Assert.Equal("rain", vm.SelectedProfile.Id); Assert.True(vm.Loop);
        Assert.Equal(48000, vm.SampleRate); Assert.Equal(2, vm.Channels);
        vm.UndoCommand.Execute(null);
        Assert.Equal(before, RecipeFile.Serialize(vm.Current));
        vm.SelectedCategory = SoundCatalog.Categories.First(c => c.Id == "ui");
        Assert.Equal("confirm", vm.SelectedProfile.Id);
        Assert.Equal("", vm.SearchText);
        Assert.Equal(2, vm.VisibleProfiles.Count);
        await WaitForRender(vm);
        Assert.False(vm.IsPlaying);
    });

    [Fact]
    public Task UnavailableAudioDoesNotDisableExport() => OnUi(async () =>
    {
        using var vm = new MainViewModel(new SilentPreview(fail: true));
        await vm.InitializeAsync();
        var sound = vm.Sound;
        vm.PlayCommand.Execute(null);
        Assert.Contains("WAV export is still available", vm.ErrorMessage);
        Assert.True(vm.ExportCommand.CanExecute(null));
        Assert.Same(sound, vm.Sound);
        Assert.False(vm.IsPlaying);
    });

    [Fact]
    public Task RapidEditsUpdateTheDisplayedAndExportableBuffer() => OnUi(async () =>
    {
        using var vm = new MainViewModel(new SilentPreview());
        await vm.InitializeAsync();
        vm.SelectedCategory = SoundCatalog.Categories.First(c => c.Id == "weather");
        for (int i = 0; i < 20; i++) vm.SimpleParameters[1].Value = i;
        Assert.False(vm.ExportCommand.CanExecute(null));
        await WaitForRender(vm);
        var expected = new SoundRenderer().Render(vm.Current);
        Assert.NotNull(vm.Sound); Assert.Equal(expected.Pcm16, vm.Sound.Pcm16);
        Assert.True(vm.ExportCommand.CanExecute(null));
    });

    [Theory]
    [InlineData(1292, 802, 96, "normal", "")]
    [InlineData(872, 442, 96, "small", "")]
    [InlineData(1000, 580, 144, "scaled", "")]
    [InlineData(1292, 802, 96, "instruments", "harp")]
    [InlineData(872, 442, 96, "instruments-small", "recorder")]
    [InlineData(1000, 580, 144, "instruments-scaled", "frame-drum")]
    public Task WindowRendersWithoutBindingErrorsAndKeepsPrimaryActionsVisible(int width, int height, int dpi, string name, string instrumentId) => OnUi(async () =>
    {
        var window = new MainWindow();
        var vm = (MainViewModel)window.DataContext;
        try
        {
            await vm.InitializeAsync();
            if (instrumentId.Length > 0)
            {
                vm.SelectedInstrument = InstrumentCatalog.Get(instrumentId);
                vm.InstrumentStyle = InstrumentStyle.Acoustic;
                vm.InstrumentMode = InstrumentMode.Phrase;
                await WaitForRender(vm);
            }
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var content = (FrameworkElement)window.Content;
            content.Measure(new Size(width, height));
            content.Arrange(new Rect(0, 0, width, height));
            content.UpdateLayout();
            var buttons = Descendants<Button>(content).Where(b => b.Command is not null).ToList();
            Assert.Equal(8, buttons.Count);
            foreach (var button in buttons)
            {
                var rect = button.TransformToAncestor(content).TransformBounds(new Rect(button.RenderSize));
                Assert.True(rect.Left >= 0 && rect.Right <= width, $"{button.Content} is clipped horizontally at {width}px: {rect}");
                Assert.True(rect.Top >= 0 && rect.Bottom < height, $"{button.Content} is clipped vertically.");
            }
            foreach (var item in Descendants<FrameworkElement>(content))
            {
                Assert.False(Validation.GetHasError(item), $"Invalid binding on {item.GetType().Name}");
                var localValues = item.GetLocalValueEnumerator();
                while (localValues.MoveNext())
                {
                    var expression = System.Windows.Data.BindingOperations.GetBindingExpressionBase(item, localValues.Current.Property);
                    Assert.False(expression?.HasError == true, $"Binding failed on {item.GetType().Name}.{localValues.Current.Property.Name}");
                }
            }
            var bitmap = new RenderTargetBitmap((int)(width * dpi / 96.0), (int)(height * dpi / 96.0), dpi, dpi, PixelFormats.Pbgra32);
            var background = new DrawingVisual();
            using (var drawing = background.RenderOpen()) drawing.DrawRectangle(SystemColors.ControlBrush, null, new Rect(0, 0, width, height));
            bitmap.Render(background);
            bitmap.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/ui-checks"));
            Directory.CreateDirectory(dir);
            using var file = File.Create(Path.Combine(dir, name + ".png")); encoder.Save(file);
        }
        finally { vm.Dispose(); window.Close(); }
    });

    [Fact]
    public Task InstrumentsPreserveOutputUndoAndLatestPreview() => OnUi(async () =>
    {
        using var vm = new MainViewModel(new SilentPreview());
        await vm.InitializeAsync();
        vm.SampleRate = 48000; vm.Channels = 2;
        vm.SelectedInstrument = InstrumentCatalog.Get("lute");
        Assert.True(vm.IsInstrument); Assert.False(vm.IsSynthesis);
        Assert.Single(vm.InstrumentStyles);
        vm.InstrumentMode = InstrumentMode.Phrase;
        vm.InstrumentPhrase = InstrumentPhrase.Strum;
        vm.InstrumentTempo = 120;
        var before = RecipeFile.Serialize(vm.Current);
        Assert.Throws<ArgumentException>(() => vm.InstrumentTempo = 0);
        Assert.Equal(before, RecipeFile.Serialize(vm.Current));
        vm.InstrumentKey = 5;
        vm.UndoCommand.Execute(null);
        Assert.Equal(before, RecipeFile.Serialize(vm.Current));
        vm.SelectedInstrument = InstrumentCatalog.Get("harp");
        Assert.Equal(48000, vm.SampleRate); Assert.Equal(2, vm.Channels);
        vm.InstrumentStyle = InstrumentStyle.Acoustic;
        vm.InstrumentMode = InstrumentMode.Phrase;
        for (int tempo = 90; tempo <= 130; tempo += 10) vm.InstrumentTempo = tempo;
        await WaitForRender(vm);
        Assert.Equal(new SoundRenderer().Render(vm.Current).Pcm16, vm.Sound!.Pcm16);
        Assert.False(vm.IsPlaying);
        vm.Loop = true;
        Assert.Equal(8 * 60.0 / 130, vm.Current.DurationSeconds, 8);
        await WaitForRender(vm);
        Assert.Equal((int)Math.Round(vm.Current.DurationSeconds * 48000), vm.Sound!.FrameCount);
    });

    [Fact]
    public Task EveryNumericControlClampsSliderEndpointsAndRejectsInvalidTextWithoutThrowing() => OnUi(async () =>
    {
        using var vm = new MainViewModel(new SilentPreview());
        await vm.InitializeAsync();
        var parameters = vm.SimpleParameters.Concat(vm.EffectGroups.SelectMany(g => g.Parameters))
            .Concat(vm.Layers.SelectMany(l => l.Parameters)).ToArray();
        var thrown = new List<Exception>();
        void OnException(object? sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs args)
        {
            if (args.Exception is ArgumentException or InvalidDataException) thrown.Add(args.Exception);
        }
        AppDomain.CurrentDomain.FirstChanceException += OnException;
        try
        {
            foreach (var parameter in parameters)
            {
                var slider = new BoundedSlider { DataContext = parameter };
                slider.SetBinding(Slider.MinimumProperty, new System.Windows.Data.Binding(nameof(NumberParameter.Minimum)));
                slider.SetBinding(Slider.MaximumProperty, new System.Windows.Data.Binding(nameof(NumberParameter.Maximum)));
                slider.SetBinding(Slider.ValueProperty, new System.Windows.Data.Binding(nameof(NumberParameter.SliderValue)) { Mode = System.Windows.Data.BindingMode.TwoWay });
                foreach (double value in new[] { Math.BitDecrement(parameter.Minimum), parameter.Minimum - 100,
                    Math.BitIncrement(parameter.Maximum), parameter.Maximum + 100 })
                {
                    slider.Value = value;
                    Assert.InRange(slider.Value, parameter.Minimum, parameter.Maximum);
                    Assert.InRange(parameter.Value, parameter.Minimum, parameter.Maximum);
                    RecipeValidator.Validate(vm.Current);
                }
                foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                    parameter.SliderValue = value;
                double before = parameter.Value;
                foreach (string text in new[] { "not a number", "NaN", "Infinity", "1e999", "", (parameter.Maximum + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) })
                {
                    parameter.Text = text;
                    Assert.True(parameter.HasErrors, parameter.Label + " accepted " + text);
                    Assert.Equal(before, parameter.Value);
                }
                parameter.Text = parameter.Minimum.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
                Assert.False(parameter.HasErrors);
                Assert.Equal(parameter.Minimum, parameter.Value);
            }
        }
        finally { AppDomain.CurrentDomain.FirstChanceException -= OnException; }
        Assert.Empty(thrown);
        await WaitForRender(vm);
    });

    [Fact]
    public Task TypedValidationStaysOnTheControlAndRecoversOnAValidEdit() => OnUi(async () =>
    {
        using var vm = new MainViewModel(new SilentPreview());
        await vm.InitializeAsync();
        var parameter = vm.SimpleParameters[0];
        var box = new TextBox { DataContext = parameter };
        box.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding(nameof(NumberParameter.Text))
        { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.Explicit, ValidatesOnNotifyDataErrors = true });
        box.Text = "0.001";
        box.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.True(Validation.GetHasError(box));
        Assert.Equal(.38, vm.Current.DurationSeconds);
        box.Text = "0.03";
        box.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.False(Validation.GetHasError(box));
        Assert.Equal(.03, vm.Current.DurationSeconds);
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new("sv-SE");
            parameter.Text = "0,05";
            Assert.False(parameter.HasErrors);
            Assert.Equal(.05, vm.Current.DurationSeconds);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = culture; }
        await WaitForRender(vm);
    });

    [Fact]
    public Task InstrumentLimitsTrackTailsPitchVibratoAndTempoWithoutChangingTheRecipeDuringRefresh() => OnUi(async () =>
    {
        var window = new MainWindow();
        var vm = (MainViewModel)window.DataContext;
        try
        {
            await vm.InitializeAsync();
            var content = (FrameworkElement)window.Content;
            content.Measure(new Size(1292, 802)); content.Arrange(new Rect(0, 0, 1292, 802)); content.UpdateLayout();
            vm.SelectedInstrument = InstrumentCatalog.Get("recorder");
            vm.InstrumentStyle = InstrumentStyle.Acoustic;
            vm.InstrumentMode = InstrumentMode.Phrase;
            var expected = RecipeFile.Serialize(vm.Current);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(expected, RecipeFile.Serialize(vm.Current));
            var duration = vm.SimpleParameters[0];
            duration.SliderValue = .03;
            Assert.Equal(InstrumentCatalog.RequiredDuration(vm.Current.Instrument!), duration.Value);
            var pitch = vm.SimpleParameters[1];
            Assert.Equal(-2, pitch.Minimum); Assert.Equal(8, pitch.Maximum);
            pitch.SliderValue = 36;
            Assert.Equal(8, vm.Current.PitchSemitones);
            var vibrato = vm.EffectGroups.SelectMany(g => g.Parameters).Single(p => p.Label == "Vibrato depth");
            Assert.Equal(0, vibrato.Maximum); Assert.False(vibrato.IsEditable);
            vibrato.SliderValue = 2;
            Assert.Equal(0, vm.Current.Effects.VibratoSemitones);
            pitch.SliderValue = 0;
            vibrato.SliderValue = 2;
            Assert.Equal(0, pitch.Minimum); Assert.Equal(6, pitch.Maximum);
            pitch.SliderValue = -36;
            RecipeValidator.Validate(vm.Current);
            vm.Loop = true;
            Assert.Equal(duration.Minimum, duration.Maximum); Assert.False(duration.IsEditable);
            duration.Text = duration.Text;
            Assert.False(duration.HasErrors);
            vm.InstrumentTempoParameter.Text = "0";
            Assert.True(vm.InstrumentTempoParameter.HasErrors); Assert.Equal(90, vm.InstrumentTempo);
            vm.InstrumentTempoParameter.SliderValue = 250;
            Assert.Equal(200, vm.InstrumentTempo);
            Assert.Equal(2.4, duration.Minimum);
            vm.InstrumentTempoParameter.SliderValue = 1;
            Assert.Equal(40, vm.InstrumentTempo);
            Assert.Equal(12, duration.Minimum);
            vm.SelectedProfile = SoundCatalog.Profiles.First(p => p.Id == "clink");
            expected = RecipeFile.Serialize(vm.Current);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(expected, RecipeFile.Serialize(vm.Current));
            Assert.Equal(.03, duration.Minimum); Assert.Equal(30, duration.Maximum);
            Assert.Equal(.38, duration.Value); Assert.Equal(-36, pitch.Minimum); Assert.Equal(36, pitch.Maximum);
            await WaitForRender(vm);
        }
        finally { vm.Dispose(); window.Close(); }
    });

    [Fact]
    public Task AcousticSliderEndpointsRemainValidWithFractionalVibrato() => OnUi(async () =>
    {
        using var vm = new MainViewModel(new SilentPreview());
        await vm.InitializeAsync();
        foreach (var instrument in InstrumentCatalog.All.Where(i => i.HasAcoustic))
        {
            vm.SelectedInstrument = instrument;
            vm.InstrumentStyle = InstrumentStyle.Acoustic;
            foreach (var mode in Enum.GetValues<InstrumentMode>())
            {
                var pitch = vm.SimpleParameters[1];
                var vibrato = vm.EffectGroups.SelectMany(g => g.Parameters).Single(p => p.Label == "Vibrato depth");
                vibrato.SliderValue = 0; pitch.SliderValue = 0;
                vm.InstrumentMode = mode;
                foreach (double depth in new[] { .01, .33, 1.1, 2 })
                {
                    vibrato.SliderValue = 0; pitch.SliderValue = 0;
                    vibrato.SliderValue = depth;
                    foreach (bool minimum in new[] { true, false })
                    {
                        double endpoint = minimum ? pitch.Minimum : pitch.Maximum;
                        pitch.SliderValue = endpoint;
                        Assert.False(pitch.HasErrors, instrument.Id + ": " + string.Join(", ", pitch.GetErrors(nameof(NumberParameter.Text)).Cast<string>()));
                        Assert.Equal(endpoint, vm.Current.PitchSemitones);
                        RecipeValidator.Validate(vm.Current);
                    }
                }
            }
        }
        await WaitForRender(vm);
    });

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static async Task WaitForRender(MainViewModel vm)
    {
        var timeout = DateTime.UtcNow.AddSeconds(10);
        while (vm.IsRendering && DateTime.UtcNow < timeout) await Task.Delay(15);
        Assert.False(vm.IsRendering);
        Assert.NotNull(vm.Sound);
    }

    private sealed class SilentPreview(bool fail = false) : IAudioPreview
    {
        public event Action<string?>? Stopped { add { } remove { } }
        public void Play(RenderedSound sound, bool loop) { if (fail) throw new IOException("No output device."); }
        public void Stop() { }
        public void Dispose() { }
    }
}
