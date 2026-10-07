using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// Trouble inside the cars (after the 100-night playtest: "more problems players need to face in cars, and reasons not
/// to roof walk the whole time; defeatable, but able to hurt or kill"). Each takes a cargo car, is told by sound through
/// its walls, and is answered from inside it, on the car's floor, by holding Use at it. Cut the car loose and it goes
/// with it. None can be shot. They run on the shared spine, so the fairness rule holds: the commit waits on a telegraph.
/// </summary>
public abstract class Incident(int id) : Enemy(id)
{
    public override PressureZone Zone => PressureZone.Interior;

    /// <summary>Where on the car's floor it is answered from: the cargo stack's face, beside the aisle.</summary>
    public static Double3 At(TrainOnLine train, int car, double along)
    {
        var layout = train.Dynamics.Tuning.Geometry.Interior!;
        var shape = train.Frames[car].Shape;
        double half = shape.HalfLength;
        // The cargo's stacked down the right-hand side; its face is in reach of the aisle.
        double face = shape.HalfWidth - layout.WallThickness - layout.CargoDepth;
        return new Double3(face, layout.FloorHeight, Math.Clamp(along, -half + 2, half - 2));
    }

    /// <summary>The crew on this car's floor, inside its walls, and whether they're holding Use within reach of it.</summary>
    protected IEnumerable<(Net.PlayerSnapshot Player, bool Working)> Inside(EnemyContext ctx, double reach)
    {
        foreach (var (p, intent) in ctx.Crew)
        {
            var s = p.State;
            if (!s.Alive || s.Parent != Attached || !PlayerMotor.Indoors(s, ctx.Train) || PlayerMotor.InCab(s, ctx.Train))
                continue;
            double dx = s.Position.X - Local.X, dz = s.Position.Z - Local.Z;
            bool near = dx * dx + dz * dz <= reach * reach;
            yield return (p, near && intent.Has(PlayerButtons.Use) && intent.MoveZ <= 0.5);
        }
    }

    /// <summary>Still coupled to the engine: cut loose, it's the Territory's problem now.</summary>
    protected bool Aboard(EnemyContext ctx) =>
        Attached > 0 && Attached < ctx.Train.Frames.Count && ctx.Train.Dynamics.Consist.Vehicles.Any(v => v.Id == Attached);

    /// <summary>
    /// The cargo car either side of this one without one of these in it already, if there is one, and if there's room for
    /// another of its kind (<paramref name="max"/> about at once: one left to itself doesn't take the whole train).
    /// </summary>
    protected int? Neighbour(EnemyContext ctx, int max)
    {
        var train = ctx.Train;
        if (ctx.World.ActiveEnemies.Count(e => !e.Gone && e.Kind == Kind) >= max)
            return null;
        foreach (int car in new[] { train.VehicleBehind(Attached), train.VehicleAhead(Attached) })
            if (car > 0 && train.Vehicles[car].Kind == VehicleKind.Cargo && !ctx.World.ActiveEnemies.Any(e => !e.Gone && e.Kind == Kind && e.Attached == car))
                return car;
        return null;
    }

    protected void Out(EnemyContext ctx)
    {
        Enter(ctx, SpinePhase.BreakOff);
        Enter(ctx, SpinePhase.Gone);
    }
}


