using System.Windows;
using System.Windows.Media;
using NightmaresSound.Core;

namespace NightmaresSound.App;

public sealed class WaveformView : FrameworkElement
{
    public static readonly DependencyProperty SoundProperty = DependencyProperty.Register(nameof(Sound), typeof(RenderedSound),
        typeof(WaveformView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public RenderedSound? Sound { get => (RenderedSound?)GetValue(SoundProperty); set => SetValue(SoundProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(SystemColors.WindowBrush, new Pen(SystemColors.ControlDarkBrush, 1), new Rect(RenderSize));
        if (Sound is not { } sound || ActualWidth < 2 || ActualHeight < 2) return;
        int columns = Math.Max(1, (int)ActualWidth - 2);
        var line = new Pen(SystemParameters.HighContrast ? SystemColors.WindowTextBrush : Brushes.Teal, 1);
        var axis = new Pen(SystemColors.ControlDarkBrush, .5);
        double channelHeight = (ActualHeight - 2) / sound.Channels;
        for (int c = 0; c < sound.Channels; c++)
        {
            double mid = 1 + channelHeight * (c + .5);
            dc.DrawLine(axis, new(1, mid), new(ActualWidth - 1, mid));
            for (int x = 0; x < columns; x++)
            {
                int start = (int)((long)x * sound.FrameCount / columns);
                int end = Math.Max(start + 1, (int)((long)(x + 1) * sound.FrameCount / columns));
                short min = short.MaxValue, max = short.MinValue;
                for (int frame = start; frame < Math.Min(end, sound.FrameCount); frame++)
                {
                    short sample = sound.SampleAt(frame, c);
                    min = Math.Min(min, sample); max = Math.Max(max, sample);
                }
                double scale = channelHeight * .45 / 32768;
                dc.DrawLine(line, new(x + 1, mid - max * scale), new(x + 1, mid - min * scale));
            }
        }
    }
}
