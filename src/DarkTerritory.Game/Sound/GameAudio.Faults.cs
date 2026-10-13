using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The train's faults on the audio checklist: a cannon fouled (state-cannon-foul; GDD §23 "Cannon fouls: someone clears it
/// by hand, under fire") and a car breached (state-breach, decided 1 Oct), with the crew boarding it up (crew-repair's
/// board-place, hammer and done). Read off the replicated gun (<see cref="GunState.Jammed"/>, its <see cref="GunState.ReloadProgress"/>
/// while it's cleared), the replicated car (<see cref="Vehicle.Breached"/>, <see cref="Vehicle.BreachAt"/>) and each
/// crewmate's record (their hold's <see cref="PlayerState.ActionProgress"/> at the hole), so a client hears what the host does.
/// The breach's other sound is the train's: inside a breached car the outside isn't muffled (<see cref="PlayerMotor.Space"/>
/// counts it outside) and its wind blows in (<see cref="BreachedAround"/>).
/// </summary>
public sealed partial class GameAudio
{
    /// <summary>A clear's or a board's count that's stopped rising this long (s) has stopped: a lost packet isn't a pause.</summary>
    const double FaultHoldGrace = 0.2;
    // The fouled bore's pfft comes a beat after the shot that fouled it, clear of the boom's attack (s).
    const double FizzleAfterMisfire = 0.9;
    // Presentation, not design: the first nail this long after the board's put to the hole, then one a blow this often.
    const double HammerFirst = 0.35, HammerEvery = 0.55;

    sealed class FaultGun
    {
        public bool Mounted, Jammed;
        public double Progress, LastRise = double.NegativeInfinity;
    }

    sealed class FaultCar
    {
        public bool Breached;
        public Double3 At;
        public double HotBox, Loose, NextKnock, TightenedAt = double.NegativeInfinity;
    }

    /// <summary>A loose pin's knocks a second, just come loose and about to drop: D1's synth's pace (note 356).</summary>
    const double KnockFrom = 1.4, KnockTo = 5.5;

    /// <summary>A hot box that goes cool this near its catching (or nearer) caught: its car's fire is the sound, not a greasing.</summary>
    const double CaughtWithin = 1.0;

    sealed class Boarder
    {
        public double Progress, LastRise = double.NegativeInfinity, NextBlow;
        public bool Placed;
    }

    readonly Dictionary<int, FaultGun> _faultGuns = new();
    readonly Dictionary<int, FaultCar> _faultCars = new();
    readonly Dictionary<int, Boarder> _boarders = new();
    // The wrench's mends: each car's shell as it was (mendable or not) and when a crewmate was last at its dent; the lamp.
    readonly Dictionary<int, (bool Mendable, double At)> _mending = new();
    bool? _lampSmashed;
    double _lampMendedAt = double.NegativeInfinity;
    bool _ownFireWas;

    partial void FaultSounds(World world)
    {
        FoulSounds(world);
        BreachSounds(world);
        WrenchSounds(world);
    }

    partial void EndNightFaults()
    {
        _faultGuns.Clear();
        _faultCars.Clear();
        _boarders.Clear();
        _ownFireWas = false;
        _mending.Clear();
        _lampSmashed = null;
        _lampMendedAt = double.NegativeInfinity;
    }

    /// <summary>
    /// A gun's foul (note 183: a shot fouls the bore as it goes): the damp charge's feeble pfft out of the vent a beat after
    /// that shot, everyone's gun, from its state, so the crew hear it's fouled; the dead click at each pull of your own trigger
    /// on a fouled gun you're at; the bore worked by hand while it's being cleared; and cleared.
    /// </summary>
    void FoulSounds(World world)
    {
        var train = world.Train;
        float outside = Occlusion(PlayerMotor.Outside);
        bool fire = OwnIntent.Has(PlayerButtons.Fire), pulled = fire && !_ownFireWas;
        _ownFireWas = fire;
        int? mine = null;
        if (pulled && world.Combat is { } combat)
            foreach (var (id, s) in CrewStates)
                if (id == OwnId)
                    mine = Guns.MannedGun(s, train, combat.Guns);
        foreach (var v in train.Vehicles)
        {
            if (v.Id >= train.Frames.Count)
                continue;
            var g = v.Gun;
            if (!_faultGuns.TryGetValue(v.Id, out var m))
            {
                // Fouled already when first seen: that's how it is, not something happening.
                _faultGuns[v.Id] = new FaultGun { Mounted = g.Mounted, Jammed = g.Jammed, Progress = g.ReloadProgress };
                continue;
            }
            if (g.Mounted && m.Mounted && Guns.Mount(train, v.Id) is { } mount)
            {
                // At the touch hole, where the priming didn't take and where it's picked clear.
                var vent = train.Frames[v.Id].ToWorld(mount.Position - mount.Facing * 0.55 + Double3.Up * 0.15);
                if (g.Jammed && m.Jammed && mine == v.Id)
                    Cue("state-cannon-foul.misfire", vent, outside);
                // As it fouls, after the shot that fouled it, the damp charge's pfft out of the vent (crew-mishaps).
                if (g.Jammed && !m.Jammed)
                    CrewAfter(FizzleAfterMisfire, "crew-mishaps.foul-fizzle", vent, outside);
                if (g.Jammed && m.Jammed && g.ReloadProgress > m.Progress + 1e-6)
                    m.LastRise = _time;
                if (g.Jammed && g.ReloadProgress > 0 && _time - m.LastRise < FaultHoldGrace)
                    Hold("state-cannon-foul.clear", v.Id, vent, outside);
                if (m.Jammed && !g.Jammed)
                    Cue("state-cannon-foul.cleared", vent, outside);
            }
            m.Mounted = g.Mounted;
            m.Jammed = g.Jammed;
            m.Progress = g.ReloadProgress;
        }
    }

