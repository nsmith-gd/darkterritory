using Ballast;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// The creature gym's calls (GDD §34.1, ARCHITECTURE §8 note 676; a developer build's, through <see cref="World.Call"/>): one
/// of a kind put into the world now by its own spawn rule, gates aside, and kept off whoever called it. Nothing on a real
/// night comes this way.
/// </summary>
public static class Calls
{
    /// <summary>The crew a call is made as: packs and groups sized for a crew, not for the one who called (a Ribbit pack of one never goes for a lone player).</summary>
    public const int AsCrew = 4;

    /// <summary>
    /// Puts one of <paramref name="kind"/> in by its own rule: the director's table's <see cref="SpawnRule.Spawn"/>, called
    /// from where you stand as if you were the ground crew; or the path its own condition takes on a night (the Stoker at
    /// the tender, the marsh under the cars, the Choir gathering, the house creatures in their houses). Where the rule needs
    /// a prop the stop hasn't got and the gym can supply it, it does (a crate for the Freight Beetle and the Pickers, a body
    /// for the Mourners, a car's lamp lit for the Fire Flies). Returns how it came in a few words, or why it couldn't (null:
    /// by its own rule, as the director sends it).
    /// </summary>
    public static string? Put(World world, EnemyKind kind, Double3 you, double min, double max, EnemyTuning t, Director d)
    {
        var train = world.Train;
        var ctx = new SpawnContext(world, t, d) { CalledAt = you, CrewAs = AsCrew };
        switch (kind)
        {
            case EnemyKind.Stoker:
                if (train.BoilerTuning is null || train.Boiler.Ruptured)
                    return "no fire to come to: the boiler's out";
                world.CallStoker(t);
                return "boarding at the tender, for the firebox";
            case EnemyKind.Drift:
                if (Drift.Ground(world, t.Drift) is null)
                {
                    double front = train.Dynamics.Distance;
                    var marsh = world.Route?.Of(Route.FeatureKind.Marsh).OrderBy(f => Math.Abs(f.Start - front)).FirstOrDefault();
                    return marsh is null ? "it's the marsh's own ground, and this line has no marsh"
                        : $"it's the marsh's own ground: stand the train on the marsh {Math.Abs(marsh.Start - front) / 1000:0.0} km {(marsh.Start > front ? "ahead" : "back")}";
                }
                world.CallDrift(t);
                return "the marsh coming up under a car";
            case EnemyKind.Sleepers:
                // Track debris on the main line ahead (§22): past the lamp's reveal, for the train to run onto.
                world.AddEnemy(i => new Sleepers(i) { LineDistance = train.Dynamics.Distance + Math.Min(max - Inside, t.Sleepers.LampRevealDistance + 30), Height = 0.2 });
                return "debris on the line ahead: drive onto it";
            case EnemyKind.Choir:
                return world.CallChoir() ? "gathering: it comes within 20 s, and it hears you" : "this night has no guns or loudness meter";
            case EnemyKind.CarFire:
                {
                    int car = FarthestCar(world, you, interior: true);
                    if (car < 0)
                        return "no car with a room to burn";
                    world.AddEnemy(i => CarFire.In(i, train, car, 0, t.CarFire, FireCause.Called));
                    return $"car {car} alight";
                }
            case EnemyKind.Waker:
                world.AddEnemy(i => Waker.Rise(i, train.Dynamics.RearDistance, 0, t.Wakers));
                return "rising far behind the train: it runs it down";
            case EnemyKind.Mourners:
                {
                    var body = Lying(world) ?? StandIn(world, you, Math.Clamp(min, 0, max));
                    world.CallMourners(body, t.Mourners);
                    return "come for a body lying off the train";
                }
            case EnemyKind.Dave:
                {
                    var (at, hint, yaw) = Out(world, you, Math.Clamp(min, 0, max), t.Dave.TrackClearance);
                    world.AddEnemy(i => Dave.At(i, at, hint, yaw, t.Dave));
                    return "at his easel";
                }
            case EnemyKind.Jacob:
                {
                    var (at, hint, yaw) = Out(world, you, Math.Clamp(min, 0, max), t.Jacob.TrackClearance);
                    world.AddEnemy(i => Jacob.At(i, at, hint, yaw));
                    return "fishing (no water here: on the bank of nothing)";
                }
            case EnemyKind.Lodger or EnemyKind.Householder or EnemyKind.HollowHouse or EnemyKind.Hanger:
                return House(world, kind, you, max, t);
            // What stands where its rule finds it is taken only within the gym's reach (the director, 13 Oct: "nothing should
            // spawn more than 200m off").
            case EnemyKind.TrackDoll:
                {
                    // On the rail ahead, inside the reach: she shows in the headlamp at its reveal distance, and a train standing
                    // when she does is her answer (she goes), so she's met moving.
                    double hint = train.Dynamics.Distance;
                    var (_, mine) = train.Line.Nearest(you, ref hint);
                    double ahead = Math.Max(20, max - Inside - (train.Dynamics.Distance - mine));
                    world.AddEnemy(i => TrackDoll.Ahead(i, train, ahead, t.TrackDoll));
                    return train.Dynamics.Speed < 0.3 ? "on the rail ahead: a standing train answers her as she shows, so be moving when you call her" : "on the rail ahead";
                }
            // What couldn't be tried at a standing train (the director's run-through, 13 Oct, GDD App. F.5) comes so it can be.
            case EnemyKind.CinderHound:
                {
                    // Close behind the rear car, to come up and board it: a standing train's pack runs only at its closing
                    // speed, so from the rule's 180 m back it never got there.
                    var h = t.CinderHounds;
                    int pack = world.NextEnemyId;
                    int size = Math.Clamp(AsCrew, h.PackSize[0], h.PackSize[^1]);
                    double rear = train.Dynamics.RearDistance;
                    for (int n = 0; n < size; n++)
                    {
                        int k = n;
                        world.AddEnemy(i => new CinderHound(i, pack)
                        {
                            LineDistance = rear - min - k * 6,
                            Lateral = (k % 2 == 0 ? 1 : -1) * (3 + k % 3),
                            Height = 0.6,
                            Health = h.Health,
                        });
                    }
                    return "close behind the rear car: they board it, and go for whoever's out on the train (climb up to meet them)";
                }
            case EnemyKind.TippyToesie:
                world.AddEnemy(i => TippyToesie.Hiding(i, t.TippyToesie));
                return $"hidden: stand still and alone {t.TippyToesie.IdleSeconds:0} s and it comes up behind you; turn and it runs";
            case EnemyKind.Dragger:
                {
                    // Under the roof's edge of the car nearest you, on your side, along where you are: the panel names it, as
                    // it waits unseen for whoever walks that edge.
                    int car = NearestCar(world, you);
                    if (car < 0)
                        return "no car for it to wait under";
                    var shape = train.Frames[car].Shape;
                    var local = train.Frames[car].ToLocal(you);
                    int side = local.X < 0 ? -1 : 1;
                    world.AddEnemy(i => Dragger.Under(i, train, car, side, Math.Clamp(local.Z, -shape.HalfLength, shape.HalfLength)));
                    return $"waiting under car {car}'s {(side < 0 ? "left" : "right")} roof edge, unseen: walk that roof along that edge";
                }
            case EnemyKind.TowerJaw:
                return Tower(world, you, max, t);
            case EnemyKind.Grumbler:
                return Castings(world, you, max, t);
            case EnemyKind.MouseMoose:
                return MouseMooseCall(world, you, max, t);
            case EnemyKind.Bruin:
                return BruinCall(world, you, t);
            case EnemyKind.Tapper:
                return TapperCall(world, t);
            case EnemyKind.Pickers:
                {
                    var crates = Crates(world, you, min, max, 3);
                    // Their drains at the stop's sheds when the train's in its yard; out on the ground past the crates when it isn't.
                    var drains = world.Run?.FacilityFeature is not null ? Picking.Drains(world, t.Pickers) : [];
                    if (drains.Count == 0)
                    {
                        var (at, hint, yaw) = Out(world, crates, t.Pickers.FacilityReach * 0.2, 4);
                        drains = [(at, hint, yaw)];
                    }
                    int count = t.Pickers.CountFor(world.Route?.Tier ?? Route.RouteTier.Local, AsCrew);
                    for (int n = 0; n < count && drains.Count > 0; n++)
                    {
                        int k = n % drains.Count;
                        var (at, hint, yaw) = drains[k];
                        world.AddEnemy(i => Picker.Up(i, at, k, hint, yaw, t.Pickers));
                    }
                    return "up out of the drains, for the crates set down";
                }
            case EnemyKind.FreightBeetle:
                if (FreightBeetle.Freight(world, t.FreightBeetle).Count == 0)
                    Crates(world, you, min, max, 1);
                break;
            case EnemyKind.FireFlies:
                // Drawn to a lit car with a way in (note 286): the car farthest from you, its lamp lit and its door left open.
                if (!train.Dynamics.Consist.Vehicles.Any(v => v.Id > 0 && v.LampLit && train.Frames[v.Id].Shape.Interior is not null && !(t.FireFlies.ShutCarKeepsOut && Boarding.ShutUp(train, v.Id))))
                {
                    int car = FarthestCar(world, you, interior: true);
                    if (car < 0)
                        return "no car with a room and a lamp";
                    var v = train.Dynamics.Consist.Vehicles.First(v => v.Id == car);
                    v.LampLit = true;
                    world.AddEnemy(i => FireFlies.OnLamp(i, train, car));
                    return $"on car {car}'s lamp";
                }
                break;
        }
        if (Spawns.For(kind) is not { } rule)
            return "it has no spawn of its own";
        if (rule.Spawn(ctx).Placed)
            return null;
        // From the train's middle, for what lives at the stop (a warren, a roost) further from you than its rule reaches.
        var middle = train.Line.Sample(train.Dynamics.Path, train.Dynamics.Distance - train.Dynamics.Consist.LengthMetres / 2).Position;
        if (rule.Spawn(new SpawnContext(world, t, d) { CalledAt = middle, CrewAs = AsCrew }).Placed)
            return "from where it lives at this stop";
        return kind switch
        {
            EnemyKind.Ribbit => Warren(world, you, t),
            EnemyKind.CarHugger => Hugger(world, t),
            EnemyKind.Follower => "it rides whoever's off the train: step down and call it again (only the others ever see it)",
            EnemyKind.Passenger => "it needs a car with a room, and a crewmate",
            _ => "its rule found nowhere to put it here",
        };
    }

