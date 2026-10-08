using Ballast;
using Ballast.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Physics;

/// <summary>Crate and lamp are the train's own stores; cargo is freight from a facility (spec D.2 manual crates).</summary>
/// <summary><see cref="Radio"/> is a walkie-talkie (T41, spec A.5): worn on the belt, not carried in the hands.</summary>
/// <summary><see cref="Heavy"/> is freight that takes two to lift (spec D.2 "heavy items need two", T43).</summary>
/// <summary>
/// GDD v1.1 App. C.4 hand-carried loot: a <see cref="Toy"/> (the Track Doll steals one and goes), <see cref="Loot"/> (salvage:
/// what the Gaunt takes, what the Followers nest by; a village find, level-design P12, pocketable and paying when stowed
/// aboard, its <see cref="Body.Owner"/> saying which find), a rescued <see cref="Child"/> survivor (carried by hand, the most
/// valuable cargo there is), and each car's wall-mounted <see cref="Extinguisher"/> (App. C.5).
/// </summary>
/// <summary>
/// <see cref="RepairKit"/> is the engineer's toolbox (GDD §12 "the repair kit is an item, not a station"): whoever carries
/// it opens a Holdout's lock quietly (App. D.7), and when they die it's lying where they fell. <see cref="Powder"/> is a charge
/// for a gun's ready rack (note 374), taken from a powder locker and carried up to the gun.
/// </summary>
public enum BodyKind : byte { Crate = 1, Lamp = 2, Ragdoll = 3, Cargo = 4, Radio = 5, Heavy = 6, Toy = 7, Loot = 8, Child = 9, Extinguisher = 10, RepairKit = 11, Powder = 12 }

/// <summary>
/// What a toy sounds like while it's carried (GDD v1.4 §19, App. C.7): most are quiet; a squeaker, a music box and a wind-up
/// drummer feed the crew loudness meter in the carrier's name (ARCHITECTURE §8 note 175).
/// </summary>
public enum ToyNoise : byte { None = 0, Squeaker = 1, MusicBox = 2, Drummer = 3 }

/// <summary>
/// A loose physical thing: cargo, a tool, a crewmate's body. It lives in a car's frame while it touches that
/// car, and in the world frame in flight, exactly like a player (ARCHITECTURE §6.1): a crate on a roof at
/// 22 m/s is still, and one thrown off the side lands behind the train with the train's speed.
/// </summary>
public sealed class Body
{
    public Body(int id, BodyKind kind, int parent, PbdBody pbd)
    {
        Id = id;
        Kind = kind;
        Parent = parent;
        Pbd = pbd;
    }

    public int Id { get; }
    public BodyKind Kind { get; }
    /// <summary>Vehicle id, or <see cref="PlayerState.World"/>.</summary>
    public int Parent { get; set; }
    public PbdBody Pbd { get; }
    /// <summary>Player carrying it, or −1.</summary>
    public int Carrier { get; set; } = -1;
    /// <summary>
    /// A heavy crate's other carrier, or −1 (T43). With one on it, it's held but not lifted; with both, it rides between them.
    /// </summary>
    public int Second { get; set; } = -1;
    /// <summary>
    /// The thing carrying it off (an enemy's id), or −1: the Gaunt leaving with what it took (GDD App. A.6, "it carries the
    /// body out at walking pace, in full view, and the crew can still chase it down"). Nobody picks it up out of its hands;
    /// kill it and it's dropped.
    /// </summary>
    public int TakenBy { get; set; } = -1;
    /// <summary>Whether a player has hold of it (either end of a heavy crate).</summary>
    public bool HeldBy(int playerId) => Carrier == playerId || Second == playerId;
    /// <summary>Off the ground in someone's hands: a heavy crate only with both ends taken.</summary>
    public bool Lifted => Carrier >= 0 && (Kind != BodyKind.Heavy || Second >= 0);
    /// <summary>For a ragdoll, whose body it is.</summary>
    public int Owner { get; set; } = -1;
    /// <summary>A ragdoll left by a player who disconnected, not one who died (GDD App. D.2, D.9).</summary>
    public bool DroppedOut { get; set; }
    /// <summary>Facing, for drawing single-point bodies (crates, lamps); tumbles in flight.</summary>
    public double Yaw { get; set; }
    /// <summary>
    /// Drawn only: its up (world) when it isn't its parent frame's, as a load thrown into the wreck film tumbles (note 370);
    /// zero for upright. <see cref="Yaw"/> turns it about this.
    /// </summary>
    public Double3 Tilt { get; set; }
    public double Spin { get; set; }
    /// <summary>An extinguisher's charge, 0..1 (App. C.5: limited, and it recharges slowly on its mount).</summary>
    public double Charge { get; set; } = 1;
    /// <summary>An extinguisher's car: its mount is there (the car it hangs in), or −1.</summary>
    public int Home { get; set; } = -1;
    /// <summary>
    /// Facility freight's cargo (App. B.8: the facility's, facilities.json "cargo"), so its crate shows what's in it (GDD §19
    /// "physically aboard and readable"); <see cref="CargoKind.None"/> for a stop's loot crates and everything else.
    /// </summary>
    public CargoKind Cargo { get; set; }
    /// <summary>A radio smashed in a fall, a grab or a blow (GDD §23 "radio breaks"; note 183): carried, but dead.</summary>
    public bool Broken { get; set; }
    /// <summary>
    /// A broken radio being mended with the repair kit (note 201): how many ticks Use has been held at it, 0 when nobody is.
    /// On the body record, so the mender's HUD shows how far it's got. A healing find being used from the hands (note 272):
    /// how many ticks it's been held at it, standing.
    /// </summary>
    public int MendTicks { get; set; }
    /// <summary>A toy's noise (App. C.7): <see cref="ToyNoise.None"/> for a quiet one and everything that isn't a toy.</summary>
    public ToyNoise Noise { get; set; }
    /// <summary>
    /// For a ragdoll, the tools its player was carrying when they died (GDD v1.4 App. D.2: the body keeps everything,
    /// the engineering kit included), packed like <see cref="PlayerState.Kit"/>. Whoever lifts the body takes them.
    /// </summary>
    public ulong Tools { get; set; }
    public bool HasTool(Tool tool) => Player.Kit.Has(Tools, tool);
    /// <summary>
    /// The crew locker it's on a shelf of (its car's <see cref="CarShape.Lockers"/> index, ARCHITECTURE §8 note 173), or −1.
    /// Stowed, it's the locker's: still in its car's frame, at its shelf (<see cref="Lockers.SlotAt"/>), out of the physics
    /// and out of reach but through the locker's open door.
    /// </summary>
    public int Locker { get; set; } = -1;
    /// <summary>Which shelf of its locker, from the bottom.</summary>
    public int Slot { get; set; }
    public bool Stowed => Locker >= 0;
    /// <summary>
    /// The crew's: stocked aboard at the fortress, or in someone's hands since. A repair kit found at a stop is the crew's
    /// once it's been picked up (GDD v1.4 §23.2 counts the kits the crew have; one lying unfound in a village isn't one).
    /// Host only.
    /// </summary>
    public bool Claimed { get; set; }
    internal int Airborne;
    internal double LineHint;
    public Double3 Centre => Pbd.Centre;
}

