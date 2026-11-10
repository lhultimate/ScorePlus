using System.IO;
using System.Text.Json;

namespace Scoreboard.Core;

/// <summary>
/// A portable bundle of node rules. One ScorePlus Rule (<c>.spr</c>) file can carry a whole display's
/// graph, so copying a show to another machine does not mean rebuilding each condition by hand.
/// The file is JSON with a kind marker because rosters already use the .spr extension; reading checks
/// the marker rather than trusting the extension alone.
/// </summary>
public static class RuleFiles
{
    public const string Extension = ".spr";

    const string Kind = "ScorePlusRules";
    const int FileVersion = 1;

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    sealed class RuleDocument
    {
        public string? Kind { get; set; }
        public int Version { get; set; }
        public List<AutomationRule>? Rules { get; set; }
    }

    /// <summary>Writes every supplied rule into one bundle; ids are not trusted on import.</summary>
    public static void Save(string path, IEnumerable<AutomationRule?> rules)
    {
        var document = new RuleDocument
        {
            Kind = Kind,
            Version = FileVersion,
            Rules = AutomationRules.Normalize(rules)
        };
        var full = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(full);
        if (!string.IsNullOrWhiteSpace(folder)) Directory.CreateDirectory(folder);
        var temporary = full + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(document, Json));
        File.Move(temporary, full, true);
    }

    /// <summary>Reads a rule bundle, or null when it is missing, damaged or not a rule file.</summary>
    public static List<AutomationRule>? Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var document = JsonSerializer.Deserialize<RuleDocument>(File.ReadAllText(path), Json);
            if (document?.Rules is null
                || !string.Equals((document.Kind ?? "").Trim(), Kind, StringComparison.OrdinalIgnoreCase))
                return null;
            return AutomationRules.Normalize(document.Rules);
        }
        catch
        {
            return null;
        }
    }
}