    /// <summary>
    /// The pack from the stop's warren nearest you, however far (the rule takes only one within reach of the ground crew); a
    /// pack sized for a crew of <see cref="AsCrew"/>, so it goes for a lone player.
    /// </summary>
    static string Warren(World world, Double3 you, EnemyTuning t)
    {
        var sites = CreatureSites.Of(world, Stops.LairKind.Warren, t.Sites.Around);
        if (sites.Count == 0)
            return "no warren at this stop";
        var at = sites.Select(x => x.At).OrderBy(a => Flat(a - you)).First();
        int size = Math.Min(AsCrew, Math.Max(2, t.Ribbits.PackSize[^1]));
        int pack = world.NextEnemyId;
        for (int n = 0; n < size; n++)
        {
            var spot = at + new Double3(n * 1.2, 0, (n % 2) * 1.2);
            world.AddEnemy(i => Ribbit.At(i, pack, spot, t.Ribbits));
        }
        return "out of the stop's warren";
    }

    /// <summary>Tower Jaw at the nearest coaling tower or standing crane within <paramref name="max"/> of you, here or not.</summary>
    static string Tower(World world, Double3 you, double max, EnemyTuning t)
    {
        if (world.Run is not { } run)
            return "no stops on this line";
        double front = world.Train.Dynamics.Distance;
        var down = world.ActiveEnemies.OfType<TowerJaw>().Where(j => j.Mode == TowerJawMode.Wreck).Select(j => j.FacilityIndex).ToHashSet();
        foreach (int i in Enumerable.Range(0, run.Facilities.Count).Where(i => !down.Contains(i)).OrderBy(i => Math.Abs((run.Facilities[i].Start + run.Facilities[i].End) / 2 - front)))
        {
            Enemy? jaw = null;
            if (run.Facilities[i].Facility == Route.FacilityKind.CoalingTower && run.ChuteLeft(i) > 0)
                jaw = world.AddEnemy(n => TowerJaw.AtCoalingTower(n, world, i, t.TowerJaw, t.TowerJaw.StartGnawed));
            else if (run.Sites.ElementAtOrDefault(i)?.Crane is { Wrecked: false } crane)
                jaw = world.AddEnemy(n => TowerJaw.AtGantry(n, world, i, crane, t.TowerJaw, t.TowerJaw.StartGnawed));
            if (jaw is null)
                continue;
            double off = Flat(jaw.WorldPosition(world.Train) - you);
            if (off <= max)
                return run.Facilities[i].Facility == Route.FacilityKind.CoalingTower ? "gnawing the coaling tower's leg" : "gnawing the stop's crane's leg";
            // The nearest standing structure is past the gym's reach: it doesn't come.
            jaw.Dismiss();
            return $"the nearest crane or tower is {off:0} m off, past the gym's {max:0} m: walk towards it and call again";
        }
        return "no coaling tower or crane standing on this line";
    }

