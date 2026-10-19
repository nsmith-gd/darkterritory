using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Player;

/// <summary>What a ping is on (GDD §12 "Ping the bots", note 739), each an order a bot can take.</summary>
public enum PingKind : byte { None, Fire, Cab, Loot, Crane, Town }

/// <summary>
/// A ping as the crew see it (note 739): unique for the night (its record's key), the host tick it was made, whose, on what,
/// which one of those (the car on fire, the loot's body, the crane at the stop, the stop whose village it is; 0 for the cab) and
/// where it was, in the world.
/// </summary>
public readonly record struct PingEvent(int Id, uint Tick, int By, PingKind Kind, int Target, Double3 At);

/// <summary>Field docs live in player.json <c>pings</c>.</summary>
public sealed record PingTuning(double Range = 60, double ConeDegrees = 12, double Seconds = 20, double OrderSeconds = 120, double Cooldown = 0.5, double TownRange = 150,
    double CraneOrderSeconds = 600);

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
        (PingKind, int, Double3)? best = null;
        double bestCos = cone;
        void Consider(PingKind kind, int target, Double3 at, double radius = 0)
        {
            var to = at - eye;
            double d = to.Length;
            if (d > (kind == PingKind.Town ? t.TownRange : t.Range) || d < 1e-6)
                return;
            // Something big is looked at anywhere on it: its cone widened by its size where it is.
            double c = Double3.Dot(to * (1 / d), view) + Math.Min(0.5, radius / d) * (1 - cone);
            if (c > bestCos)
                (bestCos, best) = (c, (kind, target, at));
        }
        // Note 739 part two, the abandoned town: a house of the stop's village, looked at anywhere on it. First, so anything
        // else as near the middle of the view (a find at its door, the crane) is pinged rather than the town round it.
        if (world.Run is { CurrentSite: { } here } run && train.Walls is { } walls && run.StopOf(here.Feature) is ( >= 0 and var stop, var feature))
        {
            foreach (var h in walls.OpenHouses.Where(h => h.Feature == feature))
                Consider(PingKind.Town, stop, h.Middle + Double3.Up * 2, Math.Max(h.Plan.HalfX, h.Plan.HalfY));
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
            Consider(PingKind.Loot, b.Id, Physics.Bodies.WorldCentre(b, train), 0.4);
        if (world.Run?.CurrentSite is { } site)
            for (int i = 0; i < site.Cranes.Count; i++)
                Consider(PingKind.Crane, i, site.Cranes[i].Controls + Double3.Up, 1.5);
        return best;
    }
}
