using System.Numerics;
using Ballast;
using Ballast.Xr;
using DarkTerritory.Game;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>Hand interactions (T29): the reaching hand into intent, climbing by hand, and the cab's levers.</summary>
public class VrHandsTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly VrTuning Vr = DataFile.Load<VrTuning>(Path.Combine(Content, VrTuning.File));
    static readonly PlayerTuning Player = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));
    static readonly TrainTuning Train = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));

    static Quaternion Yawed(double degrees) => Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)(degrees * Math.PI / 180));

    static XrControllerState Hands(Vector3 right, bool grip = false, Vector3? left = null, bool leftGrip = false) => new()
    {
        Grip = grip || leftGrip,
        Right = new XrHand(true, right, Quaternion.Identity, grip),
        Left = new XrHand(left is not null, left ?? default, Quaternion.Identity, leftGrip),
    };

    [Fact]
    public void AHandAheadOfTheHeadIsAheadOfThePlayer()
    {
        var v = new VrLocomotion(Vr);
        v.Frame(default, Quaternion.Identity, 0.01);
        var intent = v.Intent(default, Hands(new Vector3(0.2f, -0.3f, -0.5f)));
        Assert.True(intent.Has(PlayerButtons.Hand));
        Assert.Equal(0.2f, intent.HandX, 5);
        Assert.Equal((float)(Eyes.Height - 0.3), intent.HandY, 5);
        Assert.Equal(-0.5f, intent.HandZ, 5);

        // Turned in the room: the head faces the room's left, and a hand out that way is still ahead of the player.
        var turned = new VrLocomotion(Vr);
        turned.Frame(default, Yawed(90), 0.01);
        var ahead = turned.Intent(default, Hands(new Vector3(-0.5f, 0, 0)));
        Assert.Equal(0, ahead.HandX, 4);
        Assert.Equal(-0.5f, ahead.HandZ, 4);
        // And the sim agrees: after the look, the hand is half a metre out along the way the player faces.
        var s = new PlayerState { Health = 100 };
        PlayerMotor.Look(ref s, ahead);
        PlayerMotor.TakeHand(ref s, ahead, Player.Hand);
        var at = PlayerMotor.HandAt(s)!.Value;
        Assert.Equal(-0.5 * Math.Sin(s.Yaw), at.X, 2);
        Assert.Equal(-0.5 * Math.Cos(s.Yaw), at.Z, 2);
    }

    [Fact]
    public void TheLastHandToGripIsTheOneReaching()
    {
        var v = new VrLocomotion(Vr);
        var right = new Vector3(0.3f, 0, -0.3f);
        var left = new Vector3(-0.3f, 0, -0.3f);
        Assert.True(v.Intent(default, Hands(right, left: left)).HandX > 0);
        v.Intent(default, Hands(right, left: left, leftGrip: true));
        Assert.True(v.LeftReaches);
        // Letting go keeps the left reaching until the right takes hold.
        Assert.True(v.Intent(default, Hands(right, left: left)).HandX < 0);
        v.Intent(default, Hands(right, grip: true, left: left));
        Assert.False(v.LeftReaches);
    }

    [Fact]
    public void PullingDownOnALadderClimbsIt()
    {
        var v = new VrLocomotion(Vr);
        var onLadder = new PlayerState { Surface = Surface.Ladder, Health = 100 };
        double tick = SimConstants.TickSeconds;
        float y = 0;
        v.Intent(onLadder, Hands(new Vector3(0, y, -0.3f), grip: true), Player.LadderClimb);
        // Half the climbing speed's worth of pull a tick climbs at half speed.
        y -= (float)(Player.LadderClimb * tick / 2);
        var up = v.Intent(onLadder, Hands(new Vector3(0, y, -0.3f), grip: true), Player.LadderClimb);
        Assert.Equal(0.5f, up.MoveZ, 3);
        // Pushing the hand up only climbs down slowly: never as far back as letting go (Use with the stick back).
        y += 1;
        var down = v.Intent(onLadder, Hands(new Vector3(0, y, -0.3f), grip: true), Player.LadderClimb);
        Assert.True(down.MoveZ is < 0 and > -0.5f, $"{down.MoveZ}");
        // Not gripping, the hand's just moving about.
        y -= 1;
        Assert.Equal(0, v.Intent(onLadder, Hands(new Vector3(0, y, -0.3f)), Player.LadderClimb).MoveZ);
    }

    static (TrainOnLine Train, PlayerState Driver, CabLevers Levers) Cab()
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Train, 4, 1)), line, 1_000);
        var levers = train.Frames[0].Shape.Levers!.Value;
        var driver = PlayerMotor.SpawnInCab(train, Player, localX: levers.Brake.X - 0.3);
        driver.Position = driver.Position with { Z = levers.Brake.Z + 0.3 };
        return (train, driver, levers);
    }

    /// <summary>A headset's intent with the hand at a point in the engine's frame.</summary>
    static PlayerIntent Gripping(in PlayerState s, Double3 hand, bool grip = true)
    {
        var intent = new PlayerIntent { Buttons = grip ? PlayerButtons.Use : PlayerButtons.None };
        var d = hand - s.Position;
        double c = Math.Cos(-s.Yaw), n = Math.Sin(-s.Yaw);
        intent.Reach(new Double3(d.X * c + d.Z * n, d.Y, -d.X * n + d.Z * c));
        return intent;
    }

    [Fact]
    public void TheRegulatorOpensAsFarAsTheHandPullsIt()
    {
        var (train, driver, levers) = Cab();
        var hands = new VrLevers();
        var controls = new TrainControls { Reverser = 1 };
        var intent = Gripping(driver, levers.RegulatorAt(0));
        hands.Apply(ref intent, driver, train, controls, Player.Hand);
        Assert.Equal(CabLever.Regulator, hands.Held);
        // A hand on a lever isn't also working the firebox.
        Assert.False(intent.Has(PlayerButtons.Use));
        Assert.Equal(0, intent.ThrottleNotch);

        // Half its travel back: half open, in the quadrant's notches, through the same cab path a keyboard uses.
        intent = Gripping(driver, levers.RegulatorAt(0.5));
        hands.Apply(ref intent, driver, train, controls, Player.Hand);
        Assert.Equal(2, intent.ThrottleNotch);
        Sim.Net.CabControls.Apply(ref controls, intent, driver, train);
        Assert.Equal(0.5, controls.Throttle);
        intent = Gripping(driver, levers.RegulatorAt(0.5));
        hands.Apply(ref intent, driver, train, controls, Player.Hand);
        Assert.Equal(0, intent.ThrottleNotch);
        // Let go and it stays where it was left.
        intent = Gripping(driver, levers.RegulatorAt(0), grip: false);
        hands.Apply(ref intent, driver, train, controls, Player.Hand);
        Assert.Equal(CabLever.None, hands.Held);
        Assert.Equal(0, intent.ThrottleNotch);
    }

    [Fact]
    public void TheBrakeIsOnWhileItsHandleIsPulled()
    {
        var (train, driver, levers) = Cab();
        var hands = new VrLevers();
        var controls = new TrainControls { Reverser = 1 };
        var intent = Gripping(driver, levers.BrakeAt(0));
        hands.Apply(ref intent, driver, train, controls, Player.Hand);
        Assert.Equal(CabLever.Brake, hands.Held);
        Assert.False(intent.Has(PlayerButtons.Brake));
        intent = Gripping(driver, levers.BrakeAt(1));
        hands.Apply(ref intent, driver, train, controls, Player.Hand);
        Assert.True(intent.Has(PlayerButtons.Brake));
    }

    [Fact]
    public void TheReverserFlipsOncePerThrow()
    {
        var (train, driver, levers) = Cab();
        var hands = new VrLevers();
        var controls = new TrainControls { Reverser = 1 };
        var intent = Gripping(driver, levers.ReverserAt(1));
        hands.Apply(ref intent, driver, train, controls, Player.Hand);
        Assert.Equal(CabLever.Reverser, hands.Held);
        Assert.False(intent.Has(PlayerButtons.Reverser));
        // Thrown back into reverse: one press, not one a tick (the cab flips on every press).
        int presses = 0;
        for (int i = 0; i < 10; i++)
        {
            intent = Gripping(driver, levers.ReverserAt(-1));
            hands.Apply(ref intent, driver, train, controls, Player.Hand);
            if (intent.Has(PlayerButtons.Reverser))
                presses++;
        }
        Assert.Equal(1, presses);
        Sim.Net.CabControls.Apply(ref controls, new PlayerIntent { Buttons = PlayerButtons.Reverser }, driver, train);
        Assert.Equal(-1, controls.Reverser);
    }

    [Fact]
    public void NoLeversOffTheEngine()
    {
        var (train, _, levers) = Cab();
        var roof = PlayerMotor.SpawnOnRoof(train, 2, 0, Player);
        var hands = new VrLevers();
        var intent = Gripping(roof, roof.Position + new Double3(0, 1, -0.3));
        hands.Apply(ref intent, roof, train, new TrainControls { Reverser = 1 }, Player.Hand);
        Assert.Equal(CabLever.None, hands.Held);
        Assert.True(intent.Has(PlayerButtons.Use));
        Assert.NotEqual(default, levers.Regulator);
    }
}
