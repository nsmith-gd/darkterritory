using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Bots;

/// <summary>
/// The relief driver (note 399): the driver's dead, and the train stands where it fell, or runs on with nobody at the
/// controls. A walker goes forward and takes them. Who goes is a claim on the crew's calls (<see cref="CrewCalls.Relief"/>):
/// the first free walker to hear the driver's gone, and it holds it while it lives. It goes along the roofs to car 1, a
/// running jump onto the engine's hood (note 338: level with the cars' roofs and the cab's), round the stack, and down the
/// cab's roof hatch ladder (the forward gun's, up from the cab floor), and from there it's the driver: a
/// <see cref="ConductorBot"/> in the dead driver's place on the calls (member 0), so the crew hear a driver again.
/// </summary>
public static class ReliefDriver
{
    /// <summary>
    /// The way to the cab from wherever on the train: null once it's in the cab, or where the walker's legs take it (along
    /// the roofs, forward; up out of a car or a gap first), with <paramref name="forward"/> set.
    /// </summary>
    public static PlayerIntent? ToTheCab(in PlayerState self, TrainOnLine train, out bool forward)
    {
        forward = false;
        if (PlayerMotor.InCab(self, train) || train.Frames[0].Shape.Cab is not { } cab)
            return null;
        var shape = train.Frames[0].Shape;
        if (self.Parent == 0)
        {
            if (self.Surface == Surface.Ladder)
                return new PlayerIntent { MoveZ = -1 };
            if (self.Surface != Surface.Roof)
                return null; // on the running boards or a step: the cab's a step away, and the legs can't help
            var plan = EnginePlan.Of(train.Dynamics.Tuning.Geometry);
            // Behind the stack, which stands up through the hood on the centreline: by it on one side first.
            if (self.Position.Z > plan.StackZ - 0.6)
                return WarmUp.Steer(self, new Double3(Beside, 0, plan.StackZ - 0.9), 0).Step;
            // The hatch ladder down into the cab: the one whose foot is on the cab's floor.
            Ladder? hatch = null;
            foreach (var l in shape.Ladders)
                if (cab.Contains(l.Foot + new Double3(0, 0.2, 0)))
                    hatch = l;
            if (hatch is not { } ladder)
                return null;
            var (step, there) = WarmUp.Steer(self, ladder.Foot with { Y = self.Position.Y }, self.Yaw);
            return there ? new PlayerIntent { Actions = PlayerActions.Ladder } : step;
        }
        if (self.Parent > 0 && self.Surface == Surface.Roof && train.VehicleAhead(self.Parent) == 0)
            return OntoTheEngine(self, train);
        forward = true;
        return null;
    }

    /// <summary>m: to the side of the centreline, round the engine's stack.</summary>
    const double Beside = 0.7;

    /// <summary>
    /// Along the car's roof to its front end on the centreline, facing forward, and a running jump onto the engine's hood when
    /// it's one to make (<see cref="WarmUp.CanJumpGap"/>); squared up at the end until it is.
    /// </summary>
    public static PlayerIntent OntoTheEngine(in PlayerState self, TrainOnLine train)
    {
        double l = train.Frames[self.Parent].Shape.HalfLength;
        double turn = Math.IEEERemainder(0 - self.Yaw, 2 * Math.PI);
        bool aligned = Math.Abs(turn) < 0.1;
        var intent = new PlayerIntent
        {
            LookYaw = (float)Math.Clamp(turn * 0.3, -0.2, 0.2),
            MoveZ = aligned ? 1 : 0,
            MoveX = aligned ? (float)Math.Clamp(-self.Position.X * 0.8, -1, 1) : 0,
            Buttons = PlayerButtons.Run,
        };
        if (self.Position.Z < -l + 0.45 && aligned)
        {
            if (WarmUp.CanJumpGap(self, train, null, 0))
                intent.Buttons |= PlayerButtons.Jump;
            else
                intent.MoveZ = 0;
        }
        return intent;
    }

    /// <summary>
    /// Off the engine's hood roof, back onto the train (a walker come forward that's no longer wanted there): round the stack,
    /// then back along the centreline and a running jump down onto car 1's roof. Null off the engine's roof, or with no car
    /// behind it.
    /// </summary>
    public static PlayerIntent? OffTheEngine(in PlayerState self, TrainOnLine train)
    {
        int behind = train.VehicleBehind(0);
        if (behind <= 0 || self.Parent != 0 || self.Surface != Surface.Roof)
            return null;
        var plan = EnginePlan.Of(train.Dynamics.Tuning.Geometry);
        double edge = plan.Half - CarShape.BoilerToEnd;
        // Short of the stack, and on the centreline: out to its side first, then past it.
        if (self.Position.Z < plan.StackZ + 0.55)
            return WarmUp.Steer(self, new Double3(Beside, 0, plan.StackZ + 0.95), Math.PI).Step;
        double turn = Math.IEEERemainder(Math.PI - self.Yaw, 2 * Math.PI);
        bool aligned = Math.Abs(turn) < 0.1;
        var intent = new PlayerIntent
        {
            LookYaw = (float)Math.Clamp(turn * 0.3, -0.2, 0.2),
            MoveZ = aligned ? 1 : 0,
            MoveX = aligned ? (float)Math.Clamp(self.Position.X * 0.8, -1, 1) : 0,
            Buttons = PlayerButtons.Run,
        };
        if (self.Position.Z > edge - 0.45 && aligned)
        {
            if (WarmUp.CanJumpGap(self, train, null, behind))
                intent.Buttons |= PlayerButtons.Jump;
            else
                intent.MoveZ = 0;
        }
        return intent;
    }
}
