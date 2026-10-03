using Ballast;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>What a spawn rule sees: the world, the enemies' tuning, the director, and where the train is.</summary>
public sealed class SpawnContext(World world, EnemyTuning tuning, Director director)
{
    public World World { get; } = world;
    public EnemyTuning Tuning { get; } = tuning;
    public Director Director { get; } = director;
    public TrainOnLine Train => World.Train;
    public double Front => Train.Dynamics.Distance;
    public int Crew => Director.Crew;
    public IEnumerable<(int Id, PlayerState State)> Living => World.CrewThisTick.Where(c => c.State.Alive).Select(c => ((int)c.Id, c.State));
    public IEnumerable<(int Id, PlayerState State)> OnGround => Living.Where(c => CrewSense.OnGround(c.State));
    public bool AtFacility => World.Run is { Phase: RunPhase.AtFacility };
    public bool Stopped => Train.Dynamics.Speed < 0.3;
    /// <summary>
    /// Stopped at a stop: a facility, a dead settlement, or any stop the line has a layout for (a yard, a village halt). Where
    /// GDD v1.5's three are met, the crew on foot.
    /// </summary>
    public bool AtStop => Stopped && (AtFacility || World.InSettlement
        || World.Route?.Features.Any(f => f.Stop is not null && Front >= f.Start - 100 && Front <= f.End + 100) == true);
    public RouteTier Tier => World.Route?.Tier ?? RouteTier.Frontier;
    public bool Once(EnemyKind kind) => !Director.Log.Any(l => l.Kind == kind) && !World.ActiveEnemies.Any(e => !e.Gone && e.Kind == kind);
    public bool None(EnemyKind kind) => !World.ActiveEnemies.Any(e => !e.Gone && e.Kind == kind);
    /// <summary>The ground crew's middle, for what comes at them out of a yard.</summary>
    public Double3? GroundCentre()
    {
        var ground = OnGround.Select(c => PlayerMotor.WorldPosition(c.State, Train)).ToList();
        return ground.Count == 0 ? null : ground.Aggregate(Double3.Zero, (a, b) => a + b) * (1.0 / ground.Count);
    }
    /// <summary>A point on the ground out from <paramref name="centre"/>, away from the train, deterministic from the director's dice.</summary>
    public Double3 Out(Double3 centre, double distance)
    {
        var t = Train.Line.Sample(Train.Dynamics.Path, Front - Train.Dynamics.Consist.LengthMetres * 0.5);
        var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
        double side = Double3.Dot(centre - t.Position, right) >= 0 ? 1 : -1;
        double along = Director.NextRange(-0.6, 0.6);
        var dir = (right * side + t.Tangent * along).Normalized;
        var at = centre + dir * distance;
        return at with { Y = centre.Y };
    }
    public int NextId => World.NextEnemyId;
    public T Add<T>(Func<int, T> make) where T : Enemy => World.AddEnemy(make);
}

/// <summary>
/// One enemy's spawn rule (GDD v1.1 App. B.2-B.8): its context and gates (<see cref="Weight"/> null when it can't come now),
/// its weighting, and how it's put into the world (<see cref="Spawn"/>, false if in the end it couldn't be).
/// </summary>
public sealed record SpawnRule(EnemyKind Kind, Func<SpawnContext, double?> Weight, Func<SpawnContext, bool> Spawn);

