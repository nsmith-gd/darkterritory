using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

public class PlayerMotorTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    const double Dt = SimConstants.TickSeconds;

    sealed class Rig
    {
        public required TrainOnLine Train;
        public PlayerState Player;
        public double Speed;

        /// <summary>Runs at constant train speed, feeding intent from <paramref name="drive"/> each tick.</summary>
        public void Run(double seconds, Func<Rig, PlayerIntent> drive)
        {
            for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            {
                Train.Dynamics.Velocity = Speed;
                Train.Step(Dt, default);
                var intent = drive(this);
                PlayerMotor.Step(ref Player, intent, Train, P, T, Dt);
            }
        }

        public void Run(double seconds, PlayerIntent intent) => Run(seconds, _ => intent);

        public Double3 Local(int car) => Train.Frames[car].ToLocal(PlayerMotor.WorldPosition(Player, Train));
        public double CarLength(int car) => Train.Frames[car].Shape.Body.Max.Z * 2;
    }

    static Rig OnRoof(int cars, double speed, int car, double localZ = 0, params TrackSegment[] track)
    {
        var line = new RailLine(new LineDefinition("t", track.Length > 0 ? track : [new TrackSegment(20_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, cars, 1)), line, 1_000);
        return new Rig { Train = train, Speed = speed, Player = PlayerMotor.SpawnOnRoof(train, car, localZ, P) };
    }

    static PlayerIntent Move(float x, float z, PlayerButtons b = PlayerButtons.None) => new() { MoveX = x, MoveZ = z, Buttons = b };

    [Fact]
    public void StandingOnTheRoofAtMaxSpeedGoesNowhereRelativeToTheCar()
    {
        var rig = OnRoof(20, T.MaxSpeed, car: 3);
        var start = PlayerMotor.WorldPosition(rig.Player, rig.Train);
        rig.Run(10, default(PlayerIntent));

        Assert.Equal(Surface.Roof, rig.Player.Surface);
        Assert.Equal(3, rig.Player.Parent);
        Assert.InRange(rig.Player.Position.Z, -1e-9, 1e-9);
        Assert.InRange((PlayerMotor.WorldPosition(rig.Player, rig.Train) - start).Length, 219, 221);
    }

    [Fact]
    public void StandingOnTheRoofThroughATightCurve()
    {
        var rig = OnRoof(10, T.MaxSpeed, car: 5, 0, new TrackSegment(1_100), new TrackSegment(2_000, Radius: 250));
        rig.Run(30, default(PlayerIntent));
        Assert.Equal(Surface.Roof, rig.Player.Surface);
        Assert.Equal(5, rig.Player.Parent);
        Assert.True(Math.Abs(rig.Train.Frames[5].Heading) > 1, "train should have turned a long way");
    }

    [Fact]
    public void RoofRunCoversSpecSpeed()
    {
        var rig = OnRoof(20, 14, car: 3, localZ: 5);
        rig.Run(2, Move(0, 1, PlayerButtons.Run));
        Assert.InRange(rig.Player.Position.Z, 5 - P.RoofRun * 2 - 0.05, 5 - P.RoofRun * 2 + 0.05);
    }

    [Fact]
    public void WalkingOffTheSideAtWorkingSpeedKills()
    {
        var rig = OnRoof(6, 12, car: 2);
        rig.Run(4, Move(1, 0));
        Assert.Equal(DeathCause.JumpedAtSpeed, rig.Player.Death);
        Assert.Equal(Surface.Ground, rig.Player.Surface);
    }

    [Fact]
    public void SteppingOffAtYardSpeedIsARoll()
    {
        var rig = OnRoof(6, 2, car: 2);
        rig.Run(4, Move(1, 0));
        Assert.True(rig.Player.Alive);
        Assert.Equal(Surface.Ground, rig.Player.Surface);
        Assert.Equal(P.Health - P.Landing.RollDamage, rig.Player.Health);
    }

    [Fact]
    public void SprintingOffAtYardSpeedIsStillLethal()
    {
        // Roof run sideways plus the train's own speed: your speed over the ground is what counts.
        var rig = OnRoof(6, 2.5, car: 2);
        rig.Run(4, Move(1, 0, PlayerButtons.Run));
        Assert.Equal(DeathCause.JumpedAtSpeed, rig.Player.Death);
    }

    [Fact]
    public void JumpingTheCouplingGapLandsOnTheNextRoof()
    {
        var rig = OnRoof(6, 14, car: 3, localZ: -3);
        double edge = -OnRoof(6, 14, 3).CarLength(3) / 2;
        rig.Run(3, r => r.Player.Parent == 3 && r.Player.Grounded && r.Player.Position.Z < edge + 0.4
            ? Move(0, 1, PlayerButtons.Run | PlayerButtons.Jump)
            : r.Player.Parent == 3 ? Move(0, 1, PlayerButtons.Run) : default);
        Assert.True(rig.Player.Alive);
        Assert.Equal(2, rig.Player.Parent);
        Assert.Equal(Surface.Roof, rig.Player.Surface);
    }

    [Fact]
    public void WalkingOffTheEndDropsOntoTheCouplerPlate()
    {
        var rig = OnRoof(6, 14, car: 3, localZ: -4);
        rig.Run(3, Move(0, 1));
        Assert.True(rig.Player.Alive);
        Assert.Equal(Surface.Coupler, rig.Player.Surface);
        Assert.Equal(2, rig.Player.Parent); // the plate belongs to the car in front of the gap
    }

    static Rig BesideLadder(double speed, double behind)
    {
        var rig = OnRoof(3, speed, car: 3);
        var frame = rig.Train.Frames[3];
        var ladder = frame.ToWorld(frame.Shape.Ladders[0]);
        var start = ladder + frame.Back * behind + frame.Right * 0.3;
        rig.Player = PlayerMotor.SpawnOnGround(start, rig.Train.Line, rig.Train.Cars[3].FrontDistance, P);
        return rig;
    }

    [Fact]
    public void AYardSpeedTrainCanBeRunDownAndBoarded()
    {
        var rig = BesideLadder(T.SpeedBands.Yard, behind: 8);
        rig.Run(15, Move(0, 1, PlayerButtons.Run | PlayerButtons.Use));
        Assert.Equal(Surface.Roof, rig.Player.Surface);
        Assert.Equal(3, rig.Player.Parent);
    }

    [Fact]
    public void AWorkingSpeedTrainCannotBeCaught()
    {
        var rig = BesideLadder(10, behind: 8);
        rig.Run(15, Move(0, 1, PlayerButtons.Run | PlayerButtons.Use));
        Assert.Equal(Surface.Ground, rig.Player.Surface);
    }

    [Fact]
    public void APassingLadderAtSpeedCannotBeGrabbed()
    {
        var rig = BesideLadder(T.MaxSpeed, behind: -6);
        rig.Run(5, Move(0, 0, PlayerButtons.Use));
        Assert.NotEqual(Surface.Ladder, rig.Player.Surface);
        Assert.True(rig.Player.Alive);
    }

    [Fact]
    public void JumpVelocityIsDerivedFromSpecJumpGap()
    {
        double airtime = 2 * P.JumpVelocity / P.Gravity;
        Assert.Equal(P.JumpGap, airtime * P.RoofRun, 6);
    }
}
