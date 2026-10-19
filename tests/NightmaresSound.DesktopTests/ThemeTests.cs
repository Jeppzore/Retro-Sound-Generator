using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NightmaresSound.App;
using NightmaresSound.Core;
using Xunit;

namespace NightmaresSound.DesktopTests;

public sealed partial class DesktopTests
{
    [Fact]
    public void InstrumentLabelsTolerateBindingTeardown()
    {
        var converter = new InstrumentLabelConverter();
        Assert.Equal("", converter.Convert(null!, typeof(string), null!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("Single note / hit", converter.Convert(InstrumentMode.SingleNote, typeof(string), null!, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public Task EducationalAccentsMatchTheControlsWithoutEditingTheRecipe() => OnUi(async () =>
    {
        var window = new MainWindow();
        var vm = (MainViewModel)window.DataContext;
        try
        {
            await vm.InitializeAsync();
            var before = RecipeFile.Serialize(vm.Current);
            Assert.Equal(new[] { SoundConcept.Timing, SoundConcept.Pitch, SoundConcept.Timbre, SoundConcept.Timbre, SoundConcept.Output },
                vm.SimpleParameters.Select(p => p.Concept));
            Assert.Equal(vm.SimpleParameters[2].Concept, vm.EffectGroups[0].Parameters[0].Concept);
            Assert.Equal(SoundConcept.Timing, vm.InstrumentTempoParameter.Concept);
            foreach (var layer in vm.Layers)
                Assert.Equal(new[] { SoundConcept.Output, SoundConcept.Timing, SoundConcept.Pitch, SoundConcept.Pitch,
                    SoundConcept.Timing, SoundConcept.Timing, SoundConcept.Timing, SoundConcept.Timing,
                    SoundConcept.Timing, SoundConcept.Output, SoundConcept.Timing }, layer.Parameters.Select(p => p.Concept));
            var content = ArrangeThemeWindow(window);
            var rows = Descendants<Grid>(content).Where(g => g.DataContext is NumberParameter && Enum.IsDefined(Semantic.GetConcept(g))).ToArray();
            Assert.NotEmpty(rows);
            foreach (var row in rows)
            {
                var parameter = Assert.IsType<NumberParameter>(row.DataContext);
                Assert.Equal(ThemeColor(parameter.Concept + "Brush"), ((SolidColorBrush)Semantic.GetAccent(row)).Color);
                Assert.StartsWith(Semantic.Describe(parameter.Concept), parameter.TeachingDescription);
                var slider = Assert.Single(Descendants<BoundedSlider>(row));
                Assert.Equal(((SolidColorBrush)Semantic.GetAccent(row)).Color, ((SolidColorBrush)Semantic.GetAccent(slider)).Color);
                Assert.IsType<Track>(slider.Template.FindName("PART_Track", slider));
            }
            Assert.Equal(before, RecipeFile.Serialize(vm.Current));

            foreach (string surface in new[] { "BackgroundBrush", "PanelBrush", "InputBrush", "HoverBrush", "SelectedBrush" })
            {
                foreach (string ink in new[] { "TextBrush", "MutedBrush", "PitchBrush", "TimbreBrush", "TimingBrush", "OutputBrush", "ErrorBrush", "WarningBrush" })
                    Assert.True(Contrast(ThemeColor(surface), ThemeColor(ink)) >= 4.5, ink + " on " + surface);
                Assert.True(Contrast(ThemeColor(surface), ThemeColor("FocusBrush")) >= 3);
            }
            foreach (string surface in new[] { "PanelBrush", "InputBrush" })
                Assert.True(Contrast(ThemeColor(surface), ThemeColor("ControlBorderBrush")) >= 3);
        }
        finally { vm.Dispose(); window.Close(); }
    });

    [Fact]
    public Task DarkDropdownsFocusAndSliderCommandsRemainUsable() => OnUi(async () =>
    {
        var window = new MainWindow();
        var vm = (MainViewModel)window.DataContext;
        try
        {
            window.Show();
            window.Activate();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await WaitForRender(vm);
            var content = (FrameworkElement)window.Content;
            var search = (TextBox)window.FindName("SearchBox");
            Keyboard.Focus(search);
            Assert.True(search.IsKeyboardFocusWithin);
            var chrome = Assert.IsType<Border>(search.Template.FindName("Chrome", search));
            Assert.Equal(ThemeColor("FocusBrush"), ((SolidColorBrush)chrome.BorderBrush).Color);
            Assert.True(search.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
            Assert.False(search.IsKeyboardFocusWithin);

            var combo = Descendants<ComboBox>(content).Single(c => AutomationProperties.GetName(c) == "Sample rate");
            combo.BringIntoView();
            Keyboard.Focus(combo);
            Assert.True(combo.IsKeyboardFocusWithin);
            Assert.Equal(Visibility.Visible, ((Border)combo.Template.FindName("FocusBorder", combo)).Visibility);
            combo.IsDropDownOpen = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var popup = Assert.IsType<Popup>(combo.Template.FindName("PART_Popup", combo));
            Assert.True(popup.IsOpen);
            var popupBorder = Assert.IsType<Border>(popup.Child);
            Assert.Equal(ThemeColor("PanelBrush"), ((SolidColorBrush)popupBorder.Background).Color);
            var selected = Assert.IsType<ComboBoxItem>(combo.ItemContainerGenerator.ContainerFromIndex(combo.SelectedIndex));
            Assert.True(selected.IsSelected);
            var selectedChrome = Assert.IsType<Border>(selected.Template.FindName("Chrome", selected));
            Assert.Equal(ThemeColor(selected.IsHighlighted ? "FocusBrush" : "TimbreBrush"), ((SolidColorBrush)selectedChrome.BorderBrush).Color);
            CaptureThemeElement(popupBorder, "dropdown");
            combo.IsDropDownOpen = false;

            var slider = Descendants<BoundedSlider>(content).First(s => ReferenceEquals(s.DataContext, vm.SimpleParameters[1]));
            slider.BringIntoView();
            Keyboard.Focus(slider);
            Assert.True(slider.IsKeyboardFocused);
            double before = slider.Value;
            Slider.IncreaseSmall.Execute(null, slider);
            Assert.Equal(before + slider.SmallChange, vm.SimpleParameters[1].Value, 8);
            await WaitForRender(vm);
        }
        finally { vm.Dispose(); window.Close(); }
    });

    [Fact]
    public Task ValidationClippingAndDisabledInstrumentsKeepTheirDistinctPresentation() => OnUi(async () =>
    {
        var window = new MainWindow();
        var vm = (MainViewModel)window.DataContext;
        try
        {
            await vm.InitializeAsync();
            vm.SimpleParameters[4].Value = 6;
            await WaitForRender(vm);
            Assert.NotEmpty(vm.ClipWarning);
            vm.SimpleParameters[0].Text = "invalid";
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var content = ArrangeThemeWindow(window);
            var box = Descendants<TextBox>(content).First(b => ReferenceEquals(b.DataContext, vm.SimpleParameters[0]));
            Assert.True(Validation.GetHasError(box));
            Assert.NotNull(box.ToolTip);
            var chrome = Assert.IsType<Border>(box.Template.FindName("Chrome", box));
            Assert.Equal(ThemeColor("ErrorBrush"), ((SolidColorBrush)chrome.BorderBrush).Color);
            var warning = Descendants<TextBlock>(content).Single(t => t.Text == vm.ClipWarning);
            Assert.Equal(ThemeColor("WarningBrush"), ((SolidColorBrush)warning.Foreground).Color);
            CaptureThemeElement(content, "validation-clipping", window.Background);

            vm.SelectedInstrument = InstrumentCatalog.Get("frame-drum");
            await WaitForRender(vm);
            content = ArrangeThemeWindow(window);
            foreach (string name in new[] { "Instrument key", "Instrument octave", "Instrument phrase", "Instrument scale" })
            {
                var disabled = Descendants<ComboBox>(content).Single(c => AutomationProperties.GetName(c) == name);
                Assert.False(disabled.IsEnabled);
                Assert.True(disabled.Opacity < 1);
            }
            CaptureThemeElement(content, "disabled-instrument", window.Background);
        }
        finally { vm.Dispose(); window.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task HighContrastPaletteUpdatesExistingControlsWithoutChangingTheRecipe(bool lightBackground) => OnUi(async () =>
    {
        var app = (NightmaresSound.App.App)Application.Current;
        var window = new MainWindow();
        var vm = (MainViewModel)window.DataContext;
        try
        {
            await vm.InitializeAsync();
            var content = ArrangeThemeWindow(window);
            var before = RecipeFile.Serialize(vm.Current);
            var theme = app.Resources.MergedDictionaries[0];
            NightmaresSound.App.App.ApplyHighContrast(theme);
            Assert.Equal(SystemColors.WindowBrush, window.Background);
            Assert.Equal(SystemColors.WindowTextBrush, window.Foreground);
            Assert.Equal(SystemColors.HighlightBrush, app.FindResource("SelectionBrush"));
            var waveform = Assert.Single(Descendants<WaveformView>(content));
            Assert.Equal(SystemColors.WindowTextBrush, waveform.Trace);
            var row = Descendants<Grid>(content).First(g => ReferenceEquals(g.DataContext, vm.SimpleParameters[4]) && Enum.IsDefined(Semantic.GetConcept(g)));
            Assert.Equal(SystemColors.WindowTextBrush, Semantic.GetAccent(row));
            // Exercise live resource updates for both contrast polarities without changing OS settings.
            var background = lightBackground ? Brushes.White : Brushes.Black;
            var foreground = lightBackground ? Brushes.Black : Brushes.White;
            foreach (string key in new[] { "BackgroundBrush", "TextureBrush", "PanelBrush", "InputBrush", "HoverBrush", "SelectedBrush" }) theme[key] = background;
            foreach (string key in new[] { "TextBrush", "MutedBrush", "BorderBrush", "ControlBorderBrush", "FocusBrush", "PitchBrush", "TimbreBrush", "TimingBrush", "OutputBrush", "ErrorBrush", "WarningBrush" }) theme[key] = foreground;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            content = ArrangeThemeWindow(window);
            Assert.Equal(foreground, waveform.Trace);
            Assert.Equal(before, RecipeFile.Serialize(vm.Current));
            CaptureThemeElement(content, lightBackground ? "high-contrast-light" : "high-contrast-dark", window.Background);
        }
        finally { app.ApplyAccessibilityPalette(); vm.Dispose(); window.Close(); }
    });

    private static FrameworkElement ArrangeThemeWindow(Window window)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(1292, 802));
        content.Arrange(new Rect(0, 0, 1292, 802));
        content.UpdateLayout();
        return content;
    }

    private static Color ThemeColor(string key) => ((SolidColorBrush)Application.Current.FindResource(key)).Color;
    private static double Contrast(Color a, Color b)
    {
        static double Linear(byte value) { double v = value / 255.0; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
        static double Luminance(Color c) => .2126 * Linear(c.R) + .7152 * Linear(c.G) + .0722 * Linear(c.B);
        double first = Luminance(a), second = Luminance(b);
        return (Math.Max(first, second) + .05) / (Math.Min(first, second) + .05);
    }

    private static void CaptureThemeElement(FrameworkElement element, string name, Brush? background = null)
    {
        int width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth + element.Margin.Left + element.Margin.Right));
        int height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight + element.Margin.Top + element.Margin.Bottom));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) dc.DrawRectangle(background ?? Brushes.Transparent, null, new Rect(0, 0, width, height));
        bitmap.Render(visual); bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        string dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/ui-checks"));
        Directory.CreateDirectory(dir);
        using var file = File.Create(Path.Combine(dir, name + ".png")); encoder.Save(file);
    }
}