    /// <summary>The Grumbler on castings stacked by a crane (a facility's own or a yard's gantry, note 848) within <paramref name="max"/> of you, whether the train's in its yard or not.</summary>
    static string Castings(World world, Double3 you, double max, EnemyTuning t)
    {
        if (world.Run is not { } run)
            return "no stops on this line";
        double front = world.Train.Dynamics.Distance;
        foreach (int i in Enumerable.Range(0, run.Facilities.Count).OrderBy(i => Math.Abs((run.Facilities[i].Start + run.Facilities[i].End) / 2 - front)))
        {
            // The nearest casting stacked by any of its cranes.
            var stacked = (run.Sites.ElementAtOrDefault(i)?.Cranes ?? []).SelectMany(k => Enumerable.Range(0, k.Castings.Length)
                .Where(c => k.Castings[c].State == CastingState.Stacked).Select(c => (Crane: k, On: c))).OrderBy(x => Flat(x.Crane.Castings[x.On].At - you)).ToList();
            if (stacked.Count == 0)
                continue;
            var (crane, on) = stacked[0];
            if (Flat(crane.Castings[on].At - you) > max)
                return $"the nearest castings are {Flat(crane.Castings[on].At - you):0} m off, past the gym's {max:0} m: walk towards them and call again";
            var at = crane.Castings[on].At;
            world.AddEnemy(n => Grumbler.OnCrates(n, at + Double3.Up * 0.8, on, t.Grumbler, crane));
            return "gnawing the castings by the crane: leave it be, or hit it";
        }
        return "no crane with castings stacked on this line";
    }

