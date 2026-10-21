using Ballast;
using DarkTerritory.Sim.Rail;

namespace DarkTerritory.Sim.Train;

/// <summary>Where one vehicle sits in the world this tick.</summary>
/// <param name="Index">Vehicle id: 0 is the engine + tender, cars are 1..n.</param>
/// <param name="Centre">Centre of the car at rail height.</param>
/// <param name="Forward">Unit vector from rear bogie to front bogie.</param>
/// <param name="FrontDistance">Distance along the line of the car's front face.</param>
public readonly record struct CarPose(int Index, Double3 Centre, Double3 Forward, double Length, double FrontDistance)
{
    public Double3 Right => Double3.Cross(Forward, Double3.Up).Normalized;
    public Double3 Up => Double3.Cross(Right, Forward);
}

/// <summary>Two rakes touched this tick, or one hit a buffer stop.</summary>
/// <param name="Front">Vehicle id at the rear of the front rake (the front of a rake at a buffer stop).</param>
/// <param name="Rear">Vehicle id at the front of the rear rake; −1 for a buffer stop.</param>
public readonly record struct RakeContact(int Front, int Rear, double ClosingSpeed, bool Coupled, double Damage);

/// <param name="Path">The track the rake's front is on: <see cref="RailLine.MainPath"/>, or a branch index.</param>
public readonly record struct RakeState(int[] Vehicles, double Distance, double Velocity, double BrakeEfficiency, bool Handbrake, bool FrontCouplerLocked, int Path = RailLine.MainPath);
public readonly record struct VehicleState(int Id, double Load, double Integrity, double CargoIntegrity, GunState Gun = default, byte DoorsOpen = 0,
    CargoKind Cargo = CargoKind.None, bool LampLit = true, double Eaten = 0, uint LockersOpen = 0, bool Breached = false, Double3 BreachAt = default, byte[]? Char = null, double HotBox = 0, double Gutter = 0, double Loose = 0, bool Wound = false, bool Seized = false, double Knot = 0,
    bool OffRails = false, double Placed = 0, double Grill = 0, int Beers = 0, LooseCause LooseCause = LooseCause.None, bool Pinned = false);

/// <summary>Everything about the train that the host owns and clients re-simulate from.</summary>
public sealed record TrainState(RakeState[] Rakes, VehicleState[] Vehicles, Boiler Boiler)
{
    /// <summary>The handcar's own state (note 729), when the night has one; unset, a restore leaves it as it is.</summary>
    public HandcarState? Handcar { get; init; }
}

/// <summary>What the handcar is doing (note 729; <see cref="Train.Handcar"/>), as the host has it.</summary>
public readonly record struct HandcarState(bool Carried, double Phase, double Drive, double Brake, double Lift, int Pumpers);

/// <summary>
/// All rolling stock on a line: one or more rakes, each a 1D body with its own speed. The rake with the
/// engine is "the train"; cutting leaves the rest behind, and rakes that meet couple or collide.
/// Also owns the boiler, and the 3D pose and collision frame of every vehicle, indexed by vehicle id.
/// <para>
/// The line may have branches at switches (GDD §17). Each rake runs on a path, the main line or a branch taken at its
/// points, chosen by the switch as the rake's front runs through them. Backing out through the points needs no
/// choice. The switches are the train's state, like the rakes: the host owns them and they're replicated.
/// </para>
/// </summary>
public sealed class TrainOnLine
{
    /// <summary>
    /// Off the rails (T117): the cars as the wreck has them, tumbling and then lying where they came to rest. While there's a
    /// wreck its bodies are the cars' frames, so everyone aboard goes where their car goes.
    /// </summary>
    public Wreck? Wreck { get; set; }

    /// <summary>Lays the frames out again (after the wreck's moved them).</summary>
    public void RefreshFrames() => UpdatePoses();

    /// <summary>The stops' buildings, for whoever walks among them (T114); set with the run, alike on every machine.</summary>
    public Run.StopWalls? Walls { get; set; }

    /// <summary>
    /// What's lying still in the cars (note 659, the director: "we want the loot we put in the cars to persist once in there so
    /// poor stacking becomes an obstacle for players passing through"): each thing's box in its car's frame, to walk round or
    /// step up onto. Made after the bodies' step on the host and from the bodies' record on a client
    /// (<see cref="Physics.Bodies.Lying"/>), from the same rounded numbers, so every machine walks into the same things.
    /// </summary>
    public IReadOnlyList<(int Car, Box Box)> Lying { get; set; } = [];

    readonly List<TrainDynamics> _rakes = new();
    // Grown once when a switchyard's standing cars are put on its sidings (Stand), before the night begins.
    Vehicle[] _vehicles;
    CarPose[] _poses;
    CarFrame[] _frames;
    readonly List<RakeContact> _contacts = new();
    readonly bool[] _diverging;
    TrainDynamics _engineRake;

    /// <param name="boiler">Boiler tuning. Without it the engine has unlimited steam, which the pure
    /// dynamics tests (spec B.5) rely on.</param>
    public TrainOnLine(TrainDynamics dynamics, RailLine line, double startDistance, BoilerTuning? boiler = null)
    {
        _engineRake = dynamics;
        _rakes.Add(dynamics);
        Line = line;
        dynamics.Distance = startDistance;
        dynamics.PreviousDistance = startDistance;
        _vehicles = new Vehicle[dynamics.Consist.Vehicles.Max(v => v.Id) + 1];
        foreach (var v in dynamics.Consist.Vehicles)
            _vehicles[v.Id] = v;
        OwnVehicles = _vehicles.Length;
        _poses = new CarPose[_vehicles.Length];
        _frames = new CarFrame[_vehicles.Length];
        // The handcar (note 729), bought or always: its vehicle after the train's own, so every machine numbers it alike, and
        // a yard's standing cars after it.
        if (dynamics.Tuning.HasHandcar)
            AddHandcar();
        _diverging = new bool[line.Branches.Count];
        for (int i = 0; i < _diverging.Length; i++)
            _diverging[i] = line.Branches[i].Definition.StartsDiverging;
        BoilerTuning = boiler;
        if (boiler is not null)
            Boiler = Boiler.Fresh(boiler);
        _kitCar = KitCar;
        UpdatePoses();
        // The guns start where their cars' shapes stand them (the engine's forward, the guard van's back): from here they're
        // the vehicles' own, and ride the roof rails wherever they're pushed (T93).
        foreach (var f in _frames)
            if (f.Shape?.Gun is { } home && !_vehicles[f.Index].Gun.Mounted)
                _vehicles[f.Index].Gun = new GunState { Mounted = true, Z = home.Position.Z, Facing = (sbyte)(home.Facing.Z < 0 ? -1 : 1) };
    }

    /// <summary>
    /// The train's own vehicles as the night began: ids 0 to this less one. Any past them are a switchyard's standing cars
    /// (<see cref="Stand"/>), which nobody boards at the start.
    /// </summary>
    public int OwnVehicles { get; }

    /// <summary>
    /// The handcar (GDD §24.1; note 729), when the night has one (train.json <c>handcar</c>, campaign.json's upgrade): a vehicle of
    /// its own kind in a rake of its own, hung on the back of the guard van until it's set on the rails at a stand. On the rails
    /// it runs as any rake does, through the points as they're set, but couples to nothing and can't pass the train.
    /// </summary>
    public Handcar? Handcar { get; private set; }

    void AddHandcar()
    {
        int id = _vehicles.Length;
        var consist = new Consist(Tuning);
        consist.Add(new Vehicle(id, VehicleKind.Handcar, 0) { LampLit = false });
        Array.Resize(ref _vehicles, id + 1);
        Array.Resize(ref _poses, id + 1);
        Array.Resize(ref _frames, id + 1);
        _vehicles[id] = consist.Vehicles[0];
        _rakes.Add(new TrainDynamics(consist) { Path = _engineRake.Path, Distance = _engineRake.Distance, PreviousDistance = _engineRake.Distance, Handbrake = true, FrontCouplerLocked = true });
        Handcar = new Handcar(id);
    }

