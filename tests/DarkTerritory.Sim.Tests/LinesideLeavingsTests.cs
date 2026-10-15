using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The railway's leavings beside the line are solid (ARCHITECTURE §8 note 792; GDD App. F.1 "the world is solid", its last
/// lineside piece "still open"; E1's note 325). The sim deals them from the night's seed by the art's rules, a deck of every
/// kind a stretch at a time, kept 2 m off the line's middle for the cars going by (note 780), and stands each piece on its
/// footprint; the art draws them from its list.
/// </summary>
[Collection(nameof(LineGenTests))]
public class LinesideLeavingsTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly IReadOnlySet<string> Pieces = Leavings.PieceNames;

    static (Route.Route Route, RailLine Line, LinesideProps Side) Night(string spec)
    {
        var route = Routes.Generate(Content, spec, 6);
        var line = route.Build();
        return (route, line, LinesideProps.Of(route, line)!);
    }

    [Fact]
    public void EachNightLeavesItsOwnBesideTheLineAndTheyStandAsWalls()
    {
        var footprints = DataFile.Load<FootprintsFile>(Path.Combine(Content, LineGenConfig.Directory, "footprints.json"));
        var seen = new HashSet<string>();
        foreach (string spec in new[] { "frontier:7", "local:12", "deadLines:2" })
        {
            var (route, line, side) = Night(spec);
            var leavings = side.Props(0, line.Length).Where(p => p.Kind == LinesideKind.Piece && Pieces.Contains(p.Species)).ToList();
            Assert.True(leavings.Count > 30, $"{spec}: {leavings.Count} pieces of leavings down {line.Length / 1000:0.0} km");
            // Every piece the leavings use has its footprint, and stands on it.
            foreach (var p in leavings)
                Assert.True(footprints.Pieces.ContainsKey(p.Species) && footprints.Pieces[p.Species][p.Variant].Length > 0, $"{p.Species}-{p.Variant} has no footprint");
            // The night's own: no two of these nights leave the same things in the same places.
            Assert.True(seen.Add(string.Join(";", leavings.Select(p => $"{p.Along:0}:{p.Species}"))), $"{spec} leaves what another night does");
            // Dealt alike every time (a client builds its own and walks into the same walls).
            var again = Night(spec);
            Assert.Equal(leavings, again.Side.Props(0, again.Line.Length).Where(p => p.Kind == LinesideKind.Piece && Pieces.Contains(p.Species)));
            // None comes within 2 m of the line's middle: each box of each piece, turned as its wall stands.
            foreach (var p in leavings)
                foreach (var b in footprints.Pieces[p.Species][p.Variant])
                {
                    double c = DMath.Cos(p.Yaw), s = DMath.Sin(p.Yaw), turn = p.Yaw + b[4];
                    double centre = p.Lateral + b[0] * c + b[1] * s;
                    double spread = Math.Abs(b[2] * DMath.Cos(turn)) + Math.Abs(b[3] * DMath.Sin(turn));
                    Assert.True(Math.Sign(p.Lateral) * centre - spread >= LinesideProps.LeavingClearM - 1e-6,
                        $"{spec}: {p.Species} at {p.Along:0} m, {p.Lateral:0.0} out, reaches {Math.Sign(p.Lateral) * centre - spread:0.00} m off the middle");
                }
        }
    }

    [Fact]
    public void TheWoodsKeepOffTheLeavings()
    {
        // Note 794 (note 792's "not yet"): the woods and the biomes' props were dealt without asking where the leavings lie,
        // and a lineside tree could grow up through a gangs' hut. Nothing of the rest of the lineside stands within a leaving
        // piece's reach (its footprint round its foot), its own trunk's or reach added.
        foreach (string spec in new[] { "frontier:7", "local:12", "deadLines:2" })
        {
            var (_, line, side) = Night(spec);
            var all = side.Props(0, line.Length).ToList();
            var leavings = all.Where(p => p.Kind == LinesideKind.Piece && Pieces.Contains(p.Species)).ToList();
            Assert.NotEmpty(leavings);
            foreach (var p in all.Where(p => p.Kind != LinesideKind.Pole && !(p.Kind == LinesideKind.Piece && Pieces.Contains(p.Species))))
            {
                double reach = p.Kind == LinesideKind.Tree ? p.Radius : side.Extent(p);
                foreach (var l in leavings.Where(l => Math.Abs(l.Along - p.Along) < 20))
                    Assert.True(double.Hypot(l.Along - p.Along, l.Lateral - p.Lateral) >= side.Extent(l) + reach,
                        $"{spec}: a {p.Species} at {p.Along:0.0} m, {p.Lateral:0.0} out, stands in a {l.Species} at {l.Along:0.0}, {l.Lateral:0.0}");
            }
        }
    }

    [Fact]
    public void ALeavingIsAWallWhereItStands()
    {
        // A crewmate walking beside the train is stopped by the gangs' hut and goes round their stack of ties.
        var (route, line, side) = Night("frontier:7");
        var forts = Fortresses.Of(route, line, 600, Tuning.Run.TerminusZone);
        var walls = side.Walls(forts, Tuning.Run.Walls.LinesideReachM).ToList();
        var hut = side.Props(0, line.Length).First(p => p.Species == "junkHut" && !LinesideProps.InsideAFort(forts, p.Along, p.Lateral));
        var t = line.Sample(hut.Along);
        var foot = t.Position + Double3.Cross(t.Tangent, Double3.Up).Normalized * hut.Lateral;
        Assert.Contains(walls, w => double.Hypot(w.Centre.X - foot.X, w.Centre.Z - foot.Z) < 2 && w.Top > hut.Ground + 2);
    }
}
