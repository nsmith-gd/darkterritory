using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Audit;

/// <summary>Mirror of content/tuning/balance.json <c>cascades</c>: GDD §34's cascade audit. Field docs live there.</summary>
public sealed record CascadeTuning
{
    public int Crew { get; init; } = 6;
    public int Cars { get; init; } = 6;
    public double FailAtSeconds { get; init; } = 30;
    public double RepairWithinSeconds { get; init; } = 120;
    public double RestartWithinSeconds { get; init; } = 240;
    public double RefireWithinSeconds { get; init; } = 120;
    public double FireOutWithinSeconds { get; init; } = 120;
    public double ClearedWithinSeconds { get; init; } = 60;
    public double StokerWithinSeconds { get; init; } = 60;
    public double RecoupleWithinSeconds { get; init; } = 90;
    public double CutRunM { get; init; } = 40;
}

/// <summary>One failure chain, scripted, and whether the crew came back from it in time.</summary>
/// <param name="Failure">What was done to the night (GDD §23's "everything else cascades" table).</param>
/// <param name="Recovery">What counts as coming back from it.</param>
/// <param name="Seconds">From the failure to the recovery, or to giving up.</param>
public sealed record CascadeResult(string Name, string Failure, string Recovery, bool Recovered, double Seconds, string Detail);

public sealed record CascadeReport(IReadOnlyList<CascadeResult> Scenarios, IReadOnlyList<string> Unrecovered, bool Pass);

/// <summary>
/// GDD §34 "Cascade audit: no failure chain is unrecoverable" (note 186), over §23's table: each chain scripted into a night
/// (the boiler ruptured, the fire let die, a car alight, a gun fouled, the Stoker in the firebox, a car cut loose) and the
/// crew left to answer it: bots over the harness's loopback, through intent like any player, or for the coupling, a driver
/// scripted on the controls. A chain passes if it's recovered within its window (the tuning's, each well over the GDD's own
/// times: the 25 s repair, the 40 s restart at three cars).
/// </summary>
public static class CascadeAudit
{
    /// <summary>The chains, by name, in report order.</summary>
    public static IReadOnlyList<string> Names { get; } =
        ["rupture", "rupture-kit-in-car-four", "fire-out", "car-fire", "gun-fouled", "stoker", "uncoupled-car"];

    /// <param name="trace">With it, once a second after the failure: what each bot's doing (to see why a chain wasn't recovered).</param>
    public static CascadeReport Run(AuditContent c, IEnumerable<string>? only = null, int seed = 1, TextWriter? trace = null)
    {
        var results = (only ?? Names).Select(n => Scenario(c, n, seed, trace)).ToList();
        var unrecovered = results.Where(r => !r.Recovered).Select(r => $"{r.Name}: {r.Detail}").ToList();
        return new CascadeReport(results, unrecovered, unrecovered.Count == 0);
    }

