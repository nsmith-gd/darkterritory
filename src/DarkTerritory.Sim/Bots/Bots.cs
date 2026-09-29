using Ballast;
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
public sealed class GunnerBot(GunTuning guns, ChoirTuning? choir = null, int seed = 1, ColdTuning? cold = null) : IWorldBot
{
    public string Name => "gunner";
    public int WarmUps => _legs.WarmUps;
    bool _holding;
    // Off the gun, it gets about like anyone else on the roofs, and goes in to get warm like them.
    readonly RoofWalkerBot _legs = new(seed, cold);

    public PlayerIntent Decide(in PlayerState self, TrainOnLine train, uint tick) => default;

    public PlayerIntent Decide(in PlayerState self, World world, uint tick, out PlayerState aimed)
    {
        aimed = self;
        if (!self.Alive)
            return default;
        // Nobody holds a gun through the cold (spec B.2): off it and indoors until warm, then back.
        if (_legs.Warming(self))
            return _legs.Decide(self, world, tick, out aimed);
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
public sealed class RoofWalkerBot(int seed, ColdTuning? cold = null) : IWorldBot
{
    const double PryReach = 1.0;
    // Each walker goes in at its own point in the onset, so a crew that started out together doesn't all queue at one door.
    readonly WarmUp? _warm = cold is null ? null : new WarmUp(cold, 0.45 + 0.25 * new Random(seed).NextDouble());

    /// <summary>Cold enough to go in, or already on the way (a gunner leaves its gun for it).</summary>
    public bool Warming(in PlayerState self) => _warm is { } w && (w.Active || w.Wants(self));
    /// <summary>Where getting warm has got to (for tests and the harness).</summary>
    public string? WarmUpStep => _warm?.Doing;
    /// <summary>Times it's gone in and got warm.</summary>
    public int WarmUps => _warm?.Done ?? 0;

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
        if (!self.Alive)
            return default;
        // Off onto the ballast (not just in the air over a gap, which is how you get down onto a coupler plate).
        if (self.Parent == PlayerState.World && self.Surface == Surface.Ground)
        {
            _warm?.Abandon();
            return Board(self, train);
        }
        if (_warm?.Decide(self, train) is { } warming)
            return warming;
        if (self.Parent == PlayerState.World)
            return default;
        // On a car's floor (in from the cold, or knocked in): out through the nearer door, then up the end ladder.
        if (self.Surface == Surface.Deck && self.Parent > 0 && _warm is not null && _warm.Leave(self, train) is { } leaving)
            return leaving;
        // Mid-climb: keep going up.
        if (self.Surface == Surface.Ladder)
            return new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Use };
        // Fell into a coupling gap: over to the foot of the end ladder (the plate is narrow, the ladder's just off its
        // edge), facing it, then take hold and climb. Use here is only ever with a push: standing, it cuts the coupling.
        if (self.Surface == Surface.Coupler)
        {
            double l = train.Frames[self.Parent].Shape.Bounds.Max.Z;
            var (step, there) = WarmUp.Steer(self, new Double3(0.2, 0, l + 0.45), 0);
            return there ? new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Use } : step;
        }
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

