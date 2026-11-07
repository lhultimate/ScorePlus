using System.IO;
using System.Text.Json;

namespace Scoreboard.Core;

/// <summary>
/// Shares one team roster (<c>.spr</c> — ScorePlus Roster) or one player (<c>.spp</c> — ScorePlus
/// Player) between machines and operators. A roster file carries the whole team, a player file a
/// single entry with all of its stats. Reading a file always returns a sanitised copy with brand
/// new ids, so importing the same file twice makes two independent entries and a damaged or
/// foreign file yields null instead of throwing. Writes go through a temporary file, so a failed
/// save never truncates the file that was already there.
/// </summary>
public static class RosterFiles
{
    /// <summary>ScorePlus Roster: one team and its players. Stat values never travel.</summary>
    public const string RosterExtension = ".spr";

    /// <summary>ScorePlus Player: one player, with their stat columns left blank.</summary>
    public const string PlayerExtension = ".spp";

    const string RosterKind = "ScorePlusRoster";
    const string PlayerKind = "ScorePlusPlayer";
    const int FileVersion = 1;

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    sealed class RosterDocument
    {
        public string? Kind { get; set; }
        public int Version { get; set; }
        public Roster? Roster { get; set; }
    }

    sealed class PlayerDocument
    {
        public string? Kind { get; set; }
        public int Version { get; set; }
        /// <summary>The roster the player came from, so an import can show where they belong.</summary>
        public string? Roster { get; set; }
        public RosterPlayer? Player { get; set; }
    }

    /// <summary>Writes a roster file. The roster in the file is a cleaned copy; ids are not reused on import.</summary>
    public static void SaveRoster(string path, Roster roster)
    {
        ArgumentNullException.ThrowIfNull(roster);
        var document = new RosterDocument { Kind = RosterKind, Version = FileVersion, Roster = Clean(roster) };
        Write(path, JsonSerializer.Serialize(document, Json));
    }

    /// <summary>Reads a roster file, or null when it is missing, unreadable or not a roster file.</summary>
    public static Roster? LoadRoster(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var document = JsonSerializer.Deserialize<RosterDocument>(File.ReadAllText(path), Json);
            if (document?.Roster is null || !IsKind(document.Kind, RosterKind)) return null;
            return Clean(document.Roster);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Writes one player file, optionally naming the roster they came from.</summary>
    public static void SavePlayer(string path, RosterPlayer player, string? rosterName = null)
    {
        ArgumentNullException.ThrowIfNull(player);
        var document = new PlayerDocument
        {
            Kind = PlayerKind,
            Version = FileVersion,
            Roster = Trimmed(rosterName),
            Player = Clean(player)
        };
        Write(path, JsonSerializer.Serialize(document, Json));
    }

    /// <summary>Reads a player file, or null when it is missing, unreadable or not a player file.</summary>
    public static RosterPlayer? LoadPlayer(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var document = JsonSerializer.Deserialize<PlayerDocument>(File.ReadAllText(path), Json);
            if (document?.Player is null || !IsKind(document.Kind, PlayerKind)) return null;
            return Clean(document.Player);
        }
        catch
        {
            return null;
        }
    }

    static void Write(string path, string json)
    {
        var full = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(full);
        if (!string.IsNullOrWhiteSpace(folder)) Directory.CreateDirectory(folder);
        var temporary = full + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, full, true);
    }

    static bool IsKind(string? value, string expected) =>
        string.Equals((value ?? "").Trim(), expected, StringComparison.OrdinalIgnoreCase);

    /// <summary>A copy with fresh ids and sane text, ready to join the library.</summary>
    static Roster Clean(Roster roster) => new()
    {
        Id = NewId(),
        Name = Trimmed(roster.Name) ?? "Imported roster",
        Players = (roster.Players ?? []).Select(Clean).ToList()
    };

    /// <summary>
    /// A player as a shareable file carries them: number, name, position and the stat columns the
    /// team tracks, but never a value. A roster file says who is on the team, not what they have
    /// done, so a shared or imported team starts with blank numbers and nobody's totals travel.
    /// </summary>
    static RosterPlayer Clean(RosterPlayer player)
    {
        var stats = new List<GameStat>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var stat in player.Stats ?? [])
        {
            var name = Trimmed(stat.Name);
            if (name is null || !seen.Add(name)) continue;
            stats.Add(new GameStat(NewId(), name));
        }
        return new RosterPlayer
        {
            Id = NewId(),
            Number = Math.Clamp(player.Number, 0, 999),
            Name = Trimmed(player.Name) ?? "Imported player",
            Position = Trimmed(player.Position) ?? "",
            PhotoPath = player.PhotoPath ?? "",
            Stats = stats
        };
    }

    static string NewId() => Guid.NewGuid().ToString("N");

    static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
