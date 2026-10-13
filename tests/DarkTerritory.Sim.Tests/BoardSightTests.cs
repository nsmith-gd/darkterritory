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
                // (A km post low by the line, under the eye line.)
                if (route.Plan.Rules.Boards[sign.Type].Side == 0)
                    Assert.True(site.Bottom.Y >= at.Position.Y + 2, $"{spec} {sign.Type} {sign.Text}: up over the rail");
                // A warning is never moved on past where the plan put it.
                if (route.Plan.Rules.Boards[sign.Type].Warns)
                    Assert.True(site.Along <= sign.S + 1e-6, $"{spec} {sign.Type} {sign.Text} moved on {site.Along - sign.S:0} m");
            }
            // On the billboard side, across from the telegraph poles, but where an alternate's track, a road or a yard's
            // ground runs along it, or a board there would stand in front of another (frontier:7's Renwick Yard has the
            // left for most of 1.5 km); the km posts on their own side, by the poles.
            var billboards = standing.Where(b => route.Plan.Rules.Boards[b.Key.Type].Side == 0).ToList();
            Assert.True(billboards.Count(b => b.Value.Side == rules.Side) >= billboards.Count * 0.4,
                $"{spec}: {billboards.Count(b => b.Value.Side == rules.Side)} of {billboards.Count} on the billboard side");
            var posts = standing.Where(b => route.Plan.Rules.Boards[b.Key.Type].Side != 0).ToList();
            Assert.True(posts.Count(b => b.Value.Side == route.Plan.Rules.Boards[b.Key.Type].Side) >= posts.Count * 0.8,
                $"{spec}: {posts.Count(b => b.Value.Side == route.Plan.Rules.Boards[b.Key.Type].Side)} of {posts.Count} posts on their own side");
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

    /// <summary>Every board standing beside a night's line: the plan's and the sim's own (sight.json's, as the content has them).</summary>
    static List<BoardSite> Standing(Route.Route route, LinesideProps side) =>
        [.. side.PlanBoards.Where(b => b.Key.State == SignState.Intact && !b.Value.Plate).Select(b => b.Value)
            .Concat(Route.Lineside.Boards(new Route.SightTuning(), route).Select(side.SightBoard))];

    [Fact]
    public void BoardsCloseTogetherHangOneUnderAnother()
    {
        // A limit and what it's for (note 265's BRASS), a mail board by a bridge's, anything else within stackM: one set of
        // posts, no face before another.
        var route = Routes.Generate(Content, "frontier:7", 6);
        var line = route.Build();
        var side = LinesideProps.Of(route, line)!;
        var standing = Standing(route, side);
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
        Assert.True(stacked > 0, "no boards stand together on frontier:7 (its mail boards with what's by them)");
        // And otherwise no two stand so close along the line, on a side, that the nearer would hide the further.
        foreach (var a in standing)
            foreach (var b in standing)
                if (a.Edge == b.Edge && a.Side == b.Side && b.Along > a.Along + 1e-6)
                    Assert.True(b.Along - a.Along >= 10, $"{a.Type} {a.Text} at {a.Along:0} and {b.Type} {b.Text} at {b.Along:0}");
        // None hung together is taller than stackMaxM.
        foreach (var group in standing.Where(b => b.Posts.Count > 0 || standing.Any(o => !ReferenceEquals(o, b) && o.Edge == b.Edge && Math.Abs(o.Along - b.Along) < 1e-6))
            .GroupBy(b => (b.Edge, Math.Round(b.Along, 3), b.Side)))
        {
            double low = group.Min(b => b.Bottom.Y), high = group.Max(b => b.Bottom.Y + b.Face.Height);
            Assert.True(high - low <= route.Plan!.Rules.Billboard.StackMaxM + 1e-6, $"{string.Join(" over ", group.Select(b => b.Text))}: {high - low:0.0} m tall");
        }
    }

    [Fact]
    public void NoBoardStandsInFrontOfAnotherWhileItsRead()
    {
        // The director, 13 Oct (`pt13-signs`): "Some signs are overlapping." From where a board's words are made out
        // (clearFromM) until it's passed (readToM), from the driver's eye, no other board's face (the plan's, the sim's mail
        // and limit boards, the km posts) is between them and any of its face (FaceGrid), from anywhere along the line.
        foreach (var spec in Nights)
        {
            var route = Routes.Generate(Content, spec, 6);
            var line = route.Build();
            var side = LinesideProps.Of(route, line)!;
            var rules = route.Plan!.Rules.Billboard;
            var standing = Standing(route, side);
            int lines = 0;
            foreach (var board in standing.Where(b => b.Edge == "main"))
                for (double a = board.Along - rules.ClearFromM; a <= board.Along - rules.ReadToM + 1e-9; a += rules.SightStepM / 3)
                {
                    if (a < 0)
                        continue;
                    var t = line.Sample(a);
                    var dir = new Double3(t.Tangent.X, 0, t.Tangent.Z).Normalized;
                    var eye = t.Position + Double3.Up * rules.EyeM + Double3.Cross(dir, Double3.Up).Normalized * rules.EyeAcrossM;
                    // (Only where the lamp's on it and the land doesn't hide it anyway: what's to be read.)
                    if (!board.SeenFrom.Any(s => BoardSite.AboutEye(s, rules.SightStepM).Any(e => Math.Abs(e - a) < 1e-6)))
                        continue;
                    foreach (var target in board.FaceGrid())
                    {
                        lines++;
                        foreach (var other in standing)
                            Assert.False(!ReferenceEquals(other, board) && other.Crosses(eye, target),
                                $"{spec}: the {other.Type} {other.Text} at {other.Along:0} stands in front of the {board.Type} {board.Text} at {board.Along:0}, seen from {a:0}");
                    }
                }
            Assert.True(lines > 100, $"{spec}: only {lines} sight lines");
            // And where a board couldn't be stood clear of the others, it's said (none on these nights).
            Assert.All(standing, b => Assert.Equal(0, b.BoardsInWay));
        }
    }

    [Fact]
    public void TheKmPostsStandAtTheirKmAndABridgesNameIsStoodOnce()
    {
        // A km post marks its km (signage.json's `fixed`): nudged a step or two to be seen, never shoved along by the
        // billboards. A bridge's plate at each end, both facing the train coming, would stand one in front of the other:
        // the second isn't stood.
        foreach (var spec in Nights)
        {
            var route = Routes.Generate(Content, spec, 6);
            var side = LinesideProps.Of(route, route.Build())!;
            var rules = route.Plan!.Rules;
            foreach (var (sign, site) in side.PlanBoards.Where(b => b.Key.State == SignState.Intact && rules.Boards[b.Key.Type].Fixed))
                Assert.True(Math.Abs(site.Along - sign.S) <= 2 * rules.Billboard.StepBackM + 1e-6, $"{spec}: km post {sign.Text} {site.Along - sign.S:0} m off its km");
            foreach (var group in side.PlanBoards.Where(b => b.Key is { State: SignState.Intact, Type: "bridge" }).GroupBy(b => (b.Key.Edge, b.Key.Text)))
            {
                var at = group.Select(b => b.Key.S).OrderBy(x => x).ToList();
                for (int i = 1; i < at.Count; i++)
                    Assert.True(at[i] - at[i - 1] > rules.Boards["bridge"].VisibleM, $"{spec}: {group.Key.Text} stood twice, {at[i] - at[i - 1]:0} m apart");
            }
        }
    }

    [Fact]
    public void TheSimsOwnBoardsStandWhereTheirSightLinesWereCleared()
    {
        // The lineside clears for the sim's boards (sight.json's: a bend's advance limit, LOW, MAIL, END) at the default
        // tuning, before any night's is loaded; the content's puts them in the same places, so the clearing is theirs.
        var sight = DataFile.Load<Route.SightTuning>(Path.Combine(Content, Route.SightTuning.File));
        var route = Routes.Generate(Content, "frontier:7", 6);
        var boards = Route.Lineside.Boards(sight, route).ToList();
        Assert.Equal(Route.Lineside.Boards(new Route.SightTuning(), route).Select(b => (b.Kind, b.Board)), boards.Select(b => (b.Kind, b.Board)));
        var side = LinesideProps.Of(route, route.Build())!;
        Assert.Contains(boards, b => b.Kind == Route.SignKind.Drop);
        foreach (var b in boards)
        {
            var site = side.SightBoard(b);
            Assert.Same(site, side.SightBoard(b));
            Assert.Equal(LinesideProps.SightLettering(b).Text, site.Text);
        }
        // (One on frontier:7 is on a ledge round a hillside, seen from nowhere within its reach.)
        Assert.True(boards.Sum(b => side.SightBoard(b).SeenM) >= 0.85 * boards.Sum(b => side.SightBoard(b).ApproachM));
    }

    static double Distance(double x, double z, double ax, double az, double bx, double bz)
    {
        double dx = bx - ax, dz = bz - az, len = dx * dx + dz * dz;
        double t = len < 1e-12 ? 0 : Math.Clamp(((x - ax) * dx + (z - az) * dz) / len, 0, 1);
        return double.Hypot(ax + dx * t - x, az + dz * t - z);
    }
}
