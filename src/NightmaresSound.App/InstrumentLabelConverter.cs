using System.Globalization;
using System.Windows.Data;
using NightmaresSound.Core;

namespace NightmaresSound.App;

public sealed class InstrumentLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        InstrumentMode.SingleNote => "Single note / hit",
        MusicalScale.NaturalMinor => "Natural minor",
        InstrumentPhrase.Ascending => "Ascending arpeggio",
        InstrumentPhrase.Descending => "Descending arpeggio",
        InstrumentPhrase.Strum => "Strummed chords",
        InstrumentPhrase.Melody => "Modal melody",
        InstrumentPhrase.March => "March rhythm",
        InstrumentPhrase.Dance => "Dance rhythm",
        _ => value.ToString() ?? ""
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
