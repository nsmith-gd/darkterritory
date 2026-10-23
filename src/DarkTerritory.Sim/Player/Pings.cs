using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Player;

/// <summary>
/// What a ping is on (GDD §12 "Ping the bots", note 739), each an order a bot can take. Note 936: a switch's points (its
/// lever, the target the branch), and the stop's cargo (a crate on the ground, the target 0; or an open cargo car to load it
/// into, the target the car).
/// </summary>
public enum PingKind : byte { None, Fire, Cab, Loot, Crane, Town, Points, Cargo }

/// <summary>
/// A ping as the crew see it (note 739): unique for the night (its record's key), the host tick it was made, whose, on what,
/// which one of those (the car on fire, the loot's body, the crane at the stop, the stop whose village it is; 0 for the cab) and
/// where it was, in the world. Note 936: <paramref name="Taker"/>, the crewmate who's answered it (taken it on: "ON THE
/// SPUR"), or −1.
/// </summary>
public readonly record struct PingEvent(int Id, uint Tick, int By, PingKind Kind, int Target, Double3 At, int Taker = -1);

/// <summary>Field docs live in player.json <c>pings</c>.</summary>
public sealed record PingTuning(double Range = 60, double ConeDegrees = 12, double Seconds = 20, double OrderSeconds = 120, double Cooldown = 0.5, double TownRange = 150,
    double CraneOrderSeconds = 600, double TownReach = 300, double DoneSeconds = 1, double TownAheadSeconds = 240);

/// <summary>
/// Note 739 (the director's second playtest, 14 Oct 2026: "I'm going to want to use my middle mouse button to create some
/// sort of context ping for them"): what a ping is on. The host's to work out, from where the pinger is and looks, among
/// what a bot can be sent to: the thing nearest the middle of their view, within the cone and the range. Deterministic
/// (cosines compared, ids broken ties), as everything that sets bots going is.
/// </summary>
public static class Pings
{
    /// <summary>The ping <paramref name="s"/> makes looking where they look, or null: nothing a bot can do is in view.</summary>
    public static (PingKind Kind, int Target, Double3 At)? Resolve(World world, in PlayerState s, PingTuning t)
    {
        var train = world.Train;
        var pick = train.Dynamics.Tuning.Pick;
        double cp = DMath.Cos(s.Pitch);
        var localView = new Double3(-DMath.Sin(s.Yaw) * cp, DMath.Sin(s.Pitch), -DMath.Cos(s.Yaw) * cp);
        var localEye = s.Position + Double3.Up * (s.Has(PlayerFlags.Crouched) ? pick.CrouchedEyeHeight : pick.EyeHeight);
        Double3 eye = localEye, view = localView;
        if (s.Parent != PlayerState.World && s.Parent >= 0 && s.Parent < train.Frames.Count)
        {
            var f = train.Frames[s.Parent];
            eye = f.ToWorld(localEye);
            view = f.Right * localView.X + f.Up * localView.Y + f.Back * localView.Z;
        }
        double cone = DMath.Cos(t.ConeDegrees * Math.PI / 180);
        // Everything in the cone, each with how near the middle of the view it is and whether it has to be seen (note 1010);
        // the nearest the middle that's seen wins, ties to the first considered.
        var candidates = new List<(double Cos, int Order, PingKind Kind, int Target, Double3 At, bool Sight)>();
        void Consider(PingKind kind, int target, Double3 at, double radius = 0, bool sight = false)
        {
            var to = at - eye;
            double d = to.Length;
            if (d > (kind == PingKind.Town ? t.TownRange : t.Range) || d < 1e-6)
                return;
            // Something big is looked at anywhere on it: its cone widened by its size where it is.
            double c = Double3.Dot(to * (1 / d), view) + Math.Min(0.5, radius / d) * (1 - cone);
            if (c > cone)
                candidates.Add((c, candidates.Count, kind, target, at, sight));
        }
        // Note 739 part two, the abandoned town: a house of a stop's village, looked at anywhere on it. First, so anything
        // else as near the middle of the view (a find at its door, the crane) is pinged rather than the town round it. Note
        // 1010 (the director, 23 Oct: "even if I just sort of like point in the direction of a village ... we're gonna go loot
        // this place"): any stop's village in reach, not only the one the train stands at; the bots take it once it does.
        var villages = new Dictionary<int, List<Run.StopWalls.OpenHouse>>();
        if (world.Run is { } run && train.Walls is { } walls)
            foreach (var h in walls.OpenHouses)
                if (run.StopAt(h.Feature) is >= 0 and var stop && (h.Middle - eye).Length <= t.TownRange + 50)
                {
                    Consider(PingKind.Town, stop, h.Middle + Double3.Up * 2, Math.Max(h.Plan.HalfX, h.Plan.HalfY));
                    if (!villages.TryGetValue(stop, out var houses))
                        villages[stop] = houses = [];
                    houses.Add(h);
                }
        foreach (var e in world.ActiveEnemies.OfType<CarFire>().Where(e => !e.Gone && e.Attached > 0 && e.Attached < train.Frames.Count).OrderBy(e => e.Id))
        {
            var car = train.Frames[e.Attached];
            Consider(PingKind.Fire, e.Attached, car.ToWorld(new Double3(0, car.Shape.RoofHeight * 0.5, 0)), car.Shape.HalfLength * 0.6);
        }
        // The controls: the regulator's handle on the backhead (from in the cab, looking at it; from outside, at the cab).
        if (train.Frames.Count > 0 && train.Frames[0] is var engine && (engine.Shape.Levers?.Regulator ?? (engine.Shape.Cab is { } cab ? (cab.Min + cab.Max) * 0.5 : null)) is { } controls)
            Consider(PingKind.Cab, 0, engine.ToWorld(controls), 0.6);
        foreach (var b in world.Bodies.All.Where(b => b.Kind == Physics.BodyKind.Loot && b.Carrier < 0).OrderBy(b => b.Id))
            Consider(PingKind.Loot, b.Id, Physics.Bodies.WorldCentre(b, train), 0.4, sight: true);
        if (world.Run?.CurrentSite is { } site)
        {
            for (int i = 0; i < site.Cranes.Count; i++)
                Consider(PingKind.Crane, i, site.Cranes[i].Controls + Double3.Up, 1.5);
            // Note 936 (the director, 20 Oct: "You want me to load that cargo into the car that's open ... Sure"): at a stop, a
            // crate of its cargo on the ground, and a cargo car with its door open to load it into.
            foreach (var b in world.Bodies.All.Where(b => b.Kind is Physics.BodyKind.Cargo or Physics.BodyKind.Heavy && b.Carrier < 0 && !b.Stowed
                && b.Parent == PlayerState.World).OrderBy(b => b.Id))
                Consider(PingKind.Cargo, 0, Physics.Bodies.WorldCentre(b, train), 0.5, sight: true);
            var layout = train.Dynamics.Tuning.Geometry.Interior;
            foreach (var v in train.Dynamics.Consist.Vehicles.Where(v => v.Kind == VehicleKind.Cargo && v.Id < train.Frames.Count).OrderBy(v => v.Id))
                for (int side = -1; side <= 1 && layout is not null; side += 2)
                    if (Bots.StopHand.SideDoor(train.Frames[v.Id].Shape, side) is { } door && v.DoorOpen(door))
                        Consider(PingKind.Cargo, v.Id, train.Frames[v.Id].ToWorld(new Double3(side * train.Frames[v.Id].Shape.Bounds.Max.X, layout!.FloorHeight + 1, 0)),
                            layout.SideDoorWidth * 0.5);
        }
        // Note 936 (the director, 20 Oct: "oh, the spur? Yeah, of course, you want me to throw the spur"): a switch's lever
        // beside its points, the branch's.
        if (world.Switches is { } stands)
            for (int i = 0; i < train.Line.Branches.Count; i++)
                Consider(PingKind.Points, i, stands.LeverAt(train.Line, i), 0.8);
        foreach (var c in candidates.OrderByDescending(c => c.Cos).ThenBy(c => c.Order))
            if (!c.Sight || Seen(world, eye, c.At))
                return (c.Kind, c.Target, c.At);
        return Village(villages, eye, view, t);
    }

