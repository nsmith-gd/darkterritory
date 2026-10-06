using Ballast;
using DarkTerritory.Sim.Rail;

namespace DarkTerritory.Sim.Run;

/// <summary>Forts as safe ground (enemies.json <c>forts</c>). Field docs live in that file.</summary>
public sealed record FortTuning(bool On = true, double HalfWidth = 16.5, double TurnBackWithin = 15, double LoseInterestSeconds = 4, bool SilentTerminus = true);

/// <summary>
/// One fortress's walls along the main line (GDD §9 "lights, then walls, then gun towers"): the strip of the line between
/// its gate and the end of the line on its side, <see cref="FortTuning.HalfWidth"/> either side of the track (the walls
/// stand 14.8 m out, WorldArt.Walls).
/// </summary>
/// <param name="Gate">The gate's main-line distance.</param>
/// <param name="Inward">−1: the fort is the line short of its gate (the home fortress); +1: past it (the terminus).</param>
public sealed record Fort(string Name, double Gate, int Inward);

/// <summary>
/// The forts are safe (the director's decision of 2026-10-06, GDD App. F.1 and §9: "we're not letting monsters in the home
/// bases in any of the forts ... it should be a safe space"; ARCHITECTURE §8 note 266): the home fortress the night leaves
/// from and the fort at the terminus. Nothing is sent inside one, nothing follows anyone in, and nobody inside one is seen
/// by anything outside. From the route alone, alike on every machine: the zones are the walls the scene draws
/// (GreyboxScene.Fortress), not anything the buildings' collision has.
/// </summary>
public sealed class Forts
{
    const double SampleM = 10;
    readonly RailLine _line;
    readonly List<(Fort Fort, double From, double To, double[] S, Double3[] P, double MinX, double MaxX, double MinZ, double MaxZ)> _zones = [];

    public FortTuning Tuning { get; }
    public IReadOnlyList<Fort> All => [.. _zones.Select(z => z.Fort)];

    Forts(RailLine line, FortTuning tuning)
    {
        _line = line;
        Tuning = tuning;
    }

    /// <summary>
    /// The route's forts: home, from the back of its yard to <paramref name="yardEnd"/> (the outer gate); the terminus, from
    /// its gate (the line plan's, or <paramref name="terminusZone"/> + 200 m short of the end, as the scene draws it) to the
    /// line's end. A silent settlement's walls count unless <see cref="FortTuning.SilentTerminus"/> is off.
    /// </summary>
    public static Forts Of(Route.Route route, RailLine line, double yardEnd, double terminusZone, FortTuning tuning)
    {
        var forts = new Forts(line, tuning);
        if (!tuning.On)
            return forts;
        double length = Math.Min(route.Length, line.Length);
        forts.Add(new Fort("fortress", yardEnd, -1), 0, Math.Min(yardEnd, length));
        bool silent = route.Plan?.Terminus.Silent == true;
        double gate = route.Plan?.Terminus.GateM ?? length - terminusZone - 200;
        if ((!silent || tuning.SilentTerminus) && gate > yardEnd && gate < length)
            forts.Add(new Fort("terminus", gate, +1), gate, length);
        return forts;
    }

    void Add(Fort fort, double from, double to)
    {
        if (to - from < 1)
            return;
        int n = Math.Max(1, (int)Math.Ceiling((to - from) / SampleM));
        var s = new double[n + 1];
        var p = new Double3[n + 1];
        for (int i = 0; i <= n; i++)
        {
            s[i] = from + (to - from) * i / n;
            p[i] = _line.Sample(s[i]).Position;
        }
        double pad = Tuning.HalfWidth + Tuning.TurnBackWithin + 1;
        _zones.Add((fort, from, to, s, p, p.Min(q => q.X) - pad, p.Max(q => q.X) + pad, p.Min(q => q.Z) - pad, p.Max(q => q.Z) + pad));
    }

    /// <summary>
    /// Where a point is against a fort: its main-line distance and how far off the track (horizontal), for the nearest zone
    /// whose box holds it; null when it's nowhere near one.
    /// </summary>
    (int Zone, double Along, double Off, Double3 Across)? Locate(Double3 at)
    {
        for (int z = 0; z < _zones.Count; z++)
        {
            var zone = _zones[z];
            if (at.X < zone.MinX || at.X > zone.MaxX || at.Z < zone.MinZ || at.Z > zone.MaxZ)
                continue;
            double best = double.MaxValue, along = 0, off = 0;
            var across = Double3.Zero;
            for (int i = 0; i + 1 < zone.P.Length; i++)
            {
                var a = zone.P[i];
                var d = (zone.P[i + 1] - a) with { Y = 0 };
                double len2 = d.X * d.X + d.Z * d.Z;
                double u = len2 > 1e-9 ? ((at.X - a.X) * d.X + (at.Z - a.Z) * d.Z) / len2 : 0;
                // Off the ends of the strip only past the gate counts as off it; the far end's the end of the line.
                double t = Math.Clamp(u, 0, 1);
                var q = a + d * t;
                var w = (at - q) with { Y = 0 };
                double dist = w.Length;
                if (dist < best)
                {
                    best = dist;
                    along = zone.S[i] + (zone.S[i + 1] - zone.S[i]) * u;
                    off = dist;
                    var right = Double3.Cross(d.Normalized, Double3.Up).Normalized;
                    across = Double3.Dot(w, right) >= 0 ? right : right * -1;
                }
            }
            return (z, along, off, across);
        }
        return null;
    }

    /// <summary>Inside a fort's walls (grown by <paramref name="margin"/> m all round, the gate's side too).</summary>
    public bool Inside(Double3 at, double margin = 0) => Within(at, margin) is not null;

    /// <summary>The fort a point is inside (grown by <paramref name="margin"/>), or null.</summary>
    public Fort? Within(Double3 at, double margin = 0)
    {
        if (Locate(at) is not { } l)
            return null;
        var zone = _zones[l.Zone];
        bool past = zone.Fort.Inward < 0 ? l.Along <= zone.Fort.Gate + margin : l.Along >= zone.Fort.Gate - margin;
        return past && l.Off <= Tuning.HalfWidth + margin ? zone.Fort : null;
    }

    /// <summary>
    /// The nearest point outside the fort a point inside it is: out through the gate or over the side, whichever is
    /// nearer, a little past the wall (its height kept). A point outside comes back as it is.
    /// </summary>
    public Double3 Outside(Double3 at)
    {
        if (Locate(at) is not { } l || Within(at) is not { } fort)
            return at;
        const double Clear = 0.5;
        double throughGate = Math.Abs(l.Along - fort.Gate) + Clear;
        double overSide = Tuning.HalfWidth - l.Off + Clear;
        if (overSide < throughGate)
            return at + l.Across * overSide;
        var tangent = _line.Sample(fort.Gate).Tangent with { Y = 0 };
        // Out through the gate: along the line away from the fort's side of it.
        return at + tangent.Normalized * (-fort.Inward * throughGate);
    }
}
