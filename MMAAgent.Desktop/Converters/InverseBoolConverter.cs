using System;
using System.Globalization;
using System.Windows.Data;

namespace MMAAgent.Desktop.Converters
{
    public sealed class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b) return !b;
            return true; // si no es bool, por defecto habilitado
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}