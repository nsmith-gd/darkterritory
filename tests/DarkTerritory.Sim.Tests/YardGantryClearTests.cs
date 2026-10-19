using Ballast;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// No yard gantry stands in a building (queue #596, ARCHITECTURE §8 note 869; the director's playtest of 19 Oct, line 9:
/// "Crane overlap inside a building is a bug", the switchyard's hook and its load hanging inside a shed by the sidings, the
/// bridge through its wall). Every loading face's sheds stand 4 m off it, and a gantry reaches 9 m out (its cab and its
/// castings further), so a shed the runway passes on the gantry's side is cut open for it (StopWalls.CraneCut): the gantry's
/// legs, bridge, cab and castings stand in the open, clear of every roofed length of every building.
/// </summary>
public class YardGantryClearTests
{
    static readonly StopTuning S = Tuning.Route.Stops!;
    static readonly StopContext Cx = Tuning.Route.StopContext;
    static readonly CraneTuning Crane = DataFile.Load<FacilityTuning>(Path.Combine(DataFile.FindContentRoot(), FacilityTuning.File)).Crane;

    // The art's pieces' half sizes (tools/models: crane_leg 0.9 m square, crane_cab 2.1 m), not design numbers.
    const double LegHalf = 0.45, CabHalf = 1.05;

    [Theory]
    [InlineData(RouteTier.Local)]
    [InlineData(RouteTier.Frontier)]
    [InlineData(RouteTier.DeadLines)]
    [InlineData(RouteTier.DeepTerritory)]
    public void EveryYardGantryStandsClearOfTheBuildings(RouteTier tier)
    {
        int gantries = 0;
        foreach (var kind in new[] { StopKind.Yard, StopKind.YardAndVillage })
            foreach (var facility in new FacilityKind?[] { null, FacilityKind.Foundry, FacilityKind.Switchyard })
                for (ulong seed = 1; seed <= 30; seed++)
                {
                    var stop = StopGenerator.Generate(S, tier, seed, kind, Cx with { Facility = facility });
                    foreach (var track in stop.Tracks)
                    {
                        if (track.Crane is not { Bays: > 0 } rw)
                            continue;
                        gantries++;
                        int toward = StopWalls.GantryToward(stop, track);
                        double face = track.FaceStart.D;
                        // Its footprint across the face, toward its castings: the near legs to the cab beyond the far ones; and
                        // along it, the runway and its legs.
                        double u0 = Crane.Span[0] - LegHalf, u1 = Math.Max(Crane.Span[1] + LegHalf, Crane.Span[1] + Crane.CabOut + CabHalf);
                        double a0 = rw.From - LegHalf, a1 = rw.To + LegHalf;
                        for (int i = 0; i < stop.Buildings.Count; i++)
                        {
                            var b = stop.Buildings[i];
                            var rooms = b.Kind == BuildingKind.Shed ? StopWalls.Roofed(stop, i).ToList() : [(-b.Length / 2, b.Length / 2)];
                            foreach (var (lo, hi) in rooms)
                            {
                                // (The yard's sheds stand square to the line.)
                                double s0 = b.S + lo, s1 = b.S + hi;
                                double w0 = toward * (b.D - b.Width / 2 - face), w1 = toward * (b.D + b.Width / 2 - face);
                                double near = Math.Min(w0, w1), far = Math.Max(w0, w1);
                                bool overlaps = s0 < a1 && s1 > a0 && near < u1 && far > u0;
                                Assert.False(overlaps, $"{tier} {kind} {facility} seed {seed}: track {track.Index}'s gantry ({a0:0.0}..{a1:0.0} along, "
                                    + $"{u0:0.0}..{u1:0.0} out) stands in {b.Kind} {i} ({s0:0.0}..{s1:0.0}, {near:0.0}..{far:0.0})");
                            }
                        }
                    }
                }
        Assert.True(gantries > 10, $"{tier}: only {gantries} yard gantries to look at");
    }
}
