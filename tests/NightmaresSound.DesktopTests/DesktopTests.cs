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
    [InlineData(1292, 802, 96, "normal")]
    [InlineData(872, 442, 96, "small")]
    [InlineData(1000, 580, 144, "scaled")]
    public Task WindowRendersWithoutBindingErrorsAndKeepsPrimaryActionsVisible(int width, int height, int dpi, string name) => OnUi(async () =>
    {
        var window = new MainWindow();
        var vm = (MainViewModel)window.DataContext;
        try
        {
            await vm.InitializeAsync();
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
