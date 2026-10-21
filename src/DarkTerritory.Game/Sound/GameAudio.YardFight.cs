using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Towns;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// A creature brought in through the arrival town's gate, heard (queue #649, note 958; GDD v1.6 "Not yet", §3.1, §9; the
/// fight itself notes 589 and 608, the guards turning out note 951). It was fought in silence but for the wall guns
/// (<see cref="WallGunSounds"/>). Now, while the yard fights it, it cries out as it's hit, in its own hurt voice, and dies in
/// its own death where it has one; the town's bell is rung from the gatehouse; and the houses it set burning
/// (<see cref="TownGenerator.Harmed"/>, as the scene draws them) are heard burning, from the moment it came in, for the rest
/// of the night. Read off what every machine has (the enemies, the route's terminus, the town's plan); the guards running out
/// to it are heard by their feet with the rest of the town's walkers (<see cref="FolkOnTheirRounds"/>, from
/// <see cref="Town.Now"/>).
/// </summary>
public sealed partial class GameAudio
{
    // HoldLevel owners for a burning house's flames (its id, then which of them), clear of the town's other things.
    const int HouseFireOwner = TownOwner + 80_000;
    // How often a creature being fought cries out (per second, after the first cry as it's met), and how loud (under its own
    // tells, which it may still be giving: a hound's howl kept over the fight, `dt audio render --scenario brought`); how
    // often the bell is rung while the fight's on (its sample is two to four strokes). The bell hangs this high over the gate.
    const double YardCries = 0.5, YardBellEvery = 3.5, YardBellUp = 7;
    const float YardCry = 0.6f;
    // A burning house is held at this many points along its front, this high up, heard from this far, no more than this many
    // houses at once. Note 966: the houses burn beside the standing train now (the nearest the way in it came through), and
    // a whole house going up is the town's great fire (place-town.flame, the Flame of Hope's roar, carrying hundreds of
    // metres), held once a house; the square's brazier (place-town.fire, gone at 30 m) was never heard from the yard.
    const int HouseFirePoints = 3, HouseFiresHeard = 4;
    const double HouseFireUp = 2.5, HouseFireReach = 120;
    const float HouseFireLevel = 1, HouseFlameLevel = 0.5f;

    // The creatures being fought (id: where it was, its kind), the bell's next stroke, and every one fought tonight (the
    // houses burnt are one each), kept for the town it was in.
    readonly Dictionary<int, (Double3 At, EnemyKind Kind)> _yardFought = [];
    readonly HashSet<int> _yardSeen = [];
    double _yardBell = double.NegativeInfinity;
    Town? _yardTown;

    /// <summary>A creature's hurt in its own voice (the cue it gives when the crew strike it), or null where it has none.</summary>
    static string? HurtCue(EnemyKind kind) => kind switch
    {
        EnemyKind.CarHugger => "cs-car-hugger.hit",
        EnemyKind.Whistler => "cs-whistler.hit",
        EnemyKind.Ribbit => "cs-ribbits.hit",
        EnemyKind.Choir => "cs-choir.hit",
        EnemyKind.CinderHound => "cs-hounds.yelp",
        EnemyKind.Climber => "cs-climbers.hit",
        EnemyKind.Stoker => "cs-stoker.shriek",
        EnemyKind.Gaunt => "cs-gaunt.hit",
        EnemyKind.Follower => "cs-followers.hit",
        EnemyKind.SootChildren => "cs-soot-children.hit",
        EnemyKind.Grumbler => "cs-grumbler.hit",
        EnemyKind.Gannet => "cs-gannet-strike.hit",
        EnemyKind.Pickers => "cs-pickers.hit",
        EnemyKind.Moose => "cs-moose-snag.bellow",
        _ => null,
    };

    /// <summary>Its death, where it has one of its own; else its hurt, once more.</summary>
    static string? DeathCue(EnemyKind kind) => kind switch
    {
        EnemyKind.Gaunt => "cs-gaunt.death",
        EnemyKind.Whistler => "cs-whistler.death",
        EnemyKind.SootChildren => "cs-soot-children.death",
        EnemyKind.Gannet => "cs-gannet-strike.death",
        _ => HurtCue(kind),
    };

