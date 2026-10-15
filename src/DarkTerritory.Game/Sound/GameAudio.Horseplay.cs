using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The crew knocking each other about heard (queue #498, note 768; K1's #451, note 717): read off the host's replicated
/// knocks (<see cref="World.Horseplays"/>, each heard once by its id) and the lumps in flight (<see cref="World.Lumps"/>).
/// A shove's palms and the breath knocked out, a shoulder bumped, a tool's clang and the head's bonk, a lump flung off the
/// shovel, a lump off a friend's coat, and a lump landing on the roof, the boards or the ground where it came down.
/// None of it is a hurt: the crew-hurt hooks hear Health, and horseplay takes none.
/// </summary>
public sealed partial class GameAudio
{
    /// <summary>A lump that went out of the air this close to a creature hit it: the blow's heard as a blow (World.Hits).</summary>
    const double LumpOnCreature = 1.5;

    readonly HashSet<int> _horseplayHeard = new();
    readonly Dictionary<int, LumpInFlight> _lumpsSeen = new();
    World? _horseplayWorld;

    void HorseplaySounds(World world)
    {
        if (!ReferenceEquals(world, _horseplayWorld))
        {
            // A new night: its knocks and lumps are its own (and the first sight of a session's isn't news).
            _horseplayWorld = world;
            _horseplayHeard.Clear();
            _lumpsSeen.Clear();
            foreach (var h in world.Horseplays)
                _horseplayHeard.Add(h.Id);
            foreach (var l in world.Lumps)
                _lumpsSeen[l.Id] = l;
            return;
        }
        var train = world.Train;
        float outside = Occlusion(PlayerMotor.Outside);
        var throwers = new HashSet<int>();
        foreach (var h in world.Horseplays)
        {
            if (!_horseplayHeard.Add(h.Id))
                continue;
            switch (h.Kind)
            {
                case KnockKind.Shove when Victim(train, h.Target, 1.2) is { } at:
                    Cue("crew-horseplay.shove", at, outside);
                    break;
                case KnockKind.Bump when Victim(train, h.Target, 1.3) is { } at:
                    Cue("crew-horseplay.bump", at, outside);
                    break;
                case KnockKind.Bonk when Victim(train, h.Target, 1.65) is { } at:
                    Cue("crew-horseplay.bonk", at, outside);
                    break;
                case KnockKind.Coal when h.Target < 0:
                    // Flung off the shovel: the blade swung (the shovelful never reaches the fire, so nothing's heard there).
                    if (Victim(train, h.By, 1.2) is { } hands)
                        Cue("crew-melee.shovel-swing", hands, outside);
                    break;
                case KnockKind.Coal when Victim(train, h.Target, 1.2) is { } at:
                    Cue("crew-horseplay.coal-hit", at, outside);
                    throwers.Add(h.By);
                    break;
            }
        }
        // A lump gone out of the air without landing on anyone this tick came down on the train or the ground.
        var now = world.Lumps.Select(l => l.Id).ToHashSet();
        foreach (var (id, lump) in _lumpsSeen.Where(kv => !now.Contains(kv.Key)).ToList())
        {
            _lumpsSeen.Remove(id);
            if (throwers.Contains(lump.By)
                || world.ActiveEnemies.Any(e => !e.Gone && (e.WorldPosition(train) - lump.Position).Length <= LumpOnCreature))
                continue;
            Cue("crew-horseplay.coal-land", LumpLandsOn(world, lump.Position), lump.Position, outside);
        }
        foreach (var l in world.Lumps)
            _lumpsSeen[l.Id] = l;
    }

    /// <summary>What a lump came down on: a car's roof tin or its boards (inside it, or on a flat's deck), or the ground there.</summary>
    static string LumpLandsOn(World world, Double3 at)
    {
        var train = world.Train;
        foreach (var frame in train.Frames)
        {
            if ((frame.Origin - at).Length > 30)
                continue;
            var local = frame.ToLocal(at);
            var b = frame.Shape.Bounds;
            if (local.X < b.Min.X - 0.3 || local.X > b.Max.X + 0.3 || local.Z < b.Min.Z - 0.3 || local.Z > b.Max.Z + 0.3 || local.Y < 0.5)
                continue;
            return local.Y >= frame.Shape.RoofHeight - 0.4 ? "roof" : "wood";
        }
        double hint = train.Dynamics.Distance;
        return Footing.Ground(world, at, ref hint);
    }

    /// <summary>
    /// A swing that meets a crewmate before the train (World.Bonk's reach and cone, horseplay on): the bonk, heard off its
    /// knock (<see cref="HorseplaySounds"/>), not the tool ringing on the car behind them.
    /// </summary>
    bool Bonks(World world, Double3 eye, Double3 facing, MeleeTuning melee, Double3 swinger)
    {
        if (!world.HorseplayTuning.Enabled)
            return false;
        double cos = Math.Cos(melee.ConeDegrees * Math.PI / 180);
        foreach (var (_, o) in CrewStates)
        {
            if (!o.Alive || o.Has(PlayerFlags.Held) || At(world.Train, o) is not { } feet || (feet - swinger).Length < 0.05)
                continue;
            var to = feet + Double3.Up * Horseplay.Chest - eye;
            var flat = to with { Y = 0 };
            if (to.Length <= melee.Reach && (flat.Length <= 0.4 || Double3.Dot(flat.Normalized, facing) >= cos))
                return true;
        }
        return false;
    }
}