/// <summary>The director's spawn table: every enemy it spends on (the Stoker and the Choir come on their own conditions).</summary>
public static class Spawns
{
    public static readonly IReadOnlyList<SpawnRule> Rules =
    [
        // B.2 · Track Doll: straight track with a clear 200 m sightline ahead, once a run; weight up if the cab's been left
        // empty earlier in the run.
        new(EnemyKind.TrackDoll, c =>
        {
            var t = c.Tuning.TrackDoll;
            double at = c.Front + t.SpawnAhead;
            if (!c.Once(EnemyKind.TrackDoll) || !c.Train.OnMain || !TrackDoll.Straight(c.Train, at, t.StraightNeeded)
                || c.World.Route?.Features.Any(f => f.Kind == FeatureKind.Tunnel && f.Contains(at)) == true)
                return null;
            return c.World.CabWasLeftEmpty ? t.EmptyCabWeight : 1;
        }, c =>
        {
            c.Add(i => TrackDoll.Ahead(i, c.Train, c.Tuning.TrackDoll.SpawnAhead, c.Tuning.TrackDoll));
            return true;
        }),
        // B.3 · Cinder Hounds: behind the train, after sustained speed; ×1.5 with the boiler hot (×2.5 livestock, ×2 food:
        // the cargo weights).
        new(EnemyKind.CinderHound, c =>
        {
            var h = c.Tuning.CinderHounds;
            if (c.Train.Dynamics.Speed < h.MinTrainSpeed || c.Train.Dynamics.Consist.CarCount < 1 || !c.None(EnemyKind.CinderHound))
                return null;
            return c.Train.BoilerTuning is { } b && c.Train.Boiler.Pressure > b.WorkingBandMax ? c.Director.Tuning.HoundsHotBoilerWeight : 1;
        }, c =>
        {
            var t = c.Tuning.CinderHounds;
            int pack = c.NextId;
            int size = (int)Math.Round(c.Director.NextRange(t.PackSize[0], t.PackSize[1] + 0.49));
            for (int i = 0; i < size; i++)
            {
                int n = i;
                c.Add(id => new CinderHound(id, pack)
                {
                    LineDistance = c.Train.Dynamics.RearDistance - t.SpawnBehind - n * 6,
                    Lateral = (n % 2 == 0 ? 1 : -1) * c.Director.NextRange(3, 6),
                    Height = 0.6,
                    Health = t.Health,
                });
            }
            return true;
        }),
        // B.3 · Car Hugger: marsh, water crossings, low ground; a train of two or more; never on a grade; weight up at low speed.
        new(EnemyKind.CarHugger, c =>
        {
            var t = c.Tuning.CarHugger;
            if (c.Train.Dynamics.Consist.CarCount < t.MinCars || !c.None(EnemyKind.CarHugger) || CarHugger.Spot(c.World, t) is null)
                return null;
            return c.Train.Dynamics.Speed < t.LowSpeed ? t.LowSpeedWeight : 1;
        }, c =>
        {
            if (CarHugger.Spot(c.World, c.Tuning.CarHugger) is not { } at)
                return false;
            c.Add(i => CarHugger.Lurking(i, at, c.Director.NextRange(0, 1) < 0.5 ? -1 : 1, c.Tuning.CarHugger));
            return true;
        }),
        // B.4 · Climbers: at least two coupling gaps and a minimum speed; weight scales with the gap count.
        new(EnemyKind.Climber, c =>
        {
            var t = c.Tuning.Climbers;
            var gaps = CrewSense.Gaps(c.Train);
            if (gaps.Count < t.MinGaps || c.Train.Dynamics.Speed < t.MinSpeed || !c.None(EnemyKind.Climber))
                return null;
            return t.PerGapWeight * gaps.Count;
        }, c =>
        {
            var t = c.Tuning.Climbers;
            var gaps = CrewSense.Gaps(c.Train);
            if (gaps.Count == 0)
                return false;
            int gap = gaps[(int)c.Director.NextRange(0, gaps.Count - 1e-9)];
            int side = c.Director.NextRange(0, 1) < 0.5 ? -1 : 1;
            int size = Math.Max(1, (int)Math.Round(c.Director.NextRange(t.PackSize[0], t.PackSize[1] + 0.49)));
            int pack = c.NextId;
            for (int i = 0; i < size; i++)
                c.Add(id => new Climber(id)
                {
                    Extra = gap,
                    Extra2 = side,
                    Health = t.Health,
                    Pack = pack,
                    LineDistance = Climber.GapAlong(c.Train, gap) - 2 * i,
                    Lateral = side * (c.Train.Frames[gap].Shape.HalfWidth + t.PaceOut + i * 0.8),
                });
            return true;
        }),
        // B.4 · Draggers: a train of two or more, dormant until someone's on the roofs; weight up per roof walker.
        new(EnemyKind.Dragger, c =>
        {
            var t = c.Tuning.Draggers;
            int onRoofs = c.Living.Count(p => p.State.Surface == Surface.Roof && p.State.Parent > 0);
            if (c.Train.Dynamics.Consist.CarCount < t.MinCars || onRoofs == 0
                || c.World.ActiveEnemies.Count(e => !e.Gone && e.Kind == EnemyKind.Dragger) >= t.MaxAttached)
                return null;
            return onRoofs;
        }, c =>
        {
            var walked = c.Living.Where(p => p.State.Surface == Surface.Roof && p.State.Parent > 0 && p.State.Parent < c.Train.Frames.Count).ToList();
            if (walked.Count == 0)
                return false;
            var on = walked[(int)c.Director.NextRange(0, walked.Count - 1e-9)].State;
            int side = c.Director.NextRange(0, 1) < 0.5 ? -1 : 1;
            c.Add(i => Dragger.Under(i, c.Train, on.Parent, side, on.Position.Z + c.Director.NextRange(-4, 4)));
            return true;
        }),
        // B.4 · Whistler: boards at any stop, into a coupling gap; a train of two or more; weight up on routes with more stops.
        new(EnemyKind.Whistler, c =>
        {
            var t = c.Tuning.Whistler;
            if (c.Train.Dynamics.Consist.CarCount < t.MinCars || !c.Stopped || !c.None(EnemyKind.Whistler) || CrewSense.Gaps(c.Train).Count == 0)
                return null;
            int stops = c.World.Route?.Of(FeatureKind.Facility).Count() ?? 0;
            return 1 + t.StopWeight * stops;
        }, c =>
        {
            var gaps = CrewSense.Gaps(c.Train);
            // A gap nobody's standing in.
            var free = gaps.Where(g => !c.Living.Any(p => CrewSense.InGap(PlayerMotor.WorldPosition(p.State, c.Train), c.Train, g))).ToList();
            if (free.Count == 0)
                return false;
            int gap = free[(int)c.Director.NextRange(0, free.Count - 1e-9)];
            c.Add(i => Whistler.InGap(i, c.Train, gap, c.Tuning.Whistler));
            return true;
        }),
        // B.5 · Tippy Toesie: any car or the ground near the train; crew of two or more; weight up per player idle and alone.
        new(EnemyKind.TippyToesie, c =>
        {
            var t = c.Tuning.TippyToesie;
            if (c.Crew < t.MinCrew || !c.None(EnemyKind.TippyToesie))
                return null;
            int idle = c.Living.Count(p => c.World.IdleSeconds.GetValueOrDefault(p.Id) >= t.IdleSeconds
                && !c.Living.Any(o => o.Id != p.Id && (PlayerMotor.WorldPosition(o.State, c.Train) - PlayerMotor.WorldPosition(p.State, c.Train)).Length <= t.AloneRadius));
            return idle == 0 ? null : t.PerIdleWeight * idle;
        }, c =>
        {
            c.Add(i => TippyToesie.Hiding(i, c.Tuning.TippyToesie));
            return true;
        }),
        // B.5 · Fire Flies: lineside in dark forest and open sections, at least one lamp lit inside a car; ×2 at night depth,
        // ×0 with every car lamp out.
        new(EnemyKind.FireFlies, c =>
        {
            var t = c.Tuning.FireFlies;
            var lit = LitCars(c);
            if (lit.Count == 0 || c.AtFacility || c.World.ActiveEnemies.Count(e => !e.Gone && e.Kind == EnemyKind.FireFlies) >= 1
                || c.World.Route?.Features.Any(f => f.Kind is FeatureKind.Tunnel && f.Contains(c.Front)) == true)
                return null;
            return c.World.Route is { } r && c.Front > r.Length * 0.5 ? t.DepthWeight : 1;
        }, c =>
        {
            var lit = LitCars(c);
            if (lit.Count == 0)
                return false;
            int car = lit[(int)c.Director.NextRange(0, lit.Count - 1e-9)];
            c.Add(i => FireFlies.OnLamp(i, c.Train, car));
            return true;
        }),
        // B.6 · Ribbits: yards and villages; the pack capped at the crew's size; weight up per player on the ground.
        new(EnemyKind.Ribbit, c =>
        {
            var t = c.Tuning.Ribbits;
            int ground = c.OnGround.Count();
            if (ground == 0 || c.Crew < 2 || !c.None(EnemyKind.Ribbit) || !(c.AtFacility || c.World.InSettlement))
                return null;
            return 1 + t.PerGroundWeight * ground;
        }, c =>
        {
            var t = c.Tuning.Ribbits;
            if (c.GroundCentre() is not { } centre)
                return false;
            int size = Math.Min(c.Crew, (int)Math.Round(c.Director.NextRange(t.PackSize[0], t.PackSize[1] + 0.49)));
            var at = c.Out(centre, t.SpawnOut);
            int pack = c.NextId;
            for (int i = 0; i < size; i++)
            {
                var spot = at + new Double3(i * 1.2, 0, (i % 2) * 1.2);
                c.Add(id => Ribbit.At(id, pack, spot, t));
            }
            return true;
        }),
        // B.6 · The Gaunt: asleep in villages and yards, the Frontier and on, once a run; weight up on long facility stops.
        new(EnemyKind.Gaunt, c =>
        {
            var t = c.Tuning.Gaunt;
            if (c.Tier < c.Director.Gate(c.World, RouteTier.Frontier) || c.Crew < t.MinCrew || !c.Once(EnemyKind.Gaunt)
                || !(c.AtFacility || c.World.InSettlement) || !c.OnGround.Any())
                return null;
            return c.World.Run?.StopSeconds >= t.LongStopSeconds ? t.LongStopWeight : 1;
        }, c =>
        {
            if (c.GroundCentre() is not { } centre)
                return false;
            var at = c.Out(centre, c.Tuning.Gaunt.SpawnOut);
            c.Add(i => Gaunt.Asleep(i, at, c.Tuning.Gaunt));
            return true;
        }),
        // B.6 · Followers: facility grounds, latching onto a disembarked player (an excursion); weight up per extra on the ground.
        new(EnemyKind.Follower, c =>
        {
            var t = c.Tuning.Followers;
            var free = Follower.Excursions(c.World);
            if (free.Count == 0 || !c.AtFacility || c.World.ActiveEnemies.Count(e => !e.Gone && e.Kind == EnemyKind.Follower) >= t.MaxActive)
                return null;
            return 1 + t.PerGroundWeight * (c.OnGround.Count() - 1);
        }, c =>
        {
            var free = Follower.Excursions(c.World);
            if (free.Count == 0)
                return false;
            int on = free[(int)c.Director.NextRange(0, free.Count - 1e-9)];
            var s = c.World.CrewThisTick.First(p => p.Id == on).State;
            c.Add(i => Follower.On(i, c.Train, s, on, c.Tuning.Followers));
            return true;
        }),
        // B.6 · Soot Children: near facilities and dead settlements; crew of two or more; a true 50/50 with a real child (a
        // host's first-ever call always real).
        new(EnemyKind.SootChildren, c =>
        {
            var t = c.Tuning.SootChildren;
            if (c.Crew < t.MinCrew || !c.None(EnemyKind.SootChildren) || !c.OnGround.Any() || !(c.AtFacility || c.World.InSettlement))
                return null;
            return 1;
        }, c =>
        {
            var t = c.Tuning.SootChildren;
            if (c.GroundCentre() is not { } centre)
                return false;
            bool soot = !c.World.NextChildReal && c.Director.NextRange(0, 1) >= t.RealChance;
            c.World.NextChildReal = false;
            c.World.ChildCalled = true;
            var at = c.Out(centre, t.CallOut);
            c.Add(i => SootChildren.Calls(i, at, soot, t));
            return true;
        }),
        // B.8 · The Passenger: boards during a facility stop; the Dead lines and on (comet relaxes it a tier); crew of three or
        // more (a crowd to hide in); once a run; weight up with the crew split across tasks.
        new(EnemyKind.Passenger, c =>
        {
            var t = c.Tuning.Passenger;
            if (c.Tier < c.Director.Gate(c.World, RouteTier.DeadLines) || c.Crew < t.MinCrew || !c.AtFacility || !c.Once(EnemyKind.Passenger)
                || Passenger.Car(c.Train) is null)
                return null;
            int places = c.Living.Select(p => p.State.Parent).Distinct().Count();
            return places >= t.SplitPlaces ? t.SplitWeight : 1;
        }, c =>
        {
            if (Passenger.Car(c.Train) is not { } car)
                return false;
            var faces = c.Living.Select(p => p.Id).ToList();
            if (faces.Count == 0)
                return false;
            int looks = faces[(int)c.Director.NextRange(0, faces.Count - 1e-9)];
            c.Add(i => Passenger.Boards(i, c.Train, car, looks, c.Tuning.Passenger));
            return true;
        }),
        // B.8 · The Switchman: the junction network ahead; the Frontier and on; a route with three or more junctions; weight up
        // with dead-line branches available.
        new(EnemyKind.Switchman, c =>
        {
            var t = c.Tuning.Switchman;
            if (c.Tier < RouteTier.Frontier || c.World.Route is not { } r || r.Of(FeatureKind.Junction).Count() < t.MinJunctions
                || c.World.Switches is null || !c.None(EnemyKind.Switchman) || Switchman.Junction(c.World, t) is null)
                return null;
            return c.Train.Line.Branches.Any(b => b.Kind == Rail.BranchKind.DeadLine) ? t.DeadLineWeight : 1;
        }, c =>
        {
            if (Switchman.Junction(c.World, c.Tuning.Switchman) is not { } junction)
                return false;
            bool derail = c.Director.NextRange(0, 1) < c.Tuning.Switchman.DerailChance;
            c.Add(i => Switchman.At(i, junction, c.Tuning.Switchman, c.World.Switches?.Tuning.LeverOffset ?? 2.6, derail));
            return true;
        }),
        // B.8 · The Grumbler: facilities with a crane and crates (the Foundry first); any tier; ×2 with food at the facility.
        new(EnemyKind.Grumbler, c =>
        {
            if (!c.AtFacility || c.World.Run?.CurrentSite?.Crane is not { } crane || crane.Left == 0 || !c.None(EnemyKind.Grumbler))
                return null;
            return Director.Aboard(c.World).Contains(CargoKind.Food) ? c.Tuning.Grumbler.FoodWeight : 1;
        }, c =>
        {
            if (c.World.Run?.CurrentSite?.Crane is not { } crane)
                return false;
            var stacked = Enumerable.Range(0, crane.Castings.Length).Where(i => crane.Castings[i].State == CastingState.Stacked).ToList();
            if (stacked.Count == 0)
                return false;
            int on = stacked[(int)c.Director.NextRange(0, stacked.Count - 1e-9)];
            var at = crane.Castings[on].At;
            c.Add(i => Grumbler.OnCrates(i, at + Double3.Up * 0.8, on, c.Tuning.Grumbler));
            return true;
        }),
        // B.6 · The Shy Thing (v1.5): at a stop, out in the dark where someone on the ground is looking; any tier; weight up
        // per player on the ground.
        new(EnemyKind.ShyThing, c =>
        {
            int ground = c.OnGround.Count();
            if (!c.AtStop || ground == 0 || !c.None(EnemyKind.ShyThing))
                return null;
            return 1 + 0.5 * (ground - 1);
        }, c =>
        {
            var t = c.Tuning.ShyThing;
            var ground = c.OnGround.ToList();
            if (ground.Count == 0)
                return false;
            var on = ground[(int)c.Director.NextRange(0, ground.Count - 1e-9)].State;
            if (ShyThing.Spot(c.Train, on, c.Director.NextRange(t.SpawnOut[0], t.SpawnOut[1])) is not { } at)
                return false;
            c.Add(i => ShyThing.Waiting(i, at, t));
            return true;
        }),
        // B.6 · The Huddle (v1.5): at a stop, the crew on the ground; any tier.
        new(EnemyKind.Huddle, c => c.AtStop && c.OnGround.Any() && c.None(EnemyKind.Huddle) ? 1 : null, c =>
        {
            var t = c.Tuning.Huddle;
            if (c.GroundCentre() is not { } centre)
                return false;
            int size = Math.Max(1, (int)Math.Round(c.Director.NextRange(t.FlockSize[0], t.FlockSize[1] + 0.49)));
            var at = c.Out(centre, t.SpawnOut);
            c.Add(i => Huddle.Flock(i, at, size));
            return true;
        }),
        // B.6 · The Mimic (v1.5): at a stop whose crates are out, one more than a stack's count; any tier.
        new(EnemyKind.Mimic, c => c.AtStop && c.None(EnemyKind.Mimic) && c.World.Run?.MimicSlots(c.Front).Count > 0 ? 1 : null, c =>
        {
            var slots = c.World.Run?.MimicSlots(c.Front) ?? [];
            if (slots.Count == 0)
                return false;
            var (at, hint, cargo) = slots[(int)c.Director.NextRange(0, slots.Count - 1e-9)];
            var body = c.World.Bodies.SpawnCargo(at, hint, cargo: cargo);
            c.Add(i => Mimic.As(i, body, c.Tuning.Mimic));
            return true;
        }),
    ];

    /// <summary>Cars in the engine's rake with a room and their lamp lit (the Fire Flies' light).</summary>
    static List<int> LitCars(SpawnContext c) =>
        [.. c.Train.Dynamics.Consist.Vehicles.Where(v => v.Id > 0 && v.LampLit && c.Train.Frames[v.Id].Shape.Interior is not null).Select(v => v.Id)];

    public static SpawnRule? For(EnemyKind kind) => Rules.FirstOrDefault(r => r.Kind == kind);
}
