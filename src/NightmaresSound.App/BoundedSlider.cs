using System.Windows;
using System.Windows.Controls;

namespace NightmaresSound.App;

/// <summary>Clamp exact endpoints, including floating-point roundoff tolerated by RangeBase.</summary>
public sealed class BoundedSlider : Slider
{
    static BoundedSlider() => ValueProperty.OverrideMetadata(typeof(BoundedSlider),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            null, (control, value) =>
            {
                var slider = (BoundedSlider)control;
                return Math.Clamp((double)value, slider.Minimum, slider.Maximum);
            }));
}
