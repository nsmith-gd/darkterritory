using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 573 (queue #303, D1's ask after #295's falls audit, and the director's off Stroud Bridge): spec B.3's landing knock is
/// for leaving the train, at its speed. A crewmate who runs off the lip of a dip on open ground, or down a bank, isn't knocked
/// for their own run; one who leaves a moving car is knocked exactly as the table says; and a fall hurts by its height, an
/// 8 m drop (the bridge's deck to the river bank) killing.
/// </summary>
public class LandingTests
{
    static readonly PlayerTuning P = Tuning.Player;

    /// <summary>Beside the track, 6 m out from car 1's front, <paramref name="up"/> over the ground there.</summary>
    static (Night N, PlayerState S) Aloft(double speed, double up, Double3 velocity, bool offTheGround)
    {
        var n = new Night(4, speed);
        double along = n.Train.Cars[1].FrontDistance;
        var t = n.Train.Line.Sample(RailLine.MainPath, along);
        var at = t.Position + Double3.Cross(t.Tangent, Double3.Up).Normalized * 6;
        double hint = along;
        double ground = PlayerMotor.GroundAt(at, n.Train.Line, ref hint);
        var s = new PlayerState
        {
            Parent = PlayerState.World,
            Surface = Surface.Air,
            Position = at with { Y = ground + up },
            Velocity = velocity,
            Health = P.Health,
            LineHint = along,
            Flags = offTheGround ? PlayerFlags.OffTheGround : PlayerFlags.None,
        };
        return (n, s);
    }

    static PlayerState Fall(Night n, PlayerState s)
    {
        for (int i = 0; i < 4 * SimConstants.TickRate && s.Surface == Surface.Air; i++)
            PlayerMotor.Step(ref s, default, n.Train, P, n.Train.Dynamics.Tuning, SimConstants.TickSeconds);
        return s;
    }

    [Fact]
    public void ARunOffTheLipOfADipOnOpenGroundIsntAKnock()
    {
        // At full run, 0.5 m down: spec B.3's knock would be 10 or more for a run's speed off the train; off the ground, none.
        var (n, s) = Aloft(0, 0.5, new Double3(P.Run, 0, 0), offTheGround: true);
        s = Fall(n, s);
        Assert.Equal(Surface.Ground, s.Surface);
        Assert.Equal(P.Health, s.Health);
        Assert.False(s.Has(PlayerFlags.OffTheGround));
    }

    [Fact]
    public void FasterThanARunOffTheGroundTheRestIsTheKnock()
    {
        // Thrown or sliding faster than anyone runs, the part over a run still lands as B.3's table says.
        double over = P.Landing.RollAbove + 4;
        var (n, s) = Aloft(0, 0.5, new Double3(P.Run + over, 0, 0), offTheGround: true);
        s = Fall(n, s);
        var (n2, off) = Aloft(0, 0.5, new Double3(over, 0, 0), offTheGround: false);
        off = Fall(n2, off);
        Assert.True(s.Health < P.Health);
        Assert.Equal(off.Health, s.Health);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(14)]
    public void OffAMovingCarItsTheTableAsEver(double speed)
    {
        // Spec B.3: "a knock on landing: 10 just above 1.5 m/s, rising to 80 at the lethal edge", at the train's speed.
        var (n, s) = Aloft(speed, 0.5, new Double3(0, 0, -speed), offTheGround: false);
        s = Fall(n, s);
        var l = P.Landing;
        double k = Math.Clamp((speed - l.RollAbove) / (l.LethalAbove - l.RollAbove), 0, 1);
        int knock = (int)Math.Round(l.RollDamage + (Math.Max(l.DamageAtLethal, l.RollDamage) - l.RollDamage) * k);
        Assert.Equal(P.Health - knock, s.Health);
    }

    [Fact]
    public void AJumpOnOpenGroundOrAStoodTrainIsOffTheGroundAndOffAMovingCarIsnt()
    {
        var n = new Night(4, 0);
        double along = n.Train.Cars[1].FrontDistance;
        var t = n.Train.Line.Sample(RailLine.MainPath, along);
        var s = PlayerMotor.SpawnOnGround(t.Position + Double3.Cross(t.Tangent, Double3.Up).Normalized * 6, n.Train.Line, along, P);
        PlayerMotor.Step(ref s, new PlayerIntent { Buttons = PlayerButtons.Jump }, n.Train, P, n.Train.Dynamics.Tuning, SimConstants.TickSeconds);
        Assert.Equal(Surface.Air, s.Surface);
        Assert.True(s.Has(PlayerFlags.OffTheGround));
        // Off a moving car it's the train's speed (spec B.3's table); off one standing still, its footing is the ground's (note
        // 717, the director's 14 Oct playtest, line 7).
        var moving = new Night(4, 5);
        var roof = PlayerMotor.SpawnOnRoof(moving.Train, 1, 0, P);
        PlayerMotor.Step(ref roof, new PlayerIntent { Buttons = PlayerButtons.Jump }, moving.Train, P, moving.Train.Dynamics.Tuning, SimConstants.TickSeconds);
        Assert.Equal(Surface.Air, roof.Surface);
        Assert.False(roof.Has(PlayerFlags.OffTheGround));
        var stood = PlayerMotor.SpawnOnRoof(n.Train, 1, 0, P);
        PlayerMotor.Step(ref stood, new PlayerIntent { Buttons = PlayerButtons.Jump }, n.Train, P, n.Train.Dynamics.Tuning, SimConstants.TickSeconds);
        Assert.Equal(Surface.Air, stood.Surface);
        Assert.True(stood.Has(PlayerFlags.OffTheGround));
    }

    [Fact]
    public void WalkingOffAStandingCarsRoofIsNoKnock()
    {
        // Note 717 (the director's 14 Oct playtest, line 7: "Whenever I step off the train, I get like a little vignette red
        // flash"): off a train standing still, a crewmate's own walk or run is no knock, and a roof's 4 m is under the fall knock.
        var n = new Night(4, 0);
        var s = PlayerMotor.SpawnOnRoof(n.Train, 1, 0, P) with { Yaw = -Math.PI / 2 };
        for (int i = 0; i < 4 * SimConstants.TickRate && (s.Parent != PlayerState.World || s.Surface == Surface.Air); i++)
            PlayerMotor.Step(ref s, new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Run }, n.Train, P, n.Train.Dynamics.Tuning, SimConstants.TickSeconds);
        Assert.Equal((PlayerState.World, Surface.Ground), (s.Parent, s.Surface));
        Assert.Equal(P.Health, s.Health);
        // Off a moving one it's the table as ever (OffAMovingCarItsTheTableAsEver): the knock is the train's speed.
        var m = new Night(4, 6);
        var r = PlayerMotor.SpawnOnRoof(m.Train, 1, 0, P) with { Yaw = -Math.PI / 2 };
        for (int i = 0; i < 6 * SimConstants.TickRate && (r.Parent != PlayerState.World || r.Surface == Surface.Air); i++)
        {
            m.Train.Step(SimConstants.TickSeconds, new TrainControls { Reverser = 1 });
            m.Train.Dynamics.Velocity = 6;
            PlayerMotor.Step(ref r, new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Run }, m.Train, P, m.Train.Dynamics.Tuning, SimConstants.TickSeconds);
        }
        Assert.True(r.Health < P.Health, $"{r.Health}");
    }

    [Fact]
    public void EightMetresDownKills()
    {
        // The director, off Stroud Bridge's deck (frontier:7's truss, 8 m over the river), lived: by the old knock a straight drop
        // was nothing at all.
        var (n, s) = Aloft(0, 8.05, default, offTheGround: false);
        s = Fall(n, s);
        Assert.False(s.Alive);
        Assert.Equal(DeathCause.Fell, s.Death);
    }

    [Fact]
    public void SevenMetresDownAllButKills()
    {
        var (n, s) = Aloft(0, 7, default, offTheGround: false);
        s = Fall(n, s);
        Assert.True(s.Alive);
        Assert.True(s.Health <= 40, $"{s.Health}");
    }

    [Fact]
    public void AStepOffALandingIsNothing()
    {
        var (n, s) = Aloft(0, 1.5, new Double3(1, 0, 0), offTheGround: false);
        s = Fall(n, s);
        Assert.Equal(P.Health, s.Health);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(10)]
    public void ALeapOffAMovingTrainsRoofIsTheTableAsEver(double speed)
    {
        // A car's roof over the ballast and a jump's apex over that: under the fall knock, so it's B.3's speed table alone.
        var (n, s) = Aloft(speed, 4.0 + P.JumpHeight, new Double3(0, 0, -speed), offTheGround: false);
        s = Fall(n, s);
        var l = P.Landing;
        double k = Math.Clamp((speed - l.RollAbove) / (l.LethalAbove - l.RollAbove), 0, 1);
        int knock = (int)Math.Round(l.RollDamage + (Math.Max(l.DamageAtLethal, l.RollDamage) - l.RollDamage) * k);
        Assert.Equal(P.Health - knock, s.Health);
    }

    [Fact]
    public void NobodyGetsDownOffTheTrainWhereTheresNoGroundToLandOn()
    {
        // deepTerritory:1 seed 2: a walker and the gunner stepped down off the train at a stand where the ground beside it was
        // 26 m under the rail, and the fall (now) killed them. Getting down looks for ground on its side, then the other.
        // (frontier:5's trestle, 34 m up, on note 554's 7 km nights; named from the night's dice, Cotter now the halts take
        // their names first, note 868.)
        var route = LineGen.Routes.Generate(DataFile.FindContentRoot(), "frontier:5", 8);
        var line = route.Build();
        var bridge = route.Plan!.Structures.Single(st => st.Name == "Cotter Trestle");
        var n = new Night(4, 0, line: line);
        var mid = line.Sample(RailLine.MainPath, (bridge.S0 + bridge.S1) / 2).Position;
        var open = line.Sample(RailLine.MainPath, bridge.S0 - 300).Position;
        Assert.False(Bots.StopHand.GroundBeside(n.Train, mid, 1));
        Assert.False(Bots.StopHand.GroundBeside(n.Train, mid, -1));
        Assert.True(Bots.StopHand.GroundBeside(n.Train, open, 1) || Bots.StopHand.GroundBeside(n.Train, open, -1));
    }
}
