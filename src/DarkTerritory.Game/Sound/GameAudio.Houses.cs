using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The house creatures heard (queue #497, note 767; G1's notes 583-586, their design docs' §4): the Lodger, the Householder,
/// the Hollow House and the Hanger, each read off its record (its mode in Height, <c>Extra2</c> its house among the stop
/// walls' open houses). Heard through the house's walls as the house's own (<see cref="OccludedInHouse"/>): clear to an ear
/// inside it, through the walls from the street. A door the Hollow House slams or the Lodger breaks in is heard as theirs, not
/// as a crewmate's hand on it (<see cref="HouseDoorBy"/>).
/// </summary>
public sealed partial class GameAudio
{
    /// <summary>The Lodger's blows on a door it's breaking in, one about every half second of its <c>breakSeconds</c>.</summary>
    const double LodgerBlowEvery = 0.5;
    /// <summary>The Householder's stiff tread: a step each stride of this, and never quicker than <see cref="TreadAtMost"/>.</summary>
    const double TreadStride = 1.3, TreadAtMost = 0.45;
    /// <summary>The Hanger's hum swells as someone comes among its strands: full this close to one.</summary>
    const double StrandNear = 1.0;

    void HouseSounds(World world, Enemy e, Creature was, Double3 at, bool struck)
    {
        switch (e)
        {
            case Lodger l:
                LodgerSounds(world, l, was, at);
                break;
            case Householder h:
                HouseholderSounds(world, h, was, at);
                break;
            case HollowHouse hh:
                HollowSounds(world, hh, was, at);
                break;
            case Hanger hg:
                HangerSounds(world, hg, was, at, struck);
                break;
        }
    }

    /// <summary>
    /// The Lodger (note 583): its slow, wet breathing while it's folded in its corner; the shriek each time it shrieks (its
    /// telegraph, before every lunge); the thump of its feet as it lunges; a shut door in its way splintering under its blows.
    /// </summary>
    void LodgerSounds(World world, Lodger l, Creature was, Double3 at)
    {
        var mode = (LodgerMode)(int)l.Height;
        bool began = mode != (LodgerMode)(int)was.Height;
        float occ = OccludedInHouse(world, at + Double3.Up * 0.5);
        switch (mode)
        {
            case LodgerMode.Hidden:
                Hold("tell-lodger-breathing.breathing", l.Id, at + Double3.Up * 0.6, occ);
                break;
            case LodgerMode.Shriek when began:
                Cue("tell-lodger-shriek.shriek", at + Double3.Up * 1.4, occ);
                break;
            case LodgerMode.Lunge when began:
                Cue("cs-lodger.lunge", at, occ);
                break;
            case LodgerMode.Break:
                if (began || _time >= was.Next)
                {
                    Cue("cs-lodger.break", at + Double3.Up * 1.1, occ);
                    was.Next = _time + LodgerBlowEvery * (0.85 + 0.3 * _creatureRng.Next());
                }
                break;
        }
    }

    /// <summary>
    /// The Householder (note 584): its house's homely sound (the stove, a clock, a kettle) wherever it sits or watches; its
    /// chair and neck creaking now and then as it watches; rising, the chair scraped back; hunting, its stiff even tread and
    /// dry breath; the clutch of its grab; home again, the thing set down and the chair drawn in.
    /// </summary>
    void HouseholderSounds(World world, Householder h, Creature was, Double3 at)
    {
        var mode = (HouseholderMode)(int)h.Height;
        var before = (HouseholderMode)(int)was.Height;
        bool began = mode != before;
        float occ = OccludedInHouse(world, at + Double3.Up * 0.5);
        if (mode is HouseholderMode.Sit or HouseholderMode.Watch)
            Hold("tell-householder.house", h.Id, at + Double3.Up * 0.8, occ);
        switch (mode)
        {
            case HouseholderMode.Sit when before == HouseholderMode.Return:
                Cue("cs-householder.settle", at + Double3.Up * 0.8, occ);
                break;
            case HouseholderMode.Watch:
                if (began)
                    was.Next = _time + 1 + _creatureRng.Next();
                else if (_time >= was.Next)
                {
                    Cue("cs-householder.creak", at + Double3.Up * 1.2, occ);
                    was.Next = _time + 2.5 + 2 * _creatureRng.Next();
                }
                break;
            case HouseholderMode.Rise when began:
                Cue("tell-householder-rise.rise", at + Double3.Up * 0.6, occ);
                break;
            case HouseholderMode.Grab when began:
                Cue("cs-householder.grab", Victim(world.Train, (int)h.Extra, 1.3) ?? at + Double3.Up * 1.3, occ);
                break;
        }
        if (mode is HouseholderMode.Hunt or HouseholderMode.Grab)
            Hold("cs-householder.breath", h.Id, at + Double3.Up * 1.6, occ);
        // Its tread: a step each stride it covers, at a pace that never hurries.
        if (mode is HouseholderMode.Hunt or HouseholderMode.Return)
        {
            was.Stride += ((at - was.At) with { Y = 0 }).Length;
            if (was.Stride >= TreadStride && _time - was.MovedAt >= TreadAtMost)
            {
                Cue("cs-householder.step", at, occ);
                was.Stride = 0;
                was.MovedAt = _time;
            }
        }
        else
            was.Stride = 0;
    }

    /// <summary>
    /// The Hollow House (note 585): the ground rumbling under it while it rumbles (its tell), its timbers screaming as it sinks
    /// (louder the deeper), and the house coming down. Its doors slamming are the doors' own (<see cref="HouseDoorBy"/>).
    /// </summary>
    void HollowSounds(World world, HollowHouse h, Creature was, Double3 at)
    {
        var mode = (HollowMode)(int)h.Height;
        bool began = mode != (HollowMode)(int)was.Height;
        float occ = OccludedInHouse(world, at + Double3.Up * 0.5);
        double depth = (world.Enemies?.HollowHouse ?? new HollowHouseTuning()).SinkDepth;
        switch (mode)
        {
            case HollowMode.Rumble:
                Hold("tell-hollow-house.rumble", h.Id, at, occ);
                break;
            case HollowMode.Sink:
                HoldLevel("cs-hollow-house.sink", h.Id, at, occ, 0.6 + 0.4 * Math.Clamp(h.Extra / Math.Max(0.1, depth), 0, 1));
                break;
            case HollowMode.Collapse when began:
                Cue("cs-hollow-house.collapse", at + Double3.Up * 2, occ);
                break;
        }
    }

    /// <summary>
    /// The Hanger (note 586): to an ear in its house, the rafters creaking and a wet clicking overhead; its strands humming
    /// from the moment anyone's in its house (its telegraph), swelling as someone comes among them; the snap of its grab;
    /// the haul up the strand; a hiss and a scuttle as it lets go and draws back into the roof.
    /// </summary>
    void HangerSounds(World world, Hanger h, Creature was, Double3 at, bool struck)
    {
        var mode = (HangerMode)(int)h.Height;
        bool began = mode != (HangerMode)(int)was.Height;
        int house = (int)h.Extra2;
        float occ = OccludedInHouse(world, at - Double3.Up * 0.5);
        var walls = world.Train.Walls;
        if (mode == HangerMode.Hidden && walls is not null)
        {
            if (_space == PlayerMotor.HouseSpace(house))
                Hold("tell-hanger.rafters", h.Id, at, 0);
            if (StrandsNearest(world, h, house) is { } near)
                HoldLevel("tell-hanger-strands.hum", h.Id, at - Double3.Up * 1.0, occ, 0.4 + 0.6 * Math.Clamp(1 - (near - StrandNear) / 3, 0, 1));
        }
        switch (mode)
        {
            case HangerMode.Grab when began:
                Cue("cs-hanger.grab", Victim(world.Train, (int)h.Extra, 1.3) ?? at, occ);
                break;
            case HangerMode.Haul:
                Hold("cs-hanger.haul", h.Id, at, occ);
                break;
            case HangerMode.Retreat when began:
                Cue("cs-hanger.retreat", at, occ);
                break;
        }
    }

    /// <summary>How near the nearest crewmate in the Hanger's house is to one of its strands (flat, m); null with nobody inside.</summary>
    double? StrandsNearest(World world, Hanger h, int house)
    {
        if (world.Train.Walls is not { } walls || world.Enemies?.Hanger is not { } t)
            return null;
        var home = walls.OpenHouses.ElementAtOrDefault(house);
        if (home.Doors is null)
            return null;
        var strands = Hanger.Strands(home, h.Id, t.Strands).Select(p => home.World(p.X, p.Y)).ToList();
        double? best = null;
        foreach (var (_, s) in CrewStates)
        {
            if (!s.Alive || At(world.Train, s) is not { } feet || !walls.InHouse(house, feet + Double3.Up * 0.1))
                continue;
            double d = strands.Count > 0 ? strands.Min(p => ((p - feet) with { Y = 0 }).Length) : double.MaxValue;
            best = best is { } b ? Math.Min(b, d) : d;
        }
        return best;
    }

    /// <summary>
    /// Who threw a house door (note 767): shut by the Hollow House as it closes on whoever's inside (its slam), or opened by
    /// the Lodger breaking it in (its last blow); null for a crewmate's hand, heard as the door's own.
    /// </summary>
    string? HouseDoorBy(World world, HouseDoor d, bool shut)
    {
        foreach (var e in world.ActiveEnemies)
        {
            if (e.Gone)
                continue;
            if (shut && e is HollowHouse hh && (int)hh.Extra2 == d.House
                && (HollowMode)(int)hh.Height is HollowMode.Shut or HollowMode.Sink or HollowMode.Collapse)
                return "cs-hollow-house.slam";
            if (!shut && e is Lodger l && (LodgerMode)(int)l.Height == LodgerMode.Break
                && ((l.WorldPosition(world.Train) - d.At) with { Y = 0 }).Length <= 2.5)
                return "cs-lodger.break";
        }
        return null;
    }
}
