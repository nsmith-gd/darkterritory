using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Audit;

/// <summary>Mirror of content/tuning/balance.json <c>grabs</c>: App. A.9 / B.10's per-tree GRAB check. Field docs live there.</summary>
public sealed record GrabAuditTuning
{
    public int[] Crews { get; init; } = [2, 3, 4, 5, 6, 7, 8];
    public double StandOffM { get; init; } = 4;
    public double BeyondAloneM { get; init; } = 1;
    public double GrabWithinSeconds { get; init; } = 75;
}

/// <summary>
/// One creature's GRAB at one crew size. <paramref name="Freed"/>: the grab ended with its victim alive, and
/// <paramref name="FreedBy"/> says how (pulled free, struck off, the count evened, the thing killed).
/// </summary>
public sealed record GrabCheck(string Kind, int Crew, bool Grabbed, bool Freed, double HeldSeconds, double WindowSeconds, string FreedBy, string Detail);

public sealed record GrabAuditReport(IReadOnlyList<GrabCheck> Checks, IReadOnlyList<string> Uninterruptible, IReadOnlyList<string> NeverGrabbed, bool Pass);

/// <summary>
/// GDD App. A.9 "what the harness verifies per tree: every GRAB is interruptible by the crew actually present", and B.10
/// "Rescue windows: every GRAB is interruptible by the crew present, at every crew size" (note 186). For every creature
/// with a grab, at every crew size: the victim is put where the creature takes them (alone, idle, at its gap, in front of
/// its mouth), the rest of the crew stand nearby but far enough off that it still does (its own "alone" radius and a
/// metre), with what players carry. When it grabs, the friends answer as the bots do (<see cref="Heed.Rescue"/>: go to
/// the held one, haul at them, swing at what holds them) through intent. The check passes if the grab is broken with
/// the victim alive before its window runs out.
/// </summary>
public static class GrabAudit
{
    /// <summary>App. A.9's shared GRAB state's users, and everything else that grabs (every kill goes through a grab, A.1).</summary>
    public static IReadOnlyList<EnemyKind> Grabbers { get; } =
    [
        EnemyKind.CarHugger, EnemyKind.Dragger, EnemyKind.Whistler, EnemyKind.TippyToesie, EnemyKind.Ribbit, EnemyKind.SootChildren,
        EnemyKind.Choir, EnemyKind.Passenger, EnemyKind.Climber, EnemyKind.Gaunt, EnemyKind.CinderHound, EnemyKind.Grumbler,
        EnemyKind.Moose, EnemyKind.Gannet, EnemyKind.Knotter,
    ];

    const int Cars = 6;

    public static GrabAuditReport Run(AuditContent c, IEnumerable<EnemyKind>? kinds = null, IEnumerable<int>? crews = null)
    {
        var t = c.Balance.Grabs;
        var checks = new List<GrabCheck>();
        foreach (var kind in kinds ?? Grabbers)
            foreach (int crew in crews ?? t.Crews)
                checks.Add(Check(c, kind, crew));
        var uninterruptible = checks.Where(x => x.Grabbed && !x.Freed).Select(x => $"{x.Kind} at crew {x.Crew}: {x.Detail}").ToList();
        var never = checks.Where(x => !x.Grabbed).Select(x => $"{x.Kind} at crew {x.Crew}: {x.Detail}").ToList();
        return new GrabAuditReport(checks, uninterruptible, never, uninterruptible.Count == 0 && never.Count == 0);
    }

