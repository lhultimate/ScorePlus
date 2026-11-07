using System.Text.Json;
using System.Text.Json.Nodes;

namespace Scoreboard.Core;

public sealed class SportCatalog
{
    readonly string _path;
    readonly List<SportDefinition> _custom = [];
    readonly Dictionary<string, SportDefinition> _overrides = new(StringComparer.OrdinalIgnoreCase);

    public SportCatalog(string? path = null)
    {
        _path = path ?? AppPaths.Combine("sports.json");
        try { Load(); }
        catch { /* A corrupt user catalog must not prevent the game application from starting. */ }
        _custom.RemoveAll(s => !IsValid(s) || SportDefinition.BuiltIns.Any(b => b.Id == s.Id));
        foreach (var key in _overrides.Keys.ToArray())
            if (!IsValid(_overrides[key])) _overrides.Remove(key);
    }

    void Load()
    {
        if (!File.Exists(_path)) return;
        var node = JsonNode.Parse(File.ReadAllText(_path));
        if (node is JsonArray legacy)
        {
            _custom.AddRange(legacy.Deserialize<List<SportDefinition>>() ?? []);
            return;
        }
        if (node is not JsonObject root) return;
        if (root["custom"] is JsonArray customArray)
            _custom.AddRange(customArray.Deserialize<List<SportDefinition>>() ?? []);
        if (root["overrides"] is JsonArray overrideArray)
        {
            foreach (var profile in overrideArray.Deserialize<List<SportDefinition>>() ?? [])
            {
                if (SportDefinition.BuiltIns.Any(b => b.Id == profile.Id)) _overrides[profile.Id] = profile;
            }
        }
    }

    /// <summary>
    /// Built-in presets with any saved overrides applied, then custom sports, all by name — except
    /// the blank <c>Custom</c> preset, which always sits at the bottom where it is easy to find.
    /// </summary>
    public IReadOnlyList<SportDefinition> All =>
        SportDefinition.BuiltIns.Select(ApplyOverride).Concat(_custom)
            .OrderBy(s => string.Equals(s.Id, "generic", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

    public IReadOnlyList<SportDefinition> Custom => _custom.AsReadOnly();
    public SportDefinition? Find(string? id) => All.FirstOrDefault(s => s.Id == id);
    public bool IsOverridden(string? id) => id is not null && _overrides.ContainsKey(id);
    public bool IsBuiltIn(string? id) => SportDefinition.BuiltIns.Any(b => b.Id == id);

    SportDefinition ApplyOverride(SportDefinition builtIn) =>
        _overrides.TryGetValue(builtIn.Id, out var saved) ? saved with { Id = builtIn.Id } : builtIn;

    public SportDefinition AddOrUpdate(SportDefinition profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile = Normalize(profile);
        if (!IsValid(profile)) throw new ArgumentException("Enter a name, period label, 1–99 periods, a clock of 0–24 hours, 0–20 timeouts, and positive scoring values.");
        if (IsBuiltIn(profile.Id)) throw new ArgumentException("Built-in sports are saved as overrides. Use SaveProfile instead.");
        var index = _custom.FindIndex(s => s.Id == profile.Id);
        if (index < 0) _custom.Add(profile); else _custom[index] = profile;
        Persist(); return profile;
    }

    /// <summary>Saves an edited profile. Built-in presets become an override, custom sports are updated in place.</summary>
    public SportDefinition SaveProfile(SportDefinition profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile = Normalize(profile);
        if (!IsValid(profile)) throw new ArgumentException("Enter a name, period label, 1–99 periods, a clock of 0–24 hours, 0–20 timeouts, and positive scoring values.");
        if (IsBuiltIn(profile.Id))
        {
            _overrides[profile.Id] = profile with { Id = profile.Id };
            Persist(); return profile;
        }
        return AddOrUpdate(profile);
    }

    /// <summary>Removes the saved override so the original preset settings come back.</summary>
    public bool ResetOverride(string id)
    {
        var removed = _overrides.Remove(id);
        if (removed) Persist();
        return removed;
    }

    public void Duplicate(string id, string name)
    {
        var source = Find(id) ?? throw new ArgumentException("Choose a sport profile to duplicate.");
        AddOrUpdate(source with { Id = Guid.NewGuid().ToString("N"), Name = name.Trim() });
    }

    public bool Delete(string id)
    {
        var removed = _custom.RemoveAll(s => s.Id == id) > 0;
        if (removed) Persist();
        return removed;
    }

    static SportDefinition Normalize(SportDefinition profile) => profile with
    {
        Id = string.IsNullOrWhiteSpace(profile.Id) ? Guid.NewGuid().ToString("N") : profile.Id.Trim(),
        Name = profile.Name.Trim(),
        Unit = profile.Unit.Trim(),
        // Football's laws allow three timeouts a side, including when a preset is edited.
        Timeouts = profile.Id == "football" ? Math.Clamp(profile.Timeouts, 0, 3) : profile.Timeouts,
        ScoringOptions = profile.ScoringOptions?.Distinct().Order().ToArray() ?? []
    };

    static bool IsValid(SportDefinition s) =>
        !string.IsNullOrWhiteSpace(s.Id) && !string.IsNullOrWhiteSpace(s.Name) && !string.IsNullOrWhiteSpace(s.Unit)
        && s.Periods is >= 1 and <= 99 && s.PeriodSeconds is >= 0 and <= 86400
        && s.Timeouts is >= 0 and <= 20
        && s.ScoringOptions is { Length: > 0 and <= 20 } && s.ScoringOptions.All(x => x is > 0 and <= 100);

    void Persist()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var temp = _path + ".tmp";
        var json = new JsonObject
        {
            ["custom"] = JsonSerializer.SerializeToNode(_custom),
            ["overrides"] = JsonSerializer.SerializeToNode(_overrides.Values.ToArray())
        };
        File.WriteAllText(temp, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, _path, true);
    }
}
