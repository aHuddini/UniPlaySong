using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace UniPlaySong.DeskMediaControl
{
    // Converts (progressPercent, containerWidth) → fill width for the progress bar
    public class ProgressWidthConverter : IMultiValueConverter
    {
        public static readonly ProgressWidthConverter Instance = new ProgressWidthConverter();

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2) return 0.0;

            // Guard against DependencyProperty.UnsetValue during binding initialization
            if (values[0] == DependencyProperty.UnsetValue || values[1] == DependencyProperty.UnsetValue)
                return 0.0;

            double percent = 0;
            double containerWidth = 0;

            if (values[0] is double p) percent = p;
            else if (values[0] is int pi) percent = pi;

            if (values[1] is double w) containerWidth = w;

            return Math.Max(0, Math.Min(containerWidth, containerWidth * percent / 100.0));
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
