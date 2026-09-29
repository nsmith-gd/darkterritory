using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
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

/// <summary>A bot that needs to see more of the world than the train (enemies, the Choir).</summary>
public interface IWorldBot : IBot
{
    PlayerIntent Decide(in PlayerState self, World world, uint tick, out PlayerState aimed);
}

/// <summary>
/// Mans the guard gun and shoots running hounds only inside range: restraint, because every round
/// brings the Choir (GDD §14). Given the Choir's tuning it also holds fire once another burst would
/// bring the swarm, and lets the hounds come rather than get the whole crew killed. Aims by turning
/// to face the target.
/// </summary>
public sealed class GunnerBot(GunTuning guns, ChoirTuning? choir = null, int seed = 1) : IWorldBot
{
    public string Name => "gunner";
    bool _holding;
    // Off the gun, it gets about like anyone else on the roofs.
    readonly RoofWalkerBot _legs = new(seed);

    public PlayerIntent Decide(in PlayerState self, TrainOnLine train, uint tick) => default;

    public PlayerIntent Decide(in PlayerState self, World world, uint tick, out PlayerState aimed)
    {
        aimed = self;
        if (!self.Alive)
            return default;
        // Hounds that got aboard can't be shot from the gun they're standing next to: get clear (they drop
        // off once nobody's near), then walk back to the guard car and take the gun again.
        bool houndsAboard = world.ActiveEnemies.Any(e => e.Kind == EnemyKind.CinderHound && e.Attached >= 0);
        if (houndsAboard || Guns.MannedGun(self, world.Train, guns) is not { } gun)
        {
            if (!houndsAboard)
                _legs.Head(+1);
            return self.Parent > 0 ? _legs.Decide(self, world, tick, out aimed) : default;
        }
        // Hold fire once another burst would bring the swarm, and keep holding until they've dispersed
        // below the approach: firing again sooner resets the Choir's quiet clock and it never drops.
        if (choir is not null && world.Choir.Aggro + 3 * choir.AggroPerRound >= choir.SwarmThreshold)
            _holding = true;
        else if (choir is null || world.Choir.Aggro < choir.ApproachThreshold)
            _holding = false;
        bool holdFire = _holding;
        var frame = world.Train.Frames[gun];
        var muzzle = frame.ToWorld(frame.Shape.Gun!.Value.Position);
        var target = world.ActiveEnemies.Where(e => e.HitRadius > 0 && !e.Gone)
            .Select(e => (e, offset: e.WorldPosition(world.Train) - muzzle))
            .Where(x => x.offset.Length <= guns.Range)
            .OrderBy(x => x.offset.Length).FirstOrDefault();
        if (target.e is null)
            return default;
        var d = frame.DirToLocal(target.offset).Normalized;
        double yaw = Math.Atan2(-d.X, -d.Z), pitch = Math.Asin(d.Y);
        // Turn by look deltas, the way a player would.
        var intent = new PlayerIntent { LookYaw = (float)Wrap(yaw - self.Yaw), LookPitch = (float)(pitch - self.Pitch) };
        aimed = self with { Yaw = self.Yaw + intent.LookYaw, Pitch = self.Pitch + intent.LookPitch };
        if (!holdFire)
            intent.Buttons = PlayerButtons.Fire;
        return intent;
    }

    static double Wrap(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }
}

/// <summary>
/// Paces the roofs end to end, jumping the coupling gaps, with occasional stops and glances.
/// Exercises car-frame changes, airborne world-frame play and landing: the hard cases for prediction.
/// With the world in view it also answers Clingers (App. A.4): heads for the car and prises them off.
/// </summary>
public sealed class RoofWalkerBot(int seed) : IWorldBot
{
    const double PryReach = 1.0;

    public PlayerIntent Decide(in PlayerState self, World world, uint tick, out PlayerState aimed)
    {
        aimed = self;
        var train = world.Train;
        if (self.Alive && self.Parent > 0 && self.Parent < train.Frames.Count)
        {
            int parent = self.Parent;
            var clinger = world.ActiveEnemies.Where(e => e.Kind == EnemyKind.Clinger && !e.Gone)
                .OrderBy(e => Math.Abs(e.Attached - parent)).FirstOrDefault();
            if (clinger is not null && clinger.Attached == self.Parent && self.Grounded && self.Surface == Surface.Roof)
                return Pry(self, clinger);
            if (clinger is not null && clinger.Attached != self.Parent)
                _direction = clinger.Attached < self.Parent ? -1 : 1;
            // Hounds aboard: nobody goes near them, and anyone close walks away (they drop off when bored).
            if (world.ActiveEnemies.Any(e => e.Kind == EnemyKind.CinderHound && e.Attached >= 0 && e.Attached >= parent - 1))
                _direction = -1;
        }
        return Decide(self, train, tick);
    }