/// <summary>Carry, throw and body tuning (content/tuning/player.json "hands").</summary>
public sealed record HandsTuning(double Reach, double ThrowSpeed, double RagdollThrowSpeed, double CarryHeight, double CarryForward);

/// <summary>
/// Host-simulated loose bodies (GDD §33: thrown objects, cargo, ragdolls, bodies). Clients mirror them from
/// Body records. A dead player's body persists where they fell and can be carried: brought home aboard, it refunds most
/// of its crew-loss fee (GDD App. D.9).
/// </summary>
public sealed class Bodies
{
    readonly List<Body> _bodies = new();
    readonly Dictionary<int, bool> _useWas = new(), _throwWas = new();
    // How many ticks each player has held Use at a locker, from the press (a tap stows or takes; a hold works the door).
    readonly Dictionary<int, int> _lockerHeld = new();
    // Who's holding Use with the repair kit at a broken radio (note 201): which radio, and for how many ticks from the press
    // (a tap puts the kit down; a hold mends).
    readonly Dictionary<int, (int Radio, int Ticks)> _mending = new();
    // Who's holding Use on a healing find in their hands (note 272): which find, and for how many ticks from the press (a tap
    // puts it down; a hold uses it).
    readonly Dictionary<int, (int Find, int Ticks)> _dosing = new();
    // Health given back this tick by a find used up (host), by player, for the world to add (World.CrewAct).
    readonly Dictionary<int, int> _doses = new();
    int _nextId = 1;
    const double Dt = SimConstants.TickSeconds;
    const double NearbyCar = 40;

    public IReadOnlyList<Body> All => _bodies;
    public HandsTuning Hands { get; set; } = new(1.6, 9, 4, 1.15, 0.6);

    /// <summary>
    /// How much health a body gives back used from the hands (GDD App. F.1's rare healing loot: a stop's bandages, medicine,
    /// morphine; note 272), 0 for anything else. The world sets it with the run's loot (<see cref="Run.Run.HealOf"/>).
    /// </summary>
    public Func<Body, int>? Heals { get; set; }
    /// <summary>How long Use is held, standing, to use a healing find (loot.json <c>healing.useSeconds</c>).</summary>
    public double HealSeconds { get; set; } = 2;
    /// <summary>A player's full health (player.json <c>health</c>): a find isn't used on a player who's whole.</summary>
    public int FullHealth { get; set; } = 100;

    /// <summary>Host: the health a find used up this tick gives <paramref name="playerId"/> (0 for none), taken once.</summary>
    public int TakeDose(int playerId) => _doses.Remove(playerId, out int amount) ? amount : 0;

    /// <summary>
    /// The radios are things (T41): only someone wearing one talks or hears on the radio. False until the train's
    /// been stocked with them (<see cref="World.Stock"/>), when the radio's just a button everyone has.
    /// </summary>
    public bool RadiosCarried { get; set; }

    /// <summary>Whether a player can use the radio: wearing one, or everyone while radios aren't things.</summary>
    public bool HasRadio(int playerId) => !RadiosCarried || _bodies.Any(b => b.Kind == BodyKind.Radio && b.Carrier == playerId && !b.Broken);

    /// <summary>What a player carries in their hands (a radio's on the belt, not in them).</summary>
    public Body? CarriedBy(int playerId) => _bodies.FirstOrDefault(b => b.HeldBy(playerId) && b.Kind != BodyKind.Radio);

    /// <summary>A heavy crate's span (T43): its carriers this far apart at most, hands to hands, or it's down.</summary>
    public double HeavySpan { get; set; } = 2.4;

    public Body SpawnCrate(TrainOnLine train, int car, Double3 local, BodyKind kind = BodyKind.Crate)
    {
        // The toolbox is a flat thing (train_stores.py's repair_kit, 0.2 m high): it lies on the floor, not a hand over it.
        double radius = kind switch { BodyKind.Crate or BodyKind.Child => 0.35, BodyKind.RepairKit => 0.1, _ => 0.15 };
        var pbd = new PbdBody([new Particle(local + Double3.Up * radius, 1, radius)]) { Friction = 0.2, Bounce = 0.1 };
        // Stocked aboard: the crew's from the start.
        var b = new Body(_nextId++, kind, car, pbd) { LineHint = train.Cars[Math.Max(0, car)].FrontDistance, Claimed = true };
        _bodies.Add(b);
        return b;
    }

    /// <summary>
    /// Puts a thing on a locker's first free shelf (ARCHITECTURE §8 note 173): out of whoever's hands, into its car's frame,
    /// lying along the locker (a toolbox's length goes in deep), asleep and out of the physics. False if it's full, or the
    /// thing isn't one that goes in.
    /// </summary>
    public bool Stow(Body b, TrainOnLine train, int car, int locker)
    {
        var shape = train.Frames[car].Shape;
        if (locker < 0 || locker >= shape.Lockers.Count || !Lockers.Holds(train, b.Kind) || b.Pbd.Particles.Length != 1)
            return false;
        int slot = Lockers.FreeSlot(this, train, car, locker);
        if (slot < 0)
            return false;
        if (b.Parent != car)
        {
            if (b.Parent != PlayerState.World)
                ToWorld(b, train, b.Parent);
            ToCar(b, train, car);
        }
        b.Carrier = b.Second = -1;
        b.Locker = locker;
        b.Slot = slot;
        b.Airborne = 0;
        b.Spin = 0;
        b.Yaw = 0;
        ref var p = ref b.Pbd.Particles[0];
        p.InverseMass = 1;
        p.Position = p.Previous = Lockers.SlotAt(shape.Lockers[locker], slot, Lockers.Slots(train), p.Radius);
        p.Contact = true;
        b.Pbd.Sleep();
        return true;
    }

    /// <summary>The top thing off a locker's shelves, into a player's hands; null when it's empty.</summary>
    public Body? Take(TrainOnLine train, int car, int locker, int playerId)
    {
        if (Lockers.Contents(this, car, locker).LastOrDefault() is not { } b)
            return null;
        b.Locker = -1;
        b.Carrier = playerId;
        b.Claimed = true;
        b.Pbd.Wake();
        return b;
    }

    /// <summary>What hand loot is worth (GDD v1.1 §19), for what goes after "the most valuable item".</summary>
    public static double Value(BodyKind kind) => kind switch
    {
        BodyKind.Child => 10,
        // GDD v1.4 App. D.9: "a body is valued at its refund when enemies rank loot": the Gaunt can take it (and the kit on it,
        // §23.2), and Followers nest in its car. Above the medicine (§23.1: "the Gaunt choosing a corpse over the medicine"),
        // below a living child.
        BodyKind.Ragdoll => 5,
        BodyKind.Loot => 3,
        BodyKind.Cargo or BodyKind.Heavy => 2,
        BodyKind.Toy => 1,
        _ => 0,
    };

