namespace Scoreboard.Core;

public static class PeriodDisplay
{
    /// <summary>The four ways a quarter can be printed, as offered in the studio.</summary>
    public static readonly (string Key, string Label)[] Formats =
    [
        ("Full", "Quarter 1"),
        ("Short", "Q1"),
        ("Number", "1"),
        ("Ordinal", "1st")
    ];

    public static string Format(GameState state) => Format(state, null);

    /// <param name="formatOverride">A studio element's own format ("Full", "Short", "Number", "Ordinal"),
    /// or null to use the game-wide choice in <see cref="GameState.QuarterDisplayFormat"/>.</param>
    public static string Format(GameState state, string? formatOverride = null)
    {
        // Overtime is its own label: the fifth quarter is OT, the sixth 2OT, and so on.
        if (state.TotalPeriods > 0 && state.Period > state.TotalPeriods)
        {
            var overtime = state.Period - state.TotalPeriods;
            return overtime == 1 ? "OT" : $"{overtime}OT";
        }
        var format = formatOverride is "Full" or "Short" or "Number" or "Ordinal" ? formatOverride : state.QuarterDisplayFormat;
        if (!state.PeriodLabel.Equals("Quarter", StringComparison.OrdinalIgnoreCase)) format = "Full";
        var label = format switch
        {
            "Short" => state.PeriodLabel.Equals("Quarter", StringComparison.OrdinalIgnoreCase) ? "Q" : state.PeriodLabel,
            "Number" => "",
            "Ordinal" => $"{state.Period}{Ordinal(state.Period)}",
            _ => state.PeriodLabel
        };
        if (label.Length == 0) return state.Period.ToString();
        // "Q1" and "1st" read as one chip; "Quarter 1" and "Half 1" keep their space.
        return label.Length == 1 ? $"{label}{state.Period}" : format == "Ordinal" ? label : $"{label} {state.Period}";
    }

    static string Ordinal(int value) => value switch
    {
        1 => "st",
        2 => "nd",
        3 => "rd",
        _ => "th"
    };
}