    /// <summary>
    /// Note 1010 (the director, 23 Oct: "even if I just sort of like point in the direction of a village"): with nothing else
    /// pinged, a village looked towards anywhere across it, not only at a house: the one whose spread of houses the view
    /// passes nearest the middle of (within the cone), its middle in <see cref="PingTuning.TownRange"/>. Null with none.
    /// </summary>
    static (PingKind Kind, int Target, Double3 At)? Village(Dictionary<int, List<Run.StopWalls.OpenHouse>> villages, Double3 eye, Double3 view, PingTuning t)
    {
        double coneAngle = t.ConeDegrees * Math.PI / 180;
        (double Off, int Stop, Double3 At)? best = null;
        foreach (var (stop, houses) in villages.OrderBy(v => v.Key))
        {
            var middle = houses.Aggregate(Double3.Zero, (a, h) => a + h.Middle) * (1.0 / houses.Count) + Double3.Up * 2;
            double radius = houses.Max(h => (h.Middle - middle with { Y = h.Middle.Y }).Length + Math.Max(h.Plan.HalfX, h.Plan.HalfY));
            var to = middle - eye;
            double d = to.Length;
            if (d < 1e-6 || d > t.TownRange)
                continue;
            double off = DMath.Acos(Math.Clamp(Double3.Dot(to * (1 / d), view), -1, 1)) - DMath.Atan(radius / d);
            if (off <= coneAngle && (best is null || off < best.Value.Off))
                best = (off, stop, middle);
        }
        return best is { } b ? (PingKind.Town, b.Stop, b.At) : null;
    }

    /// <summary>
    /// Note 1010 (the director, 23 Oct: "I can just, like, throw my ping around to find random loot. It should check a cast to
    /// be like, can you actually see this or not?"): whether the pinger's eye has a clear line to <paramref name="at"/>: no car
    /// of the train, no wall (a house's, a shed's, a fort's), and no ground in the way. The cast starts a hand's breadth out
    /// (an eye against a wall isn't blinded by it) and stops short of the thing (it lies on something, a floor or a shelf).
    /// </summary>
    public static bool Seen(World world, Double3 eye, Double3 at)
    {
        const double Skin = 0.15, Short = 0.4, Step = 1, GroundSlack = 0.3;
        var to = at - eye;
        double d = to.Length;
        if (d <= Skin + Short)
            return true;
        var dir = to * (1 / d);
        var start = eye + dir * Skin;
        double span = d - Skin - Short;
        var train = world.Train;
        if (Combat.Guns.TrainRaycast(train, start, dir, span) < span)
            return false;
        if (train.Walls is { } walls && walls.Raycast(start, start + dir * span) < span)
            return false;
        if (train.Line.Conditions is { } ground)
            for (double s = Step; s < span; s += Step)
            {
                var p = start + dir * s;
                if (p.Y < ground.Ground(p) - GroundSlack)
                    return false;
            }
        return true;
    }
}
