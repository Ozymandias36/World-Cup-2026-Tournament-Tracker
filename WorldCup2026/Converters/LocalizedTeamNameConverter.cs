using System.Globalization;
using System.Windows.Data;
using WorldCup2026.Services;

namespace WorldCup2026.Converters;

/// <summary>
/// Combines a team's English name + FIFA code into the currently selected display language.
/// Bind as a MultiBinding: [0] = EnglishName property, [1] = Code property.
/// </summary>
public class LocalizedTeamNameConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var name = values.Length > 0 ? values[0] as string : null;
        var code = values.Length > 1 ? values[1] as string : null;
        return LocalizationService.TeamName(name, code);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
