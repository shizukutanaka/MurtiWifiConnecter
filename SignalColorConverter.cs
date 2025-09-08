using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MurtiWifiConnecter
{
    public class SignalColorConverter : IValueConverter
    {
        public static readonly SignalColorConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int signalStrength)
            {
                return signalStrength switch
                {
                    >= 80 => Colors.Green,
                    >= 60 => Colors.YellowGreen,
                    >= 40 => Colors.Orange,
                    >= 20 => Colors.Red,
                    _ => Colors.DarkRed
                };
            }
            return Colors.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}