/// <summary>
/// FIRE (GDD v1.1 App. C.5, and what the Fire Flies start, App. A.5). A cargo car catches: smoke and a crackle through its
/// walls (the tell), then flames. It burns the cargo and the car, and whoever's in it, and it grows and jumps the couplings
/// over time. Every car has a wall-mounted extinguisher: grab it, spray it (fire held with it in your hands, inside the
/// car), put it back to recharge. Each holds a limited charge. A big fire grows faster than one extinguisher can put out,
/// so it takes several at once. Chemicals spread it faster; gunpowder, coal and timber escalate it faster (App. B.9). Or
/// cut the car loose. Rule: grab the extinguishers or abandon it. A car of gunpowder and shot at full blaze goes up (§19
/// "explodes"; note 182): the car's gone, everyone near is hurt or killed, and the cars either side catch.
/// <para>
/// It burns on the car's <see cref="FireGrid"/> (the director's decision of 6 Oct 2026, App. F.1; note 267): each cell of
/// the floor, walls and roof has its own heat; a cell well alight heats the cells round it (upward faster: fire climbs the
/// walls and runs along the roof); the extinguisher cools the cell it's aimed at, and a little the ones round it; a cell
/// that burns uses up what there is of it to burn, and chars, for the rest of the night. It jumps to the next car from a
/// cell against an end wall.
/// </para>
/// </summary>
/// <remarks>
/// <see cref="Enemy.Extra"/> is how much of the car is alight, the mean of its cells' heat, 0 to 1, and <see cref="Heat"/>
/// each cell's (both replicated: the flames are drawn cell by cell, and the sound follows the whole). <see cref="Enemy.Local"/>
/// follows the heart of it along the car.
/// </remarks>
public sealed class CarFire(int id) : Incident(id)
{
    /// <summary>Host: how long each end's cells have been well alight (s, the cargo's spread multiplier in), front then rear.</summary>
    readonly double[] _blaze = new double[2];
    /// <summary>Host: how long each player's been in it this time (s), and the burn they've taken short of a whole point.</summary>
    readonly Dictionary<int, (double In, double Dose)> _burning = new();
    readonly bool[] _spread = new bool[2];
    bool _exploded;
    /// <summary>Host: what's left to burn in each cell (1 fresh; the car's <see cref="Vehicle.Char"/> is what's gone).</summary>
    double[] _fuel = [];
    /// <summary>Host: how long each cell stays wet from the extinguisher (s): it won't catch from the cells round it until it dries.</summary>
    double[] _damp = [];
    /// <summary>Where it started, on the car's floor, before it had cells (the host lays its grid out on its first tick).</summary>
    double _startHeat;
    /// <summary>Set ablaze (<see cref="Ablaze"/>): every cell within this of where it started (m along the car) at <see cref="_startHeat"/>.</summary>
    double _span = -1;

    /// <summary>Each cell's heat, 0 to 1, in its car's <see cref="FireGrid"/> order. Empty until its first tick on the host.</summary>
    public double[] Heat { get; private set; } = [];

    public override EnemyKind Kind => EnemyKind.CarFire;
    public override Sense Sense => Sense.Heat;
    public override Want Want => Want.Cargo;

    /// <summary>
    /// Alight: what set it is the record (the Fire Flies', note 190), and what it does after is the deaths and the cars lost.
    /// </summary>
    protected override Run.Incident? Punished(EnemyContext ctx) => null;

    public static CarFire In(int id, TrainOnLine train, int car, double along, CarFireTuning t) =>
        new(id) { Attached = car, Local = At(train, car, along), Extra = t.StartIntensity, _startHeat = t.StartIntensity };

    /// <summary>
    /// Already well alight when it's found (a set piece, a test): every cell within <paramref name="span"/> m along the car of
    /// where it was set, at <paramref name="heat"/>. Before its first tick.
    /// </summary>
    public CarFire Ablaze(double heat, double span = double.MaxValue)
    {
        _startHeat = heat;
        _span = span;
        Extra = heat;
        Heat = [];
        return this;
    }

    /// <summary>The cells' heat as it goes over the wire: <see cref="Bits"/> bits a cell, as many to a field as fit.</summary>
    public const int Bits = 4, PerField = 60 / Bits;

    /// <summary>Packs cell values 0..1 into wire fields (<see cref="Bits"/> bits each, rounded up so a cell alight shows alight).</summary>
    public static long[] Pack(double[] cells)
    {
        int top = (1 << Bits) - 1;
        var f = new long[1 + (cells.Length + PerField - 1) / PerField];
        f[0] = cells.Length;
        for (int i = 0; i < cells.Length; i++)
        {
            long q = Math.Clamp((long)Math.Ceiling(cells[i] * top - 1e-9), 0, top);
            f[1 + i / PerField] |= q << (i % PerField * Bits);
        }
        return f;
    }

    /// <summary>The other way: fields as <see cref="Pack"/> wrote them, from <paramref name="at"/>; empty if there are none.</summary>
    public static double[] Unpack(long[] f, int at)
    {
        if (f.Length <= at)
            return [];
        int n = (int)f[at], top = (1 << Bits) - 1;
        var cells = new double[n];
        for (int i = 0; i < n && at + 1 + i / PerField < f.Length; i++)
            cells[i] = ((f[at + 1 + i / PerField] >> (i % PerField * Bits)) & top) / (double)top;
        return cells;
    }

    /// <summary>A client's copy, from the host's record (<see cref="Net.WorldRecords"/>).</summary>
    public void RestoreHeat(double[] heat) => Heat = heat;

    /// <summary>Where it's sprayed this tick: the cell each sprayer has it aimed at (−1 aimed at none), for the bots and the tests.</summary>
    public IReadOnlyList<int> Sprayed => _sprayed;
    readonly List<int> _sprayed = [];