    /// <summary>
    /// What this body is worth to what ranks loot: <see cref="Value(BodyKind)"/>, and a noisy toy a half more than a quiet
    /// one (GDD v1.4 App. C item 4: "worth more than quiet toys").
    /// </summary>
    public static double Value(Body b) => Value(b.Kind) * (b.Kind == BodyKind.Toy && b.Noise != ToyNoise.None ? 1.5 : 1);

    /// <summary>
    /// What this body is worth to a creature that takes or eats loot (the Gaunt, the Followers): its <see cref="Value(Body)"/>,
    /// but nothing for a rescued child, who "cannot be harmed" (GDD App. A.6 REAL; note 182). It's the most valuable thing
    /// aboard, and no creature carries it off or nests over it.
    /// </summary>
    public static double Prey(Body b) => b.Kind == BodyKind.Child ? 0 : Value(b);

    /// <summary>A small thing on the ground at a facility (a toy, salvage, a child), in the world frame.</summary>
    public Body SpawnItem(Double3 world, double lineHint, BodyKind kind)
    {
        double radius = kind switch { BodyKind.Child => 0.35, BodyKind.RepairKit => 0.1, _ => 0.15 };
        var pbd = new PbdBody([new Particle(world + Double3.Up * radius, 1, radius)]) { Friction = 0.35, Bounce = 0.05 };
        var b = new Body(_nextId++, kind, PlayerState.World, pbd) { LineHint = lineHint };
        _bodies.Add(b);
        return b;
    }

    /// <summary>A crate of freight on the ground at a facility, in the world frame; given a <paramref name="heavy"/> radius, one that takes two; holding <paramref name="cargo"/>.</summary>
    public Body SpawnCargo(Double3 world, double lineHint, double? heavy = null, CargoKind cargo = CargoKind.None)
    {
        double radius = heavy ?? 0.45;
        var pbd = new PbdBody([new Particle(world + Double3.Up * radius, 1, radius)]) { Friction = 0.35, Bounce = 0.05 };
        var b = new Body(_nextId++, heavy is null ? BodyKind.Cargo : BodyKind.Heavy, PlayerState.World, pbd) { LineHint = lineHint, Cargo = cargo };
        _bodies.Add(b);
        return b;
    }

    /// <summary>
    /// A village find lying where it was hidden (level-design P12, P14), in the world frame. <paramref name="owner"/> says
    /// which find it is (<see cref="Run.Run.LootOwner"/>), so a client can name it from its own copy of the economy.
    /// </summary>
    public Body SpawnLoot(Double3 world, double lineHint, int owner, double radius)
    {
        var pbd = new PbdBody([new Particle(world + Double3.Up * radius, 1, radius)]) { Friction = 0.4, Bounce = 0.05 };
        var b = new Body(_nextId++, BodyKind.Loot, PlayerState.World, pbd) { LineHint = lineHint, Owner = owner };
        _bodies.Add(b);
        return b;
    }

    /// <summary>Bone layout: head, chest, pelvis, elbows, hands, knees, feet (standing, in a player's frame).</summary>
    internal static readonly (Double3 At, double Radius)[] Skeleton =
    [
        (new(0, 1.62, 0), 0.13), (new(0, 1.35, 0), 0.16), (new(0, 0.95, 0), 0.15),
        (new(-0.28, 1.12, 0), 0.08), (new(-0.3, 0.85, 0), 0.07), (new(0.28, 1.12, 0), 0.08), (new(0.3, 0.85, 0), 0.07),
        (new(-0.12, 0.5, 0), 0.09), (new(-0.12, 0.08, 0), 0.08), (new(0.12, 0.5, 0), 0.09), (new(0.12, 0.08, 0), 0.08),
    ];

    internal static readonly (int A, int B, double Stiffness)[] Bones =
    [
        (0, 1, 1), (1, 2, 1), (1, 3, 1), (3, 4, 1), (1, 5, 1), (5, 6, 1), (2, 7, 1), (7, 8, 1), (2, 9, 1), (9, 10, 1),
        // Soft braces so it folds like a body rather than a chain: head over pelvis, the hips and shoulders apart.
        (0, 2, 0.3), (7, 9, 0.5), (3, 5, 0.5), (1, 7, 0.2), (1, 9, 0.2),
    ];

    /// <summary>A body where a player died, moving as they were (spec C.1: it persists at the death location).</summary>
    public Body SpawnRagdoll(TrainOnLine train, int owner, in PlayerState dead)
    {
        double c = DMath.Cos(dead.Yaw), s = DMath.Sin(dead.Yaw);
        var velocity = dead.Velocity;
        var at = dead.Position;
        var particles = Skeleton.Select(j =>
        {
            var local = at + new Double3(j.At.X * c + j.At.Z * s, j.At.Y, -j.At.X * s + j.At.Z * c);
            var p = new Particle(local, 1, j.Radius);
            p.SetVelocity(velocity, Dt);
            return p;
        }).ToArray();
        // Nobody dies standing straight: the head and chest go over backwards, or it folds into a neat pile.
        var backwards = new Double3(DMath.Sin(dead.Yaw), 0, DMath.Cos(dead.Yaw));
        particles[0].SetVelocity(velocity + backwards * 1.6, Dt);
        particles[1].SetVelocity(velocity + backwards * 1.0, Dt);
        var bones = Bones.Select(b => new DistanceConstraint(b.A, b.B, (Skeleton[b.A].At - Skeleton[b.B].At).Length, b.Stiffness)).ToArray();
        var body = new Body(_nextId++, BodyKind.Ragdoll, dead.Parent, new PbdBody(particles, bones) { Friction = 0.25, Bounce = 0.05 })
        {
            Owner = owner,
            LineHint = dead.LineHint,
            Tools = dead.Kit,
        };
        _bodies.Add(body);
        return body;
    }

    public bool HasRagdoll(int owner) => _bodies.Any(b => b.Kind == BodyKind.Ragdoll && b.Owner == owner);

    /// <summary>
    /// Lifting a body takes the tools off it, into the lifter's empty slots (GDD v1.4 §12: "they go looking for the
    /// engineer", and whoever finds the engineer has the kit). The wrench and the shovel first (note 275); what doesn't fit
    /// stays on the body.
    /// </summary>
    public static void TakeTools(ref PlayerState s, Body body)
    {
        if (body.Tools == 0)
            return;
        ulong kit = s.Kit, left = 0;
        foreach (var tool in Enumerable.Range(0, Player.Kit.Slots).Select(i => Player.Kit.At(body.Tools, i)).Where(t => t != Tool.None)
            .OrderBy(t => t is Tool.Wrench or Tool.Shovel ? 0 : 1))
        {
            // A spare crowbar isn't worth a slot; the wrench and the shovel (there's the one of each) always are.
            if (tool is not (Tool.Wrench or Tool.Shovel) && Player.Kit.Has(kit, tool) || !Player.Kit.TryAdd(ref kit, tool))
                Player.Kit.TryAdd(ref left, tool);
        }
        s.Kit = kit;
        body.Tools = left;
    }

