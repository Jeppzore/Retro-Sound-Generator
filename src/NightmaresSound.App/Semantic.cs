using System.Windows;
using System.Windows.Media;

namespace NightmaresSound.App;

// Presentation metadata only; these values never enter a sound recipe.
public enum SoundConcept { Pitch, Timbre, Timing, Output }

public static class Semantic
{
    public static string Describe(SoundConcept concept) => concept switch
    {
        SoundConcept.Pitch => "Pitch & music",
        SoundConcept.Timbre => "Timbre & effects",
        SoundConcept.Timing => "Timing & envelopes",
        _ => "Levels & output"
    };

    public static readonly DependencyProperty AccentProperty = DependencyProperty.RegisterAttached(
        "Accent", typeof(Brush), typeof(Semantic), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.Inherits));
    public static Brush GetAccent(DependencyObject target) => (Brush)target.GetValue(AccentProperty);
    public static void SetAccent(DependencyObject target, Brush value) => target.SetValue(AccentProperty, value);

    public static readonly DependencyProperty ConceptProperty = DependencyProperty.RegisterAttached(
        "Concept", typeof(SoundConcept), typeof(Semantic), new PropertyMetadata((SoundConcept)(-1), OnConceptChanged));
    public static SoundConcept GetConcept(DependencyObject target) => (SoundConcept)target.GetValue(ConceptProperty);
    public static void SetConcept(DependencyObject target, SoundConcept value) => target.SetValue(ConceptProperty, value);
    private static void OnConceptChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is FrameworkElement element)
            element.SetResourceReference(AccentProperty, args.NewValue + "Brush");
    }
}
