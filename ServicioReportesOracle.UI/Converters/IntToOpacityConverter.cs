using System;
using System.Globalization;
using System.Windows.Data;

namespace ServicioReportesOracle.UI.Converters
{
    public class IntToOpacityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int intValue)
            {
                return intValue == 0 ? 0.35 : 1.0;
            }
            // in case the binding is a string and needs parsing
            if (value is string strValue && int.TryParse(strValue, out int parsedValue))
            {
                return parsedValue == 0 ? 0.35 : 1.0;
            }
            
            return 1.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
