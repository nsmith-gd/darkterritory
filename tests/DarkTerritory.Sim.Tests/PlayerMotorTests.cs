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
        public double CarLength(int car) => Train.Frames[car].Shape.HalfLength * 2;
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
    public void WalkingOffTheSideAtMaxSpeedKills()
    {
        var rig = OnRoof(6, T.MaxSpeed, car: 2);
        rig.Run(4, Move(1, 0));
        Assert.Equal(DeathCause.JumpedAtSpeed, rig.Player.Death);
        Assert.Equal(Surface.Ground, rig.Player.Surface);
    }

    [Fact]
    public void WalkingOffTheSideAtWorkingSpeedHurtsButIsSurvived()
    {
        // T90 (playtest): only three times run speed kills; the working band is a hard landing, and the train goes on.
        var rig = OnRoof(6, 12, car: 2);
        rig.Run(4, Move(1, 0));
        Assert.True(rig.Player.Alive);
        Assert.Equal(Surface.Ground, rig.Player.Surface);
        Assert.InRange(rig.Player.Health, P.Health - P.Landing.DamageAtLethal, P.Health - P.Landing.RollDamage - 1);
    }

    [Fact]
    public void SteppingOffAtYardSpeedIsARoll()
    {
        var rig = OnRoof(6, 2, car: 2);
        rig.Run(4, Move(1, 0));
        Assert.True(rig.Player.Alive);
        Assert.Equal(Surface.Ground, rig.Player.Surface);
        Assert.InRange(rig.Player.Health, P.Health - P.Landing.RollDamage - 10, P.Health - P.Landing.RollDamage);
    }

    [Fact]
    public void TheLethalLandingIsThreeTimesRunSpeed()
    {
        Assert.Equal(3 * P.Run, P.Landing.LethalAbove, 6);
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
        var ladder = frame.ToWorld(frame.Shape.Ladders[0].Foot);
        var start = ladder + frame.Back * behind + frame.Right * 0.3;
        rig.Player = PlayerMotor.SpawnOnGround(start, rig.Train.Line, rig.Train.Cars[3].FrontDistance, P);
        return rig;
    }

    [Fact]
    public void AYardSpeedTrainCanBeRunDownAndBoarded()
    {
        var rig = BesideLadder(T.SpeedBands.Yard, behind: 8);
        // Run it down, grab, climb, and stand still once up: car 3 is the guard van, and pushing on across
        // its roof with Use held grabs the gun hatch.
        rig.Run(15, r => r.Player.Surface == Surface.Roof ? default : Move(0, 1, PlayerButtons.Run | PlayerButtons.Use));
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
    public void TheCabStepsComeUpIntoTheCabNotOntoItsRoof()
    {
        // Standing at the foot of the engine's right-hand steps, facing them: grab, climb, and you're on the cab floor.
        var rig = OnRoof(3, 0, car: 1);
        var engine = rig.Train.Frames[0];
        var steps = engine.Shape.Ladders.First(l => l.Foot.X > 0 && l.Inward.X < 0);
        rig.Player = PlayerMotor.SpawnOnGround(engine.ToWorld(steps.Foot + new Double3(0.35, 0, 0)), rig.Train.Line, rig.Train.Cars[0].FrontDistance, P);
        rig.Player.Yaw = engine.Heading + Math.PI / 2; // facing the engine's left, into the steps
        rig.Run(4, r => r.Player.Surface is Surface.Ground or Surface.Ladder ? Move(0, 1, PlayerButtons.Use) : default);
        Assert.True(PlayerMotor.InCab(rig.Player, rig.Train), $"on {rig.Player.Surface} of {rig.Player.Parent} at {rig.Player.Position}");
    }

    [Fact]
    public void APassingLadderAtSpeedCannotBeGrabbed()
    {
        var rig = BesideLadder(T.MaxSpeed, behind: -6);
        rig.Run(5, Move(0, 0, PlayerButtons.Use));
        Assert.NotEqual(Surface.Ladder, rig.Player.Surface);
        Assert.True(rig.Player.Alive);
    }

    /// <summary>Walks toward a point in the engine's frame (the track is straight: every car's frame faces the same way).</summary>
    static PlayerIntent Toward(Rig r, Double3 engineLocal, PlayerButtons b = PlayerButtons.None)
    {
        var d = engineLocal - r.Local(0);
        double len = Math.Sqrt(d.X * d.X + d.Z * d.Z);
        if (len < 0.05)
            return default;
        double y = r.Player.Yaw;
        double z = (d.X * -Math.Sin(y) + d.Z * -Math.Cos(y)) / len, x = (d.X * Math.Cos(y) + d.Z * -Math.Sin(y)) / len;
        return new PlayerIntent { MoveX = (float)x, MoveZ = (float)z, Buttons = b };
    }

    [Fact]
    public void FromTheFirstCarsCouplerTheGangwayLeadsIntoTheCab()
    {
        // T90 (playtest): there was no way into the cab from the train. Off the plate behind the tender, left onto the
        // gangway down its side, and forward into the cab, at speed.
        var rig = OnRoof(3, 14, car: 1);
        var engine = rig.Train.Frames[0];
        double l = engine.Shape.HalfLength, cabBack = l - T.Geometry.Engine.TenderLength, w = engine.Shape.HalfWidth;
        rig.Player = PlayerMotor.SpawnOnRoof(rig.Train, 0, l + T.Geometry.CouplingGap / 2, P);
        Assert.Equal(Surface.Coupler, rig.Player.Surface);
        double aisle = -w + T.Geometry.Engine.TenderGangway / 2;
        rig.Run(1, r => Toward(r, new Double3(aisle, 0, l + 0.3)));
        rig.Run(1, r => Toward(r, new Double3(aisle, 0, l - 0.4)));
        rig.Run(4, r => Toward(r, new Double3(aisle, 0, cabBack - 1.2)));
        Assert.True(PlayerMotor.InCab(rig.Player, rig.Train), $"on {rig.Player.Surface} of {rig.Player.Parent} at {rig.Player.Position}");
    }

    [Fact]
    public void UpTheTendersFrontLadderToTheCabRoofAndItsGun()
    {
        // Walking into a ladder's foot takes hold (no Use), climbing carries you over its top onto the cab roof.
        var rig = OnRoof(3, 14, car: 1);
        var engine = rig.Train.Frames[0];
        var ladder = engine.Shape.Ladders.Single(x => x.Foot.Y > T.Geometry.Engine.DeckHeight + 1);
        rig.Player = PlayerMotor.SpawnOnRoof(rig.Train, 0, ladder.Foot.Z + 0.6, P, ladder.Foot.X);
        Assert.Equal(Surface.Roof, rig.Player.Surface);
        rig.Run(4, r => r.Player.Surface == Surface.Roof && r.Player.Position.Y > ladder.Foot.Y + 0.5 ? default : Move(0, 1));
        Assert.Equal(0, rig.Player.Parent);
        Assert.Equal(Surface.Roof, rig.Player.Surface);
        Assert.Equal(T.Geometry.EngineHeight, rig.Player.Position.Y, 3);
        Assert.True(rig.Player.Position.Z < ladder.Foot.Z - 0.3, "over the top onto the cab roof");
    }

    [Fact]
    public void WalkingIntoAnEndLadderClimbsItOntoTheRoof()
    {
        var rig = OnRoof(4, 0, car: 2);
        var car = rig.Train.Frames[2];
        var ladder = car.Shape.Ladders.First(x => x.Foot.Z > car.Shape.HalfLength);
        rig.Player = PlayerMotor.SpawnOnRoof(rig.Train, 2, ladder.Foot.Z + 0.3, P, 0.3);
        Assert.Equal(Surface.Coupler, rig.Player.Surface);
        rig.Run(5, Move(0, 1));
        Assert.Equal(Surface.Roof, rig.Player.Surface);
        Assert.Equal(2, rig.Player.Parent);
    }

    [Fact]
    public void WalkingAlongTheRoofPastALaddersTopDoesNotGrabIt()
    {
        var rig = OnRoof(4, 0, car: 2, localZ: 3);
        rig.Run(3, Move(0, -1));
        Assert.NotEqual(Surface.Ladder, rig.Player.Surface);
    }

    [Fact]
    public void AJumpLiftsTheFeetTheJumpHeightAndClearsTheSpecGap()
    {
        Assert.Equal(P.JumpHeight, P.JumpVelocity * P.JumpVelocity / (2 * P.Gravity), 6);
        double airtime = 2 * P.JumpVelocity / P.Gravity;
        Assert.True(airtime * P.RoofRun >= P.JumpGap);
    }
}

