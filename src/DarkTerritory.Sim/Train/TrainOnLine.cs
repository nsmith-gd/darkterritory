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
    CargoKind Cargo = CargoKind.None, bool LampLit = true, double Eaten = 0);

/// <summary>Everything about the train that the host owns and clients re-simulate from.</summary>
public sealed record TrainState(RakeState[] Rakes, VehicleState[] Vehicles, Boiler Boiler);

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
    /// <summary>The stops' buildings, for whoever walks among them (T114); set with the run, alike on every machine.</summary>
    public Run.StopWalls? Walls { get; set; }

    readonly List<TrainDynamics> _rakes = new();
    readonly Vehicle[] _vehicles;
    readonly CarPose[] _poses;
    readonly CarFrame[] _frames;
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
        _poses = new CarPose[_vehicles.Length];
        _frames = new CarFrame[_vehicles.Length];
        _diverging = new bool[line.Branches.Count];
        for (int i = 0; i < _diverging.Length; i++)
            _diverging[i] = line.Branches[i].Definition.StartsDiverging;
        BoilerTuning = boiler;
        if (boiler is not null)
            Boiler = Boiler.Fresh(boiler);
        UpdatePoses();
        // The guns start where their cars' shapes stand them (the engine's forward, the guard van's back): from here they're
        // the vehicles' own, and ride the roof rails wherever they're pushed (T93).
        foreach (var f in _frames)
            if (f.Shape?.Gun is { } home && !_vehicles[f.Index].Gun.Mounted)
                _vehicles[f.Index].Gun = new GunState { Mounted = true, Z = home.Position.Z, Facing = (sbyte)(home.Facing.Z < 0 ? -1 : 1) };
    }

    public BoilerTuning? BoilerTuning { get; set; }
    public Boiler Boiler;
    /// <summary>True for the one tick on which the boiler ruptured.</summary>
    public bool RupturedThisTick { get; private set; }

    /// <summary>The engine's rake: the train that can drive.</summary>
    public TrainDynamics Dynamics => _engineRake;
    public IReadOnlyList<TrainDynamics> Rakes => _rakes;
    public IReadOnlyList<Vehicle> Vehicles => _vehicles;
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
    /// <summary>How much of the grip sanding has brought back on greased rail, 0 to 1 (App. A.2).</summary>
    public double Sand { get; set; }
    /// <summary>
    /// A vehicle something is dragging on this tick (the Weight, App. A.3), or −1; its rake is held back by
    /// <see cref="DragFactor"/> times the engine's full tractive force: more than the engine can pull against.
    /// </summary>
    public int DraggedVehicle { get; set; } = -1;
    public double DragFactor { get; set; }

    /// <summary>At a buffer stop: the end of the line, a dead line's end, or back at the start.</summary>
    public bool AtEndOfLine => Dynamics.Distance >= Line.PathLength(Dynamics.Path) || RearDistance <= 0;
    /// <summary>The engine is on the main line (not off down a branch).</summary>
    public bool OnMain => Line.OnMain(Dynamics.Path, Dynamics.Distance);

    /// <summary>Whether a branch's switch is set for the branch (true) or for the main line (false, as they start).</summary>
    public bool Diverging(int branch) => _diverging[branch];

    /// <summary>A wheel is on a switch's points (within <paramref name="pointsLength"/> of the toe): they won't move.</summary>
    public bool PointsOccupied(int branch, double pointsLength)
    {
        double toe = Line.Branches[branch].Toe;
        return _rakes.Any(r => On(r, RailLine.MainPath) is { } o && o.Rear < toe + pointsLength && o.Front > toe - pointsLength);
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

    /// <summary>Cuts the coupling behind <paramref name="vehicleId"/>. Returns false if nothing is coupled there.</summary>
    public bool Uncouple(int vehicleId)
    {
        var rake = RakeOf(vehicleId);
        int index = rake.Consist.IndexOf(vehicleId);
        if (index + 1 >= rake.Consist.Vehicles.Count)
            return false;
        var front = rake.Consist;
        var rear = front.SplitAfter(index);
        double shift = front.LengthMetres + Tuning.Geometry.CouplingGap;
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
        _vehicles.Select(v => new VehicleState(v.Id, v.Load, v.Integrity, v.CargoIntegrity, v.Gun, v.DoorsOpen, v.Cargo, v.LampLit, v.Eaten)).ToArray(),
        Boiler);

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
        }
        var previous = _rakes.ToDictionary(r => r.Consist.Vehicles[0].Id);
        _rakes.Clear();
        foreach (var r in state.Rakes)
        {
            var consist = new Consist(Tuning);
            foreach (int id in r.Vehicles)
                consist.Add(_vehicles[id]);
            // Reuse the engine rake object so references to Dynamics stay valid, and any rake that still
            // exists so its render interpolation carries on.
            var rake = consist.HasEngine ? _engineRake : previous.GetValueOrDefault(r.Vehicles[0]) ?? new TrainDynamics(consist);
            bool known = previous.ContainsKey(r.Vehicles[0]);
            rake.Consist = consist;
            rake.Restore(r.Distance, r.Velocity, r.BrakeEfficiency);
            if (!known)
                rake.PreviousDistance = r.Distance;
            rake.Handbrake = r.Handbrake;
            rake.FrontCouplerLocked = r.FrontCouplerLocked;
            rake.Path = r.Path;
            _rakes.Add(rake);
        }
        Boiler = state.Boiler;
        UpdatePoses();
    }

    public void Step(double dt, in TrainControls controls)
    {
        RupturedThisTick = false;
        LastControls = controls;
        _contacts.Clear();
        foreach (var v in _vehicles)
            v?.EndTick();
        foreach (var rake in _rakes)
            rake.PreviousDistance = rake.Distance;

        foreach (var rake in _rakes)
        {
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
                        // Held on its brake, it isn't working: no exhaust beats to draw the fire, no steam through the cylinders.
                        RupturedThisTick = Boiler.Step(bt, dt, rake.Speed < 0.05 ? 0 : effective.Throttle, rake.Consist.CarCount, rake.Speed / rake.Tuning.MaxSpeed);
                    }
                    else
                    {
                        effective.Throttle *= Boiler.PowerFactor(bt);
                        RupturedThisTick = Boiler.Step(bt, dt, controls.Throttle, rake.Consist.CarCount);
                    }
                }
                rake.Step(dt, effective, Conditions(rake));
            }
            else
            {
                var parked = new TrainControls { Brake = rake.Handbrake ? 1 : 0, Reverser = 1 };
                rake.Step(dt, parked, Conditions(rake));
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
            TakeSwitches(rake); // shoved through the points by a collision
            Readdress(rake);
        }
        UpdatePoses();
    }

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
        Drag = DragOn(rake) + (rake == _engineRake && Boiler.Ruptured && BoilerTuning is { } bt && rake.Speed > bt.RuptureCoastBelow ? bt.RuptureDecel : 0),
    };

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

    void HitBufferStop(TrainDynamics rake)
    {
        var c = Tuning.Couplings;
        double speed = Math.Max(0, rake.Velocity);
        double damage = speed > c.SafeContactSpeed ? (speed - c.SafeContactSpeed) * (speed - c.SafeContactSpeed) * c.DamagePerSpeedSquared : 0;
        int front = rake.Consist.Vehicles[0].Id;
        if (damage > 0)
        {
            Damage(_vehicles[front], damage);
            foreach (var v in rake.Consist.Vehicles)
                v.CargoIntegrity = Math.Max(0, v.CargoIntegrity - damage * c.CargoDamageShare);
        }
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
            if (closing > c.SafeContactSpeed)
            {
                damage = (closing - c.SafeContactSpeed) * (closing - c.SafeContactSpeed) * c.DamagePerSpeedSquared;
                Damage(_vehicles[frontId], damage);
                Damage(_vehicles[rearId], damage);
                foreach (var v in a.Consist.Vehicles.Concat(b.Consist.Vehicles))
                    v.CargoIntegrity = Math.Max(0, v.CargoIntegrity - damage * c.CargoDamageShare);
            }
            if (closing > 0)
            {
                double v = (ma * a.Velocity + mb * b.Velocity) / (ma + mb);
                a.Velocity = b.Velocity = v;
            }
            b.Distance += free; // back to just touching, along whichever path it's on
            ob = ob with { Front = ob.Front + free, Rear = ob.Rear + free };
            on[i + 1] = ob;

            // b's front is off down another route (or a's tail is): it can foul the points but not couple on this track.
            bool couple = ob.FrontHere && oa.RearHere && !b.FrontCouplerLocked && closing <= c.CoupleMaxSpeed;
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

    static void Damage(Vehicle v, double amount) => v.Integrity = Math.Max(0, v.Integrity - amount);

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
            front -= len + t.Geometry.CouplingGap;
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
                frames[v.Id] = CarFrame.From(pose, rake.Velocity, Shape(g, v.Kind, i < vehicles.Count - 1));
                front -= length + g.CouplingGap;
            }
        }
    }

    readonly Dictionary<(VehicleKind, bool), CarShape> _shapes = new();
    GeometryTuning? _shapeGeometry;

    CarShape Shape(GeometryTuning g, VehicleKind kind, bool hasCarBehind)
    {
        if (!ReferenceEquals(g, _shapeGeometry))
        {
            _shapes.Clear();
            _shapeGeometry = g;
        }
        if (!_shapes.TryGetValue((kind, hasCarBehind), out var shape))
            _shapes[(kind, hasCarBehind)] = shape = CarShape.Build(g, kind, hasCarBehind);
        return shape;
    }
}
