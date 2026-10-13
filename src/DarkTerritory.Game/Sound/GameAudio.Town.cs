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
    // A house's walls between the ear and a sound on the other side of them (a range heard from the street, the square's
    // fires from a kitchen): the doors and windows are shut, more than a stop's broken-open room (walls.json roomWall).
    const float TownWall = 0.6f;

    readonly List<(int Id, string Cue, Double3 At, int House)> _townNear = [];
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
        _townEarHouse = TownHouseAt(town, ear);
        var fires = new List<(double D, int Id, Double3 At)>();
        var rooms = new List<(double D, int Id, string Cue, Double3 At, int House)>();
        foreach (var f in town.Plan.Fixtures)
        {
            string? cue = f.Kind switch
            {
                "barrel" or "brazier" => "place-town.fire",
                "stove" => "place-town.range",
                "clock" => "place-town.clock",
                "radio" => "place-town.radio",
                _ => null,
            };
            if (cue is null)
                continue;
            // A fire at its mouth, a range at its hob, a clock or a set where it stands.
            var at = town.World(f.S, f.D, cue == "place-town.fire" ? TownFixtures.Size(f.Kind).Height : Math.Max(0.5, f.Height));
            double d = (at - ear).Length;
            if (d > TownReach)
                continue;
            if (cue == "place-town.fire")
                fires.Add((d, f.Id, at));
            else
                rooms.Add((d, f.Id, cue, at, f.House));
        }
        foreach (var (_, id, at) in fires.OrderBy(x => x.D).Take(TownFires))
            _townNear.Add((id, "place-town.fire", at, -1));
        foreach (var (_, id, cue, at, house) in rooms.OrderBy(x => x.D).Take(TownRoomThings))
            _townNear.Add((id, cue, at, house));
        // The folk out of doors for the murmur; and on their rounds (note 446), the nearest within earshot of a step, the
        // nearest out of doors close enough to breathe on you, and the watch with their lanterns. Each one's feet found once.
        var near = new List<(Townsperson P, double D)>();
        foreach (var p in town.Plan.People)
        {
            var feet = town.Feet(p);
            double d = (feet - ear).Length;
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
