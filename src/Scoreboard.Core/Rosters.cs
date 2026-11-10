using System.ComponentModel;
using System.IO;
using System.Text.Json;

namespace Scoreboard.Core;

/// <summary>
/// One player on a roster: jersey number, name, position and whatever stats that team's staff
/// keep. Stats are free-form, exactly like the team stats in the control panel.
/// </summary>
public sealed class RosterPlayer
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int Number { get; set; }
    public string Name { get; set; } = "";
    public string Position { get; set; } = "";

    /// <summary>A portrait uploaded from the roster desk, copied into the app's PlayerPhotos folder.</summary>
    public string PhotoPath { get; set; } = "";

    /// <summary>
    /// Which squad section this player sits in — "Defence", "Bench", "U12" — so a long roster can
    /// be folded and read in pieces. Empty means ungrouped.
    /// </summary>
    public string Group { get; set; } = "";

    public List<GameStat> Stats { get; set; } = [];

    /// <summary>"#12 Smith" for lists and the event log.</summary>
    public string Record => Number > 0 ? $"#{Number} {Name}".Trim() : Name;

    public int Stat(string name) =>
        Stats.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))?.Value ?? 0;

    /// <summary>Sets a stat by name, adding the stat when the player does not have it yet.</summary>
    public void SetStat(string name, int value)
    {
        name = name.Trim();
        if (name.Length == 0) return;
        var index = Stats.FindIndex(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        if (index < 0) Stats.Add(new GameStat(Guid.NewGuid().ToString("N"), name, value));
        else Stats[index] = Stats[index] with { Value = value };
    }

}

/// <summary>A team roster: an ordered list of players that can be shared by every game.</summary>
public sealed class Roster : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    string _name = "New roster";

    /// <summary>
    /// The roster's name. It notifies, because the name is bound wherever the roster is picked or
    /// listed: without this a rename changed the library but every open list kept the old text.
    /// </summary>
    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value ?? "";
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public List<RosterPlayer> Players { get; set; } = [];

    public static Roster Create(string name) => new() { Name = name.Trim() is { Length: > 0 } trimmed ? trimmed : "New roster" };

    public RosterPlayer AddPlayer()
    {
        var number = Players.Count == 0 ? 1 : Players.Max(p => p.Number) + 1;
        var player = new RosterPlayer { Number = number, Name = $"Player {Players.Count + 1}" };
        Players.Add(player);
        return player;
    }

    public bool RemovePlayer(string? id)
    {
        var player = Find(id);
        return player is not null && Players.Remove(player);
    }

    public RosterPlayer? Find(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : Players.FirstOrDefault(p => p.Id == id);

    /// <summary>
    /// Gives every player the stat columns this team tracks, without touching a value already
    /// entered. Adding a player therefore comes with the stat list already filled in.
    /// </summary>
    public bool EnsureColumns(IEnumerable<string> names)
    {
        var columns = names.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var added = false;
        foreach (var player in Players)
            foreach (var name in columns)
                if (!player.Stats.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    player.Stats.Add(new GameStat(Guid.NewGuid().ToString("N"), name.Trim()));
                    added = true;
                }
        return added;
    }

    /// <summary>Every group this roster uses, alphabetical; empty groups are not listed.</summary>
    public IReadOnlyList<string> Groups() => Players
        .Select(player => (player.Group ?? "").Trim())
        .Where(group => group.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(group => group, StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>Every stat name used on this roster, in the order the players carry them.</summary>
    public IReadOnlyList<string> StatNames() => Players
        .SelectMany(p => p.Stats)
        .Select(s => s.Name)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToList();

}

/// <summary>What a player binding can read from a roster entry, by name.</summary>
public static class PlayerFields
{
    public static readonly string[] Fixed = ["Name", "Number", "Position"];

    /// <summary>The value of one field for the graphic: name, number, position, or a stat.</summary>
    public static string Read(RosterPlayer player, string field)
    {
        var key = (field ?? "").Trim();
        if (key.Equals("name", StringComparison.OrdinalIgnoreCase)) return player.Name;
        if (key.Equals("number", StringComparison.OrdinalIgnoreCase)) return player.Number.ToString();
        if (key.Equals("position", StringComparison.OrdinalIgnoreCase)) return player.Position;
        if (key.StartsWith("stat:", StringComparison.OrdinalIgnoreCase)) key = key[5..].Trim();
        var stat = player.Stats.FirstOrDefault(s => string.Equals(s.Name, key, StringComparison.OrdinalIgnoreCase));
        return stat is null ? string.Empty : stat.Display;
    }

    /// <summary>Every field this roster can print, for the studio's player pickers.</summary>
    public static IReadOnlyList<string> All(Roster? roster) =>
        Fixed.Concat(roster?.StatNames() ?? []).ToList();
}

/// <summary>Every roster the operator has built, kept outside the game so a new game keeps them.</summary>
public sealed class RosterBook
{
    public List<Roster> Rosters { get; set; } = [];

    public Roster? Find(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : Rosters.FirstOrDefault(r => r.Id == id);

    public Roster Add(string name)
    {
        var roster = Roster.Create(name);
        Rosters.Add(roster);
        return roster;
    }

    public bool Remove(string? id)
    {
        var roster = Find(id);
        return roster is not null && Rosters.Remove(roster);
    }

    /// <summary>Every stat name used anywhere in the book — what the studio offers for player data.</summary>
    public IReadOnlyList<string> StatNames() => Rosters
        .SelectMany(r => r.StatNames())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToList();
}

/// <summary>
/// Loads and saves the roster library (<c>%LOCALAPPDATA%\ScorePlus\rosters.json</c>). Rosters live
/// outside the game so starting a new game never throws away the teams the operator typed in.
/// </summary>
public static class RosterStore
{
    static RosterBook? _book;
    static string? _overridePath;

    /// <summary>Test hook: aim storage at a throwaway file so a suite never rewrites the real rosters.</summary>
    public static void UsePath(string? path) => _overridePath = path;

    /// <summary>Bumped on every save, so a dock can tell that the library changed under it.</summary>
    public static int Version { get; private set; }

    /// <summary>Raised after the library is saved or reloaded, so graphics reprint player bindings.</summary>
    public static event Action? Changed;

    static string FilePath => _overridePath ?? AppPaths.Combine("rosters.json");

    public static RosterBook Book => _book ??= Load();

    /// <summary>Drops the cached book so the next read comes from disk (used after a test or a restore).</summary>
    public static void Reload() { _book = null; Version++; Changed?.Invoke(); }

    static RosterBook Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new RosterBook();
            var book = JsonSerializer.Deserialize<RosterBook>(File.ReadAllText(FilePath)) ?? new RosterBook();
            book.Rosters ??= [];
            foreach (var roster in book.Rosters)
            {
                roster.Players ??= [];
                foreach (var player in roster.Players)
                {
                    player.Stats ??= [];
                    player.Group ??= "";
                }
            }
            return book;
        }
        catch
        {
            // A damaged library must never stop the show; start empty rather than crash.
            return new RosterBook();
        }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Book, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, FilePath, true);
        }
        catch
        {
            // Storage failures are reported by the status line at worst; the show keeps running.
        }
        Version++;
        Changed?.Invoke();
    }

    /// <summary>Every roster in the library, for the pickers.</summary>
    public static IReadOnlyList<Roster> All() => Book.Rosters;
}

