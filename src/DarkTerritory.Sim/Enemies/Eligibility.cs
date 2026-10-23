using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Sim.Enemies;

/// <summary>One of a kind's gates and whether it holds now (note 875): its name says the number it reads.</summary>
public readonly record struct GateCheck(string Gate, bool Holds);

/// <summary>
/// A kind of the director's spend table, read at one moment (note 875; the spawn director overhaul's brief §2 issue 5): the
/// night's gates the director puts over every kind (the edition's roster, tonight's chance and cap, slain, room among what's
/// holding the crew), its rule's own gates one by one (<see cref="Eligibility.Gates"/>), whether its rule is met (its weight
/// is above 0) and whether the night's curve could send it free (<see cref="SpawnRule.Free"/>, with a weight in
/// <c>curve.kinds</c>). <see cref="Blocked"/> names the first gate that doesn't hold, null when it could be sent now.
/// </summary>
public sealed record KindEligibility(EnemyKind Kind, bool Roster, bool Tonight, bool Slain, bool Room, IReadOnlyList<GateCheck> Gates,
    bool RuleMet, bool FreeMet)
{
    /// <summary>The night allows it: on the roster, about tonight under its cap, not slain.</summary>
    public bool Night => Roster && Tonight && !Slain;
    /// <summary>Its rule's met with the night allowing it and room for it: the pressure's spend and the stops' could send it.</summary>
    public bool Sendable => Night && Room && RuleMet;
    /// <summary>The night's curve could send it without its rule (note 849).</summary>
    public bool SendableFree => Night && Room && FreeMet;

    /// <summary>Its rule met or its free gate: the moment would let it come, whatever the night's roll or the room.</summary>
    public bool Situational => RuleMet || FreeMet;

    /// <summary>The first of its rule's own gates that doesn't hold (or "weight", every gate holding but its weight 0); null when met.</summary>
    public string? SituationBlocked => Gates.FirstOrDefault(g => !g.Holds) is { Gate: not null } g ? g.Gate : !RuleMet ? "weight" : null;

    public string? Blocked => !Roster ? "roster" : !Tonight ? "tonight" : Slain ? "slain" : !Room ? "room"
        : Gates.FirstOrDefault(g => !g.Holds) is { Gate: not null } g ? g.Gate : !RuleMet ? "weight" : null;
}

/// <summary>
/// The monster eligibility audit (note 875; the spawn director overhaul's brief §2 issue 5, deliverable 5): for every kind in
/// <see cref="Spawns.Rules"/>, which of its gates hold now. Pure: it reads the world and the director and draws no dice, so a
/// night sampled by it plays exactly as one that isn't (<c>EligibilityTests</c>). Each kind's gates here mirror its rule in
/// <see cref="Spawns"/>, in the rule's order, reading the same tuning; <c>EligibilityTests</c> holds that a met rule has every
/// gate holding, so a rule changed without its gates here is caught. <c>dt eligibility</c> samples them a second at a time
/// through ordinary nights.
/// </summary>
public static class Eligibility
{
    /// <summary>Every kind of the spend table, now.</summary>
    public static List<KindEligibility> Sample(World world)
    {
        if (world.Director is not { } d || world.Enemies is not { } et)
            return [];
        var ctx = new SpawnContext(world, et, d);
        var free = new SpawnContext(world, et, d) { Free = true };
        var kinds = d.Tuning.Curve.Kinds;
        var list = new List<KindEligibility>(Spawns.Rules.Count);
        foreach (var rule in Spawns.Rules)
        {
            bool met = rule.Weight(ctx) is > 0;
            bool freeMet = rule.Free is { } f && kinds.GetValueOrDefault(Director.Key(rule.Kind), 0) > 0 && f(free);
            list.Add(new KindEligibility(rule.Kind, d.Allows(rule.Kind), d.Tonight(rule.Kind, world), world.Slain.Contains(rule.Kind),
                d.RoomFor(rule.Kind, world), Gates(rule.Kind, ctx), met, freeMet));
        }
        return list;
    }

    /// <summary>The kinds <see cref="Gates"/> names gates for: every kind of the spend table (<c>EligibilityTests</c>).</summary>
    public static bool Named(EnemyKind kind) => kind is EnemyKind.TrackDoll or EnemyKind.CinderHound or EnemyKind.CarHugger or EnemyKind.Climber
        or EnemyKind.Dragger or EnemyKind.Whistler or EnemyKind.TippyToesie or EnemyKind.FireFlies or EnemyKind.Ribbit or EnemyKind.Gaunt
        or EnemyKind.Moose or EnemyKind.TowerJaw or EnemyKind.FreightBeetle or EnemyKind.MouseMoose or EnemyKind.Brakeman or EnemyKind.Hotbox
        or EnemyKind.Knotter or EnemyKind.Gannet or EnemyKind.Follower or EnemyKind.SootChildren or EnemyKind.Passenger or EnemyKind.Grumbler or EnemyKind.Bruin;

