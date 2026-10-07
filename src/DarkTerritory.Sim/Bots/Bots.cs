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
    /// <summary>Its own player id (its legs need it for what's in its hands).</summary>
    public int Me { get => _legs.Me; set => _legs.Me = value; }
    /// <summary>The rest of the crew as its client sees them (its legs need them to know whether the kit's theirs to bring).</summary>
    public IReadOnlyList<(int Id, PlayerState State)> Crew { get => _legs.Crew; set => _legs.Crew = value; }
    /// <summary>What it's doing about the repair kit, or null.</summary>
    public string? KitStep => _legs.KitStep;
    /// <summary>What its legs are doing about trouble in a car, and about the cold (for the harness's trace).</summary>
    public string? TendStep => _legs.TendStep;
    public string? WarmUpStep => _legs.WarmUpStep;
    /// <summary>
    /// The look-out's errand when there's no walker to send (a crew of two: driver and gunner; note 222), or null. Its legs
    /// run it, off the gun while the gun can spare it.
    /// </summary>
    public LookErrand? Errand { get => _legs.Errand; set => _legs.Errand = value; }

    public PlayerIntent Decide(in PlayerState self, TrainOnLine train, uint tick) => default;

    public PlayerIntent Decide(in PlayerState self, World world, uint tick, out PlayerState aimed)
    {
        var intent = Gun(self, world, tick, out aimed);
        // Off the gun for anything else, it gets up out of the seat first (T112: Jump, seated, is getting up).
        if (self.Has(PlayerFlags.Seated) && !_atGun)
            intent.Buttons |= PlayerButtons.Jump;
        return intent;
    }

    bool _atGun;

    PlayerIntent Gun(in PlayerState self, World world, uint tick, out PlayerState aimed)
    {
        aimed = self;
        _atGun = false;
        if (!self.Alive)
        {
            _legs.Work(self, world); // the dead still say so, or the driver waits all night for them to get aboard
            return default;
        }
        // A tunnel's mouth ahead: at the gun it's down behind the shield; anywhere else on the roofs, off them.
        // Trouble in a car: off the gun for it only while there's nothing at the back to shoot (hounds out).
        // From the first howl (the telegraph): back to the gun, it's a long walk from the front cars and the pack's closing.
        bool hounds = world.ActiveEnemies.Any(e => e.Kind == EnemyKind.CinderHound && !e.Gone && e.Phase is SpinePhase.Telegraph or SpinePhase.Commit or SpinePhase.Punish);
        // The mail cranes' bags are the walkers' to catch, not the gun's (note 299: with a bag always ahead the gunner went in
        // for each and never reached its gun all night); only with no other hand aboard but the driver does it go for them.
        bool alone = _legs.Crew.Count(c => c.State.Alive && c.Id != Me) < 2;
        _legs.Looked(world, self, safe: Guns.MannedGun(self, world.Train, guns) is not null, tend: !hounds, catches: alone);
        // T103 (T93 playtest: "so a cannon can be saved before decoupling a car"): a car the Car Hugger has hold of is a car
        // lost, cut loose or eaten through, and its gun with it: first, the gun pushed up the rail onto the car ahead.
        if (SaveGun(self, world) is { } saving)
            return saving;
        // The boiler ruptured and the kit's back down the train with nobody else to bring it (KitCarry): off the gun for it.
        if (_legs.Fetching(self, world))
            return _legs.Decide(self, world, tick, out aimed);
        // Nobody holds a gun through the cold (spec B.2): off it and indoors until warm, then back. Nor through a stop
        // they have a part in.
        if (_legs.Warming(self) || _legs.Work(self, world) is not null)
            return _legs.Decide(self, world, tick, out aimed);
        // The look-out in a crew of two (note 222): off the gun to look, only while the gun can spare it (nothing at the back
        // for it to shoot), and back to it once there's nothing left to look at from here.
        if (!hounds && _legs.Errand is { } errand && errand.Wants(self, world, tick))
            return _legs.Decide(self, world, tick, out aimed);
        // Hounds that got aboard can't be shot from the gun they're standing next to: off it, to the pack fight with the
        // rest (they stay aboard, note 269; or, with stayAboard off, get clear: they drop off once nobody's near), then walk
        // back to the guard car and take the gun again.
        bool houndsAboard = world.ActiveEnemies.Any(e => e.Kind == EnemyKind.CinderHound && e.Attached >= 0);
        // The Car Hugger on the rear car (v1.1 App. A.3): the gun's no answer to it (it's below the arc). Off the gun: down to
        // the guard van's rear platform to club it off (the legs do that), or with no platform to get at it from, up the
        // train, clear of its mouth, and let it take the car.
        bool rearHeld = world.ActiveEnemies.Any(e => e is CarHugger { Latched: true } h && h.Attached == world.Train.Dynamics.Consist.Vehicles[^1].Id);
        if (houndsAboard || rearHeld || Guns.MannedGun(self, world.Train, guns) is not { } gun)
        {
            if (rearHeld)
                _legs.Head(-1);
            else if (!houndsAboard)
                _legs.Head(+1);
            // On a car, on a ladder (the engine's too), or down on the ballast after a stop: the walker's way about, and
            // aboard. Only in the cab does it stand and wait.
            return self.Parent != 0 || self.Surface == Surface.Ladder ? _legs.Decide(self, world, tick, out aimed) : default;
        }
        // Hold fire once another burst would bring the swarm, and keep holding until they've dispersed
        // below the approach: firing again sooner resets the Choir's quiet clock and it never drops.
        // v1.1: the Choir gathering (the meter's held loud) or here: hush, so no shot now; and a cannon shot's loud enough to
        // tip a crew near the threshold over it.
        // A shot every few seconds keeps the meter loud (App. C.7), so it's what's gathered that says stop: past halfway, or
        // here, hold until it's all but drained.
        if (choir is not null && (world.Choir.Build > 0.45 || world.Choir.Present))
            _holding = true;
        else if (choir is null || world.Choir.Build <= 0.1 && !world.Choir.Present)
            _holding = false;
        // GDD v1.1 App. C.3: powder, ball, ram after every shot, before anything else (Use held at the gun).
        _atGun = true;
        // A fouled bore (note 183) the same way: Use held until it's clear.
        if (world.Train.Vehicles[gun].Gun is { ReloadNeeded: > 0, Ammo: > 0 } or { Jammed: true })
            return new PlayerIntent { Buttons = PlayerButtons.Use };
        // T112: only from the seat.
        var seat = self.Has(PlayerFlags.Seated) ? PlayerActions.None : PlayerActions.Seat;
        bool holdFire = _holding;
        var frame = world.Train.Frames[gun];
        var muzzle = frame.ToWorld(Guns.Mount(world.Train, gun)!.Value.Position);
        // What the gun answers (GDD §21), and anything holding a crewmate: a ball frees them as a friend's blow would (note 290).
        var target = world.ActiveEnemies.Where(e => e.Exposed && (e.GunAnswers || e.Phase == SpinePhase.Grab) && world.Enemies is not null)
            .Select(e => (e, offset: e.AimPoint(world.Train, world.Enemies!) - muzzle))
            .Where(x => x.offset.Length <= guns.Range)
            .OrderBy(x => x.offset.Length).FirstOrDefault();
        if (target.e is null)
            return new PlayerIntent { Actions = seat };
        var d = frame.DirToLocal(target.offset).Normalized;
        double yaw = DMath.Atan2(-d.X, -d.Z), pitch = DMath.Asin(d.Y);
        // Turn by look deltas, the way a player would; the gun follows at its own pace, and it fires once it's laid.
        var intent = new PlayerIntent { LookYaw = (float)Wrap(yaw - self.Yaw), LookPitch = (float)(pitch - self.Pitch), Actions = seat };
        aimed = self with { Yaw = self.Yaw + intent.LookYaw, Pitch = self.Pitch + intent.LookPitch };
        if (!holdFire && self.Has(PlayerFlags.Seated) && Guns.Laid(Guns.Mount(world.Train, gun)!.Value, world.Train.Vehicles[gun].Gun, d, guns))
            intent.Buttons = PlayerButtons.Fire;
        return intent;
    }

    static double Wrap(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }

    /// <summary>Pushing the gun off a held car (T103), and where that's got to (for the harness's trace).</summary>
    public bool Saving { get; private set; }

    /// <summary>
    /// On the roof of the rear car while the Car Hugger's latched on it, with the gun still on it and room on the car ahead's
    /// rail: behind the gun, facing up the train, and push it along (Use held, walking) until it's over the coupling.
    /// </summary>
    PlayerIntent? SaveGun(in PlayerState self, World world)
    {
        var train = world.Train;
        int rear = train.Dynamics.Consist.Vehicles[^1].Id;
        Saving = false;
        if (!self.Alive || self.Parent != rear || self.Surface != Surface.Roof || !train.Vehicles[rear].HasGun
            || !world.ActiveEnemies.Any(e => e is CarHugger { Latched: true } h && h.Attached == rear))
            return null;
        int ahead = train.VehicleAhead(rear);
        if (ahead < 0 || train.Vehicles[ahead].HasGun || train.Frames[ahead].Shape.RoofRail is null || Guns.Mount(train, rear) is not { } mount)
            return null;
        Saving = true;
        // Facing up the train (−Z): pushing it forward, from behind it.
        const double yaw = 0;
        double turn = Wrap(yaw - self.Yaw);
        if (Math.Abs(turn) > 0.1)
            return new PlayerIntent { LookYaw = (float)Math.Clamp(turn, -0.3, 0.3) };
        if (Guns.MannedGun(self, train, guns) != rear)
            return WarmUp.Steer(self, mount.Position with { Z = mount.Position.Z + 0.7, Y = self.Position.Y }, yaw).Step;
        return new PlayerIntent
        {
            MoveZ = 1,
            MoveX = (float)Math.Clamp((self.Position.X - mount.Position.X) * -0.8, -1, 1),
            Buttons = PlayerButtons.Use,
        };
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

    /// <summary>Its own player id (the session's), so it knows what's in its hands. Set by whoever runs it.</summary>
    public int Me { get; set; } = -1;

    /// <summary>The look-out's errand, on an insisted night only (the combination sweep's, note 212); null otherwise.</summary>
    public LookErrand? Errand { get; set; }

    uint _workedTick = uint.MaxValue;
    PlayerIntent? _work;
    bool _looked;

    /// <summary>The gunner has read the line for its legs this tick already (it knows whether it's at the gun).</summary>
    internal void Looked(World world, in PlayerState self, bool safe, bool tend = true, bool catches = true)
    {
        Look(world, self, safe, tend, catches);
        _looked = true;
    }

    /// <summary>This tick's intent for its part in a stop, if it has one to do now (worked out once a tick).</summary>
    public PlayerIntent? Work(in PlayerState self, World world)
    {
        // The dead still say so (their part's up for someone else): a walker who died mid warm-up would otherwise go on
        // holding the shunter's part, and the driver wait at the switch all night for it.
        if (job is not null && !self.Alive)
        {
            job.Decide(self, world);
            return null;
        }
        job?.Warming(self, _warm is { Active: true });
        if (job is null || _warm is { Active: true })
            return null;
        // Trouble in a car beats carrying crates: a crate hand (or one with no part) goes to it, and so does the winch pair
        // for a fire that's alight or a load that's loose (the loading waits; the car doesn't).
        if (_trouble is { } trouble && (job.Job is StopJob.Crates or StopJob.None && job.TakesTrouble
                || job.Job is StopJob.Winch0 or StopJob.Winch1 && trouble is CarFire { Phase: SpinePhase.Punish }))
        {
            // In there: work it from the aisle. Short of it at a stop: in by its side door from the ground (the stop's
            // hands are down there anyway); otherwise the walker's way, along the roofs.
            if (self.Parent == trouble.Attached && PlayerMotor.Indoors(self, world.Train))
                return Tend(self, trouble, world, Me);
            return job.IntoTrouble(self, world, trouble.Attached);
        }
        if (_workedTick != world.Tick)
        {
            _workedTick = world.Tick;
            _work = job.Decide(self, world);
        }
        return _work;
    }

    /// <summary>
    /// Reads the line ahead: the roof warning up (note 260: a tunnel's mouth, or a bend the train's taking too fast, coming,
    /// or the train still in it) means off the roofs and indoors (sight.json: the mouth takes anyone standing up there). <paramref name="safe"/>: where it
    /// stands is clear anyway (a gun's crew are down behind its shield).
    /// </summary>
    /// <param name="catches">Whether it goes in for the mail cranes' bags (the gunner leaves them to the walkers, note 299).</param>
    public void Look(World world, in PlayerState self, bool safe = false, bool tend = true, bool catches = true)
    {
        if (_warm is null)
            return;
        // The Choir (App. A.7) seizes anyone outside or behind no shut door, and a gun's shield is no shelter from it: past
        // halfway gathered (about 20 s off), or here, indoors with the doors shut first, the work in there after (note 305:
        // at a crew of two the driver can't leave the controls to break a seize, so a gunner in a car with its door open for
        // the fire it was working was half the crew, every night the Choir came).
        // Not in the safe yard or a fort, where it never comes (and a crew still to board would go nowhere).
        bool choir = !world.SafeYard && !world.TrainInFort && (world.Choir.Present || world.Choir.Build >= ChoirShelterAt);
        _warm.Shelter = !safe && RoofWarned(world) || choir;
        _warm.ShutFirst = choir;
        // Trouble inside a car: in to it, and work it from the aisle, unless it's too much for us (hurt, get out).
        // The nearest to us, so a crew splits up over them; a fire first (it spreads), then a load (it's on a clock).
        int here = self.Parent;
        // Too hurt to take it on (health doesn't come back out here): leave it to someone else.
        tend &= self.Health >= TooHurt;
        var train0 = world.Train;
        // A car that's all but gone up isn't one to walk into: let it burn out.
        // Fire Flies swarming a car's lamp are trouble too (v1.1 App. A.5): in there, the lamp out, before the car catches.
        _trouble = tend ? world.ActiveEnemies.Where(e => !e.Gone && e.Attached > 0 && (e is Incident && !(e is CarFire && e.Extra > 0.85 && e.Attached != here)
                || e is FireFlies && e.Attached < train0.Vehicles.Count && train0.Vehicles[e.Attached].LampLit))
            .OrderBy(e => Covered(e, here) ? 1 : 0).ThenBy(e => e is CarFire ? 0 : 1).ThenBy(e => Math.Abs(e.Attached - here))
            .ThenBy(e => e.Id).FirstOrDefault() : null;
        var train = world.Train;
        // Otherwise a bag on a crane ahead, its board read: into a car with a side door that side, and the hook out.
        // Not with the Choir about: the hook goes out through an open side door.
        _drop = tend && catches && !choir && _trouble is null ? NextDrop(world) : null;
        _catchCar = _drop is { } d ? CatchCar(train, d, self.Parent) : null;
        _warm.Into = _trouble?.Attached ?? _catchCar;
        if (_trouble is { } trouble)
            _warm.Indoors = s => Tend(s, trouble, world, Me);
        else if (_drop is { } drop && _catchCar is { } car && world.Lineside is { } lineside)
            _warm.Indoors = s => CatchAt(s, drop, car, train, lineside);
        else
            _warm.Indoors = null;
    }

    /// <summary>
    /// Trouble in another car that crewmates are already in to work: two for a fire (it can want more than one extinguisher,
    /// GDD §21 "a big one takes several extinguishers at once"), one for anything else (a lamp to put out, a load). It's
    /// left to them while there's trouble nobody's in to. Everyone made for the same fire, the nearest, and Fire Flies on
    /// a lamp three cars off set that car alight while five of them stood in the first (frontier:7, note 188).
    /// </summary>
    bool Covered(Enemy trouble, int here)
    {
        if (trouble.Attached == here)
            return false;
        int inside = 0;
        foreach (var (_, c) in Crew)
            if (c.Alive && c.Parent == trouble.Attached && c.Surface == Surface.Deck)
                inside++;
        return inside >= (trouble is CarFire ? 2 : 1);
    }

    Enemy? _trouble;
    Sim.Route.Drop? _drop;
    int? _catchCar;

    /// <summary>The rest of the crew as its client sees them, by player id (who goes for the kit: <see cref="KitCarry"/>). Set by whoever runs it.</summary>
    public IReadOnlyList<(int Id, PlayerState State)> Crew { get; set; } = [];

    /// <summary>What it's doing about the repair kit, or null (for tests and the harness's trace).</summary>
    public string? KitStep { get; private set; }

    /// <summary>It carried the kit to the cab, and hasn't yet got back up onto the roofs.</summary>
    bool _brought;
    /// <summary>Last tick, it was on its way down off the roofs for the kit.</summary>
    bool _sentDown;

    /// <summary>Whether the kit's its to bring this tick, or it's on the way back from bringing it (the gunner leaves its gun for it).</summary>
    public bool Fetching(in PlayerState self, World world) =>
        self.Alive && (_brought || self.Has(PlayerFlags.RepairKit)
            || world.Train.Boiler.Ruptured && KitCarry.Lying(world) is { } kit && KitCarry.Mine(self, Me, Crew, world.Train, kit, _sentDown));

    /// <summary>
    /// The boiler's ruptured and the kit lies further back than car 1 (GDD §12, §23; note 186): if it's this walker's to
    /// bring, the way to it and forward with it (<see cref="KitCarry"/>). Up on the roofs, the walker's own way down
    /// (<see cref="WarmUp"/>, sent into the car ahead of the kit's, whose rear plate is the one in front of it), along the
    /// roofs to it first.
    /// </summary>
    PlayerIntent? Kit(in PlayerState self, World world, uint tick)
    {
        if (!self.Alive)
            _brought = false;
        else if (self.Has(PlayerFlags.RepairKit) && world.Train.Boiler.Ruptured)
            _brought = true;
        else if (self.Parent > 0 && self.Surface == Surface.Roof)
            _brought = false;
        var intent = KitCarry.Decide(self, world, Me, Crew, tick, _brought, _sentDown, out int? down);
        if (intent is not null)
        {
            KitStep = self.Has(PlayerFlags.RepairKit) ? "carrying" : world.Train.Boiler.Ruptured ? "fetching" : "back";
            _warm?.Abandon();
            return intent;
        }
        KitStep = null;
        // Sent down for it, and now it's someone else's (they came nearer, or got down first): back to the roofs.
        if (down is null && _sentDown)
            _warm?.Abandon();
        _sentDown = false;
        if (down is { } car && _warm is not null && self.Parent > 0)
        {
            KitStep = "down";
            _sentDown = true;
            _trouble = null;
            _drop = null;
            _catchCar = null;
            _warm.Into = car;
            _warm.Indoors = null;
            var consist = world.Train.Dynamics.Consist;
            int here = consist.IndexOf(self.Parent), ahead = consist.IndexOf(car);
            if (here > ahead + 1)
                Head(-1);
            else if (here < ahead)
                Head(+1);
        }
        return null;
    }

    /// <summary>Seconds ahead of a crane a walker goes in for its bag.</summary>
    const double CatchAhead = 45;

    /// <summary>The next crane ahead whose board has been read and that's near enough to go in for.</summary>
    static Sim.Route.Drop? NextDrop(World world)
    {
        if (world.Lineside is not { } lineside)
            return null;
        var d = world.Train.Dynamics;
        double reach = Math.Max(d.Speed, 3) * CatchAhead;
        return lineside.Signs.Where(s => s.Kind == Sim.Route.SignKind.Drop && lineside.Read(s.Id) && s.Drop is { } drop
                && !lineside.Passed(drop.Id) && drop.At > d.RearDistance && drop.At - d.Distance <= reach)
            .Select(s => s.Drop).FirstOrDefault();
    }

    /// <summary>The cargo car nearest to where we are with a side door on the crane's side.</summary>
    static int? CatchCar(TrainOnLine train, Sim.Route.Drop drop, int from) =>
        train.Dynamics.Consist.Vehicles.Where(v => v.Kind == VehicleKind.Cargo && StopHand.SideDoor(train.Frames[v.Id].Shape, drop.Side) is not null)
            .OrderBy(v => Math.Abs(v.Id - Math.Max(from, 1))).Select(v => (int?)v.Id).FirstOrDefault();

    /// <summary>At the side door on the crane's side: open it, stand in it facing out, and hook the bag as the car goes by.</summary>
    static PlayerIntent? CatchAt(in PlayerState self, Sim.Route.Drop drop, int car, TrainOnLine train, Sim.Route.Lineside lineside)
    {
        if (lineside.Passed(drop.Id) || drop.At < train.Dynamics.RearDistance - 2 || self.Parent != car)
            return null;
        var shape = train.Frames[car].Shape;
        if (StopHand.SideDoor(shape, drop.Side) is not { } door)
            return null;
        var (at, yaw) = WarmUp.Inside(shape, door);
        var (step, there) = WarmUp.Steer(self, at, yaw);
        if (!there)
            return step;
        if (!train.Vehicles[car].DoorOpen(door))
            return new PlayerIntent { Buttons = PlayerButtons.Use };
        double doorAt = train.Cars[car].FrontDistance - shape.HalfLength;
        return Math.Abs(doorAt - drop.At) < 40 ? new PlayerIntent { Buttons = PlayerButtons.Fire } : new PlayerIntent();
    }

    /// <summary>Hurt this badly, a walker leaves the trouble to someone else and gets out.</summary>
    const int TooHurt = 35;

    /// <summary>
    /// In the burning car (v1.1 App. C.5): the car's extinguisher off its mount if it's not in hand (walk to it, Use), then
    /// along the aisle to beside the fire, facing along the car, and spray (Fire held). Its charge gone, put it down and
    /// leave it to recharge. Use is a tap, every <see cref="KitRun.TapEvery"/> ticks by the bot's own tick: the hands take
    /// the press (its edge), and held from a tick it didn't take (still turning to look down at it), it never took it, and
    /// a walker stood at the extinguisher, or with a spent one in its hands, while the car burned (note 188).
    /// </summary>
    PlayerIntent? Tend(in PlayerState self, Enemy trouble, World world, int me)
    {
        var intent = Fight(self, trouble, world, me);
        // And in a burning car, its lamp out (GDD §21 "lamps off when they swarm"): the fire's most likely the Fire Flies', who
        // go once it's alight and leave the lamp lit for the next swarm. frontier:7 and :3's crews put car 2's fire out and
        // left it lit, and the flies came back to it again and again (note 188). A tap by the bot's own tick, every
        // KitRun.TapEvery, until it's out: the press toggles it, and the snapshot that says so is a few ticks behind.
        if (intent is { } fighting && trouble is CarFire && world.Train.Vehicles[self.Parent].LampLit && _tick % KitRun.TapEvery == 0)
            return fighting with { Actions = fighting.Actions | PlayerActions.CarLamp };
        return intent;
    }

    PlayerIntent? Fight(in PlayerState self, Enemy trouble, World world, int me)
    {
        var train = world.Train;
        TendStep = null;
        if (trouble.Gone || self.Parent != trouble.Attached || self.Health < TooHurt)
            return null;
        bool tap = _tick % KitRun.TapEvery == 0;
        // Fire Flies on the lamp: put it out (a press every other tick until it's out; the press is what the host counts).
        if (trouble is FireFlies)
        {
            TendStep = "lamp";
            return train.Vehicles[self.Parent].LampLit && world.Tick % 2 == 0 ? new PlayerIntent { Actions = PlayerActions.CarLamp } : new PlayerIntent();
        }
        var held = world.Bodies.CarriedBy(me);
        if (held is { Kind: Physics.BodyKind.Extinguisher, Charge: <= 0.01 })
        {
            TendStep = "spent";
            return new PlayerIntent { Buttons = tap ? PlayerButtons.Use : PlayerButtons.None }; // down it goes
        }
        if (held is not { Kind: Physics.BodyKind.Extinguisher })
        {
            int car = self.Parent;
            var here = self.Position;
            // One in the room, and not one it's walked at for a while without getting to it: put down spent by a side door it
            // can lie out on the steps (the car's still, it's in its frame), and five walkers walked at the wall for it, 250 s,
            // at a deepTerritory:2 colliery with the car alight (note 188).
            var room = train.Frames[car].Shape.Interior;
            var ext = world.Bodies.All.Where(b => b.Kind == Physics.BodyKind.Extinguisher && b.Carrier < 0 && b.Parent == car && b.Charge > 0.2
                    && room is { } r && b.Centre.X > r.Min.X && b.Centre.X < r.Max.X && b.Centre.Z > r.Min.Z && b.Centre.Z < r.Max.Z
                    && !(_unreachable.TryGetValue(b.Id, out uint until) && _tick < until))
                .OrderBy(b => (b.Centre - here).Length).FirstOrDefault();
            if (ext is null)
                return null; // nothing to fight it with here
            // Stood beside it, facing it: in the hands' reach. Where there's floor to stand on: just aft of it, as it always
            // was, is where car 1's crew lockers stand (note 173: three walkers walked at them for 25 s with the car alight
            // round them), and in from it, put down in the aisle, is the cargo's stack.
            if (TakeFrom(train, car, ext.Centre, here) is not { } spot)
            {
                _unreachable[ext.Id] = _tick + (uint)(ReachAgain * SimConstants.TickRate);
                return null;
            }
            var (walk, at) = WarmUp.Steer(self, spot.At, spot.Yaw);
            // Where it's fetched up (a fitting may hold it off the spot by a hair), the way to face from there.
            if (at && Facing(train, car, self.Position, ext.Centre, self.Yaw) is { } face && Math.Abs(Math.IEEERemainder(face - self.Yaw, 2 * Math.PI)) > 0.05)
            {
                at = false;
                walk = new PlayerIntent { LookYaw = (float)Math.Clamp(Math.IEEERemainder(face - self.Yaw, 2 * Math.PI), -0.5, 0.5) };
            }
            TendStep = at ? "taking" : "to the extinguisher";
            // Walking at it this long and not there, or there this long and not taking it: something's in the way. Another, or
            // none, for a while.
            _reaching = _reaching.Body == ext.Id ? (ext.Id, _reaching.Ticks + 1) : (ext.Id, 0);
            if (_reaching.Ticks > ReachGiveUp * SimConstants.TickRate)
            {
                _unreachable[ext.Id] = _tick + (uint)(ReachAgain * SimConstants.TickRate);
                _reaching = default;
            }
            return at ? new PlayerIntent { Buttons = tap ? PlayerButtons.Use : PlayerButtons.None, LookPitch = (float)(-0.6 - self.Pitch) } : walk;
        }
        // The fire's cells (note 267): the extinguisher puts out the cell it's aimed at, so the burning cell nearest, from the
        // aisle a little short of it, looked at. Working in from the near edge, the walker's never stood in what it's left.
        var target = new Double3(Interior(train).DoorX, 0, trouble.Local.Z);
        if (trouble is CarFire { Heat.Length: > 0 } fire && FireGrid.Of(train, trouble.Attached, world.Enemies?.CarFire.CellSize ?? 1.5) is { } grid
            && grid.Count == fire.Heat.Length)
        {
            int near = -1;
            double best = double.MaxValue;
            for (int i = 0; i < grid.Count; i++)
            {
                double d = Math.Abs(grid.Centre[i].Z - self.Position.Z) - fire.Heat[i] * 0.5;
                if (fire.Heat[i] > 0 && d < best)
                {
                    best = d;
                    near = i;
                }
            }
            if (near >= 0)
                target = grid.Centre[near];
        }
        double side = self.Position.Z > target.Z ? 1 : -1;
        double standZ = target.Z + side * 1.2;
        if (train.Frames[self.Parent].Shape.Interior is { } rm)
            standZ = Math.Clamp(standZ, rm.Min.Z + 0.4, rm.Max.Z - 0.4);
        var aisle = new Double3(Interior(train).DoorX, 0, standZ);
        double yaw = side > 0 ? 0 : Math.PI;
        // There once it's near the spot, whichever way it faces: it turns to the cell, not along the car.
        var (step, _) = WarmUp.Steer(self, aisle, yaw);
        double ox = aisle.X - self.Position.X, oz = aisle.Z - self.Position.Z;
        bool there = ox * ox + oz * oz < 0.4 * 0.4;
        TendStep = there ? "spraying" : "to the fire";
        if (!there)
            return step;
        // Look at it: the spray goes from the eye along the look (Bookmarks.Forward).
        var d3 = target - (self.Position + Double3.Up * train.Dynamics.Tuning.Pick.EyeHeight);
        double wantYaw = DMath.Atan2(-d3.X, -d3.Z), wantPitch = DMath.Atan2(d3.Y, Math.Sqrt(d3.X * d3.X + d3.Z * d3.Z));
        return new PlayerIntent
        {
            Buttons = PlayerButtons.Fire,
            LookYaw = (float)Math.Clamp(Math.IEEERemainder(wantYaw - self.Yaw, 2 * Math.PI), -0.5, 0.5),
            LookPitch = (float)Math.Clamp(wantPitch - self.Pitch, -0.5, 0.5),
        };
    }

    static InteriorLayout Interior(TrainOnLine train) => train.Dynamics.Tuning.Geometry.Interior!;

    /// <summary>Room a walker wants round where it stands (m): about a crewmate's radius (player.json 0.3).</summary>
    const double Clearance = 0.3;

    /// <summary>
    /// Where to stand to take something lying at <paramref name="at"/> on a car's floor, and the way to face: round it, a
    /// little off, nearest <paramref name="from"/> first, wherever a crewmate fits clear of the car's fittings and its shut
    /// doors (the crew lockers, the cargo's stack, the van's tool store), facing so the hands reach it and nothing the car
    /// has is in reach to take the press instead (the hands only take Use with nothing else to work there: facing car 1's
    /// crew lockers, or an end door, Use was theirs). Null if nowhere.
    /// </summary>
    public static (Double3 At, double Yaw)? TakeFrom(TrainOnLine train, int car, Double3 at, Double3 from)
    {
        var shape = train.Frames[car].Shape;
        if (shape.Interior is not { } room || train.Dynamics.Tuning.Geometry.Interior is not { } layout)
            return null;
        double floor = layout.FloorHeight, r = Clearance;
        var spots = new List<Double3>();
        foreach (double off in new[] { 0.5, 0.7 })
            for (int k = 0; k < 8; k++)
                spots.Add(new Double3(at.X + off * DMath.Cos(k * Math.PI / 4), floor, at.Z + off * DMath.Sin(k * Math.PI / 4)));
        foreach (var p in spots.OrderBy(p => (p.X - from.X) * (p.X - from.X) + (p.Z - from.Z) * (p.Z - from.Z)))
        {
            if (p.X - r < room.Min.X || p.X + r > room.Max.X || p.Z - r < room.Min.Z || p.Z + r > room.Max.Z)
                continue;
            bool Hits(Box b) => b.Max.Y > floor + 0.35 && b.Min.Y < floor + 1.8 && p.X + r > b.Min.X && p.X - r < b.Max.X && p.Z + r > b.Min.Z && p.Z - r < b.Max.Z;
            if (shape.Solids.Any(solid => Hits(solid.Box)) || shape.DoorList.Any(door => Hits(door.Box)))
                continue;
            if (Facing(train, car, p, at, DMath.Atan2(p.X - at.X, p.Z - at.Z)) is { } face)
                return (p with { Y = 0 }, face);
        }
        return null;
    }

    /// <summary>
    /// Stood at <paramref name="stood"/>, a way to face (<paramref name="first"/> if it'll do, else the nearest to it of
    /// sixteen) with the hands in reach of <paramref name="at"/> and nothing of the car's in reach to take the press. Null if
    /// none.
    /// </summary>
    static double? Facing(TrainOnLine train, int car, Double3 stood, Double3 at, double first)
    {
        for (int k = 0; k <= 16; k++)
        {
            double face = first + (k % 2 == 0 ? 1 : -1) * ((k + 1) / 2) * Math.PI / 8;
            double hx = stood.X - DMath.Sin(face) * HandsForward - at.X, hz = stood.Z - DMath.Cos(face) * HandsForward - at.Z, hy = stood.Y + HandsUp - at.Y;
            var s = new PlayerState { Parent = car, Position = stood, Yaw = face, Surface = Surface.Deck };
            if (hx * hx + hz * hz + hy * hy <= HandsReach * HandsReach && CrewActions.NearestInteractable(s, train) is null)
                return Math.IEEERemainder(face, 2 * Math.PI);
        }
        return null;
    }

    /// <summary>Where the hands take from (Bodies.Hands: 0.6 m ahead, 1.15 m up, 1.6 m reach), with a little to spare.</summary>
    const double HandsForward = 0.6, HandsUp = 1.15, HandsReach = 1.45;
    /// <summary>Seconds a walker goes for one extinguisher without taking it before it leaves it, and for how long (s).</summary>
    const double ReachGiveUp = 12, ReachAgain = 60;
    (int Body, int Ticks) _reaching;
    readonly Dictionary<int, uint> _unreachable = [];

    /// <summary>What it's doing about the trouble in the car it's in (for the harness's trace), or null.</summary>
    public string? TendStep { get; private set; }

    /// <summary>The bot's own tick (taps go by it, not the client world's: <see cref="KitRun.TapEvery"/>).</summary>
    uint _tick;

    /// <summary>A crewmate this near (m, out of the cab) can pull a walker from the Car Hugger's mouth (note 305).</summary>
    const double PullReach = 10;

    /// <summary>How far the Choir's gathered (0 to 1, about 40 s to come) when a crew bot gets behind a shut door.</summary>
    const double ChoirShelterAt = 0.5;

    /// <summary>
    /// Note 260: the roof warning is up (<see cref="Sim.Route.Lineside.Warning"/>), a tunnel's mouth or a bend taken too fast
    /// coming: the same warning a player gets, from the same route and train, lamp or no lamp. It was a posted tunnel within
    /// 40 s, its board read, so in the dark a walker learned of the mouth 10 m short.
    /// </summary>
    public static bool RoofWarned(World world) => world.Lineside?.Warning(world.Train) is not null;

    public PlayerIntent Decide(in PlayerState self, World world, uint tick, out PlayerState aimed)
    {
        aimed = self;
        _tick = tick;
        TendStep = null;
        // Warming up means a coupler plate, so it keeps to the gaps no Rattle's in (App. A.5), and out of a car a Climber's
        // got into (App. A.4): its client can see both.
        if (_warm is not null)
        {
            _warm.Barred = car => world.ActiveEnemies.Any(e => e is Climber { Inside: true } c && c.Attached == car
                || e is CarHugger { Latched: true } h && h.Attached == car || e is Whistler w && !w.Gone && w.Attached == car);
            _warm.Troubled = car => world.ActiveEnemies.Any(e => !e.Gone && e.Attached == car && e is CarFire { Phase: SpinePhase.Punish });
            _warm.LeaveOpen = door => door is 0 or 1 && world.Train.Boiler.Ruptured;
            _warm.Passing = (car, door) => AtTheDoor(world.Train, Crew, car, door);
        }
        if (!_looked)
            Look(world, self);
        _looked = false;
        // The look-out on an insisted night (note 212): a Dragger to meet keeps it out on the roofs, not in for a bag.
        if (Errand is { KeepOut: true } && _trouble is null && _warm is not null)
        {
            (_drop, _catchCar) = (null, null);
            _warm.Into = null;
            _warm.Indoors = null;
        }
        if (Kit(self, world, tick) is { } bringing)
            return bringing;
        int? lookAt = null;
        if (_warm is not { Active: true } && Errand?.Decide(self, world, tick, out lookAt) is { } looking)
            return looking;
        if (Work(self, world) is { } working)
            return working;
        var train = world.Train;
        // The Car Hugger on the car we're on, and it has a platform to get at it from (v1.1 App. A.3): down and club it off.
        // Only with someone near enough to pull us from its mouth (A.3: "swallowSeconds for friends to pull them free",
        // and one player can barely out-hit it). Alone but for the driver, a crew of two's gunner went down to it and was
        // eaten on every Dead Lines night it came (note 305): clear of it instead (below), and it may take the car.
        int on = self.Parent;
        var here = PlayerMotor.WorldPosition(self, train);
        bool pulled = Crew.Any(c => c.Id != Me && c.State.Alive && !PlayerMotor.InCab(c.State, train)
            && (PlayerMotor.WorldPosition(c.State, train) - here).Length <= PullReach);
        if (self.Alive && on > 0 && on < train.Frames.Count && _warm is not { Active: true } && train.Frames[on].Shape.Platform is { } platform
            && pulled && world.ActiveEnemies.Any(e => e is CarHugger { Latched: true } h && h.Attached == on))
            return Beat(self, train, platform, tick);
        if (self.Alive && self.Parent > 0 && self.Parent < train.Frames.Count)
        {
            int parent = self.Parent;
            // A Dragger's limb over the lip at us (v1.1 App. A.4): step back to the centreline (the rule), and take a swing at it.
            var at = self.Position;
            if (self.Surface == Surface.Roof && world.ActiveEnemies.OfType<Dragger>().FirstOrDefault(d => d.Phase == SpinePhase.Telegraph && d.Attached == parent
                    && Math.Sign(at.X + 1e-9) == d.Side && Math.Abs(at.Z - d.Local.Z) < 2) is { } limb)
                return new PlayerIntent { MoveX = (float)(-Math.Sign(at.X) * DMath.Cos(self.Yaw)), Actions = PlayerActions.Swing };
            // A Climber making for a gap at either end of this car (App. A.4): hold it (T65).
            if (self.Surface == Surface.Roof && _warm is not { Active: true } && Hold(self, world) is { } holding)
                return holding;
            // Trouble (or a bag to catch) in another car: head along the roofs for it (in through its door when we're there).
            if ((_trouble?.Attached ?? _catchCar) is { } goal && goal != parent && _warm is { Active: false } && self.Surface == Surface.Roof)
                _direction = goal < parent ? -1 : 1;
            // Or the lip over a Dragger the look-out's making for, on another car (note 212).
            else if (lookAt is { } lip && lip != parent && _warm is not { Active: true } && self.Surface == Surface.Roof)
                _direction = lip < parent ? -1 : 1;
            // Hounds aboard. They stay, setting the car alight (note 269): the fit go along the roofs at them together (Heed.Hounds
            // swings once they're close); the hurt keep clear. Where they drop off when bored (stayAboard off), nobody goes near
            // them, and anyone close walks away.
            if (world.ActiveEnemies.FirstOrDefault(e => e.Kind == EnemyKind.CinderHound && !e.Gone && e.Attached >= 0) is { } aboard)
            {
                if (world.Enemies?.CinderHounds.StayAboard == true && self.Health >= Heed.PackFightHealth)
                {
                    if (aboard.Attached != parent)
                        _direction = aboard.Attached < parent ? -1 : 1;
                }
                else if (aboard.Attached >= parent - 1)
                    _direction = -1;
            }
            // The Car Hugger on a car with no platform to club it from: off that car and the one ahead of it, toward the engine,
            // clear of its mouth. It may take the car.
            int mine = train.Dynamics.Consist.IndexOf(parent);
            if (mine >= 0 && world.ActiveEnemies.Any(e => e is CarHugger { Latched: true } h && train.Dynamics.Consist.IndexOf(h.Attached) is var held && held >= 0 && mine >= held - 1))
                _direction = -1;
        }
        return Decide(self, train, tick);
    }

    static double Wrap(double a) => Math.IEEERemainder(a, 2 * Math.PI);

    /// <summary>Inside a car this near one of its doors and moving, a crewmate is going through it (m, m/s).</summary>
    const double AtDoor = 2, Moving = 0.3;

    /// <summary>Any of <paramref name="crew"/> inside <paramref name="car"/> going through that door (<see cref="WarmUp.Passing"/>).</summary>
    public static bool AtTheDoor(TrainOnLine train, IReadOnlyList<(int Id, PlayerState State)> crew, int car, int door)
    {
        if (car <= 0 || car >= train.Frames.Count)
            return false;
        foreach (var i in train.Frames[car].Shape.Interactables)
        {
            if (i.Kind != InteractableKind.Door || i.Index != door)
                continue;
            foreach (var (_, c) in crew)
            {
                // On the move: one stood by it is warming there (having shut it), and counting them, nobody ever shut it.
                double dx = c.Position.X - i.Position.X, dz = c.Position.Z - i.Position.Z;
                double vx = c.Velocity.X, vz = c.Velocity.Z;
                if (c.Alive && c.Parent == car && dx * dx + dz * dz < AtDoor * AtDoor && vx * vx + vz * vz > Moving * Moving)
                    return true;
            }
        }
        return false;
    }

    /// <summary>A Climber running alongside this far (m) from a gap is plainly making for it.</summary>
    const double ClimberMakingFor = 20;

    /// <summary>
    /// Climbers (App. A.4): one running alongside for a gap at an end of the car we're on, or scrabbling at it ("visible
    /// from adjacent roofs"): to that roof end, over the gap, and stand there. Someone on a roof end within reach holds the
    /// mount point: it drops back and tries another, which is often this car's other end, and the walker goes there too.
    /// Whoever's on the cars either side holds it; nobody runs the train's length to (the guns take it on the roofs).
    /// </summary>
    static PlayerIntent? Hold(in PlayerState self, World world)
    {
        var train = world.Train;
        int car = self.Parent;
        double l = train.Frames[car].Shape.HalfLength;
        double? best = null;
        foreach (var c in world.ActiveEnemies.OfType<Climber>())
        {
            if (c.Gone || c.Gap <= 0 || c.Gap >= train.Frames.Count)
                continue;
            bool coming = c.Phase == SpinePhase.Telegraph
                || c.Phase == SpinePhase.Dormant && Math.Abs(c.LineDistance - Climber.GapAlong(train, c.Gap)) <= ClimberMakingFor;
            if (!coming)
                continue;
            // This car's back end (the gap behind it), or its front end (the gap behind the car ahead).
            double? end = c.Gap == car ? l - 0.6 : train.VehicleBehind(c.Gap) == car ? -l + 0.6 : null;
            if (end is { } z && (best is null || Math.Abs(z - self.Position.Z) < Math.Abs(best.Value - self.Position.Z)))
                best = z;
        }
        if (best is not { } at)
            return null;
        double facing = at > 0 ? Math.PI : 0;
        var (step, there) = WarmUp.Steer(self, new Double3(0, 0, at), facing);
        return there ? new PlayerIntent() : step;
    }

    /// <summary>Seconds a swing takes: Use down this long, then up as long (each press is one blow, App. A.3's "~5 hits").</summary>
    const double SwingSeconds = 0.35;

    /// <summary>
    /// Off the roof's back end by the platform's ladder (down, no hands: Use with a pull is letting go), across the grating
    /// to over the coupling it's hanging on, and swing at it, press by press, until it lets go. Then up the ladder, as out
    /// of any gap.
    /// </summary>
    static PlayerIntent Beat(in PlayerState self, TrainOnLine train, Box platform, uint tick)
    {
        double l = platform.Min.Z;
        switch (self.Surface)
        {
            case Surface.Ladder:
                return new PlayerIntent { MoveZ = -1 };
            case Surface.Roof:
                {
                    // To just short of the edge over the ladder, facing back down the line, then push on and take hold.
                    var (step, there) = WarmUp.Steer(self, new Double3(train.Dynamics.Tuning.Geometry.EndLadderX, 0, l - 0.35), Math.PI);
                    return there ? new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Use } : step;
                }
            case Surface.Coupler:
                {
                    // Over the coupling, clear of the rear door's reach (a press there would work the door) and off the ladder's foot.
                    var (step, there) = WarmUp.Steer(self, new Double3(train.Dynamics.Tuning.Geometry.EndLadderX + 0.45, 0, l + 0.5), Math.PI);
                    if (!there)
                        return step;
                    // Swinging at it (v1.1 App. C.2): the tool swings as often as it recovers.
                    return new PlayerIntent { Actions = PlayerActions.Swing };
                }
            default:
                return default;
        }
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
        // T81: a posted tunnel's mouth near (or the train in it), and off the roofs already: stay off them. On a plate, in a
        // car, or on the way down, it's clear; it's the roof the mouth takes. deepTerritory:2's three walkers, turned away from
        // a car a Climber had, climbed straight back up the ladder from the plate into it.
        if (_warm is { Shelter: true } && self.Parent > 0)
        {
            if (self.Surface == Surface.Ladder)
                return new PlayerIntent { MoveZ = -1 };
            if (self.Surface is Surface.Coupler or Surface.Deck)
                return new PlayerIntent();
        }
        // On a car's floor (in from the cold, or knocked in): out through the nearer door, then up the end ladder.
        if (self.Surface == Surface.Deck && self.Parent > 0 && _warm is not null && _warm.Leave(self, train) is { } leaving)
            return leaving;
        // Mid-climb: keep going up.
        if (self.Surface == Surface.Ladder)
            return new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Use };
        // Fell into a coupling gap: over to the foot of the end ladder (the plate is narrow, the ladder's just off its
        // right edge), facing it, then take hold and climb. Use here is only ever with a push: standing, it cuts the coupling.
        if (self.Surface == Surface.Coupler)
        {
            double l = train.Frames[self.Parent].Shape.Bounds.Max.Z;
            var g = train.Dynamics.Tuning.Geometry;
            var (step, there) = WarmUp.Steer(self, new Double3(g.PlateX + g.CouplerWidth / 2 - 0.2, 0, l + 0.45), 0);
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
            if (WarmUp.CanJumpGap(self, train, cold, beyond))
                intent.Buttons |= PlayerButtons.Jump;
            else
            {
                // Not a jump to make from here (off the centreline, a curve pulling the roof away, or too cold to run at it):
                // stand at the end and square up, and chilled, turn back rather than try it.
                intent.MoveZ = 0;
                if (cold is { } c && self.Cold >= c.OnsetSeconds)
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
    /// catch a train at a stand or a crawl this way, not one at speed: one moving off more than <see cref="ChaseFor"/> away
    /// is let go, but one standing is walked to from however far (and run to, when it's far). The cap once held for a
    /// standing train too: a walker that fell off backing out of a spur (T121) watched the train go on past 60 m to the main line
    /// and stop there for it, and stood on the ballast until the driver gave up on it and left.
    /// </summary>
    static PlayerIntent Board(in PlayerState self, TrainOnLine train)
    {
        Double3? best = null;
        double bestD = double.MaxValue;
        int bestCar = -1;
        foreach (var frame in train.Frames)
        {
            if (frame.Index == 0)
                continue; // the engine is boarded by its crew
            // Nor a switchyard's cars still standing on their siding (note 187): they're not the train (yet).
            if (train.StandingCar(frame.Index))
                continue;
            foreach (var ladder in frame.Shape.Ladders)
            {
                // Side ladders from the ground: stand just outside the foot.
                if (Math.Abs(ladder.Inward.X) < 0.9 || ladder.Foot.Y > 0.5)
                    continue;
                var at = frame.ToWorld(ladder.Foot - ladder.Inward * 0.3);
                double lx = at.X - self.Position.X, lz = at.Z - self.Position.Z, d = Math.Sqrt(lx * lx + lz * lz);
                if (d < bestD)
                {
                    bestD = d;
                    best = at;
                    bestCar = frame.Index;
                }
            }
        }
        if (best is { } && bestD > ChaseFor && Math.Abs(train.RakeOf(bestCar).Velocity) > StandingStill)
            best = null;
        if (best is not { } target || !self.Grounded)
            return default;
        double dx = target.X - self.Position.X, dz = target.Z - self.Position.Z;
        double yaw = DMath.Atan2(-dx, -dz);
        double turn = WrapAngle(yaw - self.Yaw);
        bool aligned = Math.Abs(turn) < 0.15;
        return new PlayerIntent
        {
            LookYaw = (float)Math.Clamp(turn, -0.4, 0.4),
            MoveZ = aligned ? 1 : 0,
            // At the foot, push on and take hold: it climbs from there as out of any gap. Far off, run (a standing train is
            // waiting on us).
            Buttons = bestD < 0.6 ? PlayerButtons.Use : bestD > RunFrom ? PlayerButtons.Run : PlayerButtons.None,
        };
    }

    /// <summary>m: a train moving off is chased this far on foot, and no further.</summary>
    const double ChaseFor = 60;
    /// <summary>m/s: a rake slower than this is standing (waiting for whoever's down).</summary>
    const double StandingStill = 0.05;
    /// <summary>m: further than this from a ladder, a walker runs for it.</summary>
    const double RunFrom = 6;
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
    public string Name => Fireman ? "fireman" : "conductor";
    public double CruiseSpeed { get; init; } = 14;
    /// <summary>Cruise with the lamp out: just under the Sleepers' derailing speed (enemies.json, 11.1 m/s: 40 km/h).</summary>
    public double DarkCruiseSpeed { get; init; } = 10.5;
    /// <summary>The braking it plans a stop for the Track Doll on (m/s²): well under the brake's, so it stops short in time.</summary>
    public double DollBraking { get; init; } = 0.35;
    bool _clubbed;
    Rail.Branch? _alone;
    /// <summary>Whether it's throwing <see cref="_alone"/> over for the branch (a stop's spur, note 261), not setting it back.</summary>
    bool _aloneTo;
    StopHand? _aloneHand;
    /// <summary>With a crew to call to, it stops to work the facilities they can (T32, GDD §17).</summary>
    public StopDriver? Stops { get; } = calls is null ? null : new StopDriver(calls);

    public PlayerIntent Decide(in PlayerState self, TrainOnLine train, uint tick) => Work(self, train, Drive(train, tick, CruiseSpeed, 1.5));

    /// <summary>The forward lamp kept lit (v1.1: the Track Doll only shows in the light; the Fire Flies are the cars' lamps).</summary>
    static LampSwitch Lamp(World world) => world.LampLit ? LampSwitch.None : LampSwitch.On;

    /// <summary>
    /// The fireman (T75): rides in the cab with the driver, keeping the fire and the cab from ever being empty (App. A.5's
    /// Deadman), and takes the controls if the driver dies. Nobody else can get to the cab at speed: the tender's full width
    /// and the cab roof's over it, so the one in the cab already is the crew's only other driver.
    /// </summary>
    public bool Fireman { get; init; }

    /// <summary>Everyone else aboard (or not) as this bot's client sees them, set each tick (T96).</summary>
    public IReadOnlyList<PlayerState>? Crewmates { get; set; }

    /// <summary>Of <see cref="Crewmates"/>, the people playing (not bots), set each tick (note 259). Null: not known, taken as none.</summary>
    public IReadOnlyList<PlayerState>? Players { get; set; }

    double _waitedForBreach, _waitedForBoarder;
    bool _backedUp;

    /// <summary>How far behind the train's rear a crewmate on the ground has to be to count as left behind (m).</summary>
    const double LeftBehind = 20;
    /// <summary>How far the driver will set back for one, at most (m), and at what speed (m/s).</summary>
    const double SetBackFor = 1500, SetBackSpeed = StopDriver.SetBackTop;

    /// <summary>
    /// T96 (playtest: "if I get off the train it never stops for me"): a crewmate left on the ground behind the train while
    /// it's running. Stop, set back to them at yard speed, and wait there for them to climb on; then forward again. Null
    /// when nobody's left behind.
    /// </summary>
    PlayerIntent? ForTheLeftBehind(World world)
    {
        var train = world.Train;
        var d = train.Dynamics;
        bool atAStop = Stops is { Doing: not StopDriver.Leg.Cruise };
        // T81: where they are along the train's own path (a LineHint is along the main line, and on an alternate that's
        // another count altogether: deepTerritory:1's driver set back for crew a couple of hundred metres "behind" who were
        // on his own roofs). Only someone stood on the ground: in the air is a hop between roofs. And anywhere from behind
        // the rear up to the engine: walking beside the rear car at the train's pace, two of that crew were out of the old
        // window (10 m past the rear) as often as in it, and walked 2.5 km at 2 m/s till one died of the cold.
        var behind = atAStop || Crewmates is null ? null : Crewmates
            .Where(c => c.Alive && c.Parent == PlayerState.World && c.Surface == Surface.Ground)
            .Select(c => (c, along: AlongTheTrain(train, c.Position)))
            .Where(x => x.along < d.Distance + 5 && d.RearDistance - x.along < SetBackFor)
            .OrderBy(x => d.RearDistance - x.along).Select(x => ((PlayerState c, double along)?)x).FirstOrDefault();
        if (behind is not { } them || _waitedForBoarder > 120)
        {
            if (behind is null)
                _waitedForBoarder = 0;
            // Set back for someone, and they're on: forward again (the reverser at a stand).
            if (_backedUp && world.Controls.Reverser < 0)
                return StopDriver.Toward(world, d.Distance + 1000, +1, SetBackSpeed);
            _backedUp = false;
            return null;
        }
        double gap = d.RearDistance - them.along;
        if (gap > LeftBehind && (d.Velocity < -0.05 || Math.Abs(d.Velocity) < 0.05 || world.Controls.Reverser < 0))
        {
            _backedUp = true;
            return StopDriver.Toward(world, them.along + LeftBehind * 0.5, -1, SetBackSpeed, rear: true);
        }
        if (gap > LeftBehind)
            return new PlayerIntent { Buttons = PlayerButtons.Brake, ThrottleNotch = -4 };
        // Alongside them: stand for them to get on.
        _waitedForBoarder += SimConstants.TickSeconds;
        return new PlayerIntent { Buttons = PlayerButtons.Brake, ThrottleNotch = -4 };
    }

    /// <summary>How far along the train's own path a point on the ground beside it is (from its rear, a few projections).</summary>
    public static double AlongTheTrain(TrainOnLine train, Double3 at)
    {
        var d = train.Dynamics;
        double along = d.RearDistance;
        for (int i = 0; i < 4; i++)
        {
            var sample = train.Line.Sample(d.Path, along);
            along = Math.Clamp(along + Double3.Dot(at - sample.Position, sample.Tangent), 0, train.Line.PathLength(d.Path));
        }
        return along;
    }

    /// <summary>
    /// T96: a Holdout lit ahead (someone's come back to it, GDD App. D.5). Down to a stand with the train alongside it,
    /// and stand there while a crewmate breaches it (or a while, if nobody does). Null when there's none to stop for.
    /// </summary>
    PlayerIntent? ForAHoldout(World world, ref double cruise)
    {
        var train = world.Train;
        var d = train.Dynamics;
        bool breaching = calls?.Breaching == true;
        var lit = world.Holdouts?.All.Where(h => h.Lit).Select(h => h.LineHint + 20 - d.Distance).Where(a => a > -150 && a < 600)
            .OrderBy(Math.Abs).Select(a => (double?)a).FirstOrDefault();
        if (lit is not { } ahead && !breaching || _waitedForBreach > HoldoutGiveUp)
        {
            if (lit is null && !breaching)
                _waitedForBreach = 0;
            return null;
        }
        if (lit is { } a && !breaching && !AlongsideTheHoldout(a, d.Speed))
        {
            cruise = Math.Min(cruise, Math.Max(0, Math.Sqrt(2 * DollBraking * Math.Max(0, a - HoldoutStandAt))));
            return null;
        }
        if (d.Speed < 0.05)
            _waitedForBreach += SimConstants.TickSeconds;
        return new PlayerIntent { Buttons = PlayerButtons.Brake, ThrottleNotch = -4 };
    }
    /// <summary>How long the driver stands for a lit Holdout (s) before it gives up on them and goes on: nobody's breaching it.</summary>
    const double HoldoutGiveUp = 180;
    /// <summary>Where the driver stands for a lit Holdout: this far short of it (m), and how far short a stand still counts.</summary>
    const double HoldoutStandAt = 5, HoldoutShortBy = 10;

    /// <summary>
    /// At the Holdout to stand for it: there, or come to a stand just short of it. T81: a train braking in on it stood a hair
    /// over 5 m short, its allowed speed there all but nothing and the wait not started (that's only counted alongside), and
    /// stayed there till the dawn (deepTerritory:1, a crew of two).
    /// </summary>
    public static bool AlongsideTheHoldout(double ahead, double speed) =>
        ahead <= HoldoutStandAt || speed < 0.05 && ahead <= HoldoutStandAt + HoldoutShortBy;

    bool _outToBreach, _firedToLeave;
    double _standingForHoldout;
    /// <summary>
    /// Standing at a lit Holdout, how long the driver leaves it to a walker or the gunner who could go before it goes itself
    /// (s): one warming in a car can't get down to it (T115), and comes out once warm. And how long it waits for someone
    /// playing who's aboard to come to the cab and mind it (note 259).
    /// </summary>
    const double LeaveItToTheCrew = 45, ForAPlayerToTheCab = 20;
    /// <summary>
    /// What the driver vents the gauge down to before it leaves the train standing on its brake: the pressure it fires to
    /// hold at a stand, so steam pulls no harder against the brake than it does there, and there's steam to go on with.
    /// </summary>
    const double LeavingPressure = StandingPressure;
    /// <summary>The firebox this full (of boiler.json fireboxCapacity) before the driver leaves it to breach (note 259).</summary>
    const double LeavingFire = 0.8;

    /// <summary>Out of the cab to breach a Holdout itself, or on the way back up (for the harness's trace; note 259).</summary>
    public bool BreachingAlone => _outToBreach;

    /// <summary>
    /// Note 258 (T115's leftover): a crewmate in a lit Holdout (GDD App. D.5: "a living crew member begins the breach") and
    /// nobody else to begin it. Heed.Holdouts sends a walker or the gunner; with none of them alive (a crew of one bot and
    /// someone playing who's died, or the driver the last bot standing) the driver's held the train there for
    /// <c>ForAHoldout</c>'s wait and gone on without them. So the driver goes, as it does to set a switch back alone or to
    /// club the Switchman: the train standing on its brake, the gauge vented down to what it holds at a stand and the firebox
    /// door shut; down out of the cab on the Holdout's side, along to its door, Use held there till they're out; and back up
    /// into the cab, where <c>ForAHoldout</c> finds nothing lit and it drives on. Someone playing in the cab minds it instead
    /// (the brake's theirs), and one aboard gets a while to come to it. Null when it isn't going (or is back).
    /// </summary>
    PlayerIntent? BreachAlone(in PlayerState self, World world)
    {
        var train = world.Train;
        if (!self.Alive || calls is null || world.Holdouts is not { } hs)
        {
            _outToBreach = _firedToLeave = false;
            _standingForHoldout = 0;
            return null;
        }
        var at = PlayerMotor.WorldPosition(self, train);
        var lit = hs.All.Where(h => h.Lit).Select(h => (h, d: ((h.Door - at) with { Y = 0 }).Length)).Where(x => x.d <= Heed.HoldoutRange)
            .OrderBy(x => x.d).Select(x => x.h).FirstOrDefault();
        bool givenUp = _waitedForBreach > HoldoutGiveUp;
        if (_outToBreach)
        {
            _aloneHand ??= new StopHand(StopJob.None, calls, member);
            _waitedForBreach += SimConstants.TickSeconds;
            if (lit is not null && !givenUp && _aloneHand.Breach(self, world, lit) is { } breaking)
                return breaking with { Buttons = breaking.Buttons | PlayerButtons.Brake };
            // They're out (or it's given up on them): back up into the cab, and on from there.
            if (_aloneHand.SetBackAlone(self, world, null) is { } back)
                return back with { Buttons = back.Buttons | PlayerButtons.Brake, ThrottleNotch = -4 };
            _outToBreach = _firedToLeave = false;
            _standingForHoldout = 0;
            return null;
        }
        // Only at a stand alongside it, with nobody on their way to it or at it already (someone playing, at its door).
        if (lit is null || givenUp || train.Dynamics.Speed > 0.05 || calls.Breaching || lit.State == Run.HoldoutState.Breaching
            || !PlayerMotor.InCab(self, train))
        {
            if (lit is null)
            {
                _standingForHoldout = 0;
                _firedToLeave = false;
            }
            return null;
        }
        _standingForHoldout += SimConstants.TickSeconds;
        if (calls.AnyBreacher && _standingForHoldout < LeaveItToTheCrew)
            return null;
        // Someone playing in the cab has the controls: out it goes. One aboard elsewhere gets a while to come to them.
        bool playerInCab = Players?.Any(p => p.Alive && PlayerMotor.InCab(p, train)) == true;
        if (!playerInCab && Players?.Any(p => p.Alive && p.Parent != PlayerState.World) == true && _standingForHoldout < ForAPlayerToTheCab)
            return null;
        var hold = new PlayerIntent { Buttons = PlayerButtons.Brake, ThrottleNotch = -4 };
        if (ReadyToLeave(self, world, playerInCab || calls.FiremanMinding) is { } readying)
            return readying;
        _outToBreach = true;
        _aloneHand ??= new StopHand(StopJob.None, calls, member);
        return _aloneHand.Breach(self, world, lit) is { } going ? going with { Buttons = going.Buttons | PlayerButtons.Brake, ThrottleNotch = -4 } : hold;
    }

    /// <summary>
    /// Getting the engine ready to be left standing on its brake while the driver's down on the ground (note 259's Holdout
    /// breach; note 261's stop work): the firebox filled, its door shut, the gauge vented down. This tick's intent while it
    /// isn't, null once it is. <paramref name="minded"/>: someone's at the controls meanwhile, so the fire and the gauge are theirs.
    /// </summary>
    /// <param name="fire">How full the firebox goes first (of boiler.json fireboxCapacity).</param>
    PlayerIntent? ReadyToLeave(in PlayerState self, World world, bool minded, double fire = LeavingFire)
    {
        var train = world.Train;
        // Nobody to fire it while it's out, and the cars' heating draws the gauge down all the while: the firebox filled first.
        // Out with the fire as it was, frontier:7's lone driver came back from a 70 s breach to a fire at 0.08 and the gauge at
        // 29, under the Stoker's low fire and low pressure (App. A.5).
        // Once: shovelling opens the door, and waiting for it to swing shut the fire burnt back under the mark, and round again
        // (frontier:7 stood 145 s at a Holdout so).
        if (train.BoilerTuning is { } fired && train.Boiler.FireFraction(fired) >= fire)
            _firedToLeave = true;
        if (!minded && !_firedToLeave && train.BoilerTuning is { } bt && train.Boiler.Tender >= 1 && train.Boiler.FireFraction(bt) < fire)
        {
            var firebox = train.Frames[0].Shape.Interactables.First(i => i.Kind == InteractableKind.Firebox).Position;
            var (step, there) = WarmUp.Steer(self, FiringSpot(firebox, 1), FacingFire);
            var firing = there && CrewActions.Nearest(self, train) == InteractableKind.Firebox ? new PlayerIntent { Buttons = PlayerButtons.Use } : step;
            return firing with { Buttons = firing.Buttons | PlayerButtons.Brake, ThrottleNotch = -4 };
        }
        // Not out of the cab with the firebox door open (the Stoker comes in at an open door at a stand, App. A.5). With no
        // enemies there's no Stoker, and nothing swings the door shut (World.Step), so it isn't waited on.
        if (train.Boiler.FireDoorOpen && world.Enemies is not null)
            return new PlayerIntent { Buttons = PlayerButtons.Brake, ThrottleNotch = -4 };
        // Nobody minding the controls: the train left standing on its brake (CabControls.Clears), with the steam vented down
        // so it pulls no harder against it than a standing engine's fire keeps it.
        if (!minded && train.BoilerTuning is { SteamDrive: true } && train.Boiler.Pressure > LeavingPressure)
        {
            var (step, there) = WarmUp.Steer(self, VentWay(train)[0], 0);
            var venting = there && CrewActions.Nearest(self, train) == InteractableKind.Vent ? new PlayerIntent { Buttons = PlayerButtons.Use } : step;
            return venting with { Buttons = venting.Buttons | PlayerButtons.Brake, ThrottleNotch = -4 };
        }
        return null;
    }

    bool _workingOut;
    StopHand? _workHand;
    /// <summary>
    /// The firebox this full before the driver leaves it for a stop's loading (note 261): a crate run is minutes, not a
    /// Holdout's breach, and at a stand (boiler.json idleDraft) a full box lasts it a minute before it wants firing again.
    /// </summary>
    const double WorkingFire = 0.95;
    /// <summary>Out working, with the fire under this many times the low fire, back in to fire it (note 261).</summary>
    const double WorkFireCalls = 1.25;

    /// <summary>Down on the ground working a stop's loading (note 261), or on the way back up (for the harness's trace).</summary>
    public bool WorkingOut => _workingOut;
    /// <summary>What it's doing down there (for tests and the harness's trace).</summary>
    public string? WorkStep => _workingOut ? _workHand?.Doing : null;

    /// <summary>
    /// Note 261 (T114): too few others at a stop for its loading (a crew of two's winch wants a second handle; with nobody but
    /// the driver, the crates), so the driver gets down to it, as it does to breach a Holdout alone (note 259): the train left
    /// standing on its brake, fired up and vented first; down to its part (<see cref="StopDriver.DriverPart"/>) through a
    /// <see cref="StopHand"/>; and back up into the cab once there's nothing left for it, or the fire wants it (it burns down
    /// with nobody at it, and the Stoker comes for a low fire, App. A.5). Null while it's in the cab with nothing to go to.
    /// </summary>
    PlayerIntent? WorkOut(in PlayerState self, World world, StopDriver stops)
    {
        var train = world.Train;
        if (calls is null || !self.Alive)
        {
            _workingOut = false;
            return null;
        }
        var part = stops.DriverPart(world);
        bool lowFire = !calls.FiremanMinding && train.BoilerTuning is { } fb && train.Boiler.FireFraction(fb) < fb.LowFireFraction * WorkFireCalls;
        _workHand ??= new StopHand(StopJob.Driver, calls, member);
        if (_workingOut)
        {
            if (part != StopJob.None && !lowFire && _workHand.Lend(self, world, stops.Plan!, part) is { } working)
                return working with { Buttons = working.Buttons | PlayerButtons.Brake, ThrottleNotch = -4 };
            // Done (or the fire wants it): back up into the cab, and from there the loading's its to call. Nobody climbs with
            // freight in their arms: down with it first.
            if (self.Has(PlayerFlags.Heavy))
            {
                var letGo = _workHand.LetGo();
                return letGo with { Buttons = letGo.Buttons | PlayerButtons.Brake, ThrottleNotch = -4 };
            }
            if (_workHand.SetBackAlone(self, world, null) is { } back)
                return back with { Buttons = back.Buttons | PlayerButtons.Brake, ThrottleNotch = -4 };
            _workingOut = _firedToLeave = false;
            return null;
        }
        if (part == StopJob.None || !PlayerMotor.InCab(self, train))
            return null;
        if (ReadyToLeave(self, world, calls.FiremanMinding, WorkingFire) is { } readying)
            return readying;
        _workingOut = true;
        var going = _workHand.Lend(self, world, stops.Plan!, part) ?? new PlayerIntent();
        return going with { Buttons = going.Buttons | PlayerButtons.Brake, ThrottleNotch = -4 };
    }
    bool _driving, _sawDriver;
    double _boardWait;
    /// <summary>How long the driver waits at the gate for the crew to climb aboard (T102) before it goes anyway.</summary>
    const double AllAboardSeconds = 120;
    /// <summary>At the controls: the driver, or a fireman who's had to take them.</summary>
    public bool Driving => !Fireman || _driving;

    public PlayerIntent Decide(in PlayerState self, World world, uint tick, out PlayerState aimed)
    {
        aimed = self;
        var train = world.Train;
        // T102: the night starts with the crew on the ballast at the gate (the harness walks them aboard): driver and fireman
        // up the cab steps on foot. Only before departure: at a stop, the driver's down there working.
        if (self.Alive && world.Run is { Phase: Run.RunPhase.Yard } && train.Dynamics.Speed < 0.05 && calls is not null)
        {
            // On the steps: on up (with nothing held, a hand lets go).
            if (self.Parent == 0 && self.Surface == Surface.Ladder)
                return new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Use };
        }
        if (self.Alive && self.Parent == PlayerState.World && self.Surface == Surface.Ground && world.Run is { Phase: Run.RunPhase.Yard }
            && train.Dynamics.Speed < 0.05 && calls is not null)
        {
            _aloneHand ??= new StopHand(StopJob.None, calls, member);
            if (_aloneHand.SetBackAlone(self, world, null) is { } up)
                return up;
        }
        if (Fireman && !_driving)
        {
            // Standing by: the fire, and out of the way of anything that's got into the cab. The driver's dead (it's said so,
            // having been heard driving): the controls are ours from here.
            _sawDriver |= calls?.Has(StopJob.Driver) == true;
            if (calls is null || !_sawDriver || calls.Has(StopJob.Driver) || !self.Alive)
            {
                calls?.Say(member, StopJob.None, self);
                // T105: the driver's away from the controls. The brake holds only while someone in the cab holds it
                // (CabControls.Clears), so with nobody at them steam pulled the train on unchecked, over whatever board or curve
                // came next (frontier:11, the driver out sanding, derailed so). T107: or back down the hill it had stalled on
                // (deepTerritory:1, the driver pulled off the engine by Climbers, ran back 9.5 km). The fireman minds them
                // meanwhile: moving, no faster than the boards and the line allow, and braked if it's rolling back; standing,
                // on the brake (it never drives off without the driver).
                if (calls?.DriverAway == true && self.Alive && PlayerMotor.InCab(self, train))
                {
                    var minding = train.Dynamics.Speed < Net.CabControls.StandingBelow ? new PlayerIntent { Buttons = PlayerButtons.Brake, ThrottleNotch = -4 }
                        : WatchTheRoad(world, Drive(train, tick, MindingCruise(world), 0.5));
                    return KeepClear(self, world, Work(self, train, minding), -1);
                }
                if (Mend(self, world, tick) is { } mendingToo)
                    return mendingToo;
                if (FightStoker(self, world) is { } fightingToo)
                    return fightingToo with { Buttons = fightingToo.Buttons & ~PlayerButtons.Brake, ThrottleNotch = 0 };
                // T106: fired for the speed the line allows here, not the open-line cruise: under a board, steam fired for 14 m/s
                // is steam the driver brakes away. And with too much on the gauge for it (a board come up), out to the
                // blow-off to vent it down while the driver holds the train on the brake: the brake and the vent as a pair.
                _cruise = MindingCruise(world);
                var venting = Vent(self, world);
                calls?.Vent(venting is not null && !PlayerMotor.InCab(self, train));
                calls?.Mind(venting is null && self.Alive && PlayerMotor.InCab(self, train));
                if (venting is { } blowing)
                    return blowing;
                return self.Alive && PlayerMotor.InCab(self, train) ? KeepClear(self, world, Work(self, train, default), -1) : default;
            }
            _driving = true;
        }
        calls?.Say(member, StopJob.Driver, self);
        calls?.Away(self.Alive && !PlayerMotor.InCab(self, train));
        // Note 261: who works the stops (facilities.json crew), and how many people playing there are to count as hands.
        if (calls is not null)
        {
            var crew = world.Run?.FacilityTuning?.Crew;
            calls.DriverWorks = crew?.DriverWorks ?? false;
            calls.PeopleAreHands = crew?.PeopleAreHands ?? true;
            calls.Playing(Players?.Count(p => p.Alive) ?? 0);
        }
        var lamp = Lamp(world);
        // All aboard (T102): standing at the gate, the driver waits on the brake for whoever's still down beside the train, or
        // still on a ladder, to climb on, for so long.
        if (world.Run is { Phase: Run.RunPhase.Yard } && PlayerMotor.InCab(self, train) && train.Dynamics.Speed < 0.1
            && _boardWait < AllAboardSeconds && Crewmates?.Any(c => c.Alive && (c.Parent == PlayerState.World || c.Surface == Surface.Ladder)) == true)
        {
            _boardWait += SimConstants.TickSeconds;
            return new PlayerIntent { Buttons = PlayerButtons.Brake, ThrottleNotch = -4, Lamp = lamp };
        }
        // The Stoker in the firebox (v1.1 App. A.5): at the firebox door and club it out, brake on, whatever else is going on
        // (every blow burns: it's done while there's health to spare for it, and the brake holds the runaway meanwhile).
        if (FightStoker(self, world) is { } fighting)
            return fighting with { Lamp = lamp };
        // A ruptured boiler (T109): the wrench from its rack, and mended at the firebox; the train coasts meanwhile.
        if (Mend(self, world, tick) is { } mending)
            return mending with { Lamp = lamp };
        // On a generated line, no faster than its authority allows here (linegen plan §9, §16.1): what the boards say.
        double cruise = OpenCruise(world);
        // The Track Doll on the rail ahead in the lamp (v1.1 App. A.2): stop before you hit the doll. Braking to a stand
        // short of it, it's gone for the run.
        if (world.ActiveEnemies.OfType<TrackDoll>().FirstOrDefault(d => d.Phase == SpinePhase.Telegraph && !d.Haunting) is { } doll)
        {
            double ahead = doll.LineDistance - train.Dynamics.Distance;
            if (ahead > 0)
                cruise = Math.Min(cruise, Math.Max(0, Math.Sqrt(Math.Max(0, 2 * DollBraking * (ahead - 25)))));
        }
        // A derailing Switchman at its lever ahead (v1.1 App. A.8): stop short of its points, and the driver goes and clubs it.
        var derailer = world.ActiveEnemies.OfType<Switchman>().FirstOrDefault(w => w.Derailer && !w.Gone && w.Phase is SpinePhase.Telegraph or SpinePhase.Commit
            && w.Branch >= 0 && w.Branch < train.Line.Branches.Count && train.Line.Branches[w.Branch].Toe > train.Dynamics.Distance - 2);
        if (derailer is not null)
        {
            double ahead = train.Line.Branches[derailer.Branch].Toe - train.Dynamics.Distance;
            cruise = Math.Min(cruise, Math.Max(0, Math.Sqrt(Math.Max(0, 2 * DollBraking * (ahead - 40)))));
            // Close in, on the brake and off the regulator, whatever the drive says: it's a stop short, not a crawl.
            if (ahead < 90 && Math.Abs(train.Dynamics.Velocity) >= 0.05 && PlayerMotor.InCab(self, train))
                return new PlayerIntent { Buttons = PlayerButtons.Brake, ThrottleNotch = -4, Lamp = lamp };
            // Not out of the cab with the firebox door still open from the last shovelful: an empty cab leaves it open, and
            // an open door at a stop lets the Stoker in (App. A.5), which drives the train on over the points.
            if (PlayerMotor.InCab(self, train) && train.Boiler.FireDoorOpen)
                return new PlayerIntent { Buttons = PlayerButtons.Brake, Lamp = lamp };
            if (ahead < 300 && Math.Abs(train.Dynamics.Velocity) < 0.05 && calls is not null && self.Alive)
            {
                _aloneHand ??= new StopHand(StopJob.None, calls, member);
                if (_aloneHand.Club(self, world, derailer) is { } clubbing)
                {
                    // Out of the cab with the brake held, so the train's left standing on it (on a grade it would roll on
                    // into the points).
                    _clubbed = true;
                    return clubbing with { Lamp = lamp, Buttons = clubbing.Buttons | PlayerButtons.Brake };
                }
            }
        }
        else if (_clubbed && _aloneHand is not null && self.Alive)
        {
            // Back up into the cab after.
            if (_aloneHand.SetBackAlone(self, world, null) is { } back)
                return back with { Lamp = lamp, Buttons = back.Buttons | PlayerButtons.Brake };
            _clubbed = false;
        }
        // Note 258: out of the cab breaking a crewmate out of a Holdout nobody else could, or back up into it after.
        if (_outToBreach && BreachAlone(self, world) is { } outBreaching)
            return outBreaching with { Lamp = lamp };
        // T96: a crewmate left behind, or back in a lit Holdout: stop for them.
        if (self.Alive && PlayerMotor.InCab(self, train))
        {
            if (ForTheLeftBehind(world) is { } setBack)
                return Work(self, train, setBack) with { Lamp = lamp };
            if (ForAHoldout(world, ref cruise) is { } standing)
                return (BreachAlone(self, world) ?? Work(self, train, standing)) with { Lamp = lamp };
        }
        if (world.TrackPlan is { } plan)
            cruise = Math.Min(cruise, LineGen.LineAuthority.For(plan, train.Line).Allowed(train));
        // The boards it's read (sight.json): down to a posted speed in time, and held there till the last car's through.
        cruise = Math.Min(cruise, Posted(world));
        // And the Sleepers in the lamp, or greased rail down a grade: under the Sleepers' speed (note 231).
        cruise = Math.Min(cruise, HazardAllow(world));
        if (Stops is { } stops)
        {
            // Nobody left to set a switch back but the driver: down it gets, and back up (the train stands on its brake).
            if (self.Alive && stops.SetBackAlone(world) is { } wrong)
                (_alone, _aloneTo) = (wrong, false);
            // Note 261: or over for the spur at a stop with no shunter.
            else if (self.Alive && _alone is null && stops.ThrowAlone(world) is { } spur)
                (_alone, _aloneTo) = (spur, true);
            if (_alone is not null && calls is not null)
            {
                _aloneHand ??= new StopHand(StopJob.None, calls, member);
                // The stop's given the spur up (its Held give-up: the points wouldn't go over) with the switch still for the
                // main: back up into the cab, not at the lever all night (note 300).
                var to = _aloneTo && !train.Diverging(_alone.Index) && stops.ThrowAlone(world) is null ? null : _alone;
                if (_aloneHand.SetBackAlone(self, world, to, _aloneTo) is { } getting)
                {
                    stops.Decide(self, world); // its clock runs on while it's out of the cab
                    return getting with { Lamp = lamp, Buttons = getting.Buttons | PlayerButtons.Brake };
                }
                _alone = null;
            }
            // Note 261: down to a part of the loading with too few others for it, and back up after.
            if (WorkOut(self, world, stops) is { } working)
            {
                stops.Decide(self, world); // its clock runs on while it's out of the cab
                return working with { Lamp = lamp };
            }
            stops.CruiseSpeed = cruise;
            stops.ReckonTop = world.LampShining ? CruiseSpeed : DarkCruiseSpeed;
            if (stops.Decide(self, world) is { } stopping)
                return KeepClear(self, world, Work(self, train, stopping), +1) with { Lamp = lamp };
        }
        // Greased rail (App. A.2): the controls do nothing on it, so out to the sandbox and sand it, and back after.
        if (Sand(self, world, cruise) is { } sanding)
            return sanding with { Lamp = lamp };
        // With no fireman at the blow-off, the driver vents for the Sleepers (or a fading brake) himself, holding the brake (the
        // cab's vent is a step across: T109). Their crews of two ran onto them over the brake as the eights did (note 231).
        if (calls?.FiremanMinding != true && calls?.Venting != true && (HazardAllow(world) < double.MaxValue || train.Dynamics.BrakeEfficiency < FadedBrake || Venting) && Vent(self, world) is { } ventingAlone)
            return KeepClear(self, world, ventingAlone with { Buttons = ventingAlone.Buttons | PlayerButtons.Brake, ThrottleNotch = -4 }, +1) with { Lamp = lamp };
        var intent = Drive(train, tick, cruise, world.TrackPlan is null ? 1.5 : 0.5);
        intent.Lamp = lamp;
        return KeepClear(self, world, Work(self, train, WatchTheRoad(world, intent)), +1);
    }

    /// <summary>
    /// A Climber got into the cab (App. A.4): it takes whoever comes within its reach, and stays while anyone's in the cab
    /// (the Deadman's there for an empty one). So nobody leaves: the driver and fireman keep to the cab's front corners
    /// (<paramref name="side"/>: +1 the driver's right), out of its reach, working the controls and the firebox from there.
    /// The controls and the fire as the intent had them; only where it stands changes.
    /// </summary>
    static PlayerIntent KeepClear(in PlayerState self, World world, PlayerIntent intent, int side)
    {
        var train = world.Train;
        if (!self.Alive || !PlayerMotor.InCab(self, train) || !world.ActiveEnemies.Any(e => e is Climber { Inside: true } c && c.Attached == 0))
            return intent;
        var cab = train.Frames[0].Shape.Cab!.Value;
        var corner = new Double3(side * (cab.Max.X - 0.45), 0, cab.Min.Z + 0.45);
        var (step, _) = WarmUp.Steer(self, corner, 0);
        return intent with { MoveX = step.MoveX, MoveZ = step.MoveZ, LookYaw = step.LookYaw };
    }

    /// <summary>Under a posted speed by this much (m/s): the driver's cruise wobbles either side of what it's holding.</summary>
    const double PostedMargin = 1;

    int _sandLeg = -1;
    /// <summary>With nobody minding the controls, the driver's out on the board sanding only while the train's this slow (m/s).</summary>
    const double AloneSandingTop = 6;
    /// <summary>Slowed this far under its cruise on grease (m/s), it goes out to sand.</summary>
    const double SandBelowCruise = 2;
    /// <summary>Alone, rolling back faster than this (m/s) with sand down, it's lost the hill: back to the brake (note 252).</summary>
    const double SandRollBack = 0.5;
    /// <summary>Alone, back in at <see cref="AloneSandingTop"/>, it's out again only this much under it (m/s; note 252).</summary>
    const double AloneSandingBack = 1;
    /// <summary>Alone, with the fire under this many times the low fire (boiler.json lowFireFraction), back in to fire it (note 252).</summary>
    const double SandFireCalls = 1.5;
    /// <summary>Out on the running board to sand, or on the way there or back (for the harness's trace).</summary>
    public bool Sanding => _sandLeg >= 0;

    /// <summary>
    /// Grease (App. A.2): "cannot climb grade · cannot brake · cannot accelerate", so the controls can wait. Out of the cab's
    /// doorway on the driver's side, forward along the running board to the sandbox, and hold Use there until the engine's
    /// off it; then back the way it came. Only once the grease is costing it way (slowed well under cruise: a climb), as on the
    /// level the train coasts through it. Null in the cab with nothing to sand for.
    /// </summary>
    PlayerIntent? Sand(in PlayerState self, World world, double cruise)
    {
        var train = world.Train;
        if (!self.Alive || self.Parent != 0 || world.Lineside is not { } lineside)
        {
            _sandLeg = -1;
            return null;
        }
        bool greased = lineside.OnGrease(train);
        var way = SandWay(train);
        // T107: with steam driving, nobody minding the controls (no fireman in the cab) and the sanded drivers taking hold,
        // the train runs on up to whatever its steam makes with the driver out on the board: deepTerritory:1's ran onto the
        // Sleepers at 11.8 m/s so. Alone, the driver goes out only while it's slow, and comes back in once it isn't.
        bool alone = train.BoilerTuning is { SteamDrive: true } && calls?.FiremanMinding != true;
        // Note 233: alone, the driver goes out with the brake off (below), so the sanded drivers pull. Then he's back in once
        // the train's going, or once the fire wants him (well before the low fire or the low gauge that brings the Stoker,
        // App. A.5: under the gauge he fires to at a stand, or a fire getting low), or once it's rolling back with sand down:
        // the steam can't hold it on the hill, and it wants the brake. Out and in on deepTerritory:2's stall with the gauge
        // going down, he never stood at the firebox long enough to fire it, and the Stoker came for the low pressure.
        bool callsBack = alone && (train.Dynamics.Speed > AloneSandingTop || train.Boiler.Pressure < StandingPressure
            || train.BoilerTuning is { } fb && train.Boiler.FireFraction(fb) < fb.LowFireFraction * SandFireCalls
            || train.Dynamics.Velocity < -SandRollBack && train.Sand > 0.5);
        if (callsBack && _sandLeg >= 0 && _sandLeg < way.Length)
            _sandLeg = 2 * way.Length - 1 - _sandLeg;
        if (_sandLeg < 0)
        {
            // And out again only once it's slowed a little under that, or it's in and out of the doorway at the top speed.
            if (callsBack || alone && train.Dynamics.Speed > AloneSandingTop - AloneSandingBack)
                return null;
            // Only when the grease is costing way: on the level the train coasts through it at speed, and whatever's behind
            // (the hounds gain on every slowing, App. A.3) is better left behind than stopped for. On a climb it can't hold
            // speed ("cannot climb grade"): out it goes, steam left on, so the sanded drivers pull.
            if (!greased || !PlayerMotor.InCab(self, train) || train.Dynamics.Speed >= cruise - SandBelowCruise || calls?.Venting == true
                || train.BoilerTuning is { SteamDrive: true } sd && train.Boiler.Pressure <= sd.PowerFloor + 1)
                return null;
            _sandLeg = 0;
        }
        // Out: doorway, board, along it, the sandbox. Back: the same, the other way, and done at the doorway. T81: and back in
        // to the Stoker (App. A.5): it's fought from the cab, and out on the board the driver of a crew of two sanded on while
        // it put the fire out (deepTerritory:2), and the night ended there.
        bool stoker = world.ActiveEnemies.OfType<Stoker>().Any(st => !st.Gone && st.Phase is SpinePhase.Telegraph or SpinePhase.Commit);
        // Nor any use out there with no steam to pull on the sanded rail: back in to fire it (that driver stood at the sandbox
        // with the fire out till the cold took him).
        bool noSteam = train.BoilerTuning is { SteamDrive: true } st && train.Boiler.Pressure <= st.PowerFloor + 1;
        if ((!greased || stoker || noSteam) && _sandLeg < way.Length)
            _sandLeg = 2 * way.Length - 1 - _sandLeg;
        // Not out of the cab with the firebox door still open from the last shovelful: only someone in the cab shuts it, and
        // an open door at a stand lets the Stoker in (App. A.5; the Switchman's club waits the same).
        if (_sandLeg == 0 && PlayerMotor.InCab(self, train) && train.Boiler.FireDoorOpen)
            return new PlayerIntent { Buttons = train.BoilerTuning is { SteamDrive: true } && !alone ? PlayerButtons.Brake : PlayerButtons.None };
        if (_sandLeg >= 2 * way.Length)
        {
            _sandLeg = -1;
            return null;
        }
        int at = _sandLeg < way.Length ? _sandLeg : 2 * way.Length - 1 - _sandLeg;
        var (step, there) = WarmUp.Steer(self, way[at], 0);
        // With steam driving (T97) nobody's holding the train back while the driver's out on the board, and sanded drivers
        // pull it up to whatever speed its steam makes: out with the brake held, so it's standing on it till the driver's back.
        // Note 233: but alone, that's a train on the climb that only ever stops: the brake bites on the sand it's laid, it
        // stands on it there (CabControls.Clears), and never gets off the grease to call the driver back (deepTerritory:3 and
        // :2 at a crew of two, out ~100 s while the fire burnt down and the Stoker got in). So alone it's let off on the way
        // out (a notch up, if it's standing on it), and the steam pulls on the sand; he's back in once it's going (above).
        // Rolling back, it's held, as before: the sand's what makes the brake bite on the grease.
        if (train.BoilerTuning is { SteamDrive: true } && PlayerMotor.InCab(self, train))
        {
            if (!alone || train.Dynamics.Velocity < -RollingBack)
                step.Buttons |= PlayerButtons.Brake;
            else if (train.Dynamics.Speed < Net.CabControls.StandingBelow)
                step.ThrottleNotch = 1;
        }
        if (!there)
            return step;
        if (_sandLeg == way.Length - 1)
            return greased && CrewActions.Nearest(self, train) == InteractableKind.Sandbox ? new PlayerIntent { Buttons = PlayerButtons.Use } : new PlayerIntent();
        _sandLeg++;
        return new PlayerIntent();
    }

    int _ventLeg = -1;
    /// <summary>Over the pressure that makes the speed the line allows by this much, the fireman goes out and vents (T106).</summary>
    const double VentOver = 10;
    /// <summary>The brake faded under this (spec B.5's fade, from 1), the steam pulling against it is vented (note 231).</summary>
    const double FadedBrake = 0.7;
    /// <summary>Out on the running board at the blow-off, or on the way there or back (for the harness's trace).</summary>
    public bool Venting => _ventLeg >= 0;

    /// <summary>
    /// T97: steam sets the speed, so a board ahead wants the gauge down, not just the brake on (held, the brake fades). Out of
    /// the cab's left doorway, along the left running board to the blow-off by the smokebox, and hold Use there until the
    /// gauge is down to what the line allows; then back. Not while the driver's out sanding: someone minds the controls.
    /// </summary>
    PlayerIntent? Vent(in PlayerState self, World world)
    {
        var train = world.Train;
        if (!self.Alive || self.Parent != 0 || train.BoilerTuning is not { SteamDrive: true } bt)
        {
            _ventLeg = -1;
            return null;
        }
        double allowed = MindingCruise(world), target = Boiler.PressureFor(bt, allowed + 1, train.Dynamics.Tuning.MaxSpeed);
        // Only while the steam is running the train over what's allowed (the driver's on the brake against it): a gauge high
        // for a board that's still coming the brake deals with, and every pound vented is coal. Venting whenever the gauge
        // read over the mark, frontier:11's fireman spent 1,412 s of the night out there and the train ran out of steam.
        // Note 224: or the brake's fading against it (spec B.5, on a descent): held there at the cruise by a brake that's going,
        // the train never reads over it until the brake's gone, and deepTerritory:1's then ran away at 16 m/s with steam for
        // 17.7 and took a 45 km/h bend at 57. Then down to steam that doesn't pull at all at the speed it's held to (it pulls
        // up to boiler.json driveSpeedBand short of its own speed): the brake has the grade to hold, not the engine too.
        double still = Boiler.PressureFor(bt, Math.Max(0, allowed - bt.DriveSpeedBand), train.Dynamics.Tuning.MaxSpeed);
        bool fading = train.Dynamics.BrakeEfficiency < FadedBrake && train.Boiler.Pressure > still + 2 && train.Dynamics.Speed > allowed - 1;
        bool over = (train.Boiler.Pressure > target + (_ventLeg >= 0 ? 2 : VentOver) && train.Dynamics.Speed > allowed + (_ventLeg >= 0 ? 0 : 1) || fading)
            && calls?.DriverAway != true;
        var way = VentWay(train);
        if (_ventLeg < 0)
        {
            if (!over || !PlayerMotor.InCab(self, train))
                return null;
            _ventLeg = 0;
        }
        if (!over && _ventLeg < way.Length)
            _ventLeg = 2 * way.Length - 1 - _ventLeg;
        if (_ventLeg >= 2 * way.Length)
        {
            _ventLeg = -1;
            return null;
        }
        int at = _ventLeg < way.Length ? _ventLeg : 2 * way.Length - 1 - _ventLeg;
        var (step, there) = WarmUp.Steer(self, way[at], 0);
        if (!there)
            return step;
        if (_ventLeg == way.Length - 1)
            return over && CrewActions.Nearest(self, train) == InteractableKind.Vent ? new PlayerIntent { Buttons = PlayerButtons.Use } : new PlayerIntent();
        _ventLeg++;
        return new PlayerIntent();
    }

    /// <summary>The way to the blow-off, in the engine's frame: in the left doorway, out onto the board, along it, at the cock.</summary>
    static Double3[] VentWay(TrainOnLine train)
    {
        var vent = train.Frames[0].Shape.Interactables.First(i => i.Kind == InteractableKind.Vent).Position;
        // T109: the vent's in the cab, on its left side: a step across to it.
        return [vent with { X = vent.X + 0.25 }];
    }

    /// <summary>The way to the right-hand sandbox, in the engine's frame: in the doorway, out onto the board, along it, at the box.</summary>
    static Double3[] SandWay(TrainOnLine train)
    {
        var g = train.Dynamics.Tuning.Geometry;
        var e = g.Engine;
        double w = g.RoofWidth / 2, doorFront = EnginePlan.Of(g).DoorFront;
        double outside = w + Math.Min(0.32, e.RunningBoardWidth / 2);
        var box = train.Frames[0].Shape.Interactables.First(i => i.Kind == InteractableKind.Sandbox && i.Position.X > 0).Position;
        // Out at the back of the right doorway, where the board begins (note 276: the boards run back along the boiler).
        double door = doorFront + g.Doorway.Width * 0.75;
        return
        [
            new(w - 0.35, e.DeckHeight, door),
            new(outside, e.DeckHeight, door),
            new(outside, e.DeckHeight, box.Z),
        ];
    }

    /// <summary>
    /// The fastest the boards read so far allow here: a posted stretch's speed on it, until the last car's through, and
    /// short of it no faster than a gentle brake gets down to that by the time it's there.
    /// </summary>
    /// <summary>Watch the road: the Sleepers showing on the line ahead means get below their derailing speed, on the brake.</summary>
    static PlayerIntent WatchTheRoad(World world, PlayerIntent intent)
    {
        var train = world.Train;
        if (SleepersSeen(world) && train.Dynamics.Speed > 4)
        {
            intent.Buttons |= PlayerButtons.Brake;
            intent.ThrottleNotch = -4;
        }
        return intent;
    }

    /// <summary>The Sleepers in the lamp (or braced, close) within this far ahead (m): the driver brakes for them.</summary>
    const double SleepersWatch = 200;

    static bool SleepersSeen(World world)
    {
        var d = world.Train.Dynamics;
        return world.ActiveEnemies.Any(e => e.Kind == EnemyKind.Sleepers && e.Phase == SpinePhase.Telegraph
            && e.LineDistance > d.Distance && e.LineDistance - d.Distance < SleepersWatch);
    }

    /// <summary>Under the Sleepers' derailing speed (enemies.json sleepers.derailAbove) by this much (m/s), to vent for them.</summary>
    const double UnderSleepers = 1.6;

    /// <summary>Falling at least this steeply (%) where greased rail starts, it's ridden down under the Sleepers' speed.</summary>
    const double GreasedFall = 0.5;

    /// <summary>
    /// The Sleepers seen ahead as a limit (note 231): with steam driving (T97) the engine pulls against the brake at full effort
    /// while its steam would make more than the train's speed, and a brake faded on the descent before (spec B.5) is no
    /// match for it. Braking for them from 12 m/s, deepTerritory:4's and :1's trains lost under 1 m/s in 200 m and derailed
    /// on them at 42–44 km/h. Steam's taken off as well as the brake put on: the vent, as for a board (T106).
    /// <para>And greased rail seen ahead on a falling grade: there the brakes barely bite (App. A.2) and the grade gains on
    /// them, so it's entered under the Sleepers' speed, not braked for once on it. deepTerritory:4's and :1's Sleepers lay
    /// in the grease 2 % down, seen once 10 m into it, and the train ran onto them at 43 km/h with the brake on all the
    /// way.</para>
    /// </summary>
    static double HazardAllow(World world)
    {
        if (world.Enemies is not { } et)
            return double.MaxValue;
        var train = world.Train;
        bool greasedFall = train.Dynamics.Path == Rail.RailLine.MainPath && world.Lineside?.GreaseAhead(train, world.LampShining) is { } start
            && train.Line.Sample(Rail.RailLine.MainPath, Math.Max(start, train.Dynamics.Distance)).GradePercent < -GreasedFall;
        return SleepersSeen(world) || greasedFall ? et.Sleepers.DerailAbove - UnderSleepers : double.MaxValue;
    }

    /// <summary>What the fireman holds the train to while the driver's out of the cab: the lamp's cruise, the line's, the boards'.</summary>
    double MindingCruise(World world) => Math.Min(Math.Min(OpenCruise(world), LineAllows(world)), HazardAllow(world));

    /// <summary>What the line allows here: its authority (linegen plan §9, §16.1) and the boards read.</summary>
    static double LineAllows(World world)
    {
        double allowed = Posted(world);
        if (world.TrackPlan is { } plan)
            allowed = Math.Min(allowed, LineGen.LineAuthority.For(plan, world.Train.Line).Allowed(world.Train));
        return allowed;
    }

    /// <summary>Over the Fire Flies' pull-away speed by this much (m/s) to drive away from them.</summary>
    const double OutrunBy = 1.5;

    /// <summary>
    /// The cruise before the line's limits: the lamp's (spec B.3's 14 m/s), or under the Sleepers in the dark. And Fire Flies
    /// swarming a lit car's lamp (v1.1 App. A.5 "BREAK OFF: lamp turned off, or the train pulls away at speed"; GDD §21 "lamps
    /// off when they swarm. Or drive away"), where the line allows it: over their pull-away speed until they've gone. The
    /// cruise is a hair under it (enemies.json fireFlies, 15 m/s), so they were never outrun, and five swarms set five
    /// cars alight on frontier:7's crew of eight (note 188). The fireman's fire and vent go by the same, or he'd vent it away.
    /// Since they come only to a stopped train and go once it's under way (note 269), that's no more than the cruise.
    /// </summary>
    double OpenCruise(World world)
    {
        if (!world.LampShining)
            return DarkCruiseSpeed;
        var train = world.Train;
        if (world.Enemies is { } et && world.ActiveEnemies.Any(e => e is FireFlies { Gone: false, Phase: SpinePhase.Telegraph } f && f.Attached > 0
                && f.Attached < train.Vehicles.Count && train.Vehicles[f.Attached].LampLit)
            && et.FireFlies.PullAwaySpeed + OutrunBy is var away && LineAllows(world) >= away)
            return Math.Max(CruiseSpeed, away);
        return CruiseSpeed;
    }

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

    /// <summary>Where it stands to fire (engine frame): this far to its side of the firebox door, and this far out from it into the cab.</summary>
    const double FiringSide = 0.35, FiringOut = 0.4;

    /// <summary>
    /// Its firing place, <paramref name="side"/> of the firebox door (+1 the driver's right): out from the door into the cab.
    /// Cab forward (note 276), the firebox is in the cab's back wall, so that's forward (−Z) of it.
    /// </summary>
    internal static Double3 FiringSpot(Double3 firebox, int side) => new(side * FiringSide, 0, firebox.Z - FiringOut);

    /// <summary>The look that faces the firebox door from <see cref="FiringSpot"/>: back down the engine (+Z), cab forward.</summary>
    internal const double FacingFire = Math.PI;

    /// <summary>At a stand, the gauge it fires to hold: comfortably over the Stoker's low-pressure mark (enemies.json, 40).</summary>
    const double StandingPressure = 60;

    /// <summary>At its own side of the firebox door, facing it, swinging, with the brake held: null if there's no Stoker to fight.</summary>
    /// <summary>
    /// T109: the boiler's ruptured, and the repair kit mends it (GDD §12: the engineer is whoever has it). It rides in car 1:
    /// out of the cab, down the tender's gangway, across the plate and in at its front door for it, then back to the firebox
    /// and held there till it's mended (<see cref="KitRun"/>). The fireman goes; the driver only with nobody else in the cab
    /// to, nor on the way (the cab's gangway, the plate, car 1): the cascade audit (note 186) found the driver following the
    /// fireman out once they'd left the cab, and the two of them at the locker each undoing the other's press, all night.
    /// Null when there's nothing to mend, or someone else has the kit.
    /// </summary>
    PlayerIntent? Mend(in PlayerState self, World world, uint tick)
    {
        var train = world.Train;
        if (!train.Boiler.Ruptured || !self.Alive)
            return null;
        // Note 301: the wrench is the repair tool and everyone carries one, so it's mended where the bot stands, in the cab:
        // the fireman, or the driver with nobody else there. The wrench into hand, at the firebox, and worked till it's whole.
        if (Repairs.ByWrench(train))
        {
            if (!PlayerMotor.InCab(self, train) || !Fireman && Crewmates?.Any(c => c.Alive && PlayerMotor.InCab(c, train)) == true)
                return null;
            if (Repairs.WrenchKey(self) is var key and > 0)
                return new PlayerIntent { Select = key };
            if (!Repairs.WrenchInHand(self))
                return null;
            var fire = train.Frames[0].Shape.Interactables.First(i => i.Kind == InteractableKind.Firebox).Position;
            var (step, there) = WarmUp.Steer(self, FiringSpot(fire, Fireman ? -1 : 1), FacingFire);
            return there ? new PlayerIntent { Buttons = PlayerButtons.Use } : step;
        }
        var vehicles = train.Dynamics.Consist.Vehicles;
        int kitCar = vehicles.Count > 1 ? vehicles[1].Id : -1;
        if (!Fireman && !self.Has(PlayerFlags.RepairKit)
            && Crewmates?.Any(c => c.Alive && (PlayerMotor.InCab(c, train) || c.Parent == 0 && c.Surface is Surface.Deck or Surface.Coupler || c.Parent == kitCar && c.Surface == Surface.Deck)) == true)
            return null;
        var firebox = train.Frames[0].Shape.Interactables.First(i => i.Kind == InteractableKind.Firebox).Position;
        return KitRun.Decide(self, world, FiringSpot(firebox, Fireman ? -1 : 1), tick);
    }

    PlayerIntent? FightStoker(in PlayerState self, World world)
    {
        var train = world.Train;
        if (!self.Alive || !PlayerMotor.InCab(self, train)
            || world.ActiveEnemies.OfType<Stoker>().FirstOrDefault(st => !st.Gone && st.Phase is SpinePhase.Telegraph or SpinePhase.Commit) is not { } stoker)
            return null;
        var firebox = train.Frames[0].Shape.Interactables.First(i => i.Kind == InteractableKind.Firebox).Position;
        // On its way in from the bunker (note 263) it's in the open: at the fire, club it there.
        if (stoker.Boarding)
        {
            var (step, there) = WarmUp.Steer(self, FiringSpot(firebox, Fireman ? -1 : 1), FacingFire);
            var club = there ? new PlayerIntent { Actions = PlayerActions.Swing } : step;
            club.Buttons |= PlayerButtons.Brake;
            club.ThrottleNotch = -4;
            return club;
        }
        // In the firebox (Stoker v3, note 271): never the door (it burns), never a shovelful; vent and starve it out, the brake
        // held against its runaway.
        var intent = new PlayerIntent { Actions = PlayerActions.Vent, Buttons = PlayerButtons.Brake, ThrottleNotch = -4 };
        return intent;
    }

    PlayerIntent Work(in PlayerState self, TrainOnLine train, PlayerIntent intent)
    {
        // Fire it for pressure, and at a stand (where pressure holds up on its own while the fire burns down) keep the fire
        // itself from going low: that brings the Hollow (App. A.5). The safety valve sheds what a standing fire makes.
        // v1.1 App. A.5: a firebox door left open at a stop lets the Stoker in, and every shovelful opens it: at a stand, fire
        // only what keeps the fire from going low or the gauge from sinking (it's the low fire that brings it down the stack).
        bool standing = train.Dynamics.Speed < 0.5;
        // With steam driving (T97) the pressure is the speed: fire to what makes a little over the cruise, no more (over it,
        // the brake would be holding the train back from its own steam, and burning coal to do it).
        double fireTo = train.BoilerTuning is { SteamDrive: true } sd ? Boiler.PressureFor(sd, _cruise + 1, train.Dynamics.Tuning.MaxSpeed)
            : train.BoilerTuning is { } lb ? lb.WorkingBandMax - 2 : 0;
        // Its brake fading against the steam (note 231): no more than steam that doesn't pull at the cruise, as it's vented to.
        if (train.BoilerTuning is { SteamDrive: true } fb && train.Dynamics.BrakeEfficiency < FadedBrake && !standing)
            fireTo = Math.Min(fireTo, Boiler.PressureFor(fb, Math.Max(0, _cruise - fb.DriveSpeedBand), train.Dynamics.Tuning.MaxSpeed));
        // Only with the shovel to hand (note 275): the one off the rack, or in its own kit. Out with a crewmate, it's theirs to fire.
        if (train.BoilerTuning is { } bt && train.Boiler.Tender >= 1 && PlayerMotor.InCab(self, train) && CrewActions.HasShovel(self, train)
            && (!standing && train.Boiler.Pressure < fireTo || standing && train.Boiler.Pressure < StandingPressure
                // Never a low fire (the Stoker, App. A.5); with steam driving and the pressure well over what's wanted, only
                // just clear of low, or the surplus is speed.
                || train.Boiler.FireFraction(bt) < bt.LowFireFraction * (bt.SteamDrive && train.Boiler.Pressure > fireTo + 8 ? 1.15 : 2)))
        {
            // At its own side of the firebox door (the driver right, the fireman left: T75), clear of the vent's valve on the
            // left wall. Walking straight at the firebox from where it stood, the fireman fetched up at the vent, which was
            // then the nearest thing to hand, and never shovelled: deadLines:3's fire went out with the tender full.
            var firebox = train.Frames[0].Shape.Interactables.First(i => i.Kind == InteractableKind.Firebox).Position;
            var (step, there) = WarmUp.Steer(self, FiringSpot(firebox, Fireman ? -1 : 1), FacingFire);
            if (CrewActions.Nearest(self, train) == InteractableKind.Firebox)
                intent.Buttons |= PlayerButtons.Use;
            if (!there)
                intent = intent with { MoveX = step.MoveX, MoveZ = step.MoveZ, LookYaw = step.LookYaw };
        }
        return intent;
    }

    bool _holdingDown;

    /// <param name="over">How far over <paramref name="cruise"/> before holding it on the brake: a posted limit gets less slack.</param>
    /// <summary>The speed the driver's holding to, which the fire's kept for (T97).</summary>
    double _cruise = 14;

    /// <summary>Going backwards faster than this (m/s) when it's meant to be going on, it's rolling back.</summary>
    const double RollingBack = 0.2;

    PlayerIntent Drive(TrainOnLine train, uint tick, double cruise, double over)
    {
        _cruise = cruise;
        var d = train.Dynamics;
        // To the end of the track it's on: the terminus, or a dead line's buffer stop.
        double remaining = train.Line.PathLength(d.Path) - d.Distance;
        // With steam driving (T97) the engine pulls against the brake until the steam's down: plan the stop on the difference.
        var brakeRate = d.MaxBrakeForce / d.Consist.MassTonnes
            - (train.BoilerTuning is { SteamDrive: true } ? d.MaxTractiveForce / d.Consist.MassTonnes : 0);
        double stopping = d.Speed * d.Speed / (2 * Math.Max(0.1, brakeRate)) + 150;
        var intent = new PlayerIntent();
        // A descent runs the train away with the regulator shut; hold it on the brake, with some
        // hysteresis so it isn't hammered every tick (fade only builds while it's applied).
        if (d.Speed > cruise + over)
            _holdingDown = true;
        else if (d.Speed <= cruise)
            _holdingDown = false;
        // T107: running on, a train rolling back is one that's lost the hill (stalled on a climb, or stopped there and let go):
        // brake it to a stand. Cruising, nothing here ever braked one going backwards, and deepTerritory:1 ran back 9.5 km,
        // down the climb it had just come up, to the gate at 22 m/s.
        if (remaining < stopping || _holdingDown || d.Velocity < -RollingBack)
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
/// <summary>
/// The repair kit fetched to the firebox (T109, GDD §12), by a bot in the cab: out of the cab's back on the left, down the
/// tender's gangway, onto the footplate and the coupler plate, through car 1's front door to the kit (in its locker, note
/// 173: opened, and the kit taken off its shelf), and the same way back. Worked out afresh each tick from where it stands and whether it has the kit, so it never waits
/// on a step it's no longer at. Only the kit in the engine or the first car is fetched: further back, it's a crewmate's to
/// bring (<see cref="KitCarry"/>), and this is the way they come the last of it.
/// </summary>
public static class KitRun
{
    /// <summary>Ticks between taps at a shelf or the floor: long enough for the host's answer to come back on a rough link.</summary>
    internal const uint TapEvery = 16;

    /// <summary>This tick's intent, or null when it isn't the bot's to fetch (someone else has it, or it's out of reach).</summary>
    /// <param name="tick">The bot's own tick, which its taps are timed by (note 186, the cascade audit: the fireman stood at the
    /// open locker all night). Not the world's: a client's world re-simulates after each snapshot, so its tick can step by
    /// two, and a press "every other tick" by it was Use held down. And a tap only every <see cref="TapEvery"/> ticks: the
    /// host gives the kit on the tap, but the client hears it's in hand a snapshot later, and a bot tapping again meanwhile
    /// put it straight back on the shelf.</param>
    public static PlayerIntent? Decide(in PlayerState self, World world, Double3 firing, uint tick)
    {
        var train = world.Train;
        if (!self.Alive || self.Parent == PlayerState.World || self.Surface is Surface.Air or Surface.Ladder)
            return null;
        bool carrying = self.Has(PlayerFlags.RepairKit);
        var kit = world.Bodies.All.FirstOrDefault(b => b.Kind == Physics.BodyKind.RepairKit);
        var vehicles = train.Dynamics.Consist.Vehicles;
        int car = vehicles.Count > 1 && vehicles[0].IsEngine ? vehicles[1].Id : -1;
        if (!carrying && (kit is null || kit.Carrier >= 0 || kit.Parent != 0 && kit.Parent != car))
            return null;
        if (self.Parent != 0 && self.Parent != car)
            return null;
        var g = train.Dynamics.Tuning.Geometry;
        var route = Route(train);
        if (carrying)
        {
            if (self.Parent == 0 && PlayerMotor.InCab(self, train))
            {
                var (step, there) = WarmUp.Steer(self, firing, ConductorBot.FacingFire);
                return there ? new PlayerIntent { Buttons = PlayerButtons.Use } : step;
            }
            if (self.Parent == car)
            {
                // To the front door from inside, and out through it onto the plate.
                double front = -train.Frames[car].Shape.HalfLength;
                var door = new Double3(g.PlateX, 0, front + 0.5);
                if (self.Position.Z > door.Z + 0.15)
                    return WarmUp.Steer(self, door, 0).Step;
                if (!train.Vehicles[car].DoorOpen(0))
                    return new PlayerIntent { Buttons = PlayerButtons.Use }; // drops it to open the door; picked up again after
                return WarmUp.Steer(self, Into(train, 0, route[^1], car), 0).Step;
            }
            // Back along the route to the cab: the next point nearer the front than here.
            for (int i = route.Length - 1; i >= 0; i--)
                if (route[i].Z < self.Position.Z - RouteStep)
                    return WarmUp.Steer(self, route[i], 0).Step;
            return WarmUp.Steer(self, firing, ConductorBot.FacingFire).Step;
        }
        if (kit!.Parent == self.Parent)
            return Take(self, train, kit, tick);
        if (self.Parent != 0)
            return null; // in car 1, and the kit's been taken forward: nothing to fetch
        return ToCarOne(self, train, route, car);
    }

    /// <summary>
    /// Out of the engine (cab forward, note 276): back down the middle of the cab past the coal bunker on the left wall; left
    /// to the doorway at the cab's back corner; out onto the left running board; back along it beside the boiler; in onto the
    /// rear deck past the smokebox; the footplate off it; the plate at car 1's door. Each point's further back than the last
    /// (the way out takes the next one that is, more than <see cref="RouteStep"/> on).
    /// </summary>
    static Double3[] Route(TrainOnLine train)
    {
        var g = train.Dynamics.Tuning.Geometry;
        var plan = EnginePlan.Of(g);
        double l = plan.Half, w = g.RoofWidth / 2, d = plan.DoorFront;
        double board = -w - Math.Min(0.3, g.Engine.RunningBoardWidth / 2);
        return
        [
            new(0, 0, d + 0.35),
            new(-w + 0.4, 0, d + 0.57),
            new(board, 0, d + 0.8),
            new(board, 0, l - CarShape.BoilerToEnd - 0.4),
            new(-w + 0.35, 0, l - 0.2),
            new((-w + g.PlateX - g.CouplerWidth / 2) / 2, 0, l + g.CouplingGap * 0.25),
            new(g.PlateX, 0, l + g.CouplingGap - 0.45),
        ];
    }

    /// <summary>How much further along a route the next point has to be for the way along it to take it (m).</summary>
    const double RouteStep = 0.15;

    /// <summary>Off the engine and into car 1, for a crewmate whose place is back on the train (<see cref="KitCarry"/>); null off the engine.</summary>
    internal static PlayerIntent? BackToTheTrain(in PlayerState self, TrainOnLine train)
    {
        var vehicles = train.Dynamics.Consist.Vehicles;
        if (self.Parent != 0 || self.Surface is Surface.Air or Surface.Ladder || vehicles.Count < 2 || !vehicles[0].IsEngine)
            return null;
        return ToCarOne(self, train, Route(train), vehicles[1].Id);
    }

    /// <summary>
    /// Off the engine and into car 1 (<see cref="Decide"/>'s way out): back along <paramref name="route"/>, the door opened
    /// from the plate, and in. Also the way back for a crewmate who brought the kit up from further back (<see cref="KitCarry"/>).
    /// </summary>
    static PlayerIntent ToCarOne(in PlayerState self, TrainOnLine train, Double3[] route, int car)
    {
        var g = train.Dynamics.Tuning.Geometry;
        // In the engine, out to car 1: the next point further back than here; at the plate, its door opened, and in.
        bool inCab = PlayerMotor.InCab(self, train);
        // (Lined up with the route's first point only short of it: past it, the way turns off to the doorway.)
        if (inCab && self.Position.Z < route[0].Z - RouteStep && Math.Abs(self.Position.X - route[0].X) > 0.2)
            return WarmUp.Steer(self, route[0], Math.PI).Step;
        foreach (var p in route)
            if (p.Z > self.Position.Z + RouteStep)
                return WarmUp.Steer(self, p, Math.PI).Step;
        if (!train.Vehicles[car].DoorOpen(0))
        {
            var (step, there) = WarmUp.Steer(self, route[^1], Math.PI);
            return there ? new PlayerIntent { Buttons = PlayerButtons.Use } : step;
        }
        double carFront = -train.Frames[car].Shape.HalfLength;
        return WarmUp.Steer(self, Into(train, car, new Double3(g.PlateX, 0, carFront + 0.9), 0), Math.PI).Step;
    }

    /// <summary>
    /// The kit taken where it lies in the car the bot's in. In its locker (note 173): in front of it, facing its door; Use held
    /// till it's open, then tapped (a press, let go the next tick) to take the kit off its shelf. On the floor: beside it on
    /// the aisle side, facing it, looking down at it, a tap every <see cref="TapEvery"/> ticks (the press is the edge the host
    /// counts).
    /// </summary>
    internal static PlayerIntent Take(in PlayerState self, TrainOnLine train, Physics.Body kit, uint tick)
    {
        var g = train.Dynamics.Tuning.Geometry;
        if (kit.Stowed && kit.Locker < train.Frames[kit.Parent].Shape.Lockers.Count)
        {
            var bay = train.Frames[kit.Parent].Shape.Lockers[kit.Locker];
            var front = bay.Front;
            var stand = new Double3(front.X + bay.Facing * 0.42, 0, front.Z);
            var (step, there) = WarmUp.Steer(self, stand, bay.Facing * Math.PI / 2);
            if (!there)
                return step;
            bool open = train.Vehicles[kit.Parent].LockerOpen(bay.Index);
            return new PlayerIntent { Buttons = !open || tick % TapEvery == 0 ? PlayerButtons.Use : PlayerButtons.None };
        }
        var at = kit.Centre;
        var beside = new Double3(at.X - (at.X >= g.PlateX ? 0.6 : -0.6), 0, at.Z);
        double yaw = at.X > beside.X ? -Math.PI / 2 : Math.PI / 2;
        var (walk, by) = WarmUp.Steer(self, beside, yaw);
        return by ? new PlayerIntent { Buttons = tick % TapEvery == 0 ? PlayerButtons.Use : PlayerButtons.None, LookPitch = (float)(-0.7 - self.Pitch) } : walk;
    }

    /// <summary>A point on car <paramref name="from"/>'s floor, in car <paramref name="to"/>'s frame (flat: the steering's).</summary>
    static Double3 Into(TrainOnLine train, int from, Double3 local, int to) =>
        train.Frames[to].ToLocal(train.Frames[from].ToWorld(local));
}

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
    public string Doing => _step is Step.Warm || _step is Step.Shut && _why.Length > 0 ? $"{_step}/{_why}" : _step.ToString();
    /// <summary>What it's waiting on in there (for the harness's trace): the cold, a tunnel, or the job it's in for.</summary>
    string _why = "";
    /// <summary>Times it's been in and got warm.</summary>
    public int Done { get; private set; }

    public bool Wants(in PlayerState s) => Shelter || Into is not null ||
        s.Cold >= cold.OnsetSeconds * goInAt;

    /// <summary>
    /// Off the roofs and indoors, cold or not, for as long as it's set (a tunnel's mouth ahead, sight.json): the same way
    /// in, and it waits in there until it's clear.
    /// </summary>
    public bool Shelter { get; set; }

    /// <summary>
    /// A car to go into, cold or not (trouble in it: a fire, a loose load, Gnawers). The way in is this car's own rear
    /// door, from its roof, or from the roof of the car behind it.
    /// </summary>
    public int? Into { get; set; }

    /// <summary>What to do once in and shut in (work the trouble); null for nothing (then it warms up, and out).</summary>
    public Func<PlayerState, PlayerIntent?>? Indoors { get; set; }

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
                    // the plate is only a metre wide. Wait at the end for a straighter bit, as anyone would.
                    double end = train.Frames[self.Parent].Shape.HalfLength * (_dropYaw == 0 ? -1 : 1);
                    bool atEdge = Math.Abs(end - self.Position.Z) < 0.8;
                    bool go = Aligned(self, _dropYaw) && (!atEdge || SteadyUnder(train, self.Parent));
                    // Off the back of the last car onto its rear platform: nothing beyond it to stop you, and walked off at
                    // the roof's pace you'd carry 1.4 m in the fall, past its 1.2 to the ballast. Step off it slowly.
                    bool platform = _dropYaw != 0 && train.VehicleBehind(self.Parent) <= 0;
                    // Over the plate, not the roof's middle: it lies on the end doors' line (PlateX, left of centre), and from
                    // the centreline its edge is a hand's breadth away. deepTerritory:3's gunner stepped off car 10's front
                    // 7 cm right of the middle, went down past the plate's edge to the ballast at 14 m/s and was left on 1 hp
                    // (note 231). At the end, not off it until squarely over it.
                    var g = train.Dynamics.Tuning.Geometry;
                    double across = g.PlateX - self.Position.X;
                    go &= !atEdge || Math.Abs(across) < g.CouplerWidth / 2 - PlateMargin;
                    return new PlayerIntent { LookYaw = Turn(self, _dropYaw), MoveZ = go ? (atEdge && platform ? PlatformStep : 1) : 0, MoveX = (float)Math.Clamp(across * 0.8 * (_dropYaw == 0 ? 1 : -1), -1, 1) };
                }
            case Step.ToDoor:
                if (self.Parent != _car || self.Surface != Surface.Coupler)
                    return Abandon();
                // Just outside the door, facing it, on the plate that lies in line with it.
                return Reach(self, new Double3(_doorX, 0, _l + 0.45), 0, Step.Open);
            case Step.Open:
                if (open)
                    return Next(Step.In);
                return CrewActions.Nearest(self, train) == InteractableKind.Door ? new PlayerIntent { Buttons = PlayerButtons.Use } : Next(Step.ToDoor);
            case Step.In:
                // Someone else's hand on the same door shut it again: back to opening it.
                if (!open && self.Surface == Surface.Coupler)
                    return Next(Step.Open);
                // Straight in, not sideways: the plate's middle is the doorway's.
                return Reach(self, new Double3(_doorX, 0, _l - 1.6), 0, Step.Shut);
            case Step.Shut:
                {
                    // In for trouble: work it first (a fire's growing while the doors are shut behind us; in its first
                    // seconds, before it's taken hold, it burns nobody, App. C.5), the doors after.
                    // Inside, that is: on the plate still, the way to it is through the door it's walking at (and it never
                    // gave up, its clock kept at nothing by the work).
                    _why = "";
                    if (!ShutFirst && Into == _car && self.Parent == _car && PlayerMotor.Indoors(self, train) && Indoors?.Invoke(self) is { } first)
                    {
                        _ticks = 0;
                        _why = "busy";
                        return first;
                    }
                    // Every door shut: a car only warms you shut, and someone else may have left another open (the far end,
                    // or a cargo car's side door left open for loading).
                    var shape = train.Frames[_car].Shape;
                    var openDoor = shape.DoorList.Where(d => train.Vehicles[_car].DoorOpen(d.Index) && LeaveOpen?.Invoke(d.Index) != true
                            && Passing?.Invoke(_car, d.Index) != true)
                        .Select(d => (int?)d.Index).FirstOrDefault();
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
                _why = "busy";
                if (Indoors?.Invoke(self) is { } busy)
                    return busy;
                // Trouble broken out in here that isn't ours to work (Gnawers out of the crates bite everyone in the car, a
                // fire burns them): out, and warm somewhere else. deadLines:1's walkers sat through it and were gnawed to death,
                // too hurt to stamp them out and with nothing telling them to go (T77).
                if (Troubled?.Invoke(_car) == true)
                {
                    _outEnd = WayOut(self, train);
                    return Next(Step.Reopen);
                }
                // The car's breached (the Car Hugger through its end, Climbers in through its roof): it warms nobody until it's
                // boarded up (decided 1 Oct), so that first, at the hole. Not with the thing still at it, nor with no kit when
                // boarding needs it: out, and warm somewhere else.
                if (train.Vehicles[_car].Breached)
                {
                    if (Barred?.Invoke(_car) == true || (Repairs.ByWrench(train) ? !Kit.Has(self.Kit, Tool.Wrench) : train.Dynamics.Tuning.Breach.NeedsKit && !self.Has(PlayerFlags.RepairKit)))
                    {
                        _outEnd = WayOut(self, train);
                        return Next(Step.Reopen);
                    }
                    // Note 301: the wrench into hand first, where it's what boards it up.
                    if (Repairs.ByWrench(train) && Breaches.AtHole(self, train) is not null && Repairs.WrenchKey(self) is var key and > 0)
                        return new PlayerIntent { Select = key };
                    if (Breaches.Within(self, train) is not null)
                        return new PlayerIntent { Buttons = PlayerButtons.Use };
                    return Steer(self, Breaches.StandAt(train, _car), self.Yaw).Step;
                }
                // Someone came or went and left a door open: shut it again (a roof hatch is the crane's, T99, worked from the roof).
                int ajar = train.Vehicles[_car].DoorsOpen & ~(1 << CarShape.HatchBit);
                for (int d = 0; d < CarShape.HatchBit; d++)
                    if (LeaveOpen?.Invoke(d) == true || Passing?.Invoke(_car, d) == true)
                        ajar &= ~(1 << d);
                if (ajar != 0)
                    return Next(Step.Shut);
                if (self.Cold > WarmEnough || Shelter)
                {
                    _why = Shelter ? "shelter" : "cold";
                    return new PlayerIntent();
                }
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
                // Shut again in our face (someone warming in here): open it again rather than walk into it till we give up.
                if (!train.Vehicles[_car].DoorOpen(_outEnd > 0 ? RearDoor : FrontDoor))
                    return Next(Step.Reopen);
                return Reach(self, new Double3(_doorX, 0, _outEnd * (_l + 0.6)), _outEnd > 0 ? Math.PI : 0, Step.Out);
            default:
                _step = Step.Off;
                return null;
        }
    }

    const int FrontDoor = 0, RearDoor = 1; // doors are listed front (−Z) then rear (+Z)
    /// <summary>The share of a roof walk a walker steps off the end onto a rear platform at (0.7 m/s: 0.55 m in the fall).</summary>
    const float PlatformStep = 0.4f;
    /// <summary>How far inside the plate's edge a walker squares up before stepping off the roof onto it (note 231).</summary>
    internal const double PlateMargin = 0.2;
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
    /// <summary>
    /// Whether that way in is barred: a Rattle in the gap behind the vehicle (T54), or a Climber in the car (T58). Set by the
    /// bot, which sees the world.
    /// </summary>
    public Func<int, bool>? Barred { get; set; }
    /// <summary>A car with trouble in it (alight, or Gnawers out): nowhere to go and get warm, unless it's the trouble we're going in for.</summary>
    public Func<int, bool>? Troubled { get; set; }
    /// <summary>
    /// A door of the car it's warming in that's to be left open, by its index: the repair kit's way up the train while the
    /// boiler's ruptured (<see cref="KitCarry"/>). The cascade audit's walker in out of the cold in car 3 shut every end door
    /// the kit's bringer opened, and the kit never left car 4.
    /// </summary>
    public Func<int, bool>? LeaveOpen { get; set; }

    /// <summary>The doors shut before any work in there (the Choir's about, note 305).</summary>
    public bool ShutFirst { get; set; }
    /// <summary>
    /// A crewmate at that door of that car (car, door index), going out or coming in: it's not shut on them. A crew of eight
    /// warmed up in one car together, and whoever was still warming shut the door on each one leaving, who walked into it
    /// till it gave up 25 s later, again and again, with Fire Flies on a lamp two cars back (frontier:7, note 188).
    /// </summary>
    public Func<int, int, bool>? Passing { get; set; }

    bool Plan(in PlayerState s, TrainOnLine train)
    {
        int here = s.Parent;
        int behind = train.VehicleBehind(here), ahead = train.VehicleAhead(here);
        // The last car's own way in is its rear platform (the guard van's, when it's last: its rear door opens onto it), not a
        // plate with a car behind it. Without it nobody ever went into the guard van: Fire Flies on its lamp set it alight, the
        // walkers paced its roof with the fire under them, and it spread forward a car at a time (frontier:7, note 188).
        bool ownPlate = behind > 0 || train.Frames[here].Shape.Platform is not null;
        bool back = ownPlate && Walkable(train, here) && Barred?.Invoke(here) != true && (Into == here || Troubled?.Invoke(here) != true);
        bool front = ahead > 0 && Walkable(train, ahead) && Barred?.Invoke(ahead) != true && (Into == ahead || Troubled?.Invoke(ahead) != true);
        // A tunnel's mouth coming and the only ways in troubled (the rear car, the car ahead alight): in anyway. A fire's a
        // chance; the roof under that mouth isn't.
        if (Shelter && !back && !front)
        {
            back = ownPlate && Walkable(train, here) && Barred?.Invoke(here) != true;
            front = ahead > 0 && Walkable(train, ahead) && Barred?.Invoke(ahead) != true;
        }
        // Sent into one car in particular: its own rear door, from its roof or the roof behind it.
        if (Into is { } into && !Shelter)
        {
            back &= here == into;
            front &= ahead == into;
        }
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
        return (handle with { Y = 0 } + inward * 0.5, DMath.Atan2(inward.X, inward.Z));
    }

    /// <summary>
    /// Straight enough under a car to drop into its gap: the train's sideways pull there (v²/R) small enough that the drift
    /// across the plate in the fall stays well inside its half-width.
    /// </summary>
    static bool SteadyUnder(TrainOnLine train, int car)
    {
        var rake = train.RakeOf(car);
        var pose = train.Cars[car];
        // Its whole length (front, middle and back): one end on the straight says nothing about a curve under the other.
        double curvature = 0;
        for (int i = 0; i <= 2; i++)
            curvature = Math.Max(curvature, Math.Abs(train.Line.Sample(rake.Path, pose.FrontDistance - pose.Length * i / 2).Curvature));
        return rake.Speed * rake.Speed * curvature < MaxDropPull;
    }

    /// <summary>m/s²: at 15 m/s, a curve of 750 m radius or wider.</summary>
    const double MaxDropPull = 0.3;

    /// <summary>
    /// Whether a gap jump from where a walker stands will make the far roof: square on the centreline (the coupler plate
    /// under the gap is only a metre wide, and off its edge is the ballast), on a straight enough bit that the roof ahead
    /// doesn't swing away under the jump, and not chilled (spec B.2: slowed, a flat jump carries a fifth less, short of the
    /// far edge). Walkers who jumped off-centre from the top of an end ladder, or chilled, died between the cars.
    /// <para>
    /// The curve that matters is under both roofs, the one jumped from and the one jumped to (<paramref name="beyond"/>), not
    /// just under the near car's front: backing out of a Foundry's spur at shunting speed (6 m/s), a walker on car 1, its
    /// front just off the 60 m turnout curve, jumped back for car 2 round it, and the far roof went out from under it onto
    /// the ballast (and it was left behind there once the brakes were cut, T121).
    /// </para>
    /// </summary>
    public static bool CanJumpGap(in PlayerState self, TrainOnLine train, ColdTuning? cold, int beyond) =>
        Math.Abs(self.Position.X) < 0.3 && SteadyUnder(train, self.Parent)
        && (beyond <= 0 || SteadyUnder(train, beyond) && !Splayed(train, self.Parent, beyond))
        && (cold is null || self.Cold < cold.OnsetSeconds);

    /// <summary>
    /// Two roofs at an angle on a curve, and the train moving: a jumper carries on the way the near car was going, and the
    /// far one goes its own way, so the landing walks sideways at the speed times the angle between them for the flight.
    /// On a tight curve it's that, more than the curve's pull, that puts the far roof's edge under you, slow as it goes.
    /// </summary>
    static bool Splayed(TrainOnLine train, int car, int beyond)
    {
        Double3 a = train.Cars[car].Forward, b = train.Cars[beyond].Forward;
        double cross = a.X * b.Z - a.Z * b.X, dot = a.X * b.X + a.Z * b.Z;
        double angle = Math.Abs(DMath.Atan2(cross, dot));
        return train.RakeOf(car).Speed * angle * JumpFlight > MaxJumpDrift;
    }

    /// <summary>s: about how long a gap jump is in the air (up and back down to roof height).</summary>
    const double JumpFlight = 0.8;
    /// <summary>m: the sideways walk across the far roof a jump is let make (it's 3 m wide).</summary>
    const double MaxJumpDrift = 0.5;

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
        double fx = -DMath.Sin(self.Yaw), fz = -DMath.Cos(self.Yaw), rx = DMath.Cos(self.Yaw), rz = -DMath.Sin(self.Yaw);
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
/// What every bot heeds whatever its job (GDD v1.1): the answers that are the crew's, not a station's. Stand stock still in
/// the marsh; club a Follower off a friend's back; go and break a crewmate out of anything holding them (the rescue window,
/// App. A.1); look behind you standing about alone (Tippy Toesie); keep up a patter near the Gaunt; hush while the Choir
/// gathers; and keep talking otherwise (the Passenger never does). Worked out from what its client sees.
/// </summary>
public static class Heed
{
    /// <summary>How far from a lit Holdout a crewmate will go to breach it, with the train standing (T96).</summary>
    public const double HoldoutRange = 180;