    // Who has a body for the death they're in now (one body per death, GDD App. D.9: die twice, leave two).
    readonly HashSet<int> _bodied = [];

    /// <summary>In-run deaths so far, each with its body (drop-outs aren't deaths: D.2). Host only.</summary>
    public int Deaths { get; private set; }

    // Deaths counted before the body goes down (a derailment's, on the derail tick: World.ApplyDamage).
    readonly HashSet<int> _ahead = [];

    /// <summary>
    /// Counts <paramref name="owner"/>'s death now, though their body goes down later: a derailment's crew, whose deaths the
    /// settlement fixes on the derail tick and who each die on their own hit in the wreck (GDD v1.4 App. E.2 step 1, the
    /// director's decision of 5 Oct 2026). The body still comes when they die; it isn't counted twice.
    /// </summary>
    public void CountAhead(int owner)
    {
        if (_ahead.Add(owner))
            Deaths++;
    }

    /// <summary>Host: a body for everyone who died this tick (not a mid-run joiner still waiting: they've no body).</summary>
    /// <returns>Who died this tick, and the body each left.</returns>
    public List<(int Id, PlayerState State, Body Body)> OnDeaths(TrainOnLine train, IEnumerable<(int Id, PlayerState State)> crew)
    {
        var died = new List<(int, PlayerState, Body)>();
        foreach (var (id, s) in crew)
        {
            if (s.Alive)
            {
                _bodied.Remove(id);
                continue;
            }
            if (s.Death == DeathCause.Waiting || !_bodied.Add(id))
                continue;
            died.Add((id, s, SpawnRagdoll(train, id, s)));
            if (!_ahead.Remove(id))
                Deaths++;
        }
        return died;
    }

    /// <summary>
    /// A disconnected player's inert body (D.2): its kit can be recovered, but it carries no fee and no refund. What they had
    /// in their hands or on their belt goes down with them: nobody's left to carry it, and the train's one repair kit
    /// mustn't hang in the air where they stood.
    /// </summary>
    public Body DropOut(TrainOnLine train, int owner, in PlayerState left)
    {
        // (A heavy crate's end is let go by CarryHeavy, which sees its carrier gone.)
        foreach (var held in _bodies.Where(x => x.Carrier == owner && x.Kind != BodyKind.Heavy).ToList())
            Release(held, left, train, 0);
        var b = SpawnRagdoll(train, owner, left);
        b.DroppedOut = true;
        return b;
    }

    /// <summary>
    /// A player's hands, on the host, before crew actions: Use (pressed) picks up the nearest body in reach
    /// or puts down what you're carrying; Throw throws it where you're looking. Returns true if it took the
    /// Use press, so the press doesn't also work a lever.
    /// <para>
    /// A radio picked up goes on the belt (one each), leaving the hands free; Throw with empty hands takes it off and
    /// sets it down, to pass to someone. The dead drop theirs where they fall, still squawking (T41).
    /// </para>
    /// </summary>
    /// <param name="hand">The hand tuning, when hands are reported (T29): a reaching hand takes what it's on.</param>
    /// <param name="keep">
    /// Use is working what's carried, not putting it down: the repair kit at a Holdout's lock (App. D.7) or a ruptured
    /// boiler's firebox (T109). The press isn't taken, so the work goes on from this tick, the same on a predicting client.
    /// </param>
    /// <param name="lever">
    /// At a switch's lever, a stand's or the cab's thrower's (queue #94, note 357): Use is the lever's, so what's carried stays
    /// carried and nothing lying by it is picked up. Throw still lets go.
    /// </param>
    /// <param name="fetch">Empty hands here take a new one of these on a Use press (a powder locker's charge, note 374).</param>
    public bool Handle(in PlayerState s, in PlayerIntent intent, int playerId, TrainOnLine train, HandTuning? hand = null, bool keep = false,
        bool lever = false, BodyKind? fetch = null)
    {
        bool use = intent.Has(PlayerButtons.Use), thrown = intent.Has(PlayerButtons.Throw);
        bool usePressed = use && !_useWas.GetValueOrDefault(playerId);
        bool throwPressed = thrown && !_throwWas.GetValueOrDefault(playerId);
        _useWas[playerId] = use;
        _throwWas[playerId] = thrown;
        var carried = CarriedBy(playerId);
        var worn = _bodies.FirstOrDefault(b => b.Kind == BodyKind.Radio && b.Carrier == playerId);
        if (!s.Alive)
        {
            if (carried is not null)
                Release(carried, s, train, 0);
            if (worn is not null)
                Release(worn, s, train, 0);
            return false;
        }
        if (carried is not null && (keep || lever) && !throwPressed)
            return false;
        // At a locker's door (note 173), Use is the locker's: tapped, what's in your hands goes on a shelf (or the top thing
        // comes off one); held, CrewActions works the door. So the hands wait for the release to know which it was, and
        // never take the press (the door's hold is worked the same on a predicting client, which has no hands).
        if (!throwPressed && (carried is null || Lockers.Holds(train, carried.Kind)) && Lockers.AtHand(s, train, hand) is { } locker)
        {
            if (usePressed)
                _lockerHeld[playerId] = 1;
            else if (use && _lockerHeld.ContainsKey(playerId))
                _lockerHeld[playerId]++;
            else if (!use && _lockerHeld.Remove(playerId, out int held) && held * Dt < Lockers.DoorSeconds(train) - Dt / 2)
            {
                // Note 267 ("put in the locker doesn't seem to be working well"): a tap at a shut locker opens it, and what's
                // in your hands goes in all the same; empty-handed, the tap only opens it, so you see what's there first.
                var v = train.Vehicles[locker.Car];
                bool open = v.LockerOpen(locker.Bay.Index);
                if (!open)
                    v.ToggleLocker(locker.Bay.Index);
                if (carried is not null)
                    Stow(carried, train, locker.Car, locker.Bay.Index);
                else if (open)
                    Take(train, locker.Car, locker.Bay.Index, playerId);
            }
            return false;
        }
        _lockerHeld.Remove(playerId);
        if (Mend(s, intent, playerId, train, hand, carried, use, usePressed, throwPressed))
            return false;
        if (Dose(s, intent, playerId, train, hand, carried, use, usePressed, throwPressed))
            return false;
        if (carried is not null && (throwPressed || usePressed))
        {
            // Nobody throws a heavy crate: either of you lets go, and it's down.
            double speed = !throwPressed || carried.Kind == BodyKind.Heavy ? 0 : carried.Kind == BodyKind.Ragdoll ? Hands.RagdollThrowSpeed : Hands.ThrowSpeed;
            Release(carried, s, train, speed);
            return usePressed || carried.Kind == BodyKind.Heavy;
        }
        if (carried is null && throwPressed && worn is not null)
        {
            Release(worn, s, train, 0);
            return false;
        }
        if (!usePressed || carried is not null || lever || intent.MoveZ > 0.5)
            return false;
        // A charge from the powder locker (note 374): into the hands, in the car's frame where they are.
        if (fetch is { } kind)
        {
            var taken = SpawnItem(PlayerMotor.WorldPosition(s, train) + Double3.Up * 1.0, s.LineHint, kind);
            taken.Carrier = playerId;
            taken.Claimed = true;
            return true;
        }
        if (CrewActions.NearestInteractable(s, train, hand) is not null)
            return false;
        if (InReach(s, train, hand, wearingRadio: worn is not null, playerId) is not { } nearest)
            return false;
        if (nearest.Kind == BodyKind.Heavy)
        {
            // Your end of it, with both hands on it if they're reported (T43); the other end, if someone has the first.
            if (hand is not null && PlayerMotor.HandWorld(s, train, other: true) is { } other && Surface(nearest, train, other) > hand.Grab)
                return false;
            if (nearest.Carrier >= 0)
                nearest.Second = playerId;
            else
                nearest.Carrier = playerId;
        }
        else
            nearest.Carrier = playerId;
        nearest.Claimed = true;
        nearest.Pbd.Wake();
        return true;
    }

