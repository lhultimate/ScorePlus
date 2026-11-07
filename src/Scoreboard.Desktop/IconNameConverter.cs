using System.Globalization;
using System.Windows.Data;

namespace Scoreboard.Desktop;

/// <summary>Shows enum icon names as separate words: PossessionDot -> Possession Dot.</summary>
public sealed class IconNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value?.ToString() ?? "";
        var words = new System.Text.StringBuilder(text.Length + 4);
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (index > 0 && char.IsUpper(character) && char.IsLower(text[index - 1])) words.Append(' ');
            words.Append(character);
        }
        return words.ToString();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
