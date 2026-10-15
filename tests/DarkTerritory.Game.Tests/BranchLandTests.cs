using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// An alternate's or a dead line's land isn't seen through (ARCHITECTURE §8 note 498; the director, 8 Oct, GDD App. F.4:
/// "see through or missing"): out on the main line's own land, the branch has its bed beside its ballast and the sky
/// over it; further out, land all round it and past its buffer stop, and no hills over it. `dt holes --edges branches`
/// found every alternate and dead line running under a roof of the main line's land, the ground beside its ballast open.
/// </summary>
public class BranchLandTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Theory]
    [InlineData("frontier:5")]
    [InlineData("deadLines:8")]
    public void OutOnTheMainLinesLandABranchHasItsGroundBesideItAndTheSkyOverIt(string spec)
    {
        // Along each alternate and dead line, every 250 m where it runs 40-280 m off the main line (on the main line's land,
        // whose columns out there are 22-50 m apart), an eye 2.5 m over its rail looking on down it, the scene built round
        // it as the game builds it. Before note 498 the main line's land spanned the branch's cutting from column to column
        // overhead, and the branch's own land was left out under it, so the ground beside the ballast was open. These two
        // nights' dead lines run out on that land longest (4 and 5 places each) on today's 9 km nights.
        var route = Routes.Generate(Content, spec, 6);
        var line = route.Build();
        var plan = route.Plan!;
        var tuning = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
        // A Look of its own, let go after: a static one held its art's cells and pieces for the whole run, beside every
        // other class's, and the suite's host ran out of memory.
        var scene = new GreyboxScene { DrawDistance = 400, Look = Look.Load(Content), Route = route, Time = 0.37 };
        var problems = new List<string>();
        int places = 0;
        foreach (var b in line.Branches.Where(b => b.Kind is BranchKind.Alternate or BranchKind.DeadLine))
        {
            string edge = plan.Alignment.First(a => a.Branch == b.Index && a.Role is EdgeRole.Alternate or EdgeRole.DeadLine).Edge;
            for (double s = 150; s < b.Local.Length - 150; s += 250)
            {
                // Not on or under its own bridge or through its own bore: those have their own ground.
                if (plan.Structures.Any(st => st.Edge == edge && st.S0 < s + 80 && st.S1 > s - 10))
                    continue;
                var (fromMain, hint) = OffMain(line, b, s);
                if (fromMain is < 40 or > 280)
                    continue;
                var at = b.Local.Sample(s);
                var eye = at.Position + Double3.Up * 2.5;
                var tris = Built(scene, tuning, line, hint, eye);
                Vector3 At(double ahead, double lateral, double up)
                {
                    var q = b.Local.Sample(s + ahead);
                    return (q.Position + Double3.Cross(q.Tangent, Double3.Up).Normalized * lateral + Double3.Up * up).RelativeTo(eye);
                }
                foreach (double ahead in new[] { 20.0, 40, 60 })
                {
                    // The air over the track is open: nothing spans it, low or high.
                    foreach (double up in new[] { 1.5, 8, 20 })
                        if (TunnelArtTests.Hits(tris, Vector3.Zero, At(ahead, 0, up)))
                            problems.Add($"{edge} at {s:0} ({fromMain:0} m off the main line): a roof over the track {ahead} m on, {up} m up");
                    // The ground right beside the ballast (its foot at 2.6 m): a sight line to just over it lands on it.
                    foreach (int side in new[] { -1, 1 })
                        if (!TunnelArtTests.Hits(tris, Vector3.Zero, At(ahead, side * 3.1, -0.3) * 1.1f))
                            problems.Add($"{edge} at {s:0} ({fromMain:0} m off the main line): no ground {(side > 0 ? "right" : "left")} of the ballast {ahead} m on");
                }
                places++;
            }
        }
        Assert.True(places >= 4, $"{spec}: only {places} places out on the main line's land");
        Assert.True(problems.Count == 0, $"{spec}, {problems.Count} of {places * 15} sight lines:\n" + string.Join("\n", problems.Take(20)));
    }

    [Fact]
    public void OutPastTheMainLinesLandABranchHasLandAllRoundItAndNoHillsOverIt()
    {
        // The dead lines that run furthest out (1.3-1.6 km off the main line on today's nights), wherever they're 400 m and
        // more off it: past the main line's land (300 m), under its far land's columns (292-1,700 m out, 240-550 m apart there).
        // Before note 498 the main line's far hills spanned the dead line from column to column, a roof tens of metres over
        // it, and a dead line past the far land's edge (1.7 km) had nothing past its own 78 m: the sky. From an eye 3 m over
        // its rail, every 300 m and at a dead line's end: the air over the track ahead is open, and every sight line down
        // through the ground out to 500 m either side, and ahead past the buffer stop, lands on some land.
        var tuning = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
        var problems = new List<string>();
        int places = 0, rays = 0;
        foreach (string spec in new[] { "deadLines:6", "frontier:7", "local:4" })
        {
            var route = Routes.Generate(Content, spec, 6);
            var line = route.Build();
            var plan = route.Plan!;
            // A Look of its own, let go after: a static one held its art's cells and pieces for the whole run, beside every
            // other class's, and the suite's host ran out of memory.
            var scene = new GreyboxScene { DrawDistance = 400, Look = Look.Load(Content), Route = route, Time = 0.37 };
            foreach (var b in line.Branches.Where(b => b.Kind is BranchKind.Alternate or BranchKind.DeadLine))
            {
                string edge = plan.Alignment.First(a => a.Branch == b.Index && a.Role is EdgeRole.Alternate or EdgeRole.DeadLine).Edge;
                var stations = new List<double>();
                for (double s = 200; s < b.Local.Length - 100; s += 300)
                    stations.Add(s);
                if (!b.Rejoins)
                    stations.Add(b.Local.Length - 20);
                foreach (double s in stations)
                {
                    if (plan.Structures.Any(st => st.Edge == edge && st.S0 < s + 150 && st.S1 > s - 10))
                        continue;
                    var (fromMain, hint) = OffMain(line, b, s);
                    if (fromMain < 400)
                        continue;
                    var at = b.Local.Sample(s);
                    var eye = at.Position + Double3.Up * 3;
                    var tris = Built(scene, tuning, line, hint, eye);
                    var end = b.Local.Sample(b.Local.Length);
                    Double3 On(double ahead, double lateral)
                    {
                        // On along the track, and past its end along its last heading.
                        double along = s + ahead;
                        var q = along <= b.Local.Length ? b.Local.Sample(along) : end with { Position = end.Position + end.Tangent * (along - b.Local.Length) };
                        return q.Position + Double3.Cross(q.Tangent, Double3.Up).Normalized * lateral;
                    }
                    string where = $"{spec} {edge} at {s:0} ({fromMain:0} m off the main line)";
                    // The air over the track ahead is open: no hills span it (short of a dead line's end, where its own land goes on).
                    foreach (double ahead in new[] { 30.0, 60 })
                        if (s + ahead < b.Local.Length)
                            foreach (double up in new[] { 8.0, 20, 40 })
                                if (TunnelArtTests.Hits(tris, Vector3.Zero, (On(ahead, 0) + Double3.Up * up).RelativeTo(eye)))
                                    problems.Add($"{where}: a roof over the track {ahead} m on, {up} m up");
                    var targets = new List<(double Ahead, double Lateral)>();
                    foreach (double ahead in new[] { 40.0, 120 })
                        foreach (double lateral in new[] { -500.0, -250, -120, 120, 250, 500 })
                            targets.Add((ahead, lateral));
                    if (!b.Rejoins && s > b.Local.Length - 50)
                        foreach (double ahead in new[] { 60.0, 170, 420 })
                            foreach (double lateral in new[] { -100.0, 0, 100 })
                                targets.Add((ahead, lateral));
                    foreach (var (ahead, lateral) in targets)
                    {
                        var w = On(ahead, lateral);
                        var ground = (w with { Y = line.Conditions!.Ground(w) - 1 }).RelativeTo(eye);
                        // Down below the horizon only, as `dt holes` asks: up a hillside the sky over its crest is the sky.
                        if (ground.Y > -2)
                            continue;
                        rays++;
                        // On through it for 2 km: past the branch's own land (78 m) its far land lies under the ground's own lie (2 m
                        // and more, its columns 50-80 m apart there), as the main line's does under its corridor's, so what's asked is
                        // whether the sight line lands on any land at all, or goes on into the sky.
                        if (!TunnelArtTests.Hits(tris, Vector3.Zero, Vector3.Normalize(ground) * 2000))
                            problems.Add($"{where}: nothing on the ground {ahead} m on, {lateral} m out");
                    }
                    places++;
                }
            }
        }
        Assert.True(places >= 4, $"only {places} places out past the main line's land");
        Assert.True(problems.Count == 0, $"{problems.Count} problems, {places} places, {rays} sight lines down:\n" + string.Join("\n", problems.Take(20)));
    }

    /// <summary>How far a branch is from the main line at <paramref name="s"/> along it, and the main line's distance nearest it.</summary>
    static (double FromMain, double Hint) OffMain(RailLine line, Branch b, double s)
    {
        var at = b.Local.Sample(s);
        double hint = b.Toe + s;
        line.Nearest(at.Position, ref hint);
        var off = line.Sample(hint).Position - at.Position;
        return (Math.Sqrt(off.X * off.X + off.Z * off.Z), hint);
    }

    /// <summary>The scene the game builds round <paramref name="eye"/>, the train on the main line 700 m short of it.</summary>
    static List<(Vector3, Vector3, Vector3)> Built(GreyboxScene scene, TrainTuning tuning, RailLine line, double hint, Double3 eye)
    {
        var mesh = new MeshBuilder();
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(tuning, 6, 1)), line, Math.Clamp(hint - 700, 60, line.Length - 60));
        scene.Build(mesh, train, eye);
        return TunnelArtTests.Triangles(mesh);
    }
}
