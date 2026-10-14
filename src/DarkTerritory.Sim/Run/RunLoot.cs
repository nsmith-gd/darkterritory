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
    readonly List<(RouteFeature Feature, int Index, StopLayout Stop, IReadOnlyList<LootFind> Finds, IReadOnlyList<int> Kits,
        IReadOnlyList<(int Container, Physics.ToyNoise Noise)> Toys)> _stopLoot = [];
    bool[] _stocked = [];
    readonly Dictionary<int, double> _lootSettling = new();
    readonly List<LootFind> _stowed = [];
    /// <summary>
    /// Note 659 (the director, 13 Oct: "I tried clicking to let go of a piece of loot before it was in the car and it just
    /// disappeared"): the finds banked (in <see cref="Scavenged"/> and <see cref="Stowed"/>) and still lying aboard, by body:
    /// their owner and what they're worth. A find lying still in a car is banked but stays there, to be seen and picked up again;
    /// it used to be taken out of the world. Picked up, carried off or put in a locker, it's unbanked.
    /// </summary>
    readonly Dictionary<int, (int Owner, LootFind Find, int Finder)> _banked = new();
    /// <summary>
    /// Note 718 (the finder's line): who last had each find in their hands, by body (host only), so a find stowed is put down
    /// to whoever brought it aboard. Looked up, never iterated.
    /// </summary>
    readonly Dictionary<int, int> _carriedBy = new();
    /// <summary>Who brought each of <see cref="Stowed"/> aboard, in step with it (−1: nobody known, a resumed night's older save's).</summary>
    readonly List<int> _finders = [];
    /// <summary>Each stop's comet fragments (note 718), by the container each lies beside, in <see cref="_stopLoot"/>'s order.</summary>
    readonly List<IReadOnlyList<int>> _comets = [];
    /// <summary>A resumed night's banked finds' owners (<see cref="RunTakings.Banked"/>): their bodies bank without paying again.</summary>
    readonly List<int> _alreadyBanked = [];

    /// <summary>Scrip for the finds stowed aboard so far (paid with the cargo on delivery).</summary>
    public double Scavenged { get; private set; }
    /// <summary>The finds stowed so far (host only), in order.</summary>
    public IReadOnlyList<LootFind> Stowed => _stowed;
    /// <summary>Who brought each of <see cref="Stowed"/> aboard (host only; note 718), a player id or −1.</summary>
    public IReadOnlyList<int> Finders => _finders;
    /// <summary>The route's stops with layouts (facilities' yards and village halts), in order along the line.</summary>
    public IReadOnlyList<RouteFeature> Stops => [.. _stopLoot.Select(s => s.Feature)];
    public bool Stocked(int stop) => stop >= 0 && stop < _stocked.Length && _stocked[stop];

    /// <summary>A find's body's owner: the stop, and the container in its layout.</summary>
    public static int LootOwner(int stop, int container) => stop << 12 | container;

    /// <summary>The "stop" a creature's trophy's owner names (note 340): past any line's stops, its container the creature's kind.</summary>
    public const int TrophyStop = 0xFFF;

    /// <summary>A creature's trophy's owner (the Gannet's head, note 340): a find that's the kind's, not a stop's.</summary>
    public static int TrophyOwner(Enemies.EnemyKind kind) => LootOwner(TrophyStop, (int)kind);

    /// <summary>
    /// The "container" a stop's first comet fragment's owner names (note 718), the next the next: past any layout's own
    /// containers, so a client names a fragment from its body as it names a find.
    /// </summary>
    public const int CometContainer = 0xF00;

    /// <summary>Whether a find's body is a comet fragment (note 718), from its owner alone (replicated: the same on every machine).</summary>
    public static bool IsComet(Physics.Body b) =>
        b.Kind == Physics.BodyKind.Loot && b.Owner >> 12 != TrophyStop && (b.Owner & 0xFFF) >= CometContainer;

    /// <summary>
    /// Fills the stops from the economy and builds their yards' cranes. Host and clients both do this (after
    /// <see cref="EnableSites"/>), so a client can name a find from its body.
    /// </summary>
    public void EnableLoot(LootTuning t, RailLine line, FacilityTuning? facilities)
    {
        _loot = t;
        _lootLine = line;
        _stopLoot.Clear();
        // A find's worth is its kind's share of the tier's value per car, the deep tiers' more (loot.json findsByTier; note 718).
        double unit = Tuning.Economy.PerCar.GetValueOrDefault(StopLoot.TierKey(_route.Tier), 700) * t.FindsByTier.GetValueOrDefault(StopLoot.TierKey(_route.Tier), 1);
        for (int i = 0; i < _route.Features.Count; i++)
            if (_route.Features[i].Stop is { } stop)
                _stopLoot.Add((_route.Features[i], i, stop, StopLoot.Village(t, stop, _route.Seed, i, unit), StopLoot.Kits(t, stop, _route.Seed, i),
                    StopLoot.Toys(t, stop, _route.Seed, i)));
        _stocked = new bool[_stopLoot.Count];
        FindHidingSpots();
        // Note 718: the stops' comet fragments, each beside something lying out in the open, to be seen from the line.
        _comets.Clear();
        foreach (var (_, index, stop, _, _, _) in _stopLoot)
            _comets.Add(StopLoot.Comets(t, stop, _route.Seed, index, _route.Tier, c => CometAt(stop, c) is not null));
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
        // A creature's trophy (the director, 7 Oct 2026: "a dead Gannet can be worth a good deal"; note 340): loot.json
        // trophies, by the kind, paying its share of the tier's car-load. The same on every machine.
        if (stop == TrophyStop)
            return _loot is { } lt && _route is { } r && lt.Trophies.TryGetValue(Enemies.Director.Key((Enemies.EnemyKind)container), out var trophy)
                ? new LootFind(container, trophy.Item, trophy.PerCar * Tuning.Economy.PerCar.GetValueOrDefault(StopLoot.TierKey(r.Tier), 700))
                : null;
        if (stop < 0 || stop >= _stopLoot.Count)
            return null;
        // A comet fragment (note 718): worth its kind's, as every find is.
        if (container >= CometContainer)
            return _loot?.Comet is { } comet && stop < _comets.Count && container - CometContainer < _comets[stop].Count
                ? new LootFind(container, comet.Item, _loot.WorthOf(comet.Item, Tuning.Economy.PerCar.GetValueOrDefault(StopLoot.TierKey(_route.Tier), 700)))
                : null;
        foreach (var f in _stopLoot[stop].Finds)
            if (f.Container == container)
                return f;
        return null;
    }

    /// <summary>A find's name for the HUD.</summary>
    public string? FindName(Physics.Body b) => FindOf(b) is { } f && _loot is { } t ? t.Name(f.Item) : null;

    /// <summary>
    /// How much health a find gives back used (GDD App. F.1's rare healing loot, loot.json <c>healing</c>; note 272), 0 for
    /// anything that doesn't heal. From the run's seed, so a client (its HUD, a bot) knows as well as the host.
    /// </summary>
    public int HealOf(Physics.Body b) => _loot?.Healing is { } h && FindOf(b) is { } f ? h.Of(f.Item) : 0;

    /// <summary>The healing tuning (loot.json), or null when no find heals.</summary>
    public HealingTuning? Healing => _loot?.Healing;

    /// <summary>
    /// Note 649 (the director's `D1.5-loot-slots`: "a find in a crew locker stays there, on its shelf, and pays at the gates
    /// with the rest, like Lethal Company's ship. A locker lost with its car is lost"): what the finds on the lockers' shelves
    /// of <paramref name="attached"/> cars are worth.
    /// </summary>
    public double LockerFinds(World world, IReadOnlySet<int> attached) =>
        world.Bodies.All.Where(b => b.Kind == Physics.BodyKind.Loot && b.Stowed && attached.Contains(b.Parent)).Sum(b => FindOf(b)?.Value ?? 0);

    void Unbank(int body)
    {
        if (!_banked.Remove(body, out var banked))
            return;
        Scavenged -= banked.Find.Value;
        // The same find put down by the same hands, latest first (two of a kind are alike but for who brought them).
        int i = _stowed.Count - 1;
        while (i >= 0 && !(_stowed[i] == banked.Find && _finders[i] == banked.Finder))
            i--;
        if (i < 0)
            i = _stowed.IndexOf(banked.Find);
        if (i < 0)
            return;
        _stowed.RemoveAt(i);
        _finders.RemoveAt(i);
    }

    /// <summary>
    /// Where a comet fragment lies beside a stop's container (note 718), out in the open and seen from the line: a yard's own
    /// where it stands, back along the stop from it; one kept in a building (a shed, a house) on the ground outside it, off the
    /// side of it nearest the line. Either way with no building between it and the main line. Null when there's no such place.
    /// </summary>
    static Pt? CometAt(StopLayout stop, StopContainer c)
    {
        const double Clear = 1.2;
        // Nothing built across the way from the line to it, a few metres either side (not just glimpsed down a gap).
        bool Seen(Pt p)
        {
            for (double d = 0; d <= Math.Abs(p.D); d += 1)
                for (double s = -3; s <= 3; s += 1.5)
                    if (stop.Buildings.Any(o => Plan.Inside(new Pt(p.S + s, Math.Sign(p.D) * d), o, 0.4)))
                        return false;
            return true;
        }
        if (c.Building < 0 || c.Building >= stop.Buildings.Count)
            return c.At + new Pt(-1.1, 0) is var at && Seen(at) ? at : null;
        var b = stop.Buildings[c.Building];
        Pt[] sides = [StopWalls.InHouse(b, 0, b.Width / 2 + Clear), StopWalls.InHouse(b, 0, -b.Width / 2 - Clear),
            StopWalls.InHouse(b, b.Length / 2 + Clear, 0), StopWalls.InHouse(b, -b.Length / 2 - Clear, 0)];
        foreach (var p in sides.OrderBy(p => Math.Abs(p.D)).ThenBy(p => p.S))
            if (Seen(p))
                return p;
        return null;
    }

    /// <summary>Who last had a find in their hands (note 718), or −1 (host only).</summary>
    public int FinderOf(Physics.Body b) => _carriedBy.GetValueOrDefault(b.Id, -1);

    /// <summary>
    /// The comet fragments at stop <paramref name="stop"/> (note 718), by the container each lies beside, for tools and tests.
    /// </summary>
    public IReadOnlyList<int> CometsAt(int stop) => stop >= 0 && stop < _comets.Count ? _comets[stop] : [];

    /// <summary>
    /// Whether a comet fragment is aboard (note 718; GDD §19's "attracts everything"): lying in a car of the engine's rake, or
    /// in the hands of someone standing in one (a carried thing is in its carrier's frame). The director weighs and gates as
    /// it does for a car-load of the comet's freight (<see cref="Enemies.Director.Aboard"/>). From the bodies, so it's the same
    /// wherever it's asked.
    /// </summary>
    public static bool CometAboard(World world)
    {
        var train = world.Train;
        foreach (var b in world.Bodies.All)
            if (IsComet(b) && b.Parent >= 0 && b.Parent < train.Vehicles.Count && b.TakenBy < 0
                && train.Dynamics.Consist.Vehicles.Any(v => v.Id == b.Parent))
                return true;
        return false;
    }

    /// <summary>
    /// Note 718 (the director, 14 Oct 2026: team-killable creatures "can sometimes leave trophies"): the trophy the crew's kill of
    /// enemy <paramref name="enemy"/>, of <paramref name="kind"/>, leaves, or null when it leaves none: loot.json
    /// <c>trophies</c>' chance, rolled from the run's seed and the enemy's id, so a night replays the same.
    /// </summary>
    public TrophyTuning? TrophyFor(Enemies.EnemyKind kind, int enemy)
    {
        if (_loot is not { } t || !t.Trophies.TryGetValue(Enemies.Director.Key(kind), out var trophy))
            return null;
        if (trophy.Chance >= 1)
            return trophy;
        var rng = new Pcg32(StopSeed.Of(StopSeed.Of(_route.Seed, StopSeed.Trophy, (ulong)kind), (ulong)(uint)enemy));
        return rng.NextDouble() < trophy.Chance ? trophy : null;
    }

    void StepLoot(World world, double dt)
    {
        if (_loot is not { } t || _lootLine is not { } line)
            return;
        var train = world.Train;
        var engine = EngineRake(train);
        for (int k = 0; k < _stopLoot.Count; k++)
            if (!_stocked[k] && Due(_stopLoot[k].Feature, train, engine, t.StockAhead))
                Stock(world.Bodies, line, t, k);

        // A find put down inside a car, and lying still there, is stowed: banked, and left lying where it is (note 659). (Only a
        // stop's finds: other hand loot, the salvage the Gaunt and the Followers go for, stays a body.) One on a crew locker's
        // shelf stays there, and pays at the gates if its car's still on the train (note 649: LockerFinds).
        var present = new HashSet<int>();
        foreach (var b in world.Bodies.All.Where(b => b.Kind == Physics.BodyKind.Loot).ToList())
        {
            if (FindOf(b) is not { } find)
                continue;
            present.Add(b.Id);
            // Whose hands it's in (note 718): whoever puts it down in a car brought it aboard.
            if (b.Carrier >= 0)
                _carriedBy[b.Id] = b.Carrier;
            bool aboard = !b.Stowed && b.Carrier < 0 && b.TakenBy < 0 && b.Parent > 0 && b.Parent < train.Vehicles.Count
                && train.Frames[b.Parent].Shape.Interior is { } room && room.Contains(b.Pbd.Particles[0].Position);
            if (_banked.ContainsKey(b.Id))
            {
                if (!aboard)
                {
                    Unbank(b.Id);
                    b.Banked = false;
                }
                continue;
            }
            if (b.Stowed)
                continue;
            double still = aboard ? _lootSettling.GetValueOrDefault(b.Id) + dt : 0;
            if (still < t.SettleSeconds)
            {
                _lootSettling[b.Id] = still;
                continue;
            }
            _lootSettling.Remove(b.Id);
            int finder = FinderOf(b);
            _banked[b.Id] = (b.Owner, find, finder);
            b.Banked = true;
            // Put back where it lay by a resumed night (note 500): banked already, in the takings.
            if (_alreadyBanked.Remove(b.Owner))
                continue;
            Scavenged += find.Value;
            _stowed.Add(find);
            _finders.Add(finder);
        }
        // Gone from the world (carried off and away, a car taken with it): not aboard any more.
        foreach (int id in _banked.Keys.Where(id => !present.Contains(id)).ToList())
            Unbank(id);
    }

    /// <summary>
    /// Whether a stop's loot is due out. Note 352 (the director, 8 Oct 2026: "[I] watched the loot spawn in the yard after I'd
    /// already stopped the train"): with the engine on the main line within <paramref name="ahead"/> m of the stop's zone,
    /// whatever its speed, before anyone's near enough to be sent it (enemies.json interestRadius). With no look-ahead, once
    /// the train first stops there.
    /// </summary>
    bool Due(RouteFeature f, TrainOnLine train, TrainDynamics engine, double ahead) => ahead > 0
        ? train.OnMain && engine.Distance >= f.Start - ahead && engine.Distance <= f.End + ahead
        : engine.Speed < Tuning.StopBelowSpeed && engine.Distance >= f.Start && engine.Distance <= f.End + 100;

    /// <summary>
    /// Puts stop <paramref name="stop"/>'s loot out into <paramref name="bodies"/> now, as the host does as the train comes
    /// up to it (for tools: `dt screenshot --site`). Does nothing before <see cref="EnableLoot"/>.
    /// </summary>
    /// <param name="searched">Its open houses searched too (note 326), every find out where it was kept (and only that, if its
    /// loot's already out).</param>
    public void Stock(Physics.Bodies bodies, int stop, bool searched = false)
    {
        if (_loot is not { } t || _lootLine is not { } line || stop < 0 || stop >= _stopLoot.Count)
            return;
        if (!searched || !_stocked[stop])
            Stock(bodies, line, t, stop);
        if (searched)
            foreach (var spot in _spots.Where(x => x.Stop == stop).ToList())
                Reveal(bodies, stop, spot.Container);
    }

    /// <summary>The containers at stop <paramref name="stop"/> with a repair kit in them (E.12 question 4), for tools and tests.</summary>
    public IReadOnlyList<int> KitsAt(int stop) => stop >= 0 && stop < _stopLoot.Count ? _stopLoot[stop].Kits : [];

    /// <summary>The containers at stop <paramref name="stop"/> with a toy in them, and its noise (note 264), for tools and tests.</summary>
    public IReadOnlyList<(int Container, Physics.ToyNoise Noise)> ToysAt(int stop) => stop >= 0 && stop < _stopLoot.Count ? _stopLoot[stop].Toys : [];

    /// <summary>A stop's loot comes out: the yard's crate stacks and strongroom, the village's finds, and a repair kit now and then.</summary>
    void Stock(Physics.Bodies bodies, RailLine line, LootTuning t, int k)
    {
        _stocked[k] = true;
        var (f, index, stop, finds, kits, toys) = _stopLoot[k];
        double heavy = _facilityTuning?.Crates.Heavy.Radius ?? 0.55;
        foreach (var c in stop.Containers)
        {
            double hint = f.Start + c.At.S;
            // A repair kit (E.12 question 4), lying beside what the container holds: not the crew's until one of them picks it up.
            if (kits.Contains(c.Index))
            {
                var put = StopWalls.FindAt(stop, c);
                bodies.SpawnItem(StopWorld(line, f, put + new Pt(0.4, 0.3)), hint, Physics.BodyKind.RepairKit);
            }
            switch (c.Kind)
            {
                case ContainerKind.CrateStack:
                    int n = StopLoot.CratesIn(t, stop, _route.Seed, index, c);
                    for (int i = 0; i < n; i++)
                        bodies.SpawnCargo(StopWorld(line, f, c.At + new Pt(0, (i - (n - 1) * 0.5) * 1.1)), hint);
                    break;
                case ContainerKind.Strongroom:
                    for (int i = 0; i < t.Yard.Strongroom.Heavy; i++)
                        bodies.SpawnCargo(StopWorld(line, f, c.At + new Pt(i * 1.6, 0)), hint, heavy);
                    break;
                case ContainerKind.CraneBay:
                    break;
                default:
                    // In an open house's cupboard, cabinet, cellar or floor, it's there to be searched for (note 326).
                    if (Hidden(k, c))
                        break;
                    // A find in a shut house is put out on its step (T114: the houses are walls, with no way in); in an open
                    // one it's inside, where it'd be kept (note 326).
                    var put = StopWalls.FindAt(stop, c);
                    if (finds.Any(x => x.Container == c.Index))
                        bodies.SpawnLoot(StopWorld(line, f, put), f.Start + put.S, LootOwner(k, c.Index), t.Radius);
                    // A toy with it (note 264), beside the find: GDD §19's hand loot, for the Track Doll or the meter.
                    foreach (var toy in toys)
                        if (toy.Container == c.Index)
                            bodies.SpawnItem(StopWorld(line, f, put + new Pt(-0.4, 0.3)), f.Start + put.S, Physics.BodyKind.Toy).Noise = toy.Noise;
                    break;
            }
        }
        // Note 718: a comet fragment or two, lying out in the open beside what they're by (a yard's crate stack or strongroom,
        // back along the stop from it, clear of its crates; outside a shed or a house, off its side nearest the line), glowing
        // for the crew to see from the line.
        if (t.Comet is not null && k < _comets.Count)
            for (int i = 0; i < _comets[k].Count; i++)
            {
                var put = (CometAt(stop, stop.Containers[_comets[k][i]]) ?? StopWalls.FindAt(stop, stop.Containers[_comets[k][i]])) + new Pt(-0.7 * i, 0);
                bodies.SpawnLoot(StopWorld(line, f, put), f.Start + put.S, LootOwner(k, CometContainer + i), t.Radius);
            }
    }
}