    /// <summary>One creature's grab at one crew size.</summary>
    /// <param name="friendsHelp">False: the friends stand there and watch (the audit's control: then nothing should free them).</param>
    public static GrabCheck Check(AuditContent c, EnemyKind kind, int crew, bool friendsHelp = true)
    {
        var t = c.Balance.Grabs;
        var rig = Rig(c, kind, crew);
        var n = rig.Night;
        // Friends keep from standing idle (Tippy Toesie's mark) until someone's held; then they go to them.
        var stir = new PlayerIntent { MoveX = 0.01f };
        PlayerIntent Act(int id, PlayerState s) =>
            id == rig.Victim ? rig.VictimIntent(n, s)
            : !friendsHelp ? stir
            : (Holder() is not null ? rig.Friend?.Invoke(id, s) : null) ?? Heed.Rescue(stir, s, n.World, id, [.. n.Crew.Select(x => (x.Key, x.Value))]);
        Enemy? Holder() => n.World.ActiveEnemies.FirstOrDefault(x => x.Kind == kind && x.Phase == SpinePhase.Grab && x.Holding >= 0);
        n.Run(t.GrabWithinSeconds, Act, () => Holder() is not null);
        if (Holder() is not { } holder)
            return new GrabCheck(kind.ToString(), crew, false, false, 0, 0, "", $"no grab within {t.GrabWithinSeconds:0} s ({rig.Setting})");
        int victim = holder.Holding;
        double window = holder.GrabWindow;
        int from = n.Events.Count;
        double held = 0;
        // Through the window (and a second over): it lets go, or it punishes.
        while (held < window + 1 && holder.Phase == SpinePhase.Grab && holder.Holding == victim && n.Crew[victim].Alive)
        {
            n.Run(SimConstants.TickSeconds, Act);
            held += SimConstants.TickSeconds;
        }
        bool alive = n.Crew[victim].Alive;
        bool freed = alive && !(holder.Phase == SpinePhase.Grab && holder.Holding == victim);
        var after = n.Events.Skip(from).FirstOrDefault(x => x.EnemyId == holder.Id);
        string by = !freed ? "" : holder.Health <= 0 ? "killed" : holder.Gone ? "fled" : after.To == SpinePhase.BreakOff ? "broken off" : after.To.ToString();
        string detail = freed ? $"freed after {held:0.0} of {window:0.0} s ({rig.Setting})"
            : alive ? $"still held after {held:0.0} s ({rig.Setting})" : $"{n.Crew[victim].Death} after {held:0.0} of {window:0.0} s ({rig.Setting})";
        return new GrabCheck(kind.ToString(), crew, true, freed, Math.Round(held, 2), Math.Round(window, 2), by, detail);
    }

    /// <param name="Friend">A friend's intent once someone's held, before the bots' rescue (getting into the victim's car); null for none.</param>
    sealed record RigSetup(AuditNight Night, int Victim, string Setting, Func<AuditNight, PlayerState, PlayerIntent> VictimIntent)
    {
        public Func<int, PlayerState, PlayerIntent?>? Friend { get; init; }
    }

    static PlayerIntent Still(AuditNight n, PlayerState s) => default;

    /// <summary>
    /// Where the friends stand: <paramref name="place"/> for each of the <paramref name="crew"/> − 1, given the distance
    /// they keep (the creature's own alone radius and a metre, at least the stand-off) and which friend this is.
    /// </summary>
    static void Friends(AuditNight n, int crew, double alone, Func<double, int, PlayerState> place)
    {
        for (int i = 0; i < crew - 1; i++)
            n.Add(place(alone, i));
    }

    static double Off(AuditContent c, double alone) => Math.Max(c.Balance.Grabs.StandOffM, alone + c.Balance.Grabs.BeyondAloneM);

