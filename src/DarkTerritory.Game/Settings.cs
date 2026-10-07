using System.Text.Json;
using Ballast;

namespace DarkTerritory.Game;

/// <summary>
/// A player's settings (roadmap M6 "settings"): sound, voice, the HUD, VR comfort, the mouse, and the display (T83). One small text
/// file in the user's app data, beside the save slots; the front end's settings screen writes it as you change things.
/// </summary>
public sealed record Settings
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DarkTerritory", "settings.json");

    public bool Mute { get; init; }
    /// <summary>Everything you hear, 0..1 (the audio checklist's mix-settings).</summary>
    public double MasterVolume { get; init; } = 1;
    /// <summary>The game's sounds: the train, the world, the tells, your own hands (the mixer's tiers 1 and 3-6).</summary>
    public double EffectsVolume { get; init; } = 1;
    /// <summary>The music: the work's drone and the derailment's opera.</summary>
    public double MusicVolume { get; init; } = 1;
    /// <summary>The crew's voices: near, on the radio, on the dead channel, and the yard's.</summary>
    public double VoiceVolume { get; init; } = 1;
    /// <summary>The microphone you talk into, by its name; empty, the system's default.</summary>
    public string MicDevice { get; init; } = "";
    /// <summary>A gain on the microphone before anything hears it (the voice activity too): 1 as it comes, up to 3.</summary>
    public double MicLevel { get; init; } = 1;
    /// <summary>Hold V to talk, rather than an open mic that opens on your voice.</summary>
    public bool PushToTalk { get; init; }
    public bool Hud { get; init; } = true;
    /// <summary>
    /// The keys in the HUD's corner for what you're holding or driving (note 285, the director: "a setting to hide corner
    /// controls"). Off, the corner keeps only what it's about (the speed, what's in your hands); the prompts at the
    /// crosshair stay.
    /// </summary>
    public bool ControlHints { get; init; } = true;
    public VrTurn VrTurn { get; init; } = VrTurn.Snap;
    public bool VrVignette { get; init; } = true;
    /// <summary>The name the crew and the incident report know you by (GDD v1.4 App. D.12); empty, your online or system name.</summary>
    public string PlayerName { get; init; } = "";
    /// <summary>
    /// Hosting, listed for anyone to find (the LAN beacon, a public Steam lobby) or private (friends, invites and the address
    /// only); the host screen's VISIBILITY, remembered (the user's playtest: "If it's a private lobby its not listed").
    /// </summary>
    public bool PublicLobby { get; init; } = true;
    /// <summary>What the host calls the lobby in the browser; empty, "&lt;name&gt;'S RUN".</summary>
    public string LobbyName { get; init; } = "";
    /// <summary>A multiplier on mouse look.</summary>
    public double MouseSpeed { get; init; } = 1;
    /// <summary>T83: the whole screen (borderless, the desktop's own mode) rather than a window.</summary>
    public bool Fullscreen { get; init; }
    /// <summary>T83: wait for the monitor between frames (no tearing); off, frames go out as soon as they're drawn.</summary>
    public bool VSync { get; init; } = true;
    /// <summary>T83: the resolution the game draws at (and the window's size, windowed), one of <see cref="Resolutions"/>.</summary>
    public string Resolution { get; init; } = "1280x720";
    /// <summary>T83: a fraction of <see cref="Resolution"/> the scene's drawn at and scaled up from (a slower GPU's friend).</summary>
    public double RenderScale { get; init; } = 1;
    /// <summary>The player's own key for a control (T80), by control name; the rest are <see cref="Controls.Defaults"/>.</summary>
    public Dictionary<string, string> Keys { get; init; } = new();

    /// <summary>The resolutions on offer (16:9, the HUD's own shape: ARCHITECTURE §8 note 57's 720p is the least).</summary>
    public static readonly string[] Resolutions = ["1280x720", "1600x900", "1920x1080", "2560x1440"];
    /// <summary>The render scales on offer.</summary>
    public static readonly double[] RenderScales = [0.5, 0.75, 1];

    /// <summary>The window's size for <see cref="Resolution"/> (720p if it isn't one it knows).</summary>
    public (int Width, int Height) WindowSize
    {
        get
        {
            var parts = (Resolutions.Contains(Resolution) ? Resolution : Resolutions[0]).Split('x');
            return (int.Parse(parts[0]), int.Parse(parts[1]));
        }
    }

    /// <summary>The size the scene's drawn at: the resolution at the render scale, kept even (the post passes halve it).</summary>
    public (int Width, int Height) InternalSize
    {
        get
        {
            var (w, h) = WindowSize;
            double k = RenderScales.Contains(RenderScale) ? RenderScale : 1;
            return ((int)Math.Round(w * k / 2) * 2, (int)Math.Round(h * k / 2) * 2);
        }
    }

    /// <summary>The next (or previous, <paramref name="by"/> −1) choice along a list, wrapping round.</summary>
    public static T Cycle<T>(T[] list, T now, int by)
    {
        int i = Array.IndexOf(list, now);
        return list[((i < 0 ? 0 : i) + by + list.Length) % list.Length];
    }

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
        && MasterVolume == other.MasterVolume && EffectsVolume == other.EffectsVolume && MusicVolume == other.MusicVolume
        && VoiceVolume == other.VoiceVolume && MicDevice == other.MicDevice && MicLevel == other.MicLevel
        && VrTurn == other.VrTurn && VrVignette == other.VrVignette && MouseSpeed == other.MouseSpeed
        && Fullscreen == other.Fullscreen && VSync == other.VSync && Resolution == other.Resolution && RenderScale == other.RenderScale
        && PlayerName == other.PlayerName && PublicLobby == other.PublicLobby && LobbyName == other.LobbyName
        && Keys.Count == other.Keys.Count && Keys.All(k => other.Keys.GetValueOrDefault(k.Key) == k.Value);

    public override int GetHashCode() => HashCode.Combine(Mute, PushToTalk, Hud, VrTurn, VrVignette, MouseSpeed, Keys.Count,
        HashCode.Combine(Fullscreen, VSync, Resolution, RenderScale, PublicLobby, LobbyName, HashCode.Combine(MasterVolume, EffectsVolume, MusicVolume, VoiceVolume, MicDevice, MicLevel)));

    /// <summary>The volumes as the mixer takes them.</summary>
    public Ballast.Audio.MixVolumes Volumes => new((float)MasterVolume, (float)EffectsVolume, (float)VoiceVolume, (float)MusicVolume);

    /// <summary>The comfort defaults from content, with the player's choices over them.</summary>
    public VrTuning Apply(VrTuning t) => t with { Turn = VrTurn, Vignette = t.Vignette with { Enabled = VrVignette } };
}