    /// <summary>Walk to the roof edge over it, then stand and hold Use until it lets go.</summary>
    static PlayerIntent Pry(in PlayerState self, Enemy clinger)
    {
        double side = Math.Sign(clinger.Local.X);
        double dx = side * 0.6 - self.Position.X, dz = clinger.Local.Z - self.Position.Z;
        if (Math.Abs(dz) < PryReach * 0.5 && Math.Abs(dx) < 0.3)
            return new PlayerIntent { Buttons = PlayerButtons.Use };
        // Move in the car frame whichever way we happen to be facing: forward is −Z turned by yaw.
        double fx = -Math.Sin(self.Yaw), fz = -Math.Cos(self.Yaw), rx = Math.Cos(self.Yaw), rz = -Math.Sin(self.Yaw);
        return new PlayerIntent
        {
            MoveZ = (float)Math.Clamp((dx * fx + dz * fz) * 2, -1, 1),
            MoveX = (float)Math.Clamp((dx * rx + dz * rz) * 2, -1, 1),
        };
    }

    readonly Random _rng = new(seed);
    int _direction = -1; // −1 walks toward the engine (car-local −Z), +1 toward the back
    int _pauseTicks;

    public string Name => "roof-walker";

    /// <summary>Walk towards the back (+1) or the engine (−1) from here on.</summary>
    public void Head(int direction) => _direction = Math.Sign(direction);

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
/// Works the cab alone: holds cruise (spec B.3: 14 m/s, the speed at which even twenty cars can stop
/// for what the lamp shows), brakes hard when the lamp finds something on the line, brakes before the
/// end of the line, and keeps the fire fed (walks to the firebox and shovels when pressure drops).
/// One bot doing both jobs is fine at short consists; at twenty cars it can't keep up (spec B.6).
/// </summary>
public sealed class ConductorBot : IWorldBot
{
    public string Name => "conductor";
    public double CruiseSpeed { get; init; } = 14;

    public PlayerIntent Decide(in PlayerState self, TrainOnLine train, uint tick) => Work(self, train, Drive(train, tick));

    public PlayerIntent Decide(in PlayerState self, World world, uint tick, out PlayerState aimed)
    {
        aimed = self;
        var train = world.Train;
        var intent = Drive(train, tick);
        // Watch the road: something showing on the line ahead means get below derailing speed.
        bool somethingAhead = world.ActiveEnemies.Any(e => e.Kind == EnemyKind.Sleepers && e.Phase == SpinePhase.Telegraph
            && e.LineDistance > train.Dynamics.Distance && e.LineDistance - train.Dynamics.Distance < 200);
        if (somethingAhead && train.Dynamics.Speed > 4)
        {
            intent.Buttons |= PlayerButtons.Brake;
            intent.ThrottleNotch = -4;
        }
        return Work(self, train, intent);
    }

    PlayerIntent Work(in PlayerState self, TrainOnLine train, PlayerIntent intent)
    {
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

    bool _holdingDown;

    PlayerIntent Drive(TrainOnLine train, uint tick)
    {
        var d = train.Dynamics;
        // To the end of the track it's on: the terminus, or a dead line's buffer stop.
        double remaining = train.Line.PathLength(d.Path) - d.Distance;
        var brakeRate = d.MaxBrakeForce / d.Consist.MassTonnes;
        double stopping = d.Speed * d.Speed / (2 * Math.Max(0.1, brakeRate)) + 150;
        var intent = new PlayerIntent();
        // A descent runs the train away with the regulator shut; hold it on the brake, with some
        // hysteresis so it isn't hammered every tick (fade only builds while it's applied).
        if (d.Speed > CruiseSpeed + 1.5)
            _holdingDown = true;
        else if (d.Speed <= CruiseSpeed)
            _holdingDown = false;
        if (remaining < stopping || _holdingDown)
        {
            intent.Buttons |= PlayerButtons.Brake;
            intent.ThrottleNotch = -4;
        }
        else if (tick % 15 == 0)
            // Notch towards cruise: open up below it, ease off above it.
            intent.ThrottleNotch = (sbyte)(d.Speed < CruiseSpeed - 1 ? 1 : d.Speed > CruiseSpeed + 0.5 ? -1 : 0);
        return intent;
    }
}