    /// <summary>
    /// Crew spraying it: inside, an extinguisher with charge in their hands, fire held. Each sprays the cell they're looking
    /// at, if it's in reach (App. F.1: "the extinguisher puts out the cell you aim at"); aimed at none, it's wasted.
    /// </summary>
    List<(int Player, Physics.Body Extinguisher, int Cell)> Spraying(EnemyContext ctx, FireGrid grid, CarFireTuning t)
    {
        var list = new List<(int, Physics.Body, int)>();
        double eye = ctx.Train.Dynamics.Tuning.Pick.EyeHeight;
        foreach (var (p, intent) in ctx.Crew)
        {
            var s = p.State;
            if (!s.Alive || s.Parent != Attached || !PlayerMotor.Indoors(s, ctx.Train) || !intent.Has(PlayerButtons.Fire))
                continue;
            if (ctx.World.Bodies.CarriedBy(p.Id) is not { Kind: Physics.BodyKind.Extinguisher, Charge: > 0 } ext)
                continue;
            list.Add((p.Id, ext, grid.Hit(s.Position + Double3.Up * eye, Run.Bookmarks.Forward(s.Yaw, s.Pitch), t.SprayReach)));
        }
        return list;
    }

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.CarFire;
        if (!Aboard(ctx) || FireGrid.Of(ctx.Train, Attached, t.CellSize) is not { } grid)
        {
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        double dt = SimConstants.TickSeconds;
        var car = ctx.Train.Vehicles[Attached];
        if (Heat.Length != grid.Count)
            Lay(grid, car);
        if (Phase == SpinePhase.Dormant)
            Enter(ctx, SpinePhase.Telegraph); // smoke through the boards, and the crackle
        var spraying = Spraying(ctx, grid, t);
        _sprayed.Clear();
        foreach (var (_, ext, cell) in spraying)
        {
            ext.Charge = Math.Max(0, ext.Charge - dt / t.ChargeSeconds);
            _sprayed.Add(cell);
        }
        Spread(grid, car, t, spraying, dt);
        // What it's burnt is gone for the night: the car's char (App. F.1, "burnt cells char the textures").
        if (car.Char.Length != grid.Count)
            car.Char = new byte[grid.Count];
        int top = (1 << Bits) - 1;
        for (int i = 0; i < grid.Count; i++)
            car.Char[i] = (byte)Math.Clamp((int)Math.Ceiling((1 - _fuel[i]) * top - 1e-9), car.Char[i], top);
        double sum = 0, hottest = 0, z = 0;
        for (int i = 0; i < grid.Count; i++)
        {
            sum += Heat[i];
            hottest = Math.Max(hottest, Heat[i]);
            z += Heat[i] * grid.Centre[i].Z;
        }
        if (hottest <= 0)
        {
            Extra = 0;
            Out(ctx);
            return;
        }
        Extra = sum / grid.Count;
        Local = Local with { Z = z / sum };
        if (Phase == SpinePhase.Telegraph && hottest >= t.BurnFrom && Enter(ctx, SpinePhase.Commit))
            Enter(ctx, SpinePhase.Punish); // alight
        if (Phase != SpinePhase.Punish)
            return;
        if (!_exploded && car.Cargo == CargoKind.Ammunition && car.Load > 0.01 && car.CargoIntegrity > 0.02 && Extra >= t.ExplodeAt - 1e-9)
        {
            Explode(ctx, car, t);
            return;
        }
        car.CargoIntegrity = Math.Max(0, car.CargoIntegrity - t.CargoPerSecond * Extra * dt);
        car.Integrity = Math.Max(0, car.Integrity - t.IntegrityPerSecond * Extra * dt);
        Burn(ctx, grid, t, dt);
        Jump(ctx, grid, car, t, dt);
    }

    /// <summary>Lays the fire out on its car's cells: what's charred already has less to burn; it starts on the floor where it was set.</summary>
    void Lay(FireGrid grid, Vehicle car)
    {
        Heat = new double[grid.Count];
        _fuel = new double[grid.Count];
        _damp = new double[grid.Count];
        int top = (1 << Bits) - 1;
        for (int i = 0; i < grid.Count; i++)
            _fuel[i] = car.Char.Length == grid.Count ? 1 - car.Char[i] / (double)top : 1;
        Heat[grid.FloorAt(Local)] = _startHeat;
        if (_span >= 0)
            for (int i = 0; i < grid.Count; i++)
                if (Math.Abs(grid.Centre[i].Z - Local.Z) <= _span)
                    Heat[i] = _startHeat;
    }

