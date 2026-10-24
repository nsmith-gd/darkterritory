using System.Text.Json;
using Ballast;

namespace DarkTerritory.Game;

/// <summary>The HUD's palette (note 348).</summary>
public enum HudColours { Standard, Colourblind }

/// <summary>
/// DISPLAY (T83; note 672): a window, the whole screen borderless (other windows and the system's notices can come over
/// it), or the whole screen exclusively, at the desktop's own mode (the system holds its notices back).
/// </summary>
public enum DisplayMode { Windowed, Borderless, Fullscreen }

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
    /// <summary>
    /// A private run's password (note 450; the director, 8 Oct 2026: "Private matches should be password gated"), the host
    /// screen's PASSWORD, remembered so a crew's usual one needn't be typed each night.
    /// </summary>
    public string LobbyPassword { get; init; } = "";
    /// <summary>
    /// Who you're playing for (note 450): HERE FOR LAUGHS, FEELING COMPETITIVE or EITHER. One setting on the host and join
    /// screens alike: a host's run is listed with it, and the join list puts runs of it first.
    /// </summary>
    public RunMood Mood { get; init; }
    /// <summary>A multiplier on mouse look.</summary>
    public double MouseSpeed { get; init; } = 1;
    /// <summary>Note 297: the mouse pushed forward looks down, as a flight stick does.</summary>
    public bool InvertMouse { get; init; }
    /// <summary>
    /// Note 297: your eyes' vertical field of view, in degrees, one of <see cref="FieldsOfView"/> (75 as the game was drawn and
    /// its frame cost measured, tuning/perf.json's views). Wider sees more of the dark, and draws more of it.
    /// </summary>
    public double FieldOfView { get; init; } = 75;
    /// <summary>
    /// Note 297: how much of the boiler's shake and a strained car's judder reaches your eyes (notes 263, 277), 0 to 1. The
    /// world says it otherwise too (the gauges, the squeal, the sparks); only the eyes are spared.
    /// </summary>
    public double CameraShake { get; init; } = 1;
    /// <summary>
    /// Note 298 (GDD §9: in the yard the crew "try on outfits"): which of the crew's looks you wear (look.json crewColours,
    /// with the cap or helmet and scarf that go with it), or −1 for your player id's. Sent as you join; tried on in the yard.
    /// </summary>
    public int Outfit { get; init; } = -1;
    /// <summary>The hat from the hat rack (note 714): 0 none, else look.json crewHats' (from 1). Worn only on a train with the rack.</summary>
    public int Hat { get; init; }
    /// <summary>
    /// Note 731 (GDD §29 "Painted masks"): your gas mask's coat (look.json maskPaints, from 1; 0 unpainted), its stencil
    /// (maskStencils, from 1; 0 none) and the stencil's colour (from 1; 0 look.json maskInk's). Sent as you join; tried on in
    /// the yard (PAINT YOUR MASK).
    /// </summary>
    public int MaskColour { get; init; }
    public int MaskStencil { get; init; }
    public int MaskInk { get; init; }
    /// <summary>
    /// Note 347: how big the HUD's and the menus' print is, one of <see cref="TextSizes"/>. The overlay's canvas is drawn smaller
    /// and scaled up to the window (<see cref="Canvas"/>), so everything on it grows together and keeps its layout.
    /// </summary>
    public double TextSize { get; init; } = 1;
    /// <summary>
    /// Note 348: the HUD's colours that mean something, as drawn or told apart without red against green (hud.json's
    /// <see cref="HudTuning.Standard"/> and <see cref="HudTuning.Colourblind"/>).
    /// </summary>
    public HudColours Colours { get; init; } = HudColours.Standard;
    /// <summary>
    /// Note 350: a tip on the loading screen, and for a new player's first nights the core controls on a card in the yard
    /// (content/ui/tips.json). Off, neither.
    /// </summary>
    public bool FirstNights { get; init; } = true;
    /// <summary>
    /// Note 349: the sounds worth hearing named as they're heard, and where ("[TIPTOEING, BEHIND]"; content/ui/captions.json).
    /// Off by default: the game is made to be listened to, and the caption says only what a hearing player hears.
    /// </summary>
    public bool Captions { get; init; }

    /// <summary>
    /// Note 383: HOLD KEYS, HOLD or TOGGLE. With TOGGLE a press of one of <see cref="Toggleable"/> latches it on and another
    /// lets go (<see cref="HoldLatch"/>). Use stays a hold either way: its taps and its holds mean different things.
    /// </summary>
    public bool ToggleHolds { get; init; }

    /// <summary>
    /// Note 404: TEXT BACKING, a dim band behind the HUD's print in play (hud.json <see cref="HudTuning.TextBacking"/>), so a
    /// caption or a prompt reads over a fire's glare, fog or snow. Off by default: note 285 puts no frames in play.
    /// </summary>
    public bool TextBacking { get; init; }

    /// <summary>The holds HOLD KEYS can make toggles: run, the brake, talk, the radio and the crew roster.</summary>
    // Note 664: crouch too, a hold that HOLD KEYS on TOGGLE makes a press down and a press up.
    public static readonly Control[] Toggleable = [Control.Run, Control.Brake, Control.Talk, Control.Radio, Control.Roster, Control.Crouch];

    /// <summary>The outfit as the wire has it (note 298): none for −1 or anything off the end.</summary>
    public byte OutfitByte(int outfits, int hats = 0) =>
        Hat > 0 && Hat <= hats ? Sim.Player.Outfit.Encode(Outfit < outfits ? Outfit : -1, Hat)
            : Outfit >= 0 && Outfit < outfits ? (byte)Outfit : Sim.Net.Messages.NoOutfit;

    /// <summary>The mask's paint as the wire has it (note 731): each part none past what's on offer (<paramref name="paints"/>, <paramref name="stencils"/>).</summary>
    public Sim.Player.MaskPaint MaskPaint(int paints, int stencils) => new(
        MaskColour > 0 && MaskColour <= paints ? (byte)MaskColour : (byte)0,
        MaskStencil > 0 && MaskStencil <= stencils ? (byte)MaskStencil : (byte)0,
        MaskInk > 0 && MaskInk <= paints ? (byte)MaskInk : (byte)0);
    /// <summary>
    /// T83, note 672: a window, or the whole screen, borderless or exclusive. A new install starts on the whole screen,
    /// exclusive (the director, 13 Oct: "I have notifications popping up that are coming up over the bottom right
    /// information"); Alt+Enter goes between it and a window.
    /// </summary>
    public DisplayMode Display { get; init; } = DisplayMode.Fullscreen;

    /// <summary>Alt+Enter (note 672): a window to the whole screen (exclusive), the whole screen, either way, to a window.</summary>
    public Settings AltEnter() => this with { Display = Display == DisplayMode.Windowed ? DisplayMode.Fullscreen : DisplayMode.Windowed };
    /// <summary>T83: wait for the monitor between frames (no tearing); off, frames go out as soon as they're drawn.</summary>
    public bool VSync { get; init; } = true;
    /// <summary>T83: the resolution the game draws at (and the window's size, windowed), one of <see cref="Resolutions"/>.</summary>
    public string Resolution { get; init; } = "1280x720";
    /// <summary>T83: a fraction of <see cref="Resolution"/> the scene's drawn at and scaled up from (a slower GPU's friend).</summary>
    public double RenderScale { get; init; } = 1;
    /// <summary>
    /// The renderer's safe path from the next start (note 1017; <c>--gpu-safe</c> for one launch): no bloom or occlusion, the
    /// material textures at half size, for a driver that draws black squares over the full path (a Linux player's, 16 Oct).
    /// </summary>
    public bool GpuSafe { get; init; }
    /// <summary>The player's own key for a control (T80), by control name; the rest are <see cref="Controls.Defaults"/>.</summary>
    public Dictionary<string, string> Keys { get; init; } = new();

    /// <summary>The resolutions on offer (16:9, the HUD's own shape: ARCHITECTURE §8 note 57's 720p is the least).</summary>
    public static readonly string[] Resolutions = ["1280x720", "1600x900", "1920x1080", "2560x1440"];
    /// <summary>The fields of view on offer (note 297), vertical degrees: 75 is about 107 across a 16:9 screen.</summary>
    public static readonly double[] FieldsOfView = [60, 65, 70, 75, 80, 85, 90];
    /// <summary>The camera shake on offer (note 297): off, a quarter, half, three quarters, all of it.</summary>
    public static readonly double[] CameraShakes = [0, 0.25, 0.5, 0.75, 1];
    /// <summary>The text sizes on offer (note 347): 100%, 125% and 150%, whole steps of the canvas's pixel at 1080p (4, 5, 6).</summary>
    public static readonly double[] TextSizes = [1, 1.25, 1.5];
    /// <summary>The overlay's canvas at 100% (the HUD's and the menus' own size; note 57's 720p shows each of its pixels as 2.66).</summary>
    public const int CanvasWidth = 480, CanvasHeight = 270;

    /// <summary>The overlay's canvas for <see cref="TextSize"/> (note 347): smaller, so the same print is bigger on the screen.</summary>
    public (int Width, int Height) Canvas
    {
        get
        {
            return ((int)Math.Round(CanvasWidth / TextScale), (int)Math.Round(CanvasHeight / TextScale));
        }
    }

    /// <summary>The text size the print is drawn at: the setting if it's one on offer, else 100%.</summary>
    public double TextScale => TextSizes.Contains(TextSize) ? TextSize : 1;

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

    /// <summary>
    /// A saved file's keys brought past a default that moved (note 898): a shove saved on Caps Lock goes to its default, 5,
    /// unless the player put something else on 5; and a player who did, with the shove never changed, keeps the shove on
    /// Caps Lock, where it was, so no key does two things. Every other key the player chose stays.
    /// </summary>
    public Settings Migrated()
    {
        string shove = Control.Shove.ToString(), key = Controls.Defaults[Control.Shove];
        bool taken = Keys.Any(k => k.Key != shove && k.Value == key);
        var keys = new Dictionary<string, string>(Keys);
        if (keys.GetValueOrDefault(shove) == "CapsLock" && !taken)
            keys.Remove(shove);
        else if (!keys.ContainsKey(shove) && taken)
            keys[shove] = "CapsLock";
        else
            return this;
        return this with { Keys = keys };
    }

    /// <summary>The file's settings, or the defaults if there's no file or it can't be read.</summary>
    public static Settings Load(string path)
    {
        try
        {
            return File.Exists(path) ? (JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), DataFile.Options) ?? new()).Migrated() : new();
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
    public bool Equals(Settings? other) => other is not null && Mute == other.Mute && PushToTalk == other.PushToTalk && Hud == other.Hud && ControlHints == other.ControlHints
        && MasterVolume == other.MasterVolume && EffectsVolume == other.EffectsVolume && MusicVolume == other.MusicVolume
        && VoiceVolume == other.VoiceVolume && MicDevice == other.MicDevice && MicLevel == other.MicLevel
        && VrTurn == other.VrTurn && VrVignette == other.VrVignette && MouseSpeed == other.MouseSpeed
        && InvertMouse == other.InvertMouse && FieldOfView == other.FieldOfView && CameraShake == other.CameraShake && Outfit == other.Outfit && Hat == other.Hat
        && MaskColour == other.MaskColour && MaskStencil == other.MaskStencil && MaskInk == other.MaskInk
        && TextSize == other.TextSize && Colours == other.Colours && FirstNights == other.FirstNights && Captions == other.Captions && ToggleHolds == other.ToggleHolds && TextBacking == other.TextBacking
        && Display == other.Display && VSync == other.VSync && Resolution == other.Resolution && RenderScale == other.RenderScale && GpuSafe == other.GpuSafe
        && PlayerName == other.PlayerName && PublicLobby == other.PublicLobby && LobbyName == other.LobbyName
        && LobbyPassword == other.LobbyPassword && Mood == other.Mood
        && Keys.Count == other.Keys.Count && Keys.All(k => other.Keys.GetValueOrDefault(k.Key) == k.Value);

    public override int GetHashCode() => HashCode.Combine(Mute, PushToTalk, Hud, VrTurn, VrVignette, MouseSpeed, Keys.Count,
        HashCode.Combine(Display, VSync, Resolution, HashCode.Combine(RenderScale, GpuSafe), PublicLobby, LobbyName, HashCode.Combine(MasterVolume, EffectsVolume, MusicVolume, VoiceVolume, MicDevice, MicLevel),
            HashCode.Combine(InvertMouse, FieldOfView, CameraShake, Outfit, ControlHints, TextSize, Colours, HashCode.Combine(FirstNights, Captions, ToggleHolds, TextBacking, LobbyPassword, Mood, HashCode.Combine(MaskColour, MaskStencil, MaskInk)))));

    /// <summary>The field of view the eyes are drawn at (note 297): the setting if it's one on offer, else 75.</summary>
    public float EyeFov => (float)(FieldsOfView.Contains(FieldOfView) ? FieldOfView : 75);

    /// <summary>The volumes as the mixer takes them.</summary>
    public Ballast.Audio.MixVolumes Volumes => new((float)MasterVolume, (float)EffectsVolume, (float)VoiceVolume, (float)MusicVolume);

    /// <summary>The comfort defaults from content, with the player's choices over them.</summary>
    public VrTuning Apply(VrTuning t) => t with { Turn = VrTurn, Vignette = t.Vignette with { Enabled = VrVignette } };
}
