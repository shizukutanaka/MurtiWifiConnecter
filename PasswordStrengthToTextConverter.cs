using System;
using System.Globalization;
using System.Windows.Data;

namespace MurtiWifiConnecter
{
    public class PasswordStrengthToTextConverter : IValueConverter
    {
        public static readonly PasswordStrengthToTextConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is PasswordStrength strength)
            {
                return strength switch
                {
                    PasswordStrength.VeryWeak => "非常に弱い",
                    PasswordStrength.Weak => "弱い",
                    PasswordStrength.Fair => "普通",
                    PasswordStrength.Strong => "強い",
                    PasswordStrength.VeryStrong => "非常に強い",
                    _ => "不明"
                };
            }
            return "不明";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}