using System.Globalization;
using System.Windows.Data;
using WorldCup2026.Helpers;

namespace WorldCup2026.Converters;

public class TeamCodeToFlagConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string code && !string.IsNullOrEmpty(code) ? FlagHelper.CreateFlagImage(code, 24, 16) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
