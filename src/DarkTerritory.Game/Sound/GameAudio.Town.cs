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
        foreach (var p in town.Plan.People)
            if (p.House < 0 && town.Feet(p) is var feet && (feet - ear).Length <= TownFolkReach)
                _townFolk.Add(feet);
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