/// <summary>
/// The stat columns a team tracks, by sport. A new player arrives with these already there, so
/// nobody has to name a stat before entering it.
/// </summary>
public static class PlayerStatPresets
{
    // Every sport carries a full column set out of the box — twenty or more — so a roster is
    // useful the moment it is created and nobody has to invent stat names first.
    static readonly string[] Default =
    [
        "Points", "Goals", "Assists", "Rebounds", "Steals", "Blocks", "Turnovers", "Fouls",
        "Shots", "Shots on target", "Passes", "Tackles", "Interceptions", "Saves", "Penalties",
        "Yellow cards", "Red cards", "Minutes", "Plus/minus", "Efficiency", "Wins", "Losses"
    ];

    public static IReadOnlyList<string> For(string? sportId) => sportId switch
    {
        "football" =>
        [
            "Passing yards", "Passing TDs", "Interceptions thrown", "Completions", "Attempts",
            "Rushing yards", "Rushing TDs", "Carries", "Receiving yards", "Receptions",
            "Receiving TDs", "Tackles", "Solo tackles", "Sacks", "Tackles for loss",
            "Interceptions", "Passes defended", "Forced fumbles", "Fumble recoveries",
            "Field goals", "Extra points", "Punts", "Punt return yards", "Kick return yards",
            "Touchdowns", "Penalties", "Minutes"
        ],
        "australian-football" =>
        [
            "Disposals", "Kicks", "Handballs", "Marks", "Contested marks", "Tackles", "Goals",
            "Behinds", "Hitouts", "Clearances", "Inside 50s", "Rebound 50s", "Contested possessions",
            "Uncontested possessions", "Intercepts", "Spoils", "Goal assists", "Score involvements",
            "Metres gained", "Frees for", "Frees against", "Clangers"
        ],
        "baseball" =>
        [
            "At bats", "Hits", "Runs", "RBIs", "Home runs", "Doubles", "Triples", "Walks",
            "Strikeouts", "Stolen bases", "Caught stealing", "On-base %", "Slugging %", "Errors",
            "Assists", "Putouts", "Innings pitched", "Earned runs", "ERA", "WHIP", "Saves",
            "Wins", "Losses"
        ],
        "basketball" =>
        [
            "Points", "Rebounds", "Offensive rebounds", "Defensive rebounds", "Assists", "Steals",
            "Blocks", "Turnovers", "Fouls", "Field goals made", "Field goals attempted", "FG%",
            "3-pointers made", "3-pointers attempted", "Free throws made", "Free throws attempted",
            "Minutes", "Plus/minus", "Double-doubles", "Triple-doubles", "Usage %", "Efficiency"
        ],
        "cricket" =>
        [
            "Runs", "Balls faced", "Fours", "Sixes", "Strike rate", "Wickets", "Maidens",
            "Economy", "Catches", "Stumpings", "Run outs", "Half-centuries", "Centuries",
            "High score", "Overs bowled", "Dot balls", "Extras", "Boundaries conceded",
            "Bowling average", "Batting average", "Ducks", "Not outs"
        ],
        "hockey" =>
        [
            "Goals", "Assists", "Points", "Shots", "Penalty minutes", "Saves", "Save %",
            "Shots against", "Plus/minus", "Power-play goals", "Short-handed goals", "Faceoff wins",
            "Hits", "Blocked shots", "Giveaways", "Takeaways", "Ice time", "Wins",
            "Goals against average", "Losses", "Shutouts", "Shootout goals"
        ],
        "lacrosse" =>
        [
            "Goals", "Assists", "Shots", "Ground balls", "Saves", "Faceoff wins",
            "Caused turnovers", "Penalty minutes", "Man-up goals", "Man-down goals", "Clears",
            "Turnovers", "Points", "Save %", "Shots on goal", "Draw controls", "Draws won",
            "Minutes played", "Fast-break goals", "Unforced errors", "Backup saves",
            "Assists per game"
        ],
        "rugby" =>
        [
            "Tries", "Conversions", "Penalties", "Drop goals", "Tackles", "Metres carried",
            "Carries", "Breaks", "Passes", "Offloads", "Turnovers won", "Lineouts won",
            "Scrums won", "Minutes played", "Points", "Yellow cards", "Red cards",
            "Attempted tackles", "Missed tackles", "Kicks", "Kick metres", "Handling errors"
        ],
        "soccer" =>
        [
            "Goals", "Assists", "Shots", "Shots on target", "Passes", "Pass accuracy",
            "Key passes", "Dribbles", "Tackles", "Interceptions", "Clearances", "Blocks", "Saves",
            "Clean sheets", "Saves %", "Yellow cards", "Red cards", "Fouls", "Offsides",
            "Corners", "Crosses", "Minutes played"
        ],
        "tennis" =>
        [
            "Aces", "Double faults", "First serves in", "First serve %", "Winners",
            "Unforced errors", "Break points", "Break points won", "Points won", "Games won",
            "Sets won", "First serve points won", "Receiving points won", "Net points",
            "Longest streak", "Tiebreaks", "Match time", "Return games", "Aces against",
            "Double faults against"
        ],
        "volleyball" =>
        [
            "Kills", "Assists", "Blocks", "Aces", "Digs", "Attacks", "Attack errors",
            "Reception errors", "Points", "Serve errors", "Block assists", "Sets played",
            "Total attacks", "Hitting %", "Service aces", "Receptions", "Defensive digs",
            "Solo blocks", "Kill %", "Efficiency", "Errors"
        ],
        "wrestling" =>
        [
            "Takedowns", "Escapes", "Reversals", "Near fall", "Penalty points", "Points", "Pins",
            "Tech falls", "Decisions", "Major decisions", "Forfeits", "Ride time", "Top time",
            "Neutral time", "Bottom time", "Warnings", "Stalling calls", "Caution points",
            "Period points", "Match time"
        ],
        _ => Default
    };
}

