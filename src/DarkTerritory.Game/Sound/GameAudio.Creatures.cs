using Ballast;
using Ballast.Audio;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The creature sounds (tools/audio/cues.py's cs-* lines): what each enemy does, heard where it does it (GDD v1.1 §21, App. A).
/// Everything here is read off the replicated enemy records against last tick's (out/audio/hooks-map.md, the cs-* tables):
/// a phase entered, Extra or Attached changing, Health dropping (a blow: Damage events are host-only), and a record vanishing
/// (enemies are removed the tick they go, so a death is a record gone with its last Health within one blow). Where a
/// crewmate's record says more (who was bitten, who was dragged under), <see cref="CrewStates"/> fills it in; without it each cue
/// falls back on what the enemy's own record says.
/// </summary>
public sealed partial class GameAudio
{
    /// <summary>A drop in Health bigger than any regeneration in a tick (the Car Hugger's, the Grumbler's): a blow landed.</summary>
    const double HitDrop = 0.02;
    /// <summary>How long before a grab's window runs out its end counts as the punish rather than a rescue or a kill.</summary>
    const double WindowSlack = 0.25;
    /// <summary>A gun fired this recently, and a record gone within one round is the round's kill.</summary>
    const double ShotKills = 0.3;
    /// <summary>The pack close behind (cues.py tell-hounds howl-near: "close behind (40-100 m)").</summary>
    const double NearHowlFrom = 40, NearHowlTo = 100;

    // A new night is seen as a new world (_creatureWorld), and that resets everything here.
    partial void EndNightCreatures() => _creatureWorld = null;

    readonly Dictionary<int, Creature> _creatures = new();
    readonly HashSet<int> _liveCreatures = new();
    readonly Dictionary<int, (int Health, DeathCause Death)> _crewHealth = new();
    readonly List<Hurt> _hurts = new();
    readonly Dictionary<int, uint> _shotTicks = new();
    readonly Dictionary<int, BodyKind> _bodies = new();
    readonly HashSet<BodyKind> _bodiesGone = new();
    readonly HashSet<int> _bodyIds = new();
    readonly Dictionary<int, bool> _points = new();
    readonly Dictionary<int, double> _bangs = new();
    readonly HashSet<int> _gallopers = new();
    readonly List<(double At, Action Play)> _later = new();
    readonly Pcg32Ish _creatureRng = new(20261002);
    World? _creatureWorld;
    double _creatureTime, _lastShot = double.NegativeInfinity;

    /// <summary>What a creature's record said last tick, and its own clocks for steps and irregular calls.</summary>
    sealed class Creature
    {
        public EnemyKind Kind;
        public SpinePhase Phase;
        public double PhaseSeconds, Health, Extra, Extra2, GrabWindow, LineDistance, Height;
        public int Id, Attached, Holding, Space;
        public Double3 Local, At;
        // Kept across ticks (not last tick's record): distance toward the next step, the next irregular call, when it last
        // moved and was last hit, and whether its car's breaking away has been heard.
        public double Stride, Next, MovedAt = double.NegativeInfinity, HitAt = double.NegativeInfinity, ModeAt;
        public bool CutAway, Passing;
        // A one-shot that goes where it goes (the Gannet's dive whistle, down its line).
        public SoundInstance? Moving;

        public void Take(Enemy e, Double3 at, int space)
        {
            Kind = e.Kind;
            Phase = e.Phase;
            PhaseSeconds = e.PhaseSeconds;
            Health = e.Health;
            Extra = e.Extra;
            Extra2 = e.Extra2;
            GrabWindow = e.GrabWindow;
            LineDistance = e.LineDistance;
            Height = e.Height;
            Attached = e.Attached;
            Holding = e.Holding;
            Local = e.Local;
            At = at;
            Space = space;
        }
    }

    /// <summary>A crewmate hurt this tick (their replicated Health fell, or they died).</summary>
    readonly record struct Hurt(int Id, Double3 At, int Drop, DeathCause Death);

    partial void CreatureSounds(World world)
    {
        var train = world.Train;
        if (!ReferenceEquals(world, _creatureWorld))
        {
            // A new night (or a new session): nothing heard so far belongs to it.
            _creatureWorld = world;
            _creatures.Clear();
            _crewHealth.Clear();
            _shotTicks.Clear();
            _bodies.Clear();
            _points.Clear();
            _bangs.Clear();
            _later.Clear();
            _lastShot = double.NegativeInfinity;
            _creatureTime = _time;
        }
        double dt = Math.Max(1e-6, _time - _creatureTime);
        _creatureTime = _time;
        Later();
        ShotsHeard(train);
        CrewHurt(train);
        BodiesGone(world);
        Gallopers(world);

        _liveCreatures.Clear();
        foreach (var e in world.ActiveEnemies)
        {
            if (e.Gone)
                continue;
            _liveCreatures.Add(e.Id);
            var at = e.WorldPosition(train);
            int space = SpaceOf(e, train);
            if (_creatures.TryGetValue(e.Id, out var was) && was.Kind == e.Kind)
                Heard(world, e, was, at, space, dt);
            else
            {
                // First sight: nothing has changed yet, so nothing plays; a car already cut loose has been heard going.
                _creatures[e.Id] = was = new Creature { Id = e.Id, CutAway = e.Attached >= 0 && Cut(train, e.Attached) };
            }
            was.Take(e, at, space);
        }
        foreach (var id in _creatures.Keys.Where(k => !_liveCreatures.Contains(k)).ToList())
        {
            Vanished(world, _creatures[id]);
            _creatures.Remove(id);
        }
        Mauled(world);
        ChoirSwarm(world);
        SwitchmanThrows(world);
    }

    /// <summary>One enemy's record this tick against last tick's.</summary>
    void Heard(World world, Enemy e, Creature was, Double3 at, int space, double dt)
    {
        float occ = Occlusion(space);
        bool struck = was.Health - e.Health > HitDrop;
        if (struck)
            was.HitAt = _time;
        bool grabbed = e.Phase == SpinePhase.Grab && was.Phase != SpinePhase.Grab;
        switch (e)
        {
            case TrackDoll doll:
                DollSounds(world, doll, was, at, occ, struck);
                break;
            case CarHugger:
                // App. A.3 SWALLOW: the mouth closing round whoever stood in front of it.
                if (grabbed)
                    Cue("cs-car-hugger.swallow", at, occ);
                // Bludgeoned from the rear platform (it heals between blows: only a real drop is a blow).
                if (struck)
                    Cue("cs-car-hugger.hit", at, occ);
                // Note 310: the crew pull the swallowed crewmate back out. Eaten or pulled free, its grab ends the same way on
                // the wire (back to grinding on the car), so it's the one it held still alive that says they got out.
                if (was.Phase == SpinePhase.Grab && e.Phase != SpinePhase.Grab && Crewmate(was.Holding) is { Health: > 0 })
                    Cue("cs-car-hugger.spit-out", at, occ);
                BreakAway(world.Train, e.Attached, was, at);
                break;
            case Whistler:
                WhistlerSounds(e, was, at, occ, struck, grabbed);
                break;
            case TippyToesie:
                // App. A.5: a hand over the mouth (the victim's voice muffles, World.VoiceEffect), the struggle, and its scurry
                // away when it's seen or pulled off.
                if (grabbed)
                    Cue("cs-tippy.grab", Victim(world.Train, e.Holding, 1.6) ?? at + Double3.Up * 1.4, occ);
                if (e.Phase == SpinePhase.Grab)
                    Hold("cs-tippy.struggle", e.Id, Victim(world.Train, e.Holding, 1.0) ?? at, occ);
                if (was.Phase is SpinePhase.Telegraph or SpinePhase.Commit or SpinePhase.Grab && e.Phase is SpinePhase.Dormant or SpinePhase.BreakOff)
                    Cue("cs-tippy.flee", was.At, Occlusion(was.Space));
                break;
            case Ribbit:
                RibbitSounds(world.Train, e, was, at, occ, struck, grabbed);
                break;
            case ChoirGhost:
                // App. A.7: seizing someone exposed; and a blow on one (it hits back: that's the crew's hurt, not this).
                if (grabbed)
                    Cue("cs-choir.seize", at, occ);
                if (struck)
                    Cue("cs-choir.hit", at, occ);
                break;
            case CinderHound:
                HoundSounds(world.Train, e, was, at, occ, struck, grabbed, dt);
                break;
            case Climber:
                ClimberSounds(e, was, at, occ, struck);
                break;
            case Dragger:
                DraggerSounds(world.Train, e, was, at, grabbed);
                break;
            case Stoker:
                // App. A.5: something moving in the fire while it's in there, behind the fire door's iron when that's shut (heard
                // as through a wall, so it never sits over the cab's tells); clubbed through the open door, it shrieks and the
                // fire bites back at whoever swung (Stoker.Struck: every blow burns the swinger).
                if (e.Phase is SpinePhase.Telegraph or SpinePhase.Commit)
                    Hold("cs-stoker.in-fire", e.Id, at, world.Train.Boiler.FireDoorOpen ? occ : 1);
                if (struck)
                    StokerClubbed(world.Train, at, occ);
                break;
            case Gaunt:
                // App. A.6: nothing before the blow (silence is the tell). A crush when it's beaten someone down; its other blows
                // are heard from the crew's records (Mauled).
                if (grabbed)
                    Cue("cs-gaunt.blow", Victim(world.Train, e.Holding, 1.2) ?? at, occ);
                Pained("cs-gaunt.hit", e, at, occ, struck);
                break;
            case Follower:
                // App. A.6: its nest being beaten in, for as long as the blows keep coming (the swing is 0.8 s).
                if (e.Phase == SpinePhase.Punish && _time - was.HitAt <= 1.2)
                    Hold("cs-followers.nest-smash", e.Id, at, occ);
                // A blow on its nest is the nest's (above); one on the creature itself, off a back, is its own.
                else
                    Pained("cs-followers.hit", e, at, occ, struck);
                break;
            case SootChildren soot:
                SootSounds(soot, was, at, occ, grabbed);
                Pained("cs-soot-children.hit", e, at, occ, struck);
                break;
            case Passenger:
                PassengerSounds(e, was, at, occ);
                break;
            case Switchman sw:
                // The derailer gripping the lever (App. A.8): the junction lamp flickers, again and again, until it throws.
                if (sw.Gripping && _time >= was.Next)
                {
                    Cue("cs-switchman.flicker", Lamp(world, sw.Branch, at), Occlusion(PlayerMotor.Outside));
                    was.Next = _time + 0.25 + 0.55 * _creatureRng.Next();
                }
                Pained("cs-switchman.hit", e, at, occ, struck);
                break;
            case Grumbler g:
                GrumblerSounds(g, was, at, occ);
                // The blow that turns it feral is heard as it turning (GrumblerSounds); one before that, or after, is a hurt.
                if (!(g.Feral && was.Extra2 <= 0.5))
                    Pained("cs-grumbler.hit", e, at, occ, struck);
                break;
            case Moose m:
                MooseSounds(world, m, was, at, occ);
                break;
            case Gannet g:
                GannetSounds(world, g, was, at, occ, dt);
                Pained("cs-gannet-strike.hit", e, at, occ, struck);
                break;
        }
    }

    /// <summary>
    /// A ball or a blow landing on a creature and not killing it (note 290: the gun hits what it's laid on now, and per-creature
    /// pain is the audio chat's). One that kills it is its death, when its record goes (Vanished).
    /// </summary>
    void Pained(string cue, Enemy e, Double3 at, float occ, bool struck)
    {
        if (struck && e.Health > 0)
            Cue(cue, at, occ);
    }

    // ---- The kinds ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The Track Doll haunting (App. A.2). At the controls of an empty cab it moves the regulator (and at its last stage the
    /// brake) in beats of three seconds (TrackDoll.Tamper; note 268); those moves only reach the train through World.Step's
    /// own copy of the controls, never the replicated ones, so the crew's lever hooks can't hear them: they're heard here, as
    /// the crew's own levers. Restless at stage 2 (her last stage coming), she rattles the brake handle on the beat she'd
    /// take it, without moving it. Come at, it vanishes to another car; cornered and clubbed, its porcelain cracks.
    /// </summary>
    void DollSounds(World world, TrackDoll e, Creature was, Double3 at, float occ, bool struck)
    {
        var train = world.Train;
        bool wasTampering = was.Phase == SpinePhase.Punish && was.Attached == 0 && was.Extra2 > 0.5;
        if (e.Tampering)
        {
            double nudge = (world.Enemies?.TrackDoll ?? new()).NudgeThrottle;
            var set = world.Controls;
            var now = TrackDoll.Hands(e.Escalation, e.PhaseSeconds, nudge, set);
            var before = wasTampering ? TrackDoll.Hands(Math.Abs(was.Extra2), was.PhaseSeconds, nudge, set) : set;
            var levers = train.Frames[0].Shape.Levers;
            if (Math.Abs(now.Throttle - before.Throttle) > 0.01)
                Tamper("regulator-notch", levers is { } l ? train.Frames[0].ToWorld(l.RegulatorAt(now.Throttle)) : at);
            bool rattle = e.Stage == 2 && e.Restless && DollBeat(e.PhaseSeconds) == 2 && (!wasTampering || DollBeat(was.PhaseSeconds) != 2);
            if (Math.Abs(now.Brake - before.Brake) > 0.01 || rattle)
                Tamper("brake-handle", levers is { } k ? train.Frames[0].ToWorld(k.BrakeAt(now.Brake)) : at);
        }
        // Gone from where it stood to another car (or to the cab's controls): approached, or bored of the car.
        if (was.Phase == SpinePhase.Punish && e.Phase == SpinePhase.Punish && (e.Attached != was.Attached || (e.Local - was.Local).Length > 0.75))
            Cue("cs-track-doll.vanish", was.At, Occlusion(was.Space));
        // Only cornered can it be struck at all (TrackDoll.Struck), so any drop is a blow on porcelain.
        if (struck)
            Cue("cs-track-doll.crack", at, occ);
    }

    static int DollBeat(double phaseSeconds) => (int)(phaseSeconds / 3) % 3;

    /// <summary>A cab lever moved by nobody: the doll's own cue once it has one (the director kept one), else the crew's lever.</summary>
    void Tamper(string lever, Double3 where)
    {
        if (Cue("cs-track-doll.tamper", where, Occlusion(0)) is null)
            Cue($"crew-cab-controls.{lever}", where, Occlusion(0));
    }

    /// <summary>
    /// The Car Hugger's car dropping away with it (App. A.3: eaten through, or cut loose by the crew): once, the first tick
    /// its car isn't in the engine's rake, at the coupling it parted from. The crew's coupling hooks hear the knuckle part.
    /// </summary>
    void BreakAway(TrainOnLine train, int car, Creature was, Double3 at)
    {
        if (was.CutAway || car < 0 || !Cut(train, car))
            return;
        was.CutAway = true;
        Cue("cs-car-hugger.break-away", GapAhead(train, car) ?? at, Occlusion(PlayerMotor.Outside));
    }

    /// <summary>
    /// The Whistler (App. A.4): the snatch at the gap, its run to the nest with the victim (so the chase can follow it by ear),
    /// the nest once it's there, and the blows that land on it.
    /// </summary>
    void WhistlerSounds(Enemy e, Creature was, Double3 at, float occ, bool struck, bool grabbed)
    {
        if (grabbed)
        {
            Cue("cs-whistler.snatch", at, occ);
            was.MovedAt = _time; // off at a run
        }
        if (e.Phase == SpinePhase.Grab)
        {
            if (was.Phase == SpinePhase.Grab && (e.Local - was.Local).Length > 0.01)
                was.MovedAt = _time;
            // A snapshot or two missed shouldn't stop the run: still for a third of a second, and it's at the nest.
            if (_time - was.MovedAt <= 0.3)
                Hold("cs-whistler.run", e.Id, at, occ);
            else
                Hold("cs-whistler.nest", e.Id, at, occ);
        }
        if (struck)
            Cue("cs-whistler.hit", at, occ);
    }

    /// <summary>
    /// Ribbits (App. A.6). They hop in bursts on the sim's own clock (Ribbit.Hop: a half-second leap, a half-second sit), so a
    /// landing is that clock leaving a leap that went somewhere: robust to a missed snapshot, which a stop in Local isn't. The
    /// leader's tongue, the feed while the target's held, a blow.
    /// </summary>
    void RibbitSounds(TrainOnLine train, Enemy e, Creature was, Double3 at, float occ, bool struck, bool grabbed)
    {
        bool leaping = HopLeaping(e.PhaseSeconds, e.Id), wasLeaping = HopLeaping(was.PhaseSeconds, e.Id);
        if (e.Attached == was.Attached)
            was.Stride += (e.Local - was.Local).Length;
        if (wasLeaping && !leaping && was.Stride > 0.25)
            Cue("cs-ribbits.hop-land", SurfaceOf(e, train), at, occ);
        if (!leaping)
            was.Stride = 0;
        // Only the leader grabs (Ribbit.Tick): Telegraph to Commit to Grab in one tick, the tongue lashing out.
        if (grabbed)
            Cue("cs-ribbits.tongue", at, occ);
        if (e.Phase == SpinePhase.Grab)
            Hold("cs-ribbits.feed", e.Id, Victim(train, e.Holding, 0.6) ?? at, occ);
        if (struck)
            Cue("cs-ribbits.hit", at, occ);
    }

    /// <summary>Where in a gallop's stride each paw falls: the hind pair, then the fore pair.</summary>
    static readonly double[] PawFalls = [0, 0.1, 0.4, 0.5];

    static bool HopLeaping(double phaseSeconds, int id) => (int)(phaseSeconds * 2 + id) % 2 == 0;

    /// <summary>
    /// Cinder Hounds (App. A.3). Running the line, their paws at the gallop's tempo (a four-beat gallop: two pairs to a stride,
    /// strides of a few metres); the leap onto the rear car and its landing on the roof; snarls in the pack fight; the pin's
    /// bite; a yelp for every blow or round that lands. Their bites short of a pin are heard from the crew's records (Mauled).
    /// </summary>
    void HoundSounds(TrainOnLine train, Enemy e, Creature was, Double3 at, float occ, bool struck, bool grabbed, double dt)
    {
        if (e.Attached < 0 && was.Attached < 0)
        {
            double run = Math.Abs(e.LineDistance - was.LineDistance), speed = run / dt;
            if (_gallopers.Contains(e.Id) && speed > 0.5)
            {
                // A stride's length grows with speed (a trot's metre and a bit, a full gallop's six); the paws fall at these
                // fractions of it.
                double stride = Math.Clamp(speed * 0.3, 1.2, 6);
                double from = was.Stride, to = from + run / stride;
                foreach (double paw in PawFalls)
                    for (double k = Math.Floor(from - paw) + 1; k + paw <= to; k++)
                        Cue("cs-hounds.paw", "ground", at, occ, (float)(0.75 + 0.25 * _creatureRng.Next()));
                was.Stride = to - Math.Floor(to);
            }
        }
        if (e.Attached >= 0 && was.Attached < 0)
        {
            // LEAP onto the rear car (CinderHound.Board), and it lands on the roof a moment later.
            Cue("cs-hounds.leap", at, Occlusion(PlayerMotor.Outside));
            var land = at;
            Later(0.28, () => Cue("cs-hounds.paw", "roof", land, Occlusion(PlayerMotor.Outside)));
            Later(0.36, () => Cue("cs-hounds.paw", "roof", land, Occlusion(PlayerMotor.Outside)));
            was.Next = _time + 0.8 + 1.5 * _creatureRng.Next(); // and then it's snarling
        }
        if (struck)
            Cue("cs-hounds.yelp", at, occ);
        // Clubbed off whoever it pinned, it's snarling on the car (Telegraph aboard): after its yelp, if that's what this was.
        if (e.Phase == SpinePhase.Telegraph && was.Phase != SpinePhase.Telegraph && e.Attached >= 0)
        {
            var snarl = at;
            Later(struck ? 0.5 : 0, () => Cue("cs-hounds.snarl", snarl, occ));
            was.Next = _time + 2.5 + 3.5 * _creatureRng.Next();
        }
        else if (e.Attached >= 0 && e.Phase == SpinePhase.Commit && _time >= was.Next)
        {
            // The pack fight: a snarl now and then, never on a beat.
            Cue("cs-hounds.snarl", at, occ);
            was.Next = _time + 2.5 + 3.5 * _creatureRng.Next();
        }
        // Bitten down to the last, pinned (App. A.1 GRAB).
        if (grabbed)
            Cue("cs-hounds.bite", Victim(train, e.Holding, 0.8) ?? at, occ);
    }

    /// <summary>
    /// Climbers (App. A.4): along the roofs toward the engine, each step heard by whoever's in the car under it (the cue is
    /// the roof from inside: its own car's space, unmuffled in there); forcing a way into the car; blows on one.
    /// </summary>
    void ClimberSounds(Enemy e, Creature was, Double3 at, float occ, bool struck)
    {
        if (e.Phase == SpinePhase.Commit && was.Phase == SpinePhase.Commit && e.Extra >= 0 && was.Extra >= 0 && e.Attached >= 0)
        {
            // Over a coupling onto the next roof is a step; otherwise one every 0.8 m (TraverseSpeed 2.2 m/s: under three a second).
            if (e.Attached != was.Attached)
                was.Stride = 0.8;
            else
                was.Stride += (e.Local - was.Local).Length;
            if (was.Stride >= 0.8)
            {
                was.Stride = 0;
                Cue("cs-climbers.step", "roof", at, Occlusion(e.Attached));
            }
        }
        // ENTER (Climber.Traverse): off the roof into the car under it, or down into the cab.
        if (was.Extra >= 0 && e.Extra < 0 && e.Attached >= 0 && was.Attached >= 0)
            Cue("cs-climbers.force", e.Attached == 0 ? at : was.At, Occlusion(e.Attached));
        if (struck)
            Cue("cs-climbers.hit", at, occ);
    }

    /// <summary>
    /// Draggers (App. A.4): the grab over the side, the victim's boots on the car side while they hang, and how it ended:
    /// hauled back up (or the limb clubbed off), or dragged under.
    /// </summary>
    void DraggerSounds(TrainOnLine train, Enemy e, Creature was, Double3 at, bool grabbed)
    {
        float outside = Occlusion(PlayerMotor.Outside);
        if (grabbed)
            Cue("cs-draggers.grab", at, outside);
        if (e.Phase == SpinePhase.Grab)
            Hold("cs-draggers.scrabble", e.Id, Victim(train, e.Holding, 0.2) ?? at - Double3.Up * 1.2, outside);
        if (was.Phase == SpinePhase.Grab && e.Phase != SpinePhase.Grab)
            DraggerLetGo(train, was);
    }

    void DraggerLetGo(TrainOnLine train, Creature was)
    {
        // The punish (Dragger.Punish: killed, then rearmed, all in one tick) shows as the grab's window run out, or the victim
        // dead of it (a crewmate's record may come a tick or two behind the enemy's: either says so).
        bool under = was.PhaseSeconds >= was.GrabWindow - WindowSlack || Crewmate(was.Holding) is { Death: DeathCause.Dragged };
        var where = Victim(train, was.Holding, 0.2) ?? was.At - Double3.Up * 1.2;
        Cue(under ? "cs-draggers.under" : "cs-draggers.haul-up", where, Occlusion(PlayerMotor.Outside));
    }

    /// <summary>A blow through the firebox door: the Stoker's shriek, and the burn on whoever swung (the nearest hurt in the cab).</summary>
    void StokerClubbed(TrainOnLine train, Double3 at, float occ)
    {
        Cue("cs-stoker.shriek", at, occ);
        var swinger = _hurts.Where(h => (h.At - at).Length <= 3.5).OrderBy(h => (h.At - at).Length).Select(h => (Double3?)(h.At + Double3.Up * 1.1)).FirstOrDefault();
        // Without the crew's records, at the door's mouth, where the tool went in.
        var mouth = train.Frames[0].ToWorld(train.Frames[0].ToLocal(at) + new Double3(0, 0.3, 0.6));
        Cue("cs-stoker.burn", swinger ?? mouth, occ);
    }

    /// <summary>
    /// A Soot Child (App. A.6): turning inhuman and lunging in the one tick (Telegraph, Commit and the grab together), the pin
    /// and the drinking while it holds them.
    /// </summary>
    void SootSounds(SootChildren e, Creature was, Double3 at, float occ, bool grabbed)
    {
        bool turned = e.Soot && was.Phase == SpinePhase.Telegraph && e.Phase is SpinePhase.Commit or SpinePhase.Grab;
        if (turned)
            Cue("cs-soot-children.turn", was.At, Occlusion(was.Space));
        if (grabbed)
        {
            var on = at;
            Later(turned ? 0.15 : 0, () => Cue("cs-soot-children.lunge", on, occ));
        }
        if (e.Phase == SpinePhase.Grab)
            Hold("cs-soot-children.drink", e.Id, at, occ);
    }

    /// <summary>
    /// The Passenger (App. A.8). Its footsteps are the crew's own boots on the car floor, so silence stays its only tell; the
    /// director kept a take for it, which plays where it's installed. Dragging someone to the caboose, the drag under it.
    /// </summary>
    void PassengerSounds(Enemy e, Creature was, Double3 at, float occ)
    {
        if (e.Attached >= 0 && was.Attached >= 0 && e.Phase is SpinePhase.Telegraph or SpinePhase.Grab)
        {
            // Through into the next car is a step; otherwise one every 0.75 m (walking pace, 1.3 m/s).
            was.Stride += e.Attached != was.Attached ? 0.75 : (e.Local - was.Local).Length;
            if (was.Stride >= 0.75)
            {
                was.Stride = 0;
                if (Cue("cs-passenger.step", at, occ) is null)
                    Cue("crew-footsteps.walk", "wood", at, occ);
            }
        }
        if (e.Phase == SpinePhase.Grab && Surfaced("cs-passenger.drag", "wood") is { } drag)
            Hold(drag, e.Id, at, occ);
    }

    /// <summary>
    /// The Grumbler (App. A.8): scuttling about the crane it gnaws on (now and then) and after whoever hit it (as it runs);
    /// going feral; eating the cargo of a car it was craned aboard in.
    /// </summary>
    void GrumblerSounds(Grumbler e, Creature was, Double3 at, float occ)
    {
        if (e.Phase == SpinePhase.Commit && e.Attached == was.Attached)
        {
            was.Stride += (e.Local - was.Local).Length;
            if (was.Stride >= 1.0)
            {
                was.Stride = 0;
                Cue("cs-grumbler.scuttle", at, occ);
            }
        }
        else if (e.Phase == SpinePhase.Telegraph && !e.Feral && e.Casting >= 0 && _time >= was.Next)
        {
            if (was.Next > 0)
                Cue("cs-grumbler.scuttle", at, occ);
            was.Next = _time + 1.5 + 2.5 * _creatureRng.Next();
        }
        if (e.Feral && was.Extra2 <= 0.5)
            Cue("cs-grumbler.feral", at, occ);
        if (e.Attached >= 0 && e.Phase == SpinePhase.Telegraph && !e.Feral)
            Hold("cs-grumbler.eat", e.Id, at, occ);
    }

    static readonly string[] MooseGrazing = ["tell-moose-grazing.browse", "tell-moose-grazing.creak", "tell-moose-grazing.grunt"];
    static readonly string[] MooseWarning = ["tell-moose-warning.grunt", "tell-moose-warning.clack", "tell-moose-warning.hoof-drag"];

    /// <summary>
    /// The Moose (note 339; queue #73, note 334), read off its record as G1 answered on #247: its <see cref="MooseMode"/> in
    /// Height, its aggro in Extra2 against <see cref="MooseTuning.ListenAt"/> and <see cref="MooseTuning.WarnAt"/>, its rams in
    /// Health. Grazing it chews (held) and browses, creaks and grunts now and then; listening, the chewing stops dead (the
    /// silence is the tell, so nothing plays); warning, a cough-like grunt, teeth clacks and a hoof raked back. Squaring up,
    /// two stamps and a snort; charging, its hooves and its wheeze held and brush breaking; snagged, the wood groaning and a
    /// bellow, then the rack grinding; searching, its breath held and the rack knocking now and then; a boom on each ram,
    /// landed on the shudder, the rack scraping after. Grazing as the train goes by (the art's trainPass rule), a call after
    /// it and the verge thrashed: the sim's +25 on the same condition, so sound, clip and temper agree.
    /// </summary>
    void MooseSounds(World world, Moose m, Creature was, Double3 at, float occ)
    {
        var t = world.Enemies?.Moose ?? new MooseTuning();
        var mode = m.Mode;
        bool began = mode != (MooseMode)(int)was.Height;
        if (began)
            was.Next = 0;
        string Pick(string[] of) => of[(int)(_creatureRng.Next() * of.Length) % of.Length];
        switch (mode)
        {
            case MooseMode.Graze when m.Aggro < t.ListenAt:
                Hold("tell-moose-grazing.chew", m.Id, at, occ);
                if (_time >= was.Next)
                {
                    if (was.Next > 0)
                        Cue(Pick(MooseGrazing), at, occ);
                    was.Next = _time + 3 + 5 * _creatureRng.Next();
                }
                break;
            case MooseMode.Graze when m.Aggro >= t.WarnAt:
                if (_time >= was.Next)
                {
                    Cue(Pick(MooseWarning), at, occ);
                    was.Next = _time + 1.2 + 1.6 * _creatureRng.Next();
                }
                break;
            case MooseMode.SquareUp when began:
                Cue("tell-moose-square-up.stamp", at, occ);
                CueLater(0.38, "tell-moose-square-up.stamp", at, occ);
                CueLater(1.1, "tell-moose-square-up.snort", at, occ);
                break;
            case MooseMode.Charge:
                Hold("tell-moose-charge.hooves", m.Id, at, occ);
                Hold("tell-moose-charge.wheeze", m.Id, at, occ);
                if (_time >= was.Next)
                {
                    if (was.Next > 0)
                        Cue("tell-moose-charge.brush", at, occ);
                    was.Next = _time + 0.8 + 1.4 * _creatureRng.Next();
                }
                break;
            case MooseMode.Snag:
                if (began)
                {
                    Cue("cs-moose-snag.groan", at, occ);
                    CueLater(0.5, "cs-moose-snag.bellow", at, occ);
                }
                Hold("cs-moose-snag.grind", m.Id, at, occ);
                break;
            case MooseMode.Search:
                Hold("cs-moose-search.breath", m.Id, at, occ);
                if (_time >= was.Next)
                {
                    if (was.Next > 0)
                        Cue("cs-moose-search.knock", at, occ);
                    was.Next = _time + 3 + 4 * _creatureRng.Next();
                }
                break;
        }
        // A ram, on the shudder (RamCount, replicated in Health).
        if (m.RamCount > Math.Max(0, (int)Math.Round(was.Health) - 1))
        {
            Cue("cs-moose-ram.boom", at, occ);
            CueLater(0.2, "cs-moose-ram.scrape", at, occ);
        }
        var train = world.Train;
        bool passing = mode == MooseMode.Graze && m.Aggro < t.WarnAt && Math.Abs(train.Dynamics.Speed) > 1 && train.Frames.Count > 0
            && train.Frames.Min(f => ((f.Origin - at) with { Y = 0 }).Length - f.Shape.HalfLength) <= t.TrainPassAt;
        if (passing && !was.Passing)
        {
            Cue("cs-moose-train-pass.bellow", at, occ);
            CueLater(0.6, "cs-moose-train-pass.thrash", at, occ);
        }
        was.Passing = passing;
    }

    /// <summary>
    /// The Gannet (note 340; queue #121, note 384; docs/design/creatures/gannet.md §4), read off its record: its
    /// <see cref="GannetMode"/> in Height, the spine's phase and time (its pecks on the sim's beat, <see cref="Gannet.PecksLanded"/>),
    /// its Health. Soaring or climbing over the train, its calls now and then and a great wingbeat between; hanging over a
    /// walker, nothing (the calls stop: the silence is the tell). The fold, a crack of wings and the air whistling up as it
    /// drops, following it down its line. A stab that landed (a crewmate hurt under the beak as it climbs out of the dive);
    /// a miss, the thunk into the planks, thrashing held while it's stuck, and the tear free as the art's tearFree starts.
    /// The bank, the scream and the wingbeats closing (held, coming with it). The pin, its weight landing, then each peck's
    /// wind-up and blow as <see cref="CreatureArt.GannetClip"/> draws them; driven off it with its victim alive, a screech.
    /// </summary>
    void GannetSounds(World world, Gannet g, Creature was, Double3 at, float occ, double dt)
    {
        var t = world.Enemies?.Gannet ?? new GannetTuning();
        var mode = g.Mode;
        var before = (GannetMode)(int)was.Height;
        bool began = mode != before;
        if (began)
            was.ModeAt = _time;
        if (mode != GannetMode.Fold)
            was.Moving = null;
        else if (was.Moving is { } whistle)
            whistle.Position = at;
        switch (mode)
        {
            case GannetMode.Soar or GannetMode.Climb:
                if (began && before == GannetMode.Fold && _hurts.Any(h => (h.At - at).Length <= GannetStabReach))
                    Cue("cs-gannet-strike.stab", at, occ);
                if (began && before == GannetMode.Pin && Crewmate(was.Holding) is { Health: > 0 })
                    Cue("cs-gannet-strike.driven", at, occ);
                // Overhead: a call, or a wingbeat, every few seconds; the first a while after it's come or climbed away.
                if (began && before is not (GannetMode.Soar or GannetMode.Climb))
                    was.Next = _time + 1.5 + 2 * _creatureRng.Next();
                if (_time >= was.Next)
                {
                    if (was.Next > 0)
                        Cue(_creatureRng.Next() < 0.7 ? "tell-gannet-calls.call" : "tell-gannet-calls.wings", at, occ);
                    was.Next = _time + 2.5 + 3.5 * _creatureRng.Next();
                }
                break;
            case GannetMode.Fold when began:
                Cue("tell-gannet-fold.crack", at, occ);
                was.Moving = Cue("tell-gannet-fold.whistle", at, occ);
                break;
            case GannetMode.Stuck:
                if (began)
                    Cue("cs-gannet-strike.thunk", at, occ);
                double tear = was.ModeAt + Math.Max(0, t.StuckSeconds - CreatureArt.GannetTearFreeSeconds);
                if (_time < tear)
                    Hold("cs-gannet-strike.thrash", g.Id, at, occ);
                else if (_time - dt < tear)
                    Cue("cs-gannet-strike.tear", at, occ);
                break;
            case GannetMode.Bank:
                if (began)
                    Cue("tell-gannet-bank.scream", at, occ);
                Hold("tell-gannet-bank.wingbeats", g.Id, at, occ);
                break;
            case GannetMode.Pin:
                if (began)
                    Cue("cs-gannet-strike.land", at, occ);
                // Each peck as the art draws it (GannetClip): its wind-up, then the blow on the peckEvery beat.
                if (g.Phase == SpinePhase.Grab && was.Phase == SpinePhase.Grab)
                    for (int k = 1; k <= t.Pecks; k++)
                    {
                        double strike = k * t.PeckEvery - CreatureArt.GannetPeckStrike;
                        if (Crossed(was.PhaseSeconds, g.PhaseSeconds, strike - CreatureArt.GannetWindupSeconds))
                            Cue("cs-gannet-strike.windup", at, occ);
                        if (Crossed(was.PhaseSeconds, g.PhaseSeconds, strike))
                            Cue("cs-gannet-strike.peck", Victim(world.Train, g.Holding, 0.3) ?? at, occ);
                    }
                break;
        }
    }

    /// <summary>How near a crewmate hurt as the Gannet climbs out of its dive was to it: the stab's (its strikeRadius, and the
    /// beak's reach down to them).</summary>
    const double GannetStabReach = 3;

    static bool Crossed(double from, double to, double mark) => from < mark && to >= mark;

    // ---- Records gone -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// A record removed: the enemy is gone (or out of the interest radius, past hearing). Which way it went is read off its
    /// last record: a kill (its Health within one blow, someone in reach or a round just fired), a punish (its grab's window
    /// run out), or what it took with it.
    /// </summary>
    void Vanished(World world, Creature c)
    {
        var train = world.Train;
        float occ = Occlusion(c.Space);
        bool killed = Killed(world, c);
        // Killed, it goes over and crumbles to ash from halfway (note 208's GreyboxScene.Deaths, Effects.DeathSeconds): the
        // crumble's heard as it's seen, the kill confirmed whatever it was (note 244).
        if (killed && GreyboxScene.Falls(c.Kind))
            CueLater(Effects.DeathSeconds * 0.5, "creature-crumble", c.At, occ);
        switch (c.Kind)
        {
            case EnemyKind.TrackDoll when c.Phase == SpinePhase.Punish:
                // APPEASED: a toy held out, and it's gone with it (TrackDoll.HauntTick removes the toy the same tick).
                if (_bodiesGone.Contains(BodyKind.Toy))
                    Cue("cs-track-doll.take-toy", c.At, occ);
                else if (c.Extra > 0.5 && killed)
                    Cue("cs-track-doll.crack", c.At, occ);
                break;
            case EnemyKind.CarHugger:
                BreakAway(train, c.Attached, c, c.At);
                if (killed)
                    Cue("cs-car-hugger.hit", c.At, occ);
                break;
            case EnemyKind.Whistler when c.Phase is SpinePhase.Commit or SpinePhase.Grab:
                // Clubbed while carrying, it drops them and runs (Whistler.Rescued): the blow's in the same tick as it goes.
                bool brokenOff = c.Phase == SpinePhase.Grab && c.PhaseSeconds < c.GrabWindow - WindowSlack;
                if (brokenOff || killed)
                    Cue("cs-whistler.hit", c.At, occ);
                if (killed)
                    Cue("cs-whistler.death", c.At, occ);
                break;
            case EnemyKind.Ribbit when killed:
                Cue("cs-ribbits.hit", c.At, occ);
                break;
            case EnemyKind.Choir when killed:
                Cue("cs-choir.hit", c.At, occ);
                break;
            case EnemyKind.Climber when killed:
                Cue("cs-climbers.hit", c.At, occ);
                break;
            case EnemyKind.CinderHound when killed || c.Attached < 0 && _time - _lastShot <= 1.5:
                // Killed, or a running hound driven off by a round from the rear (App. A.3 break off).
                Cue("cs-hounds.yelp", c.At, occ);
                break;
            case EnemyKind.Stoker when killed:
                StokerClubbed(train, c.At, occ);
                break;
            case EnemyKind.Gaunt when killed && c.Phase != SpinePhase.Dormant && !_bodiesGone.Any(k => Bodies.Value(k) > 0):
                // Not when something valuable went with it: led into a car, it takes the best thing there and leaves.
                Cue("cs-gaunt.death", c.At, occ);
                break;
            case EnemyKind.Follower when c.Phase is SpinePhase.Dormant or SpinePhase.Telegraph && c.Extra >= 0:
                // Riding someone's back, and a friend clubbed it off (it's never on its carrier's own machine to hear).
                if (FriendNear(world, c, (int)Math.Round(c.Extra)))
                    Cue("cs-followers.clubbed-off", c.At, occ);
                break;
            case EnemyKind.Follower when c.Phase == SpinePhase.Punish && killed && c.Attached >= 0 && !Cut(train, c.Attached):
                Cue("cs-followers.nest-burst", c.At, occ);
                break;
            case EnemyKind.SootChildren when c.Extra2 > 0.5 && killed
                && !(c.Phase == SpinePhase.Grab && c.PhaseSeconds >= c.GrabWindow - WindowSlack)
                && (Crewmate(c.Holding) is not { } held || held.Alive):
                Cue("cs-soot-children.death", c.At, occ);
                break;
            case EnemyKind.Passenger when c.Phase == SpinePhase.Grab
                && (c.Attached >= 0 && Cut(train, c.Attached) || Crewmate(c.Holding) is { Death: DeathCause.Uncoupled }):
                // At the caboose, it uncoupled it (Passenger.Tick): the crew's coupling hooks hear the knuckle part; this is its own.
                Cue("cs-passenger.uncouple", (c.Attached >= 0 ? GapAhead(train, c.Attached) : null) ?? c.At, Occlusion(PlayerMotor.Outside));
                break;
            case EnemyKind.Switchman when killed && c.Phase is SpinePhase.Telegraph or SpinePhase.Commit:
                Cue("cs-switchman.death", c.At, occ);
                break;
            case EnemyKind.Dragger when c.Phase == SpinePhase.Grab:
                // Its limb clubbed off the one it held (one blow kills it): they're free, hauled back up.
                DraggerLetGo(train, c);
                break;
            case EnemyKind.Gannet when killed:
                GannetDown(train, c, occ);
                break;
        }
    }

    /// <summary>
    /// A Gannet killed: its crash across the roof (gannet.py's death). Shot out of the air over a car it falls to that roof
    /// first, as GreyboxScene draws it (dead weight from where it was), so the crash is heard when it lands there, where the
    /// car will be by then.
    /// </summary>
    void GannetDown(TrainOnLine train, Creature c, float occ)
    {
        if (c.Attached < 0 || c.Attached >= train.Frames.Count)
        {
            Cue("cs-gannet-strike.death", c.At, occ);
            return;
        }
        var f = train.Frames[c.Attached];
        double roof = f.Shape.RoofHeight, drop = Math.Max(0, c.Local.Y - roof);
        double fall = Math.Sqrt(2 * drop / 9.81);
        var lands = f.ToWorld(c.Local with { Y = roof }) - f.Back * (train.Dynamics.Speed * fall);
        CueLater(fall, "cs-gannet-strike.death", lands, occ);
    }

    /// <summary>
    /// Killed: the host's word, where it's given (T121's HitConfirm.Killed: a blow, a round or a blast that killed it, on the
    /// wire with the record it ends; note 244). With no hit on it at all to go by: the last record's Health was within one
    /// blow (a tool's, or a round's just after a gun fired), and something could have dealt it: a gun just fired, or a
    /// crewmate in reach (or no crew records to say otherwise).
    /// </summary>
    bool Killed(World world, Creature c)
    {
        bool hit = false;
        foreach (var h in world.Hits)
            if (h.EnemyId == c.Id)
            {
                if (h.Killed)
                    return true;
                hit = true;
            }
        if (hit)
            return false;
        var melee = world.Enemies?.Melee ?? new MeleeTuning();
        bool shot = _time - _lastShot <= ShotKills;
        double blow = Math.Max(melee.Hardest, shot ? world.Combat?.Guns.DamagePerRound ?? 4 : 0);
        if (c.Health <= 0 || c.Health > blow + 1e-3)
            return false;
        if (shot || CrewStates.Count == 0)
            return true;
        return CrewStates.Any(p => p.State.Alive && At(world.Train, p.State) is { } w && (w - c.At).Length <= melee.Reach + 2);
    }

    /// <summary>Someone other than its carrier, alive, near enough to have swung at it (true without crew records).</summary>
    bool FriendNear(World world, Creature c, int carrier)
    {
        if (CrewStates.Count == 0)
            return true;
        double reach = (world.Enemies?.Melee.Reach ?? 2.2) + 1.5;
        return CrewStates.Any(p => p.Id != carrier && p.State.Alive && At(world.Train, p.State) is { } w && (w - c.At).Length <= reach);
    }

    // ---- What isn't one enemy's ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Bites and blows that land without a phase changing: a crewmate's Health falls with a boarded hound in its pack fight, or a
    /// Gaunt attacking, in reach of them (CinderHound.Maul, Gaunt.Attack).
    /// </summary>
    void Mauled(World world)
    {
        var train = world.Train;
        foreach (var h in _hurts)
        {
            if (h.Drop <= 0)
                continue;
            var near = world.ActiveEnemies.Where(e => !e.Gone && e.Phase == SpinePhase.Commit
                    && (e is CinderHound && e.Attached >= 0 && (e.WorldPosition(train) - h.At).Length <= 5.5
                        || e is Gaunt && (e.WorldPosition(train) - h.At).Length <= 3.2))
                .OrderBy(e => (e.WorldPosition(train) - h.At).Length).FirstOrDefault();
            if (near is null)
                continue;
            var space = Occlusion(SpaceOf(near, train));
            Cue(near is Gaunt ? "cs-gaunt.blow" : "cs-hounds.bite", h.At + Double3.Up * 0.9, space);
        }
    }

    /// <summary>
    /// The Choir's swarm (App. A.7): arriving and here (a loop over the train while it's present), banging and rattling at the
    /// shut doors with somebody behind them while its ghosts besiege, and leaving once quiet holds.
    /// </summary>
    void ChoirSwarm(World world)
    {
        var train = world.Train;
        bool present = world.Combat is not null && world.Choir.Present;
        var d = train.Dynamics;
        var over = train.Line.Sample(d.Path, d.Distance - d.Consist.LengthMetres * 0.5).Position + Double3.Up * 6;
        float outside = Occlusion(PlayerMotor.Outside);
        if (present)
            Hold("cs-choir.arrive", 0, over, outside);
        if (Fell("cs-choir.present", 0, present))
            Cue("cs-choir.disperse", over, outside);

        // BESIEGE (ChoirGhost: nobody exposed to seize): at the doors of every space somebody's shut in, the listener's own too.
        if (!world.ActiveEnemies.Any(e => e is ChoirGhost { Gone: false, Phase: SpinePhase.Commit, Extra: < 0 }))
        {
            _bangs.Clear();
            return;
        }
        var shut = new SortedSet<int>();
        if (_space >= 0)
            shut.Add(_space);
        foreach (var (_, s) in CrewStates)
            if (s.Alive && PlayerMotor.Space(s, train) is >= 0 and var space)
                shut.Add(space);
        foreach (int space in shut)
        {
            if (space >= train.Frames.Count)
                continue;
            if (_bangs.TryGetValue(space, out double next) && _time < next)
                continue;
            _bangs[space] = _time + 0.6 + 1.6 * _creatureRng.Next();
            var frame = train.Frames[space];
            if (space == 0 && frame.Shape.Cab is { } cab)
            {
                // The cab has no door: they beat on its ironwork, either side.
                double side = _creatureRng.Next() < 0.5 ? cab.Max.X : cab.Min.X;
                Cue("cs-choir.bang-door", "grate", frame.ToWorld(new Double3(side, cab.Centre.Y, cab.Centre.Z)), Occlusion(0));
            }
            else if (frame.Shape.DoorList is { Count: > 0 } doors)
            {
                var door = doors[(int)(_creatureRng.Next() * doors.Count) % doors.Count];
                Cue("cs-choir.bang-door", "wood", frame.ToWorld(door.Box.Centre), Occlusion(space));
            }
        }
    }

    /// <summary>
    /// The Switchman throwing the points (App. A.8): a branch it stands at going over to diverging. The crew's switch hooks
    /// play the lever and the blades for any throw; this is its own cue, at the lever.
    /// </summary>
    void SwitchmanThrows(World world)
    {
        var train = world.Train;
        for (int b = 0; b < train.Line.Branches.Count; b++)
        {
            bool now = train.Diverging(b);
            bool thrown = _points.TryGetValue(b, out bool was) && now && !was;
            _points[b] = now;
            if (thrown && world.ActiveEnemies.Any(e => e is Switchman { Gone: false } s && s.Branch == b))
                Cue("cs-switchman.throw", Lever(world, b), Occlusion(PlayerMotor.Outside));
        }
    }

    /// <summary>
    /// The pack's howl (GameAudio.Enemies, the tell): close behind (cues.py's 40-100 m) it's tell-hounds.howl-near once that's
    /// installed; anywhere else, and until then, the howl the tell has always had. Nearer than that they're at the leap, and the
    /// pack fight has its own sounds.
    /// </summary>
    string HowlFor(TrainOnLine train, Enemy hound)
    {
        double gap = train.Dynamics.RearDistance - hound.LineDistance;
        return hound.Attached < 0 && gap >= NearHowlFrom && gap <= NearHowlTo && HasCue("tell-hounds.howl-near") ? "tell-hounds.howl-near" : "hound-howl";
    }

    // ---- Bookkeeping -----------------------------------------------------------------------------------------------------------

    /// <summary>Everyone's gun: a shot is the tick its replicated LastShotTick changes (World.Shots holds only your own).</summary>
    void ShotsHeard(TrainOnLine train)
    {
        foreach (var v in train.Vehicles)
        {
            if (_shotTicks.TryGetValue(v.Id, out uint was) && was != v.Gun.LastShotTick)
                _lastShot = _time;
            _shotTicks[v.Id] = v.Gun.LastShotTick;
        }
    }

    /// <summary>Who in the crew lost Health (or their life) since last tick, and where they are.</summary>
    void CrewHurt(TrainOnLine train)
    {
        _hurts.Clear();
        foreach (var (id, s) in CrewStates)
        {
            if (_crewHealth.TryGetValue(id, out var was) && At(train, s) is { } at && (s.Health < was.Health || s.Death != was.Death))
                _hurts.Add(new Hurt(id, at, was.Health - s.Health, s.Death));
            _crewHealth[id] = (s.Health, s.Death);
        }
    }

    /// <summary>The kinds of body whose records went this tick (a toy the Track Doll took, the loot a Gaunt left with).</summary>
    void BodiesGone(World world)
    {
        _bodiesGone.Clear();
        _bodyIds.Clear();
        foreach (var b in world.Bodies.All)
            _bodyIds.Add(b.Id);
        foreach (var (id, kind) in _bodies)
            if (!_bodyIds.Contains(id))
                _bodiesGone.Add(kind);
        _bodies.Clear();
        foreach (var b in world.Bodies.All)
            _bodies[b.Id] = b.Kind;
    }

    /// <summary>The three running hounds nearest the ears, within the paws' hearing: a pack's every paw would eat the voices.</summary>
    void Gallopers(World world)
    {
        _gallopers.Clear();
        double range = Bank.Get("cs-hounds.paw.ground")?.MaxDistance ?? 80;
        var ear = Mixer.Listener.Position;
        foreach (var e in world.ActiveEnemies.Where(e => e is CinderHound { Gone: false, Attached: < 0 })
                     .Select(e => (e.Id, Distance: (e.WorldPosition(world.Train) - ear).Length)).Where(x => x.Distance <= range)
                     .OrderBy(x => x.Distance).ThenBy(x => x.Id).Take(3))
            _gallopers.Add(e.Id);
    }

    void Later(double seconds, Action play)
    {
        if (seconds <= 0)
            play();
        else
            _later.Add((_time + seconds, play));
    }

    void Later()
    {
        for (int i = 0; i < _later.Count; i++)
            if (_later[i].At <= _time)
            {
                var play = _later[i].Play;
                _later.RemoveAt(i--);
                play();
            }
    }

    /// <summary>A crewmate's record, if the crew's known.</summary>
    PlayerState? Crewmate(int id)
    {
        if (id < 0)
            return null;
        foreach (var (i, s) in CrewStates)
            if (i == id)
                return s;
        return null;
    }

    /// <summary>Where a held crewmate is (their feet, raised by <paramref name="up"/>), if the crew's known.</summary>
    Double3? Victim(TrainOnLine train, int id, double up) =>
        Crewmate(id) is { } s && At(train, s) is { } at ? at + Double3.Up * up : null;

    static Double3? At(TrainOnLine train, in PlayerState s) =>
        s.Parent == PlayerState.World || s.Parent < train.Frames.Count ? PlayerMotor.WorldPosition(s, train) : null;

    /// <summary>
    /// The space a creature is in for occlusion: inside a car's room (or the cab) it's that car's; on a roof, at a gap or
    /// out on the line it's outside.
    /// </summary>
    static int SpaceOf(Enemy e, TrainOnLine train)
    {
        if (e.Attached < 0 || e.Attached >= train.Frames.Count)
            return PlayerMotor.Outside;
        var shape = train.Frames[e.Attached].Shape;
        var p = e.Local + Double3.Up * 0.1;
        if (shape.Interior is { } room && room.Contains(p) || e.Attached == 0 && shape.Cab is { } cab && cab.Contains(p))
            return e.Attached;
        return PlayerMotor.Outside;
    }

    /// <summary>What a creature stands on: a roof or a car floor aboard, the ground off the train.</summary>
    static string SurfaceOf(Enemy e, TrainOnLine train)
    {
        if (e.Attached < 0 || e.Attached >= train.Frames.Count)
            return "ground";
        return e.Local.Y >= train.Frames[e.Attached].Shape.RoofHeight - 0.3 ? "roof" : "wood";
    }

    /// <summary>A vehicle no longer in the engine's rake: cut loose.</summary>
    static bool Cut(TrainOnLine train, int car) => car >= train.Frames.Count || train.Dynamics.Consist.IndexOf(car) < 0;

    /// <summary>The coupling at a vehicle's front end, where it parts from the one ahead.</summary>
    static Double3? GapAhead(TrainOnLine train, int car)
    {
        if (car < 0 || car >= train.Frames.Count)
            return null;
        var f = train.Frames[car];
        return f.ToWorld(new Double3(0, 1.0, -f.Shape.HalfLength - train.Dynamics.Tuning.Geometry.CouplingGap * 0.5));
    }

    /// <summary>A branch's switch-stand lever (SwitchStands.LeverAt).</summary>
    static Double3 Lever(World world, int branch)
    {
        var line = world.Train.Line;
        if (world.Switches is { } stands)
            return stands.LeverAt(line, branch);
        var t = line.Sample(line.Branches[branch].Toe);
        return t.Position + Double3.Cross(t.Tangent, Double3.Up).Normalized * line.Branches[branch].Side * 1.5 + Double3.Up * 0.9;
    }

    /// <summary>The junction lamp on top of a switch stand.</summary>
    static Double3 Lamp(World world, int branch, Double3 fallback) =>
        branch >= 0 && branch < world.Train.Line.Branches.Count ? Lever(world, branch) + Double3.Up * 0.6 : fallback;

    bool _answerHeard;

    /// <summary>The sound hook for the dark's answer to a draw (note 287), for the audio chat: tell-draw.whistle, .cannon, ….</summary>
    public static string AnswerCue(DrawCause cause) => $"tell-draw.{DrawLedger.Key(cause)}";

    /// <summary>
    /// The dark answering what the crew did (note 287; World.Answer, replicated): once, as it starts, a call from out past the
    /// lamp where it's heard. Its own cue (<see cref="AnswerCue"/>, the audio chat's to make) once installed; until then the
    /// pack's howl, distant: something out there heard you. The eyes at the lamp's edge are Art/Effects.Eyes.
    /// </summary>
    void Answer(World world)
    {
        var answer = world.Answer;
        if (!answer.Showing)
        {
            _answerHeard = false;
            return;
        }
        if (_answerHeard)
            return;
        _answerHeard = true;
        string cue = HasCue(AnswerCue(answer.Cause)) ? AnswerCue(answer.Cause) : "hound-howl";
        Mixer.Play(cue, answer.At)?.Also(v => v.Occlusion = Occlusion(PlayerMotor.Outside));
    }

    bool _signHeard;

    /// <summary>The sound hook for a sign shown a crewmate afoot (note 327), for the audio chat: sign.ribbits, sign.gaunt, ….</summary>
    public static string SignCue(EnemyKind kind) => $"sign.{Director.Key(kind)}";

    /// <summary>
    /// Until the audio chat makes its own (<see cref="SignCue"/>): each creature's own movement, heard from where it is, never
    /// its tell (a tell means it's coming; a sign means only that it's there). The Gaunt and the Followers make no sound of
    /// their own until they're on you: their sign is the eyes alone.
    /// </summary>
    static string? SignFallback(EnemyKind kind) => kind switch
    {
        EnemyKind.Ribbit => "cs-ribbits.hop-land.ground",
        EnemyKind.Grumbler => "cs-grumbler.scuttle",
        EnemyKind.SootChildren => "cs-soot-children.turn",
        EnemyKind.Whistler => "cs-whistler.run",
        _ => null,
    };

    /// <summary>
    /// A sign shown a crewmate afoot off the train (note 327; World.Watcher, replicated): once, as it starts, the sound of
    /// what lives there, from where its eyes are, a little under full (it's something moving out there, not something on
    /// you). The eyes are Art/Effects.Eyes.
    /// </summary>
    void Watched(World world)
    {
        var sign = world.Watcher;
        if (!sign.Showing)
        {
            _signHeard = false;
            return;
        }
        if (_signHeard)
            return;
        _signHeard = true;
        string? cue = HasCue(SignCue(sign.Kind)) ? SignCue(sign.Kind) : SignFallback(sign.Kind);
        if (cue is not null)
            Cue(cue, sign.At, Occlusion(PlayerMotor.Outside), SignVolume);
    }

    const float SignVolume = 0.7f;
}