    /// <summary>
    /// A tick of the cells: each grows (faster the hotter it is and the worse the cargo, upward faster), uses up what it has
    /// to burn and dies down once that's gone; embers below <see cref="CarFireTuning.OutBelow"/> don't grow on their own, and
    /// go out unless a cell round them is heating them; each well alight heats the cells round it; the extinguisher cools what it's
    /// aimed at and wets it. All from the cells as they were at the start of the tick, in cell order, so it's the same
    /// wherever it's run.
    /// </summary>
    void Spread(FireGrid grid, Vehicle car, CarFireTuning t, List<(int Player, Physics.Body Extinguisher, int Cell)> spraying, double dt)
    {
        bool cargoLeft = car.CargoIntegrity > 0.02;
        double fuel = !cargoLeft ? 1 : car.Cargo is CargoKind.Ammunition ? t.PowderGrowth : car.Cargo is CargoKind.Chemicals ? t.ChemicalGrowth
            : Cargoes.Fuel(car.Cargo) ? t.FuelGrowth : 1;
        var was = (double[])Heat.Clone();
        var next = new double[grid.Count];
        var caught = new bool[grid.Count];
        for (int i = 0; i < grid.Count; i++)
        {
            double h = was[i];
            _damp[i] = Math.Max(0, _damp[i] - dt);
            if (h >= t.OutBelow)
            {
                // The floor's the cargo's (it burns what's stacked on it); the walls and roof are the car's own boards.
                double f = grid.Face[i] == FireFace.Floor ? fuel : 1;
                double grow = _fuel[i] > 0 ? (t.GrowPerSecond + t.GrowWithSize * h) * f : -t.BurnOutPerSecond;
                next[i] += grow * dt;
                _fuel[i] = Math.Max(0, _fuel[i] - t.CharPerSecond * h * dt);
            }
            if (h < t.CatchFrom)
                continue;
            foreach (int j in grid.Next[i])
            {
                if (_damp[j] > 0 || _fuel[j] <= 0)
                    continue;
                bool up = grid.Centre[j].Y > grid.Centre[i].Y + 0.1;
                caught[j] = true;
                next[j] += t.CatchPerSecond * h * (up ? t.Climb : 1) * (grid.Face[j] == FireFace.Floor ? fuel : 1) * dt;
            }
        }
        foreach (var (_, _, cell) in spraying)
        {
            if (cell < 0)
                continue;
            next[cell] -= t.SprayPerSecond * dt;
            _damp[cell] = t.DampSeconds;
            foreach (int j in grid.Next[cell])
                next[j] -= t.SprayPerSecond * t.SprayShare * dt;
        }
        // What's knocked down to embers with nothing round it alight goes out: it doesn't creep back from a spark.
        for (int i = 0; i < grid.Count; i++)
        {
            double h = Math.Clamp(was[i] + next[i], 0, 1);
            Heat[i] = h < t.OutBelow && !caught[i] ? 0 : h;
        }
    }

    /// <summary>
    /// It jumps the coupling from a cell against an end wall that's been well alight long enough (chemicals, coal and timber
    /// sooner, B.9: "fire cascades escalate faster"), into the next car's near end; once from each end.
    /// </summary>
    void Jump(EnemyContext ctx, FireGrid grid, Vehicle car, CarFireTuning t, double dt)
    {
        var train = ctx.Train;
        double rate = car.Cargo == CargoKind.Chemicals ? t.ChemicalSpread : Cargoes.Fuel(car.Cargo) ? t.FuelSpread : 1;
        for (int side = 0; side < 2; side++)
        {
            double hottest = 0;
            for (int i = 0; i < grid.Count; i++)
                if (grid.End(i) is (0, var s) && (s < 0 ? 0 : 1) == side)
                    hottest = Math.Max(hottest, Heat[i]);
            _blaze[side] = hottest >= t.SpreadFrom ? _blaze[side] + dt * rate : 0;
            if (_spread[side] || _blaze[side] < t.SpreadSeconds)
                continue;
            int into = side == 0 ? train.VehicleAhead(Attached) : train.VehicleBehind(Attached);
            if (into <= 0 || train.Vehicles[into].Kind != VehicleKind.Cargo || ctx.World.ActiveEnemies.Count(e => !e.Gone && e.Kind == Kind) >= t.MaxActive
                || ctx.World.ActiveEnemies.Any(e => !e.Gone && e.Kind == Kind && e.Attached == into))
                continue;
            _spread[side] = true;
            double half = train.Frames[into].Shape.HalfLength;
            ctx.World.AddEnemy(i => In(i, train, into, side == 0 ? half : -half, t));
        }
        Extra2 = Math.Max(_blaze[0], _blaze[1]);
    }