    /// <summary>
    /// A crewmate waiting in a lit Holdout (GDD App. D.5, T96): with the train standing, the nearest walker gets down and
    /// breaches its door, and the driver waits (<see cref="CrewCalls.Breaching"/>). Then back aboard as from any stop.
    /// With no walker or gunner left alive, the driver goes itself (note 259: <see cref="CrewCalls.AnyBreacher"/>).
    /// </summary>
    public static PlayerIntent Holdouts(PlayerIntent intent, in PlayerState self, World world, int selfId, CrewCalls? calls, StopHand? hand)
    {
        calls?.CanBreach(selfId, self.Alive && hand is not null);
        if (calls is null || hand is null)
            return intent;
        if (!self.Alive || world.Holdouts is not { } hs || world.Train.Dynamics.Speed > 0.1 || self.Has(PlayerFlags.Held))
        {
            calls.DropBreach(selfId);
            return intent;
        }
        // T115 playtest ("none of the bots are coming to save me when I call out from a Halt Lockup. They're just standing at
        // a door"): only one who can get off the train now takes it. The nearest used to, from inside a car where it was
        // warming up, and couldn't get down from there (StopHand.GetDown has no way out of a car): it stood at the car's door,
        // and the claim was its, so nobody else went.
        bool free = self.Parent == PlayerState.World || self.Parent == 0 && self.Surface == Surface.Deck
            || self.Parent >= 0 && self.Surface is Surface.Roof or Surface.Coupler;
        if (!free)
        {
            calls.DropBreach(selfId);
            return intent;
        }
        var at = PlayerMotor.WorldPosition(self, world.Train);
        var lit = hs.All.Where(h => h.Lit).Select(h => (h, d: ((h.Door - at) with { Y = 0 }).Length)).Where(x => x.d <= HoldoutRange)
            .OrderBy(x => x.d).FirstOrDefault();
        if (lit.h is null || !calls.ClaimBreach(lit.h.Index, selfId, lit.d))
        {
            if (lit.h is null)
                calls.DropBreach(selfId);
            return intent;
        }
        if (hand.Breach(self, world, lit.h) is not { } breaching)
            return intent;
        // The gunner in the gun's seat gets up out of it first (T112: Jump, seated, is getting up). Note 258: a crew of two's
        // gunner took the breach from the guard gun and sat there "getting down" till the driver gave up on it.
        if (self.Has(PlayerFlags.Seated))
            breaching.Buttons |= PlayerButtons.Jump;
        return breaching;
    }

