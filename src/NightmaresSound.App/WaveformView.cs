using System.Windows;
using System.Windows.Media;
using NightmaresSound.Core;

namespace NightmaresSound.App;

public sealed class WaveformView : FrameworkElement
{
    public static readonly DependencyProperty SoundProperty = DependencyProperty.Register(nameof(Sound), typeof(RenderedSound),
        typeof(WaveformView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public RenderedSound? Sound { get => (RenderedSound?)GetValue(SoundProperty); set => SetValue(SoundProperty, value); }

    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(nameof(Background), typeof(Brush),
        typeof(WaveformView), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush Background { get => (Brush)GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
    public static readonly DependencyProperty TraceProperty = DependencyProperty.Register(nameof(Trace), typeof(Brush),
        typeof(WaveformView), new FrameworkPropertyMetadata(Brushes.Teal, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush Trace { get => (Brush)GetValue(TraceProperty); set => SetValue(TraceProperty, value); }
    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(nameof(GridBrush), typeof(Brush),
        typeof(WaveformView), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush GridBrush { get => (Brush)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }

    public WaveformView()
    {
        SetResourceReference(BackgroundProperty, "InputBrush");
        SetResourceReference(TraceProperty, "OutputBrush");
        SetResourceReference(GridBrushProperty, "ControlBorderBrush");
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Background, new Pen(GridBrush, 1), new Rect(RenderSize));
        if (Sound is not { } sound || ActualWidth < 2 || ActualHeight < 2) return;
        int columns = Math.Max(1, (int)ActualWidth - 2);
        var line = new Pen(Trace, 1);
        var axis = new Pen(GridBrush, .5);
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
