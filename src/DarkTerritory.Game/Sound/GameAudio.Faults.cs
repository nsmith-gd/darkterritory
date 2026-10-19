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
    // The pre-cut boards' run of quick blows (up-tells.precut's takes, about 1.5 s): the hammering goes on after it.
    const double PrecutRun = 1.5;

    sealed class FaultGun
    {
        public bool Mounted, Jammed;
        public double Progress, LastRise = double.NegativeInfinity, Glinted = double.NegativeInfinity;
        public uint Shot;
    }

    sealed class FaultCar
    {
        public bool Breached;
        public Double3 At;
        public double HotBox, Loose, NextKnock, NextRattle, TightenedAt = double.NegativeInfinity;
        /// <summary>Note 865: what its pin was working loose from last tick (a warning that's just jumped is heard so).</summary>
        public LooseCause Cause;
    }

    /// <summary>A loose pin's knocks a second, just come loose and about to drop: D1's synth's pace (note 356).</summary>
    const double KnockFrom = 1.4, KnockTo = 5.5;
    /// <summary>
    /// Presentation (note 732): a pin's first knock this soon after the moment it jumped, so the knocking is plainly what the
    /// slam, the clash or the lurch began; and its loudness, from a clear knock just loose to full about to drop.
    /// </summary>
    const double FirstKnockAfter = 0.6, KnockGainFrom = 0.7;
    /// <summary>
    /// Note 865: a pin working loose before it jumps knocks this many times a second, from the start of its warning to its end
    /// (twice a worked-loose pin's, and climbing), as loud as a knock goes; and the pin rattles in its knuckle between.
    /// </summary>
    const double WarnKnockFrom = 3, WarnKnockTo = 10, RattleEvery = 0.45;
    /// <summary>
    /// Note 865: the warning's knock carries the length of the train (the director: "hella telegraphing"). Until AU1's own cue
    /// for it (<c>state-coupling-loose.warn</c>, long-carrying) is installed, the knock's played from no further than this off
    /// the ear (m), toward the gap: the knock's own sound stops at 60 m, and the cab is further than that from most gaps.
    /// </summary>
    const double WarnCarries = 45;

    /// <summary>Note 865: where a warning's knock is heard from: the pin, or toward it from the ear, no further than <see cref="WarnCarries"/>.</summary>
    Double3 Carried(Double3 pin)
    {
        var ear = Mixer.Listener.Position;
        var to = pin - ear;
        double d = to.Length;
        return d <= WarnCarries ? pin : ear + to * (WarnCarries / d);
    }

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
                _faultGuns[v.Id] = new FaultGun { Mounted = g.Mounted, Jammed = g.Jammed, Progress = g.ReloadProgress, Shot = g.LastShotTick };
                continue;
            }
            if (g.Mounted && m.Mounted && Guns.Mount(train, v.Id) is { } mount)
            {
                // At the touch hole, where the priming didn't take and where it's picked clear.
                var vent = train.Frames[v.Id].ToWorld(mount.Position - mount.Facing * 0.55 + Double3.Up * 0.15);
                if (g.Jammed && m.Jammed && mine == v.Id)
                    Cue("state-cannon-foul.misfire", vent, outside);
                // The lucky horseshoe (queue #505, note 775): a shot that would have fouled without it glints.
                if (g.LastShotTick != m.Shot && g.LastShotTick > 0 && !g.Jammed && _time - m.Glinted > GlintAgain
                    && HasCue("up-tells.horseshoe") && HorseshoeSaved(world, g.LastShotTick, v.Id))
                {
                    m.Glinted = _time;
                    Cue("up-tells.horseshoe", vent, outside);
                }
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
            m.Shot = g.LastShotTick;
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
                    // Pre-cut boards (queue #505, note 775): cut to the hole, nailed light and quick, in place of the first blows.
                    if (Fitted(train, "precutBoards", "up-tells.precut"))
                    {
                        Cue("up-tells.precut", hole, outside);
                        b.NextBlow = _time + PrecutRun;
                    }
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
    /// D1's synth loop where no knock's installed; from the moment it jumped, heard as what jumped it (<see cref="PinJumped"/>,
    /// note 732), the first knock straight after. Gone tight with a crewmate's wrench on it a moment ago, the pin seated
    /// home; left to drop, the parting is the knuckle's and the hoses' (CrewCouplings).
    /// </summary>
    void LooseSounds(TrainOnLine train, Vehicle v, FaultCar c, float outside)
    {
        if (train.Loose is not { } lt)
            return;
        // The moment it jumped (note 732), heard as what jumped it; the knocking follows straight on.
        if (v.Loose > 0 && c.Loose <= 0 && v.LooseCause != LooseCause.None)
        {
            PinJumped(train, v, outside);
            c.NextKnock = _time + FirstKnockAfter;
        }
        // Note 865: the warning's over and the pin's out: heard as loud as anything on the train, the slack's snatch and the
        // pin flung up in its knuckle, twice over.
        if (v.Loose > 0 && c.Cause == LooseCause.Working && v.LooseCause == LooseCause.Jumped)
        {
            PinJumped(train, v, outside);
            Cue("crew-coupling.pin-lift", Pin(train, v.Id), outside, 1.4f);
            c.NextKnock = _time + FirstKnockAfter;
        }
        c.Cause = v.Loose > 0 ? v.LooseCause : LooseCause.None;
        // Note 865: working loose, the warning. The knock quickens from three a second to ten as it comes to jumping, full loud,
        // and the pin rattles in its knuckle between: a sound the crew hears from the cab and the roofs, before anything's gone.
        if (v.Loose > 0 && v.LooseCause == LooseCause.Working && HasCue("state-coupling-loose.knock"))
        {
            double warned = Couplings.Warned(v, lt);
            if (_time >= c.NextKnock)
            {
                if (Cue("state-coupling-loose.warn", Pin(train, v.Id), outside) is null)
                    Cue("state-coupling-loose.knock", Carried(Pin(train, v.Id)), outside, 1.2f);
                c.NextKnock = _time + (0.85 + 0.3 * _creatureRng.Next()) / (WarnKnockFrom + (WarnKnockTo - WarnKnockFrom) * warned);
            }
            if (_time >= c.NextRattle)
            {
                Cue("crew-coupling.pin-lift", Pin(train, v.Id), outside, (float)(0.6 + 0.6 * warned));
                c.NextRattle = _time + RattleEvery * (1.3 - 0.6 * warned) * (0.8 + 0.4 * _creatureRng.Next());
            }
        }
        else if (v.Loose > 0)
        {
            // Note 865: a jumped pin goes sooner, and knocks up to it as quick.
            double loose = Math.Clamp(v.Loose / (v.LooseCause == LooseCause.Jumped ? lt.Jump.PartAfter : lt.PartAfter), 0, 1);
            if (!HasCue("state-coupling-loose.knock"))
                Hold("coupling-loose", v.Id, Pin(train, v.Id), outside)?.Params.Set("loose", loose);
            else if (_time >= c.NextKnock)
            {
                if (c.NextKnock > 0)
                    Cue("state-coupling-loose.knock", Pin(train, v.Id), outside, (float)(KnockGainFrom + (1 - KnockGainFrom) * loose));
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
    /// A pin jumping loose (note 732; the director's playtest of 14 Oct, line 12: "We still have this coupling is loose for no
    /// reason issue. The reason's not obvious"): at the gap, the moment heard as what did it, from the car's record
    /// (<see cref="Vehicle.LooseCause"/>). Each cause's own cue where AU1 has installed it (<c>state-coupling-loose.jump-brake</c>,
    /// <c>.jump-coupled</c>, <c>.jump-bend</c>); till then the train's own sounds stand in: the slack running in hard (a
    /// run-in), the frame's creak (a bend's lurch), and for each the pin lifting in its knuckle. A knock's clash is the coupling
    /// up's own (CrewCouplings).
    /// </summary>
    void PinJumped(TrainOnLine train, Vehicle v, float outside)
    {
        var pin = Pin(train, v.Id);
        string own = v.LooseCause switch
        {
            LooseCause.RunIn => "state-coupling-loose.jump-brake",
            LooseCause.Bend => "state-coupling-loose.jump-bend",
            // Note 865: a rail joint at speed (AU1's cue to come; the slack's snatch stands in).
            LooseCause.Jumped or LooseCause.Working => "state-coupling-loose.jump-joint",
            _ => "state-coupling-loose.jump-coupled",
        };
        if (Cue(own, pin, outside) is not null)
            return;
        if (v.LooseCause is LooseCause.RunIn or LooseCause.Jumped or LooseCause.Working)
            Cue("bed-slack.run-in", pin, outside);
        else if (v.LooseCause == LooseCause.Bend)
            Cue("bed-groan.creak", pin, outside);
        Cue("crew-coupling.pin-lift", pin, outside);
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
