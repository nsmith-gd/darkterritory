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
        // On the doors' line, where the plate runs (off it, by the end ladder, walking on takes hold of the ladder: T90).
        var rig = OnRoof(6, 14, car: 3, localZ: -4);
        rig.Player.Position = rig.Player.Position with { X = T.Geometry.PlateX };
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
    public void FromTheFirstCarsCouplerTheRunningBoardLeadsIntoTheCab()
    {
        // T90 (playtest): there was no way into the cab from the train. Cab forward (note 276), car 1 couples on behind the
        // smokebox: off the plate onto the footplate, out onto the left running board, forward along it beside the boiler,
        // and in at the cab's left doorway, at speed.
        var rig = OnRoof(3, 14, car: 1);
        var engine = rig.Train.Frames[0];
        var plan = EnginePlan.Of(T.Geometry);
        double l = engine.Shape.HalfLength, w = engine.Shape.HalfWidth;
        double board = -w - T.Geometry.Engine.RunningBoardWidth / 2, door = plan.DoorFront + T.Geometry.Doorway.Width * 0.75;
        rig.Player = PlayerMotor.SpawnOnRoof(rig.Train, 0, l + T.Geometry.CouplingGap / 2, P);
        Assert.Equal(Surface.Coupler, rig.Player.Surface);
        rig.Run(1, r => Toward(r, new Double3(-w + 0.3, 0, l + 0.3)));
        rig.Run(1, r => Toward(r, new Double3(board, 0, l - 0.6)));
        rig.Run(8, r => Toward(r, new Double3(board, 0, door)));
        rig.Run(2, r => Toward(r, new Double3(-w + 0.5, 0, door)));
        Assert.True(PlayerMotor.InCab(rig.Player, rig.Train), $"on {rig.Player.Surface} of {rig.Player.Parent} at {rig.Player.Position}");
    }

    [Fact]
    public void AlongTheHoodsRoofYouWalkOntoTheCabRoofAndItsGun()
    {
        // The hood (note 338): the engine's roof is one level from its back to the cab's front, so from the top of the train
        // (up the engine's back ladder) you walk forward onto the cab roof and its gun, no ladder up the cab's back.
        var rig = OnRoof(3, 14, car: 1);
        var engine = rig.Train.Frames[0];
        var plan = EnginePlan.Of(T.Geometry);
        Assert.DoesNotContain(engine.Shape.Ladders, x => x.Foot.Y > T.Geometry.Engine.DeckHeight + 1);
        rig.Player = PlayerMotor.SpawnOnRoof(rig.Train, 0, plan.CabBack + 3, P, 0);
        Assert.Equal(Surface.Roof, rig.Player.Surface);
        Assert.Equal(T.Geometry.EngineHeight, rig.Player.Position.Y, 3);
        rig.Run(3, _ => Move(0, 1));
        Assert.Equal(0, rig.Player.Parent);
        Assert.Equal(Surface.Roof, rig.Player.Surface);
        Assert.Equal(T.Geometry.EngineHeight, rig.Player.Position.Y, 3);
        Assert.True(rig.Player.Position.Z < plan.CabBack - 1, $"onto the cab roof: at {rig.Player.Position}");
    }

    [Fact]
    public void WalkingIntoAnEndLadderClimbsItOntoTheRoof()
    {
        var rig = OnRoof(4, 0, car: 2);
        var car = rig.Train.Frames[2];
        var ladder = car.Shape.Ladders.First(x => x.Foot.Z > car.Shape.HalfLength);
        rig.Player = PlayerMotor.SpawnOnRoof(rig.Train, 2, ladder.Foot.Z + 0.3, P, 0);
        Assert.Equal(Surface.Coupler, rig.Player.Surface);
        rig.Run(5, Move(0, 1));
        Assert.Equal(Surface.Roof, rig.Player.Surface);
        Assert.Equal(2, rig.Player.Parent);
    }

    /// <summary>Car 2's end ladder at its back (inward is the car's −Z).</summary>
    static Ladder EndLadder(Rig rig)
    {
        var car = rig.Train.Frames[2];
        return car.Shape.Ladders.First(x => x.Foot.Z > car.Shape.HalfLength);
    }

    [Fact]
    public void JumpingIntoALadderTakesHoldOfItWhereYouMeetIt()
    {
        // Note 1018 (the director, 24 Oct: "if I jump into a ladder I should automatically grab it"): in the air, moving at
        // it, no key and no push, a metre up its rungs. A side ladder: nothing of the train under you to come down onto.
        var rig = OnRoof(4, 0, car: 2);
        var car = rig.Train.Frames[2];
        var ladder = car.Shape.Ladders.First(x => Math.Abs(x.Inward.X) > 0.5 && x.Foot.Y == 0);
        var from = ladder.Foot - ladder.Inward * (P.Ladder.AirReach + 0.3) + new Double3(0, 1, 0);
        rig.Player.Parent = PlayerState.World;
        rig.Player.Surface = Surface.Air;
        rig.Player.Position = car.ToWorld(from);
        rig.Player.Velocity = car.ToWorld(from + ladder.Inward * 2) - car.ToWorld(from);
        rig.Run(0.4, default(PlayerIntent));
        Assert.Equal(Surface.Ladder, rig.Player.Surface);
        Assert.Equal(2, rig.Player.Parent);
        Assert.InRange(rig.Player.Position.Y, ladder.Foot.Y + 0.4, ladder.Foot.Y + 1);
        // And letting go (a jump) pushes off it, away: it doesn't take you back.
        rig.Run(0.5, Move(0, 0, PlayerButtons.Jump));
        rig.Run(0.5, default(PlayerIntent));
        Assert.NotEqual(Surface.Ladder, rig.Player.Surface);
    }

    [Fact]
    public void WalkingIntoALadderTakesHoldOfItFromAFairReach()
    {
        // Note 1018: from walkInReach (past the 0.45 m it was), on the first step at it.
        Assert.True(P.Ladder.WalkInReach > 0.45);
        var rig = OnRoof(4, 0, car: 2);
        var ladder = EndLadder(rig);
        rig.Player = PlayerMotor.SpawnOnRoof(rig.Train, 2, ladder.Foot.Z + P.Ladder.WalkInReach - 0.03, P, 0);
        rig.Player.Position = rig.Player.Position with { X = ladder.Foot.X };
        Assert.Equal(Surface.Coupler, rig.Player.Surface);
        rig.Run(Dt, Move(0, 1));
        Assert.Equal(Surface.Ladder, rig.Player.Surface);
    }

    [Fact]
    public void WalkingPastALadderAlongsideItDoesNotTakeIt()
    {
        // Across the plate beside the end ladder, facing along the car's end: never on it.
        var rig = OnRoof(4, 0, car: 2);
        var ladder = EndLadder(rig);
        rig.Player = PlayerMotor.SpawnOnRoof(rig.Train, 2, ladder.Foot.Z + 0.25, P, 0);
        rig.Player.Position = rig.Player.Position with { X = ladder.Foot.X + 0.8 * Math.Sign(ladder.Foot.X) };
        rig.Player.Yaw = Math.Sign(ladder.Foot.X) * -Math.PI / 2; // facing −X when the ladder's on the +X side: along the end, past it
        Assert.Equal(Surface.Coupler, rig.Player.Surface);
        bool took = false;
        rig.Run(1.2, r => { took |= r.Player.Surface == Surface.Ladder; return Move(0, 1); });
        Assert.False(took, $"took the ladder walking past it, at {rig.Player.Position}");
        Assert.True(Math.Abs(rig.Player.Position.X - ladder.Foot.X) > 0.3 || Math.Sign(rig.Player.Position.X) != Math.Sign(ladder.Foot.X),
            $"walked past it: at {rig.Player.Position}");
    }

    [Fact]
    public void WalkingOffARoofsEndOntoThePlateDoesNotTakeTheEndLadder()
    {
        // Note 1018: off a roof's end, eyes down at the plate (note 717's way down), you land on the plate: neither that car's
        // end ladder (down to the ballast) nor the next car's takes you, though you're walking out over the one and falling at
        // the other.
        var rig = OnRoof(4, 0, car: 2);
        var ladder = EndLadder(rig);
        rig.Player = PlayerMotor.SpawnOnRoof(rig.Train, 2, ladder.Foot.Z - 2, P, 0);
        rig.Player.Position = rig.Player.Position with { X = T.Geometry.PlateX }; // over the plate, a step in from the end ladder
        rig.Player.Yaw = Math.PI; // facing +Z, out over the car's back end
        rig.Player.Pitch = -1.3;
        bool took = false;
        rig.Run(3, r => { took |= r.Player.Surface == Surface.Ladder; return r.Player.Surface == Surface.Coupler ? default : Move(0, 1); });
        Assert.False(took, $"took a ladder: on {rig.Player.Surface} of {rig.Player.Parent} at {rig.Player.Position}");
        Assert.Equal(Surface.Coupler, rig.Player.Surface);
    }

    [Fact]
    public void RunningAlongTheRoofToJumpTheGapDoesNotTakeTheEndLadders()
    {
        var rig = OnRoof(4, 0, car: 2);
        var ladder = EndLadder(rig);
        rig.Player = PlayerMotor.SpawnOnRoof(rig.Train, 2, ladder.Foot.Z - 3, P, 0);
        rig.Player.Position = rig.Player.Position with { X = ladder.Foot.X };
        rig.Player.Yaw = Math.PI;
        bool took = false;
        rig.Run(2.5, r => { took |= r.Player.Surface == Surface.Ladder; return Move(0, 1, PlayerButtons.Run | (r.Local(2).Z > ladder.Foot.Z - 0.6 ? PlayerButtons.Jump : 0)); });
        Assert.False(took, $"took the end ladder: on {rig.Player.Surface} of {rig.Player.Parent} at {rig.Player.Position}");
    }

    /// <summary>The engine's hatch ladder up from the cab floor through its roof (note 704).</summary>
    static Ladder CabHatch(Rig rig)
    {
        var shape = rig.Train.Frames[0].Shape;
        return shape.Ladders.Single(l => shape.Cab!.Value.ContainsXZ(l.Foot) && PlayerMotor.IsHatchLadder(shape, l));
    }

    /// <summary>On the cab floor a step short of the hatch ladder, facing it, looking <paramref name="pitchDegrees"/> up.</summary>
    static Rig AtTheCabHatchLadder(double pitchDegrees)
    {
        var rig = OnRoof(3, 0, car: 1);
        var hatch = CabHatch(rig);
        rig.Player = PlayerMotor.SpawnInCab(rig.Train, P);
        rig.Player.Position = hatch.Foot - hatch.Inward * 0.35;
        rig.Player.Yaw = Math.PI; // facing the engine's +Z, up the rungs
        rig.Player.Pitch = pitchDegrees * Math.PI / 180;
        return rig;
    }

    [Fact]
    public void WalkingAtTheCabHatchLadderLookingUpClimbsOntoTheCabRoof()
    {
        // Note 704 (the director: "this ladder inside the cab should actually go up to the roof"): eyes on the hatch and
        // walking at the ladder takes it, and it comes out on the cab roof.
        var rig = AtTheCabHatchLadder(P.Ladder.HatchLookUpDegrees + 10);
        var hatch = CabHatch(rig);
        Assert.True(PlayerMotor.LadderInReachOf(rig.Player, rig.Train, P) is { FromTop: false }, "the hatch ladder in reach from its foot");
        rig.Run(0.2, Move(0, 1));
        Assert.Equal(Surface.Ladder, rig.Player.Surface);
        rig.Run(4, Move(0, 1));
        Assert.Equal(0, rig.Player.Parent);
        Assert.Equal(Surface.Roof, rig.Player.Surface);
        Assert.Equal(hatch.Top, rig.Player.Position.Y, 1);
    }

    [Fact]
    public void WalkingPastTheCabHatchLadderEyesLevelDoesNotTakeIt()
    {
        // In the cab you walk past it all night, working (the reason a hatch ladder indoors wasn't walked onto at all).
        var rig = AtTheCabHatchLadder(0);
        rig.Run(1, Move(0, 1));
        Assert.NotEqual(Surface.Ladder, rig.Player.Surface);
        Assert.True(PlayerMotor.InCab(rig.Player, rig.Train), $"on {rig.Player.Surface} of {rig.Player.Parent} at {rig.Player.Position}");
    }

    [Fact]
    public void TheLadderKeyAtTheCabHatchTakesYouDownIntoTheCab()
    {
        // And back the other way, quickly: on the cab roof at the open hatch, the ladder key and down.
        var rig = AtTheCabHatchLadder(P.Ladder.HatchLookUpDegrees + 10);
        rig.Run(5, Move(0, 1));
        Assert.Equal(Surface.Roof, rig.Player.Surface);
        var hatch = CabHatch(rig);
        rig.Player.Position = new Double3(hatch.Foot.X, rig.Player.Position.Y, hatch.Foot.Z - 0.3);
        Assert.True(PlayerMotor.LadderInReachOf(rig.Player, rig.Train, P) is { FromTop: true }, "the hatch in reach from the roof");
        rig.Run(0.1, new PlayerIntent { Actions = PlayerActions.Ladder });
        Assert.Equal(Surface.Ladder, rig.Player.Surface);
        rig.Run(4, Move(0, -1));
        Assert.True(PlayerMotor.InCab(rig.Player, rig.Train), $"on {rig.Player.Surface} of {rig.Player.Parent} at {rig.Player.Position}");
    }

    [Fact]
    public void WalkingAlongTheRoofPastALaddersTopDoesNotGrabIt()
    {
        var rig = OnRoof(4, 0, car: 2, localZ: 3);
        // Past the hatch's top, short of the roof's end (off its end, falling into car 3's ladder takes it: note 1018).
        rig.Run(2, Move(0, -1));
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
