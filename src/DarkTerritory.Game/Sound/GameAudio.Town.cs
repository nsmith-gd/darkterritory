using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Towns;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The walled town heard (queue #151, ARCHITECTURE §8 note 415; the towns since note 335, B2's notes 353 and 335): the
/// departure town's fires, its lived-in houses and its people, which burnt, ticked and stood about in silence. All of it
/// read off <see cref="World.Town"/>'s plan, alike on every machine, and held where it is while the ear's near: the square's
/// fire barrels and braziers (the nearest few), a house's range, clock or wireless (the nearest few, through the house's
/// walls from outside it), and the townsfolk out of doors talking low through their masks, by how many are near, one of
/// them coughing now and then. A townsperson is where <see cref="Town.Feet"/> has them: at their post, or (B2's #389) where
/// their round has taken them.
/// </summary>
public sealed partial class GameAudio
{
    // HoldLevel owners for the town's things (a fixture's id after it), clear of the vehicles' and the sites'.
    const int TownOwner = 15_000;
    // How far out the town is listened for, how many fires and how many of a house's things are held at once (the nearest),
    // and how often what's near is looked up again (the town doesn't move; its people, on their rounds, slowly).
    const double TownReach = 40, TownFolkReach = 30, TownLook = 0.25;
    const int TownFires = 4, TownRoomThings = 3;
    // Nicki's party carries further than a house's range or clock (music, a crowd), and holds by the house's own id.
    const double TownPartyReach = 70;
    const int TownPartyId = 90_000, TownForgeId = 91_000, TownFlameId = 92_000;
    // A harbour town's Hope Tower (B2's #1091, note 819; queue #554, note 826): its Flame burns in an iron basket on the
    // tower's top, a little over the basket's brim (TownFixtures' "lighthouse" height), and is heard far out over the town.
    const double FlameOverBrim = 2.5, FlameReach = 800;
    // The night market on the square (B2's #1111, note 825; queue #555, note 827): a stall's own trade heard at its counter
    // (the tea boiler, the clocks, the needles), and its keeper calling to the crew now and then, from across the square.
    const double StallReach = 16, StallCounter = 1.0, StallCallReach = 30;
    // About once every twenty seconds one of the stallholders near enough calls (not while they're away on their round).
    const double StallCallOften = 0.05;
    readonly List<Townsperson> _stallholders = [];
    // A house's walls between the ear and a sound on the other side of them (a range heard from the street, the square's
    // fires from a kitchen): the doors and windows are shut, more than a stop's broken-open room (walls.json roomWall).
    const float TownWall = 0.6f;

    readonly List<(int Id, string Cue, Double3 At, int House)> _townNear = [];
    // The nearest forge within reach (note 775), for the smith's work at it.
    Double3? _townForge;
    // The smith at work, now and then: the hammer on the anvil (in a run of blows), the bellows, the quench (per second).
    const double AnvilRuns = 0.12, BellowsOften = 0.06, QuenchOften = 0.03;

    /// <summary>
    /// The blacksmith at the forge (queue #505, note 775): a run of hammer blows on the anvil now and then, the bellows worked,
    /// hot iron quenched, where the hearth is heard.
    /// </summary>
    void Smithy(float outside, double dt)
    {
        if (_townForge is not { } at)
            return;
        if (Sometimes(AnvilRuns, dt))
        {
            int blows = 3 + (int)(OutsideOdds() * 4);
            for (int b = 0; b < blows; b++)
                CueLater(b * (0.42 + 0.1 * OutsideOdds()), "up-forge.anvil", at + Double3.Up * 0.2, outside, b == 0 ? 1 : 0.85f);
        }
        if (Sometimes(BellowsOften, dt))
            Cue("up-forge.bellows", at, outside);
        if (Sometimes(QuenchOften, dt))
            Cue("up-forge.quench", at - Double3.Up * 0.5, outside);
    }
    /// <summary>A market stall's own trade, by what it sells (world/towns.json's market): the stalls that make a sound.</summary>
    string? StallCue(string stall)
    {
        string? cue = stall switch
        {
            "the tea stall" => "place-town.stall-tea",
            "the clock stall" => "place-town.stall-clocks",
            "the knitting stall" => "place-town.stall-knitting",
            _ => null,
        };
        return cue is not null && HasCue(cue) ? cue : null;
    }

    /// <summary>A stallholder calling their wares now and then (note 827), where they stand at their stall: not on their round.</summary>
    void StallCalls(Town town, float walls, double dt)
    {
        if (_stallholders.Count == 0 || !Sometimes(StallCallOften, dt))
            return;
        var p = _stallholders[(int)(OutsideOdds() * _stallholders.Count) % _stallholders.Count];
        var pose = town.Now(p);
        if (!pose.Walking)
            Cue("place-town.stall-call", pose.Feet + Double3.Up * 1.55, walls);
    }

    readonly List<Double3> _townFolk = [];
    double _townLookAt = double.NegativeInfinity;
    int _townEarHouse = -1;

    // On their rounds (B2's #389; queue #182, note 446): the nearest few people, whose feet are heard as they walk (how far
    // each has come since their last step), the one close enough that their breathing gear is heard, and the watch with
    // their lanterns. Who's near is looked up with the rest; where they are, every tick.
    const double TownStepReach = 20, TownGearReach = 4, TownLanternReach = 25, TownStride = 0.7;
    const int TownWalkers = 4, TownLanterns = 2;
    // A townsperson's step to the crew's (crew-footsteps.walk): they go softly, at a stroll.
    const float TownStep = 0.6f;
    readonly List<Townsperson> _townWalkers = [], _townLanterns = [];
    readonly Dictionary<int, (Double3 At, double Since)> _townStrides = [];
    Townsperson? _townGear;
    double _townStepHint = double.NaN;

    void TownSounds(World world, Double3 ear, double dt)
    {
        if (world.Town is not { } town)
            return;
        if (_time - _townLookAt >= TownLook || _time < _townLookAt)
        {
            _townLookAt = _time;
            LookRoundTown(town, ear);
        }
        float outside = Occlusion(PlayerMotor.Outside);
        foreach (var (id, cue, at, house) in _townNear)
        {
            // In a house: clear to an ear in it, through its walls to one outside. In the square: through the walls of the
            // house the ear's in, if it's in one.
            float walls = house >= 0 ? (house == _townEarHouse ? 0 : TownWall) : _townEarHouse >= 0 ? TownWall : 0;
            HoldLevel(cue, TownOwner + id, at, Math.Max(outside, walls), 1);
        }
        Smithy(outside, dt);
        StallCalls(town, Math.Max(outside, _townEarHouse >= 0 ? TownWall : 0), dt);
        FolkOnTheirRounds(world, town, ear, outside);
        if (_townFolk.Count == 0)
            return;
        // The murmur where most of them are, louder the more there are (four or more a full square's worth).
        var middle = Double3.Zero;
        foreach (var p in _townFolk)
            middle += p;
        middle *= 1.0 / _townFolk.Count;
        float folkWalls = Math.Max(outside, _townEarHouse >= 0 ? TownWall : 0);
        HoldLevel("place-town.murmur", TownOwner - 1, middle + Double3.Up * 1.6, folkWalls, Math.Min(1, _townFolk.Count / 4.0));
        // A cough now and then, from one of them: the more there are, the oftener (once a minute or so for one).
        if (Sometimes(0.018 * _townFolk.Count, dt))
            Cue("place-town.cough", _townFolk[(int)(OutsideOdds() * _townFolk.Count) % _townFolk.Count] + Double3.Up * 1.55, folkWalls,
                (float)(0.6 + 0.4 * OutsideOdds()));
    }

    /// <summary>The town's things and people near the ear: the nearest fires, the nearest of the houses' things, the folk out of doors.</summary>
    void LookRoundTown(Town town, Double3 ear)
    {
        _townNear.Clear();
        _townFolk.Clear();
        _townForge = null;
        _townEarHouse = TownHouseAt(town, ear);
        var fires = new List<(double D, int Id, Double3 At)>();
        var rooms = new List<(double D, int Id, string Cue, Double3 At, int House)>();
        foreach (var f in town.Plan.Fixtures)
        {
            // The Flame of Hope, high over everything: heard from anywhere in the town and out along the line, never through
            // a house's things' short reach.
            if (f.Kind == "marketstall")
            {
                var counter = town.World(f.S, f.D, StallCounter);
                if (StallCue(f.Name) is { } trade && (counter - ear).Length <= StallReach)
                    _townNear.Add((f.Id, trade, counter, -1));
                continue;
            }
            if (f.Kind == "lighthouse" && f.House < 0)
            {
                var flame = town.World(f.S, f.D, TownFixtures.Size(f.Kind).Height + FlameOverBrim);
                if (HasCue("place-town.flame") && (flame - ear).Length <= FlameReach)
                    _townNear.Add((TownFlameId + f.Id, "place-town.flame", flame, -1));
                continue;
            }
            string? cue = f.Kind switch
            {
                "barrel" or "brazier" => "place-town.fire",
                "stove" => "place-town.range",
                "clock" => "place-town.clock",
                "radio" => "place-town.radio",
                // The blacksmith's forge (U1's #967, note 697; queue #505, note 775), where it's installed.
                "forge" when HasCue("up-forge.hearth") => "up-forge.hearth",
                _ => null,
            };
            if (cue is null)
                continue;
            // A fire at its mouth, a range at its hob, a clock or a set where it stands.
            var at = town.World(f.S, f.D, cue == "place-town.fire" ? TownFixtures.Size(f.Kind).Height : Math.Max(0.5, f.Height));
            double d = (at - ear).Length;
            if (d > TownReach)
                continue;
            if (cue == "up-forge.hearth")
            {
                if (_townForge is not { } forge || d < (forge - ear).Length)
                    _townForge = at;
                continue;
            }
            if (cue == "place-town.fire")
                fires.Add((d, f.Id, at));
            else
                rooms.Add((d, f.Id, cue, at, f.House));
        }
        foreach (var (_, id, at) in fires.OrderBy(x => x.D).Take(TownFires))
            _townNear.Add((id, "place-town.fire", at, -1));
        foreach (var (_, id, cue, at, house) in rooms.OrderBy(x => x.D).Take(TownRoomThings))
            _townNear.Add((id, cue, at, house));
        // The forge out of doors, the nearest, held by its own id (not one of a house's things).
        if (_townForge is { } hearth)
            _townNear.Add((TownForgeId, "up-forge.hearth", hearth, -1));
        // Nicki's party (note 571; note 773): the one house up late, heard through its walls from further than a range.
        foreach (var h in town.Plan.Houses)
            if (h.Party && town.World(h.S, h.D, 1.5) is var party && (party - ear).Length <= TownPartyReach)
                _townNear.Add((TownPartyId + h.Id, "figures.party", party, h.Id));
        // The folk out of doors for the murmur; and on their rounds (note 446), the nearest within earshot of a step, the
        // nearest out of doors close enough to breathe on you, and the watch with their lanterns. Each one's feet found once.
        var near = new List<(Townsperson P, double D)>();
        _stallholders.Clear();
        foreach (var p in town.Plan.People)
        {
            var feet = town.Feet(p);
            double d = (feet - ear).Length;
            if (p.Role == "stallholder" && d <= StallCallReach)
                _stallholders.Add(p);
            if (p.House < 0 && d <= TownFolkReach)
                _townFolk.Add(feet);
            if (d <= TownLanternReach)
                near.Add((p, d));
        }
        near.Sort((a, b) => a.D.CompareTo(b.D));
        _townWalkers.Clear();
        _townLanterns.Clear();
        _townWalkers.AddRange(near.Where(x => x.D <= TownStepReach).Take(TownWalkers).Select(x => x.P));
        _townLanterns.AddRange(near.Where(x => x.P.Pose == "lantern").Take(TownLanterns).Select(x => x.P));
        _townGear = near.Where(x => x.P.House < 0 && x.D <= TownGearReach).Select(x => x.P).FirstOrDefault();
        foreach (int id in _townStrides.Keys.Where(id => !_townWalkers.Any(w => w.Id == id)).ToList())
            _townStrides.Remove(id);
    }

    /// <summary>
    /// The townsfolk on their rounds (queue #182, note 446): the nearest walkers' feet, a step each <see cref="TownStride"/>
    /// they cover, on what's under them (the street's cobbles or dirt as <see cref="Footing.Ground"/> has the town, a
    /// house's boards, the wall-walk's planks); the breathing gear of whoever's close enough, out of doors; the watch's
    /// lanterns swinging as they walk, hanging still as they stand.
    /// </summary>
    void FolkOnTheirRounds(World world, Town town, Double3 ear, float outside)
    {
        if (double.IsNaN(_townStepHint))
            _townStepHint = world.Train.Dynamics.Distance;
        foreach (var p in _townWalkers)
        {
            var pose = town.Now(p);
            if (!_townStrides.TryGetValue(p.Id, out var was))
            {
                _townStrides[p.Id] = (pose.Feet, 0);
                continue;
            }
            double since = was.Since + (pose.Walking ? (pose.Feet - was.At).Length : 0);
            if (since >= TownStride)
            {
                since -= TownStride * Math.Floor(since / TownStride);
                string mat = p.House >= 0 || p.Up > 0.5 ? "wood" : Footing.Ground(world, pose.Feet, ref _townStepHint);
                Cue($"crew-footsteps.walk.{mat}", pose.Feet + Double3.Up * 0.05, Walls(p.House), TownStep);
            }
            _townStrides[p.Id] = (pose.Feet, since);
        }
        if (_townGear is { } breather)
            HoldLevel($"place-town.gear-{breather.Gear}", TownOwner + breather.Id, town.Feet(breather) + Double3.Up * 1.55, Walls(-1), 1);
        foreach (var p in _townLanterns)
        {
            var pose = town.Now(p);
            HoldLevel("place-town.lantern", TownOwner + p.Id, pose.Feet + Double3.Up * 0.9 + pose.Facing * 0.3, Math.Max(outside, Walls(p.House)),
                pose.Walking ? 1 : 0.35);
        }

        float Walls(int house) => Math.Max(outside, house >= 0 ? (house == _townEarHouse ? 0 : TownWall) : _townEarHouse >= 0 ? TownWall : 0);
    }

    /// <summary>The town house a point stands in (its id), or −1: its main block's footprint, in the house's own frame.</summary>
    public static int TownHouseAt(Town town, Double3 p)
    {
        foreach (var h in town.Plan.Houses)
        {
            if (h.Kind == HouseKind.Burnt)
                continue;
            var middle = town.World(h.S, h.D);
            var local = p - middle;
            if (Math.Abs(local.Y) > 4)
                continue;
            if (Math.Abs(Double3.Dot(local, town.Direction(h.S, 1, 0))) <= h.Width / 2
                && Math.Abs(Double3.Dot(local, town.Direction(h.S, 0, 1))) <= h.Depth / 2)
                return h.Id;
        }
        return -1;
    }
}