    public static CascadeResult Scenario(AuditContent c, string name, int seed = 1, TextWriter? trace = null)
    {
        trace?.WriteLine($"-- {name}");
        var t = c.Balance.Cascades;
        var bt = c.Boiler;
        switch (name)
        {
            case "rupture":
            case "rupture-kit-in-car-four":
                {
                    // §23: "Someone fetches the engineering kit and holds it at the firebox for 25s, then the fire is built back
                    // up from cold." In the fitter's locker in car 1 (note 173), or, the table's "left in car four", on its floor.
                    bool far = name.EndsWith("four");
                    double? mended = null, restarted = null;
                    var r = Night(c, seed, trace, t.RepairWithinSeconds + t.RestartWithinSeconds, (tick, w) =>
                    {
                        if (tick != At(t.FailAtSeconds))
                            return;
                        if (far)
                            MoveKit(w, 4);
                        Rupture(w);
                    }, (w, since) =>
                    {
                        if (mended is null && !w.Train.Boiler.Ruptured)
                            mended = since;
                        if (mended is not null && restarted is null && w.Train.Boiler.InWorkingBand(bt))
                            restarted = since;
                        return restarted is not null || mended is null && since > t.RepairWithinSeconds;
                    });
                    bool ok = mended <= t.RepairWithinSeconds && restarted <= t.RestartWithinSeconds;
                    return new(name, far ? "boiler ruptured, the kit on car 4's floor" : "boiler ruptured, the kit in its locker in car 1",
                        $"mended within {t.RepairWithinSeconds:0} s, back in the working band within {t.RestartWithinSeconds:0} s", ok,
                        Math.Round(restarted ?? mended ?? r.Since, 1),
                        mended is null ? $"never mended in {r.Since:0} s (the kit {KitWhere(r.World)})"
                            : restarted is null ? $"mended at {mended:0} s, the pressure never back in the band ({r.World.Train.Boiler.Pressure:0})"
                            : $"mended at {mended:0} s, back in the band at {restarted:0} s");
                }
            case "fire-out":
                {
                    // §23 "Fire dies: coasting on grade and momentum, brakes only", until it's built back up.
                    double? back = null;
                    var r = Night(c, seed, trace, t.RefireWithinSeconds, (tick, w) =>
                    {
                        if (tick == At(t.FailAtSeconds))
                        {
                            w.Train.Boiler.Firebox = 0;
                            w.Train.Boiler.Pressure = Math.Min(w.Train.Boiler.Pressure, bt.WorkingBandMin * 0.5);
                        }
                    }, (w, since) =>
                    {
                        if (back is null && since > 1 && w.Train.Boiler.InWorkingBand(bt))
                            back = since;
                        return back is not null;
                    });
                    return new(name, "the fire out, the pressure half the band's floor", $"back in the working band within {t.RefireWithinSeconds:0} s",
                        back <= t.RefireWithinSeconds, Math.Round(back ?? r.Since, 1),
                        back is null ? $"pressure {r.World.Train.Boiler.Pressure:0} after {r.Since:0} s" : $"back in the band at {back:0} s");
                }
            case "car-fire":
                {
                    // §23 "Car catches fire: grab the extinguishers or abandon it" (App. C.5): every car with a room has one.
                    double? outAt = null;
                    const int car = 3;
                    var r = Night(c, seed, trace, t.FireOutWithinSeconds, (tick, w) =>
                    {
                        if (tick == At(t.FailAtSeconds) && w.Enemies is { } et)
                            w.AddEnemy(id => CarFire.In(id, w.Train, car, 0, et.CarFire));
                    }, (w, since) =>
                    {
                        if (outAt is null && since > 1 && !w.ActiveEnemies.Any(e => e.Kind == EnemyKind.CarFire && !e.Gone))
                            outAt = since;
                        return outAt is not null || w.Train.Vehicles[car].Taken;
                    });
                    var v = r.World.Train.Vehicles[car];
                    bool ok = outAt <= t.FireOutWithinSeconds && !v.Taken && v.Integrity > 0;
                    return new(name, $"car {car} alight, extinguishers aboard", $"every fire out within {t.FireOutWithinSeconds:0} s, the car still whole",
                        ok, Math.Round(outAt ?? r.Since, 1),
                        outAt is null ? $"still burning after {r.Since:0} s ({r.World.ActiveEnemies.Count(e => e.Kind == EnemyKind.CarFire && !e.Gone)} fires)"
                            : $"out at {outAt:0} s, the car at {v.Integrity:P0}");
                }
            case "gun-fouled":
                {
                    // §23 "Cannon fouls: someone clears it by hand, under fire" (note 183).
                    double? cleared = null;
                    int gun = -1;
                    var r = Night(c, seed, trace, t.ClearedWithinSeconds, (tick, w) =>
                    {
                        if (tick != At(t.FailAtSeconds))
                            return;
                        gun = w.Train.Vehicles.Where(v => v.Gun.Mounted && v.Gun.Facing > 0).Select(v => v.Id).DefaultIfEmpty(-1).Last();
                        if (gun >= 0)
                            w.Train.Vehicles[gun].Gun.Jammed = true;
                    }, (w, since) =>
                    {
                        if (cleared is null && gun >= 0 && !w.Train.Vehicles[gun].Gun.Jammed)
                            cleared = since;
                        return cleared is not null;
                    });
                    return new(name, "the guard van's gun fouled", $"cleared within {t.ClearedWithinSeconds:0} s", cleared <= t.ClearedWithinSeconds,
                        Math.Round(cleared ?? r.Since, 1), gun < 0 ? "no rear gun" : cleared is null ? $"still fouled after {r.Since:0} s" : $"cleared at {cleared:0} s");
                }
            case "stoker":
                {
                    // App. A.5: "open the firebox and club it to kill it ... ignored, the train runs away and derails".
                    double? gone = null;
                    var r = Night(c, seed, trace, t.StokerWithinSeconds, (tick, w) =>
                    {
                        if (tick == At(t.FailAtSeconds) && w.Enemies is { } et)
                            w.AddEnemy(id => Stoker.InFirebox(id, w.Train, false, et.Stoker));
                    }, (w, since) =>
                    {
                        if (gone is null && since > 1 && !w.ActiveEnemies.Any(e => e.Kind == EnemyKind.Stoker && !e.Gone))
                            gone = since;
                        return gone is not null || w.Derailed;
                    });
                    bool ok = gone <= t.StokerWithinSeconds && !r.World.Derailed && !r.World.Train.Boiler.Ruptured;
                    return new(name, "the Stoker in the firebox", $"gone within {t.StokerWithinSeconds:0} s, no rupture, no derailment", ok,
                        Math.Round(gone ?? r.Since, 1),
                        r.World.Derailed ? "derailed" : r.World.Train.Boiler.Ruptured ? "the boiler ruptured" : gone is null ? $"still in after {r.Since:0} s" : $"gone at {gone:0} s");
                }
            case "uncoupled-car":
                return Uncoupled(c);
            default:
                throw new ArgumentOutOfRangeException(nameof(name), $"no cascade called {name}");
        }
    }