    /// <summary>A bot's voice while it's working (a crew talks: its level in the intent), and how loud it gets near the Gaunt.</summary>
    const byte Chatter = 70, ToTheGaunt = 60;

    /// <summary>
    /// The marsh (v1.1 §22, formerly the Drift): coming at us, or on us, stand stock still until it loses us ("~4s"). The look
    /// can go where it likes: it's feet it feels.
    /// </summary>
    /// <summary>
    /// GDD App. F.1's rare healing loot (note 272): a bot carrying a find that heals, hurt below loot.json
    /// <c>healing.botBelow</c>, stands still and holds Use on it till it's used (a person's way: Bodies.Dose). Never at a
    /// lever or a door, where Use would work that instead.
    /// </summary>
    public static PlayerIntent Heal(PlayerIntent intent, in PlayerState self, World world, int selfId)
    {
        if (!self.Alive || self.Has(PlayerFlags.Held) || world.Run is not { Healing: { } h } run || self.Health >= h.BotBelow
            || world.Bodies.CarriedBy(selfId) is not { } find || run.HealOf(find) <= 0
            || CrewActions.NearestInteractable(self, world.Train, world.Hand) is not null)
            return intent;
        return intent with { MoveX = 0, MoveZ = 0, Buttons = (intent.Buttons | PlayerButtons.Use) & ~(PlayerButtons.Run | PlayerButtons.Jump | PlayerButtons.Throw) };
    }

