using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Run;

/// <summary>
/// GDD §18's switchyard and wreck yard (WP15b, ARCHITECTURE §8 note 187). The switchyard's cars stand on its sidings when
/// the night begins, rakes of their own with their handbrakes on, for the crew to couple up to and bring away: a siding at a
/// time, the switch thrown for each, every car of them ahead of the engine (the sidings' points all face up the line, so
/// there's no getting round them). The wreck yard is the train that came off at the end of its line: heaps of cars on
/// their sides with salvage in them that nobody finds without a lamp on it, and that shift when too much has been pulled out.
/// </summary>
public sealed partial class Run
{
    // Host: each salvage piece still lying where a heap gave it up, by body id, and the site and heap it came out of; last
    // tick's shifts, where and how wide, and who pulled the piece that set each off.
    readonly SortedDictionary<int, (int Site, int Heap)> _pieces = new();
    readonly List<(Double3 At, double Radius, int By)> _shifts = new();

    /// <summary>A roll in an inclusive range, from the route's seed, the same on every machine.</summary>
    int Roll(int[] range, ulong salt, ulong at) =>
        range[0] + (int)((_route.Seed * salt + at) % (ulong)(Math.Max(0, range[^1] - range[0]) + 1));

    /// <summary>How much salvage is in each of a wreck yard's heaps (facilities.json wreck: heaps, salvage).</summary>
    int[] Salvage(WreckYardTuning w, int facility)
    {
        int heaps = Math.Min(Roll(w.Heaps, 41, (ulong)facility * 19), w.Layout.Length);
        return [.. Enumerable.Range(0, heaps).Select(h => Roll(w.Salvage, 53, (ulong)facility * 31 + (ulong)h * 7))];
    }

    /// <summary>A facility's yard tracks (level-design P5): the branches whose points are in its zone, its own first.</summary>
    public IReadOnlyList<int> YardTracks(int facility) =>
        facility < 0 || facility >= _yards.Length ? [] : [.. _yards[facility].OrderBy(b => b == _spurs[facility] ? 0 : 1).ThenBy(b => b)];

    /// <summary>
    /// Stands each switchyard's cars on its sidings (GDD §18 "cars scattered across six sidings"; note 187): a rake of
    /// <c>rakes.cars</c> on every track of the yard but its own (on its own, if that's all it has), at the buffer stop, each
    /// car part-loaded with one of <c>rakes.cargoes</c> ("mixed"). Never more in all than the shortest of those sidings takes
    /// with the engine and <c>rakes.spare</c> of its own cars: the crew go in for each siding's with every car they've already
    /// picked up still ahead of the engine. Host and clients both do this, from the route, before the night begins.
    /// </summary>
    public void StandCars(TrainOnLine train, double pointsLength = 12)
    {
        if (_facilityTuning is not { } t)
            return;
        var g = train.Dynamics.Tuning.Geometry;
        var r = t.Rakes;
        foreach (var site in _sites)
        {
            if (site is null || !site.Has(ModuleKind.Rakes) || site.Spur < 0)
                continue;
            int i = site.Index;
            var tracks = YardTracks(i).Skip(1).ToList();
            if (tracks.Count == 0)
                tracks = [site.Spur];
            int most = tracks.Min(b => SpurDrill.Capacity(g, train.Line.Branches[b], pointsLength)) - r.Spare;
            int stood = 0;
            for (int k = 0; k < tracks.Count; k++)
            {
                int n = Math.Min(Roll(r.Cars, 61, (ulong)i * 37 + (ulong)k * 11), most - stood);
                if (n <= 0)
                    break;
                var cars = Enumerable.Range(0, n).Select(c =>
                {
                    ulong at = (ulong)i * 101 + (ulong)k * 13 + (ulong)c * 5;
                    double share = (_route.Seed * 67 + at) % 11 / 10.0;
                    double load = Math.Round(r.Load[0] + (r.Load[^1] - r.Load[0]) * share, 2);
                    string name = r.Cargoes.Length == 0 ? "goods" : r.Cargoes[(int)((_route.Seed * 71 + at) % (ulong)r.Cargoes.Length)];
                    return (load, Enum.TryParse<CargoKind>(name, ignoreCase: true, out var cargo) ? cargo : CargoKind.Goods);
                }).ToList();
                var branch = train.Line.Branches[tracks[k]];
                train.Stand(branch.Index, branch.End - r.Back, cars);
                stood += n;
            }
        }
        // Then every yard's blocked sidings (level-design D.2; note 294): its layout's derelicts at the buffer stop. After
        // all the switchyards' cars, so theirs keep their ids.
        var d = t.Derelicts;
        foreach (var site in _sites)
        {
            if (site?.Feature.Stop is not { } stop || site.Spur < 0)
                continue;
            var branches = YardTracks(site.Index).Select(b => train.Line.Branches[b]).ToList();
            foreach (var track in stop.Tracks.Where(tr => tr.Blocked))
            {
                double toe = site.Feature.Start + track.Toe;
                if (branches.FirstOrDefault(b => Math.Abs(b.Toe - toe) < 0.01 && b.Definition.Side == track.Side) is { } branch)
                    train.StandDerelicts(branch.Index, branch.End - d.Back, track.Derelicts, d.Integrity, d.Pays);
            }
        }
    }

