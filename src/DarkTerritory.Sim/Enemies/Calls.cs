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
                    return "it's the ground itself: it needs the train standing on a marsh (the map's)";
                world.CallDrift(t);
                return "the marsh coming up under a car";
            case EnemyKind.Sleepers:
                // Track debris on the main line ahead (§22): past the lamp's reveal, for the train to run onto.
                world.AddEnemy(i => new Sleepers(i) { LineDistance = train.Dynamics.Distance + Math.Max(max, t.Sleepers.LampRevealDistance + 30), Height = 0.2 });
                return "debris on the line ahead: drive onto it";
            case EnemyKind.Choir:
                return world.CallChoir() ? "gathering: it comes in a few seconds, and it hears you" : "this night has no guns or loudness meter";
            case EnemyKind.CarFire:
                {
                    int car = FarthestCar(world, you, interior: true);
                    if (car < 0)
                        return "no car with a room to burn";
                    world.AddEnemy(i => CarFire.In(i, train, car, 0, t.CarFire));
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
                return House(world, kind, you, t);
            case EnemyKind.Pickers:
                {
                    if (world.Run?.FacilityFeature is null)
                        return "they come up at a yard: stand the train at its stop";
                    Crates(world, you, min, max, 3);
                    var drains = Picking.Drains(world, t.Pickers);
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
        if (rule.Spawn(ctx))
            return null;
        // From the train's middle, for what lives at the stop (a warren, a roost) further from you than its rule reaches.
        var middle = train.Line.Sample(train.Dynamics.Path, train.Dynamics.Distance - train.Dynamics.Consist.LengthMetres / 2).Position;
        if (rule.Spawn(new SpawnContext(world, t, d) { CalledAt = middle, CrewAs = AsCrew }))
            return "from where it lives at this stop";
        return kind switch
        {
            EnemyKind.CarHugger => Hugger(world, t),
            EnemyKind.Follower => "it rides a crewmate off the train: step down onto the ground at the stop",
            EnemyKind.Switchman => "it needs a branch's points 0.5 to 1.2 km ahead: drive on towards one",
            EnemyKind.TowerJaw => "it needs a coaling tower or a crane at the stop, standing",
            EnemyKind.Grumbler => "it needs the stop's crane with castings stacked",
            EnemyKind.Passenger => "it needs a car with a room, and a crewmate",
            _ => "its rule found nowhere to put it here",
        };
    }

    /// <summary>A Car Hugger with no marsh or bridge ahead: lurking on the line a little ahead, for the train to run over.</summary>
    static string Hugger(World world, EnemyTuning t)
    {
        var train = world.Train;
        world.AddEnemy(i => CarHugger.Lurking(i, train.Dynamics.Distance + 300, 1, t.CarHugger));
        return "lurking under the line ahead: run the train over it";
    }

    /// <summary>What lives in a house, in the open house nearest you that you're not in (a house creature acts only on who comes in).</summary>
    static string House(World world, EnemyKind kind, Double3 you, EnemyTuning t)
    {
        if (world.Route is not { } route || world.Train.Walls is not { } walls)
            return "no open houses here";
        var houses = walls.OpenHouses.Where(h => h.Feature >= 0 && h.Feature < route.Features.Count && route.Features[h.Feature].Stop is not null)
            .Where(h => kind != EnemyKind.Householder || route.Features[h.Feature].Stop!.Containers.Any(c => c.Building == h.Building))
            .Where(h => !world.ActiveEnemies.Any(e => !e.Gone && e.Kind is EnemyKind.Lodger or EnemyKind.Householder or EnemyKind.HollowHouse or EnemyKind.Hanger && (int)e.Extra2 == h.Index))
            .OrderBy(h => Flat(h.Middle - you) < 12 ? double.MaxValue : Flat(h.Middle - you)).ToList();
        if (houses.Count == 0)
            return kind == EnemyKind.Householder ? "no open house with its things in it" : "every open house is taken";
        var house = houses[0];
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
    static void Crates(World world, Double3 you, double min, double max, int count)
    {
        var (at, hint, _) = Out(world, you, Math.Clamp(min, 0, max), 4);
        for (int n = 0; n < count; n++)
            world.Bodies.SpawnCargo(at + new Double3(n * 1.6, 0.6, (n % 2) * 1.4), hint);
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
