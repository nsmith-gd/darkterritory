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
public readonly record struct VehicleState(int Id, double Load, double Integrity, double CargoIntegrity, GunState Gun = default, byte DoorsOpen = 0);

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
        BoilerTuning = boiler;
        if (boiler is not null)
            Boiler = Boiler.Fresh(boiler);
        UpdatePoses();
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
        return _rakes.Any(r => r.RearDistance < toe + pointsLength && r.Distance > toe - pointsLength
            && Line.Shared(r.Path, branch) >= toe);
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
        _vehicles.Select(v => new VehicleState(v.Id, v.Load, v.Integrity, v.CargoIntegrity, v.Gun, v.DoorsOpen)).ToArray(),
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
                    effective.Throttle *= Boiler.PowerFactor(bt);
                    RupturedThisTick = Boiler.Step(bt, dt, controls.Throttle, rake.Consist.CarCount);
                }
                rake.Step(dt, effective, new TrackConditions { GradePercent = AverageGrade(rake), Traction = Traction });
            }
            else
            {
                var parked = new TrainControls { Brake = rake.Handbrake ? 1 : 0, Reverser = 1 };
                rake.Step(dt, parked, new TrackConditions { GradePercent = AverageGrade(rake), Traction = Traction });
            }
            TakeSwitches(rake);
            // Buffer stops: the line ends are hard limits.
            double min = rake.Consist.LengthMetres, max = Line.PathLength(rake.Path);
            if (rake.Distance > max || rake.Distance < min)
            {
                // A branch's buffer stop is a stop block at the end of a dead line: running into it hurts (App. A.7).
                if (rake.Distance > max && rake.Path >= 0)
                    HitBufferStop(rake);
                rake.Distance = Math.Clamp(rake.Distance, min, max);
                rake.Velocity = 0;
            }
        }
        ResolveContacts();
        foreach (var rake in _rakes)
        {
            TakeSwitches(rake); // shoved through the points by a collision
            if (rake.Path >= 0 && rake.Distance <= Line.Branches[rake.Path].Toe)
                rake.Path = RailLine.MainPath; // backed out through the points: the whole rake is on the main line
        }
        UpdatePoses();
    }

    /// <summary>A rake's front running forward through a branch's points goes where the switch is set.</summary>
    void TakeSwitches(TrainDynamics rake)
    {
        if (rake.Path >= 0 || rake.Distance <= rake.PreviousDistance)
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
    /// Resolved along each path in turn. A rake on another path takes part by whatever of it is on the track the two
    /// share (up to the points): its tail can be hit from behind, but a rake whose front has gone off down the other
    /// route can't couple to anything on this one; it only sideswipes what's fouling the points.
    /// </para>
    /// </summary>
    void ResolveContacts()
    {
        if (_rakes.Count < 2)
            return;
        ResolveOn(RailLine.MainPath);
        foreach (var b in Line.Branches)
            if (_rakes.Any(r => r.Path == b.Index))
                ResolveOn(b.Index);
    }

    /// <summary>A rake's front as seen along <paramref name="path"/>: clipped to the shared track if it's on another.</summary>
    double FrontOn(TrainDynamics r, int path) => r.Path == path ? r.Distance : Math.Min(r.Distance, Line.Shared(path, r.Path));

    void ResolveOn(int path)
    {
        var on = _rakes.Where(r => r.Path == path || r.RearDistance < Line.Shared(path, r.Path)).ToList();
        if (on.Count < 2)
            return;
        var c = Tuning.Couplings;
        double gap = Tuning.Geometry.CouplingGap;
        on.Sort((a, b) => FrontOn(b, path).CompareTo(FrontOn(a, path)));
        for (int i = 0; i + 1 < on.Count;)
        {
            var a = on[i];
            var b = on[i + 1];
            double bFront = FrontOn(b, path);
            // b's front is off down another route: it can foul the points but not couple on this track.
            bool offPath = bFront < b.Distance;
            double free = a.RearDistance - gap - bFront;
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

            bool couple = !offPath && !b.FrontCouplerLocked && closing <= c.CoupleMaxSpeed;
            _contacts.Add(new RakeContact(frontId, rearId, Math.Max(0, closing), couple, damage));
            if (!couple)
            {
                i++;
                continue;
            }
            // Keep the engine's rake as the survivor so Dynamics stays the same object.
            // The merged rake runs on the front one's path: the rear one is wholly on track the two share.
            if (b == _engineRake)
            {
                b.Consist.Prepend(a.Consist);
                b.Distance = a.Distance;
                b.PreviousDistance = a.PreviousDistance;
                b.FrontCouplerLocked = a.FrontCouplerLocked;
                b.Path = a.Path;
                _rakes.Remove(a);
                on.RemoveAt(i);
            }
            else
            {
                if (a != _engineRake)
                    a.Handbrake |= b.Handbrake;
                a.Consist.Append(b.Consist);
                _rakes.Remove(b);
                on.RemoveAt(i + 1);
            }
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
