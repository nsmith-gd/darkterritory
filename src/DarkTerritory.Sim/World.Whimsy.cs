using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim;

// The whimsies (ARCHITECTURE §8 note 715; docs/design/upgrades.md §8.2): Gerald and the garden gnomes the Gannet dives on, the
// canary that goes quiet and dies, the gramophone and the calliope that the Gaunt and the Moose hear. The host's state, in the
// world record (WhimsyState); worked out on the host only, but each in the open: a tell on the train, and a line in the report.
public sealed partial class World
{
    /// <summary>The whimsies' state tonight (the host's; a client's from the world record).</summary>
    public WhimsyState Whimsy { get; set; }

    // How long the train's stood at a chemical works (s), for the canary; who had Use down at the gramophone last tick.
    double _atChemicals;
    readonly HashSet<int> _windingWas = [];

    WhimsyTuning WhimsyTuning => Train.Dynamics.Tuning.Composition.Whimsy;

    bool Fitted(string id) => Train.Dynamics.Tuning.Composition.Dressed(id);

    /// <summary>Gerald and the gnomes still aboard tonight (note 715): what the Gannet can dive on.</summary>
    public List<Decoy> Decoys() =>
        [.. Sim.Train.Whimsy.Decoys(Train, Fitted("gerald"), Fitted("gardenGnomes") ? WhimsyTuning.Gnomes : 0).Where(d => !Whimsy.IsTaken(d.Index))];

    /// <summary>
    /// The Gannet's taken a decoy (note 715): gone for the night, and the clerk has it ("Gerald. Body not recovered. Fee 0.").
    /// </summary>
    public void TakeDecoy(Decoy d)
    {
        if (!Authority || Whimsy.IsTaken(d.Index))
            return;
        Whimsy = Whimsy with { Taken = Whimsy.Taken | 1 << d.Index };
        Attribution.Add(Sim.Run.IncidentLog.Event(this, Sim.Run.IncidentKind.Whimsy, d.Gerald ? "Gerald taken by the Gannet" : "A garden gnome taken by the Gannet",
            -1, d.Gerald ? "Body not recovered. Fee 0." : "Not recovered.", Train.Frames[d.Car].ToWorld(d.Local)));
    }

    /// <summary>Whether the gramophone's playing (wound, and its record not yet run out).</summary>
    public bool GramophonePlaying => Whimsy.Playing && Fitted("gramophone");

    /// <summary>Where the gramophone is in the world, and the calliope while it blows: what the Moose stops to listen to.</summary>
    public IEnumerable<(Double3 At, double Hears)> Playing()
    {
        if (GramophonePlaying && Train.Frames.Count > 1)
        {
            var van = Train.Frames[Sim.Train.Whimsy.GramophoneCar(Train)];
            yield return (van.ToWorld(Sim.Train.Whimsy.Gramophone(van.Shape) + Double3.Up * Sim.Train.Whimsy.Table), WhimsyTuning.GramophoneHears);
        }
        if (WhistleSeconds > 0 && Fitted("calliope"))
        {
            var engine = Train.Frames[0];
            yield return (engine.ToWorld(new Double3(0, engine.Shape.RoofHeight + 0.6, -2)), WhimsyTuning.CalliopeHears);
        }
    }

    /// <summary>A crewmate pulling the cord fresh (the whistle quiet before it): the calliope's next note (note 715).</summary>
    void CalliopePull(int by)
    {
        if (by >= 0 && WhistleSeconds <= 0 && Fitted("calliope"))
            Whimsy = Whimsy with { CalliopeNote = Whimsy.CalliopeNote + 1 };
    }

    /// <summary>
    /// The gramophone wound or stopped (host; each tick, after the hands): a Use press at it starts the next record from the
    /// start; pressed again while it plays, it's lifted off. A record plays twice through, and stops.
    /// </summary>
    void WindGramophone(in PlayerState s, in PlayerIntent intent, int playerId)
    {
        bool use = intent.Has(PlayerButtons.Use) && s.Alive && Fitted("gramophone")
            && CrewActions.NearestInteractable(s, Train, Hand) is ({ Kind: InteractableKind.Gramophone }, _);
        bool pressed = use && !_windingWas.Contains(playerId);
        if (use)
            _windingWas.Add(playerId);
        else
            _windingWas.Remove(playerId);
        if (!Authority || !pressed || BandTuning is not { Scores.Count: > 0 } band)
            return;
        Whimsy = Whimsy.Playing
            ? Whimsy with { Playing = false }
            : Whimsy with { Playing = true, Record = (Whimsy.Record + 1) % band.Scores.Count, WoundAt = Tick };
    }

    /// <summary>The canary gassed: the chemicals' fumes set off near its cage (App. B.9; note 715).</summary>
    void CanaryGassed(IEnumerable<int> cars)
    {
        if (!Fitted("canary") || Whimsy.Canary == CanaryMood.Dead)
            return;
        int car = Sim.Train.Whimsy.CanaryCar(Train);
        var cage = Train.Frames[car].ToWorld(Sim.Train.Whimsy.Cage(Train));
        if (cars.Any(c => FromCar(c, cage) <= WhimsyTuning.CanaryGasM))
            CanaryDies("The chemicals' gas");
    }

    void CanaryDies(string why)
    {
        Whimsy = Whimsy with { Canary = CanaryMood.Dead };
        Attribution.Add(Sim.Run.IncidentLog.Event(this, Sim.Run.IncidentKind.Whimsy, "The canary deceased", -1, $"{why}. Fee 0.",
            Sim.Train.Whimsy.CanaryCar(Train)));
    }

