using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Run;

/// <summary>A hiding spot in an open house (note 326): the stop, its container, where its find is put out, and how long it takes to search.</summary>
public readonly record struct HidingSpot(int Stop, StopContainer Container, Double3 At, double Seconds)
{
    public int Key => Run.LootOwner(Stop, Container.Index);
}

/// <summary>
/// Searching the open houses (GDD App. F.3 "searchable for finds"; level-design P14 "hiding spots ... that take time to
/// search"; ARCHITECTURE §8 note 326): what a plain village house keeps in a cupboard, a cabinet, its cellar or under its
/// boards isn't out until someone has gone through it. A crewmate on foot and empty-handed, at the spot, holds Use for
/// the kind's seconds (loot.json <c>search</c>); let go and it starts again. Then the find comes out where it was kept,
/// a toy with it if there was one. The host searches; which spots are done, and how far along the ones under way are,
/// replicate (<see cref="Net.WorldRecords"/>), so every crewmate's HUD says what's left. Nothing a client predicts: the
/// player and the train are unchanged by it, and the finds are bodies the host puts out.
/// </summary>
public sealed partial class Run
{
    // Every stop's hiding spots, worked out once the stops are filled (the line doesn't move). The spots searched (their
    // LootOwner keys), alike on host and clients. Host: who's at which spot, holding Use how long. Clients: each spot under
    // way, as far through as the host has it.
    readonly List<HidingSpot> _spots = [];
    readonly HashSet<int> _searched = [];
    readonly Dictionary<int, (int Key, double Held)> _searching = new();
    readonly Dictionary<int, double> _searchMirror = new();

    /// <summary>Every stop's hiding spots (none without loot.json <c>search</c>).</summary>
    public IReadOnlyList<HidingSpot> HidingSpots => _spots;

    /// <summary>Whether a stop's container has been searched.</summary>
    public bool Searched(int stop, int container) => _searched.Contains(LootOwner(stop, container));

    /// <summary>Whether a container is a hiding spot: in an open house, of a kind that's searched.</summary>
    static bool Hides(LootTuning t, StopLayout stop, StopContainer c) =>
        t.Search?.Of(c.Kind) is not null && c.Building >= 0 && c.Building < stop.Buildings.Count
        && stop.Buildings[c.Building].Open && StopWalls.Walled(stop, c.Building);

    /// <summary>Still to search: a hiding spot nobody's been through.</summary>
    bool Hidden(int stop, StopContainer c) =>
        _loot is { } t && Hides(t, _stopLoot[stop].Stop, c) && !_searched.Contains(LootOwner(stop, c.Index));

    void FindHidingSpots()
    {
        _spots.Clear();
        _searched.Clear();
        _searching.Clear();
        _searchMirror.Clear();
        if (_loot is not { Search: { } search } t || _lootLine is not { } line)
            return;
        for (int k = 0; k < _stopLoot.Count; k++)
        {
            var (f, _, stop, _, _, _) = _stopLoot[k];
            foreach (var c in stop.Containers)
                if (Hides(t, stop, c))
                    _spots.Add(new HidingSpot(k, c, StopWorld(line, f, StopWalls.FindAt(stop, c)), search.Of(c.Kind)!.Value));
        }
    }

    /// <summary>
    /// The hiding spot a crewmate is at and could search (on foot, alive, within reach of it, its stop's loot out): the
    /// nearest still to search, else null. Alike on host and client, for the HUD.
    /// </summary>
    public HidingSpot? SpotInReach(in PlayerState s, TrainOnLine train)
    {
        if (Over || !s.Alive || s.Parent != PlayerState.World || _loot?.Search is not { } t || _spots.Count == 0)
            return null;
        var at = PlayerMotor.WorldPosition(s, train);
        HidingSpot? best = null;
        double nearest = double.MaxValue;
        foreach (var spot in _spots)
        {
            var d = spot.At - at;
            double flat = (d with { Y = 0 }).Length;
            // Standing at it, a reaching hand or not: a cupboard's gone through, not gripped.
            if (flat > t.Reach || Math.Abs(d.Y) > 2 || flat >= nearest || !Stocked(spot.Stop) || _searched.Contains(spot.Key))
                continue;
            best = spot;
            nearest = flat;
        }
        return best;
    }

