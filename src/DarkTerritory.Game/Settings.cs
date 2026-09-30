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
    /// <summary>The player's own key for a control (T80), by control name; the rest are <see cref="Controls.Defaults"/>.</summary>
    public Dictionary<string, string> Keys { get; init; } = new();

    /// <summary>The key a control is on.</summary>
    public string KeyFor(Control c) => Keys.GetValueOrDefault(c.ToString()) ?? Controls.Defaults[c];

    /// <summary>
    /// The control on this key, the one it was on handed the control's old key (a swap, so nothing's ever left unbound
    /// and no key does two things). A menu key isn't taken: the same settings back.
    /// </summary>
    public Settings Bind(Control c, string key)
    {
        if (Controls.Reserved.Contains(key))
            return this;
        var keys = new Dictionary<string, string>(Keys);
        string old = KeyFor(c);
        foreach (var other in Enum.GetValues<Control>())
            if (other != c && KeyFor(other) == key)
                keys[other.ToString()] = old;
        keys[c.ToString()] = key;
        // Only what differs from the defaults is kept, so a new default reaches a player who never changed that one.
        foreach (var d in Controls.Defaults)
            if (keys.GetValueOrDefault(d.Key.ToString()) == d.Value)
                keys.Remove(d.Key.ToString());
        return this with { Keys = keys };
    }

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

    /// <summary>Settings are the same when every choice is, the keys by what's in them (a record compares a dictionary by reference).</summary>
    public bool Equals(Settings? other) => other is not null && Mute == other.Mute && PushToTalk == other.PushToTalk && Hud == other.Hud
        && VrTurn == other.VrTurn && VrVignette == other.VrVignette && MouseSpeed == other.MouseSpeed
        && Keys.Count == other.Keys.Count && Keys.All(k => other.Keys.GetValueOrDefault(k.Key) == k.Value);

    public override int GetHashCode() => HashCode.Combine(Mute, PushToTalk, Hud, VrTurn, VrVignette, MouseSpeed, Keys.Count);

    /// <summary>The comfort defaults from content, with the player's choices over them.</summary>
    public VrTuning Apply(VrTuning t) => t with { Turn = VrTurn, Vignette = t.Vignette with { Enabled = VrVignette } };
}
