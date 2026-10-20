using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// U1's second whimsies heard (queue #559, note 831; U1's #967, note 744): the hammock in the guard van and the hot tub on the
/// engine's hood got into and out of, the hammock's ropes creaking while anyone lies in it, the tub bubbling while the boiler
/// feeds it, a sausage or a grilled fish bitten while it's eaten, and the fishing rod: the angler's own cast and line (off
/// this machine's own Use, as the listener's own swing: the host keeps when a line went in to itself, so no one else's is
/// on the wire) and everyone's catch, as the fish lands at the angler's feet.
/// </summary>
public sealed partial class GameAudio
{
    const int LoungeOwner = 0x0C8000;
    const int TubOwner = 0x0C8400;
    const int LineOwner = 0x0C8800;
    /// <summary>A sausage or a grilled fish is bitten about every this many seconds while it's had (Bodies.Dose's hold).</summary>
    const double BiteEvery = 1.3;
    /// <summary>A fish that turns up this close (m) to someone with a line in the water was theirs: caught.</summary>
    const double FishCaughtWithin = 2.0;
    /// <summary>The hot tub's bubbling with nobody in it, against its level with a bather.</summary>
    const double TubIdle = 0.6;

    readonly Dictionary<int, LoungeKind?> _loungeWas = [];
    readonly HashSet<int> _fishSeen = [];
    readonly Dictionary<int, double> _biteNext = [];
    bool _castWas, _loungePrimed;

    void EndNightLounge()
    {
        _loungeWas.Clear();
        _fishSeen.Clear();
        _biteNext.Clear();
        _castWas = false;
        _loungePrimed = false;
    }

    void LoungeSounds(World world)
    {
        var train = world.Train;
        bool primed = _loungePrimed;
        _loungePrimed = true;
        float outside = Occlusion(PlayerMotor.Outside);
        // Got into or out of the hammock or the hot tub: where they are, in the one they got into or out of.
        var lying = new HashSet<(int, Double3)>();
        int bathers = 0;
        foreach (var (id, s) in CrewStates)
        {
            // (Not the big cab's benches, note 905: sat on in silence, for now; AU1's to give a sound.)
            var now = s.Alive && Bbq.LoungingIn(s, train) is { Kind: not LoungeKind.Bench } lounge ? lounge : (Lounge?)null;
            var was = _loungeWas.GetValueOrDefault(id);
            if (primed && now?.Kind != was && (now?.Kind ?? was) is { } kind && At(train, s) is { } feet)
                Cue(kind == LoungeKind.Hammock ? "up-lounge.hammock" : "up-lounge.tub-in", feet + Double3.Up * 0.5,
                    Occlusion(PlayerMotor.Space(s, train)));
            _loungeWas[id] = now?.Kind;
            if (now is { Kind: LoungeKind.Hammock } hammock)
                lying.Add((s.Parent, hammock.At));
            else if (now is { Kind: LoungeKind.HotTub })
                bathers++;
        }
        bool steam = train.BoilerTuning is null || train.Boiler.Pressure > 0;
        for (int v = 0; v < train.Frames.Count; v++)
        {
            var frame = train.Frames[v];
            var tub = Double3.Zero;
            int seats = 0;
            foreach (var l in frame.Shape.Lounges)
            {
                // Lain in, the van rocking it: its ropes creak at each end of the swing.
                if (l.Kind == LoungeKind.Hammock && lying.Contains((v, l.At)))
                    Hold("up-lounge.sway", LoungeOwner + v, frame.ToWorld(l.At + Double3.Up * 0.5), OccludedAt(train, v, l.At));
                if (l.Kind == LoungeKind.HotTub)
                {
                    tub += l.At;
                    seats++;
                }
            }
            // The hot tub fed off the boiler (note 744: "warm while the boiler has steam"): bubbling while it's fed, up with
            // someone in it.
            if (seats > 0 && steam)
                HoldLevel("up-lounge.tub", TubOwner + v, frame.ToWorld(tub * (1.0 / seats) + Double3.Up * 0.3), outside,
                    bathers > 0 ? 1 : TubIdle);
        }
        Fishing(world, outside, primed);
        Eating(world);
    }

    /// <summary>
    /// The fishing rod (note 744): this machine's own angler's cast (Use pressed with a line to put in the water) and their
    /// line in it while Use is held; a fish that turns up beside anyone with a line in the water, caught.
    /// </summary>
    void Fishing(World world, float outside, bool primed)
    {
        var train = world.Train;
        bool fishing = Crewmate(OwnId) is { } own && world.Angling(own, OwnId) && OwnIntent.Has(PlayerButtons.Use);
        if (fishing && Crewmate(OwnId) is { } angler)
        {
            var ahead = new Double3(-Math.Sin(angler.Yaw), 0, -Math.Cos(angler.Yaw));
            if (!_castWas)
                Cue("up-fishing.cast", angler.Position + Double3.Up * 1.3 + ahead * 0.8, outside);
            Hold("up-fishing.line", LineOwner, angler.Position + ahead * 3 + Double3.Up * 0.2, outside);
        }
        _castWas = fishing;
        foreach (var b in world.Bodies.All)
        {
            if (b.Kind != BodyKind.Fish || !_fishSeen.Add(b.Id) || !primed || b.Carrier >= 0)
                continue;
            var at = Bodies.WorldCentre(b, train);
            foreach (var (id, s) in CrewStates)
                if (world.Angling(s, id) && ((s.Position - at) with { Y = 0 }).Length < FishCaughtWithin)
                {
                    Cue("up-fishing.catch", at, outside);
                    break;
                }
        }
        if (_fishSeen.Count > 64)
            _fishSeen.RemoveWhere(id => !world.Bodies.All.Any(b => b.Id == id));
    }

    /// <summary>
    /// A sausage or a grilled fish had with Use held (note 698, 744; Bodies.Dose counts the hold in its MendTicks): a bite
    /// as it starts, and another every so often while they're at it, at the eater's mouth.
    /// </summary>
    void Eating(World world)
    {
        var train = world.Train;
        var eating = new HashSet<int>();
        foreach (var b in world.Bodies.All)
        {
            if (b.Kind is not (BodyKind.Sausage or BodyKind.GrilledFish) || b.MendTicks <= 0 || Crewmate(b.Carrier) is not { } s
                || At(train, s) is not { } feet)
                continue;
            eating.Add(b.Id);
            if (_biteNext.TryGetValue(b.Id, out double next) && _time < next)
                continue;
            Cue("up-bbq.eat", feet + Double3.Up * 1.55, Occlusion(PlayerMotor.Space(s, train)));
            _biteNext[b.Id] = _time + BiteEvery * (0.8 + 0.4 * OutsideOdds());
        }
        foreach (var id in _biteNext.Keys.Where(k => !eating.Contains(k)).ToList())
            _biteNext.Remove(id);
    }
}
