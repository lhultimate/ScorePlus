using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Scoreboard.Core;

/// <summary>
/// What a ScorePlus Game file holds. The game state is the show; the design is every display and
/// layout it was using, so opening a saved game comes back to exactly the screen the operator left.
/// </summary>
public sealed class GameFileDocument
{
    public int Version { get; set; } = 1;
    public GameState? Game { get; set; }
    public ScorebugDocument? Design { get; set; }
}

/// <summary>
/// Reads and writes .spg files. A file written before designs travelled with the game is a bare
/// <see cref="GameState"/> and still opens — it simply brings no design with it, so the current
/// scorebug stays as it is.
/// </summary>
public static class GameFiles
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    /// <summary>The name offered by Save: the game's own name, or Untitled when it has none.</summary>
    public static string SuggestedFileName(string? gameName)
    {
        var name = (gameName ?? "").Trim();
        if (name.Length == 0) return "Untitled.spg";
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(character => invalid.Contains(character) ? '-' : character).ToArray()).Trim();
        // A name made only of dots or spaces is not a usable file name on Windows.
        clean = clean.TrimEnd('.', ' ');
        if (clean.Length == 0) return "Untitled.spg";
        if (clean.Length > 80) clean = clean[..80].TrimEnd();
        return clean + ".spg";
    }

    public static string Serialize(GameState state, ScorebugDocument? design) =>
        design is null
            ? JsonSerializer.Serialize(state, Options)
            : JsonSerializer.Serialize(new GameFileDocument { Game = state, Design = design }, Options);

    public static (GameState Game, ScorebugDocument? Design) Read(string path)
    {
        var text = File.ReadAllText(path);
        var node = JsonNode.Parse(text);
        if (node is JsonObject root && root["Game"] is not null)
        {
            var document = root.Deserialize<GameFileDocument>(Options) ?? new GameFileDocument();
            return (document.Game ?? new GameState(), document.Design);
        }
        return (JsonSerializer.Deserialize<GameState>(text, Options) ?? new GameState(), null);
    }

    /// <summary>True when the file carries a design, so callers can say what it restores.</summary>
    public static bool HasDesign(string path)
    {
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(path));
            return node is JsonObject root && root["Design"] is JsonObject;
        }
        catch { return false; }
    }
}