    /// <summary>
    /// A car's breach: the shell giving way, once, at the hole, loud enough to carry; for a listener inside a breached car, the
    /// outside coming in through it; and its boarding up: a board put to the hole as each hold begins, a nail hammered every
    /// so often while the hold goes on, and done when it's shut.
    /// </summary>
    void BreachSounds(World world)
    {
        var train = world.Train;
        float outside = Occlusion(PlayerMotor.Outside);
        foreach (var v in train.Vehicles)
        {
            if (v.Id >= train.Frames.Count)
                continue;
            // A hot axle box (note 331): its squeal at the rear bogie, harsher as it heats, and its smoke from halfway.
            if (v.HotBox > 0 && train.HotBoxTuning is { } hb)
                Hold("hotbox", v.Id, train.Frames[v.Id].ToWorld(HotBoxes.Box(train.Frames[v.Id].Shape, hb)), outside)?
                    .Params.Set("heat", Math.Clamp(v.HotBox / hb.FireAfter, 0, 1));
            var hole = train.Frames[v.Id].ToWorld(v.BreachAt);
            if (!_faultCars.TryGetValue(v.Id, out var c))
            {
                _faultCars[v.Id] = new FaultCar { Breached = v.Breached, At = hole, HotBox = v.HotBox, Loose = v.Loose };
                continue;
            }
            LooseSounds(train, v, c, outside);
            if (v.Breached && !c.Breached)
                Cue("state-breach.breach", hole, outside);
            // Boarded up: the last nail inside a car that's shut again (heard through its walls from another).
            if (c.Breached && !v.Breached)
                Cue("crew-repair.done", hole, Occlusion(v.Id));
            c.Breached = v.Breached;
            c.At = hole;
            // Greased (note 358): the box gone cool before it could catch, the last of the grease hissing off the iron.
            if (c.HotBox > 0 && v.HotBox <= 0 && train.HotBoxTuning is { } cooled && c.HotBox < cooled.FireAfter - CaughtWithin)
                Cue("crew-upkeep.greased", train.Frames[v.Id].ToWorld(HotBoxes.Box(train.Frames[v.Id].Shape, cooled)), outside);
            c.HotBox = v.HotBox;
        }
        if (BreachedAround(world, Mixer.Listener.Position) is { } car)
            Hold("state-breach.open-to-outside", car, train.Frames[car].ToWorld(train.Vehicles[car].BreachAt));

        // Greasing a hot box (note 358): the grease gun worked at it while a crewmate's hold there goes on.
        if (train.HotBoxTuning is { } hbt)
            foreach (var (id, s) in CrewStates)
                if (s.ActionProgress > 0 && HotBoxes.Within(s, train, hbt) is { } box && train.Vehicles[box].HotBox > 0)
                    Hold("crew-upkeep.grease", id, train.Frames[box].ToWorld(HotBoxes.Box(train.Frames[box].Shape, hbt)), outside);
        // Tightening a loose coupling (note 385): the wrench on the pin's nut while a crewmate's hold there goes on.
        if (train.Loose is { } lt)
            foreach (var (id, s) in CrewStates)
                if (s.ActionProgress > 0 && Couplings.Within(s, train, lt) is { } gap && Couplings.Tightens(s, train))
                {
                    Hold("crew-upkeep.tighten", id, Pin(train, gap), outside);
                    if (_faultCars.TryGetValue(gap, out var tightening))
                        tightening.TightenedAt = _time;
                }

        foreach (var (id, s) in CrewStates)
        {
            if (Breaches.Within(s, train, world.Hand) is not { } at)
            {
                _boarders.Remove(id);
                continue;
            }
            if (!_boarders.TryGetValue(id, out var b))
                _boarders[id] = b = new Boarder();
            var hole = train.Frames[at].ToWorld(train.Vehicles[at].BreachAt);
            if (s.ActionProgress > b.Progress + 1e-6)
            {
                if (!b.Placed)
                {
                    Cue("crew-repair.board-place", hole, outside);
                    b.NextBlow = _time + HammerFirst;
                }
                b.Placed = true;
                b.LastRise = _time;
            }
            // Let go, and the next hold puts up a board again.
            if (s.ActionProgress <= 0)
                b.Placed = false;
            // Nailing it, a blow at a time, while the hold goes on.
            if (b.Placed && _time - b.LastRise < FaultHoldGrace && _time >= b.NextBlow)
            {
                Cue("crew-repair.hammer", hole, outside);
                b.NextBlow = _time + HammerEvery;
            }
            b.Progress = s.ActionProgress;
        }
    }