    /// <summary>
    /// GDD §23 "radio breaks", mended (note 201): the repair kit in hand (§12, the engineer is whoever has it), not at
    /// anything Use works, and a broken radio on your belt or lying in reach. Use held there, standing, for train.json
    /// <c>kit.radioMendSeconds</c> mends it; moving or letting go starts it over. Like a locker's tap and hold, the hands
    /// wait for the release: a tap still puts the kit down. Returns whether it had the hands this tick.
    /// </summary>
    bool Mend(in PlayerState s, in PlayerIntent intent, int playerId, TrainOnLine train, HandTuning? hand, Body? carried,
        bool use, bool usePressed, bool throwPressed)
    {
        var radio = carried is { Kind: BodyKind.RepairKit } && !throwPressed ? MendableRadio(s, train, hand, playerId) : null;
        bool had = _mending.TryGetValue(playerId, out var was);
        if (had && (radio is null || radio.Id != was.Radio))
        {
            _mending.Remove(playerId);
            if (_bodies.FirstOrDefault(b => b.Id == was.Radio) is { } left)
                left.MendTicks = 0;
            had = false;
        }
        if (radio is null)
            return false;
        if (usePressed)
            _mending[playerId] = (radio.Id, 1);
        else if (use && had)
            _mending[playerId] = (radio.Id, was.Ticks + 1);
        else if (!use && had)
        {
            _mending.Remove(playerId);
            radio.MendTicks = 0;
            // A tap: it was the hands', putting the kit down.
            if (was.Ticks * Dt < Lockers.DoorSeconds(train) - Dt / 2)
                Release(carried!, s, train, 0);
            return true;
        }
        if (!use || !_mending.ContainsKey(playerId))
            return true;
        bool still = Math.Abs(intent.MoveX) <= 0.1 && Math.Abs(intent.MoveZ) <= 0.1;
        radio.MendTicks = still ? radio.MendTicks + 1 : 0;
        if (radio.MendTicks * Dt >= train.Dynamics.Tuning.Kit.RadioMendSeconds - Dt / 2)
        {
            radio.Broken = false;
            radio.MendTicks = 0;
        }
        return true;
    }

    /// <summary>
    /// GDD App. F.1's rare healing loot, used (note 272): a find that heals (<see cref="Heals"/>) in the hands of a player
    /// short of full health, not at anything Use works. Use held there, standing, for <see cref="HealSeconds"/> uses it up:
    /// the find's gone and its health is the carrier's (<see cref="TakeDose"/>); moving starts it over. Like the kit at a
    /// radio, the hands wait for the release: a tap still puts it down. Returns whether it had the hands this tick.
    /// </summary>
    bool Dose(in PlayerState s, in PlayerIntent intent, int playerId, TrainOnLine train, HandTuning? hand, Body? carried,
        bool use, bool usePressed, bool throwPressed)
    {
        var find = carried is { Kind: BodyKind.Loot } && !throwPressed && s.Health < FullHealth && Heals?.Invoke(carried) > 0
            && CrewActions.NearestInteractable(s, train, hand) is null ? carried : null;
        bool had = _dosing.TryGetValue(playerId, out var was);
        if (had && (find is null || find.Id != was.Find))
        {
            _dosing.Remove(playerId);
            if (_bodies.FirstOrDefault(b => b.Id == was.Find) is { } left)
                left.MendTicks = 0;
            had = false;
        }
        if (find is null)
            return false;
        if (usePressed)
            _dosing[playerId] = (find.Id, 1);
        else if (use && had)
            _dosing[playerId] = (find.Id, was.Ticks + 1);
        else if (!use && had)
        {
            _dosing.Remove(playerId);
            find.MendTicks = 0;
            // A tap: it was the hands', putting it down.
            if (was.Ticks * Dt < Lockers.DoorSeconds(train) - Dt / 2)
                Release(find, s, train, 0);
            return true;
        }
        if (!use || !_dosing.ContainsKey(playerId))
            return true;
        bool still = Math.Abs(intent.MoveX) <= 0.1 && Math.Abs(intent.MoveZ) <= 0.1;
        find.MendTicks = still ? find.MendTicks + 1 : 0;
        if (find.MendTicks * Dt >= HealSeconds - Dt / 2)
        {
            _doses[playerId] = _doses.GetValueOrDefault(playerId) + Heals!(find);
            _dosing.Remove(playerId);
            Remove(find);
        }
        return true;
    }

    /// <summary>
    /// The broken radio a player with the repair kit would mend (note 201): their own, on their belt, or else the nearest one
    /// lying loose in reach. Null if there's none, or they're at something Use works. Also the HUD's prompt.
    /// </summary>
    public Body? MendableRadio(in PlayerState s, TrainOnLine train, HandTuning? hand, int playerId)
    {
        if (!s.Alive || CrewActions.NearestInteractable(s, train, hand) is not null)
            return null;
        if (_bodies.FirstOrDefault(b => b.Kind == BodyKind.Radio && b.Carrier == playerId && b.Broken) is { } worn)
            return worn;
        var loose = _bodies.Where(b => b.Kind == BodyKind.Radio && b.Broken && b.Carrier < 0 && !b.Stowed && b.TakenBy < 0);
        if (hand is not null && PlayerMotor.HandWorld(s, train) is { } h)
            return loose.Select(b => (b, d: Surface(b, train, h))).Where(x => x.d <= hand.Grab).OrderBy(x => x.d).ThenBy(x => x.b.Id).FirstOrDefault().b;
        var hands = HandsAt(s, train);
        return loose.Select(b => (b, d: (WorldCentre(b, train) - hands).Length)).Where(x => x.d <= Hands.Reach).OrderBy(x => x.d).ThenBy(x => x.b.Id).FirstOrDefault().b;
    }

