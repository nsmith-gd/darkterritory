using System.Text.Json;
using Ballast;

namespace DarkTerritory.Game;

/// <summary>
/// A player's settings (roadmap M6 "settings"): sound, voice, the HUD, VR comfort and the mouse. One small text
/// file in the user's app data, beside the save slots; the front end's settings screen writes it as you change things.
/// </summary>
public sealed record Settings
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DarkTerritory", "settings.json");

    public bool Mute { get; init; }
    /// <summary>Hold V to talk, rather than an open mic that opens on your voice.</summary>
    public bool PushToTalk { get; init; }
    public bool Hud { get; init; } = true;
    public VrTurn VrTurn { get; init; } = VrTurn.Snap;
    public bool VrVignette { get; init; } = true;
    /// <summary>A multiplier on mouse look.</summary>
    public double MouseSpeed { get; init; } = 1;

    /// <summary>The file's settings, or the defaults if there's no file or it can't be read.</summary>
    public static Settings Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), DataFile.Options) ?? new() : new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, DataFile.Options) + "\n");
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>The comfort defaults from content, with the player's choices over them.</summary>
    public VrTuning Apply(VrTuning t) => t with { Turn = VrTurn, Vignette = t.Vignette with { Enabled = VrVignette } };
}