    public static PlayerIntent Drift(PlayerIntent intent, in PlayerState self, World world, int selfId)
    {
        if (!self.Alive || !world.ActiveEnemies.OfType<Drift>().Any(d => d.Target == selfId && d.Phase is SpinePhase.Telegraph or SpinePhase.Punish))
            return intent;
        return intent with { MoveX = 0, MoveZ = 0, Buttons = intent.Buttons & ~(PlayerButtons.Run | PlayerButtons.Jump) };
    }

    /// <summary>
    /// Followers (v1.1 App. A.6): "check each other's backs". A bot that sees one on a friend near it turns to it and clubs it
    /// off (the carrier can't: its world doesn't even have it). One crawling or nesting near us: club that.
    /// </summary>
    public static PlayerIntent Followers(PlayerIntent intent, in PlayerState self, World world, int selfId, CrewCalls? calls, uint tick)
    {
        if (!self.Alive || self.Has(PlayerFlags.Held))
            return intent;
        var train = world.Train;
        var me = PlayerMotor.WorldPosition(self, train);
        var near = world.ActiveEnemies.OfType<Follower>().Where(f => !f.Gone && f.Carrier != selfId)
            .Select(f => (f, To: f.WorldPosition(train) - me)).Where(x => x.To.Length <= 6).OrderBy(x => x.To.Length).FirstOrDefault();
        if (near.f is null)
            return intent;
        return Strike(self, train, near.f.WorldPosition(train), 1.8) ?? intent;
    }