    /// <summary>The loose body a player's hands would take with Use right now, if any (also the HUD's prompt).</summary>
    /// <remarks>A reaching hand (T29) takes the one it's on: within grab of any part of it, a crate's side or a body's arm.</remarks>
    /// <param name="wearingRadio">One radio each: someone already wearing one doesn't reach for another.</param>
    /// <param name="playerId">Who's reaching: the far end of a heavy crate they hold isn't theirs to take again.</param>
    public Body? InReach(in PlayerState s, TrainOnLine train, HandTuning? hand = null, bool wearingRadio = false, int playerId = -1)
    {
        // A heavy crate with one on it is still free at its other end (T43).
        // What's in a locker is the locker's: taken from it with a tap there, not reached for (note 173).
        var free = _bodies.Where(b => !b.Stowed && b.TakenBy < 0 && (b.Carrier < 0 || b.Kind == BodyKind.Heavy && b.Second < 0 && b.Carrier != playerId)
            && !(wearingRadio && b.Kind == BodyKind.Radio));
        if (hand is not null && PlayerMotor.HandWorld(s, train) is { } h)
            return free.Select(b => (b, d: Surface(b, train, h))).Where(x => x.d <= hand.Grab).OrderBy(x => x.d).FirstOrDefault().b;
        var hands = HandsAt(s, train);
        return free.Select(b => (b, d: (WorldCentre(b, train) - hands).Length)).Where(x => x.d <= Hands.Reach).OrderBy(x => x.d).FirstOrDefault().b;
    }

    /// <summary>How far a world point is from a body's nearest surface (0 inside it).</summary>
    static double Surface(Body b, TrainOnLine train, Double3 world)
    {
        var frame = b.Parent == PlayerState.World ? (CarFrame?)null : train.Frames[b.Parent];
        var at = frame?.ToLocal(world) ?? world;
        double best = double.MaxValue;
        foreach (var p in b.Pbd.Particles)
            best = Math.Min(best, Math.Max(0, (p.Position - at).Length - p.Radius));
        return best;
    }

    /// <summary>
    /// A rescued child carried in the arms (GDD App. C.4), clinging: its middle this high over the carrier's feet and this
    /// far out in front, against their chest (soot_child's clutch, crew_clips' cradle hold it there).
    /// </summary>
    public const double ChildHeight = 0.95, ChildForward = 0.3;

    static Double3 ChildAt(in PlayerState s, TrainOnLine train)
    {
        var forward = new Double3(-DMath.Sin(s.Yaw), 0, -DMath.Cos(s.Yaw));
        var local = s.Position + Double3.Up * ChildHeight + forward * ChildForward;
        return s.Parent == PlayerState.World ? local : train.Frames[s.Parent].ToWorld(local);
    }

    Double3 HandsAt(in PlayerState s, TrainOnLine train)
    {
        var forward = new Double3(-DMath.Sin(s.Yaw), 0, -DMath.Cos(s.Yaw));
        var local = s.Position + Double3.Up * Hands.CarryHeight + forward * Hands.CarryForward;
        return s.Parent == PlayerState.World ? local : train.Frames[s.Parent].ToWorld(local);
    }

    void Release(Body b, in PlayerState s, TrainOnLine train, double speed)
    {
        b.Carrier = b.Second = -1;
        var p = b.Pbd.Particles;
        foreach (int held in Grips(b))
            p[held].InverseMass = 1;
        // Thrown along the view, including pitch, on top of the thrower's own motion.
        var look = new Double3(-DMath.Sin(s.Yaw) * DMath.Cos(s.Pitch), DMath.Sin(s.Pitch), -DMath.Cos(s.Yaw) * DMath.Cos(s.Pitch));
        var throwVelocity = s.Velocity + look * speed + Double3.Up * (speed > 0 ? 1.5 : 0);
        for (int i = 0; i < p.Length; i++)
            p[i].SetVelocity(throwVelocity, Dt);
        b.Spin = speed > 0 ? 6 : 0;
        b.Airborne = speed > 0 ? 3 : 0;
        b.Pbd.Wake();
    }

    /// <summary>Host: one physics step for everything loose, after the train and players have moved.</summary>
    public void Step(TrainOnLine train, TrainTuning t, Func<int, PlayerState?> player)
    {
        // A hard stop or a collision throws loose things about: wake anything aboard.
        if (Math.Abs(train.Dynamics.Acceleration) > 1.0)
            foreach (var b in _bodies)
                if (b.Parent != PlayerState.World && !b.Stowed)
                    b.Pbd.Wake();
        foreach (var b in _bodies)
        {
            // On a locker's shelf it's the locker's, and goes where its car goes (note 173).
            if (b.Stowed)
                continue;
            if (b.Kind == BodyKind.Heavy)
                CarryHeavy(b, train, player);
            else if (b.Carrier >= 0 && player(b.Carrier) is { } carrier)
                Carry(b, carrier, train);
            if (b.Parent != PlayerState.World && b.Parent >= train.Frames.Count)
                ToWorld(b, train, b.Parent);
            var frame = b.Parent == PlayerState.World ? (CarFrame?)null : train.Frames[b.Parent];
            // In a car's frame, its braking or pulling is felt as a push the other way.
            double carAccel = frame is { } cf ? (train.Rakes.FirstOrDefault(r => r.Consist.Vehicles.Any(v => v.Id == cf.Index))?.Acceleration ?? 0) : 0;
            var gravity = frame is { } f ? f.DirToLocal(Double3.Up * -t.Gravity + f.Back * carAccel) : Double3.Up * -t.Gravity;
            // Asleep, it stays in the frame it fell asleep in: it isn't touching anything new.
            if (b.Pbd.Asleep && !b.Lifted)
                continue;
            int touchedCar = -1;
            var ps = b.Pbd.Particles;
            if (_before.Length < ps.Length)
                _before = new Double3[ps.Length];
            for (int i = 0; i < ps.Length; i++)
                _before[i] = ps[i].Position;
            b.Pbd.Step(Dt, gravity, (pos, r) => Contact(b, frame, pos, r, train, ref touchedCar));
            if (frame is { } room && !b.Lifted && train.Wreck is null)
                KeepInside(b, room.Shape, train.Vehicles[room.Index]);
            if (!b.Lifted && !b.Pbd.Asleep)
            {
                b.Yaw += b.Spin * Dt;
                b.Spin *= b.Pbd.Particles.Any(p => p.Contact) ? 0.5 : 0.995;
            }
            Reparent(b, train, touchedCar);
        }
    }

    Double3[] _before = new Double3[11];

    /// <summary>
    /// What was in a car's room stays in it, but out through an opening (note 173): a particle that this step went past a
    /// wall (pushed out of its far side, or through it between ticks at a throw's speed) is stopped inside it, its speed
    /// out of the room gone.
    /// </summary>
    void KeepInside(Body b, CarShape shape, Vehicle vehicle)
    {
        var ps = b.Pbd.Particles;
        for (int i = 0; i < ps.Length; i++)
        {
            ref var p = ref ps[i];
            if (Rooms.Held(shape, vehicle, _before[i], p.Position, p.Radius) is not { } held)
                continue;
            // The way it was moving, but not out through the wall: the stopped axis keeps no speed.
            var v = p.Position - p.Previous;
            v = new Double3(held.X != p.Position.X ? 0 : v.X, held.Y != p.Position.Y ? 0 : v.Y, held.Z != p.Position.Z ? 0 : v.Z);
            p.Previous = held - v;
            p.Position = held;
            b.Pbd.Wake();
        }
    }

