using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Data;

namespace Scoreboard.Desktop;

/// <summary>
/// Shows a motion enum as ordinary words in the editor pickers: WheelUp reads as “Wheel Up”
/// instead of the raw member name. Saved animation choices already carry their own label.
/// </summary>
public sealed class MotionNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Enum) return value?.ToString() ?? "";
        var name = value.ToString() ?? "";
        return Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])", " ");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
