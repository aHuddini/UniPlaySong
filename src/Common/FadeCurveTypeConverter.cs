using System;
using System.Globalization;
using System.Windows.Data;

namespace UniPlaySong
{
    public class FadeCurveTypeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is FadeCurveType curve)
                return (int)curve;
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int index)
                return (FadeCurveType)index;
            return FadeCurveType.Quadratic;
        }
    }
}
