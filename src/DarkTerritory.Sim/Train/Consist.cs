using Ballast;

namespace DarkTerritory.Sim.Train;

/// <summary>
/// What a vehicle is for (GDD §10): engine at the front, guard car with the rear gun at the back, and a crew car (the
/// GDD's "utility car": stores and a stove, warm and lamp-lit, §26) if the crew have bought one (note 184).
/// </summary>
public enum VehicleKind : byte { Engine, Cargo, Guard, Utility }

/// <summary>
/// A mounted gun's state (spec B.7). Lives on the vehicle whose roof rail it's on (T93): a gun slides along the rail and
/// over the coupling onto the next car's, so which car has it, and where along it, is state like its ammunition.
/// </summary>
public struct GunState
{
    /// <summary>There's a gun on this vehicle's rail.</summary>
    public bool Mounted;
    /// <summary>Where along the vehicle's roof rail (its local Z) the gun's pivot stands.</summary>
    public double Z;
    /// <summary>Which way it faces along the train: −1 forward (the engine's), +1 back (the guard van's).</summary>
    public sbyte Facing;
    /// <summary>The gun's rounds for the night, at it and in the powder lockers (combat.json <c>ammo</c>).</summary>
    public int Ammo;
    /// <summary>
    /// Of <see cref="Ammo"/>, the rounds in its ready rack at the gun (note 374, orchestrator.md §5.1 U4; combat.json
    /// <c>rack</c>): what it fires. The rest are down in the powder lockers, carried up a charge at a time (<see cref="Combat.Guns"/>).
    /// </summary>
    public int Rack;
    /// <summary>Ticks until it can fire again.</summary>
    public int Cooldown;
    /// <summary>GDD §23: "Gun jams: someone repairs it by hand, under fire."</summary>
    public bool Jammed;
    /// <summary>Tick of the last round fired (for muzzle flash and sound on clients), 0 if never.</summary>
    public uint LastShotTick;
    /// <summary>Reload steps still to do before it can fire (GDD v1.1 App. C.3: powder, ball, ram); 0 is loaded.</summary>
    public int ReloadNeeded;
    /// <summary>Seconds into the current reload step.</summary>
    public double ReloadProgress;
    /// <summary>How far the carriage is turned off its facing, radians (T112: the seated gunner lays it), left positive.</summary>
    public double Traverse;
    /// <summary>The barrel's elevation, radians, up positive.</summary>
    public double Elevation;
}

/// <summary>
/// One piece of rolling stock. The id is stable for the whole run: players, enemies and snapshots
/// refer to vehicles by id, so cutting and re-coupling never changes who is standing on what.
/// </summary>
/// <summary>
/// What a car's carrying (GDD §19, App. B.8): it changes the run, not just the score. A night leaves with its contract's
/// cargo (<see cref="Goods"/> without one; ARCHITECTURE §8 note 182); the facilities load their own (facilities.json
/// <c>cargo</c>). <see cref="Heavy"/> is §19's machine parts, <see cref="Ammunition"/> its gunpowder and shot. New kinds go
/// on the end, so a save's numbers keep their meaning.
/// </summary>
public enum CargoKind : byte { None, Goods, Grain, Heavy, Salvage, Livestock, Food, Chemicals, Ore, Ammunition, Comet, Medicine, Timber, Coal }

/// <summary>What each cargo is called, and what it does to a fire (GDD §19, App. B.9).</summary>
public static class Cargoes
{
    /// <summary>The cargo's name as the contract board and the clerk say it.</summary>
    public static string Name(CargoKind cargo) => cargo switch
    {
        CargoKind.None or CargoKind.Goods => "goods",
        CargoKind.Heavy => "machine parts",
        CargoKind.Ammunition => "gunpowder and shot",
        CargoKind.Comet => "comet-derived material",
        _ => cargo.ToString().ToLowerInvariant(),
    };

    /// <summary>Coal and timber (§19 "burns"; B.9 "fire cascades escalate faster").</summary>
    public static bool Fuel(CargoKind cargo) => cargo is CargoKind.Coal or CargoKind.Timber;

    /// <summary>The key a tuning table uses for a cargo (camel-cased, as facilities.json names them).</summary>
    public static string Key(CargoKind cargo) => char.ToLowerInvariant(cargo.ToString()[0]) + cargo.ToString()[1..];
}

public sealed class Vehicle(int id, VehicleKind kind, double load)
{
    public Vehicle(int id, bool isEngine, double load) : this(id, isEngine ? VehicleKind.Engine : VehicleKind.Cargo, load)
    {
    }