    /// <summary>
    /// The fire's the train's own danger (like a fall): it can kill. It burns whoever's by a burning cell, by how hot the cells
    /// round them are and how near, from their feet to their head (the one under their feet, the walls beside them, the roof
    /// over them less): out of the burning end of
    /// the car you're clear of it. It burns by the second (the director's decision of 6 Oct 2026, note 263): a brush at
    /// <see cref="CarFireTuning.BurnBrushShare"/> of the full rate, rising to it over <see cref="CarFireTuning.BurnRampSeconds"/>
    /// in it, so a brush hurts and standing in it kills. Whole points are dealt as they add up.
    /// </summary>
    void Burn(EnemyContext ctx, FireGrid grid, CarFireTuning t, double dt)
    {
        var inIt = new HashSet<int>();
        foreach (var (p, _) in Inside(ctx, 0))
        {
            // From feet to head, to the cell's patch of board (half a cell round its centre).
            var feet = p.State.Position;
            double heat = 0;
            for (int i = 0; i < grid.Count; i++)
            {
                if (Heat[i] <= 0)
                    continue;
                var c = grid.Centre[i];
                var body = feet with { Y = Math.Clamp(c.Y, feet.Y, feet.Y + t.BodyHeight) };
                double d = Math.Max(0, (c - body).Length - t.CellSize * 0.5);
                if (d < t.BurnReach)
                    heat = Math.Max(heat, Heat[i] * (1 - d / t.BurnReach) * (grid.Face[i] == FireFace.Ceiling ? t.CeilingBurnShare : 1));
            }
            if (heat <= 0)
                continue;
            inIt.Add(p.Id);
            var (was, dose) = _burning.GetValueOrDefault(p.Id);
            double inFor = was + dt;
            double share = t.BurnBrushShare + (1 - t.BurnBrushShare) * Math.Clamp(inFor / Math.Max(1e-6, t.BurnRampSeconds), 0, 1);
            dose += t.BurnPerSecond * heat * share * dt;
            int whole = (int)dose;
            if (whole > 0)
                ctx.Harm(p.Id, whole, DeathCause.Burned);
            _burning[p.Id] = (inFor, dose - whole);
        }
        foreach (int gone in _burning.Keys.Where(id => !inIt.Contains(id)).ToList())
            _burning.Remove(gone);
    }

    /// <summary>
    /// A powder car with enough of it alight goes up (GDD §19 "explodes", App. B.9 "every fire is worse"; note 182). Its cargo
    /// and the car are gone; everyone within <see cref="CarFireTuning.ExplodeKillRadius"/> of the fire takes the full blast,
    /// out to <see cref="CarFireTuning.ExplodeRadius"/> less with distance (walls or not: it's a powder car); the cars either
    /// side catch whatever's already burning. Every client sees and hears it as a blast where it was.
    /// </summary>
    void Explode(EnemyContext ctx, Vehicle car, CarFireTuning t)
    {
        _exploded = true;
        var train = ctx.Train;
        var at = train.Frames[Attached].ToWorld(Local + Double3.Up * 1.0);
        car.CargoIntegrity = 0;
        car.Integrity = 0;
        foreach (var c in ctx.LivingCrew())
        {
            double d = (c.World + Double3.Up * 1.0 - at).Length;
            if (d > t.ExplodeRadius)
                continue;
            double share = d <= t.ExplodeKillRadius ? 1 : 1 - (d - t.ExplodeKillRadius) / Math.Max(1e-6, t.ExplodeRadius - t.ExplodeKillRadius);
            int amount = (int)Math.Round(t.ExplodeDamage * share);
            if (amount > 0)
                ctx.Harm(c.Player.Id, amount, DeathCause.Exploded);
        }
        foreach (int next in new[] { train.VehicleBehind(Attached), train.VehicleAhead(Attached) })
            if (next > 0 && train.Vehicles[next].Kind == VehicleKind.Cargo && train.Dynamics.Consist.IndexOf(next) >= 0
                && !ctx.World.ActiveEnemies.Any(e => !e.Gone && e.Kind == Kind && e.Attached == next))
                ctx.World.AddEnemy(i => In(i, train, next, Local.Z, t));
        ctx.World.Blast(at);
        // What was blown out burns down from what's left: the cells cooled to where it no longer spreads.
        for (int i = 0; i < Heat.Length; i++)
            Heat[i] = Math.Min(Heat[i], t.CatchFrom * 0.9);
    }
}