    /// <summary>A switchyard's cars still standing on one of its sidings, as the night found them (note 187); not derelicts.</summary>
    public static int StandingOn(TrainOnLine train, int branch) =>
        train.Rakes.Where(r => r.Path == branch && train.Standing(r)).Sum(r => r.Consist.Vehicles.Count(v => !v.Derelict));

    /// <summary>Derelict cars still on a siding (level-design D.2; note 294): what has to come out before it's any use.</summary>
    public static int DerelictsOn(TrainOnLine train, int branch) =>
        train.Rakes.Where(r => r.Path == branch && !r.Consist.HasEngine).Sum(r => r.Consist.Vehicles.Count(v => v.Derelict));

    /// <summary>
    /// Lit (GDD §18 "unlit": the crew's lamps are all the light there is): a hand lamp within <c>wreck.lampReach</c>, carried or
    /// set down, or in the engine's headlamp, ahead of it within the beam.
    /// </summary>
    public bool Lit(World world, Double3 at)
    {
        if (_facilityTuning is not { } t)
            return false;
        var w = t.Wreck;
        var train = world.Train;
        if (world.LampShining && train.Frames.Count > 0)
        {
            var engine = train.Frames[0];
            var local = engine.ToLocal(at);
            double ahead = -local.Z - engine.Shape.HalfLength;
            // As far as this train's headlamp reaches (note 506: the lamp brightness upgrade lights more of the wreck).
            if (ahead > 0 && ahead <= w.BeamLength * train.Dynamics.Tuning.HeadlampReach && Math.Abs(local.X) <= 2 + ahead * w.BeamSpread)
                return true;
        }
        return world.Bodies.All.Any(b => b.Kind == Physics.BodyKind.Lamp && Flat(Physics.Bodies.WorldCentre(b, train) - at) <= w.LampReach);
    }

    /// <summary>Who pulled the piece that set off the heap that shifted last: a wreckage death's contributing action (App. C.9).</summary>
    public int WreckBy { get; private set; } = -1;