    /// <summary>The kind's own rule's gates (<see cref="Spawns"/>), each named for what it reads, in the rule's order.</summary>
    public static List<GateCheck> Gates(EnemyKind kind, SpawnContext c)
    {
        var et = c.Tuning;
        var train = c.Train;
        double v = train.Dynamics.Speed;
        int cars = train.Dynamics.Consist.CarCount;
        bool tunnel = c.World.Route?.Features.Any(f => f.Kind == FeatureKind.Tunnel && f.Contains(c.Front)) == true;
        bool stop = c.AtStop || c.World.InSettlement;
        int roofs = c.Living.Count(p => p.State.Surface == Surface.Roof && p.State.Parent > 0);
        GateCheck G(string name, bool holds) => new(name, holds);
        return kind switch
        {
            EnemyKind.TrackDoll =>
            [
                G("once a night", c.Once(kind)), G("on the main line", train.OnMain),
                G($"straight {et.TrackDoll.StraightNeeded:0} m ahead", TrackDoll.Straight(train, c.Front + et.TrackDoll.SpawnAhead, et.TrackDoll.StraightNeeded)),
                G("no tunnel ahead", c.World.Route?.Features.Any(f => f.Kind == FeatureKind.Tunnel && f.Contains(c.Front + et.TrackDoll.SpawnAhead)) != true),
            ],
            EnemyKind.CinderHound =>
            [
                G($"speed ≥ {et.CinderHounds.MinTrainSpeed:0.#} m/s", v >= et.CinderHounds.MinTrainSpeed), G("a car", cars >= 1), G("none about", c.None(kind)),
            ],
            EnemyKind.CarHugger =>
            [
                G($"cars ≥ {et.CarHugger.MinCars}", cars >= et.CarHugger.MinCars), G("none about", c.None(kind)),
                G(et.CarHugger.LowGround.On ? "a lurking spot ahead (marsh, bridge or low ground, level)" : "a lurking spot ahead (marsh or bridge, level)", CarHugger.Spot(c.World, et.CarHugger) is not null),
            ],
            EnemyKind.Climber =>
            [
                G($"gaps ≥ {et.Climbers.MinGaps}", CrewSense.Gaps(train).Count >= et.Climbers.MinGaps), G($"speed ≥ {et.Climbers.MinSpeed:0.#} m/s", v >= et.Climbers.MinSpeed),
                G("none about", c.None(kind)),
            ],
            EnemyKind.Dragger =>
            [
                G($"cars ≥ {et.Draggers.MinCars}", cars >= et.Draggers.MinCars),
                et.Draggers.BoardBelow < double.MaxValue ? G($"speed < {et.Draggers.BoardBelow:0.#} m/s", v < et.Draggers.BoardBelow) : G("someone on a roof", roofs > 0),
                G($"under {et.Draggers.MaxAttached} about", c.World.ActiveEnemies.Count(e => !e.Gone && e.Kind == kind) < et.Draggers.MaxAttached),
            ],
            EnemyKind.Whistler =>
            [
                G($"cars ≥ {et.Whistler.MinCars}", cars >= et.Whistler.MinCars), G("stopped", c.Stopped), G("none about", c.None(kind)),
                G("a gap", CrewSense.Gaps(train).Count > 0),
            ],
            EnemyKind.TippyToesie =>
            [
                G($"crew ≥ {et.TippyToesie.MinCrew}", c.Crew >= et.TippyToesie.MinCrew), G("none about", c.None(kind)),
                G($"speed < {et.TippyToesie.BoardBelow:0.#} m/s", v < et.TippyToesie.BoardBelow),
                G($"someone idle {et.TippyToesie.IdleSeconds:0} s and alone", c.Living.Any(p => c.World.IdleSeconds.GetValueOrDefault(p.Id) >= et.TippyToesie.IdleSeconds
                    && !c.Living.Any(o => o.Id != p.Id && (PlayerMotor.WorldPosition(o.State, train) - PlayerMotor.WorldPosition(p.State, train)).Length <= et.TippyToesie.AloneRadius))),
            ],
            EnemyKind.FireFlies =>
            [
                G("a lit car with a way in", Spawns.LitCars(c).Count > 0), G("not at a facility", !c.AtFacility),
                G($"speed < {et.FireFlies.StoppedBelow:0.#} m/s", v < et.FireFlies.StoppedBelow),
                G("none about", c.World.ActiveEnemies.Count(e => !e.Gone && e.Kind == kind) < 1), G("not in a tunnel", !tunnel),
            ],
            EnemyKind.Ribbit =>
            [
                G("someone on the ground", c.OnGround.Any()), G($"crew ≥ {et.Ribbits.MinCrew}", c.Crew >= et.Ribbits.MinCrew), G("none about", c.None(kind)),
                G("at a stop or settlement", stop),
            ],
            EnemyKind.Gaunt =>
            [
                G("tier ≥ frontier", c.Tier >= c.Director.Gate(c.World, RouteTier.Frontier)), G($"crew ≥ {et.Gaunt.MinCrew}", c.Crew >= et.Gaunt.MinCrew),
                G("once a night", c.Once(kind)), G("at a stop or settlement", stop), G("someone on the ground", c.OnGround.Any()),
            ],
            EnemyKind.Moose =>
            [
                G($"crew ≥ {et.Moose.MinCrew}", c.Crew >= et.Moose.MinCrew), G("none about", c.None(kind)), G("stopped", c.Stopped), G("at a stop or settlement", stop),
                G("its biome", Moose.BiomeWeight(c.World, et.Moose, c.Front) > 0),
            ],
            EnemyKind.TowerJaw =>
            [
                G("none about", c.None(kind)), G("a coaling tower or gantry at the stop", TowerJaw.Structure(c.World, et.TowerJaw) is not null),
            ],
            EnemyKind.FreightBeetle =>
            [
                G("stopped", c.Stopped), G("at a stop", c.AtStop), G("none about", c.None(kind)), G("loose freight", FreightBeetle.Freight(c.World, et.FreightBeetle).Count > 0),
            ],
            EnemyKind.MouseMoose =>
            [
                G("stopped", c.Stopped), G("at a facility", c.AtFacility), G("none about", c.None(kind)), G("a crane with castings to take", Spawns.MouseMooseCranes(c).Count > 0),
            ],
            EnemyKind.Bruin =>
            [
                G("on (bruin.on)", et.Bruin.On), G("stood in the wilderness long enough", Bruin.Wild(c)), G("none about", c.None(kind)),
                G("a car of cargo to climb into", Bruin.CarFor(c.Train) >= 0),
            ],
            EnemyKind.Brakeman =>
            [
                G($"crew ≥ {et.Brakeman.MinCrew}", c.Living.Count() >= et.Brakeman.MinCrew), G($"cars ≥ {et.Brakeman.MinCars}", cars >= et.Brakeman.MinCars),
                G($"speed ≥ {et.Brakeman.MovingAbove:0.#} m/s", v >= et.Brakeman.MovingAbove), G("none about", c.None(kind)), G("not slain", !c.World.Slain.Contains(kind)),
                G("not in a fort", !c.World.TrainInFort), G("not in a tunnel", !tunnel),
            ],
            EnemyKind.Hotbox =>
            [
                G($"speed ≥ {et.Hotbox.BoardAbove:0.#} m/s", v >= et.Hotbox.BoardAbove), G("none about", c.None(kind)), G("not in a fort", !c.World.TrainInFort),
                G("a car with a sound axle", Hotbox.Cars(train).Count > 0),
            ],
            EnemyKind.Knotter =>
            [
                G($"speed ≥ {et.Knotter.BoardAbove:0.#} m/s", v >= et.Knotter.BoardAbove), G($"cars ≥ {et.Knotter.MinCars}", cars >= et.Knotter.MinCars),
                G("none about", c.None(kind)), G("not in a fort", !c.World.TrainInFort), G("a coupling nobody's at", Knotter.Joints(train, c.Living).Count > 0),
            ],
            EnemyKind.Gannet =>
            [
                G("once a night", c.Once(kind)), G($"cars ≥ {et.Gannet.MinCars}", cars >= et.Gannet.MinCars),
                G($"speed ≥ {et.Gannet.Arrive:0.#} m/s", v >= et.Gannet.Arrive),
                G($"fast for {et.Gannet.ArriveSeconds:0} s", c.World.FastSeconds >= et.Gannet.ArriveSeconds), G("not in a tunnel", !tunnel),
            ],
            EnemyKind.Follower =>
            [
                G("someone off the train on follower ground", Spawns.OnFollowerGround(c, Follower.Excursions(c.World)).Count > 0), G("at a stop or settlement", stop),
                G($"under {et.Followers.MaxActive} about", c.World.ActiveEnemies.Count(e => !e.Gone && e.Kind == kind) < et.Followers.MaxActive),
            ],
            EnemyKind.SootChildren =>
            [
                G($"crew ≥ {et.SootChildren.MinCrew}", c.Crew >= et.SootChildren.MinCrew), G("none about", c.None(kind)), G("someone on the ground", c.OnGround.Any()),
                G("at a stop or settlement", stop),
            ],
            EnemyKind.Passenger =>
            [
                G("tier ≥ dead lines", c.Tier >= c.Director.Gate(c.World, RouteTier.DeadLines)), G($"crew ≥ {et.Passenger.MinCrew}", c.Crew >= et.Passenger.MinCrew),
                G("at a stop", c.AtStop), G("once a night", c.Once(kind)), G("a car to board", Passenger.Car(train) is not null),
            ],
            EnemyKind.Grumbler =>
            [
                G("at a stop", c.AtStop), G("a crane with castings stacked", Spawns.GrumblerCranes(c).Count > 0), G("none about", c.None(kind)),
            ],
            _ => [],
        };
    }
}