/// <summary>
/// The player currently on the graphic for one side. A snapshot of the roster entry, copied into
/// the game state so the scorebug can bind to it and a saved game keeps showing the same player.
/// </summary>
public sealed class SelectedPlayer
{
    public string? RosterId { get; set; }
    public string? PlayerId { get; set; }
    public int Number { get; set; }
    public string Name { get; set; } = "";
    public string Position { get; set; } = "";
    public string PhotoPath { get; set; } = "";
    public List<GameStat> Stats { get; set; } = [];

    public static SelectedPlayer From(Roster roster, RosterPlayer player) => new()
    {
        RosterId = roster.Id,
        PlayerId = player.Id,
        Number = player.Number,
        Name = player.Name,
        Position = player.Position,
        PhotoPath = player.PhotoPath,
        Stats = player.Stats.Select(s => s with { Id = Guid.NewGuid().ToString("N") }).ToList()
    };

    /// <summary>The value of one stat on this snapshot.</summary>
    public int Stat(string name) =>
        Stats.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))?.Value ?? 0;

    /// <summary>The value of one field for the graphic: name, number, position, or a stat.</summary>
    public string Field(string field)
    {
        var key = field.Trim();
        if (key.Equals("name", StringComparison.OrdinalIgnoreCase)) return Name;
        if (key.Equals("number", StringComparison.OrdinalIgnoreCase)) return Number.ToString();
        if (key.Equals("position", StringComparison.OrdinalIgnoreCase)) return Position;
        if (key.StartsWith("stat:", StringComparison.OrdinalIgnoreCase)) key = key[5..].Trim();
        var stat = Stats.FirstOrDefault(s => string.Equals(s.Name, key, StringComparison.OrdinalIgnoreCase));
        return stat is null ? string.Empty : stat.Display;
    }

}
