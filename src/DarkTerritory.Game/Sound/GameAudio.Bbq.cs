using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The BBQ deck heard (queue #505, note 775; U1's #967, note 698): "the BBQ Dads at the end of the world". Off the deck's
/// record (the grill's charcoal left, the cooler's beers, every client's) and the crew's: the charcoal catching as it's lit,
/// the grill burning while it is, a sausage turned while someone's at it, the cooler's lid as a beer comes out, a can cracked
/// as one's drunk, cans clinked when two with beers meet, a Muskoka chair as someone sits or gets up. And the dining car's bar
/// (queue #683, note 983; upgrades.md §8.5, note 959): its bell and a pour at the counter as its drinks go down, a glass's sip
/// where a drink in hand is drunk, glasses clinked where a drink meets a drink or a beer.
/// </summary>
public sealed partial class GameAudio
{
    const int BbqOwner = 0x0B8000;
    /// <summary>A sausage turned about every this many seconds while someone works the grill.</summary>
    const double BbqTurnEvery = 2.5;
    /// <summary>Two crewmates with beers in hand this close (m) touch cans: cheers, once a pair of cans.</summary>
    const double CheersWithin = 1.2;

    readonly Dictionary<int, int> _beersWas = [];
    readonly Dictionary<int, int> _drinksWas = [];
    readonly Dictionary<int, (int By, Double3 At, bool Glass, float Occlusion)> _beersHeld = [];
    readonly HashSet<(int, int)> _cheered = [];
    readonly Dictionary<int, bool> _lounging = [];
    readonly Dictionary<int, double> _turnNext = [];
    bool _bbqPrimed;

    void EndNightBbq()
    {
        _beersWas.Clear();
        _drinksWas.Clear();
        _beersHeld.Clear();
        _cheered.Clear();
        _lounging.Clear();
        _turnNext.Clear();
        _bbqPrimed = false;
    }

    void BbqSounds(World world)
    {
        var train = world.Train;
        float outside = Occlusion(PlayerMotor.Outside);
        bool primed = _bbqPrimed;
        _bbqPrimed = true;
        for (int v = 0; v < train.Frames.Count && v < train.Vehicles.Count; v++)
        {
            if (train.Frames[v].Shape.Bbq is not { } deck || train.Vehicles[v] is not { } car)
                continue;
            var frame = train.Frames[v];
            var grill = frame.ToWorld(deck.Grill + Double3.Up * 0.9);
            bool lit = Bbq.Lit(car);
            if (Flipped("up-bbq.light", BbqOwner + v, lit, primed) > 0)
                Cue("up-bbq.light", grill, outside);
            // Someone working it (Use held at the grill: their progress counting): the sizzle up, and a sausage turned now and then.
            bool cooking = false;
            foreach (var (id, s) in CrewStates)
                if (lit && s.Alive && s.Parent == v && s.ActionProgress > 0 && (s.Position - deck.Grill).Length < 1.6)
                {
                    cooking = true;
                    double next = _turnNext.GetValueOrDefault(id);
                    if (next <= 0)
                        _turnNext[id] = _time + BbqTurnEvery * (0.4 + 0.4 * OutsideOdds());
                    else if (_time >= next)
                    {
                        Cue("up-bbq.turn", grill, outside);
                        _turnNext[id] = _time + BbqTurnEvery * (0.7 + 0.6 * OutsideOdds());
                    }
                }
                else
                    _turnNext.Remove(id);
            if (lit)
                HoldLevel("up-bbq.grill", BbqOwner + v, grill, outside, cooking ? 1 : 0.7);
            // A beer out of the cooler: its lid.
            if (_beersWas.TryGetValue(v, out int was) && car.Beers < was && primed)
                Cue("up-bbq.cooler", frame.ToWorld(deck.Cooler + Double3.Up * 0.4), outside);
            _beersWas[v] = car.Beers;
            // A Muskoka chair sat in or got up from (the Lounging flag, at one of the deck's chairs).
            foreach (var (id, s) in CrewStates)
            {
                bool sat = s.Has(PlayerFlags.Lounging) && s.Parent == v;
                bool wasSat = _lounging.GetValueOrDefault(id);
                var feet = s.Position;
                if (sat != wasSat && primed && s.Parent == v && deck.Chairs.Any(c => (c - feet).Length < 0.6))
                    Cue("up-bbq.chair", frame.ToWorld(deck.Chairs.MinBy(c => (c - feet).Length) + Double3.Up * 0.4), outside);
            }
        }
        foreach (var (id, s) in CrewStates)
            _lounging[id] = s.Has(PlayerFlags.Lounging);
        BarPours(train, primed);
        Beers(world, outside, primed);
    }

    /// <summary>
    /// A drink poured at the dining car's bar (note 983): its drinks left (the car's record, every client's) down by one, the
    /// counter's bell and the pour on its top, inside the car.
    /// </summary>
    void BarPours(TrainOnLine train, bool primed)
    {
        for (int v = 0; v < train.Frames.Count && v < train.Vehicles.Count; v++)
        {
            if (train.Frames[v].Shape.Dining is not { } dining || train.Vehicles[v] is not { } car)
                continue;
            var c = dining.Counter;
            var top = new Double3((c.Min.X + c.Max.X) / 2, c.Max.Y, (c.Min.Z + c.Max.Z) / 2);
            if (_drinksWas.TryGetValue(v, out int was) && car.Beers < was && primed)
                Cue("up-dining.pour", train.Frames[v].ToWorld(top), OccludedAt(train, v, top));
            _drinksWas[v] = car.Beers;
        }
    }

    /// <summary>
    /// The beers in hand (note 698): one that's gone while it was held has been drunk (the host takes it when the drink's done),
    /// a can cracked and a swig where its drinker's head is; two crewmates with beers in hand close together, cans clinked. The
    /// dining car's drinks are glasses (note 983): a sip from one, and a clink of glasses where one's touched, the can's and the
    /// cheers' where the bar's line isn't installed.
    /// </summary>
    void Beers(World world, float outside, bool primed)
    {
        var train = world.Train;
        var held = new Dictionary<int, (int By, Double3 At, bool Glass, float Occlusion)>();
        foreach (var b in world.Bodies.All)
            if (b.Kind is BodyKind.Beer or BodyKind.Drink && b.Carrier >= 0 && Crewmate(b.Carrier) is { } s && At(train, s) is { } at)
            {
                // Indoors (the dining car's drinks are drunk in it) the drinker's car decides who hears it; out on a deck, the open air.
                float occ = s.Parent != PlayerState.World && s.Parent < train.Frames.Count ? OccludedAt(train, s.Parent, s.Position) : outside;
                held[b.Id] = (b.Carrier, at, b.Kind == BodyKind.Drink, occ);
            }
        string sip = HasCue("up-dining.sip") ? "up-dining.sip" : "up-bbq.can";
        string clink = HasCue("up-dining.clink") ? "up-dining.clink" : "up-bbq.cheers";
        foreach (var (id, (by, at, glass, occ)) in _beersHeld)
            if (!held.ContainsKey(id) && !world.Bodies.All.Any(b => b.Id == id) && primed)
                Cue(glass ? sip : "up-bbq.can", at + Double3.Up * 1.55, occ);
        var cans = held.ToList();
        for (int i = 0; i < cans.Count; i++)
            for (int j = i + 1; j < cans.Count; j++)
            {
                var (a, b) = (cans[i], cans[j]);
                if (a.Value.By == b.Value.By || (a.Value.At - b.Value.At).Length > CheersWithin)
                    continue;
                if (_cheered.Add((Math.Min(a.Key, b.Key), Math.Max(a.Key, b.Key))) && primed)
                    Cue(a.Value.Glass || b.Value.Glass ? clink : "up-bbq.cheers", (a.Value.At + b.Value.At) * 0.5 + Double3.Up * 1.4,
                        Math.Min(a.Value.Occlusion, b.Value.Occlusion));
            }
        _beersHeld.Clear();
        foreach (var (id, h) in held)
            _beersHeld[id] = h;
        if (_cheered.Count > 64)
            _cheered.RemoveWhere(p => !held.ContainsKey(p.Item1) && !held.ContainsKey(p.Item2));
    }
}