    public int Id { get; } = id;
    public VehicleKind Kind { get; set; } = kind;
    public bool IsEngine => Kind == VehicleKind.Engine;
    /// <summary>The mounted gun's state, if this vehicle carries one (the engine's and guard van's to begin with; T93).</summary>
    public GunState Gun;
    public bool HasGun => Gun.Mounted;
    /// <summary>0 empty to 1 full.</summary>
    public double Load { get; set; } = Math.Clamp(load, 0, 1);
    /// <summary>What's in it: the departure's goods, or what the last facility that loaded it put in (a mixed car goes by the last).</summary>
    public CargoKind Cargo { get; set; } = load > 0 && kind == VehicleKind.Cargo ? CargoKind.Goods : CargoKind.None;
    /// <summary>Structural condition, 1 sound to 0 wrecked.</summary>
    public double Integrity { get; set; } = 1;
    /// <summary>
    /// How much of its shell a Car Hugger has eaten (GDD v1.2 App. A.3 FEED), counted out of <see cref="Integrity"/>: the
    /// same loss, remembered as eaten rather than battered, so the car is drawn gnawed away from its rear end (and stays
    /// so once the thing is killed) rather than dented.
    /// </summary>
    /// <summary>
    /// How charred each of its <see cref="Enemies.FireGrid"/> cells is, 0..15 (App. F.1: "burnt cells char the textures";
    /// note 267). Empty until it first burns; it never comes back the same night, and a fire there again has less to burn.
    /// </summary>
    public byte[] Char { get; set; } = [];
    public double Eaten { get; set; }
    /// <summary>
    /// The Territory has it (GDD v1.4 §23.2): a car the Car Hugger finished, or a caboose the Passenger rolled away. Gone,
    /// with everything in it, however close it still is.
    /// </summary>
    public bool Taken { get; set; }
    /// <summary>
    /// Converted to armour (spec F.3 "armoured car conversion", GDD §26 "reinforced plating, heavier mass"; note 184): it
    /// weighs train.json <c>composition.armourTonnes</c> more, and takes <c>armourDamage</c> of what's done to its shell.
    /// Fixed for the night, like its kind.
    /// </summary>
    public bool Armoured { get; set; }

    /// <summary>
    /// One of a switchyard's cars, standing on its siding when the night began (GDD §18 "cars scattered across six sidings";
    /// WP15b, note 187), not the crew's: coupled up and brought home, it's theirs and it pays. Fixed for the night.
    /// </summary>
    public bool YardCar { get; init; }

    /// <summary>
    /// Bad-order stock blocking a yard's siding (level-design D.1, D.2; note 294), one of the <see cref="YardCar"/>s: empty,
    /// battered, and whatever's loaded into it pays only facilities.json <c>derelicts.pays</c>. Never the crew's: wherever it's
    /// left, it isn't a car lost. Fixed for the night.
    /// </summary>
    public bool Derelict { get; init; }

    /// <summary>
    /// A blow to the car's shell (a Car Hugger's bite, a hard knock at the couplers): what's left of it after the plate, if
    /// it's armoured. Returns what the car lost.
    /// </summary>
    public double Batter(double amount, TrainTuning t)
    {
        double lost = Math.Min(Integrity, Math.Max(0, amount) * (Armoured ? t.Composition.ArmourDamage : 1));
        Integrity -= lost;
        return lost;
    }

    /// <summary>Fraction of the cargo that would still pay on delivery (spec F.1).</summary>
    public double CargoIntegrity { get; set; } = 1;
    /// <summary>One bit per door in <see cref="CarShape.Doors"/>: set is open. Doors start shut.</summary>
    public byte DoorsOpen { get; set; }
    /// <summary>
    /// The lamp inside the car (GDD v1.1 App. A.5): lit, the crew can see in there, and it's what the Fire Flies swarm to;
    /// out, it's dark, and the Climbers and Followers like it that way. Lit at departure.
    /// </summary>
    public bool LampLit { get; set; } = true;
    public bool DoorOpen(int index) => (DoorsOpen & (1 << index)) != 0;

    /// <summary>
    /// The car's shell has given way to the outside (the breach, decided 1 Oct): the Car Hugger through its end wall, Climbers
    /// in through its roof. Until it's boarded up it doesn't shut anyone in, whatever its doors (<see cref="Player.PlayerMotor.Space"/>):
    /// not the cold, not the night's sound, not the Choir.
    /// </summary>
    public bool Breached { get; set; }
    /// <summary>Seconds its rear axle box has run hot (note 331, <see cref="HotBoxes"/>); 0 running cool.</summary>
    public double HotBox { get; set; }
    /// <summary>Seconds its lamp has been guttering (note 346, <see cref="Gutters"/>); 0 burning steady.</summary>
    public double Gutter { get; set; }
    /// <summary>Seconds the coupling behind it has been working loose (note 356, <see cref="Couplings"/>); 0 tight.</summary>
    public double Loose { get; set; }
    /// <summary>Where the hole is (car frame): what's boarded up (<see cref="Breaches"/>).</summary>
    public Double3 BreachAt { get; set; }

