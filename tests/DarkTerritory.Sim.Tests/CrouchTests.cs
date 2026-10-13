using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 664 (the director's playtest, 13 Oct: "we should have a crouch function"; D1's calls for it): held down on its key,
/// lower, at half a walk, never a run or a jump; let go, up again; a headset rider's head down low is a crouch too. The
/// tunnel's mouth is <see cref="LinesideTests.CrouchedOnTheKeyARoofRiderGoesUnderTheMouthAndStaysDownTillItsPast"/>.
/// </summary>
public class CrouchTests
{
    static readonly PlayerTuning P = Tuning.Player;
    static readonly TrainTuning T = Tuning.Train;
    const double Dt = SimConstants.TickSeconds;

    static World World()
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        var w = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 1)), line, 5_000), Tuning.Combat);
        w.Hand = P.Hand;
        w.Movement = P;
        return w;
    }

    /// <summary>How far they go along the roof in <paramref name="seconds"/> s with <paramref name="intent"/>, and how they end.</summary>
    static (double Moved, PlayerState End) Walk(World w, PlayerIntent intent, double seconds)
    {
        var s = PlayerMotor.SpawnOnRoof(w.Train, 2, 4, P);
        double z0 = s.Position.Z;
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            w.BeginTick();
            w.CrewAct(ref s, intent, 1);
            w.Step(new TrainControls { Brake = 1, Reverser = 1 });
            PlayerMotor.Step(ref s, intent, w.Train, P, T, Dt, applyLook: false);
        }
        return (z0 - s.Position.Z, s);
    }

    [Fact]
    public void HeldDownItsHalfAWalkAndNeverARun()
    {
        var w = World();
        var (walked, _) = Walk(w, new PlayerIntent { MoveZ = 1 }, 1);
        var (crept, down) = Walk(w, new PlayerIntent { MoveZ = 1, Moves = PlayerMoves.Crouch }, 1);
        var (ran, _) = Walk(w, new PlayerIntent { MoveZ = 1, Moves = PlayerMoves.Crouch, Buttons = PlayerButtons.Run }, 1);
        Assert.True(down.Has(PlayerFlags.Crouched));
        Assert.InRange(crept, walked * P.Crouch.SpeedScale - 0.05, walked * P.Crouch.SpeedScale + 0.05);
        Assert.InRange(ran, crept - 0.05, crept + 0.05);
    }

    [Fact]
    public void LetGoTheyStandAndDownTheyDontJump()
    {
        var w = World();
        var (_, down) = Walk(w, new PlayerIntent { Moves = PlayerMoves.Crouch, Buttons = PlayerButtons.Jump }, 0.5);
        Assert.True(down.Has(PlayerFlags.Crouched));
        Assert.Equal(Surface.Roof, down.Surface);
        var s = down;
        w.CrewAct(ref s, default, 1);
        Assert.False(s.Has(PlayerFlags.Crouched));
    }

    [Fact]
    public void AHeadsetRidersHeadDownLowIsACrouch()
    {
        // No key: the head's height comes with a reported hand (T82).
        var w = World();
        var s = PlayerMotor.SpawnOnRoof(w.Train, 2, 4, P);
        var low = new PlayerIntent { Buttons = PlayerButtons.Hand, HandX = 0.3f, HandY = 0.6f, HandZ = -0.3f, Head = (float)(P.Crouch.HeadsetBelow - 0.1) };
        w.CrewAct(ref s, low, 1);
        Assert.True(s.Has(PlayerFlags.Crouched));
        w.CrewAct(ref s, low with { Head = 1.6f }, 1);
        Assert.False(s.Has(PlayerFlags.Crouched));
    }

    [Fact]
    public void CrouchAndPushRideTheIntentsThirdByte()
    {
        foreach (var m in new[] { PlayerMoves.None, PlayerMoves.Crouch, PlayerMoves.Push, PlayerMoves.Crouch | PlayerMoves.Push })
        {
            var w = new NetWriter();
            Messages.WriteInput(w, [new InputFrame(7, new PlayerIntent { MoveZ = 1, Moves = m, Voice = 9 })], 0);
            var r = new NetReader(w.Written);
            r.U8();
            var frames = new List<InputFrame>();
            Messages.ReadInput(ref r, frames, out _);
            Assert.Equal(m, frames[0].Intent.Moves);
            Assert.Equal(9, frames[0].Intent.Voice);
        }
    }
}