    /// <summary>
    /// Host, each tick: the canary's mood (it stops singing while an interior creature's aboard its car or one either side, and
    /// dies after <see cref="WhimsyTuning.CanaryFumesSeconds"/> stood at a chemical works), and the gramophone's record run out.
    /// </summary>
    void StepWhimsy()
    {
        if (!Authority)
            return;
        var t = WhimsyTuning;
        // The hot tub (note 744) fed off the boiler: each bather draws its steam.
        if (Train.BoilerTuning is not null)
            foreach (var (_, bather) in LastCrew)
                if (bather.Alive && Bbq.LoungingIn(bather, Train) is { Kind: LoungeKind.HotTub })
                    Train.Boiler.Pressure = Math.Max(0, Train.Boiler.Pressure - t.TubSteam * SimConstants.TickSeconds);
        if (Whimsy.Playing && BandTuning is { } band && Whimsy.Record < band.Scores.Count
            && (Tick - Whimsy.WoundAt) * SimConstants.TickSeconds >= band.Scores[Whimsy.Record].Seconds * 2)
            Whimsy = Whimsy with { Playing = false };
        if (!Fitted("canary") || Whimsy.Canary == CanaryMood.Dead)
            return;
        bool chemicals = Run is { Phase: Sim.Run.RunPhase.AtFacility, FacilityFeature.Facility: DarkTerritory.Sim.Route.FacilityKind.ChemicalWorks };
        _atChemicals = chemicals ? _atChemicals + SimConstants.TickSeconds : 0;
        if (_atChemicals >= t.CanaryFumesSeconds)
        {
            CanaryDies("The chemical works' fumes");
            return;
        }
        int car = Sim.Train.Whimsy.CanaryCar(Train);
        bool near = _enemies.Any(e => !e.Gone && e.Zone == PressureZone.Interior && e.Phase is not SpinePhase.Dormant
            && e.Attached >= 0 && Math.Abs(e.Attached - car) <= t.CanaryCars);
        var mood = near ? CanaryMood.Silent : CanaryMood.Singing;
        if (mood != Whimsy.Canary)
            Whimsy = Whimsy with { Canary = mood };
    }

    // When each angler's line went in (the tick), while it's in.
    readonly SortedDictionary<int, uint> _castAt = [];

    /// <summary>
    /// The fishing rod in hand, stood on dry ground off the train, facing water within <see cref="WhimsyTuning.FishReach"/> m
    /// (note 744): a line in the water.
    /// </summary>
    public bool Angling(in PlayerState s, int playerId)
    {
        if (!Fitted("fishingRod") || !s.Alive || s.Parent != PlayerState.World || s.Surface != Surface.Ground
            || Bodies.CarriedBy(playerId) is not { Kind: Physics.BodyKind.FishingRod })
            return false;
        var ahead = new Double3(-DMath.Sin(s.Yaw), 0, -DMath.Cos(s.Yaw));
        for (double d = 1; d <= WhimsyTuning.FishReach; d += 0.5)
        {
            var p = s.Position + ahead * d;
            double hint = s.LineHint;
            if (Sim.Combat.Guns.Water(Train, p) is { } water && water > PlayerMotor.GroundAt(p, Train.Line, ref hint))
                return true;
        }
        return false;
    }

    /// <summary>A fish in hand at the grill (note 744): held there while it cooks.</summary>
    bool GrillingFish(in PlayerState s, int playerId) =>
        Fitted("fishingRod") && Bodies.CarriedBy(playerId) is { Kind: Physics.BodyKind.Fish } && CrewActions.Nearest(s, Train, Hand) == InteractableKind.Grill;

    /// <summary>
    /// Host: Use held with the line in the water; a bite comes in <see cref="WhimsyTuning.Bite"/> s (by the tick it was cast
    /// and whose it is), and a fish lands flapping at the angler's feet. The clerk has every one.
    /// </summary>
    void Fishing(in PlayerState s, in PlayerIntent intent, int playerId, bool angling)
    {
        if (!Authority)
            return;
        if (!angling || !intent.Has(PlayerButtons.Use))
        {
            _castAt.Remove(playerId);
            return;
        }
        if (!_castAt.TryGetValue(playerId, out uint cast))
        {
            _castAt[playerId] = Tick;
            return;
        }
        var bite = WhimsyTuning.Bite;
        uint h = (cast * 2654435761u) ^ (uint)(playerId * 40503);
        double wait = bite[0] + (bite[^1] - bite[0]) * (h % 1000) / 1000.0;
        if ((Tick - cast) * SimConstants.TickSeconds < wait)
            return;
        _castAt[playerId] = Tick;
        var ahead = new Double3(-DMath.Sin(s.Yaw), 0, -DMath.Cos(s.Yaw));
        Bodies.SpawnItem(s.Position + ahead * 0.6 + Double3.Up * 0.3, s.LineHint, Physics.BodyKind.Fish);
        Attribution.Add(Sim.Run.IncidentLog.Event(this, Sim.Run.IncidentKind.Whimsy, "A fish caught", playerId, "Angler: {actor}.", s.Position));
    }

    /// <summary>The fish in the cook's hands is grilled (note 744): eaten, it heals <see cref="WhimsyTuning.FishHeals"/>.</summary>
    void Grilled(Physics.Body raw, in PlayerState s, int playerId)
    {
        Bodies.Remove(raw);
        var fish = Bodies.SpawnItem(PlayerMotor.WorldPosition(s, Train) + Double3.Up * 1.0, s.LineHint, Physics.BodyKind.GrilledFish);
        fish.Carrier = playerId;
        fish.Claimed = true;
    }

    /// <summary>A new night (Stock): Gerald and the gnomes back, the canary singing, the gramophone stopped.</summary>
    void RestockWhimsy()
    {
        Whimsy = default;
        _atChemicals = 0;
        _castAt.Clear();
    }
}
