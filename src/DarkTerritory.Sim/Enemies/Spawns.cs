using Ballast;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;
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
    public RouteTier Tier => World.Route?.Tier ?? RouteTier.Frontier;
    /// <summary>
    /// Once a run: not drawn yet tonight, or drawn and driven off rather than killed (note 288: driven off, it can come back),
    /// and none about now.
    /// </summary>
    public bool Once(EnemyKind kind) => (World.DrivenOff.Contains(kind) || !Director.Log.Any(l => l.Kind == kind)) && !World.ActiveEnemies.Any(e => !e.Gone && e.Kind == kind);
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
    /// <summary>
    /// Where the stops' layouts say this kind of creature lives (level-design H.2; GDD B.6; note 309): every site of
    /// <paramref name="kind"/> at the generated stops round the train, in the world, on the ground there. None on a hand-laid
    /// route, whose stops have no layouts.
    /// </summary>
    public List<(Double3 At, double Radius)> Sites(LairKind kind) => CreatureSites.Of(World, kind, Tuning.Sites.Around);

    /// <summary>
    /// The site of <paramref name="kind"/> nearest <paramref name="centre"/>, if it's within <paramref name="reach"/> and no
    /// closer than <c>sites.minOut</c> (nothing comes out from under the crew's feet). Null when there is none that near.
    /// </summary>
    public Double3? NearestSite(LairKind kind, Double3 centre, double reach)
    {
        double Flat(Double3 a) => Math.Sqrt((a.X - centre.X) * (a.X - centre.X) + (a.Z - centre.Z) * (a.Z - centre.Z));
        var best = Sites(kind).Select(s => s.At).Where(a => Flat(a) >= Tuning.Sites.MinOut && Flat(a) <= reach)
            .OrderBy(Flat).Select(a => (Double3?)a).FirstOrDefault();
        return best;
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
        // B.4 · Climbers: at least two coupling gaps and a minimum speed; weight scales with the gap count. Boarding-first
        // (GDD App. F.1, note 286): they get a grip only on a slow train, so at speed they come rarely (and pace it, waiting
        // for it to slow), and slow on a tight bend more often.
        new(EnemyKind.Climber, c =>
        {
            var t = c.Tuning.Climbers;
            var gaps = CrewSense.Gaps(c.Train);
            double v = c.Train.Dynamics.Speed;
            if (gaps.Count < t.MinGaps || v < t.MinSpeed || !c.None(EnemyKind.Climber))
                return null;
            double w = t.PerGapWeight * gaps.Count;
            if (v >= t.MountBelow)
                return w * t.AtSpeedWeight;
            return Boarding.OnTightBend(c.Train, t.TightBendRadius, t.BendAheadM) ? w * t.BendWeight : w;
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
        // Boarding-first (GDD App. F.1, note 286): they get under a car only while the train's slow (a stop, a tight bend),
        // and wait there for someone to walk its roof; on a train at speed they can't get on at all.
        new(EnemyKind.Dragger, c =>
        {
            var t = c.Tuning.Draggers;
            int onRoofs = c.Living.Count(p => p.State.Surface == Surface.Roof && p.State.Parent > 0);
            bool boardsSlow = t.BoardBelow < double.MaxValue;
            if (c.Train.Dynamics.Consist.CarCount < t.MinCars || !boardsSlow && onRoofs == 0
                || boardsSlow && c.Train.Dynamics.Speed >= t.BoardBelow
                || c.World.ActiveEnemies.Count(e => !e.Gone && e.Kind == EnemyKind.Dragger) >= t.MaxAttached)
                return null;
            if (!boardsSlow)
                return onRoofs;
            var climbers = c.Tuning.Climbers;
            return (1 + onRoofs) * (Boarding.OnTightBend(c.Train, climbers.TightBendRadius, climbers.BendAheadM) ? t.BendWeight : 1);
        }, c =>
        {
            var walked = c.Living.Where(p => p.State.Surface == Surface.Roof && p.State.Parent > 0 && p.State.Parent < c.Train.Frames.Count).ToList();
            int side = c.Director.NextRange(0, 1) < 0.5 ? -1 : 1;
            if (walked.Count == 0)
            {
                if (c.Tuning.Draggers.BoardBelow == double.MaxValue)
                    return false;
                // Nobody up yet: under any car of the rake, to wait for whoever walks it.
                var cars = c.Train.Dynamics.Consist.Vehicles.Where(v => v.Id > 0).Select(v => v.Id).ToList();
                if (cars.Count == 0)
                    return false;
                int car = cars[(int)c.Director.NextRange(0, cars.Count - 1e-9)];
                double along = c.Director.NextRange(-0.4, 0.4) * c.Train.Frames[car].Shape.HalfLength * 2;
                c.Add(i => Dragger.Under(i, c.Train, car, side, along));
                return true;
            }
            var on = walked[(int)c.Director.NextRange(0, walked.Count - 1e-9)].State;
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
            // Boarding-first (GDD App. F.1, note 286): it slips aboard at a stop, and stays hidden aboard after.
            if (c.Crew < t.MinCrew || !c.None(EnemyKind.TippyToesie) || c.Train.Dynamics.Speed >= t.BoardBelow)
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
        // ×0 with every car lamp out. Only to a stopped train, and rarely (note 269).
        new(EnemyKind.FireFlies, c =>
        {
            var t = c.Tuning.FireFlies;
            var lit = LitCars(c);
            // GDD App. F, 6 Oct 2026 (note 269): "their pull on Fire Flies is rare, and only while the car is stopped".
            if (lit.Count == 0 || c.AtFacility || c.Train.Dynamics.Speed >= t.StoppedBelow
                || c.World.ActiveEnemies.Count(e => !e.Gone && e.Kind == EnemyKind.FireFlies) >= 1
                || c.World.Route?.Features.Any(f => f.Kind is FeatureKind.Tunnel && f.Contains(c.Front)) == true)
                return null;
            return t.StoppedWeight * (c.World.Route is { } r && c.Front > r.Length * 0.5 ? t.DepthWeight : 1);
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
            // Out of their warren (level-design H.2, note 309): the one nearest the ground crew. A stop with warrens and none
            // near has none to come; one without a layout sends them from out in the dark, as before.
            Double3 at;
            if (c.NearestSite(LairKind.Warren, centre, c.Tuning.Sites.WarrenReach) is { } warren)
                at = warren;
            else if (c.Sites(LairKind.Warren).Count > 0)
                return false;
            else
                at = c.Out(centre, t.SpawnOut);
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
            // Asleep in its roost (level-design H.2, note 309): the building furthest from the train on foot. You go to it.
            var at = c.NearestSite(LairKind.GauntRoost, centre, c.Tuning.Sites.RoostReach) is { } roost ? roost : c.Out(centre, c.Tuning.Gaunt.SpawnOut);
            c.Add(i => Gaunt.Asleep(i, at, c.Tuning.Gaunt));
            return true;
        }),
        // B.6 · The Moose (note 339): grazing beside a stop, in every tier and more the harder; none where one's already
        // about; by the line's biome; weight up per player on the ground.
        new(EnemyKind.Moose, c =>
        {
            var t = c.Tuning.Moose;
            if (c.Crew < t.MinCrew || !c.None(EnemyKind.Moose) || !c.Stopped || !(c.AtFacility || c.World.InSettlement))
                return null;
            double biome = Moose.BiomeWeight(c.World, t, c.Front);
            if (biome <= 0)
                return null;
            return MooseTuning.ByTier(t.TierWeights, c.Tier) * biome * (1 + t.PerGroundWeight * c.OnGround.Count());
        }, c =>
        {
            var t = c.Tuning.Moose;
            // Only to a stopped train (an insisted night places it without the weight's gates): it's the stop's.
            if (!c.Stopped)
                return false;
            var mid = c.Train.Line.Sample(c.Train.Dynamics.Path, c.Front - c.Train.Dynamics.Consist.LengthMetres * 0.5);
            var right = Double3.Cross(mid.Tangent, Double3.Up).Normalized;
            // Its ground: beside the stop, out from the consist's middle on either side, wherever a moose can stand.
            for (int i = 0; i < 16; i++)
            {
                double side = c.Director.NextRange(0, 1) < 0.5 ? -1 : 1;
                double along = c.Director.NextRange(-0.8, 0.8);
                double out_ = c.Director.NextRange(t.GroundAt[0], t.GroundAt[1]);
                var dir = (right * side + mid.Tangent * along).Normalized;
                var at = mid.Position + dir * out_;
                if (Moose.Place(c.World, t, at, c.Front) is not { } spot)
                    continue;
                double yaw = c.Director.NextRange(-Math.PI, Math.PI);
                c.Add(id => Moose.Grazing(id, spot, c.Front, yaw));
                return true;
            }
            return false;
        }),
        // B.4 · The Gannet (note 340; the orchestrator's S3): over a train run fast a while in open country (not a tunnel), a
        // train of two or more; every tier, more the harder; by the line's biome; up per walker on the roofs. Once a run:
        // it peels off and comes back on its own, until it's killed or gives up.
        new(EnemyKind.Gannet, c =>
        {
            var t = c.Tuning.Gannet;
            if (!c.Once(EnemyKind.Gannet) || c.Train.Dynamics.Consist.CarCount < t.MinCars || c.Train.Dynamics.Speed < t.ArriveAbove
                || c.World.FastSeconds < t.ArriveSeconds
                || c.World.Route?.Features.Any(f => f.Kind == FeatureKind.Tunnel && f.Contains(c.Front)) == true)
                return null;
            double biome = c.World.Route?.Plan?.Biomes is { Count: > 0 } biomes
                ? t.BiomeWeights.GetValueOrDefault((biomes.FirstOrDefault(b => b.Edge == "main" && b.S0 <= c.Front && c.Front < b.S1) ?? biomes[^1]).Biome, 1)
                : 1;
            if (biome <= 0)
                return null;
            int roofs = c.Living.Count(p => p.State.Surface == Surface.Roof && p.State.Parent >= 0);
            return MooseTuning.ByTier(t.TierWeights, c.Tier) * biome * (1 + t.PerRoofWeight * roofs);
        }, c =>
        {
            var t = c.Tuning.Gannet;
            double height = c.Director.NextRange(t.SoarHeight[0], t.SoarHeight[1]);
            c.Add(i => Gannet.Arriving(i, c.Train, t, height));
            return true;
        }),
        // B.6 · Followers: facility grounds, latching onto a disembarked player (an excursion); weight up per extra on the ground.
        new(EnemyKind.Follower, c =>
        {
            var t = c.Tuning.Followers;
            var free = OnFollowerGround(c, Follower.Excursions(c.World));
            if (free.Count == 0 || !c.AtFacility || c.World.ActiveEnemies.Count(e => !e.Gone && e.Kind == EnemyKind.Follower) >= t.MaxActive)
                return null;
            return 1 + t.PerGroundWeight * (c.OnGround.Count() - 1);
        }, c =>
        {
            var free = OnFollowerGround(c, Follower.Excursions(c.World));
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
            // From the open beyond the stop's built edge, where the train can see it (level-design H.2, note 309).
            var at = c.NearestSite(LairKind.SootCall, centre, c.Tuning.Sites.CallReach) is { } call ? call : c.Out(centre, t.CallOut);
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
            // The director's decision of 7 Oct 2026 (note 286): it never throws points under the train unless a mod asks it to
            // (the draw is kept either way, so the director's dice run as they did).
            bool derail = c.Director.NextRange(0, 1) < c.Tuning.Switchman.DerailChance && c.Tuning.Switchman.ThrowsUnderTrain;
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
    ];

    /// <summary>Cars in the engine's rake with a room and their lamp lit (the Fire Flies' light).
    /// With <see cref="FireFliesTuning.ShutCarKeepsOut"/>, only those with a way in: a door or the hatch open, or a breach
    /// (boarding-first, note 286: a car shut up tight keeps them off its lamp).</summary>
    static List<int> LitCars(SpawnContext c) =>
        [.. c.Train.Dynamics.Consist.Vehicles.Where(v => v.Id > 0 && v.LampLit && c.Train.Frames[v.Id].Shape.Interior is not null
            && !(c.Tuning.FireFlies.ShutCarKeepsOut && Boarding.ShutUp(c.Train, v.Id))).Select(v => v.Id)];

    /// <summary>
    /// Followers ride the facility's grounds (level-design H.2, note 309): of the crew on an excursion, those standing in one of
    /// the stop's follower grounds (its radius and <c>sites.groundMargin</c>). All of them where the stop has no layout.
    /// </summary>
    static List<int> OnFollowerGround(SpawnContext c, List<int> free)
    {
        var grounds = c.Sites(LairKind.FollowerGround);
        if (grounds.Count == 0)
            return free;
        return [.. free.Where(id =>
        {
            var at = PlayerMotor.WorldPosition(c.World.CrewThisTick.First(p => p.Id == id).State, c.Train);
            return grounds.Any(g => Math.Sqrt((at.X - g.At.X) * (at.X - g.At.X) + (at.Z - g.At.Z) * (at.Z - g.At.Z)) <= g.Radius + c.Tuning.Sites.GroundMargin);
        })];
    }

    public static SpawnRule? For(EnemyKind kind) => Rules.FirstOrDefault(r => r.Kind == kind);
}
