using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Bots;

/// <summary>
/// A bot is just something that produces intent from what its client can see: the same
/// input path as a person (CLAUDE.md: bots use the same path). The harness uses bots to put
/// real, messy traffic through the netcode.
/// </summary>
public interface IBot
{
    string Name { get; }
    PlayerIntent Decide(in PlayerState self, TrainOnLine train, uint tick);
}

/// <summary>
/// Paces the roofs end to end, jumping the coupling gaps, with occasional stops and glances.
/// Exercises car-frame changes, airborne world-frame play and landing: the hard cases for prediction.
/// </summary>
public sealed class RoofWalkerBot(int seed) : IBot
{
    readonly Random _rng = new(seed);
    int _direction = -1; // −1 walks toward the engine (car-local −Z), +1 toward the back
    int _pauseTicks;

    public string Name => "roof-walker";

    public PlayerIntent Decide(in PlayerState self, TrainOnLine train, uint tick)
    {
        if (!self.Alive || self.Parent == PlayerState.World)
            return default;
        // Fell into a coupling gap, or mid-climb: grab the end ladder and go up.
        if (self.Surface is Surface.Coupler or Surface.Ladder)
            return new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Use };
        if (!self.Grounded)
            return default;
        var frame = train.Frames[self.Parent];
        double halfLength = frame.Shape.HalfLength;
        double z = self.Position.Z;

        // Turn round at the ends of the cars. The engine belongs to whoever is working the cab, and its
        // tender sits lower than a car roof, so there's no jumping back from it anyway.
        bool frontEnd = train.VehicleAhead(self.Parent) <= 0, backEnd = train.VehicleBehind(self.Parent) < 0;
        if (frontEnd && _direction < 0 && z < -halfLength + 1.5 || backEnd && _direction > 0 && z > halfLength - 1.5)
            _direction = -_direction;
        if (self.Parent == 0)
            _direction = 1;

        if (_pauseTicks > 0)
        {
            _pauseTicks--;
            return new PlayerIntent { LookYaw = (float)(_rng.NextDouble() - 0.5) * 0.05f };
        }
        if (_rng.NextDouble() < 0.004)
            _pauseTicks = _rng.Next(15, 90);

        // Face along the car in the walking direction; steer back to the centreline.
        double targetYaw = _direction < 0 ? 0 : Math.PI;
        double turn = WrapAngle(targetYaw - self.Yaw);
        double lateral = -self.Position.X * 0.8;
        bool aligned = Math.Abs(turn) < 0.1;
        var intent = new PlayerIntent
        {
            LookYaw = (float)Math.Clamp(turn * 0.3, -0.2, 0.2),
            // Stand still until facing the way we're going: turning while walking walks you off the end.
            MoveZ = aligned ? 1 : 0,
            MoveX = aligned ? (float)Math.Clamp(_direction < 0 ? lateral : -lateral, -1, 1) : 0,
            Buttons = PlayerButtons.Run,
        };

        // Jump the gap at the car end we're heading for, if there's a car beyond it.
        bool nearEnd = _direction < 0 ? z < -halfLength + 0.45 : z > halfLength - 0.45;
        int beyond = _direction < 0 ? train.VehicleAhead(self.Parent) : train.VehicleBehind(self.Parent);
        bool carBeyond = beyond > 0;
        if (nearEnd && carBeyond && aligned)
            intent.Buttons |= PlayerButtons.Jump;
        return intent;
    }

    static double WrapAngle(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }
}

/// <summary>
/// Works the cab alone: opens the throttle, brakes before the end of the line, and keeps the fire fed
/// (walks to the firebox and shovels when pressure drops). One bot doing both jobs is fine at short
/// consists; at twenty cars it can't keep up, which is the point (spec B.6).
/// </summary>
public sealed class ConductorBot : IBot
{
    public string Name => "conductor";

    public PlayerIntent Decide(in PlayerState self, TrainOnLine train, uint tick)
    {
        var intent = Drive(train, tick);
        if (train.BoilerTuning is { } bt && train.Boiler.Pressure < bt.WorkingBandMax - 2 && train.Boiler.Tender >= 1 && PlayerMotor.InCab(self, train))
        {
            if (CrewActions.Nearest(self, train) == InteractableKind.Firebox)
                intent.Buttons |= PlayerButtons.Use;
            else
            {
                var firebox = train.Frames[0].Shape.Interactables.First(i => i.Kind == InteractableKind.Firebox).Position;
                intent.MoveZ = self.Position.Z > firebox.Z + 0.5 ? 1 : -1;
                intent.LookYaw = (float)(-self.Yaw * 0.3);
            }
        }
        return intent;
    }

    static PlayerIntent Drive(TrainOnLine train, uint tick)
    {
        var d = train.Dynamics;
        double remaining = train.Line.Length - d.Distance;
        var brakeRate = d.MaxBrakeForce / d.Consist.MassTonnes;
        double stopping = d.Speed * d.Speed / (2 * Math.Max(0.1, brakeRate)) + 150;
        var intent = new PlayerIntent();
        if (remaining < stopping)
            intent.Buttons |= PlayerButtons.Brake;
        else if (tick % 30 == 0)
            intent.ThrottleNotch = 1;
        return intent;
    }
}
