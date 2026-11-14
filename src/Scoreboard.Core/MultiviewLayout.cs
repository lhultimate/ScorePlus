using System.IO;
using System.Text.Json;

namespace Scoreboard.Core;

/// <summary>One tile in a saved multiview wall. Ids and names are both kept so a wall can still be
/// restored after a display or camera was rebuilt with new ids.</summary>
public sealed class MultiviewLayoutTile
{
    public string Source { get; set; } = "blank";
    public string DisplayId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string LayoutId { get; set; } = "";
    public string LayoutName { get; set; } = "";
    public int CameraIndex { get; set; } = -1;
    public string CameraName { get; set; } = "";
}

/// <summary>A portable wall of preview, audience, camera, replay and blank tiles.</summary>
public sealed class MultiviewLayoutDocument
{
    public const string FormatName = "ScorePlus Multiview Layout";
    public string Format { get; set; } = FormatName;
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "";
    public List<MultiviewLayoutTile> Tiles { get; set; } = [];
}

/// <summary>Reads and writes the .spml multiview layout file.</summary>
public static class MultiviewLayoutFiles
{
    public const string Extension = ".spml";
    public const string Filter = "ScorePlus Multiview Layout (*.spml)|*.spml";

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static void Save(string path, MultiviewLayoutDocument document)
    {
        document.Format = MultiviewLayoutDocument.FormatName;
        document.Version = 1;
        File.WriteAllText(path, JsonSerializer.Serialize(document, Options));
    }

    /// <summary>Returns null for a missing, unreadable or foreign file instead of throwing.</summary>
    public static MultiviewLayoutDocument? Load(string path)
    {
        try
        {
            var document = JsonSerializer.Deserialize<MultiviewLayoutDocument>(File.ReadAllText(path), Options);
            if (document is null || !string.Equals(document.Format, MultiviewLayoutDocument.FormatName, StringComparison.OrdinalIgnoreCase))
                return null;
            document.Tiles ??= [];
            return document;
        }
        catch { return null; }
    }
}
