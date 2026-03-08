using System;
using System.Globalization;
using System.Windows.Data;

namespace UniPlaySong
{
    // Converts between an enum value and a boolean for RadioButton binding.
    // Usage: IsChecked="{Binding MyEnumProp, Converter={StaticResource EnumToBooleanConverter}, ConverterParameter=EnumValue}"
    public class EnumToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null) return false;
            return value.ToString() == parameter.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue && boolValue && parameter != null)
            {
                return Enum.Parse(targetType, parameter.ToString());
            }
            return Binding.DoNothing;
        }
    }
}