    /// <summary>
    /// The Mouse Moose (note 790) after the nearest casting stacked by any crane on the line (a facility's own or a yard's),
    /// within <paramref name="max"/> of you, whether the train's in its yard or not: out of its hole past it.
    /// </summary>
    /// <summary>The Tapper (note 1011) behind whoever's seated at a gun, as if they'd been on it alone its dwell.</summary>
    static string TapperCall(World world, EnemyTuning t)
    {
        var train = world.Train;
        foreach (var (id, s) in world.CrewThisTick.OrderBy(c => c.Id))
            if (s.Alive && s.Has(Player.PlayerFlags.Seated) && s.Parent >= 1 && s.Parent < train.Frames.Count && train.Frames[s.Parent].Shape.Gun is not null)
            {
                int car = s.Parent;
                world.AddEnemy(n => Tapper.Over(n, car, s.Position, id, train, t.Tapper));
                return $"over the roof behind the gunner on car {car}: when it taps, look round";
            }
        return "nobody at a gun: sit at one and call again";
    }

    static string BruinCall(World world, Double3 you, EnemyTuning t)
    {
        // The gym's train stands empty at its yard: any car with a room will do to meet it.
        int car = Bruin.CarFor(world.Train);
        if (car < 0)
            car = world.Train.Dynamics.Consist.Vehicles.Where(v => v.Id != 0 && world.Train.Frames[v.Id].Shape.Interior is not null).Select(v => v.Id).DefaultIfEmpty(-1).First();
        if (car < 0)
            return "no car with a room to climb into";
        // Out of the trees on the side away from you, so you see it come round to the car.
        var frame = world.Train.Frames[car];
        int side = Double3.Dot(you - frame.Origin, frame.Right) > 0 ? -1 : 1;
        var home = Bruin.TreeLine(world, car, side, t.Bruin);
        world.AddEnemy(n => Bruin.Out(n, home, car, t.Bruin.Health));
        return $"out of the trees for car {car}: get round its side, never in front of it";
    }

