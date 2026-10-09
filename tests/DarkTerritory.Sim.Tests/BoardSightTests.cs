using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The line's lettered boards are read from the cab (ARCHITECTURE §8 note 560; the director, 9 Oct: "they need to be massive
/// like billboards and have clear sight lines and be illuminated by the lamp for a long while"): lettered big, stood back
/// from the line where the driver sees them in the lamp over their approach, and nothing of the lineside left standing in
/// those sight lines.
/// </summary>
[Collection(nameof(LineGenTests))]
public class BoardSightTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly string[] Nights = ["frontier:7", "deadLines:2", "deepTerritory:2"];

    static RailLine LineOf(Route.Route route, RailLine main, string edge) =>
        edge == "main" ? main : main.Branches[route.Plan!.Edge(edge).Branch].Local;

    [Fact]
    public void ANamesCapitalsAreReadFromWellUpTheLine()
    {
        // From the cab (the eye's 75 degrees at 1280x720: 469 px to the radian), a board's font pixel is at least a screen
        // pixel from 150 m (a name, a limit, a closed line: what has to be read), and a speed's figures from 250 m.
        var signage = DataFile.Load<SignageFile>(Path.Combine(Content, "linegen", "signage.json"));
        double focal = 360 / Math.Tan(37.5 * Math.PI / 180);
        double Reach(string type) => signage.Boards[type].LetterM / BoardLayout.GlyphHeight * focal;
        foreach (var type in new[] { "facility", "junction", "stationName", "lineClosed", "yardLimit", "bridgeLimit", "limitReason", "mailDrop" })
            Assert.True(Reach(type) >= 150, $"{type}'s letters read from {Reach(type):0} m");
        foreach (var type in new[] { "speedBoard", "resumeBoard", "restricted", "whistle" })
            Assert.True(Reach(type) >= 200, $"{type}'s figures read from {Reach(type):0} m");
        // And it shines back to as far as it's read.
        Assert.All(signage.Boards.Where(b => b.Value.Reflective), b => Assert.True(b.Value.VisibleM >= 110));
    }

    [Fact]
    public void EveryBoardIsSeenInTheLampOverItsApproach()
    {
        foreach (var spec in Nights)
        {
            var route = Routes.Generate(Content, spec, 6);
            var line = route.Build();
            var side = LinesideProps.Of(route, line)!;
            var rules = route.Plan!.Rules.Billboard;
            var standing = side.PlanBoards.Where(b => b.Key.State == SignState.Intact && !b.Value.Plate).ToList();
            Assert.NotEmpty(standing);
            foreach (var (sign, site) in standing)
            {
                // Stood back off the track, on the billboard side, turned to the cab coming, up over the rail.
                var at = LineOf(route, line, sign.Edge).Sample(site.Along);
                var inner = site.Bottom - site.Right * (site.Side * site.Face.Width / 2);
                double lateral = Double3.Dot(inner - at.Position, Double3.Cross(at.Tangent, Double3.Up).Normalized);
                Assert.InRange(lateral * site.Side, rules.InnerM - 0.5, rules.InnerM + 0.5);
                Assert.True(Double3.Dot(site.Toward, at.Tangent) < -0.9, $"{spec} {sign.Type} {sign.Text}: faces the train coming");
                Assert.True(site.Bottom.Y >= at.Position.Y + 2, $"{spec} {sign.Type} {sign.Text}: up over the rail");
                // A warning is never moved on past where the plan put it.
                if (route.Plan.Rules.Boards[sign.Type].Warns)
                    Assert.True(site.Along <= sign.S + 1e-6, $"{spec} {sign.Type} {sign.Text} moved on {site.Along - sign.S:0} m");
            }
            // On the billboard side, across from the telegraph poles, but where an alternate's track or a road runs along it.
            Assert.True(standing.Count(b => b.Value.Side == rules.Side) >= standing.Count * 0.6,
                $"{spec}: {standing.Count(b => b.Value.Side == rules.Side)} of {standing.Count} on the billboard side");
            // Read in the lamp, past the land, over most of the line's approaches (a board on a bend has its lamp on it
            // late; one in a cutting is moved back to where it's seen).
            double seen = standing.Sum(b => b.Value.SeenM), approach = standing.Sum(b => b.Value.ApproachM);
            Assert.True(seen >= 0.9 * approach, $"{spec}: boards seen over {seen / approach:P0} of their approaches");
            Assert.True(standing.Count(b => b.Value.SeenM < rules.SeenShare * b.Value.ApproachM) <= standing.Count / 10,
                $"{spec}: {string.Join(", ", standing.Where(b => b.Value.SeenM < rules.SeenShare * b.Value.ApproachM).Select(b => $"{b.Key.Type} {b.Key.Text} {b.Value.SeenM}/{b.Value.ApproachM}"))}");
        }
    }

    [Fact]
    public void NothingOfTheLinesideStandsInABoardsSightLines()
    {
        foreach (var spec in Nights)
        {
            var route = Routes.Generate(Content, spec, 6);
            var line = route.Build();
            var side = LinesideProps.Of(route, line)!;
            var rules = route.Plan!.Rules.Billboard;
            int lines = 0;
            foreach (var (sign, site) in side.PlanBoards.Where(b => b.Key is { State: SignState.Intact, Edge: "main" } && !b.Value.Plate))
            {
                var props = side.Props(site.Along - 320, site.Along + 30).Where(p => p.Kind != LinesideKind.Pole).ToList();
                foreach (double a in site.SeenFrom)
                {
                    var eye = line.Sample(a).Position;
                    foreach (var target in new[] { site.Centre, site.Bottom - site.Right * (site.Face.Width * 0.45), site.Bottom + site.Right * (site.Face.Width * 0.45) })
                    {
                        lines++;
                        foreach (var p in props)
                        {
                            var w = side.World(p.Along, p.Lateral);
                            double d = Distance(w.X, w.Z, eye.X, eye.Z, target.X, target.Z);
                            Assert.True(d >= side.SightRadius(p) + rules.ClearOfM - 1e-6,
                                $"{spec}: a {p.Species} {d:0.0} m off the sight line from {a:0} to the {sign.Type} {sign.Text} at {site.Along:0}");
                        }
                    }
                }
            }
            Assert.True(lines > 100, $"{spec}: only {lines} sight lines");
        }
    }

    [Fact]
    public void BoardsCloseTogetherHangOneUnderAnother()
    {
        // A limit and what it's for (note 265's BRASS), or anything else within stackM: one set of posts, no face before another.
        var route = Routes.Generate(Content, "frontier:7", 6);
        var line = route.Build();
        var side = LinesideProps.Of(route, line)!;
        var standing = side.PlanBoards.Where(b => b.Key.State == SignState.Intact && !b.Value.Plate).Select(b => b.Value).ToList();
        int stacked = 0;
        foreach (var a in standing)
            foreach (var b in standing)
            {
                if (ReferenceEquals(a, b) || a.Edge != b.Edge || Math.Abs(a.Along - b.Along) > 1e-6)
                    continue;
                stacked++;
                bool apart = a.Bottom.Y >= b.Bottom.Y + b.Face.Height - 1e-6 || b.Bottom.Y >= a.Bottom.Y + a.Face.Height - 1e-6;
                Assert.True(apart, $"{a.Type} {a.Text} and {b.Type} {b.Text} overlap");
                Assert.True(a.Posts.Count == 0 || b.Posts.Count == 0, "one set of posts");
            }
        Assert.True(stacked > 0, "no boards stand together on frontier:7 (its BRASS boards under their limits)");
        // And otherwise no two stand so close along the line that the nearer would hide the further.
        foreach (var a in standing)
            foreach (var b in standing)
                if (a.Edge == b.Edge && b.Along > a.Along + 1e-6)
                    Assert.True(b.Along - a.Along >= 10, $"{a.Type} {a.Text} at {a.Along:0} and {b.Type} {b.Text} at {b.Along:0}");
    }

    static double Distance(double x, double z, double ax, double az, double bx, double bz)
    {
        double dx = bx - ax, dz = bz - az, len = dx * dx + dz * dz;
        double t = len < 1e-12 ? 0 : Math.Clamp(((x - ax) * dx + (z - az) * dz) / len, 0, 1);
        return double.Hypot(ax + dx * t - x, az + dz * t - z);
    }
}
