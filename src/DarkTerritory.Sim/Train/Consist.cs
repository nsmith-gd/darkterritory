namespace DarkTerritory.Sim.Train;

/// <summary>What a vehicle is for (GDD §10): engine at the front, guard car with the rear gun at the back.</summary>
public enum VehicleKind : byte { Engine, Cargo, Guard }

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
    public int Ammo;
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
/// What a car's carrying (GDD §19, App. B.8): it changes the run, not just the score. <see cref="Goods"/> is the fortress's
/// own freight a night leaves with; the rest are what the facilities load (facilities.json <c>cargo</c>).
/// </summary>
public enum CargoKind : byte { None, Goods, Grain, Heavy, Salvage, Livestock, Food, Chemicals, Ore, Ammunition, Comet }

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
    public double Eaten { get; set; }
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

    /// <summary>The train's step: doors worked this tick can be worked again next tick.</summary>
    internal void EndTick() => _movedThisTick = 0;

    public double MassTonnes(TrainTuning t) =>
        IsEngine ? t.Mass.EngineTonnes : t.Mass.EmptyCarTonnes + Load * (t.Mass.LoadedCarTonnes - t.Mass.EmptyCarTonnes);

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
    /// "costs a cargo slot").
    /// </summary>
    public static Consist Uniform(TrainTuning tuning, int cars, double load)
    {
        var c = new Consist(tuning);
        c._vehicles.Add(new Vehicle(0, isEngine: true, 0));
        for (int i = 0; i < cars; i++)
            c.AddCar(load);
        if (cars >= 2)
            c._vehicles[^1].Kind = VehicleKind.Guard;
        return c;
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