    static string MouseMooseCall(World world, Double3 you, double max, EnemyTuning t)
    {
        if (world.Run is not { } run)
            return "no stops on this line";
        var best = run.Sites.Where(s => s is not null).SelectMany(s => s!.Cranes).Where(c => !c.Wrecked)
            .SelectMany(c => MouseMoose.Takeable(c, you).Select(i => (Crane: c, Casting: i)))
            .OrderBy(x => Flat(x.Crane.Castings[x.Casting].At - you)).Select(x => ((Crane, int)?)x).FirstOrDefault();
        if (best is not { } found)
            return "no crane with castings stacked on this line";
        var (crane, on) = found;
        var at = crane.Castings[on].At;
        if (Flat(at - you) > max)
            return $"the nearest castings are {Flat(at - you):0} m off, past the gym's {max:0} m: walk towards them and call again";
        var (hole, hint) = MouseMoose.HoleFor(world, at, t.MouseMoose);
        world.AddEnemy(n => MouseMoose.At(n, crane, on, hole, hint));
        return $"out of its hole, for a casting by the crane: stand within {t.MouseMoose.ScareWithin:0} m of it to scare it off";
    }

    /// <summary>A Car Hugger with no marsh or bridge ahead: lurking on the line a little ahead, for the train to run over.</summary>
    static string Hugger(World world, EnemyTuning t)
    {
        var train = world.Train;
        world.AddEnemy(i => CarHugger.Lurking(i, train.Dynamics.Distance + 120, 1, t.CarHugger));
        return "lurking under the line ahead: run the train over it";
    }

    /// <summary>What lives in a house, in the open house nearest you that you're not in (a house creature acts only on who comes in).</summary>
    static string House(World world, EnemyKind kind, Double3 you, double max, EnemyTuning t)
    {
        if (world.Route is not { } route || world.Train.Walls is not { } walls)
            return "no open houses here";
        var houses = walls.OpenHouses.Where(h => h.Feature >= 0 && h.Feature < route.Features.Count && route.Features[h.Feature].Stop is not null && !StopWalls.Storeyed(h.B))
            .Where(h => kind != EnemyKind.Householder || route.Features[h.Feature].Stop!.Containers.Any(c => c.Building == h.Building))
            .Where(h => !world.ActiveEnemies.Any(e => !e.Gone && e.Kind is EnemyKind.Lodger or EnemyKind.Householder or EnemyKind.HollowHouse or EnemyKind.Hanger && (int)e.Extra2 == h.Index))
            .OrderBy(h => Flat(h.Middle - you) < 12 ? double.MaxValue : Flat(h.Middle - you)).ToList();
        if (houses.Count == 0)
            return kind == EnemyKind.Householder ? "no open house with its things in it" : "every open house is taken";
        var house = houses[0];
        if (Flat(house.Middle - you) > max)
            return $"the nearest open house is {Flat(house.Middle - you):0} m off, past the gym's {max:0} m: walk towards the village and call again";
        world.AddEnemy<Enemy>(i => kind switch
        {
            EnemyKind.Lodger => Lodger.In(i, house, walls, t.Lodger),
            EnemyKind.Householder => Householder.In(i, house, walls, t.Householder),
            EnemyKind.HollowHouse => HollowHouse.In(i, house),
            _ => Hanger.In(i, house, t.Hanger),
        });
        return $"in the house {Flat(house.Middle - you):0} m off: go in";
    }

