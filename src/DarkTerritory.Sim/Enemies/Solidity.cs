using Ballast;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// The world is solid for what's loose in it (the director, 6 Oct 2026: "creatures, carries and players must respect the
/// terrain and geometry"; ARCHITECTURE §8 note 279). Once the creatures have stepped, none stands in a stop's building or
/// in a tunnel's lining (a fortress drives off what comes into it instead, note 273), and what walks is on the land: a
/// Ribbit's hop no longer keeps the height it
/// was put down at, a Gaunt walking up after its waker or a feral Grumbler goes round a house, not through it. By kind
/// (enemies.json <c>solidity</c>), with each body's widest sphere (<c>bodies</c>). From the creatures' replicated state and
/// the route alone, so every machine settles one the same way. The creatures' own steps are untouched.
/// </summary>
public static class Solidity
{
    public static void Settle(IEnumerable<Enemy> enemies, TrainOnLine train, EnemyTuning t)
    {
        var s = t.Solidity;
        foreach (var e in enemies)
        {
            if (e.Gone || e.Hazard || e.Attached != Enemy.Loose)
                continue;
            var how = s.Of(e.Kind);
            // A Grumbler on its casting rides it (a crane may have it up in the air) until it's turned feral. A Gaunt asleep (or
            // stirring) lies where its roost is, in the building (note 309, B4's): awake, it walks out of it by the walls.
            if (how == Solid.None || e is Grumbler { Feral: false } || e is Gaunt { Phase: SpinePhase.Dormant or SpinePhase.Alert })
                continue;
            double r = Radius(t, e.Kind);
            var p = e.Local;
            // Out of one part of a house can be into the next (an L, a pair): again until nothing holds it, a few times at most.
            if (train.Walls is { } walls)
                for (int pass = 0; pass < 4; pass++)
                {
                    var was = p;
                    foreach (var w in walls.Near(p))
                        if (!w.Fort)
                            p = Out(w, p, r);
                    if (p == was)
                        break;
                }
            if (train.Line.Conditions is { } land)
            {
                p = land.Confine(p, r);
                double ground = land.Ground(p);
                p = how == Solid.Air ? p with { Y = Math.Max(p.Y, ground + s.AirClearM) }
                    // By the train it may be up a car's side, boarding: never under the land, though.
                    : Climbing(train, p, s.ClimbM) ? p with { Y = Math.Max(p.Y, ground) }
                    : p with { Y = ground };
            }
            e.Local = p;
        }
    }

    /// <summary>
    /// Where a creature of <paramref name="kind"/> on the ground at <paramref name="p"/> would be put by <see cref="Settle"/>:
    /// out of the stops' walls and on the land. A place it's sent to (the Pickers' drains, note 592) is one it can reach.
    /// </summary>
    public static Double3 Clear(TrainOnLine train, EnemyTuning t, EnemyKind kind, Double3 p)
    {
        double r = Radius(t, kind);
        if (train.Walls is { } walls)
            for (int pass = 0; pass < 4; pass++)
            {
                var was = p;
                foreach (var w in walls.Near(p))
                    if (!w.Fort)
                        p = Out(w, p, r);
                if (p == was)
                    break;
            }
        if (train.Line.Conditions is { } land)
        {
            p = land.Confine(p, r);
            p = p with { Y = land.Ground(p) };
        }
        return p;
    }

    /// <summary>A creature's widest sphere (enemies.json <c>bodies</c>); a little one for a kind with none.</summary>
    static double Radius(EnemyTuning t, EnemyKind kind)
    {
        double r = 0.3;
        foreach (var (radius, _) in t.Body(kind))
            r = Math.Max(r, radius);
        return r;
    }

    /// <summary>Out of a building's wall by the nearest way, its body clear of it (by a centimetre more, so it's clear past rounding);
    /// over its top or under it, as it was.</summary>
    static Double3 Out(Wall w, Double3 p, double r)
    {
        if (p.Y > w.Top || p.Y < w.Bottom)
            return p;
        var l = w.ToLocal(p);
        double cx = Math.Clamp(l.X, -w.HalfLength, w.HalfLength), cz = Math.Clamp(l.Z, -w.HalfWidth, w.HalfWidth);
        double dx = l.X - cx, dz = l.Z - cz, d2 = dx * dx + dz * dz;
        if (d2 >= r * r)
            return p;
        r += 0.01;
        if (d2 > 1e-12)
        {
            double d = Math.Sqrt(d2);
            return w.ToWorld(l with { X = cx + dx / d * r, Z = cz + dz / d * r });
        }
        // Its middle inside the wall: out by the nearer face.
        double toEnd = w.HalfLength - Math.Abs(l.X), toSide = w.HalfWidth - Math.Abs(l.Z);
        return w.ToWorld(toEnd < toSide
            ? l with { X = (l.X < 0 ? -1 : 1) * (w.HalfLength + r) }
            : l with { Z = (l.Z < 0 ? -1 : 1) * (w.HalfWidth + r) });
    }

    /// <summary>Within <paramref name="reach"/> of a car (its footprint): where a creature may be up its side, boarding.</summary>
    static bool Climbing(TrainOnLine train, Double3 p, double reach)
    {
        foreach (var f in train.Frames)
        {
            if ((f.Origin - p).Length > 40)
                continue;
            var l = f.ToLocal(p);
            if (Math.Abs(l.X) <= f.Shape.HalfWidth + reach && Math.Abs(l.Z) <= f.Shape.HalfLength + reach)
                return true;
        }
        return false;
    }
}