public class MovingFrameRegressionTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void JumpingTheGapAtMaxSpeedWorksInBothDirections(int direction)
    {
        // Regression: the take-off tick used to count the car's own motion twice, so rearward
        // jumps at speed fell short into the gap and forward jumps overshot.
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), line, 1_000);
        double half = T.Geometry.CarLength / 2;
        var player = PlayerMotor.SpawnOnRoof(train, 3, -direction * 3, P);
        player.Yaw = direction < 0 ? 0 : Math.PI;

        for (int i = 0; i < SimConstants.TickRate * 5; i++)
        {
            train.Dynamics.Velocity = T.MaxSpeed;
            train.Step(SimConstants.TickSeconds, default);
            bool onStart = player.Parent == 3 && player.Grounded;
            bool atEdge = direction < 0 ? player.Position.Z < -half + 0.4 : player.Position.Z > half - 0.4;
            var intent = onStart
                ? new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Run | (atEdge ? PlayerButtons.Jump : 0) }
                : default;
            PlayerMotor.Step(ref player, intent, train, P, T, SimConstants.TickSeconds);
        }

        Assert.True(player.Alive);
        Assert.Equal(Surface.Roof, player.Surface);
        Assert.Equal(3 + direction, player.Parent);
    }

    [Fact]
    public void TakeOffTickMovesOnlyByTheJumpersOwnVelocityRelativeToTheCar()
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), line, 1_000);
        var player = PlayerMotor.SpawnOnRoof(train, 2, 0, P);
        train.Dynamics.Velocity = T.MaxSpeed;
        train.Step(SimConstants.TickSeconds, default);
        PlayerMotor.Step(ref player, new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Run | PlayerButtons.Jump }, train, P, T, SimConstants.TickSeconds);

        var local = train.Frames[2].ToLocal(PlayerMotor.WorldPosition(player, train));
        Assert.Equal(-P.RoofRun * SimConstants.TickSeconds, local.Z, 6);
    }
}