    /// <summary>
    /// The rescue (v1.1 App. A.1 "kills go through GRAB"): a crewmate held by something within reach of us, we go to them,
    /// haul at them (Use: a Dragger's hang, the Car Hugger's mouth, Tippy Toesie) and swing at what holds them. The nearest of
    /// us goes; everyone near does, which is the point.
    /// </summary>
    public static PlayerIntent Rescue(PlayerIntent intent, in PlayerState self, World world, int selfId)
    {
        if (!self.Alive || self.Has(PlayerFlags.Held) || world.Enemies is not { } et)
            return intent;
        var train = world.Train;
        var me = PlayerMotor.WorldPosition(self, train);
        // A Choir ghost's seize is broken by quiet or a shut door, not blows (note 288): the bot's hush (Voice) is its rescue,
        // and swinging at one only earns its hit back.
        var holder = world.ActiveEnemies.Where(e => e.Phase == SpinePhase.Grab && e.Holding >= 0 && e.Holding != selfId && !(e is ChoirGhost && et.Choir.DrivenOff))
            .Select(e => (e, At: e.WorldPosition(train))).Where(x => (x.At - me).Length <= 25).OrderBy(x => (x.At - me).Length).FirstOrDefault();
        if (holder.e is null)
            return intent;
        // The Car Hugger's mouth is at the end of its car, under the roof's edge (App. A.3): the one it has is on the end ladder
        // or the platform below. From that car's roof, to the top of the ladder over them, and haul from there. On the roof
        // above, walking at them got no nearer, and a crew of four watched a walker eaten under their feet (note 310).
        if (holder.e is CarHugger hugger && self.Parent == hugger.Attached && self.Surface == Surface.Roof
            && train.Frames[hugger.Attached].Shape.Platform is { } platform)
        {
            var (toLadder, there) = WarmUp.Steer(self, new Double3(train.Dynamics.Tuning.Geometry.EndLadderX, 0, platform.Min.Z - 0.35), Math.PI);
            return there ? new PlayerIntent { Buttons = PlayerButtons.Use } : toLadder;
        }
        // Where the held one is: the holder's side of them is close enough (they're pinned together).
        var go = Strike(self, train, holder.At, et.Grab.PullReach * 0.8);
        if (go is not { } step)
            return intent;
        if (holder.e.PullsFree && (holder.At - me).Length <= et.Grab.PullReach)
            step.Buttons |= PlayerButtons.Use;
        return step;
    }