    /// <summary>
    /// A heavy crate (spec D.2, T43): with one on it, it's held where it lies (and let go by walking off from it); with
    /// both, it rides between their hands, in the first one's frame. Too far apart, and it's down.
    /// </summary>
    void CarryHeavy(Body b, TrainOnLine train, Func<int, PlayerState?> player)
    {
        if (b.Second >= 0 && player(b.Second) is not { Alive: true })
            b.Second = -1;
        if (b.Carrier >= 0 && player(b.Carrier) is not { Alive: true })
            (b.Carrier, b.Second) = (b.Second, -1);
        if (b.Carrier < 0 || player(b.Carrier) is not { } lead)
            return;
        var a = HandsAt(lead, train);
        if (b.Second < 0 || player(b.Second) is not { } second)
        {
            // Held, not lifted: it lies as it lay (and drops, if it had been up between two).
            b.Pbd.Particles[0].InverseMass = 1;
            if (Surface(b, train, a) > Hands.Reach)
                b.Carrier = -1;
            return;
        }
        var c = HandsAt(second, train);
        if ((a - c).Length > HeavySpan)
        {
            Release(b, lead, train, 0);
            return;
        }
        Carry(b, lead, train, (a + c) * 0.5);
    }

    /// <summary>
    /// A body carried over the shoulder (GDD v1.4 App. C.4: "the child is carried in the arms; a body over the shoulder"):
    /// a fireman's carry, in the carrier's frame (x right, y up, z behind, from the feet). The hips over the left shoulder,
    /// the knees held to the chest by the left arm (crew_clips' shoulder and climb_carry hook it there), the chest down the
    /// back; head and arms hang free below it. Each pair a bone's length apart (<see cref="Skeleton"/>), so nothing strains.
    /// </summary>
    static readonly (int Joint, Double3 At)[] Shoulder =
    [
        (2, new(-0.14, 1.52, 0.02)), (1, new(-0.12, 1.24, 0.30)), (7, new(-0.2, 1.2, -0.3)), (9, new(-0.08, 1.2, -0.3)),
    ];

    /// <summary>The particles a carried body is held by: a ragdoll's in the fireman's carry, anything else's first.</summary>
    static IEnumerable<int> Grips(Body b) => b.Kind == BodyKind.Ragdoll ? Shoulder.Select(j => j.Joint) : [0];

    /// <summary>Held at the carrier's hands: a crate rides there; a body over the shoulder. A radio's on the belt.</summary>
    /// <param name="at">Where it's held in the world, when that isn't the carrier's own hands (a heavy crate between two).</param>
    void Carry(Body b, in PlayerState s, TrainOnLine train, Double3? at = null)
    {
        var hands = at ?? (b.Kind switch
        {
            BodyKind.Radio => PlayerMotor.WorldPosition(s, train) + Double3.Up * 1.0,
            BodyKind.Child => ChildAt(s, train),
            _ => HandsAt(s, train),
        });
        if (b.Parent != s.Parent)
        {
            if (b.Parent == PlayerState.World)
                ToCar(b, train, s.Parent);
            else if (s.Parent == PlayerState.World)
                ToWorld(b, train, b.Parent);
            else
            {
                ToWorld(b, train, b.Parent);
                ToCar(b, train, s.Parent);
            }
        }
        var local = s.Parent == PlayerState.World ? hands : train.Frames[s.Parent].ToLocal(hands);
        if (b.Kind == BodyKind.Ragdoll && at is null)
        {
            ShoulderCarry(b, s, train);
            return;
        }
        const int grip = 0;
        // Indoors, what's in your hands is in the room with you (note 173): faced up to a wall, your hands' reach is past it
        // (and past its middle), and what you set down there would be pushed out of its far side. Out through a doorway
        // that's open, it goes with you.
        if (s.Parent != PlayerState.World && s.Parent < train.Frames.Count && at is null)
        {
            var shape = train.Frames[s.Parent].Shape;
            var chest = s.Position + Double3.Up * Hands.CarryHeight;
            if (Rooms.Held(shape, train.Vehicles[s.Parent], chest, local, b.Pbd.Particles[grip].Radius) is { } inside)
                local = inside;
        }
        ref var p = ref b.Pbd.Particles[grip];
        p.InverseMass = 0;
        p.Position = local;
        p.Previous = local;
        b.Yaw = s.Yaw;
        b.Pbd.Wake();
    }

    /// <summary>
    /// The fireman's carry (<see cref="Shoulder"/>): each held joint set where it rides on the carrier, in the carrier's
    /// frame (the body's already in it). Indoors, kept in the room as anything in your hands is (note 173), all of it
    /// moved by what keeps the hips in.
    /// </summary>
    void ShoulderCarry(Body b, in PlayerState s, TrainOnLine train)
    {
        var shift = Double3.Zero;
        if (s.Parent != PlayerState.World && s.Parent < train.Frames.Count)
        {
            var shape = train.Frames[s.Parent].Shape;
            var hips = Riding(s.Position, s.Yaw, Shoulder[0].At, 1);
            if (Rooms.Held(shape, train.Vehicles[s.Parent], s.Position + Double3.Up * Hands.CarryHeight, hips, b.Pbd.Particles[2].Radius) is { } inside)
                shift = inside - hips;
        }
        Pin(b, s.Position + shift, s.Yaw, 1);
    }

    /// <summary>Where a held joint rides on a carrier whose feet are at <paramref name="feet"/> facing <paramref name="yaw"/>,
    /// <paramref name="scale"/> times a crewmate's size.</summary>
    static Double3 Riding(Double3 feet, double yaw, Double3 at, double scale)
    {
        var forward = new Double3(-DMath.Sin(yaw), 0, -DMath.Cos(yaw));
        var right = new Double3(DMath.Cos(yaw), 0, -DMath.Sin(yaw));
        return feet + (right * at.X + Double3.Up * at.Y - forward * at.Z) * scale;
    }

    /// <summary>A carried thing set where it rides: a ragdoll in the fireman's carry, anything else at the hands.</summary>
    void Pin(Body b, Double3 feet, double yaw, double scale)
    {
        if (b.Kind == BodyKind.Ragdoll)
            foreach (var (joint, at) in Shoulder)
            {
                ref var p = ref b.Pbd.Particles[joint];
                p.InverseMass = 0;
                p.Position = p.Previous = Riding(feet, yaw, at, scale);
            }
        else
        {
            ref var p = ref b.Pbd.Particles[0];
            p.InverseMass = 0;
            p.Position = p.Previous = Riding(feet, yaw, new Double3(0, Hands.CarryHeight, -Hands.CarryForward), scale);
        }
        b.Yaw = yaw;
        b.Pbd.Wake();
    }

