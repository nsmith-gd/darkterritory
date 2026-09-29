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
public sealed class GunnerBot(GunTuning guns, ChoirTuning? choir = null, int seed = 1, ColdTuning? cold = null, StopHand? job = null) : IWorldBot
{
    public string Name => "gunner";
    public int WarmUps => _legs.WarmUps;
    /// <summary>Its part when the train stops to work a facility (a small crew needs the gunner on the winch too).</summary>
    public StopHand? Job => _legs.Job;
    bool _holding;
    // Off the gun, it gets about like anyone else on the roofs, goes in to get warm like them, and works stops like them.
    readonly RoofWalkerBot _legs = new(seed, cold, job);

    public PlayerIntent Decide(in PlayerState self, TrainOnLine train, uint tick) => default;

    public PlayerIntent Decide(in PlayerState self, World world, uint tick, out PlayerState aimed)
    {
        aimed = self;
        if (!self.Alive)
            return default;
        // A tunnel's mouth ahead: at the gun it's down behind the shield; anywhere else on the roofs, off them.
        _legs.Looked(world, safe: Guns.MannedGun(self, world.Train, guns) is not null);
        // Nobody holds a gun through the cold (spec B.2): off it and indoors until warm, then back. Nor through a stop
        // they have a part in.
        if (_legs.Warming(self) || _legs.Work(self, world) is not null)
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
public sealed class RoofWalkerBot(int seed, ColdTuning? cold = null, StopHand? job = null) : IWorldBot
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
    /// <summary>Its part when the train stops to work a facility (T32).</summary>
    public StopHand? Job => job;

    uint _workedTick = uint.MaxValue;
    PlayerIntent? _work;
    bool _looked;

    /// <summary>The gunner has read the line for its legs this tick already (it knows whether it's at the gun).</summary>
    internal void Looked(World world, bool safe)
    {
        Look(world, safe);
        _looked = true;
    }

    /// <summary>This tick's intent for its part in a stop, if it has one to do now (worked out once a tick).</summary>
    public PlayerIntent? Work(in PlayerState self, World world)
    {
        if (job is null || _warm is { Active: true })
            return null;
        if (_workedTick != world.Tick)
        {
            _workedTick = world.Tick;
            _work = job.Decide(self, world);
        }
        return _work;
    }

    /// <summary>
    /// Reads the line ahead: a posted tunnel (its board read) whose mouth is near enough, or the train still in one, means
    /// off the roofs and indoors (sight.json: the mouth takes anyone standing up there). <paramref name="safe"/>: where it
    /// stands is clear anyway (a gun's crew are down behind its shield).
    /// </summary>
    public void Look(World world, bool safe = false)
    {
        if (_warm is not null)
            _warm.Shelter = !safe && TunnelNear(world);
    }

    /// <summary>Seconds' warning a walker wants to get off the roofs and in before a tunnel's mouth.</summary>
    const double ShelterSeconds = 40;

    /// <summary>A tunnel ahead that's been posted (the board read, or its mouth made out in the dark), and near.</summary>
    public static bool TunnelNear(World world)
    {
        if (world.Lineside is not { } lineside)
            return false;
        var d = world.Train.Dynamics;
        double reach = Math.Max(d.Speed, 3) * ShelterSeconds + 30;
        return lineside.Signs.Any(s => s.Kind == Sim.Route.SignKind.LowClearance && lineside.Read(s.Id) && s.End >= d.RearDistance - 5
            && s.Start - d.Distance <= reach);
    }

    public PlayerIntent Decide(in PlayerState self, World world, uint tick, out PlayerState aimed)
    {
        aimed = self;
        // Warming up means a coupler plate, so it keeps to the gaps no Rattle's in (App. A.5): its client can see them.
        if (_warm is not null)
            _warm.Rattled = car => world.ActiveEnemies.Any(e => e is Rattle r && r.Attached == car);
        if (!_looked)
            Look(world);
        _looked = false;
        if (Work(self, world) is { } working)
            return working;
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
        {
            if (WarmUp.CanJumpGap(self, train, cold))
                intent.Buttons |= PlayerButtons.Jump;
            else
            {
                // Not a jump to make from here (off the centreline, a curve pulling the roof away, or too cold to run at it):
                // stand at the end and square up, and chilled, turn back rather than try it.
                intent.MoveZ = 0;
                if (cold is { } c && self.Cold >= c.OnsetSeconds * (self.Has(PlayerFlags.Revived) ? c.RevivedOnsetScale : 1))
                    _direction = -_direction;
            }
        }
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
/// <para>
/// Lamps down (App. A.6): eyes catching the lamp out in the dark and it switches the lamp off, and leaves it off a while
/// after the last it saw of them. With the lamp off, the Sleepers only show close (App. A.2), at bracing distance: too
/// late to brake from cruise to under the speed they derail at. So in the dark it runs just under that speed.
/// </para>
/// </summary>
public sealed class ConductorBot(CrewCalls? calls = null, int member = 0) : IWorldBot
{
    public string Name => "conductor";
    public double CruiseSpeed { get; init; } = 14;
    /// <summary>Cruise with the lamp out: just under the Sleepers' derailing speed (enemies.json, 11.1 m/s: 40 km/h).</summary>
    public double DarkCruiseSpeed { get; init; } = 10.5;
    /// <summary>How long it keeps the lamp down after the last eyeshine.</summary>
    public double LampDownSeconds { get; init; } = 30;
    uint? _sawEyes;
    Rail.Branch? _alone;
    StopHand? _aloneHand;
    /// <summary>With a crew to call to, it stops to work the facilities they can (T32, GDD §17).</summary>
    public StopDriver? Stops { get; } = calls is null ? null : new StopDriver(calls);

    public PlayerIntent Decide(in PlayerState self, TrainOnLine train, uint tick) => Work(self, train, Drive(train, tick, CruiseSpeed));

    /// <summary>Lamps down while eyes are out there, and for a while after; up again once they've been gone a while.</summary>
    LampSwitch Lamp(World world, uint tick)
    {
        if (world.ActiveEnemies.Any(e => e is Lamplighter { Eyeshine: true }))
            _sawEyes = tick;
        if (_sawEyes is { } saw && (tick - saw) * SimConstants.TickSeconds < LampDownSeconds)
            return LampSwitch.Off;
        _sawEyes = null;
        return world.LampLit ? LampSwitch.None : LampSwitch.On;
    }

    public PlayerIntent Decide(in PlayerState self, World world, uint tick, out PlayerState aimed)
    {
        aimed = self;
        var train = world.Train;
        calls?.Say(member, StopJob.Driver, self);
        var lamp = Lamp(world, tick);
        if (Stops is { } stops)
        {
            // Nobody left to set a switch back but the driver: down it gets, and back up (the train stands on its brake).
            if (self.Alive && stops.SetBackAlone(world) is { } wrong)
                _alone = wrong;
            if (_alone is not null && calls is not null)
            {
                _aloneHand ??= new StopHand(StopJob.None, calls, member);
                if (_aloneHand.SetBackAlone(self, world, _alone) is { } getting)
                {
                    stops.Decide(self, world); // its clock runs on while it's out of the cab
                    return getting with { Lamp = lamp };
                }
                _alone = null;
            }
            stops.CruiseSpeed = CruiseSpeed;
            if (stops.Decide(self, world) is { } stopping)
                return Work(self, train, stopping) with { Lamp = lamp };
        }
        // A lantern on the line ahead, waving us down (App. A.2): do not slow down. Hold the fastest we've come at it, lamp
        // or no lamp (the Lamplighters + Ferryman bind: the lantern is its own light).
        double cruise = world.LampShining ? CruiseSpeed : DarkCruiseSpeed;
        if (world.ActiveEnemies.OfType<Ferryman>().FirstOrDefault(f => f.Waving) is { } ferryman)
            cruise = Math.Max(cruise, ferryman.Extra);
        // The boards it's read (sight.json): down to a posted speed in time, and held there till the last car's through.
        cruise = Math.Min(cruise, Posted(world));
        var intent = Drive(train, tick, cruise);
        intent.Lamp = lamp;
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

    /// <summary>Under a posted speed by this much (m/s): the driver's cruise wobbles either side of what it's holding.</summary>
    const double PostedMargin = 1;

    /// <summary>
    /// The fastest the boards read so far allow here: a posted stretch's speed on it, until the last car's through, and
    /// short of it no faster than a gentle brake gets down to that by the time it's there.
    /// </summary>
    static double Posted(World world)
    {
        if (world.Lineside is not { } lineside)
            return double.MaxValue;
        var d = world.Train.Dynamics;
        double brake = 0.3 * d.MaxBrakeForce / d.Consist.MassTonnes;
        double allowed = double.MaxValue;
        foreach (var s in lineside.Signs)
        {
            if (s.Kind != Sim.Route.SignKind.SpeedLimit || !lineside.Read(s.Id) || s.End < d.RearDistance)
                continue;
            double target = s.Limit - PostedMargin, to = s.Start - d.Distance - 15;
            allowed = Math.Min(allowed, to <= 0 ? target : Math.Sqrt(target * target + 2 * brake * to));
        }
        return allowed;
    }

    PlayerIntent Work(in PlayerState self, TrainOnLine train, PlayerIntent intent)
    {
        // Fire it for pressure, and at a stand (where pressure holds up on its own while the fire burns down) keep the fire
        // itself from going low: that brings the Hollow (App. A.5). The safety valve sheds what a standing fire makes.
        if (train.BoilerTuning is { } bt && train.Boiler.Tender >= 1 && PlayerMotor.InCab(self, train)
            && (train.Boiler.Pressure < bt.WorkingBandMax - 2 || train.Boiler.FireFraction(bt) < bt.LowFireFraction * 2))
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

    PlayerIntent Drive(TrainOnLine train, uint tick, double cruise)
    {
        var d = train.Dynamics;
        // To the end of the track it's on: the terminus, or a dead line's buffer stop.
        double remaining = train.Line.PathLength(d.Path) - d.Distance;
        var brakeRate = d.MaxBrakeForce / d.Consist.MassTonnes;
        double stopping = d.Speed * d.Speed / (2 * Math.Max(0.1, brakeRate)) + 150;
        var intent = new PlayerIntent();
        // A descent runs the train away with the regulator shut; hold it on the brake, with some
        // hysteresis so it isn't hammered every tick (fade only builds while it's applied).
        if (d.Speed > cruise + 1.5)
            _holdingDown = true;
        else if (d.Speed <= cruise)
            _holdingDown = false;
        if (remaining < stopping || _holdingDown)
        {
            intent.Buttons |= PlayerButtons.Brake;
            intent.ThrottleNotch = -4;
        }
        else if (tick % 15 == 0)
            // Notch towards cruise: open up below it, ease off above it.
            intent.ThrottleNotch = (sbyte)(d.Speed < cruise - 1 ? 1 : d.Speed > cruise + 0.5 ? -1 : 0);
        return intent;
    }
}

/// <summary>
/// Getting out of the cold (spec B.2: onset after 600 s outside, death at 1200, and back to nothing within 20 s near
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

    public bool Wants(in PlayerState s) => Shelter ||
        s.Cold >= cold.OnsetSeconds * goInAt * (s.Has(PlayerFlags.Revived) ? cold.RevivedOnsetScale : 1);

    /// <summary>
    /// Off the roofs and indoors, cold or not, for as long as it's set (a tunnel's mouth ahead, sight.json): the same way
    /// in, and it waits in there until it's clear.
    /// </summary>
    public bool Shelter { get; set; }

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
                {
                    // Along the roof to the end that has the plate we want, then straight off it (no jump).
                    if (self.Surface == Surface.Coupler)
                        return Next(Step.ToDoor);
                    // Not off the end on a curve at speed: in the air you go straight on while the train turns under you, and
                    // the plate is only 0.8 m wide. Wait at the end for a straighter bit, as anyone would.
                    double end = train.Frames[self.Parent].Shape.HalfLength * (_dropYaw == 0 ? -1 : 1);
                    bool atEdge = Math.Abs(end - self.Position.Z) < 0.8;
                    bool go = Aligned(self, _dropYaw) && (!atEdge || SteadyUnder(train, self.Parent));
                    return new PlayerIntent { LookYaw = Turn(self, _dropYaw), MoveZ = go ? 1 : 0, MoveX = (float)Math.Clamp(-self.Position.X * 0.8 * (_dropYaw == 0 ? 1 : -1), -1, 1) };
                }
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
                    // Every door shut: a car only warms you shut, and someone else may have left another open (the far end,
                    // or a cargo car's side door left open for loading).
                    var shape = train.Frames[_car].Shape;
                    var openDoor = shape.DoorList.Where(d => train.Vehicles[_car].DoorOpen(d.Index)).Select(d => (int?)d.Index).FirstOrDefault();
                    if (openDoor is not { } door)
                        return Next(Step.Warm);
                    // By it, inside, facing it. A side door is across the car from the aisle: along the aisle to it first.
                    var (at, facing) = Inside(shape, door);
                    bool sideDoor = Math.Abs(at.Z) < _l - 1;
                    if (sideDoor && Math.Abs(self.Position.Z - at.Z) > 0.6)
                        return Reach(self, new Double3(_doorX, 0, at.Z), facing, Step.Shut);
                    if ((Flat(self.Position) - Flat(at)).Length > 0.2 || !Aligned(self, facing))
                        return Reach(self, at, facing, Step.Shut);
                    return CrewActions.Nearest(self, train) == InteractableKind.Door ? new PlayerIntent { Buttons = PlayerButtons.Use } : Abandon();
                }
            case Step.Warm:
                _ticks = 0; // waiting's not being stuck
                // Someone came or went and left a door open: shut it again.
                if (train.Vehicles[_car].DoorsOpen != 0)
                    return Next(Step.Shut);
                if (self.Cold > WarmEnough || Shelter)
                    return new PlayerIntent();
                Done++;
                _outEnd = WayOut(self, train);
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
        _outEnd = WayOut(self, train);
        _step = Wants(self) ? Step.Shut : Step.Reopen;
        _ticks = 0;
        return new PlayerIntent();
    }

    /// <summary>
    /// Out by the nearer end door, but never the one onto the engine's plate: its ladder goes up onto the tender, which is
    /// the fireman's and lower than a car roof, and there's no way back along the train from there.
    /// </summary>
    int WayOut(in PlayerState self, TrainOnLine train) => train.VehicleAhead(_car) == 0 ? 1 : self.Position.Z >= 0 ? 1 : -1;

    /// <summary>
    /// Which plate to drop onto: the one behind this car (this car's own), or in front (the car ahead's), whichever is
    /// nearer and has a car on it; in through that plate's car's rear door. Never the engine's: its cab is the fireman's.
    /// </summary>
    /// <summary>Whether a Rattle's in the gap behind a vehicle (T54): that end's no way in. Set by the bot, which sees the world.</summary>
    public Func<int, bool>? Rattled { get; set; }

    bool Plan(in PlayerState s, TrainOnLine train)
    {
        int here = s.Parent;
        int behind = train.VehicleBehind(here), ahead = train.VehicleAhead(here);
        bool back = behind > 0 && Walkable(train, here) && Rattled?.Invoke(here) != true;
        bool front = ahead > 0 && Walkable(train, ahead) && Rattled?.Invoke(ahead) != true;
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

    /// <summary>
    /// Where to stand inside a car to work one of its doors, and the way to face: half a metre in from it. End doors are
    /// at the car's ends, side doors (a cargo car's) in the middle of its sides.
    /// </summary>
    public static (Double3 At, double Yaw) Inside(CarShape shape, int door)
    {
        var handle = shape.Interactables.First(i => i.Kind == InteractableKind.Door && i.Index == door).Position;
        var box = shape.DoorList.First(d => d.Index == door).Box;
        bool side = box.Max.Z - box.Min.Z > box.Max.X - box.Min.X;
        var inward = side ? new Double3(-Math.Sign(handle.X), 0, 0) : new Double3(0, 0, -Math.Sign(handle.Z));
        // Facing out through it: forward is (−sin yaw, 0, −cos yaw).
        return (handle with { Y = 0 } + inward * 0.5, Math.Atan2(inward.X, inward.Z));
    }

    /// <summary>
    /// Straight enough under a car to drop into its gap: the train's sideways pull there (v²/R) small enough that the drift
    /// across the plate in the fall stays well inside its half-width.
    /// </summary>
    static bool SteadyUnder(TrainOnLine train, int car)
    {
        var rake = train.RakeOf(car);
        double curvature = train.Line.Sample(rake.Path, train.Cars[car].FrontDistance).Curvature;
        return rake.Speed * rake.Speed * Math.Abs(curvature) < MaxDropPull;
    }

    /// <summary>m/s²: at 15 m/s, a curve of 750 m radius or wider.</summary>
    const double MaxDropPull = 0.3;

    /// <summary>
    /// Whether a gap jump from where a walker stands will make the far roof: square on the centreline (the coupler plate
    /// under the gap is only 0.8 m wide, and off its edge is the ballast), on a straight enough bit that the roof ahead
    /// doesn't swing away under the jump, and not chilled (spec B.2: slowed, a flat jump carries a fifth less, short of the
    /// far edge). Walkers who jumped off-centre from the top of an end ladder, or chilled, died between the cars.
    /// </summary>
    public static bool CanJumpGap(in PlayerState self, TrainOnLine train, ColdTuning? cold) =>
        Math.Abs(self.Position.X) < 0.3 && SteadyUnder(train, self.Parent)
        && (cold is null || self.Cold < cold.OnsetSeconds * (self.Has(PlayerFlags.Revived) ? cold.RevivedOnsetScale : 1));

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

/// <summary>
/// What every bot heeds whatever its job: "don't cross between cars rattling" (App. A.5). A bot whose next step would take
/// it into a gap the Rattle is rattling in stands where it is instead, and waits it out; one already in the gap when it
/// starts gets out of it, the quickest way it can. The rest of its intent (its hands, its look) goes through. Worked out
/// from what its client sees (the Rattle replicates), by stepping copies of itself.
/// </summary>
public static class Heed
{
    public static PlayerIntent Rattles(PlayerIntent intent, in PlayerState self, World world, PlayerTuning player)
    {
        if (!self.Alive)
            return intent;
        var train = world.Train;
        var rattling = world.ActiveEnemies.OfType<Rattle>().Where(r => r.Rattling).ToList();
        if (rattling.Count == 0)
            return intent;
        var now = PlayerMotor.WorldPosition(self, train);
        if (rattling.FirstOrDefault(r => r.InGap(now, train)) is { } around)
            return Out(intent, self, train, player, around);
        if (intent.MoveX == 0 && intent.MoveZ == 0)
            return intent;
        // A third of a second on, not just the next tick: what its client predicts isn't quite where the host has it (the
        // lag), and a car's end door opens straight onto the plate.
        var ahead = self;
        for (int i = 0; i < SimConstants.TickRate / 3; i++)
        {
            PlayerMotor.Step(ref ahead, intent, train, player, train.Dynamics.Tuning, SimConstants.TickSeconds, applyLook: false);
            var at = PlayerMotor.WorldPosition(ahead, train);
            if (rattling.Any(r => r.InGap(at, train)))
                return intent with { MoveX = 0, MoveZ = 0 };
        }
        return intent;
    }


    static Double3 After(in PlayerState self, in PlayerIntent intent, TrainOnLine train, PlayerTuning player)
    {
        var next = self;
        PlayerMotor.Step(ref next, intent, train, player, train.Dynamics.Tuning, SimConstants.TickSeconds, applyLook: false);
        return PlayerMotor.WorldPosition(next, train);
    }

    /// <summary>
    /// In the gap as it rattles: whichever way of walking takes it out of the gap soonest, or failing that furthest out to a
    /// side. Never a way off the train while it's going too fast to step down (spec B.3): off the plate at speed is worse.
    /// </summary>
    static PlayerIntent Out(PlayerIntent intent, in PlayerState self, TrainOnLine train, PlayerTuning player, Rattle r)
    {
        var frame = train.Frames[r.Attached];
        bool stepDown = !SpeedBands.JumpOffIsLethal(train.Dynamics.Tuning, train.Dynamics.Speed);
        PlayerIntent best = intent with { MoveX = 0, MoveZ = 0 };
        double bestScore = Math.Abs(frame.ToLocal(PlayerMotor.WorldPosition(self, train)).X);
        foreach (var (x, z) in new (float, float)[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            var tryIt = intent with { MoveX = x, MoveZ = z, Buttons = intent.Buttons | PlayerButtons.Run };
            var next = self;
            PlayerMotor.Step(ref next, tryIt, train, player, train.Dynamics.Tuning, SimConstants.TickSeconds, applyLook: false);
            // A third of a second on the same way: walking off the plate's edge shows by then.
            var ahead = next;
            bool falls = false;
            for (int i = 0; i < SimConstants.TickRate / 3 && !falls; i++)
            {
                PlayerMotor.Step(ref ahead, tryIt, train, player, train.Dynamics.Tuning, SimConstants.TickSeconds, applyLook: false);
                falls = ahead.Parent == PlayerState.World || ahead.Surface == Surface.Air;
            }
            if (!stepDown && self.Parent != PlayerState.World && falls)
                continue;
            var at = PlayerMotor.WorldPosition(next, train);
            double score = r.InGap(at, train) ? Math.Abs(frame.ToLocal(at).X) : 100;
            if (score > bestScore + 1e-4)
                (best, bestScore) = (tryIt, score);
        }
        return best;
    }
}
