using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim.Towns;

/// <summary>What in a town a crewmate is looking at, close enough to talk to, read or knock on.</summary>
public enum TownTargetKind : byte { Person, Board, Paper, Fixture, Door }

/// <summary>A thing in the town to use: its kind, and its index in the plan's people, papers, fixtures or buildings.</summary>
public readonly record struct TownTarget(TownTargetKind Kind, int Index);

/// <summary>
/// A fortress town put down on its line (GDD §3.1; ARCHITECTURE §8 note 304): its plan in world space, the walls that
/// make it solid (App. F.1: "the world is solid"), and what a crewmate standing in it is looking at. Talking and reading
/// change nothing in the night, so nothing here is replicated: every machine builds the same town from the route, and
/// a card opened on one shows on that one alone.
/// </summary>
public sealed class Town
{
    readonly RailLine _line;

    public Town(TownPlan plan, TownTuning tuning, RailLine line)
    {
        Plan = plan;
        Tuning = tuning;
        _line = line;
        Walls = [.. BuildWalls()];
    }

    public TownPlan Plan { get; }
    public TownTuning Tuning { get; }

    /// <summary>The square's walls, its buildings, the solid things in it and its people, as boxes nobody walks through.</summary>
    public IReadOnlyList<Wall> Walls { get; }

    /// <summary>A point in the town's rail frame, in the world: along the line, out to the side, up off the ground.</summary>
    public Double3 World(double s, double d, double up = 0)
    {
        var t = _line.Sample(s);
        return t.Position + Right(t) * d + Double3.Up * up;
    }

    /// <summary>A direction in the rail frame (along the line, across it), level, in the world.</summary>
    public Double3 Direction(double s, double alongS, double acrossD)
    {
        var t = _line.Sample(s);
        var along = new Double3(t.Tangent.X, 0, t.Tangent.Z).Normalized;
        return (along * alongS + Right(t) * acrossD).Normalized;
    }

    static Double3 Right(TrackSample t) => Double3.Cross(t.Tangent, Double3.Up).Normalized;

    public Double3 Feet(Townsperson p) => World(p.S, p.D, p.Up);

    /// <summary>The front of a building, at the middle of its face and a door's height.</summary>
    public Double3 Door(TownBuilding b)
    {
        int side = Math.Sign(b.D);
        return World(b.S, b.D - side * (b.Depth / 2 + 0.05), 1.2);
    }

    /// <summary>What a crewmate on the ground is looking at within reach, the least angle off the view's centre.</summary>
    public TownTarget? Target(in PlayerState p, double eyeHeight)
    {
        if (p.Parent != PlayerState.World || !p.Alive)
            return null;
        var eye = p.Position + Double3.Up * eyeHeight;
        double cp = DMath.Cos(p.Pitch);
        var view = new Double3(-DMath.Sin(p.Yaw) * cp, DMath.Sin(p.Pitch), -DMath.Cos(p.Yaw) * cp);
        return Target(eye, view);
    }

    public TownTarget? Target(Double3 eye, Double3 view)
    {
        var r = Tuning.Reach;
        double bestCos = DMath.Cos(r.LookDegrees * Math.PI / 180);
        TownTarget? best = null;
        void Consider(TownTargetKind kind, int index, Double3 at, double reach)
        {
            var to = at - eye;
            double d = to.Length;
            if (d > reach || d < 1e-6)
                return;
            double cos = Double3.Dot(view, to * (1 / d));
            if (cos < bestCos)
                return;
            bestCos = cos;
            best = new TownTarget(kind, index);
        }
        foreach (var p in Plan.People)
            Consider(TownTargetKind.Person, p.Id, Feet(p) + Double3.Up * 1.45, r.Talk);
        foreach (var paper in Plan.Papers)
            if (!paper.OnBoard)
                Consider(TownTargetKind.Paper, paper.Id, World(paper.S, paper.D, paper.Height), r.Read);
        foreach (var f in Plan.Fixtures)
        {
            if (f.Kind == "board" && Plan.Papers.Any(x => x.OnBoard))
                Consider(TownTargetKind.Board, f.Id, World(f.S, f.D, 1.5), r.Read + f.SolidS);
            else if (f.Text.Length > 0)
                Consider(TownTargetKind.Fixture, f.Id, World(f.S, f.D, Math.Max(0.3, f.Height * 0.6)), r.Read + Math.Max(f.SolidS, f.SolidD));
        }
        for (int i = 0; i < Plan.Buildings.Count; i++)
            if (Plan.Buildings[i].Knock.Length > 0)
                Consider(TownTargetKind.Door, i, Door(Plan.Buildings[i]), r.Read + 0.5);
        return best;
    }

    /// <summary>Where a target is (for how far away it's got, and for the person turning to face you).</summary>
    public Double3 Where(TownTarget target) => target.Kind switch
    {
        TownTargetKind.Person => Feet(Plan.People[target.Index]) + Double3.Up * 1.45,
        TownTargetKind.Paper => World(Plan.Papers[target.Index].S, Plan.Papers[target.Index].D, Plan.Papers[target.Index].Height),
        TownTargetKind.Door => Door(Plan.Buildings[target.Index]),
        _ => World(Plan.Fixtures[target.Index].S, Plan.Fixtures[target.Index].D, 1.5),
    };

    /// <summary>The notices on the board, in the order they're read.</summary>
    public IReadOnlyList<TownPaper> Notices => [.. Plan.Papers.Where(p => p.OnBoard)];

    IEnumerable<Wall> BuildWalls()
    {
        var sq = Plan.Square;
        int side = sq.Side;
        const double half = 0.8; // the fortress wall's own half-thickness (StructureKit.Wall: 1.6 m)
        // The square's far wall, and its two ends back to the line of the yard's walls (14.8 m out).
        yield return Box((sq.S0 + sq.S1) / 2, sq.WallD, (sq.S1 - sq.S0) / 2 + half, half, 9);
        double inner = side * 14.8, mid = (inner + sq.WallD) / 2, across = Math.Abs(sq.WallD - inner) / 2 + half;
        yield return Box(sq.S0, mid, half, across, 9);
        yield return Box(sq.S1, mid, half, across, 9);
        foreach (var b in Plan.Buildings)
            yield return Box(b.S, b.D, b.Length / 2, b.Depth / 2, 9);
        foreach (var f in Plan.Fixtures)
            if (f.SolidS > 0 && f.SolidD > 0)
            {
                // A thing turned to face along the line (a stall against the square's end) is turned a quarter.
                bool turned = Math.Abs(f.FaceS) > Math.Abs(f.FaceD);
                yield return Box(f.S, f.D, turned ? f.SolidD : f.SolidS, turned ? f.SolidS : f.SolidD, Math.Max(1, f.Height));
            }
        // The people: a body's width, so a crewmate walks round them, not through.
        foreach (var p in Plan.People)
            yield return Box(p.S, p.D, 0.25, 0.25, 1.8, p.Up);
    }

    /// <summary>A box in the rail frame, square to the line: half its length along it and across it, its height.</summary>
    Wall Box(double s, double d, double halfS, double halfD, double height, double up = 0)
    {
        var t = _line.Sample(s);
        var at = t.Position + Right(t) * d;
        var axis = new Double3(t.Tangent.X, 0, t.Tangent.Z).Normalized;
        return new Wall(at with { Y = 0 }, axis, halfS, halfD, at.Y + up - 0.5, at.Y + up + height);
    }
}