    /// <summary>
    /// Carried off by a thing (the Gaunt leaving with what it took, App. A.6): clutched by <paramref name="hold"/> (in
    /// <paramref name="parent"/>'s frame, a car or the world), facing <paramref name="yaw"/>. A body by the hips there,
    /// its chest draped forward over what holds it and the rest hanging; anything else by its middle.
    /// </summary>
    public void TakeAlong(Body b, TrainOnLine train, int taker, int parent, Double3 hold, double yaw)
    {
        if (b.Parent != parent)
        {
            if (b.Parent != PlayerState.World)
                ToWorld(b, train, b.Parent);
            if (parent != PlayerState.World)
                ToCar(b, train, parent);
        }
        b.Carrier = b.Second = -1;
        b.Locker = -1;
        b.TakenBy = taker;
        b.Airborne = 0;
        var forward = new Double3(-DMath.Sin(yaw), 0, -DMath.Cos(yaw));
        void Hold(int joint, Double3 at)
        {
            ref var p = ref b.Pbd.Particles[joint];
            p.InverseMass = 0;
            p.Position = p.Previous = at;
        }
        if (b.Kind == BodyKind.Ragdoll)
        {
            Hold(2, hold);
            Hold(1, hold + forward * 0.3 - Double3.Up * 0.25);
        }
        else
            Hold(0, hold);
        b.Yaw = yaw;
        b.Pbd.Wake();
    }

    /// <summary>Dropped by whatever was carrying it off (it's dead): loose, to fall where it is.</summary>
    public void LetGo(Body b)
    {
        b.TakenBy = -1;
        foreach (int held in Grips(b))
            b.Pbd.Particles[held].InverseMass = 1;
        b.Pbd.Wake();
    }

    /// <summary>What a particle hits: car solids and shut doors of any car nearby, and the ground.</summary>
    Contact? Contact(Body b, CarFrame? frame, Double3 pos, double r, TrainOnLine train, ref int touchedCar)
    {
        var world = frame is { } f ? f.ToWorld(pos) : pos;
        Contact? best = null;
        double bestDepth = 0;
        foreach (var g in train.Frames)
        {
            if ((g.Origin - world).Length > NearbyCar)
                continue;
            var local = g.ToLocal(world);
            var vehicle = train.Vehicles[g.Index];
            foreach (var solid in g.Shape.Solids)
                if (solid.Present(vehicle))
                    Consider(Collide.SphereBox(local, r, solid.Box.Min, solid.Box.Max), g, local, ref best, ref bestDepth, ref touchedCar);
            foreach (var door in g.Shape.DoorList)
                if (!vehicle.DoorOpen(door.Index))
                    Consider(Collide.SphereBox(local, r, door.Box.Min, door.Box.Max), g, local, ref best, ref bestDepth, ref touchedCar);
        }
        // The stops' and the fortresses' buildings (T124): a body thrown or dropped against one comes to rest at it.
        if (train.Walls is { } walls)
            foreach (var w in walls.Near(world))
            {
                var local = w.ToLocal(world);
                if (Collide.SphereBox(local, r, w.Box.Min, w.Box.Max) is not { } touch)
                    continue;
                double depth = (touch.Position - local).Length;
                if (best is not null && depth <= bestDepth)
                    continue;
                best = new Contact(w.ToWorld(touch.Position), w.DirToWorld(touch.Normal));
                bestDepth = depth;
                touchedCar = -2; // the world's, as the ground is
            }
        // Note 279: down in a tunnel, its lining.
        if (train.Line.Conditions is { } land && land.Confine(world, r) is var held && (held - world).Length is var into and > 1e-9 && into > bestDepth)
        {
            best = new Contact(held, (held - world) * (1 / into));
            bestDepth = into;
            touchedCar = -2;
        }
        double hint = b.LineHint;
        double ground = PlayerMotor.GroundAt(world, train.Line, ref hint) + r;
        b.LineHint = hint;
        if (world.Y < ground && ground - world.Y > bestDepth)
        {
            best = new Contact(world with { Y = ground }, Double3.Up);
            touchedCar = -2; // the ground
        }
        if (best is not { } hit)
            return null;
        return frame is { } fr ? new Contact(fr.ToLocal(hit.Position), fr.DirToLocal(hit.Normal)) : hit;
    }

    static void Consider(Contact? hit, CarFrame g, Double3 local, ref Contact? best, ref double bestDepth, ref int touchedCar)
    {
        if (hit is not { } h)
            return;
        double depth = (h.Position - local).Length;
        if (best is not null && depth <= bestDepth)
            return;
        bestDepth = depth;
        best = new Contact(g.ToWorld(h.Position), g.DirToWorld(h.Normal));
        touchedCar = g.Index;
    }

    /// <summary>Touching a car: live in its frame. Off every car for a few steps: the world's.</summary>
    static void Reparent(Body b, TrainOnLine train, int touchedCar)
    {
        if (b.Lifted)
            return;
        if (touchedCar >= 0)
        {
            b.Airborne = 0;
            if (touchedCar != b.Parent)
            {
                if (b.Parent != PlayerState.World)
                    ToWorld(b, train, b.Parent);
                ToCar(b, train, touchedCar);
            }
            return;
        }
        // Falling inside a car's walls (set down a hand's height over its floor) is still in the car: only off it altogether
        // does a body take the world's frame. Otherwise it flickers out of the car for a tick as it drops, and whatever
        // counts what's in a car (the crate hands' room, the loading) miscounts it.
        if (b.Parent != PlayerState.World && ++b.Airborne > 3
            && !(b.Parent < train.Frames.Count && train.Frames[b.Parent].Shape.Interior is { } room && room.Contains(b.Centre)))
            ToWorld(b, train, b.Parent);
    }

    static void ToWorld(Body b, TrainOnLine train, int car)
    {
        if (car == PlayerState.World)
            return;
        var f = train.Frames[Math.Min(car, train.Frames.Count - 1)];
        b.Pbd.Transform(f.ToWorld, f.VelocityToWorld, Dt);
        b.Parent = PlayerState.World;
    }

    static void ToCar(Body b, TrainOnLine train, int car)
    {
        var f = train.Frames[car];
        b.Pbd.Transform(f.ToLocal, f.VelocityToLocal, Dt);
        b.Parent = car;
    }

    public static Double3 WorldCentre(Body b, TrainOnLine train) =>
        b.Parent == PlayerState.World || b.Parent >= train.Frames.Count ? b.Centre : train.Frames[b.Parent].ToWorld(b.Centre);

    /// <summary>Client: replaces the mirrored bodies with the host's.</summary>
    public void Mirror(IEnumerable<Body> bodies)
    {
        _bodies.Clear();
        _bodies.AddRange(bodies);
    }

    /// <summary>Removes a body entirely (tests, a revived crewmate).</summary>
    public void Remove(Body b) => _bodies.Remove(b);
}
