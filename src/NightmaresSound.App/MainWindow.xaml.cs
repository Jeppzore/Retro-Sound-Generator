using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NightmaresSound.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel = new();
    public MainWindow()
    {
        InitializeComponent();
        Width = Math.Max(MinWidth, Math.Min(Width, SystemParameters.WorkArea.Width - 24));
        Height = Math.Max(MinHeight, Math.Min(Height, SystemParameters.WorkArea.Height - 24));
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync();
        PreviewMouseDown += (_, args) => { if (FindSlider(args.OriginalSource as DependencyObject) is not null) viewModel.BeginGesture(); };
        PreviewMouseUp += (_, _) => viewModel.EndGesture();
        PreviewKeyDown += (_, args) =>
        {
            if (FindSlider(args.OriginalSource as DependencyObject) is not null && IsSliderKey(args.Key)) viewModel.BeginGesture();
            if (args.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { SearchBox.Focus(); args.Handled = true; }
        };
        PreviewKeyUp += (_, args) => { if (IsSliderKey(args.Key)) viewModel.EndGesture(); };
        Deactivated += (_, _) => viewModel.EndGesture();
        Closed += (_, _) => viewModel.Dispose();
    }

    private static bool IsSliderKey(Key key) => key is Key.Left or Key.Right or Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End;
    private static Slider? FindSlider(DependencyObject? node)
    {
        while (node is Visual)
        {
            if (node is Slider slider) return slider;
            node = VisualTreeHelper.GetParent(node);
        }
        return null;
    }
}
