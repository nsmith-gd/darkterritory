using Ballast;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Run;

/// <summary>
/// The stops' loot (level-design P2, P12, P14). A stop's layout says where things are; this half of the run fills it
/// from the economy and puts it in the world: when the train first stops at a stop, its yard's crate stacks come out as
/// cargo crates and its strongroom as a heavy crate, and its village's finds as loot to carry aboard; its cranes'
/// castings are there from the start. A find put down inside any car and left still is stowed: it pays on delivery.
/// </summary>
public sealed partial class Run
{
    LootTuning? _loot;
    RailLine? _lootLine;
    readonly List<(RouteFeature Feature, int Index, StopLayout Stop, IReadOnlyList<LootFind> Finds)> _stopLoot = [];
    bool[] _stocked = [];
    readonly Dictionary<int, double> _lootSettling = new();
    readonly List<LootFind> _stowed = [];

    /// <summary>Scrip for the finds stowed aboard so far (paid with the cargo on delivery).</summary>
    public double Scavenged { get; private set; }
    /// <summary>The finds stowed so far (host only), in order.</summary>
    public IReadOnlyList<LootFind> Stowed => _stowed;
    /// <summary>The route's stops with layouts (facilities' yards and village halts), in order along the line.</summary>
    public IReadOnlyList<RouteFeature> Stops => [.. _stopLoot.Select(s => s.Feature)];
    public bool Stocked(int stop) => stop >= 0 && stop < _stocked.Length && _stocked[stop];

    /// <summary>A find's body's owner: the stop, and the container in its layout.</summary>
    public static int LootOwner(int stop, int container) => stop << 12 | container;

    /// <summary>
    /// Fills the stops from the economy and builds their yards' cranes. Host and clients both do this (after
    /// <see cref="EnableSites"/>), so a client can name a find from its body.
    /// </summary>
    public void EnableLoot(LootTuning t, RailLine line, FacilityTuning? facilities)
    {
        _loot = t;
        _lootLine = line;
        _stopLoot.Clear();
        double perCar = Tuning.Economy.PerCar.GetValueOrDefault(StopLoot.TierKey(_route.Tier), 700);
        for (int i = 0; i < _route.Features.Count; i++)
            if (_route.Features[i].Stop is { } stop)
                _stopLoot.Add((_route.Features[i], i, stop, StopLoot.Village(t, stop, _route.Seed, i, perCar)));
        _stocked = new bool[_stopLoot.Count];
        if (facilities is not null)
            BuildYardCranes(facilities.Crane);
    }

    /// <summary>Where a point in a stop's rail frame is in the world: along its (straight, level) zone, out to the side.</summary>
    public static Double3 StopWorld(RailLine line, RouteFeature stop, Pt p, double up = 0)
    {
        var t = line.Sample(stop.Start + p.S);
        var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
        return t.Position + right * p.D + Double3.Up * up;
    }

    /// <summary>
    /// A gantry over every craned loading face but the facility's own (level-design P5, P18): its runway's length, a
    /// casting for each bay it reaches, stacked towards the sheds they're in.
    /// </summary>
    void BuildYardCranes(CraneTuning ct)
    {
        foreach (var site in _sites)
        {
            if (site?.Feature.Stop is not { } stop)
                continue;
            var cranes = new List<Crane>();
            foreach (var track in stop.Tracks)
            {
                if (track.Crane is not { Bays: > 0 } rw || track.Primary && site.Crane is not null)
                    continue;
                var bays = stop.Containers.Where(c => c.Kind == ContainerKind.CraneBay && c.Track == track.Index).ToList();
                double lateral = bays.Average(c => c.At.D) - track.FaceStart.D;
                int toward = lateral == 0 ? track.Side : Math.Sign(lateral);
                double mid = (rw.From + rw.To) / 2, d = track.FaceStart.D;
                var tuning = ct with { Along = 0, Length = rw.Length, Castings = bays.Count, Spacing = Math.Min(ct.Spacing, rw.Length / (bays.Count + 1)) };
                var feature = site.Feature;
                var line = _lootLine!;
                cranes.Add(new Crane(tuning, (along, across, up) => StopWorld(line, feature, new Pt(mid + along, d + toward * across), up)));
            }
            site.YardCranes = cranes;
        }
    }