    /// <summary>
    /// Toward a point (in our frame, or loose in the world) to within <paramref name="reach"/>, turned to it, then a swing.
    /// Null if we can't get at it from where we are (another car).
    /// </summary>
    public static PlayerIntent? Strike(in PlayerState self, TrainOnLine train, Double3 world, double reach)
    {
        Double3 local;
        if (self.Parent >= 0 && self.Parent < train.Frames.Count)
        {
            local = train.Frames[self.Parent].ToLocal(world);
            // Not on our car: along to its end, the legs' way (not a strike).
            if (Math.Abs(local.Z) > train.Frames[self.Parent].Shape.HalfLength + 2)
                return null;
        }
        else
            local = world;
        var to = local - self.Position;
        var flat = new Double3(to.X, 0, to.Z);
        double yaw = DMath.Atan2(-flat.X, -flat.Z);
        var intent = new PlayerIntent { LookYaw = (float)Math.IEEERemainder(yaw - self.Yaw, 2 * Math.PI) };
        if (flat.Length > reach)
        {
            intent.MoveZ = 1;
            intent.Buttons = PlayerButtons.Run;
            return intent;
        }
        intent.Actions = PlayerActions.Swing;
        return intent;
    }

    /// <summary>Health a bot wants before it wades into a pack fight (a hound bites for 45).</summary>
    public const int PackFightHealth = 55;