    /// <summary>
    /// The wreck yard (GDD §18 "pull cargo off derailed trains. Unstable, unlit"): a heap's salvage comes out onto the
    /// ground beside it the first time it's lit; every piece pulled out of it (picked up) unsettles it; unsettled, it groans for
    /// <c>wreck.warnSeconds</c> (the tell) and then shifts, on whoever's within <c>wreck.crushRadius</c>, and settles a while.
    /// </summary>
    void Wreckage(World world, Site site, WreckYardTuning w, double dt)
    {
        foreach (var heap in site.Heaps)
        {
            if (!heap.Found && heap.Salvage > 0 && !Over && Lit(world, heap.Centre))
            {
                heap.Found = true;
                for (int k = 0; k < heap.Salvage; k++)
                {
                    var at = PieceAt(site, heap, k, heap.Salvage, w);
                    var b = world.Bodies.SpawnCargo(at, site.MainDistance, cargo: CargoKind.Salvage);
                    _pieces[b.Id] = (site.Index, heap.Index);
                }
                heap.Salvage = 0;
            }
            if (heap.Groan > 0)
            {
                heap.Groan = Math.Max(0, heap.Groan - dt);
                if (heap.Groan <= 0)
                {
                    heap.Shifts++;
                    heap.Stability = w.Settle;
                    WreckBy = heap.By;
                    _shifts.Add((heap.Centre, w.CrushRadius, heap.By));
                }
            }
        }
        // A piece picked up is a piece pulled out from under the rest.
        foreach (var (id, from) in _pieces.ToList())
        {
            if (from.Site != site.Index)
                continue;
            var body = world.Bodies.All.FirstOrDefault(b => b.Id == id);
            if (body is null)
            {
                _pieces.Remove(id);
                continue;
            }
            if (body.Carrier < 0)
                continue;
            _pieces.Remove(id);
            var heap = site.Heaps[from.Heap];
            heap.Stability -= w.StrainPerPiece;
            heap.By = body.Carrier;
            if (heap.Stability <= 1e-9 && heap.Groan <= 0 && heap.Shifts < w.Shifts)
                heap.Groan = w.WarnSeconds;
        }
    }

    /// <summary>
    /// Where a heap's <paramref name="k"/>th piece of <paramref name="of"/> comes out: on the ground on its near side, towards
    /// the track, side by side; never across the track from it, nor on the rails, but out on the site's side, where it's
    /// carried from.
    /// </summary>
    public static Double3 PieceAt(Site site, WreckHeap heap, int k, int of, WreckYardTuning w)
    {
        var toward = (site.Track.Sample(Math.Clamp(Nearest(site.Track, heap.Centre), 0, site.Track.Length)).Position - heap.Centre) with { Y = 0 };
        var into = toward.Length > 1e-6 ? toward.Normalized : new Double3(1, 0, 0);
        var across = Double3.Cross(into, Double3.Up).Normalized;
        var at = heap.Centre + into * (w.CrushRadius - 1.2) + across * ((k - (of - 1) / 2.0) * 1.1);
        var s = site.Track.Sample(Math.Clamp(Nearest(site.Track, at), 0, site.Track.Length));
        var right = Double3.Cross(s.Tangent, Double3.Up).Normalized;
        double outward = Double3.Dot(at - s.Position, right) * site.Side;
        return outward < 2.5 ? at + right * (site.Side * (2.5 - outward)) : at;
    }

    static double Nearest(Rail.RailLine track, Double3 at)
    {
        double hint = track.Length / 2;
        return track.Nearest(at, ref hint).Distance;
    }

    /// <summary>A heap groaning within reach of a point (the tell, for the HUD and the bots), or null.</summary>
    public WreckHeap? Groaning(Double3 at, double margin = 0) =>
        _facilityTuning is not { } t ? null
        : _sites.Where(s => s is not null).SelectMany(s => s!.Heaps).FirstOrDefault(h => h.Groan > 0 && Flat(at - h.Centre) <= t.Wreck.CrushRadius + margin);

    /// <summary>A wreck heap beside a player on foot, for the HUD.</summary>
    public WreckHeap? HeapNear(in PlayerState s, TrainOnLine train, double reach = 6)
    {
        if (Over || !s.Alive || s.Parent != PlayerState.World)
            return null;
        var at = PlayerMotor.WorldPosition(s, train);
        return _sites.Where(x => x is not null).SelectMany(x => x!.Heaps).Where(h => Flat(at - h.Centre) <= reach).MinBy(h => Flat(at - h.Centre));
    }
}
