using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// パスワード強度を色に変換
    /// </summary>
    public class PasswordStrengthToColorConverter : IValueConverter
    {
        public static readonly PasswordStrengthToColorConverter Instance = new();
        
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is PasswordStrength strength)
            {
                return strength switch
                {
                    PasswordStrength.VeryWeak => new SolidColorBrush(Color.FromRgb(255, 0, 0)),      // 赤
                    PasswordStrength.Weak => new SolidColorBrush(Color.FromRgb(255, 128, 0)),        // オレンジ
                    PasswordStrength.Fair => new SolidColorBrush(Color.FromRgb(255, 255, 0)),        // 黄色
                    PasswordStrength.Strong => new SolidColorBrush(Color.FromRgb(128, 255, 0)),      // 黄緑
                    PasswordStrength.VeryStrong => new SolidColorBrush(Color.FromRgb(0, 255, 0)),    // 緑
                    _ => new SolidColorBrush(Colors.Gray)
                };
            }
            return new SolidColorBrush(Colors.Gray);
        }
        
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
    
    /// <summary>
    /// パスワード強度をパーセンテージに変換
    /// </summary>
    public class PasswordStrengthToPercentageConverter : IValueConverter
    {
        public static readonly PasswordStrengthToPercentageConverter Instance = new();
        
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is PasswordStrength strength)
            {
                return strength switch
                {
                    PasswordStrength.VeryWeak => 20,
                    PasswordStrength.Weak => 40,
                    PasswordStrength.Fair => 60,
                    PasswordStrength.Strong => 80,
                    PasswordStrength.VeryStrong => 100,
                    _ => 0
                };
            }
            return 0;
        }
        
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
    
    /// <summary>
    /// パスワード強度をテキストに変換
    /// </summary>
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
                    _ => ""
                };
            }
            return "";
        }
        
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}