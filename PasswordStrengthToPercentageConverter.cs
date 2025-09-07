using System;
using System.Globalization;
using System.Windows.Data;

namespace MurtiWifiConnecter
{
    public class PasswordStrengthToPercentageConverter : IValueConverter
    {
        public static readonly PasswordStrengthToPercentageConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is PasswordStrength strength)
            {
                return (int)strength * 25; // 0, 25, 50, 75, 100
            }
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}