    /// <summary>The car of the engine's rake farthest from you (with a room, if asked), or −1.</summary>
    static int FarthestCar(World world, Double3 you, bool interior)
    {
        var train = world.Train;
        return train.Dynamics.Consist.Vehicles.Where(v => v.Id > 0 && (!interior || train.Frames[v.Id].Shape.Interior is not null))
            .OrderByDescending(v => Flat(train.Frames[v.Id].Origin - you)).Select(v => v.Id).DefaultIfEmpty(-1).First();
    }

    /// <summary>The car (not the engine or tender) nearest you, or -1.</summary>
    static int NearestCar(World world, Double3 you)
    {
        var train = world.Train;
        return train.Dynamics.Consist.Vehicles.Where(v => v.Id > 0 && !v.IsEngine)
            .OrderBy(v => Flat(train.Frames[v.Id].Origin - you)).Select(v => v.Id).DefaultIfEmpty(-1).First();
    }

    /// <summary>A body lying off the train that nothing's come for, or null.</summary>
    static Body? Lying(World world)
    {
        var train = world.Train;
        return world.Bodies.All.FirstOrDefault(b => b.Kind == BodyKind.Ragdoll && !b.DroppedOut && b.Carrier < 0 && b.TakenBy < 0
            && !(b.Parent >= 0 && train.Dynamics.Consist.IndexOf(b.Parent) >= 0)
            && !world.ActiveEnemies.Any(e => !e.Gone && e.Kind == EnemyKind.Mourners));
    }

    /// <summary>A stand-in body on the ground out from you (nobody's: no crewmate's fee rides on it).</summary>
    static Body StandIn(World world, Double3 you, double distance)
    {
        var (at, hint, yaw) = Out(world, you, distance, 4);
        var lying = new PlayerState { Parent = PlayerState.World, Position = at + Double3.Up * 0.3, Surface = Surface.Ground, LineHint = hint, Yaw = yaw };
        return world.Bodies.SpawnRagdoll(world.Train, -1, lying);
    }

    /// <summary>Loose crates set down on the ground out from you (the Freight Beetle's and the Pickers' freight).</summary>
    static Double3 Crates(World world, Double3 you, double min, double max, int count)
    {
        var (at, hint, _) = Out(world, you, Math.Clamp(min, 0, max), 4);
        for (int n = 0; n < count; n++)
            world.Bodies.SpawnCargo(at + new Double3(n * 1.6, 0.6, (n % 2) * 1.4), hint);
        return at;
    }

    /// <summary>
    /// A spot on the ground <paramref name="distance"/> from you, at least <paramref name="clear"/> off the track: out to the
    /// side of the line you're on (aboard, the right), and the way facing back at you.
    /// </summary>
    public static (Double3 At, double Hint, double Yaw) Out(World world, Double3 you, double distance, double clear)
    {
        var train = world.Train;
        double hint = train.Dynamics.Distance;
        var (path, along) = train.Line.Nearest(you, ref hint);
        var rail = train.Line.Sample(path, along);
        var right = Double3.Cross(rail.Tangent, Double3.Up).Normalized;
        double side = Double3.Dot(you - rail.Position, right) < -1 ? -1 : 1;
        double across = Math.Max(clear, Math.Min(distance, Math.Abs(Double3.Dot(you - rail.Position, right)) + distance * 0.7));
        double ahead = Math.Sqrt(Math.Max(0, distance * distance - across * across * 0.5));
        var at = rail.Position + right * (side * across) + rail.Tangent * ahead;
        double h = along + ahead;
        at = at with { Y = PlayerMotor.GroundAt(at, train.Line, ref h) };
        var back = (you - at) with { Y = 0 };
        return (at, h, DMath.Atan2(back.X, back.Z));
    }

