using System.Numerics;
using Ballast;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The Wakers heard (queue #502, note 772; note 588, docs/design/creatures/wakers.md §2-§5). In the stir, far-off thuds on
/// the same beat as the train's tremor and the lamps' swing (<see cref="DawnStir.Thud"/>), stronger and closer together as
/// dawn comes, and long low calls from behind the hills. At dawn, the land getting up; its footfalls as it runs the train
/// down, on its own gait (<see cref="WakerGait"/>), and its roars; the last car seized, the train dragged to a stand, each car
/// torn off the rails and each eaten. Stood at the walls, the town's guns firing on it (<see cref="WallGunSounds"/>). Those
/// who ran (note 710): one rising beside them, striding over, a grip, a swallow.
/// </summary>
public sealed partial class GameAudio
{
    /// <summary>The stir's calls: about every this many seconds at its start, twice as often by dawn.</summary>
    const double WakerCallEvery = 30;
    /// <summary>How far off the stir's thuds and calls are placed, out where they'll rise (m).</summary>
    const double StirFar = 700;
    /// <summary>A roar while it runs the train down or stands at the walls: about every this many seconds.</summary>
    const double WakerRoarEvery = 11;
    /// <summary>One who ran: the Waker after them strides this far (m) a footfall (its walk's half cycle).</summary>
    const double PickerStride = WakerGait.WalkCycleMetres / 2;

    /// <summary>The app's clock for the stir (its <c>timer</c>), so the thuds land with the tremor it draws; headless, ours.</summary>
    public double? StirClock { get; set; }
    double _stirSince = double.PositiveInfinity, _wakerCall;

    /// <summary>The app's clock as the town's guns were last heard by: a shot is heard when its beat fell since.</summary>
    double _gunsHeardTo = double.NaN;
    /// <summary>The walls' shots heard fired, by target and shot (note 834): only a ball heard fired is heard landing, not one
    /// already in the air when the target came to stand under the guns.</summary>
    readonly HashSet<(long Seed, long Shot)> _gunShots = [];
    /// <summary>The town's guards' pieces among the guns (note 608) are rifles: heard this much under the walls' guns.</summary>
    const float YardGunVolume = 0.7f;

    /// <summary>
    /// The town's guns heard (note 772; note 599, wakers.md §4): each gun on the gatehouse and the towers firing on a Waker
    /// stood at the walls, and the ball landing on its back a moment after; and the walls' guns with the guards' pieces on
    /// whatever was brought in through the gate (note 589, 608). On the beat the scene draws them by
    /// (<see cref="Effects.WallGunBeat"/>, the app's clock), so each flash is heard.
    /// </summary>
    void WallGunSounds(World world)
    {
        double now = StirClock ?? _time, was = _gunsHeardTo;
        _gunsHeardTo = now;
        // A first tick, or a clock that jumped (a pause, a rejoin): nothing to catch up on.
        if (double.IsNaN(was) || now <= was || now - was > 1 || world.Enemies is null)
            return;
        var (line, frames) = (world.Train.Line, world.Train.Frames);
        var route = world.Route ?? world.Run?.Route;
        foreach (var e in world.ActiveEnemies)
        {
            if (e.Gone)
                continue;
            if (e is Waker { Picker: false } w)
            {
                if (w.Phase != SpinePhase.Commit || world.Enemies.Wakers is not { } t)
                    continue;
                double wall = GreyboxScene.WakerWall(line, route, world.Run, t);
                if (w.LineDistance < wall - 1)
                    continue;
                var guns = world.Arrival is { Plan.Bounds: not null } walled ? GreyboxScene.WallGuns(walled, Double3.Zero)
                    : GreyboxScene.WallGuns(line, wall + t.StopShort, Double3.Zero);
                var at = line.Sample(w.LineDistance);
                var feet = at.Position + Double3.Cross(at.Tangent, Double3.Up).Normalized * w.Lateral + Double3.Up * w.Height;
                Volley(guns, guns.Count, feet + Double3.Up * GreyboxScene.WakerBack, w.Id, was, now);
            }
            else if (e.Kind is not (EnemyKind.Waker or EnemyKind.Passenger) && world.Arrival is { } town
                && (e.Attached >= 0 && e.Attached < frames.Count ? frames[e.Attached].ToWorld(e.Local) : e.Local) is var inside
                && GreyboxScene.InTheTown(line, route, inside))
            {
                var walls = GreyboxScene.WallGuns(town, Double3.Zero);
                Volley([.. walls, .. GreyboxScene.YardGuns(town, Double3.Zero)], walls.Count, inside + Double3.Up * 1.2, e.Id, was, now);
            }
        }
    }

    /// <summary>
    /// The shots fired and landed between <paramref name="was"/> and <paramref name="now"/>: a gun's report at its muzzle (the
    /// first <paramref name="walls"/> of them the walls' guns, the rest the guards' rifles), and each wall gun's ball heard fired landing
    /// on <paramref name="target"/> its flight later.
    /// </summary>
    void Volley(IReadOnlyList<Vector3> guns, int walls, Double3 target, long seed, double was, double now)
    {
        for (int g = 0; g < guns.Count; g++)
        {
            var muzzle = new Double3(guns[g].X, guns[g].Y, guns[g].Z);
            var (since, shot) = Effects.WallGunBeat(now, seed, g);
            if (since < now - was)
            {
                if (g < walls)
                {
                    Cue("town-guns.fire", muzzle, 0);
                    if (_gunShots.Count > 256)
                        _gunShots.Clear();
                    _gunShots.Add((seed, shot));
                }
                else
                    Cue("gunshot", muzzle, 0, YardGunVolume);
            }
            double flight = (target - muzzle).Length / Effects.WallShotSpeed;
            if (g < walls && since >= flight && since - (now - was) < flight && _gunShots.Remove((seed, shot)))
                Cue("town-guns.hit", target, 0);
        }
    }

    /// <summary>The stir, before any of them is up and running: its thuds and its calls.</summary>
    void StirSounds(World world)
    {
        if (world.Enemies?.Wakers is not { Enabled: true } || world.Run is not { Over: false })
        {
            _stirSince = double.PositiveInfinity;
            _wakerCall = 0;
            return;
        }
        // Once one runs, the thuds are its footfalls (heard from it, WakerSounds).
        if (world.ActiveEnemies.OfType<Waker>().Any(w => !w.Gone && !w.Picker && w.Phase is SpinePhase.Commit or SpinePhase.Punish))
            return;
        var (strength, since) = DawnStir.Thud(world, StirClock ?? _time);
        if (strength <= 0)
        {
            _stirSince = double.PositiveInfinity;
            _wakerCall = 0;
            return;
        }
        var far = Mixer.Listener.Position + DawnStir.SkyFrom(world, world.Train.Frames) * StirFar;
        if (since < _stirSince)
            Cue("tell-wakers.thud", far, 0, (float)(0.35 + 0.65 * strength));
        _stirSince = since;
        if (_wakerCall <= 0)
            _wakerCall = _time + WakerCallEvery * (0.3 + 0.5 * _creatureRng.Next());
        else if (_time >= _wakerCall)
        {
            var aside = Double3.Cross(Double3.Up, DawnStir.SkyFrom(world, world.Train.Frames)) * ((_creatureRng.Next() - 0.5) * StirFar);
            Cue("tell-wakers.call", far + aside + Double3.Up * 60, 0, (float)(0.5 + 0.5 * strength));
            _wakerCall = _time + WakerCallEvery * (1 - 0.5 * strength) * (0.7 + 0.6 * _creatureRng.Next());
        }
    }

    void WakerSounds(World world, Waker w, Creature was, Double3 at)
    {
        var t = world.Enemies?.Wakers ?? new WakersTuning();
        bool began = w.Phase != was.Phase;
        if (w.Phase == SpinePhase.Telegraph && began)
            Cue("tell-wakers.rise", at, 0);
        if (w.Picker)
        {
            PickerWakerSounds(world, w, was, at, began, t);
            return;
        }
        var train = world.Train;
        var cars = train.Dynamics.Consist.Vehicles;
        switch (w.Phase)
        {
            case SpinePhase.Commit:
                {
                    // Its footfalls on its own gait (the tremor's, DawnStir.Thud), and stood at the walls, none.
                    double since = WakerGait.SinceFootfall(w.PhaseSeconds, t);
                    bool stood = Math.Abs(w.LineDistance - was.LineDistance) < 1e-3 && !began;
                    if (!stood && since < was.Next && !began)
                        Cue("tell-wakers.stride", at, 0, w.Lead ? 1 : 0.7f);
                    was.Next = since;
                    WakerRoars(was, at + Double3.Up * 50);
                    break;
                }
            case SpinePhase.Punish:
                {
                    var rear = train.Frames[cars[^1].Id].Origin;
                    if (began)
                        Cue("cs-wakers.grab", rear + Double3.Up * 2, 0);
                    if (train.Dynamics.Speed > 0.3)
                        Hold("cs-wakers.drag", w.Id, train.Frames[cars[^1].Id].Origin, 0);
                    int lifted = Lifted(was.Extra, t, cars.Count, began), nowLifted = w.Lifted(t, cars.Count);
                    for (int k = lifted; k < nowLifted; k++)
                        if (k > 0)
                            Cue("cs-wakers.lift", train.Frames[cars[cars.Count - 1 - k].Id].Origin + Double3.Up * 3, 0);
                    int eaten = began ? 0 : Eaten(was.Extra, t, cars.Count), nowEaten = w.EatenCars(t, cars.Count);
                    for (int k = eaten; k < nowEaten; k++)
                        Cue("cs-wakers.eat", train.Frames[cars[cars.Count - 1 - k].Id].Origin + Double3.Up * 6, 0);
                    break;
                }
        }
    }

    /// <summary>A Waker's first tick is its getting up (Waker.Rise and Beside enter TELEGRAPH at once): heard here.</summary>
    void WakerFirstSeen(Enemy e, Double3 at)
    {
        if (e is Waker { Phase: SpinePhase.Telegraph })
            Cue("tell-wakers.rise", at, 0);
    }

    /// <summary>A roar now and then while it comes on (or stands roaring at the walls).</summary>
    void WakerRoars(Creature was, Double3 head)
    {
        if (was.ModeAt <= 0)
            was.ModeAt = _time + WakerRoarEvery * (0.4 + 0.6 * _creatureRng.Next());
        else if (_time >= was.ModeAt)
        {
            Cue("tell-wakers.roar", head, 0);
            was.ModeAt = _time + WakerRoarEvery * (0.7 + 0.6 * _creatureRng.Next());
        }
    }

    /// <summary>The cars it had lifted at <paramref name="extra"/> seconds in (Waker.Lifted, from last tick's record).</summary>
    static int Lifted(double extra, WakersTuning t, int cars, bool began) =>
        began ? 0 : Math.Min(cars, 1 + (int)Math.Floor(extra / Math.Max(1e-6, t.LiftSeconds)));

    /// <summary>The cars it had eaten at <paramref name="extra"/> seconds in (Waker.EatenCars).</summary>
    static int Eaten(double extra, WakersTuning t, int cars) =>
        Math.Min(cars, (int)Math.Floor(Math.Max(0, extra - t.EatAfter) / Math.Max(1e-6, t.EatSeconds)));

    /// <summary>One risen beside a crewmate who ran (note 710): its strides to them, its grip on them, and the swallow.</summary>
    void PickerWakerSounds(World world, Waker w, Creature was, Double3 at, bool began, WakersTuning t)
    {
        switch (w.Phase)
        {
            case SpinePhase.Commit:
                // Setting off, a foot's down half a stride in.
                if (began)
                    was.Stride = PickerStride / 2;
                was.Stride += ((at - was.At) with { Y = 0 }).Length;
                if (was.Stride >= PickerStride)
                {
                    Cue("tell-wakers.stride", at, 0, 0.8f);
                    was.Stride = 0;
                }
                break;
            case SpinePhase.Grab when began:
                Cue("cs-wakers.take", Victim(world.Train, w.Holding, 1.0) ?? at, 0);
                break;
            case SpinePhase.Punish when began:
                Cue("cs-wakers.swallow", Waker.Mouth(w.Local, w.Lateral, t), 0);
                break;
        }
    }
}