    /// <summary>The shell gives way at <paramref name="at"/> (host). Already breached, it's the hole there is: false.</summary>
    public bool Breach(Double3 at)
    {
        if (Breached)
            return false;
        Breached = true;
        BreachAt = at;
        return true;
    }

    /// <summary>
    /// Opens a shut door or shuts an open one. Two hands on one door the same tick move it once: two crew pulling it shut
    /// together shut it, rather than each undoing the other (the pair warming up in one car never got it shut, and froze).
    /// </summary>
    public void ToggleDoor(int index)
    {
        byte bit = (byte)(1 << index);
        if ((_movedThisTick & bit) != 0)
            return;
        _movedThisTick |= bit;
        DoorsOpen ^= bit;
    }

    byte _movedThisTick;

    /// <summary>One bit per crew locker in its car's <see cref="CarShape.Lockers"/> (ARCHITECTURE §8 note 173): set is open. Shut at departure.</summary>
    public uint LockersOpen { get; set; }
    public bool LockerOpen(int index) => (LockersOpen & (1u << index)) != 0;

    /// <summary>Opens a shut locker or shuts an open one: once a tick, like a door (<see cref="ToggleDoor"/>).</summary>
    public void ToggleLocker(int index)
    {
        uint bit = 1u << index;
        if ((_lockersMoved & bit) != 0)
            return;
        _lockersMoved |= bit;
        LockersOpen ^= bit;
    }

    uint _lockersMoved;

    /// <summary>The train's step: doors worked this tick can be worked again next tick.</summary>
    internal void EndTick() => (_movedThisTick, _lockersMoved) = (0, 0);

    public double MassTonnes(TrainTuning t) =>
        IsEngine ? t.Mass.EngineTonnes
            : t.Mass.EmptyCarTonnes + Load * (t.Mass.LoadedCarTonnes - t.Mass.EmptyCarTonnes) + (Armoured ? t.Composition.ArmourTonnes : 0);

    public double Length(TrainTuning t) => IsEngine ? t.Geometry.EngineLength : t.Geometry.CarLength;
}

/// <summary>
/// A rake: vehicles coupled together, front to back. The engine's rake is the train; a rake without
/// an engine is cars left behind or rolling free (GDD §17, §24).
/// </summary>
public sealed class Consist
{
    readonly List<Vehicle> _vehicles = new();

    public Consist(TrainTuning tuning) => Tuning = tuning;

    /// <summary>Swappable so edited tuning files apply to a running train.</summary>
    public TrainTuning Tuning { get; set; }
    public IReadOnlyList<Vehicle> Vehicles => _vehicles;
    public bool HasEngine => _vehicles.Exists(v => v.IsEngine);
    public int CarCount => _vehicles.Count(v => !v.IsEngine);
    public IReadOnlyList<double> Loads => _vehicles.Where(v => !v.IsEngine).Select(v => v.Load).ToList();

    /// <summary>
    /// The engine (id 0) followed by <paramref name="cars"/> cars with ids 1..n. With two or more cars the
    /// last is the guard car (GDD §10), which counts as one of the cars (spec F.3: a second guard car
    /// "costs a cargo slot"). What the crew have bought (train.json <c>composition</c>, note 184) is made of the cars between:
    /// see <see cref="Compose"/>.
    /// </summary>
    public static Consist Uniform(TrainTuning tuning, int cars, double load)
    {
        var c = new Consist(tuning);
        c._vehicles.Add(new Vehicle(0, isEngine: true, 0));
        for (int i = 0; i < cars; i++)
            c.AddCar(load);
        if (cars >= 2)
            c._vehicles[^1].Kind = VehicleKind.Guard;
        c.Compose(tuning.Composition);
        return c;
    }