    /// <summary>The handcar's rake (note 729), or null without one.</summary>
    public TrainDynamics? HandcarRake => Handcar is { } h ? RakeOf(h.Id) : null;

    /// <summary>A rake that's the handcar (note 729): never coupled, so it's always the one vehicle.</summary>
    public static bool IsHandcar(TrainDynamics rake) => rake.Consist.Vehicles is [{ IsHandcar: true }];

    /// <summary>The handcar's rake while it's hung on the van (note 729): not on the rails, laid out on the van's back.</summary>
    bool Hung(TrainDynamics rake) => Handcar is { Carried: true } && IsHandcar(rake);

    /// <summary>
    /// The vehicle the handcar hangs on (note 729): the guard van at the back (the last guard car), else the engine's rake's
    /// last vehicle. Fixed by the train's make-up, so every machine has it alike.
    /// </summary>
    public int HandcarHost
    {
        get
        {
            for (int i = OwnVehicles - 1; i > 0; i--)
                if (_vehicles[i] is { Kind: VehicleKind.Guard })
                    return i;
            return _engineRake.Consist.Vehicles[^1].Id;
        }
    }

    /// <summary>Where the handcar is set on the rails behind the van (note 729): its path and front's distance, or null where it can't go.</summary>
    public (int Path, double Front)? HandcarPlace()
    {
        if (Handcar is null)
            return null;
        var ht = Tuning.Handcar;
        var host = RakeOf(HandcarHost);
        double front = host.RearDistance - Tuning.Geometry.PlatformDepth - ht.SetBehind;
        if (front - ht.Length < 0)
            return null;
        // Nothing else on the rails there: a cut standing close behind, a yard's cars.
        var centre = Line.Sample(host.Path, front - ht.Length / 2).Position;
        foreach (var r in _rakes)
        {
            if (r == host || IsHandcar(r))
                continue;
            foreach (var v in r.Consist.Vehicles)
            {
                var f = _frames[v.Id];
                double dx = f.Origin.X - centre.X, dz = f.Origin.Z - centre.Z;
                if (Math.Sqrt(dx * dx + dz * dz) < f.Shape.HalfLength + ht.Length / 2 + 0.5)
                    return null;
            }
        }
        return (host.Path, front);
    }

    /// <summary>
    /// Whether a lift would go now (note 729): hung, the train standing and room on the rails behind it; on the rails, both
    /// standing and it near enough the van's back. Riders aboard it are the caller's to check.
    /// </summary>
    public bool HandcarCanGo()
    {
        var t = Tuning.Handcar;
        return Handcar switch
        {
            { Carried: true } => RakeOf(HandcarHost).Speed < t.StandBelow && HandcarPlace() is not null,
            { Carried: false } h => RakeOf(HandcarHost).Speed < t.StandBelow && RakeOf(h.Id).Speed < t.StandBelow && HandcarFromHome() <= t.StowReach,
            _ => false,
        };
    }

    /// <summary>
    /// Sets the hung handcar on the rails behind the van (note 729): only with the train standing (handcar.standBelow), and
    /// where there's track and room. Returns whether it went down.
    /// </summary>
    public bool SetHandcarOnRails()
    {
        if (Handcar is not { Carried: true } h || !HandcarCanGo() || HandcarPlace() is not { } place)
            return false;
        var rake = RakeOf(h.Id);
        rake.Path = place.Path;
        rake.Distance = rake.PreviousDistance = place.Front;
        rake.Velocity = 0;
        h.Carried = false;
        h.Drive = 0;
        h.Brake = 0;
        Readdress(rake);
        UpdatePoses();
        return true;
    }

