namespace Scoreboard.Core;

/// <summary>One starting design offered when a new game is created.</summary>
public sealed record ScorebugPreset(string Id, string Name, string Description)
{
    /// <summary>ComboBox templates print the selected item directly, so never show the record type name.</summary>
    public override string ToString() => Name;
}

/// <summary>
/// The starting scorebug and stadium designs. A new game picks one, so an operator can begin with
/// a compact broadcast bug, a full-frame stadium board or a clean lower third instead of editing
/// the factory layout from scratch. Every preset is ordinary elements, so it can be edited freely.
/// </summary>
public static class ScorebugPresets
{
    public const string Classic = "classic-scorebug";
    public const string Stadium = "stadium-display";
    public const string LowerThird = "broadcast-lower-third";

    public static IReadOnlyList<ScorebugPreset> All { get; } =
    [
        new(Classic, "Classic scorebug", "A compact centred clock with the home team on the right and visitors on the left."),
        new(Stadium, "Stadium display", "A full-frame video board with the score strip across the bottom, like the screen at a game."),
        new(LowerThird, "Broadcast lower third", "A slim bottom-screen strip for name-and-score reads over live pictures.")
    ];

    public static string Normalize(string? id) => All.Any(preset =>
        string.Equals(preset.Id, id, StringComparison.OrdinalIgnoreCase))
        ? All.First(preset => string.Equals(preset.Id, id, StringComparison.OrdinalIgnoreCase)).Id
        : Classic;

    public static ScorebugLayout Build(string? id) => Normalize(id) switch
    {
        Stadium => StadiumLayout(),
        LowerThird => LowerThirdLayout(),
        _ => ScorebugLayout.Default()
    };

    static ScorebugLayout StadiumLayout() => new()
    {
        Name = "Stadium display",
        Width = 1920,
        Height = 1080,
        Background = "#00000000",
        Elements =
        [
            // The board is a video first: everything above the score strip is one large picture,
            // so a clean feed or highlight reel fills the screen like the venue's own display.
            new() { Name = "Video backdrop", Kind = ScorebugElementKind.Shape, Shape = ScorebugShape.Rectangle, X = 0, Y = 0, Width = 1920, Height = 820, CornerRadius = 0, Background = "#FF000000" },
            new() { Name = "Stadium video", Kind = ScorebugElementKind.Video, X = 0, Y = 0, Width = 1920, Height = 820, Background = "#FF000000", MediaTag = "GLOBAL", PreserveVideoAspect = false },

            // The score is a single readable strip along the bottom, away on the left and home on
            // the right, with the clock and period in the middle.
            new() { Name = "Score strip", Kind = ScorebugElementKind.Shape, Shape = ScorebugShape.Rectangle, X = 0, Y = 820, Width = 1920, Height = 260, CornerRadius = 0, Background = "#FF0B1118" },
            new() { Name = "Away accent", Kind = ScorebugElementKind.Shape, Shape = ScorebugShape.Rectangle, X = 0, Y = 820, Width = 560, Height = 260, CornerRadius = 0, Background = "#33141D28" },
            new() { Name = "Home accent", Kind = ScorebugElementKind.Shape, Shape = ScorebugShape.Rectangle, X = 1360, Y = 820, Width = 560, Height = 260, CornerRadius = 0, Background = "#33141D28" },
            new() { Name = "Away team", Content = "{{AWAY_TEAM}}", X = 60, Y = 840, Width = 500, Height = 54, FontSize = 36 },
            new() { Name = "Away score", Content = "{{AWAY_SCORE}}", X = 60, Y = 890, Width = 500, Height = 150, FontSize = 120, ForegroundSource = ScorebugColorSource.AwayPrimary },
            new() { Name = "Home team", Content = "{{HOME_TEAM}}", X = 1360, Y = 840, Width = 500, Height = 54, FontSize = 36 },
            new() { Name = "Home score", Content = "{{HOME_SCORE}}", X = 1360, Y = 890, Width = 500, Height = 150, FontSize = 120, ForegroundSource = ScorebugColorSource.HomePrimary },
            new() { Name = "Period", Content = "{{PERIOD}}", X = 760, Y = 840, Width = 400, Height = 48, FontSize = 30 },
            new() { Name = "Game clock", Content = "{{GAME_CLOCK}}", X = 660, Y = 885, Width = 600, Height = 120, FontSize = 96, Transition = ScorebugTransition.Cut },
            new() { Name = "Down and distance", Content = "{{DOWNS}}", X = 700, Y = 1010, Width = 520, Height = 48, FontSize = 28 }
        ]
    };

    static ScorebugLayout LowerThirdLayout() => new()
    {
        Name = "Broadcast lower third",
        Width = 1920,
        Height = 1080,
        Background = "#00000000",
        Elements =
        [
            new() { Name = "Lower third plate", Kind = ScorebugElementKind.Shape, Shape = ScorebugShape.Rectangle, X = 100, Y = 850, Width = 1720, Height = 170, CornerRadius = 12, Background = "#E60B1118", BorderThickness = 2, BorderColor = "#FF38D7A3" },
            new() { Name = "Away accent", Kind = ScorebugElementKind.Shape, Shape = ScorebugShape.Rectangle, X = 100, Y = 850, Width = 510, Height = 170, CornerRadius = 12, Background = "#B3141D28" },
            new() { Name = "Home accent", Kind = ScorebugElementKind.Shape, Shape = ScorebugShape.Rectangle, X = 1310, Y = 850, Width = 510, Height = 170, CornerRadius = 12, Background = "#B3141D28" },
            new() { Name = "Away team", Content = "{{AWAY_TEAM}}", X = 135, Y = 875, Width = 420, Height = 56, FontSize = 38 },
            new() { Name = "Away score", Content = "{{AWAY_SCORE}}", X = 135, Y = 925, Width = 420, Height = 72, FontSize = 62, ForegroundSource = ScorebugColorSource.AwayPrimary },
            new() { Name = "Home team", Content = "{{HOME_TEAM}}", X = 1365, Y = 875, Width = 420, Height = 56, FontSize = 38 },
            new() { Name = "Home score", Content = "{{HOME_SCORE}}", X = 1365, Y = 925, Width = 420, Height = 72, FontSize = 62, ForegroundSource = ScorebugColorSource.HomePrimary },
            new() { Name = "Period", Content = "{{PERIOD}}", X = 780, Y = 875, Width = 360, Height = 48, FontSize = 30 },
            new() { Name = "Game clock", Content = "{{GAME_CLOCK}}", X = 760, Y = 925, Width = 400, Height = 78, FontSize = 64, Transition = ScorebugTransition.Cut },
            new() { Name = "Possession", Content = "{{POSSESSION}}", X = 860, Y = 1000, Width = 200, Height = 20, FontSize = 18 }
        ]
    };
}
