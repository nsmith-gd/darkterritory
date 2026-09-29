using Ballast;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

public enum CabLever : byte { None, Regulator, Brake, Reverser }

/// <summary>
/// The cab's levers by hand (T29, roadmap M4): a headset player grips a handle and moves it, and this turns that into
/// the intent a keyboard sends (throttle notches, the brake held, a reverser flip), so the host can't tell and nothing
/// in the sim knows about VR. A hand on a lever isn't working anything else, so it takes the grip's Use away.
/// </summary>
public sealed class VrLevers
{
    bool _thrown;

    /// <summary>The lever the hand is on, if any.</summary>
    public CabLever Held { get; private set; }

    /// <summary>Works whatever lever the reaching hand is on, from <paramref name="self"/> as of before this intent.</summary>
    /// <param name="controls">The cab's controls as this machine has them (the handles are where these put them).</param>
    public void Apply(ref PlayerIntent intent, in PlayerState self, TrainOnLine train, in TrainControls controls, HandTuning hand)
    {
        // Where the hand will be once the host has this intent: turned by its look, held to an arm's length.
        var s = self;
        PlayerMotor.Look(ref s, intent);
        PlayerMotor.TakeHand(ref s, intent, hand);
        if (!intent.Has(PlayerButtons.Use) || PlayerMotor.HandAt(s) is not { } at || s.Parent != 0 || !CabControls.CanDrive(s, train)
            || train.Frames[0].Shape.Levers is not { } levers)
        {
            Held = CabLever.None;
            _thrown = false;
            return;
        }
        if (Held == CabLever.None)
            Held = Nearest(levers, controls, at, hand.Grab);
        if (Held == CabLever.None)
            return;
        intent.Buttons &= ~PlayerButtons.Use;
        switch (Held)
        {
            case CabLever.Regulator:
                // The handle goes where the hand takes it, in the quadrant's quarter notches.
                double wanted = Math.Clamp((at.Z - levers.Regulator.Z) / CabLevers.RegulatorTravel, 0, 1);
                intent.ThrottleNotch = (sbyte)Math.Clamp(Math.Round(wanted * 4) - Math.Round(controls.Throttle * 4), -4, 4);
                break;
            case CabLever.Brake:
                if (at.Z - levers.Brake.Z >= CabLevers.BrakeTravel / 2)
                    intent.Buttons |= PlayerButtons.Brake;
                break;
            case CabLever.Reverser:
                // A flip is a press: once per throw, and only while it isn't where the hand has it.
                double d = at.Z - levers.Reverser.Z;
                int want = d <= -CabLevers.ReverserThrow / 2 ? 1 : d >= CabLevers.ReverserThrow / 2 ? -1 : 0;
                if (want == 0 || want == Math.Sign(controls.Reverser))
                    _thrown = false;
                else if (!_thrown)
                {
                    intent.Buttons |= PlayerButtons.Reverser;
                    _thrown = true;
                }
                break;
        }
    }

    static CabLever Nearest(in CabLevers levers, in TrainControls controls, Double3 hand, double grab)
    {
        var best = CabLever.None;
        double bestD = grab;
        void Try(CabLever lever, Double3 at)
        {
            double d = (at - hand).Length;
            if (d <= bestD)
            {
                bestD = d;
                best = lever;
            }
        }
        Try(CabLever.Regulator, levers.RegulatorAt(controls.Throttle));
        Try(CabLever.Brake, levers.BrakeAt(controls.Brake));
        Try(CabLever.Reverser, levers.ReverserAt(controls.Reverser));
        return best;
    }
}