    /// <summary>What a find is and what it pays, from its body (null for anything else).</summary>
    public LootFind? FindOf(Physics.Body b)
    {
        if (b.Kind != Physics.BodyKind.Loot)
            return null;
        int stop = b.Owner >> 12, container = b.Owner & 0xFFF;
        if (stop < 0 || stop >= _stopLoot.Count)
            return null;
        foreach (var f in _stopLoot[stop].Finds)
            if (f.Container == container)
                return f;
        return null;
    }

    /// <summary>A find's name for the HUD.</summary>
    public string? FindName(Physics.Body b) => FindOf(b) is { } f && _loot is { } t ? t.Name(f.Item) : null;

    void StepLoot(World world, double dt)
    {
        if (_loot is not { } t || _lootLine is not { } line)
            return;
        var train = world.Train;
        var engine = EngineRake(train);
        if (engine.Speed < Tuning.StopBelowSpeed)
            for (int k = 0; k < _stopLoot.Count; k++)
            {
                var f = _stopLoot[k].Feature;
                if (!_stocked[k] && engine.Distance >= f.Start && engine.Distance <= f.End + 100)
                    Stock(world.Bodies, line, t, k);
            }

        // A find put down inside a car, and lying still there, is stowed. (Only a stop's finds: other hand loot, the salvage
        // the Gaunt and the Followers go for, stays a body.)
        foreach (var b in world.Bodies.All.Where(b => b.Kind == Physics.BodyKind.Loot).ToList())
        {
            if (FindOf(b) is not { } find)
                continue;
            bool aboard = b.Carrier < 0 && b.Parent > 0 && b.Parent < train.Vehicles.Count
                && train.Frames[b.Parent].Shape.Interior is { } room && room.Contains(b.Pbd.Particles[0].Position);
            double still = aboard ? _lootSettling.GetValueOrDefault(b.Id) + dt : 0;
            if (still < t.SettleSeconds)
            {
                _lootSettling[b.Id] = still;
                continue;
            }
            Scavenged += find.Value;
            _stowed.Add(find);
            world.Bodies.Remove(b);
            _lootSettling.Remove(b.Id);
        }
    }

    /// <summary>
    /// Puts stop <paramref name="stop"/>'s loot out into <paramref name="bodies"/> now, as the host does when the train
    /// first stops there (for tools: `dt screenshot --site`). Does nothing before <see cref="EnableLoot"/>.
    /// </summary>
    public void Stock(Physics.Bodies bodies, int stop)
    {
        if (_loot is { } t && _lootLine is { } line && stop >= 0 && stop < _stopLoot.Count)
            Stock(bodies, line, t, stop);
    }

    /// <summary>Where crate <paramref name="i"/> of a stack of <paramref name="n"/> stands, in its stop's rail frame: in a row across the yard.</summary>
    static Pt CrateAt(StopContainer c, int i, int n) => c.At + new Pt(0, (i - (n - 1) * 0.5) * 1.1);

    /// <summary>
    /// Every yard crate stack's count, chalked on a board at the head of its row (level-design P12: "crate counts shown"),
    /// for the art: where the board stands, which way the row runs from it (a unit vector, level), and the number. The
    /// count is what the economy put there, so a crate more than it is something else (GDD v1.3 §21, the Mimic).
    /// </summary>
    public IEnumerable<(Double3 At, Double3 Along, int Count)> CrateCounts()
    {
        if (_loot is not { } t || _lootLine is not { } line)
            yield break;
        foreach (var (f, index, stop, _) in _stopLoot)
            foreach (var c in stop.Containers)
                if (c.Kind == ContainerKind.CrateStack && StopLoot.CratesIn(t, stop, _route.Seed, index, c) is var n and > 0)
                {
                    var head = StopWorld(line, f, CrateAt(c, -1, n));
                    var along = StopWorld(line, f, CrateAt(c, 0, n)) - head;
                    yield return (head, (along with { Y = 0 }).Normalized, n);
                }
    }