    /// <summary>
    /// A loose coupling (note 356; queue #122, note 385): the pin knocking in its gap, a take a knock, faster and harder as it
    /// works out (<see cref="KnockFrom"/> to <see cref="KnockTo"/> a second over its <see cref="LooseTuning.PartAfter"/>), or
    /// D1's synth loop where no knock's installed. Gone tight with a crewmate's wrench on it a moment ago, the pin seated
    /// home; left to drop, the parting is the knuckle's and the hoses' (CrewCouplings).
    /// </summary>
    void LooseSounds(TrainOnLine train, Vehicle v, FaultCar c, float outside)
    {
        if (train.Loose is not { } lt)
            return;
        if (v.Loose > 0)
        {
            double loose = Math.Clamp(v.Loose / lt.PartAfter, 0, 1);
            if (!HasCue("state-coupling-loose.knock"))
                Hold("coupling-loose", v.Id, Pin(train, v.Id), outside)?.Params.Set("loose", loose);
            else if (_time >= c.NextKnock)
            {
                if (c.NextKnock > 0)
                    Cue("state-coupling-loose.knock", Pin(train, v.Id), outside, (float)(0.55 + 0.45 * loose));
                // A little uneven, as iron working in a knuckle is: never a metronome.
                c.NextKnock = _time + (0.85 + 0.3 * _creatureRng.Next()) / (KnockFrom + (KnockTo - KnockFrom) * loose);
            }
        }
        else
            c.NextKnock = 0;
        if (c.Loose > 0 && v.Loose <= 0 && _time - c.TightenedAt <= FaultHoldGrace + SimConstants.TickSeconds)
            Cue("crew-upkeep.tightened", Pin(train, v.Id), outside);
        c.Loose = v.Loose;
    }

    /// <summary>
    /// The wrench's other mends (note 301's slice 2; queue #122, note 385), each held while a crewmate's hold there goes on: a
    /// dent beaten out (a car's wall from inside, the engine's boiler flank from its running board), and the smashed headlamp
    /// put right from the cab's front windows. Whole again, or lit again, with the wrench at it a moment ago: done.
    /// </summary>
    void WrenchSounds(World world)
    {
        var train = world.Train;
        if (!Repairs.ByWrench(train))
            return;
        var lamp = train.Frames[0].ToWorld(Repairs.LampAt(train));
        foreach (var (id, s) in CrewStates)
        {
            if (s.ActionProgress <= 0)
                continue;
            if (Repairs.Lamp(s, train))
            {
                Hold("crew-repair.lamp", id, lamp, Occlusion(0));
                _lampMendedAt = _time;
            }
            else if (Repairs.Dent(s, train, world.Hand) is { } car && Repairs.DentAt(train, car) is { } dent)
            {
                // The engine's is out on its running board; a car's is inside it.
                Hold("crew-repair.dent", id, train.Frames[car].ToWorld(dent), Occlusion(car == 0 ? PlayerMotor.Outside : car));
                _mending[car] = (true, _time);
            }
        }
        bool smashed = Repairs.LampSmashed(train);
        if (_lampSmashed == true && !smashed && _time - _lampMendedAt <= FaultHoldGrace + SimConstants.TickSeconds)
            Cue("crew-repair.done", lamp, Occlusion(0));
        _lampSmashed = smashed;
        foreach (var car in _mending.Keys.ToList())
        {
            var (was, at) = _mending[car];
            bool mendable = Repairs.Mendable(train, car);
            if (was && !mendable && _time - at <= FaultHoldGrace + SimConstants.TickSeconds && Repairs.DentAt(train, car) is { } dent)
                Cue("crew-repair.done", train.Frames[car].ToWorld(dent), Occlusion(car == 0 ? PlayerMotor.Outside : car));
            if (mendable)
                _mending[car] = (true, at);
            else
                _mending.Remove(car);
        }
    }

    /// <summary>The pin of the coupling behind this car, in the world.</summary>
    static Double3 Pin(TrainOnLine train, int car) =>
        train.Frames[car].ToWorld(Couplings.Pin(train.Frames[car].Shape, train.Dynamics.Tuning));

    /// <summary>The breached car a listener's ear is inside the walls of, if any: there, the night comes in as on the roof.</summary>
    public static int? BreachedAround(World world, Double3 ear)
    {
        var train = world.Train;
        foreach (var frame in train.Frames)
        {
            if (frame.Index <= 0 || (frame.Origin - ear).Length > frame.Shape.HalfLength + 5 || !train.Vehicles[frame.Index].Breached)
                continue;
            if (frame.Shape.Interior is { } room && room.Contains(frame.ToLocal(ear)))
                return frame.Index;
        }
        return null;
    }
}