    /// <summary>How far through searching a spot it is (0..1): the furthest any crewmate has got (host), or as the host has it (client).</summary>
    public double SearchProgress(in HidingSpot spot)
    {
        if (_searchMirror.TryGetValue(spot.Key, out var mirrored))
            return mirrored;
        double held = 0;
        foreach (var (_, (key, h)) in _searching)
            if (key == spot.Key)
                held = Math.Max(held, h);
        return Math.Min(1, held / spot.Seconds);
    }

    /// <summary>
    /// Host: a crewmate's hands on a hiding spot this tick (from <see cref="World.CrewAct"/>, once the hands have had their
    /// go: a Use press that picked something up isn't a search). Held long enough, the spot's find and toy come out.
    /// </summary>
    internal void SearchAct(in PlayerState s, in PlayerIntent intent, int playerId, World world, bool emptyHanded)
    {
        bool use = emptyHanded && intent.Has(PlayerButtons.Use) && intent.MoveZ <= 0.5;
        if (!use || SpotInReach(s, world.Train) is not { } spot)
        {
            _searching.Remove(playerId);
            return;
        }
        double held = (_searching.TryGetValue(playerId, out var was) && was.Key == spot.Key ? was.Held : 0) + SimConstants.TickSeconds;
        if (held < spot.Seconds - 1e-9)
        {
            _searching[playerId] = (spot.Key, held);
            return;
        }
        // Gone through: nobody else is searching it now.
        foreach (int other in _searching.Where(x => x.Value.Key == spot.Key).Select(x => x.Key).ToList())
            _searching.Remove(other);
        _searching.Remove(playerId);
        Reveal(world.Bodies, spot.Stop, spot.Container);
    }

    /// <summary>A spot searched: what it kept comes out where it was kept.</summary>
    void Reveal(Physics.Bodies bodies, int k, StopContainer c)
    {
        if (!_searched.Add(LootOwner(k, c.Index)) || _loot is not { } t || _lootLine is not { } line)
            return;
        var (f, _, stop, finds, _, toys) = _stopLoot[k];
        var put = StopWalls.FindAt(stop, c);
        if (finds.Any(x => x.Container == c.Index))
            bodies.SpawnLoot(StopWorld(line, f, put), f.Start + put.S, LootOwner(k, c.Index), t.Radius);
        // A toy with it (note 264), beside the find.
        foreach (var toy in toys)
            if (toy.Container == c.Index)
                bodies.SpawnItem(StopWorld(line, f, put + new Pt(-0.4, 0.3)), f.Start + put.S, Physics.BodyKind.Toy).Noise = toy.Noise;
    }

    /// <summary>
    /// The search at a stop, for the wire: the containers searched, and each spot under way with how far through it is.
    /// Only for a stop whose loot is out and that has hiding spots.
    /// </summary>
    public (IReadOnlyList<int> Searched, IReadOnlyList<(int Container, double Progress)> Under) SearchState(int stop)
    {
        var searched = new List<int>();
        var under = new List<(int, double)>();
        foreach (var spot in _spots)
        {
            if (spot.Stop != stop)
                continue;
            if (_searched.Contains(spot.Key))
                searched.Add(spot.Container.Index);
            else if (SearchProgress(spot) is > 0 and var p)
                under.Add((spot.Container.Index, p));
        }
        return (searched, under);
    }

    /// <summary>Whether a stop's search goes on the wire: its loot is out, and it has hiding spots.</summary>
    public bool Searchable(int stop) => Stocked(stop) && _spots.Any(s => s.Stop == stop);

    /// <summary>Client side: adopts the host's search at a stop (its loot is out there, then).</summary>
    public void MirrorSearch(int stop, IEnumerable<int> searched, IEnumerable<(int Container, double Progress)> under)
    {
        if (stop < 0 || stop >= _stocked.Length)
            return;
        _stocked[stop] = true;
        _searched.RemoveWhere(key => key >> 12 == stop);
        foreach (int c in searched)
            _searched.Add(LootOwner(stop, c));
        foreach (int key in _searchMirror.Keys.Where(key => key >> 12 == stop).ToList())
            _searchMirror.Remove(key);
        foreach (var (c, p) in under)
            _searchMirror[LootOwner(stop, c)] = p;
    }
}