    static uint At(double seconds) => (uint)Math.Round(seconds * SimConstants.TickRate);

    /// <summary>§23 / spec B.6: the burst. Pressure and fire to nothing, the engine seized.</summary>
    static void Rupture(World w)
    {
        w.Train.Boiler.Ruptured = true;
        w.Train.Boiler.Pressure = 0;
        w.Train.Boiler.Firebox = 0;
    }

    /// <summary>The engineering kit taken out of its locker and put down on a car's floor.</summary>
    static void MoveKit(World w, int car)
    {
        if (w.Bodies.All.FirstOrDefault(b => b.Kind == Physics.BodyKind.RepairKit && b.Carrier < 0) is not { } kit
            || w.Train.Frames[car].Shape.Interior is not { } room)
            return;
        w.Bodies.Remove(kit);
        w.Bodies.SpawnCrate(w.Train, car, new Double3(0.4, room.Min.Y + 0.1, room.Centre.Z), Physics.BodyKind.RepairKit);
    }

    static string KitWhere(World w) => w.Bodies.All.FirstOrDefault(b => b.Kind == Physics.BodyKind.RepairKit) is { } k
        ? k.Carrier >= 0 ? $"carried by {k.Carrier}" : k.Parent >= 0 ? $"in car {k.Parent}{(k.Stowed ? $" (locker {k.Locker}{(w.Train.Vehicles[k.Parent].LockerOpen(k.Locker) ? ", open" : "")})" : $" @{k.Centre.X:0.0},{k.Centre.Z:0.0}")}" : "on the ground"
        : "gone";

    sealed record NightResult(World World, double Since);