    /// <summary>
    /// Off onto the ballast (slipped off a plate, knocked off): walk to the nearest car's side ladder and climb it. You
    /// catch a train at a stand or a crawl this way, not one at speed.
    /// </summary>
    static PlayerIntent Board(in PlayerState self, TrainOnLine train)
    {
        Double3? best = null;
        double bestD = 60;
        foreach (var frame in train.Frames)
        {
            if (frame.Index == 0)
                continue; // the engine is boarded by its crew
            foreach (var ladder in frame.Shape.Ladders)
            {
                // Side ladders from the ground: stand just outside the foot.
                if (Math.Abs(ladder.Inward.X) < 0.9 || ladder.Foot.Y > 0.5)
                    continue;
                var at = frame.ToWorld(ladder.Foot - ladder.Inward * 0.3);
                double d = Math.Sqrt(Math.Pow(at.X - self.Position.X, 2) + Math.Pow(at.Z - self.Position.Z, 2));
                if (d < bestD)
                {
                    bestD = d;
                    best = at;
                }
            }
        }
        if (best is not { } target || !self.Grounded)
            return default;
        double dx = target.X - self.Position.X, dz = target.Z - self.Position.Z;
        double yaw = Math.Atan2(-dx, -dz);
        double turn = WrapAngle(yaw - self.Yaw);
        bool aligned = Math.Abs(turn) < 0.15;
        return new PlayerIntent
        {
            LookYaw = (float)Math.Clamp(turn, -0.4, 0.4),
            MoveZ = aligned ? 1 : 0,
            // At the foot, push on and take hold: it climbs from there as out of any gap.
            Buttons = bestD < 0.6 ? PlayerButtons.Use : PlayerButtons.None,
        };
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

/// <summary>
/// Getting out of the cold (spec B.2: onset after 200 s outside, death at 320, and back to nothing within 45 s near
/// heat), the way a person would: off the end of the roof onto the coupler plate, in through the car's end door, shut
/// it (a car only warms you shut), and wait by it until warm; then out, and back up the end ladder. All intent. Use is
/// only ever pressed facing a door that's in reach: on the coupler plate, Use anywhere else cuts the coupling.
/// </summary>
/// <param name="goInAt">How far into the onset to go in (0.6: at 120 s of the 200).</param>
public sealed class WarmUp(ColdTuning cold, double goInAt = 0.6)
{
    enum Step : byte { Off, ToEnd, Drop, ToDoor, Open, In, Shut, Warm, Reopen, Out }

    /// <summary>Come out this warm.</summary>
    const double WarmEnough = 5;
    const int GiveUpTicks = SimConstants.TickRate * 25;

    Step _step;
    int _car = -1, _ticks;
    double _l, _doorX;

    public bool Active => _step != Step.Off;
    /// <summary>Where it's got to (for tests and the harness).</summary>
    public string Doing => _step.ToString();
    /// <summary>Times it's been in and got warm.</summary>
    public int Done { get; private set; }

    public bool Wants(in PlayerState s) =>
        s.Cold >= cold.OnsetSeconds * goInAt * (s.Has(PlayerFlags.Revived) ? cold.RevivedOnsetScale : 1);

    /// <summary>This tick's intent while getting warm; null when there's nothing to do (walk as usual).</summary>
    public PlayerIntent? Decide(in PlayerState self, TrainOnLine train)
    {
        if (_step == Step.Off)
        {
            if (!Wants(self) || self.Surface != Surface.Roof || self.Parent <= 0 || !Plan(self, train))
                return null;
            _step = Step.ToEnd;
            _ticks = 0;
        }
        // Stuck (the train split under us, the door jammed by something): give it up and walk on as usual.
        if (++_ticks > GiveUpTicks || !self.Alive)
        {
            _step = Step.Off;
            return null;
        }
        // Mid-drop onto the plate: nothing to do until we land.
        if (self.Surface == Surface.Air)
            return new PlayerIntent();
        bool open = train.Vehicles[_car].DoorOpen(RearDoor);
        switch (_step)
        {
            case Step.ToEnd:
                // Along the roof to the end that has the plate we want, then straight off it (no jump).
                if (self.Surface == Surface.Coupler)
                    return Next(Step.ToDoor);
                return new PlayerIntent { LookYaw = Turn(self, _dropYaw), MoveZ = Aligned(self, _dropYaw) ? 1 : 0, MoveX = (float)Math.Clamp(-self.Position.X * 0.8 * (_dropYaw == 0 ? 1 : -1), -1, 1) };
            case Step.ToDoor:
                if (self.Parent != _car || self.Surface != Surface.Coupler)
                    return Abandon();
                // Just outside the door, facing it: on the plate (0.8 m wide), which the door's edge is just off.
                return Reach(self, new Double3(PlateLine, 0, _l + 0.45), 0, Step.Open);
            case Step.Open:
                if (open)
                    return Next(Step.In);
                return CrewActions.Nearest(self, train) == InteractableKind.Door ? new PlayerIntent { Buttons = PlayerButtons.Use } : Next(Step.ToDoor);
            case Step.In:
                // Someone else's hand on the same door shut it again: back to opening it.
                if (!open && self.Surface == Surface.Coupler)
                    return Next(Step.Open);
                // Straight in, not sideways: on the plate that's off its edge. The doorway takes the plate's middle.
                return Reach(self, new Double3(self.Surface == Surface.Coupler ? PlateLine : _doorX, 0, _l - 1.6), 0, Step.Shut);
            case Step.Shut:
                {
                    // Every door shut: a car only warms you shut, and someone else may have left the far one open.
                    var doors = train.Vehicles[_car];
                    int door = doors.DoorOpen(RearDoor) ? RearDoor : doors.DoorOpen(FrontDoor) ? FrontDoor : -1;
                    if (door < 0)
                        return Next(Step.Warm);
                    // By it, inside, facing it.
                    var at = new Double3(_doorX, 0, door == RearDoor ? _l - 0.5 : -_l + 0.5);
                    double facing = door == RearDoor ? Math.PI : 0;
                    if ((Flat(self.Position) - Flat(at)).Length > 0.2 || !Aligned(self, facing))
                        return Reach(self, at, facing, Step.Shut);
                    return CrewActions.Nearest(self, train) == InteractableKind.Door ? new PlayerIntent { Buttons = PlayerButtons.Use } : Abandon();
                }
            case Step.Warm:
                _ticks = 0; // waiting's not being stuck
                // Someone came or went and left a door open: shut it again.
                if (train.Vehicles[_car].DoorsOpen != 0)
                    return Next(Step.Shut);
                if (self.Cold > WarmEnough)
                    return new PlayerIntent();
                Done++;
                _outEnd = self.Position.Z >= 0 ? 1 : -1;
                return Next(Step.Reopen);
            case Step.Reopen:
                {
                    // The way out: the door nearer where we are (in by the rear, but someone may have moved us).
                    int door = _outEnd > 0 ? RearDoor : FrontDoor;
                    if (train.Vehicles[_car].DoorOpen(door))
                        return Next(Step.Out);
                    var at = new Double3(_doorX, 0, _outEnd * (_l - 0.5));
                    double facing = _outEnd > 0 ? Math.PI : 0;
                    if ((Flat(self.Position) - Flat(at)).Length > 0.2 || !Aligned(self, facing))
                        return Reach(self, at, facing, Step.Reopen);
                    return CrewActions.Nearest(self, train) == InteractableKind.Door ? new PlayerIntent { Buttons = PlayerButtons.Use } : Abandon();
                }
            case Step.Out:
                // Straight out through the doorway onto the plate (it's narrow: off its side is the ballast); on it,
                // the walker climbs the end ladder as it does out of any gap.
                if (self.Surface == Surface.Coupler || self.Parent != _car)
                {
                    _step = Step.Off;
                    return null;
                }
                return Reach(self, new Double3(PlateLine, 0, _outEnd * (_l + 0.6)), _outEnd > 0 ? Math.PI : 0, Step.Out);
            default:
                _step = Step.Off;
                return null;
        }
    }

    const int FrontDoor = 0, RearDoor = 1; // doors are listed front (−Z) then rear (+Z)
    // Where to cross between a doorway (left of centre) and the coupler plate (0.8 m wide, centred): in both.
    const double PlateLine = -0.15;
    double _dropYaw;
    int _outEnd = 1;

    /// <summary>
    /// Found on a car's floor with nothing under way (knocked in, or given up in there): cold, shut it up and warm up
    /// here; warm, out by the nearer door.
    /// </summary>
    public PlayerIntent? Leave(in PlayerState self, TrainOnLine train)
    {
        if (_step != Step.Off || !Walkable(train, self.Parent))
            return null;
        _car = self.Parent;
        _l = train.Frames[_car].Shape.Bounds.Max.Z;
        _doorX = train.Dynamics.Tuning.Geometry.Interior!.DoorX;
        _outEnd = self.Position.Z >= 0 ? 1 : -1;
        _step = Wants(self) ? Step.Shut : Step.Reopen;
        _ticks = 0;
        return new PlayerIntent();
    }

    /// <summary>
    /// Which plate to drop onto: the one behind this car (this car's own), or in front (the car ahead's), whichever is
    /// nearer and has a car on it; in through that plate's car's rear door. Never the engine's: its cab is the fireman's.
    /// </summary>
    bool Plan(in PlayerState s, TrainOnLine train)
    {
        int here = s.Parent;
        int behind = train.VehicleBehind(here), ahead = train.VehicleAhead(here);
        bool back = behind > 0 && Walkable(train, here), front = ahead > 0 && Walkable(train, ahead);
        if (!back && !front)
            return false;
        bool goBack = back && (!front || s.Position.Z > 0);
        _car = goBack ? here : ahead;
        _dropYaw = goBack ? Math.PI : 0; // +Z is yaw π; −Z is 0
        var interior = train.Dynamics.Tuning.Geometry.Interior!;
        _l = train.Frames[_car].Shape.Bounds.Max.Z;
        _doorX = interior.DoorX;
        return true;
    }

    /// <summary>A car you can walk into: it has a room and its doors.</summary>
    static bool Walkable(TrainOnLine train, int car) => car > 0 && train.Frames[car].Shape is { Interior: not null, DoorList.Count: >= 2 };

    PlayerIntent? Next(Step step)
    {
        _step = step;
        return new PlayerIntent();
    }

    /// <summary>Gives up getting warm (knocked off the train, or stuck): the walker carries on as usual.</summary>
    public PlayerIntent? Abandon()
    {
        _step = Step.Off;
        return null;
    }

    /// <summary>Walk to a point on this car's floor (car frame), then face <paramref name="yaw"/>; <paramref name="then"/> once there.</summary>
    PlayerIntent? Reach(in PlayerState self, Double3 target, double yaw, Step then)
    {
        var (step, there) = Steer(self, target, yaw);
        return there ? Next(then) : step;
    }

    /// <summary>
    /// One step towards a point in the player's frame, turning to <paramref name="yaw"/> on the way: true once it's
    /// there and facing that way. Slow near the point, so it stops on a plate rather than walking off its edge.
    /// </summary>
    public static (PlayerIntent Step, bool There) Steer(in PlayerState self, Double3 target, double yaw)
    {
        var offset = Flat(target) - Flat(self.Position);
        if (offset.Length < 0.15)
            return Aligned(self, yaw) ? (new PlayerIntent(), true) : (new PlayerIntent { LookYaw = Turn(self, yaw) }, false);
        double fx = -Math.Sin(self.Yaw), fz = -Math.Cos(self.Yaw), rx = Math.Cos(self.Yaw), rz = -Math.Sin(self.Yaw);
        double gain = offset.Length < 1 ? 1.2 : 2;
        return (new PlayerIntent
        {
            LookYaw = Turn(self, yaw),
            MoveZ = (float)Math.Clamp((offset.X * fx + offset.Z * fz) * gain, -1, 1),
            MoveX = (float)Math.Clamp((offset.X * rx + offset.Z * rz) * gain, -1, 1),
        }, false);
    }

    static Double3 Flat(Double3 v) => new(v.X, 0, v.Z);
    static float Turn(in PlayerState self, double yaw) => (float)Math.Clamp(Wrap(yaw - self.Yaw), -0.5, 0.5);
    static bool Aligned(in PlayerState self, double yaw) => Math.Abs(Wrap(yaw - self.Yaw)) < 0.1;
    static double Wrap(double a) => Math.IEEERemainder(a, 2 * Math.PI);
}
