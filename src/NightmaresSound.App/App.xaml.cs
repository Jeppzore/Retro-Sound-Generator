using System.Windows;
using System.ComponentModel;
using System.Windows.Media;

namespace NightmaresSound.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ApplyAccessibilityPalette();
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
            Dispatcher.Invoke(ApplyAccessibilityPalette);
    }

    public void ApplyAccessibilityPalette()
    {
        // Update brushes in place so focus and existing control templates survive a palette change.
        var defaults = new ResourceDictionary { Source = new Uri("/NightmaresSound;component/Theme.xaml", UriKind.Relative) };
        var theme = Resources.MergedDictionaries[0];
        foreach (object key in defaults.Keys)
            if (defaults[key] is Brush brush) theme[key] = brush;
        if (SystemParameters.HighContrast) ApplyHighContrast(theme);
    }

    public static void ApplyHighContrast(ResourceDictionary theme)
    {
        foreach (string key in new[] { "BackgroundBrush", "PanelBrush", "InputBrush", "TextureBrush" })
            theme[key] = SystemColors.WindowBrush;
        foreach (string key in new[] { "TextBrush", "MutedBrush", "BorderBrush", "ControlBorderBrush", "FocusBrush",
            "PitchBrush", "TimbreBrush", "TimingBrush", "OutputBrush", "ErrorBrush", "WarningBrush" })
            theme[key] = SystemColors.WindowTextBrush;
        // A system background with a visible outline preserves text contrast in every HC scheme.
        theme["HoverBrush"] = SystemColors.WindowBrush;
        theme["SelectedBrush"] = SystemColors.WindowBrush;
        theme["SelectionBrush"] = SystemColors.HighlightBrush;
        theme["SelectionTextBrush"] = SystemColors.HighlightTextBrush;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        base.OnExit(e);
    }
}