    /// <summary>
    /// Cinder Hounds aboard (v1.1 App. A.3 PACK FIGHT): "each takes several bludgeons", so everyone near enough and fit
    /// enough goes at the nearest, swinging; hurt, a bot keeps its distance (they only bite what's within their reach).
    /// </summary>
    public static PlayerIntent Hounds(PlayerIntent intent, in PlayerState self, World world, int selfId)
    {
        if (!self.Alive || self.Has(PlayerFlags.Held) || self.Parent < 0)
            return intent;
        var train = world.Train;
        var me = PlayerMotor.WorldPosition(self, train);
        var hound = world.ActiveEnemies.OfType<CinderHound>().Where(h => !h.Gone && h.Attached >= 0 && h.Phase is SpinePhase.Telegraph or SpinePhase.Commit)
            .Select(h => (h, At: h.WorldPosition(train))).Where(x => (x.At - me).Length <= 20).OrderBy(x => (x.At - me).Length).FirstOrDefault();
        if (hound.h is null || self.Health < PackFightHealth)
            return intent;
        return Strike(self, train, hound.At, 1.6) ?? intent;
    }

    /// <summary>
    /// Tippy Toesie (v1.1 App. A.5): "watch each other's backs". Standing about, a bot glances round now and then; and if it
    /// can see the thing tiptoeing at someone, it looks at it (the target's the one whose look sends it off, so it calls it,
    /// through the calls' stand-in) and goes for it.
    /// </summary>
    public static PlayerIntent Backs(PlayerIntent intent, in PlayerState self, World world, int selfId, uint tick)
    {
        if (!self.Alive || self.Has(PlayerFlags.Held))
            return intent;
        var train = world.Train;
        var me = PlayerMotor.WorldPosition(self, train);
        if (world.ActiveEnemies.OfType<TippyToesie>().FirstOrDefault(t => !t.Hidden && t.Phase is SpinePhase.Telegraph or SpinePhase.Commit) is { } tippy)
        {
            var at = tippy.WorldPosition(train);
            if (tippy.Target == selfId || (at - me).Length <= 8)
            {
                // It's after us or near: turn and look at it (seen, it flees), and hit it if we're on it.
                return Strike(self, train, at, 1.2) ?? intent;
            }
        }
        // Idle: a glance round every few seconds, a half turn. Not while holding something to (the kit at the firebox for its
        // 25 s, a door, the gun's bore): idle as the world counts it (World.CrewAct), or the glance lets go of it (the cascade
        // audit, note 186: a mend that started over every 4 s, all night).
        if (intent.MoveX == 0 && intent.MoveZ == 0 && intent.LookYaw == 0 && intent.Buttons == PlayerButtons.None && intent.Actions == PlayerActions.None
            && (tick + (uint)selfId * 37) % (4 * SimConstants.TickRate) < SimConstants.TickRate / 3)
            intent.LookYaw = (float)(Math.PI / 10);
        return intent;
    }