    /// <summary>
    /// How far the handcar on the rails is from its place on the van (note 729): its middle to the back of the van's rear
    /// platform, across the ground. Infinity when it's hung, or without one.
    /// </summary>
    public double HandcarFromHome()
    {
        if (Handcar is not { Carried: false } h)
            return double.PositiveInfinity;
        var van = _frames[HandcarHost];
        double back = van.Shape.Platform is { } p ? p.Max.Z : van.Shape.HalfLength;
        var home = van.ToWorld(new Double3(0, 0, back));
        var at = _frames[h.Id].Origin;
        double dx = at.X - home.X, dz = at.Z - home.Z;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>
    /// Hangs the handcar back on the van (note 729): both standing, it within handcar.stowReach of the van's back. Whoever's
    /// riding it is the caller's to check. Returns whether it went up.
    /// </summary>
    public bool StowHandcar()
    {
        if (Handcar is not { Carried: false } h || !HandcarCanGo())
            return false;
        h.Carried = true;
        h.Drive = 0;
        h.Brake = 0;
        HangHandcar();
        UpdatePoses();
        return true;
    }

    /// <summary>The hung handcar's rake kept where its van is (note 729): anything reading where a rake is finds it there.</summary>
    void HangHandcar()
    {
        if (Handcar is not { Carried: true } h)
            return;
        var rake = RakeOf(h.Id);
        var host = RakeOf(HandcarHost);
        rake.Path = host.Path;
        rake.Distance = host.Distance;
        rake.PreviousDistance = host.PreviousDistance;
        rake.Velocity = 0;
    }

    /// <summary>
    /// Where a lift under way is taking the handcar (note 729), for drawing it part-way: hung, its frame set on the rails
    /// behind the van; on the rails, its frame hung on the van. Null without one, or with nowhere to set it.
    /// </summary>
    public CarFrame? HandcarLiftedTo()
    {
        if (Handcar is not { } h)
            return null;
        if (!h.Carried)
            return _frames[HandcarHost].Shape is null ? null : Handcar.Hung(Tuning.Handcar, Tuning.Geometry, _frames[HandcarHost], h.Id);
        if (HandcarPlace() is not { } place)
            return null;
        double length = Tuning.Handcar.Length, inset = length * 0.2;
        var fb = Line.Sample(place.Path, place.Front - inset).Position;
        var rb = Line.Sample(place.Path, place.Front - length + inset).Position;
        var forward = (fb - rb).Normalized;
        return CarFrame.From(new CarPose(h.Id, Double3.Lerp(fb, rb, 0.5), forward, length, place.Front), 0, Shape(Tuning.Geometry, VehicleKind.Handcar, false, false));
    }

    /// <summary>The handcar's state as the host has it (note 729), or null without one.</summary>
    public HandcarState? HandcarNow => Handcar is { } h ? new HandcarState(h.Carried, h.Phase, h.Drive, h.Brake, h.Lift, h.Pumpers) : null;

    /// <summary>Adopts the handcar's state from the host (note 729).</summary>
    public void MirrorHandcar(HandcarState s)
    {
        if (Handcar is not { } h)
            return;
        h.Carried = s.Carried;
        h.Phase = s.Phase;
        h.Drive = s.Drive;
        h.Brake = s.Brake;
        h.Lift = s.Lift;
        h.Pumpers = s.Pumpers;
    }

    /// <summary>A crewmate stood on the handcar on the rails (note 729): out on the open line, not aboard the train.</summary>
    public bool OnHandcar(int parent) => Handcar is { Carried: false } h && parent == h.Id;

    /// <summary>
    /// Puts a rake of a switchyard's cars standing on a siding (GDD §18; WP15b, note 187), its front <paramref name="distance"/>
    /// along <paramref name="path"/>, uncoupled, its handbrakes on. Its cars take the ids after every vehicle there is, so a
    /// host and its clients, standing the same cars from the same route, agree on them. Done before the night begins.
    /// </summary>
    public IReadOnlyList<Vehicle> Stand(int path, double distance, IReadOnlyList<(double Load, CargoKind Cargo)> cars) =>
        Stand(path, distance, cars, null);

    /// <summary>
    /// Stands a blocked siding's derelict cars (level-design D.2; note 294) as <see cref="Stand(int, double, IReadOnlyList{ValueTuple{double, CargoKind}})"/>
    /// does a switchyard's: <paramref name="count"/> empty cars, their shells at <paramref name="integrity"/> and anything loaded
    /// into them paying <paramref name="pays"/> of itself.
    /// </summary>
    public IReadOnlyList<Vehicle> StandDerelicts(int path, double distance, int count, double integrity, double pays) =>
        Stand(path, distance, [.. Enumerable.Repeat((0.0, CargoKind.None), count)], (integrity, pays));

    IReadOnlyList<Vehicle> Stand(int path, double distance, IReadOnlyList<(double Load, CargoKind Cargo)> cars, (double Integrity, double Pays)? derelict)
    {
        var consist = new Consist(Tuning);
        int id = _vehicles.Length;
        foreach (var (load, cargo) in cars)
            consist.Add(new Vehicle(id++, VehicleKind.Cargo, load)
            {
                YardCar = true,
                Derelict = derelict is not null,
                Cargo = load > 0 ? cargo : CargoKind.None,
                LampLit = false,
                Integrity = derelict?.Integrity ?? 1,
                CargoIntegrity = derelict?.Pays ?? 1,
            });
        if (consist.Vehicles.Count == 0)
            return [];
        Array.Resize(ref _vehicles, id);
        Array.Resize(ref _poses, id);
        Array.Resize(ref _frames, id);
        foreach (var v in consist.Vehicles)
            _vehicles[v.Id] = v;
        _rakes.Add(new TrainDynamics(consist) { Path = path, Distance = distance, PreviousDistance = distance, Handbrake = true });
        UpdatePoses();
        return consist.Vehicles;
    }

    /// <summary>
    /// A rake still standing as the night found it (note 187): a switchyard's cars and nothing else, on a siding, the engine
    /// not in it. Not part of the train until it's coupled up, so not "cars left behind" either. Derelicts alone (note 294)
    /// are never the train's, wherever they've been put away.
    /// </summary>
    /// <remarks>The handcar's rake (note 729) too: it's never the train's, wherever it is, and never cars lost.</remarks>
    public bool Standing(TrainDynamics rake) =>
        IsHandcar(rake) || !rake.Consist.HasEngine && (rake.Consist.Vehicles.All(v => v.Derelict)
            || rake.Path >= 0 && rake.Path < Line.Branches.Count && Line.Branches[rake.Path].Kind == BranchKind.Spur && rake.Consist.Vehicles.All(v => v.YardCar));

    /// <summary>A vehicle in a rake still standing as the night found it (note 187).</summary>
    public bool StandingCar(int vehicleId) =>
        vehicleId >= 0 && vehicleId < _vehicles.Length && _vehicles[vehicleId] is { YardCar: true }
        && _rakes.FirstOrDefault(r => r.Consist.IndexOf(vehicleId) >= 0) is { } rake && Standing(rake);

    /// <summary>The train's rakes: the engine's and any cut off it, not counting the yards' standing cars (note 187).</summary>
    public int TrainRakes => _rakes.Count(r => !Standing(r));

    public BoilerTuning? BoilerTuning { get; set; }
    /// <summary>The hot boxes' tuning (note 331), when the night has them: their seconds count, and they drag.</summary>
    public HotBoxTuning? HotBoxTuning { get; set; }
    /// <summary>The lamps' guttering tuning (note 346), when the night has it: for how near a guttering lamp is to going out.</summary>
    public GutterTuning? Gutter { get; set; }
    /// <summary>The couplings' working loose (note 356), when the night has it: for where a loose one's tightened, and how near it is to parting.</summary>
    public LooseTuning? Loose { get; set; }
    public Boiler Boiler;
    /// <summary>True for the one tick on which the boiler ruptured.</summary>
    public bool RupturedThisTick { get; private set; }

    /// <summary>The engine's rake: the train that can drive.</summary>
    public TrainDynamics Dynamics => _engineRake;
    public IReadOnlyList<TrainDynamics> Rakes => _rakes;
    public IReadOnlyList<Vehicle> Vehicles => _vehicles;

    /// <summary>
    /// The car the repair kit and the crew lockers ride in (train.json kit.repairKitCar): that car if it's a walk-in car,
    /// else the nearest walk-in car ahead of it, else the first; null with none. Fixed for the night: a car keeps its id
    /// when the ones ahead of it are cut away.
    /// </summary>
    public int? KitCar
    {
        get
        {
            if (Tuning.Geometry.Interior is null)
                return null;
            var cars = _vehicles.Where(v => v is not null && !v.IsEngine && !v.YardCar && !v.IsHandcar).Select(v => v.Id).Order().ToList();
            if (cars.Count == 0)
                return null;
            int want = Tuning.Kit.RepairKitCar;
            return cars.Contains(want) ? want : cars.Where(c => c < want).DefaultIfEmpty(cars[0]).Max();
        }
    }
    public RailLine Line { get; }
    /// <summary>Poses of every vehicle, indexed by vehicle id.</summary>
    public IReadOnlyList<CarPose> Cars => _poses;
    /// <summary>Local frames and collision shapes of every vehicle, indexed by vehicle id, valid for the current tick.</summary>
    public IReadOnlyList<CarFrame> Frames => _frames;
    /// <summary>Couplings and collisions that happened this tick (for audio, damage feedback and tests).</summary>
    public IReadOnlyList<RakeContact> ContactsThisTick => _contacts;
    /// <summary>The engine's controls as of the last step; a working engine puts its couplings under load.</summary>
    public TrainControls LastControls { get; private set; }

    /// <summary>
    /// True when the coupling behind <paramref name="vehicleId"/> is under load: its rake has the engine
    /// and is working (throttle or brake) while moving. Cutting takes longer then (spec F.3 hints at this).
    /// </summary>
    public bool CouplingUnderLoad(int vehicleId)
    {
        var rake = RakeOf(vehicleId);
        return rake.Consist.HasEngine && rake.Speed > 0.1 && (LastControls.Throttle > 0 || LastControls.Brake > 0);
    }

    /// <summary>Winds a rake's handbrakes on or off. A rake with the engine uses its air brakes instead.</summary>
    public void SetHandbrake(int vehicleId, bool on)
    {
        var rake = RakeOf(vehicleId);
        if (!rake.Consist.HasEngine)
            rake.Handbrake = on;
    }

    /// <summary>Traction multiplier for the whole train this tick (Grease sets it; 1 is dry rail).</summary>
    public double Traction { get; set; } = 1;
    /// <summary>Someone's at a sandbox on the running boards this tick, sanding (set by crew actions, used by the lineside).</summary>
    public bool Sanding { get; set; }
    /// <summary>
    /// Whether a car's open roof hatch can't be shut now (T99: something the crane has in it, set by whoever owns the
    /// cranes). Asked the same on host and client, from replicated state.
    /// </summary>
    public Func<int, bool>? HatchBlocked { get; set; }
    /// <summary>
    /// The engine's forward lamp, smashed (note 301, slice 2): the seconds of glass work left before it can be lit again
    /// (<see cref="World.LampOutSeconds"/>), and the wrench's work on it. The world's, lent to the crew's hands.
    /// </summary>
    public Func<double>? LampOut { get; set; }
    public Action<double>? MendLamp { get; set; }
    /// <summary>How much of the grip sanding has brought back on greased rail, 0 to 1 (App. A.2).</summary>
    public double Sand { get; set; }
    /// <summary>
    /// A vehicle something is dragging on this tick (the Weight, App. A.3), or −1; its rake is held back by
    /// <see cref="DragFactor"/> times the engine's full tractive force: more than the engine can pull against.
    /// </summary>
    public int DraggedVehicle { get; set; } = -1;
    public double DragFactor { get; set; }
    /// <summary>
    /// Host, this tick: the engine's rake is lurching on a posted bend taken too fast (<c>Lineside.Hazards</c>: over its board
    /// by sight.json <c>lurchOver</c>). Held long enough it works a pin loose (<see cref="LooseTuning.BendSeconds"/>, note 732).
    /// </summary>
    public bool Lurching { get; set; }
    /// <summary>Host, this tick: the posted board (km/h) of the bend the engine's rake is lurching on, for the line that says so (note 854).</summary>
    public int LurchBoardKmh { get; set; }

    /// <summary>At a buffer stop: the end of the line, a dead line's end, or back at the start.</summary>
    public bool AtEndOfLine => Dynamics.Distance >= Line.PathLength(Dynamics.Path) || RearDistance <= 0;
    /// <summary>The engine is on the main line (not off down a branch).</summary>
    public bool OnMain => Line.OnMain(Dynamics.Path, Dynamics.Distance);

    /// <summary>Whether a branch's switch is set for the branch (true) or for the main line (false, as they start).</summary>
    public bool Diverging(int branch) => _diverging[branch];

    /// <summary>
    /// A wheel is on a switch's points (within <paramref name="pointsLength"/> of the toe): they won't move. On either leg:
    /// a car stood just inside the branch is over the blades as much as one on the main line (note 289: only the main
    /// line's leg was counted, so the points went over under a car left at the mouth of a spur).
    /// </summary>
    public bool PointsOccupied(int branch, double pointsLength) => OnThePoints(branch, pointsLength) is not null;

    /// <summary>
    /// The rake with a wheel on a switch's points (<see cref="PointsOccupied"/>), or null: the train's own, or cars standing
    /// there (note 665: the cab said "POINTS HELD", and the director couldn't tell what was holding them).
    /// </summary>
    public TrainDynamics? OnThePoints(int branch, double pointsLength)
    {
        double toe = Line.Branches[branch].Toe;
        return _rakes.FirstOrDefault(r => On(r, RailLine.MainPath) is { } o && o.Rear < toe + pointsLength && o.Front > toe - pointsLength
            || On(r, branch) is { } d && d.Rear < pointsLength);
    }

    /// <summary>Throws a switch, unless a wheel is on its points. Returns whether it moved.</summary>
    public bool ThrowSwitch(int branch, bool diverge, double pointsLength)
    {
        if (_diverging[branch] == diverge || PointsOccupied(branch, pointsLength))
            return false;
        _diverging[branch] = diverge;
        return true;
    }

    /// <summary>Adopts a switch's setting from the host.</summary>
    public void MirrorSwitch(int branch, bool diverge)
    {
        if (branch >= 0 && branch < _diverging.Length)
            _diverging[branch] = diverge;
    }
    public double RearDistance => Dynamics.RearDistance;
    TrainTuning Tuning => _engineRake.Tuning;

    /// <summary>A rake with a car off its rails (note 423) is held by this much (m/s²): far past what any engine pulls or grade drives.</summary>
    public const double OffRailsDrag = 1000;

    static bool AnyOffRails(TrainDynamics rake)
    {
        var vehicles = rake.Consist.Vehicles;
        for (int i = 0; i < vehicles.Count; i++)
            if (vehicles[i].OffRails)
                return true;
        return false;
    }

    double DragOn(TrainDynamics rake) =>
        DraggedVehicle >= 0 && rake.Consist.IndexOf(DraggedVehicle) >= 0 ? DragFactor * _engineRake.MaxTractiveForce / rake.Consist.MassTonnes : 0;

    public TrainDynamics RakeOf(int vehicleId) => _rakes.First(r => r.Consist.IndexOf(vehicleId) >= 0);

    /// <summary>The vehicle coupled directly behind <paramref name="vehicleId"/>, or −1.</summary>
    public int VehicleBehind(int vehicleId)
    {
        var c = RakeOf(vehicleId).Consist;
        int i = c.IndexOf(vehicleId);
        return i + 1 < c.Vehicles.Count ? c.Vehicles[i + 1].Id : -1;
    }

    /// <summary>The vehicle coupled directly ahead of <paramref name="vehicleId"/>, or −1.</summary>
    public int VehicleAhead(int vehicleId)
    {
        var c = RakeOf(vehicleId).Consist;
        int i = c.IndexOf(vehicleId);
        return i > 0 ? c.Vehicles[i - 1].Id : -1;
    }

    /// <summary>
    /// The couplings cut since the world last read them (note 663), each with what cut it: the host tells the crew of the cars
    /// it lost the engine, and why.
    /// </summary>
    public List<PartingMark> Partings { get; } = [];

    /// <summary>Cuts the coupling behind <paramref name="vehicleId"/>. Returns false if nothing is coupled there.</summary>
    public bool Uncouple(int vehicleId, Parting cause = Parting.Unknown, int who = -1)
    {
        var rake = RakeOf(vehicleId);
        int index = rake.Consist.IndexOf(vehicleId);
        if (index + 1 >= rake.Consist.Vehicles.Count)
            return false;
        Partings.Add(new PartingMark(vehicleId, rake.Consist.Vehicles[index + 1].Id, cause, who));
        var front = rake.Consist;
        var rear = front.SplitAfter(index);
        // (Cut at a Knotter's gap, note 365, the rear rake stands where its body held it, and the gap's its no more.)
        double shift = front.LengthMetres + Tuning.Geometry.CouplingGap + front.Vehicles[^1].Knot;
        front.Vehicles[^1].Knot = 0;
        var cut = new TrainDynamics(rear)
        {
            // Behind the front half on the same track: the path only matters past a branch's points, where it's the front half's.
            Path = rake.Path,
            Distance = rake.Distance - shift,
            PreviousDistance = rake.PreviousDistance - shift,
            Velocity = rake.Velocity,
            FrontCouplerLocked = true,
            Handbrake = rake.Handbrake,
        };
        if (rear.HasEngine)
        {
            // Keep the engine's rake object as the train: it takes the rear half, the new rake the front.
            (rake.Consist, cut.Consist) = (rear, front);
            (rake.Distance, cut.Distance) = (cut.Distance, rake.Distance);
            (rake.PreviousDistance, cut.PreviousDistance) = (cut.PreviousDistance, rake.PreviousDistance);
            (rake.FrontCouplerLocked, cut.FrontCouplerLocked) = (true, rake.FrontCouplerLocked);
            rake.Handbrake = false;
        }
        // Cars cut at a stand are parked with handbrakes wound on; cut at speed, they roll free.
        if (Math.Abs(cut.Velocity) < Tuning.Couplings.ParkBelowSpeed)
            cut.Handbrake = true;
        _rakes.Add(cut);
        UpdatePoses();
        return true;
    }

    public TrainState Capture() => new(
        _rakes.Select(r => new RakeState(r.Consist.Vehicles.Select(v => v.Id).ToArray(), r.Distance, r.Velocity, r.BrakeEfficiency, r.Handbrake, r.FrontCouplerLocked, r.Path)).ToArray(),
        _vehicles.Select(v => new VehicleState(v.Id, v.Load, v.Integrity, v.CargoIntegrity, v.Gun, v.DoorsOpen, v.Cargo, v.LampLit, v.Eaten, v.LockersOpen, v.Breached, v.BreachAt, v.Char, v.HotBox, v.Gutter, v.Loose, v.Wound, v.Seized, v.Knot, v.OffRails, v.Placed, v.Grill, v.Beers, v.LooseCause, v.Pinned)).ToArray(),
        Boiler)
    { Handcar = HandcarNow };

    /// <summary>Adopts host state and rebuilds rakes and poses; clients then re-simulate forward from it.</summary>
    public void Restore(TrainState state)
    {
        foreach (var v in state.Vehicles)
        {
            var vehicle = _vehicles[v.Id];
            vehicle.Load = v.Load;
            vehicle.Integrity = v.Integrity;
            vehicle.CargoIntegrity = v.CargoIntegrity;
            vehicle.Gun = v.Gun;
            vehicle.DoorsOpen = v.DoorsOpen;
            vehicle.Cargo = v.Cargo;
            vehicle.LampLit = v.LampLit;
            vehicle.Eaten = v.Eaten;
            vehicle.LockersOpen = v.LockersOpen;
            vehicle.Breached = v.Breached;
            vehicle.BreachAt = v.BreachAt;
            vehicle.Char = v.Char is { } c ? (byte[])c.Clone() : [];
            vehicle.HotBox = v.HotBox;
            vehicle.Gutter = v.Gutter;
            vehicle.Loose = v.Loose;
            vehicle.LooseCause = v.LooseCause;
            vehicle.Pinned = v.Pinned;
            vehicle.Wound = v.Wound;
            vehicle.Seized = v.Seized;
            vehicle.Knot = v.Knot;
            vehicle.OffRails = v.OffRails;
            vehicle.Placed = v.Placed;
            vehicle.Grill = v.Grill;
            vehicle.Beers = v.Beers;
        }
        if (state.Handcar is { } hs)
            MirrorHandcar(hs);
        Rebuild(state.Rakes);
        Boiler = state.Boiler;
        UpdatePoses();
    }

    /// <summary>
    /// A resumed night's train as its save left it (spec E "rolls back to last POI autosave"; note 481): the rakes as they
    /// were, each vehicle in the one it was in and in its order, the engine's with a switchyard's picked-up cars ahead of
    /// it, every rake at rest. Returns false and changes nothing if they aren't this train's (each vehicle in exactly one,
    /// one with the engine): a save from another build of the night.
    /// </summary>
    public bool Resume(IReadOnlyList<RakeState> rakes)
    {
        var seen = new bool[_vehicles.Length];
        int engines = 0;
        // The handcar (note 729) is hung on the van again on a resumed night, whatever the save had (or if it had none).
        if (Handcar is { } handcar)
        {
            rakes = [.. rakes.Select(r => r with { Vehicles = r.Vehicles?.Where(id => id != handcar.Id).ToArray() ?? [] }).Where(r => r.Vehicles.Length > 0)];
            seen[handcar.Id] = true;
        }
        foreach (var r in rakes)
        {
            if (r.Vehicles is not { Length: > 0 } || r.Path < RailLine.MainPath || r.Path >= Line.Branches.Count)
                return false;
            foreach (int id in r.Vehicles)
            {
                if (id < 0 || id >= seen.Length || seen[id])
                    return false;
                seen[id] = true;
                if (_vehicles[id].IsEngine)
                    engines++;
            }
        }
        if (engines != 1 || seen.Any(s => !s))
            return false;
        var hung = HandcarRake;
        Rebuild(rakes.Select(r => r with { Velocity = 0, BrakeEfficiency = 1 }));
        if (hung is not null && Handcar is { } h)
        {
            _rakes.Add(hung);
            h.Carried = true;
            h.Drive = h.Brake = h.Lift = 0;
            HangHandcar();
        }
        foreach (var rake in _rakes)
            rake.PreviousDistance = rake.Distance;
        UpdatePoses();
        return true;
    }

    void Rebuild(IEnumerable<RakeState> rakes)
    {
        var previous = _rakes.ToDictionary(r => r.Consist.Vehicles[0].Id);
        _rakes.Clear();
        foreach (var r in rakes)
        {
            var consist = new Consist(Tuning);
            foreach (int id in r.Vehicles)
                consist.Add(_vehicles[id]);
            // Reuse the engine rake object so references to Dynamics stay valid, and any rake that still
            // exists so its render interpolation carries on. Never the engine's for a rake without it: the engine's rake can
            // lead with cars a switchyard put ahead of it (note 481), and those cut off again (note 613's three nights) would
            // otherwise take the engine's object with them, leaving the train no engine at all.
            var before = previous.GetValueOrDefault(r.Vehicles[0]);
            if (!consist.HasEngine && before == _engineRake)
                before = null;
            var rake = consist.HasEngine ? _engineRake : before ?? new TrainDynamics(consist);
            bool known = consist.HasEngine ? previous.ContainsKey(r.Vehicles[0]) : before is not null;
            rake.Consist = consist;
            rake.Restore(r.Distance, r.Velocity, r.BrakeEfficiency);
            if (!known)
                rake.PreviousDistance = r.Distance;
            rake.Handbrake = r.Handbrake;
            rake.FrontCouplerLocked = r.FrontCouplerLocked;
            rake.Path = r.Path;
            _rakes.Add(rake);
        }
    }

    /// <summary>
    /// Set by the world each tick from the run's (replicated) phase: the train's in the fortress yard and the run hasn't begun
    /// (run.json yardIsSafe, note 263). The boiler and its fire hold: no pressure gained or lost, no coal burned, no rupture.
    /// </summary>
    public bool HeldInYard { get; set; }

    /// <summary>
    /// Where on the main line the engine's front has the night home in through a town terminus's gate (note 600;
    /// <see cref="Run.Run.TownHome"/>): a driver pulls up there, not at the end of the track. The world sets it every step
    /// from the run; none without one, or where home is the end of line's terminus zone.
    /// </summary>
    public double HomeAt { get; set; } = double.PositiveInfinity;

    public void Step(double dt, in TrainControls controls)
    {
        RupturedThisTick = false;
        LastControls = controls;
        _contacts.Clear();
        foreach (var v in _vehicles)
            v?.EndTick();
        foreach (var rake in _rakes)
            rake.PreviousDistance = rake.Distance;
        // A hot box runs on hotter (note 331), on every machine alike, so its drag is predicted as the host has it.
        if (HotBoxTuning is not null)
            foreach (var v in _vehicles)
                if (v is { HotBox: > 0 })
                    v.HotBox += dt;

        foreach (var rake in _rakes)
        {
            // Hung on the van (note 729), the handcar isn't on the rails: it goes where the van goes (laid out after).
            if (Hung(rake))
                continue;
            if (rake == _engineRake)
            {
                var effective = controls;
                if (BoilerTuning is { } bt)
                {
                    // Tractive effort comes from the pressure there is now; then the fire and the cylinders move it.
                    if (bt.SteamDrive)
                    {
                        // No regulator (T97): the engine pulls as hard as it takes to make the speed its steam allows.
                        effective.Throttle = Math.Clamp((Boiler.SteamSpeed(bt, rake.Tuning.MaxSpeed) - rake.Speed) / bt.DriveSpeedBand, 0, 1);
                        // Standing (under 0.05 m/s), it isn't working: no exhaust beats to draw the fire (the blower does, standingDraft), no
                        // steam through the cylinders.
                        RupturedThisTick = !HeldInYard && Boiler.Step(bt, dt, rake.Speed < 0.05 ? 0 : effective.Throttle, rake.Consist.CarCount, rake.Speed < 0.05 ? 0 : rake.Speed / rake.Tuning.MaxSpeed);
                    }
                    else
                    {
                        effective.Throttle *= Boiler.PowerFactor(bt);
                        RupturedThisTick = !HeldInYard && Boiler.Step(bt, dt, controls.Throttle, rake.Consist.CarCount);
                    }
                }
                // A failing engine's pull and brake go with its shell (note 576); broken down, it pulls nothing.
                if (Dynamics.Tuning.Failing is { Enabled: true } ft && _vehicles.Length > 0 && _vehicles[0] is not null)
                {
                    effective.Throttle *= Failing.Power(ft, _vehicles[0]);
                    effective.Brake *= Failing.Brake(ft, _vehicles[0]);
                }
                _pulling = rake.Speed < 0.05 ? 0 : effective.Throttle;
                double before = rake.Speed;
                rake.Step(dt, effective, Conditions(rake));
                BrakeShock(rake, before, dt);
            }
            else if (IsHandcar(rake) && Handcar is { } h)
            {
                // The handcar on the rails (note 729): its pumpers' drive, its brake, the grade, its own rolling and air.
                var ht = Tuning.Handcar;
                double v = Handcar.Step(ht, rake.Velocity, h.Drive, h.Brake, AverageGrade(rake), Tuning.Gravity, CreatureDrag(rake), dt);
                rake.Roll(v, dt);
                h.Phase = Handcar.Swing(ht, h.Phase, v * dt);
            }
            else
            {
                var parked = new TrainControls { Brake = rake.Handbrake ? 1 : 0, Reverser = 1 };
                double before = rake.Speed;
                rake.Step(dt, parked, Conditions(rake));
                BrakeShock(rake, before, dt);
            }
            // What drags at it where the line says (brass across the rail, linegen plan §7.2).
            if (Line.Conditions is { } lc && lc.Drag(rake.Path, rake.Distance, rake.Speed) is var drag and > 0 && rake.Speed > 0)
                rake.Velocity -= Math.Sign(rake.Velocity) * Math.Min(rake.Speed, drag * dt);
            TakeSwitches(rake);
            // Buffer stops: the line ends are hard limits.
            double min = rake.Consist.LengthMetres, max = Line.PathLength(rake.Path);
            if (rake.Distance > max || rake.Distance < min)
            {
                // A branch's buffer stop is a stop block at the end of a dead line: running into it hurts (App. A.7).
                if (rake.Distance > max && rake.Path >= 0 && !Line.Branches[rake.Path].Rejoins)
                    HitBufferStop(rake);
                rake.Distance = Math.Clamp(rake.Distance, min, max);
                rake.Velocity = 0;
            }
        }
        ResolveContacts();
        foreach (var rake in _rakes)
        {
            if (Hung(rake))
                continue;
            TakeSwitches(rake); // shoved through the points by a collision
            Readdress(rake);
        }
        HangHandcar();
        // Battered shells let go work themselves apart as they run (note 576), on every machine alike.
        Failing.Step(this, _rakes, _engineRake, _pulling, dt);
        UpdatePoses();
    }

    /// <summary>
    /// The vehicles worn through by running failing (note 576, <see cref="Failing.Step"/>): what comes apart. On the host it
    /// decides; a client's is only its prediction's, and unused.
    /// </summary>
    public HashSet<int> WornThrough { get; } = [];

    /// <summary>How hard the engine pulled this tick (its effective throttle, 0 standing): what works a failing engine apart.</summary>
    double _pulling;

    /// <summary>
    /// Keeps each rake addressed along the track it's on (see <see cref="RailLine"/>'s paths): backed out through a
    /// branch's points it's wholly on the main line again; its front back on the main line past an alternate, it's in
    /// main-line distance (tail still on the alternate) until the tail has followed it off.
    /// </summary>
    void Readdress(TrainDynamics rake)
    {
        if (rake.Path >= 0)
        {
            var b = Line.Branches[rake.Path];
            if (rake.Distance <= b.Toe)
                rake.Path = RailLine.MainPath;
            else if (b.Rejoins && rake.Distance > b.End)
                Shift(rake, RailLine.ViaPath(b.Index), b.Offset);
        }
        else if (RailLine.ViaOf(rake.Path) is var via and >= 0)
        {
            var b = Line.Branches[via];
            if (rake.RearDistance >= b.Rejoin)
                rake.Path = RailLine.MainPath;
            else if (rake.Distance < b.Rejoin)
                Shift(rake, b.Index, -b.Offset);
        }
    }

    static void Shift(TrainDynamics rake, int path, double by)
    {
        rake.Path = path;
        rake.Distance += by;
        rake.PreviousDistance += by;
    }

    /// <summary>The track under a rake this tick: its grade, and the rail's traction (wet rail on a generated line, §14).</summary>
    TrackConditions Conditions(TrainDynamics rake) => new()
    {
        GradePercent = AverageGrade(rake),
        Traction = Traction * (Line.Conditions?.Adhesion(rake.Path, rake.Distance) ?? 1),
        // The Weight's drag on the rake it holds (App. A.3); and a ruptured engine's seized cylinders (T109) on its own, hard
        // down to a coast.
        Drag = DragOn(rake) + (rake == _engineRake && Boiler.Ruptured && BoilerTuning is { } bt && rake.Speed > bt.RuptureCoastBelow ? bt.RuptureDecel : 0)
            // And an engine short of steam holds back the speed its steam can't make (boiler.json starvedDecel, note 319).
            + (rake == _engineRake && BoilerTuning is { } st ? st.StarvedDrag(Boiler, rake.Speed, rake.Tuning.MaxSpeed) : 0)
            // And its hot boxes run dry (upkeep.json hotBox, note 331): each takes some top speed off it.
            + (HotBoxTuning is { } ht ? HotBoxes.Drag(ht, rake, rake.Tuning.MaxSpeed, _engineRake.MaxTractiveForce / rake.Consist.MassTonnes) : 0)
            // And what the creatures have done to its cars: brakes the Brakeman wound on, an axle Hotbox seized (notes 364, 367).
            + CreatureDrag(rake)
            // And a car off its rails (the tipple's bad clamp, note 423) holds its whole rake fast: more than anything can pull.
            + (AnyOffRails(rake) ? OffRailsDrag : 0),
        // A shovelful flaring in the firebox (note 903): the engine surges, a second or two after the stroke. Only running (from
        // boiler.json surgeFrom) and off the brake: a standing train doesn't lurch, the yard's shunting at walking pace isn't
        // nudged off its marks, and a driver bringing it in to a stop isn't pushed past it.
        Surge = rake == _engineRake && !HeldInYard && LastControls.Brake <= 0 && BoilerTuning is { } sb && rake.Speed >= sb.SurgeFrom ? sb.Surge(Boiler) : 0,
    };

    /// <summary>The top speed a car with a seized axle holds the train to (enemies.json <c>hotbox.seizedTopSpeed</c>, set with the enemies).</summary>
    public double SeizedTopSpeed { get; set; } = 7;
    /// <summary>Over it, the drag as a share of what the engine can pull (enemies.json <c>hotbox.seizedHold</c>).</summary>
    public double SeizedHold { get; set; } = 1.1;
    /// <summary>How a seized axle's repaired (enemies.json <c>hotbox</c>), when the creatures are on; null, nothing seizes.</summary>
    public Enemies.HotboxTuning? SeizedRepair { get; set; }

    /// <summary>
    /// A rake's drag from its wound handbrakes (each car's handbrake on its own mass, as a standing rake's are: note 364) and
    /// its seized axles (over <see cref="SeizedTopSpeed"/>, more than the engine can pull: note 367). The same on every machine.
    /// </summary>
    double CreatureDrag(TrainDynamics rake)
    {
        double wound = 0;
        bool seized = false;
        foreach (var v in rake.Consist.Vehicles)
        {
            if (v.Wound)
                wound += v.MassTonnes(rake.Tuning);
            seized |= v.Seized;
        }
        double drag = wound > 0 ? rake.Tuning.Couplings.HandbrakeDecel * wound / rake.Consist.MassTonnes : 0;
        if (seized && rake.Speed > SeizedTopSpeed)
            drag += SeizedHold * _engineRake.MaxTractiveForce / rake.Consist.MassTonnes;
        return drag;
    }

    /// <summary>A rake's front running forward through a branch's points goes where the switch is set.</summary>
    void TakeSwitches(TrainDynamics rake)
    {
        // A rake whose tail is still coming off an alternate goes straight on (junctions are never that close).
        if (rake.Path != RailLine.MainPath || rake.Distance <= rake.PreviousDistance)
            return;
        foreach (var b in Line.Branches)
            if (_diverging[b.Index] && rake.PreviousDistance <= b.Toe && rake.Distance > b.Toe)
            {
                rake.Path = b.Index;
                return;
            }
    }

    /// <summary>
    /// GDD §19's fragile medicine (train.json <c>fragile</c>; note 182): a contact closing at <paramref name="closing"/> spoils a
    /// car of it from a gentler knock than the rest, and harder; <paramref name="damage"/> is what the rest already took, which
    /// it took too.
    /// </summary>
    void Jolt(IEnumerable<Vehicle> vehicles, double closing, double damage)
    {
        if (Tuning.Fragile is not { } f || closing <= f.SafeContactSpeed)
            return;
        var c = Tuning.Couplings;
        double spoil = (closing - f.SafeContactSpeed) * (closing - f.SafeContactSpeed) * c.DamagePerSpeedSquared * c.CargoDamageShare * f.ShockShare;
        double more = spoil - damage * c.CargoDamageShare;
        if (more <= 0)
            return;
        foreach (var v in vehicles)
            if (v.Cargo == CargoKind.Medicine && v.Load > 0)
                v.CargoIntegrity = Math.Max(0, v.CargoIntegrity - more);
    }

    /// <summary>Braking harder than train.json's <c>fragile.brakeShockDecel</c> spoils a rake's medicine as it slows (GDD §19; note 182).</summary>
    void BrakeShock(TrainDynamics rake, double before, double dt)
    {
        if (Tuning.Fragile is not { } f || dt <= 0)
            return;
        double over = (before - rake.Speed) / dt - f.BrakeShockDecel;
        if (over <= 0)
            return;
        foreach (var v in rake.Consist.Vehicles)
            if (v.Cargo == CargoKind.Medicine && v.Load > 0)
                v.CargoIntegrity = Math.Max(0, v.CargoIntegrity - over * f.BrakeShockPerSecond * dt);
    }

    void HitBufferStop(TrainDynamics rake)
    {
        var c = Tuning.Couplings;
        // Note 661: a buffer stop's own knock, not a collision's (a spur's end is run up to at a crawl; at a collision's rate,
        // 5.3 m/s broke the engine down outright, and the director's run into Voss Green's stranded the night).
        var b = Tuning.BufferStop;
        double speed = Math.Max(0, rake.Velocity);
        double damage = speed > b.SafeSpeed ? (speed - b.SafeSpeed) * (speed - b.SafeSpeed) * b.DamagePerSpeedSquared : 0;
        int front = rake.Consist.Vehicles[0].Id;
        if (damage > 0)
        {
            Damage(_vehicles[front], damage, KnockCause.BufferStop, speed);
            foreach (var v in rake.Consist.Vehicles)
                v.CargoIntegrity = Math.Max(0, v.CargoIntegrity - damage * c.CargoDamageShare);
        }
        Jolt(rake.Consist.Vehicles, speed, damage);
        _contacts.Add(new RakeContact(front, -1, speed, false, damage));
    }

    /// <summary>
    /// Rakes that meet: closing gently they couple (buckeye couplers), harder they collide, share momentum,
    /// and damage the two vehicles that hit (GDD §19: cargo is physical; §23: failures cascade).
    /// <para>
    /// Resolved on each piece of track in turn (the main line's, then each branch's), by whatever of each rake is on it.
    /// A rake takes part by its stretch on that piece: its tail can be hit from behind there, but only a rake whose
    /// front is on the piece can couple on it. One whose front has gone off down another route only sideswipes what's
    /// fouling the points.
    /// </para>
    /// </summary>
    void ResolveContacts()
    {
        if (_rakes.Count < 2)
            return;
        ResolveOn(RailLine.MainPath);
        foreach (var b in Line.Branches)
            if (_rakes.Any(r => On(r, b.Index) is not null))
                ResolveOn(b.Index);
    }

    /// <summary>A rake's stretch on one piece of track, in the piece's own distance, and whether its ends are on it.</summary>
    readonly record struct Occupancy(TrainDynamics Rake, double Front, double Rear, bool FrontHere, bool RearHere);

    Occupancy? On(TrainDynamics r, int piece)
    {
        if (Hung(r))
            return null;
        double front = r.Distance, rear = r.RearDistance;
        double f = double.MinValue, b = double.MaxValue;
        bool found = false, frontHere = false, rearHere = false;
        foreach (var span in Line.Spans(r.Path))
        {
            if (span.Piece != piece)
                continue;
            double lo = Math.Max(rear, span.From), hi = Math.Min(front, span.To);
            if (lo > hi)
                continue;
            found = true;
            f = Math.Max(f, hi + span.Shift);
            b = Math.Min(b, lo + span.Shift);
            frontHere |= front >= span.From && front <= span.To;
            rearHere |= rear >= span.From && rear <= span.To;
        }
        return found ? new Occupancy(r, f, b, frontHere, rearHere) : null;
    }

    /// <summary>Distance along <paramref name="path"/> of a point on a piece of track, or null if the path doesn't run over it.</summary>
    double? PathDistance(int path, int piece, double at)
    {
        foreach (var span in Line.Spans(path))
            if (span.Piece == piece && at - span.Shift >= span.From - 1e-6 && at - span.Shift <= span.To + 1e-6)
                return at - span.Shift;
        return null;
    }

    /// <summary>The piece of track under a point on a path, and the distance along that piece.</summary>
    (int Piece, double At) Where(int path, double distance)
    {
        var spans = Line.Spans(path);
        foreach (var span in spans)
            if (distance <= span.To)
                return (span.Piece, Math.Max(distance, span.From) + span.Shift);
        return (spans[^1].Piece, distance + spans[^1].Shift);
    }

    void ResolveOn(int piece)
    {
        var on = _rakes.Select(r => On(r, piece)).OfType<Occupancy>().ToList();
        if (on.Count < 2)
            return;
        var c = Tuning.Couplings;
        double gap = Tuning.Geometry.CouplingGap;
        on.Sort((a, b) => b.Front.CompareTo(a.Front));
        for (int i = 0; i + 1 < on.Count;)
        {
            var (oa, ob) = (on[i], on[i + 1]);
            var (a, b) = (oa.Rake, ob.Rake);
            double free = oa.Rear - gap - ob.Front;
            if (b.FrontCouplerLocked && free > 0.3)
                b.FrontCouplerLocked = false;
            if (free >= 0)
            {
                i++;
                continue;
            }

            double closing = b.Velocity - a.Velocity;
            double ma = a.Consist.MassTonnes, mb = b.Consist.MassTonnes;
            int frontId = a.Consist.Vehicles[^1].Id, rearId = b.Consist.Vehicles[0].Id;
            double damage = 0;
            // The handcar (note 729) couples to nothing, and half a tonne of trolley dents no car: it's bumped along (or stopped).
            bool handcar = IsHandcar(a) || IsHandcar(b);
            if (closing > c.SafeContactSpeed && !handcar)
            {
                damage = (closing - c.SafeContactSpeed) * (closing - c.SafeContactSpeed) * c.DamagePerSpeedSquared;
                Damage(_vehicles[frontId], damage, KnockCause.Collision, closing);
                Damage(_vehicles[rearId], damage, KnockCause.Collision, closing);
                foreach (var v in a.Consist.Vehicles.Concat(b.Consist.Vehicles))
                    v.CargoIntegrity = Math.Max(0, v.CargoIntegrity - damage * c.CargoDamageShare);
            }
            if (!handcar)
                Jolt(a.Consist.Vehicles.Concat(b.Consist.Vehicles), closing, damage);
            if (closing > 0)
            {
                double v = (ma * a.Velocity + mb * b.Velocity) / (ma + mb);
                a.Velocity = b.Velocity = v;
            }
            b.Distance += free; // back to just touching, along whichever path it's on
            ob = ob with { Front = ob.Front + free, Rear = ob.Rear + free };
            on[i + 1] = ob;

            // b's front is off down another route (or a's tail is): it can foul the points but not couple on this track.
            bool couple = ob.FrontHere && oa.RearHere && !b.FrontCouplerLocked && closing <= c.CoupleMaxSpeed && !handcar;
            _contacts.Add(new RakeContact(frontId, rearId, Math.Max(0, closing), couple, damage));
            if (!couple)
            {
                i++;
                continue;
            }
            // The merged rake runs on the front one's path if that runs back over the rear one's tail, and on the rear
            // one's path otherwise (a train come back off an alternate, coupling onto cars ahead of it on the main line).
            var (tailPiece, tailAt) = Where(b.Path, b.RearDistance);
            var (path, front) = PathDistance(a.Path, tailPiece, tailAt) is not null
                ? (a.Path, a.Distance)
                : (b.Path, PathDistance(b.Path, piece, oa.Front) ?? a.Distance);
            double previous = a.PreviousDistance + (front - a.Distance);
            // Keep the engine's rake as the survivor so Dynamics stays the same object.
            TrainDynamics survivor;
            if (b == _engineRake)
            {
                b.Consist.Prepend(a.Consist);
                b.FrontCouplerLocked = a.FrontCouplerLocked;
                _rakes.Remove(a);
                on.RemoveAt(i);
                survivor = b;
            }
            else
            {
                if (a != _engineRake)
                    a.Handbrake |= b.Handbrake;
                a.Consist.Append(b.Consist);
                _rakes.Remove(b);
                on.RemoveAt(i + 1);
                survivor = a;
            }
            survivor.Path = path;
            survivor.Distance = front;
            survivor.PreviousDistance = previous;
            on[i] = On(survivor, piece) ?? on[i];
            // Stay on this index: the merged rake may now touch the one behind it.
        }
    }

    /// <summary>A knock at the couplers: an armoured car's plate takes some of it (note 184).</summary>
    void Damage(Vehicle v, double amount, KnockCause cause, double speed)
    {
        v.Batter(amount, Tuning);
        v.Knock = (cause, speed);
    }

    /// <summary>Mass-weighted grade under the engine's rake; a long train straddling a summit feels both sides.</summary>
    public double AverageGrade() => AverageGrade(_engineRake);

    public double AverageGrade(TrainDynamics rake)
    {
        var t = rake.Tuning;
        double weighted = 0, mass = 0, front = rake.Distance;
        foreach (var v in rake.Consist.Vehicles)
        {
            double len = v.Length(t), m = v.MassTonnes(t);
            weighted += m * Line.Sample(rake.Path, front - len / 2).GradePercent;
            mass += m;
            front -= len + t.Geometry.CouplingGap + v.Knot;
        }
        return mass > 0 ? weighted / mass : 0;
    }

    void UpdatePoses() => Layout(1, _poses, _frames);

    /// <summary>
    /// Vehicle poses and frames between the previous and current tick (<paramref name="alpha"/> 0..1),
    /// indexed by vehicle id. Renderers use this to draw the train smoothly without touching sim state.
    /// </summary>
    public void FramesAt(double alpha, List<CarFrame> into)
    {
        var poses = new CarPose[_vehicles.Length];
        var frames = new CarFrame[_vehicles.Length];
        Layout(alpha, poses, frames);
        into.Clear();
        into.AddRange(frames);
    }

    void Layout(double alpha, CarPose[] poses, CarFrame[] frames)
    {
        var g = Tuning.Geometry;
        foreach (var rake in _rakes)
        {
            if (Hung(rake))
                continue;
            double front = rake.PreviousDistance + (rake.Distance - rake.PreviousDistance) * alpha;
            var vehicles = rake.Consist.Vehicles;
            for (int i = 0; i < vehicles.Count; i++)
            {
                var v = vehicles[i];
                double length = v.Length(Tuning);
                // Bogies sit a fifth of the way in from each end; the body is the chord between them.
                double inset = length * 0.2;
                var fb = Line.Sample(rake.Path, front - inset).Position;
                var rb = Line.Sample(rake.Path, front - length + inset).Position;
                var forward = (fb - rb).Length > 1e-9 ? (fb - rb).Normalized : Line.Sample(rake.Path, front).Tangent;
                var pose = new CarPose(v.Id, Double3.Lerp(fb, rb, 0.5), forward, length, front);
                poses[v.Id] = pose;
                bool behind = i < vehicles.Count - 1;
                frames[v.Id] = CarFrame.From(pose, rake.Velocity, Shape(g, v.Kind, behind, v.Id == _kitCar, behind ? v.Knot : 0));
                front -= length + g.CouplingGap + (behind ? v.Knot : 0);
            }
        }
        if (Wreck is { } wreck)
            foreach (var b in wreck.Bodies)
                if (b.Vehicle >= 0 && b.Vehicle < frames.Length)
                {
                    var f = frames[b.Vehicle];
                    Double3 L(Double3 p, Double3 c) => p + (c - p) * alpha;
                    var back = L(b.PrevBack, b.Back).Normalized;
                    var up = L(b.PrevUp, b.Up).Normalized;
                    var right = Double3.Cross(up, back).Normalized;
                    frames[b.Vehicle] = new CarFrame(f.Index, L(b.PrevOrigin, b.Origin), right, Double3.Cross(back, right).Normalized, back, b.Velocity, f.Shape);
                }
        // The handcar hung on the van (note 729): stood on its end behind the rear platform, wherever the van is (its wreck too).
        if (Handcar is { Carried: true } hung && frames[HandcarHost].Shape is not null)
        {
            var f = Handcar.Hung(Tuning.Handcar, g, frames[HandcarHost], hung.Id);
            frames[hung.Id] = f;
            poses[hung.Id] = new CarPose(hung.Id, f.Origin, f.Back * -1, Tuning.Handcar.Length, poses[HandcarHost].FrontDistance);
        }
    }

    readonly Dictionary<(VehicleKind, bool, bool, int, bool), CarShape> _shapes = new();
    CarShape? _handcarShape;
    GeometryTuning? _shapeGeometry;
    int? _kitCar;

    /// <param name="lockers">The kit's car: it has the crew lockers (ARCHITECTURE §8 note 173).</param>
    /// <param name="knot">A Knotter's gap behind it (note 365): its body across the gap in place of the coupler plate.</param>
    CarShape Shape(GeometryTuning g, VehicleKind kind, bool hasCarBehind, bool lockers, double knot = 0)
    {
        if (!ReferenceEquals(g, _shapeGeometry))
        {
            _shapes.Clear();
            _shapeGeometry = g;
        }
        if (kind == VehicleKind.Handcar)
            return _handcarShape ??= Handcar.Shape(Tuning.Handcar, onRails: true);
        lockers &= Tuning.Kit.Lockers is { Names.Count: > 0 };
        // (Its body across the gap, to a tenth of a metre: a handful of shapes while it forces the cars apart.)
        int tenths = hasCarBehind ? (int)Math.Round(knot * 10) : 0;
        // The BBQ deck on the engine's front (note 698), where it's fitted.
        bool bbq = kind == VehicleKind.Engine && Tuning.Composition.BbqDeck;
        // The gramophone and the hammock in the last guard van (notes 715, 744), the hot tub on the engine's hood (note 744).
        // (A train's dressing doesn't change: one key for whatever extras this kind has.)
        bool gramophone = kind == VehicleKind.Guard && !hasCarBehind && Tuning.Composition.Dressed("gramophone");
        bool hammock = kind == VehicleKind.Guard && !hasCarBehind && Tuning.Composition.Dressed("hammock");
        bool tub = kind == VehicleKind.Engine && Tuning.Composition.Dressed("hotTub");
        bool launcher = kind == VehicleKind.Guard && !hasCarBehind && Tuning.Composition.Dressed("tennisBalls");
        bool extras = bbq || gramophone || hammock || tub || launcher;
        if (!_shapes.TryGetValue((kind, hasCarBehind, lockers, tenths, extras), out var shape))
        {
            shape = CarShape.Build(g, kind, hasCarBehind, lockers ? Tuning.Kit.Lockers : null);
            if (tenths > 0)
                shape = CarShape.Knotted(shape, g, tenths / 10.0);
            if (bbq)
                shape = CarShape.WithBbqDeck(shape, g, Tuning.Composition.Bbq);
            if (gramophone)
                shape = CarShape.WithGramophone(shape);
            if (hammock)
                shape = CarShape.WithHammock(shape);
            if (tub)
                shape = CarShape.WithHotTub(shape);
            if (launcher)
                shape = CarShape.WithLauncher(shape);
            _shapes[(kind, hasCarBehind, lockers, tenths, extras)] = shape;
        }
        return shape;
    }
}