    static RigSetup Rig(AuditContent c, EnemyKind kind, int crew)
    {
        var e = c.Enemies;
        switch (kind)
        {
            case EnemyKind.Dragger:
                {
                    // On car 2's roof near the right edge, at speed; friends along the same roof on the other side of the walk.
                    var n = new AuditNight(c, Cars, 14, crew);
                    var shape = n.Train.Frames[2].Shape;
                    int v = n.Add(n.Roof(2, 0, shape.HalfWidth - e.Draggers.GrabRange * 0.5));
                    n.World.AddEnemy(id => Dragger.Under(id, n.Train, 2, 1, 0));
                    double off = Off(c, 0);
                    Friends(n, crew, off, (d, i) => n.Roof(2, (i % 2 == 0 ? 1 : -1) * (d + i / 2 * 0.7), -0.4));
                    return new(n, v, "roof edge, 14 m/s", Still);
                }
            case EnemyKind.CarHugger:
                {
                    // Latched on the rear car; out on the rear platform in front of its mouth; friends inside the van by its rear door.
                    var n = new AuditNight(c, Cars, 8, crew);
                    var hugger = n.World.AddEnemy(id => CarHugger.Lurking(id, n.Train.Dynamics.RearDistance + 1, 1, e.CarHugger));
                    n.Run(0.5, (_, _) => default);
                    int rear = n.Train.Dynamics.Consist.Vehicles[^1].Id;
                    OpenDoors(n, rear);
                    var s = n.Roof(rear, 0);
                    s.Position = hugger.Local - new Double3(0, 0.1, 0.9);
                    s.Surface = Surface.Deck;
                    int v = n.Add(s);
                    var room = n.Train.Frames[rear].Shape.Interior!.Value;
                    Friends(n, crew, 0, (_, i) => n.Inside(rear, room.Max.Z - 2.2 - i * 0.6, i % 2 == 0 ? -0.4 : 0.4));
                    return new(n, v, "rear platform, latched, 8 m/s", Still);
                }
            case EnemyKind.Whistler:
                {
                    // At a stop, alone in its gap after the whistle; friends on the ballast beside the next car, not looking.
                    var n = new AuditNight(c, Cars, 0, crew);
                    var w = n.World.AddEnemy(id => Whistler.InGap(id, n.Train, 2, e.Whistler));
                    n.Run(e.Whistler.WhistleAfterStop + e.Whistler.WhistleSeconds + 1, (_, _) => default);
                    int v = n.Add(n.Gap(2));
                    double off = Off(c, e.Whistler.PairRadius);
                    var gapAlong = CrewSense.GapLocal(n.Train, 2).Z;
                    Friends(n, crew, off, (d, i) => n.Ground(2, 2, gapAlong - d - i * 0.8));
                    return new(n, v, "alone in its gap at a stop", Still);
                }
            case EnemyKind.TippyToesie:
                {
                    // Stood still alone on the ballast at a stop; friends further along, keeping busy.
                    var n = new AuditNight(c, Cars, 0, crew);
                    int v = n.Add(n.Ground(2, 4, -3));
                    double off = Off(c, e.TippyToesie.AloneRadius);
                    Friends(n, crew, off, (d, i) => n.Ground(2, 4 + (i % 2) * 1.5, -3 + d + i / 2 * 1.2));
                    n.World.AddEnemy(id => TippyToesie.Hiding(id, e.TippyToesie));
                    return new(n, v, "standing still alone at a stop", Still);
                }
            case EnemyKind.Ribbit:
                {
                    // On the ground at a stop with the largest pack the director sends at this crew size; friends just out of the count.
                    var n = new AuditNight(c, Cars, 0, crew);
                    int v = n.Add(n.Ground(2, 4));
                    var at = n.Where(v);
                    int size = Math.Min(crew, e.Ribbits.PackSize[1]);
                    var first = n.World.AddEnemy(id => Ribbit.At(id, id, at + new Double3(9, 0, 0), e.Ribbits));
                    for (int i = 1; i < size; i++)
                    {
                        int k = i;
                        n.World.AddEnemy(id => Ribbit.At(id, first.Id, at + new Double3(9, 0, 1.5 * k), e.Ribbits));
                    }
                    double off = Off(c, e.Ribbits.GroupRadius);
                    Friends(n, crew, off, (d, i) => n.Ground(2, 4 + (i % 2) * 1.5, -d - i / 2 * 1.2));
                    return new(n, v, $"on the ground, a pack of {size}", Still);
                }
            case EnemyKind.SootChildren:
                {
                    // A soot child calling three metres off a crewmate on the ground; friends further back.
                    var n = new AuditNight(c, Cars, 0, crew);
                    int v = n.Add(n.Ground(2, 4));
                    var at = n.Where(v);
                    n.World.AddEnemy(id => SootChildren.Calls(id, at + new Double3(0, 0, 3), true, e.SootChildren));
                    double off = Off(c, e.SootChildren.LungeWithin);
                    Friends(n, crew, off, (d, i) => n.Ground(2, 4 + (i % 2) * 1.5, d + 3 + i / 2 * 1.2));
                    return new(n, v, "within five metres of a soot child", Still);
                }
            case EnemyKind.Choir:
                {
                    // Everyone up on the roofs (exposed) when it's gathered; it takes the loudest, then the nearest.
                    var n = new AuditNight(c, Cars, 8, crew);
                    n.World.Insist = [EnemyKind.Choir];
                    int v = n.Add(n.Roof(2, 0));
                    double off = Off(c, 0);
                    Friends(n, crew, off, (d, i) => n.Roof(2, (i % 2 == 0 ? 1 : -1) * (d + i / 2 * 0.7)));
                    return new(n, v, "exposed on the roofs, 8 m/s", Still);
                }
            case EnemyKind.Passenger:
                {
                    // Aboard car 3 passing for crew; a crewmate alone at its front end; friends at its rear end, beyond its alone radius.
                    var n = new AuditNight(c, Cars, 8, crew);
                    var room = n.Train.Frames[3].Shape.Interior!.Value;
                    OpenDoors(n, 3);
                    int v = n.Add(n.Inside(3, room.Min.Z));
                    n.World.AddEnemy(id => Passenger.Boards(id, n.Train, 3, 2, e.Passenger));
                    Friends(n, crew, 0, (_, i) => n.Inside(3, room.Max.Z - i * 0.5, i % 2 == 0 ? -0.45 : 0.45));
                    return new(n, v, "alone at the far end of its car, 8 m/s", Still);
                }
            case EnemyKind.Climber:
                {
                    // Once it's in a car, a crewmate walks in on it alone. Anyone else in a 14 m car is inside its count (App. A.4:
                    // it takes a lone player), so the friends are up on the roof of the car behind; when it grabs they come down
                    // onto the plate and in at the car's rear door, as the bots go in to a fire (WarmUp.Into), then at it.
                    var n = new AuditNight(c, Cars, 12, crew);
                    var climber = n.World.AddEnemy(id => Climber.Pacing(id, n.Train, 3, 1, e.Climbers));
                    n.Run(e.Climbers.PaceSeconds + e.Climbers.ScrabbleSeconds + 30, (_, _) => default, () => climber.Inside);
                    int car = climber.Attached >= 1 ? climber.Attached : 3;
                    int behind = n.Train.VehicleBehind(car);
                    var v = n.Add(n.Inside(car, climber.Local.Z, climber.Local.X));
                    double off = Off(c, e.Climbers.CountRadius);
                    Friends(n, crew, off, (d, i) => n.Roof(behind, -n.Train.Frames[behind].Shape.HalfLength + 0.8 + d - 7 + i * 0.6, i % 2 == 0 ? -0.3 : 0.3));
                    var warm = new Dictionary<int, WarmUp>();
                    return new(n, v, $"alone with it in car {car}, friends on car {behind}'s roof, 12 m/s", Still)
                    {
                        Friend = (id, s) =>
                        {
                            if (s.Parent == car && PlayerMotor.Indoors(s, n.Train))
                                return null;
                            if (!warm.TryGetValue(id, out var w))
                                warm[id] = w = new WarmUp(c.Player.Cold) { Into = car };
                            return w.Decide(s, n.Train);
                        },
                    };
                }
            case EnemyKind.Gaunt:
                {
                    // Woken by a crewmate who's already hurt, and kept silent at; friends a little way off.
                    var n = new AuditNight(c, Cars, 0, crew);
                    var s = n.Ground(2, 4);
                    s.Health = (int)e.Gaunt.GrabBelowHealth;
                    int v = n.Add(s);
                    var at = n.Where(v);
                    n.World.AddEnemy(id => Gaunt.Asleep(id, at + new Double3(0, 0, 3), e.Gaunt));
                    double off = Off(c, e.Gaunt.StirAt);
                    Friends(n, crew, off, (d, i) => n.Ground(2, 4 + (i % 2) * 1.5, -d - i / 2 * 1.2));
                    return new(n, v, "woken, hurt, and nobody talking", Still);
                }
            case EnemyKind.Gannet:
                {
                    // A crewmate who's hit it, out on car 2's roof at speed; friends along the same roof (note 340).
                    var n = new AuditNight(c, Cars, 20, crew);
                    int v = n.Add(n.Roof(2, 0));
                    n.World.AddEnemy(id => Gannet.Marking(id, n.Train, e.Gannet, e.Gannet.SoarHeight[0], v));
                    double off = Off(c, 0);
                    Friends(n, crew, off, (d, i) => n.Roof(2, (i % 2 == 0 ? 1 : -1) * (d + i / 2 * 0.7)));
                    return new(n, v, "on the roofs at 20 m/s, its mark", Still);
                }
            case EnemyKind.Moose:
                {
                    // At a stop, a crewmate already hurt out on the ground, the moose riled at them from further out (note 339);
                    // friends along the train, out of its path.
                    var n = new AuditNight(c, Cars, 0, crew);
                    var s = n.Ground(2, 8);
                    s.Health = (int)e.Moose.GrabBelowHealth;
                    int v = n.Add(s);
                    var at = n.Where(v);
                    var frame = n.Train.Frames[2];
                    var outward = (at - frame.Origin) with { Y = 0 };
                    n.World.AddEnemy(id => Moose.Enraged(id, at + outward.Normalized * 18, n.Train.Dynamics.Distance, v));
                    double off = Off(c, 0);
                    Friends(n, crew, off, (d, i) => n.Ground(2, 3, (i % 2 == 0 ? 1 : -1) * (d + i / 2 * 1.2)));
                    return new(n, v, "hurt on the ground at a stop, the moose riled at them", Still);
                }
            case EnemyKind.CinderHound:
                {
                    // A hound aboard the rear car's roof and a crewmate there already bitten down; friends along the roof.
                    var n = new AuditNight(c, Cars, 8, crew);
                    int rear = n.Train.Dynamics.Consist.Vehicles[^1].Id;
                    var shape = n.Train.Frames[rear].Shape;
                    n.World.AddEnemy(id => new CinderHound(id, id) { Attached = rear, Local = new Double3(0.6, shape.RoofHeight, shape.HalfLength - 2.5), Health = e.CinderHounds.Health });
                    var s = n.Roof(rear, shape.HalfLength - 4);
                    s.Health = (int)e.CinderHounds.BiteDamage;
                    int v = n.Add(s);
                    Friends(n, crew, 0, (_, i) => n.Roof(rear, shape.HalfLength - 4 - e.CinderHounds.Reach - 1.5 - i * 0.7, i % 2 == 0 ? -0.4 : 0.4));
                    return new(n, v, "bitten down on the rear roof, 8 m/s", Still);
                }
            case EnemyKind.Grumbler:
                {
                    // Hit once by a hurt crewmate (it's feral after them); friends a little way off.
                    var n = new AuditNight(c, Cars, 0, crew);
                    var s = n.Ground(2, 4);
                    s.Health = (int)e.Grumbler.GrabBelowHealth;
                    int v = n.Add(s);
                    var at = n.Where(v);
                    var g = n.World.AddEnemy(id => Grumbler.OnCrates(id, at + new Double3(0, 0, -1.4), -1, e.Grumbler));
                    double off = Off(c, 0);
                    Friends(n, crew, off, (d, i) => n.Ground(2, 4 + (i % 2) * 1.5, d + i / 2 * 1.2));
                    // The victim faces it and swings once (the "interrupt" that makes it hunt them, App. A.8).
                    return new(n, v, "it's after the one who hit it", (night, self) =>
                    {
                        if (g.LastHitBy >= 0 || night.World.ElapsedSeconds > 3)
                            return default;
                        var to = g.WorldPosition(night.Train) - PlayerMotor.WorldPosition(self, night.Train);
                        double yaw = DMath.Atan2(-to.X, -to.Z);
                        return new PlayerIntent { Actions = PlayerActions.Swing, LookYaw = (float)Math.IEEERemainder(yaw - self.Yaw, 2 * Math.PI) };
                    });
                }
            case EnemyKind.Knotter:
                {
                    // Taut at speed, its gap behind car 2 forced; out on its back, the friends along it either side (note 365:
                    // "broken by a friend at the gap", and its coil's too short for anyone further to come). Whoever slips
                    // first is the one held; the rest are at the gap (note 367's KnotRescue).
                    var n = new AuditNight(c, Cars, 12, crew);
                    n.World.AddEnemy(id => Knotter.Into(id, n.Train, 2, e.Knotter));
                    n.Run(e.Knotter.CreepSeconds + e.Knotter.ForceSeconds + 0.5, (_, _) => default);
                    double l = n.Train.Frames[2].Shape.HalfLength, length = n.Train.Dynamics.Tuning.Geometry.CouplingGap + n.Train.Vehicles[2].Knot;
                    PlayerState Rope(double along)
                    {
                        var g = n.Train.Dynamics.Tuning.Geometry;
                        var s = n.Gap(2);
                        s.Position = new Double3(g.PlateX, g.CouplerHeight, l + Math.Clamp(along, 0.4, length - 0.4));
                        return s;
                    }
                    int v = n.Add(Rope(length / 2));
                    Friends(n, crew, 0, (_, i) => Rope(length / 2 + (i % 2 == 0 ? 1 : -1) * (0.7 + i / 2 * 0.5)));
                    return new(n, v, "on its back, taut at 12 m/s", Still);
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), $"{kind} has no grab");
        }
    }

    /// <summary>Every door of a car open (the crew aren't shut out of the room the rig puts them in).</summary>
    static void OpenDoors(AuditNight n, int car)
    {
        var v = n.Train.Vehicles[car];
        foreach (var d in n.Train.Frames[car].Shape.DoorList)
            if (d.Index != Train.CarShape.HatchBit && !v.DoorOpen(d.Index))
                v.ToggleDoor(d.Index);
    }
}