    /// <summary>
    /// A night on a straight line with the crew as bots over a clean loopback (the chain's the test, not the link), nothing
    /// sent by the director, <paramref name="script"/> breaking something, and <paramref name="done"/> asked each tick after
    /// the failure (with the seconds since it) whether it's over.
    /// </summary>
    static NightResult Night(AuditContent c, int seed, TextWriter? trace, double window, Action<uint, World> script, Func<World, double, bool> done)
    {
        var t = c.Balance.Cascades;
        var line = new RailLine(new LineDefinition("cascade", [new TrackSegment(80_000)]));
        World? last = null;
        double since = 0;
        bool over = false;
        Harness.Run(line, c.Train, c.Player, new HarnessOptions
        {
            Bots = t.Crew,
            Cars = t.Cars,
            Seed = seed,
            Seconds = t.FailAtSeconds + window + 2,
            Link = new Ballast.Net.LinkConditions(0, 0, 0),
            StartDistance = 600,
            Combat = c.Combat,
            Enemies = c.Enemies,
            Insist = [],
            Script = (tick, w) =>
            {
                last = w;
                script(tick, w);
            },
            Observe = (tick, crew, w) =>
            {
                last = w;
                if (tick < At(t.FailAtSeconds) || over)
                    return;
                if (trace is not null && (tick - At(t.FailAtSeconds)) % SimConstants.TickRate == 0)
                    trace.WriteLine($"{since,5:0}s {w.Train.Dynamics.Speed:0.0}m/s P{w.Train.Boiler.Pressure:0}{(w.Train.Boiler.Ruptured ? " RUPTURED" : "")} kit {KitWhere(w)}  "
                        + string.Join(" | ", crew.Select(x => $"{Harness.Describe(x.Bot, x.State)} @{x.State.Position.X:0.0},{x.State.Position.Z:0.0} {x.State.Flags}")));
                since = (tick - At(t.FailAtSeconds)) * SimConstants.TickSeconds;
                over = done(w, since);
            },
            Until = _ => over,
        }, c.Boiler);
        return new NightResult(last!, since);
    }

    /// <summary>
    /// §24 and §23.2: a car cut loose isn't lost while the crew go back for it. Cut at a stand, the train runs on
    /// <see cref="CascadeTuning.CutRunM"/>, stops, and sets back onto it under a walking pace: a driver on the controls (the
    /// coupling is what's under test, which no bot yet goes back for out on the line).
    /// </summary>
    static CascadeResult Uncoupled(AuditContent c)
    {
        var t = c.Balance.Cascades;
        var n = new AuditNight(c, t.Cars, 0, 1) { Speed = null };
        var train = n.Train;
        int cars = train.Dynamics.Consist.Vehicles.Count;
        int rear = train.Dynamics.Consist.Vehicles[^1].Id;
        n.Controls = new TrainControls { Reverser = 1, Brake = 1 };
        n.Run(1, (_, _) => default);
        train.Uncouple(train.VehicleAhead(rear));
        double start = train.Dynamics.Distance, since = 0;
        int leg = 0;
        double limit = c.Train.Couplings.CoupleMaxSpeed * 0.6;
        while (since < t.RecoupleWithinSeconds && train.Dynamics.Consist.Vehicles.Count < cars)
        {
            double v = train.Dynamics.Velocity, gone = train.Dynamics.Distance - start;
            n.Controls = leg switch
            {
                0 when gone < t.CutRunM => new TrainControls { Reverser = 1, Throttle = v < 3 ? 0.5 : 0 },
                0 => new TrainControls { Reverser = 1, Brake = 1 },
                _ => new TrainControls { Reverser = -1, Throttle = Math.Abs(v) < limit ? 0.25 : 0, Brake = Math.Abs(v) > limit ? 0.3 : 0 },
            };
            if (leg == 0 && gone >= t.CutRunM && Math.Abs(v) < 0.05)
                leg = 1;
            n.Run(SimConstants.TickSeconds, (_, _) => default);
            since += SimConstants.TickSeconds;
        }
        bool ok = train.Dynamics.Consist.Vehicles.Count == cars && train.Vehicles[rear].Integrity > 0;
        return new("uncoupled-car", $"the rear car cut loose at a stand, the train {t.CutRunM:0} m on",
            $"set back and coupled up within {t.RecoupleWithinSeconds:0} s", ok, Math.Round(since, 1),
            ok ? $"coupled up at {since:0} s" : $"{train.Dynamics.Consist.Vehicles.Count} of {cars} vehicles after {since:0} s, {train.Rakes.Count} rakes");
    }
}