    void YardFightSounds(World world, Double3 ear, double dt)
    {
        var town = world.Arrival;
        if (!ReferenceEquals(town, _yardTown))
        {
            _yardTown = town;
            _yardFought.Clear();
            _yardSeen.Clear();
            _yardBell = double.NegativeInfinity;
        }
        if (town is null)
            return;
        var (line, frames) = (world.Train.Line, world.Train.Frames);
        var route = world.Route ?? world.Run?.Route;
        float outside = Occlusion(PlayerMotor.Outside);
        var now = new HashSet<int>();
        foreach (var e in world.ActiveEnemies)
        {
            // As the scene's yard fight has it (GreyboxScene.YardFight), and as the wall guns aim (WallGunSounds).
            if (e.Gone || e.Hazard || e is Incident || e.Kind is EnemyKind.Waker or EnemyKind.Passenger)
                continue;
            var at = e.Attached >= 0 && e.Attached < frames.Count ? frames[e.Attached].ToWorld(e.Local) : e.Local;
            if (!GreyboxScene.InTheTown(line, route, at))
                continue;
            now.Add(e.Id);
            _yardSeen.Add(e.Id);
            bool met = !_yardFought.ContainsKey(e.Id);
            _yardFought[e.Id] = (at, e.Kind);
            if (HurtCue(e.Kind) is { } hurt && HasCue(hurt) && (met || Sometimes(YardCries, dt)))
                Cue(hurt, at + Double3.Up * 0.8, outside, YardCry * (float)(1 + 0.25 * OutsideOdds()));
        }
        // Fought down: gone from the yard (the town's driven it off for good), its last cry where it was.
        foreach (var (id, (at, kind)) in _yardFought.Where(x => !now.Contains(x.Key)).ToList())
        {
            if (DeathCue(kind) is { } death && HasCue(death))
                Cue(death, at + Double3.Up * 0.8, outside);
            _yardFought.Remove(id);
        }
        // The alarm: the bell from over the gate, while the fight's on.
        if (now.Count > 0 && _time - _yardBell >= YardBellEvery && route?.Plan?.Terminus is { } t && HasCue("place-town.bell"))
        {
            _yardBell = _time;
            Cue("place-town.bell", line.Sample(t.GateM).Position + Double3.Up * YardBellUp, outside);
        }
        else if (now.Count == 0)
            _yardBell = double.NegativeInfinity;
        HouseFireSounds(world, town, ear, outside);
    }

    /// <summary>The houses a creature brought in has set burning (one each, the lived-in ones nearest the train, note 966), heard burning.</summary>
    void HouseFireSounds(World world, Town town, Double3 ear, float outside)
    {
        int brought = Math.Max(_yardSeen.Count, world.Run?.BroughtIn.Count ?? 0);
        bool flame = HasCue("place-town.flame");
        if (brought == 0 || !flame && !HasCue("place-town.fire"))
            return;
        int burns = brought * (world.Run?.Tuning.BroughtIn.Burns ?? 1);
        var near = TownGenerator.Harmed(town.Plan, burns)
            .Select(h => (House: h, Front: town.World(h.Rail(0, 0).S, h.Rail(0, 0).D)))
            .Where(x => (x.Front - ear).Length <= HouseFireReach)
            .OrderBy(x => (x.Front - ear).Length)
            .Take(HouseFiresHeard);
        foreach (var (h, front) in near)
        {
            if (flame)
            {
                HoldLevel("place-town.flame", HouseFireOwner + h.Id * HouseFirePoints, front + Double3.Up * HouseFireUp, outside, HouseFlameLevel);
                continue;
            }
            var (s, _) = h.Rail(0, 0);
            var along = town.Direction(s, 1, 0);
            for (int k = 0; k < HouseFirePoints; k++)
            {
                double off = (k - (HouseFirePoints - 1) / 2.0) * h.Width / HouseFirePoints;
                HoldLevel("place-town.fire", HouseFireOwner + h.Id * HouseFirePoints + k, front + along * off + Double3.Up * HouseFireUp,
                    outside, HouseFireLevel);
            }
        }
    }
}