    static double Flat(Double3 v) => Math.Sqrt(v.X * v.X + v.Z * v.Z);

    /// <summary>
    /// What a call put down on the ground (the outside creatures: Ribbits, the Gaunt, the Soot Children, the Moose) nearer you
    /// than <paramref name="min"/> is moved out to it, along the way it already lay from you. The rest stays where its rule
    /// put it: on the train its rule picked the car, on the line it's ahead or behind by its rule, in a house it's its house.
    /// </summary>
    /// <summary>How far inside the gym's reach what's brought in is put (m): a pack's width, so it's in by its farthest.</summary>
    const double Inside = 10;

    /// <summary>
    /// What a call put farther from you than <paramref name="max"/> is brought in (the director, 13 Oct 2026: "nothing should
    /// spawn more than 200m off"): what's on the line along it, ahead staying ahead and behind behind; what's on the ground
    /// along the way it lay. A pack moves together, so it stays a pack. What's on the train is within its length; what stands
    /// where its rule found it was chosen within reach (<see cref="Put"/>). The hidden (the Tippy Toesie, nowhere till it's
    /// behind someone) and the ridden (a Follower, on someone's back) are left be.
    /// </summary>
    public static void KeepIn(World world, IReadOnlyList<Enemy> came, Double3 you, double max)
    {
        var train = world.Train;
        double lim = max - Inside;
        var free = came.Where(e => e.Kind is not (EnemyKind.TippyToesie or EnemyKind.Follower)).ToList();
        // On the line: the farthest of those ahead of you brought to lim, the rest by the same; the same behind.
        double hint = train.Dynamics.Distance;
        var (_, mine) = train.Line.Nearest(you, ref hint);
        var line = free.Where(e => e.Attached == -1).ToList();
        foreach (int side in (int[])[1, -1])
        {
            var those = line.Where(e => Math.Sign(e.LineDistance - mine) == side || e.LineDistance == mine && side == 1).ToList();
            if (those.Count == 0)
                continue;
            double far = those.Max(e => Math.Abs(e.LineDistance - mine));
            if (far > lim)
                foreach (var e in those)
                    e.LineDistance -= side * (far - lim);
        }
        // On the ground: the farthest brought to lim along its bearing, the rest moved with it.
        var ground = free.Where(e => e.Attached == Enemy.Loose).ToList();
        if (ground.Count > 0 && ground.MaxBy(e => Flat(e.Local - you)) is { } farthest && Flat(farthest.Local - you) is var off && off > lim)
        {
            var toward = ((you - farthest.Local) with { Y = 0 }) * (1 / off) * (off - lim);
            foreach (var e in ground)
            {
                var to = e.Local + toward;
                double h = train.Dynamics.Distance;
                e.Local = to with { Y = PlayerMotor.GroundAt(to, train.Line, ref h) };
            }
        }
    }

    public static void KeepOff(World world, Enemy e, Double3 you, double min)
    {
        if (e.Attached != Enemy.Loose || e.Kind is not (EnemyKind.Ribbit or EnemyKind.Gaunt or EnemyKind.SootChildren or EnemyKind.Moose))
            return;
        var train = world.Train;
        var flat = (e.Local - you) with { Y = 0 };
        double off = flat.Length;
        if (off >= min)
            return;
        var dir = off > 0.1 ? flat * (1 / off) : Double3.Cross(train.Line.Sample(train.Dynamics.Path, train.Dynamics.Distance).Tangent, Double3.Up).Normalized;
        var to = you + dir * min;
        double hint = train.Dynamics.Distance;
        e.Local = to with { Y = PlayerMotor.GroundAt(to, train.Line, ref hint) };
    }
}
