using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 998 (the director's playtest of 23 Oct, line 18: "We went under a bridge, and I clipped right through the bridge
/// top. That should be treated like ... a tunnel. If I hit my head on that, should have that comedic boink"): a through
/// truss's bracing is a low clearance, boarded, warned of and struck like a tunnel's mouth.
/// </summary>
public class BridgeClearanceTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly string Content = DataFile.FindContentRoot();
    static readonly SightTuning S = DataFile.Load<SightTuning>(Path.Combine(Content, SightTuning.File));

    static Route.Route Truss() =>
        new("test", RouteTier.Frontier, 1, new LineDefinition("test", [new TrackSegment(4000)]),
            [new RouteFeature(FeatureKind.Bridge, 1500, 1700) { Through = true }], new RouteWeather(0.01, false, 0, 0), 3600);

    /// <summary>A rider on car 2's roof held at 12 m/s through the truss, crouched on the key or not; the world moves them as the host does.</summary>
    static (PlayerState Rider, bool Under, bool Warned) Ride(SightTuning sight, bool crouch)
    {
        var route = Truss();
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 5, 1)), route.Build(), 600);
        var world = new World(train, Tuning.Combat);
        world.EnableBodies();
        world.EnableLineside(sight, route);
        world.Hand = P.Hand;
        world.Movement = P;
        var s = PlayerMotor.SpawnOnRoof(train, 2, 0, P);
        bool under = false, warned = false;
        var intent = new PlayerIntent { Moves = crouch ? PlayerMoves.Crouch : PlayerMoves.None };
        while (train.Dynamics.RearDistance < 1750 && s.Alive)
        {
            under |= world.Lineside!.UnderLowClearance(s, train, P.Height);
            warned |= world.Lineside.Warning(train) is { Kind: SignKind.LowClearance, Bridge: true };
            train.Dynamics.Velocity = 12;
            world.BeginTick();
            world.CrewAct(ref s, intent, 1);
            world.Step(new TrainControls { Reverser = 1 });
            world.ApplyDamage(_ => s, (_, v) => s = v, [1]);
            PlayerMotor.Step(ref s, intent, train, P, T, SimConstants.TickSeconds, applyLook: false);
        }
        return (s, under, warned);
    }

    [Fact]
    public void ATrussIsBoardedAsALowClearance()
    {
        var sign = Lineside.Boards(S, Truss()).Single(s => s.Kind == SignKind.LowClearance);
        Assert.True(sign.Bridge);
        Assert.Equal(S.TrussClearance, sign.Clearance);
        Assert.Equal((1500.0, 1700.0, 1500 - S.BoardAhead), (sign.Start, sign.End, sign.Board));
    }

    [Fact]
    public void ATrussTakesWhoeverStandsOnARoofAndACrouchGoesUnder()
    {
        // The bracing's under a standing head on a roof: what makes it a hazard at all.
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 5, 1)), Truss().Build(), 600);
        Assert.True(PlayerMotor.SpawnOnRoof(train, 2, 0, P).Position.Y + P.Height > S.TrussClearance);

        var (stood, _, warned) = Ride(S, crouch: false);
        Assert.True(warned, "no warning for the truss");
        Assert.Equal(DeathCause.Struck, stood.Death);
        var (crouched, under, _) = Ride(S, crouch: true);
        Assert.True(under);
        Assert.True(crouched.Alive, $"crouched under the bracing: {crouched.Death}");
        // Its clearance is the data's: bracing over a standing head takes nobody.
        var (clear, _, _) = Ride(S with { TrussClearance = 20 }, crouch: false);
        Assert.True(clear.Alive, $"under bracing 20 m up: {clear.Death}");
    }

    [Fact]
    public void EveryThroughTrussOnAGeneratedLineIsBoardedLikeATunnel()
    {
        int seen = 0;
        foreach (var name in new[] { "frontier:7", "deadLines:3", "deepTerritory:2", "frontier:1", "frontier:3" })
        {
            var route = Routes.Generate(Content, name, 6);
            var trusses = route.Plan!.Structures.Where(st => st.Edge == "main" && st.Type == StructureType.Truss).ToList();
            var signs = Lineside.Boards(S, route).ToList();
            foreach (var st in trusses)
                Assert.Contains(signs, s => s.Kind == SignKind.LowClearance && s.Bridge && s.Start == st.S0 && s.End == st.S1);
            Assert.Equal(trusses.Count, signs.Count(s => s.Kind == SignKind.LowClearance && s.Bridge));
            seen += trusses.Count;
        }
        Assert.True(seen > 0, "no through truss on any of the lines tried");
    }
}