    /// <summary>
    /// A hot axle box (note 331): a bot that finds itself in reach of one (a walker through the gap behind its car, a rider
    /// down on the ground at a stop) stops and greases it. Not the crew on the engine (the driver's at the controls), and not
    /// a bot busy with its hands. A rescue (<see cref="Rescue"/>, after this) comes first.
    /// </summary>
    public static PlayerIntent HotBox(PlayerIntent intent, in PlayerState self, World world)
    {
        if (!self.Alive || self.Has(PlayerFlags.Held) || self.Parent == 0 || intent.Buttons != PlayerButtons.None
            || world.Train.HotBoxTuning is not { Enabled: true } t || HotBoxes.Within(self, world.Train, t) is null)
            return intent;
        return new PlayerIntent { Buttons = PlayerButtons.Use };
    }

    /// <summary>
    /// The Whistler (v1.1 App. A.4): "check the gaps after the whistle; move in pairs at stops". With one watching its gap, a
    /// bot doesn't go past it: it steps back out of reach of the gap and goes round another way.
    /// </summary>
    public static PlayerIntent Gaps(PlayerIntent intent, in PlayerState self, World world, int selfId)
    {
        if (!self.Alive || self.Has(PlayerFlags.Held) || world.Enemies is not { } et)
            return intent;
        var train = world.Train;
        var me = PlayerMotor.WorldPosition(self, train);
        foreach (var w in world.ActiveEnemies.OfType<Whistler>())
        {
            if (w.Gone || w.Phase is not (SpinePhase.Telegraph or SpinePhase.Commit) || w.Attached < 0)
                continue;
            var gap = w.WorldPosition(train);
            double d = (gap - me).Length;
            if (d > et.Whistler.PassReach + 2)
                continue;
            // A client doesn't know for certain who's beside it; a bot plays safe and keeps clear either way.
            var away = me - gap;
            var local = self.Parent >= 0 ? train.Frames[self.Parent].DirToLocal(away) : away;
            var target = self.Position + new Ballast.Double3(local.X, 0, local.Z).Normalized * 3;
            return WarmUp.Steer(self, target, self.Yaw).Step;
        }
        return intent;
    }

    /// <summary>
    /// Fire Flies (v1.1 App. A.5): "lamps off when they swarm". In the car they're swarming, a bot puts its lamp out (a press,
    /// every other tick, until it's out: the press is the host's to count).
    /// </summary>
    public static PlayerIntent Flies(PlayerIntent intent, in PlayerState self, World world, uint tick)
    {
        if (!self.Alive || self.Has(PlayerFlags.Held) || self.Parent <= 0 || self.Parent >= world.Train.Vehicles.Count
            || !world.Train.Vehicles[self.Parent].LampLit || !PlayerMotor.Indoors(self, world.Train))
            return intent;
        int car = self.Parent;
        if (world.ActiveEnemies.Any(e => e is FireFlies f && !f.Gone && f.Attached == car) && tick % 2 == 0)
            intent.Actions |= PlayerActions.CarLamp;
        return intent;
    }

    /// <summary>
    /// The voice (v1.1 App. C.7-C.8): a bot talks as it works (crews do; the Passenger never does), talks to the Gaunt when
    /// it's near one, and goes quiet while the Choir gathers or is here.
    /// </summary>
    public static PlayerIntent Voice(PlayerIntent intent, in PlayerState self, World world, int selfId, uint tick)
    {
        if (!self.Alive)
            return intent;
        var train = world.Train;
        var me = PlayerMotor.WorldPosition(self, train);
        bool hush = world.Choir.Build > 0.15 || world.Choir.Present;
        bool gaunt = world.ActiveEnemies.OfType<Gaunt>().Any(g => !g.Gone && g.Phase is SpinePhase.Telegraph or SpinePhase.Commit
            && (g.WorldPosition(train) - me).Length <= (world.Enemies?.Gaunt.ListenRadius ?? 8));
        if (gaunt)
            intent.Voice = ToTheGaunt; // quietly, to it: the Choir punishes noise, and it punishes silence
        else if (!hush && (tick + (uint)selfId * 53) % (6 * SimConstants.TickRate) < SimConstants.TickRate)
            intent.Voice = Chatter;
        else
            intent.Voice = 0;
        return intent;
    }
}