    /// <summary>
    /// Where a Mimic can lie at the stop the engine is at (GDD v1.3 App. B.6), once its loot is out: just past the last
    /// crate of each of its yard's crate stacks, one more than the count chalked there; with none, beside the facility's own
    /// crates. With the cargo those crates hold, so it's a crate like the rest. Empty anywhere else.
    /// </summary>
    public List<(Double3 At, double LineHint, CargoKind Cargo)> MimicSlots(double engine)
    {
        var slots = new List<(Double3, double, CargoKind)>();
        if (_loot is { } t && _lootLine is { } line)
            for (int k = 0; k < _stopLoot.Count; k++)
            {
                var (f, index, stop, _) = _stopLoot[k];
                if (!_stocked[k] || engine < f.Start - 100 || engine > f.End + 100)
                    continue;
                foreach (var c in stop.Containers)
                    if (c.Kind == ContainerKind.CrateStack && StopLoot.CratesIn(t, stop, _route.Seed, index, c) is var n and > 0)
                        slots.Add((StopWorld(line, f, CrateAt(c, n, n)), f.Start + c.At.S, CargoKind.None));
            }
        if (slots.Count == 0 && CurrentSite is { Stocked: true, CrateStack.Length: > 0 } site)
        {
            var stack = site.CrateStack;
            var step = stack.Length > 1 ? (stack[^1] - stack[0]) with { Y = 0 } : new Double3(1, 0, 0);
            var cargo = FacilityFeature?.Facility is { } kind && _facilityTuning is { } ft ? ft.CargoOf(kind) : CargoKind.None;
            slots.Add((stack[^1] + step.Normalized * 1.1, site.CrateLineHint, cargo));
        }
        return slots;
    }

    /// <summary>A stop's loot comes out: the yard's crate stacks and strongroom, the village's finds.</summary>
    void Stock(Physics.Bodies bodies, RailLine line, LootTuning t, int k)
    {
        _stocked[k] = true;
        var (f, index, stop, finds) = _stopLoot[k];
        double heavy = _facilityTuning?.Crates.Heavy.Radius ?? 0.55;
        foreach (var c in stop.Containers)
        {
            double hint = f.Start + c.At.S;
            switch (c.Kind)
            {
                case ContainerKind.CrateStack:
                    int n = StopLoot.CratesIn(t, stop, _route.Seed, index, c);
                    for (int i = 0; i < n; i++)
                        bodies.SpawnCargo(StopWorld(line, f, CrateAt(c, i, n)), hint);
                    break;
                case ContainerKind.Strongroom:
                    for (int i = 0; i < t.Yard.Strongroom.Heavy; i++)
                        bodies.SpawnCargo(StopWorld(line, f, c.At + new Pt(i * 1.6, 0)), hint, heavy);
                    break;
                case ContainerKind.CraneBay:
                    break;
                default:
                    // A find in a shut house is put out on its step (T114: the houses are walls now, with no way in).
                    var put = c.Building >= 0 && c.Building < stop.Buildings.Count && StopWalls.Walled(stop, c.Building)
                        ? StopWalls.Doorstep(stop.Buildings[c.Building], c.Index) : c.At;
                    if (finds.Any(x => x.Container == c.Index))
                        bodies.SpawnLoot(StopWorld(line, f, put), f.Start + put.S, LootOwner(k, c.Index), t.Radius);
                    break;
            }
        }
    }
}
