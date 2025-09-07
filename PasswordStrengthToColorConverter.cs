using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MurtiWifiConnecter
{
    public class PasswordStrengthToColorConverter : IValueConverter
    {
        public static readonly PasswordStrengthToColorConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is PasswordStrength strength)
            {
                return strength switch
                {
                    PasswordStrength.VeryWeak => Brushes.Red,
                    PasswordStrength.Weak => Brushes.Orange,
                    PasswordStrength.Fair => Brushes.Yellow,
                    PasswordStrength.Strong => Brushes.LightGreen,
                    PasswordStrength.VeryStrong => Brushes.Green,
                    _ => Brushes.Gray
                };
            }
            return Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}