    /// <summary>
    /// The crew's purchases made of the cargo cars (note 184), never leaving fewer than <c>minCargoCars</c> of them: the
    /// extra guard cars first (a gun matters more than a stove), then the crew cars. Crew cars go right behind the engine
    /// (the first is the kit's car, its lockers the stores: warm, a walk from the footplate); an extra guard car goes in the
    /// middle of the cargo left, its gun covering what the engine's and the van's can't reach. Armour goes on from the rear
    /// forward, guard van first: the rear's where the Car Hugger feeds. Each converted car carries no freight, its mass as a
    /// cargo car's (its stores, its gun), as the guard van's always has.
    /// </summary>
    void Compose(CompositionTuning t)
    {
        var cargo = _vehicles.Where(v => v.Kind == VehicleKind.Cargo).ToList();
        if (_vehicles.Count(v => v.Kind == VehicleKind.Guard) > 0)
        {
            int spare = Math.Max(0, cargo.Count - Math.Max(0, t.MinCargoCars));
            int guards = Math.Min(Math.Max(0, t.GuardCars - 1), spare);
            int utility = Math.Min(Math.Max(0, t.UtilityCars), spare - guards);
            for (int i = 0; i < utility; i++)
                Convert(cargo[i], VehicleKind.Utility);
            var left = cargo.Skip(utility).ToList();
            for (int i = 0; i < guards; i++)
            {
                var v = left[left.Count * (i + 1) / (guards + 1)];
                Convert(v, VehicleKind.Guard);
            }
        }
        int armoured = 0;
        for (int i = _vehicles.Count - 1; i >= 0 && armoured < t.ArmouredCars; i--)
            if (!_vehicles[i].IsEngine)
            {
                _vehicles[i].Armoured = true;
                armoured++;
            }
    }

    static void Convert(Vehicle v, VehicleKind kind)
    {
        v.Kind = kind;
        v.Cargo = CargoKind.None;
    }

    /// <summary>
    /// The night's freight from the fortress (GDD §9 "choose freight contracts"; note 182): every loaded cargo car carries the
    /// contract's cargo. Goods (or none) leaves it as it is.
    /// </summary>
    public Consist Carrying(CargoKind cargo)
    {
        if (cargo is not (CargoKind.None or CargoKind.Goods))
            foreach (var v in _vehicles)
                if (v.Kind == VehicleKind.Cargo && v.Load > 0)
                    v.Cargo = cargo;
        return this;
    }

    public Vehicle AddCar(double load)
    {
        var v = new Vehicle(_vehicles.Count == 0 ? 1 : _vehicles.Max(x => x.Id) + 1, isEngine: false, load);
        _vehicles.Add(v);
        return v;
    }

    public void Add(Vehicle v) => _vehicles.Add(v);

    /// <summary>Keeps the first <paramref name="cars"/> cars behind the engine and drops the rest. Returns how many were dropped.</summary>
    public int UncoupleFrom(int cars)
    {
        int keep = cars + (HasEngine ? 1 : 0);
        int removed = _vehicles.Count - keep;
        if (removed > 0)
            _vehicles.RemoveRange(keep, removed);
        return Math.Max(0, removed);
    }

    /// <summary>Cuts behind the vehicle at <paramref name="index"/>; returns the vehicles behind as a new rake.</summary>
    public Consist SplitAfter(int index)
    {
        var rear = new Consist(Tuning);
        for (int i = index + 1; i < _vehicles.Count; i++)
            rear._vehicles.Add(_vehicles[i]);
        _vehicles.RemoveRange(index + 1, _vehicles.Count - index - 1);
        return rear;
    }

    /// <summary>Couples <paramref name="rear"/> on behind this rake.</summary>
    public void Append(Consist rear) => _vehicles.AddRange(rear._vehicles);

    /// <summary>Couples <paramref name="front"/> on ahead of this rake.</summary>
    public void Prepend(Consist front) => _vehicles.InsertRange(0, front._vehicles);

    /// <summary>Distance from this rake's front face to the front face of the vehicle at <paramref name="index"/>.</summary>
    public double OffsetOf(int index)
    {
        double d = 0;
        for (int i = 0; i < index; i++)
            d += _vehicles[i].Length(Tuning) + Tuning.Geometry.CouplingGap;
        return d;
    }

    public int IndexOf(int vehicleId) => _vehicles.FindIndex(v => v.Id == vehicleId);

    public double MassTonnes => _vehicles.Sum(v => v.MassTonnes(Tuning));

    /// <summary>Mass of an engine plus this many cars fully loaded; the reference for the performance table.</summary>
    public static double LoadedMassTonnes(TrainTuning t, int cars) => t.Mass.EngineTonnes + cars * t.Mass.LoadedCarTonnes;

    /// <summary>Front face of the first vehicle to rear face of the last, couplings included (spec B.4).</summary>
    public double LengthMetres => _vehicles.Sum(v => v.Length(Tuning)) + Math.Max(0, _vehicles.Count - 1) * Tuning.Geometry.CouplingGap;
}
