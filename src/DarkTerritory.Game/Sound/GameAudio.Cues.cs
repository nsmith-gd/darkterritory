using Ballast;
using Ballast.Audio;
using DarkTerritory.Sim;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The audio checklist's cues (tools/audio/cues.py): one sound per thing a player or the sim does, each its own sound
/// definition named <c>line.cue</c>, or <c>line.cue.surface</c> where it touches something (written into
/// content/audio/sounds by tools/audio/install.py). A cue whose sound isn't installed plays nothing, so a hook can land
/// before its sound does, and the checklist's Keep/Redo swaps what plays without touching code.
/// The hooks read replicated state only, never host-only events (out/audio/hooks-map.md): an edge in a field this tick
/// against last tick's value is the event.
/// </summary>
public sealed partial class GameAudio
{
    readonly Dictionary<(string, int), bool> _edges = new();
    readonly Dictionary<(string, int), double> _last = new();
    readonly Dictionary<(string, int), SoundInstance> _held = new();
    readonly HashSet<(string, int)> _heldNow = new();

    // One file per area, each filling in its own part; an area not written yet compiles to nothing.
    partial void CrewSounds(World world);
    partial void CreatureSounds(World world);
    partial void TrainSounds(World world);
    partial void OutsideSounds(World world);

    void Cues(World world)
    {
        _heldNow.Clear();
        CrewSounds(world);
        CreatureSounds(world);
        TrainSounds(world);
        OutsideSounds(world);
        // A held loop nobody held this tick has stopped (its owner went away, or its state ended).
        foreach (var key in _held.Keys.Where(k => !_heldNow.Contains(k)).ToList())
        {
            _held[key].Stop();
            _held.Remove(key);
        }
    }

    /// <summary>Whether a cue's sound is installed.</summary>
    public bool HasCue(string name) => Bank.Get(name) is not null;

    /// <summary>Plays a one-shot cue at a point (nothing if it isn't installed).</summary>
    SoundInstance? Cue(string name, Double3 at, float occlusion = 0, float volume = 1) =>
        HasCue(name) ? Mixer.Play(name, at, volume)?.Also(v => v.Occlusion = occlusion) : null;

    /// <summary>Plays a per-surface cue: <c>name.surface</c>, or the nearest surface that has a sound.</summary>
    SoundInstance? Cue(string name, string surface, Double3 at, float occlusion = 0, float volume = 1) =>
        Surfaced(name, surface) is { } n ? Cue(n, at, occlusion, volume) : null;

    // When a surface has no sound of its own yet, the one that sounds most like it (tools/audio/cues.py MATERIALS).
    static readonly Dictionary<string, string[]> Nearest = new()
    {
        ["wood"] = ["ground"],
        ["grate"] = ["plate", "roof", "wood"],
        ["plate"] = ["grate", "roof", "wood"],
        ["roof"] = ["plate", "grate", "wood"],
        ["coal"] = ["ballast", "ground", "dirt"],
        ["ballast"] = ["ground", "dirt", "coal"],
        ["dirt"] = ["ground", "grass", "mud"],
        ["grass"] = ["dirt", "ground"],
        ["mud"] = ["dirt", "ground"],
        ["cobbles"] = ["concrete", "ground"],
        ["concrete"] = ["cobbles", "ground"],
        ["ground"] = ["dirt", "ballast", "grass"],
    };

    string? Surfaced(string name, string surface)
    {
        if (HasCue($"{name}.{surface}"))
            return $"{name}.{surface}";
        foreach (var near in Nearest.GetValueOrDefault(surface, []))
            if (HasCue($"{name}.{near}"))
                return $"{name}.{near}";
        return HasCue(name) ? name : null;
    }

    /// <summary>
    /// Holds a loop on for as long as it's called each tick (keyed by what it belongs to), following <paramref name="at"/>;
    /// it stops the first tick it isn't.
    /// </summary>
    SoundInstance? Hold(string name, int owner, Double3 at, float occlusion = 0)
    {
        var key = (name, owner);
        _heldNow.Add(key);
        if (!_held.TryGetValue(key, out var v) || v.Finished)
        {
            if ((v = HasCue(name) ? Mixer.Play(name, at) : null) is null)
                return null;
            _held[key] = v;
        }
        v.Position = at;
        v.Occlusion = occlusion;
        return v;
    }

    /// <summary>True the tick <paramref name="now"/> turns true, for this key and owner.</summary>
    bool Rose(string key, int owner, bool now)
    {
        bool was = _edges.GetValueOrDefault((key, owner));
        _edges[(key, owner)] = now;
        return now && !was;
    }

    /// <summary>True the tick <paramref name="now"/> turns false.</summary>
    bool Fell(string key, int owner, bool now)
    {
        bool was = _edges.GetValueOrDefault((key, owner));
        _edges[(key, owner)] = now;
        return was && !now;
    }

    /// <summary>How much a value moved since last tick (0 the first time it's seen).</summary>
    double Moved(string key, int owner, double now)
    {
        double d = _last.TryGetValue((key, owner), out var was) ? now - was : 0;
        _last[(key, owner)] = now;
        return d;
    }

    /// <summary>Plays an interface sound (not placed in the world: the sound's definition is flat).</summary>
    public void Ui(string name)
    {
        if (HasCue(name))
            Mixer.Play(name);
    }
}
