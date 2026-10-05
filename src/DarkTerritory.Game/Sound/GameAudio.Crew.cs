using Ballast;
using Ballast.Audio;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;
using Kit = DarkTerritory.Sim.Player.Kit;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The crew's lines on the audio checklist (tools/audio/cues.py crew-*): feet, ladders, doors, lamps, the firebox and the
/// shovel, the cab's controls, switches, couplings, the boiler's mending, the cannons, carrying, the extinguisher, tools,
/// getting hurt, jumping off and the cold. Everything is read off replicated state (out/audio/hooks-map.md): an edge
/// against last tick is the event, kept per player, vehicle, gun and body by id, so a client hears what the host does.
/// The listener's own swing (and trigger) are the exception: heard from this machine's own intent (<see cref="OwnIntent"/>),
/// the tick it's pressed. A crewmate's swing is the host's <c>World.Swings</c> (note 197), landed or not.
/// </summary>
public sealed partial class GameAudio
{
    // CrewStates (GameAudio.Cues.cs): everyone aboard, the listener's own player among them, set before each Update.
    /// <summary>This machine's own player id, or −1 (the bench, a dedicated host).</summary>
    public int OwnId { get; set; } = -1;
    /// <summary>This machine's own player's intent this tick: your swing and your trigger, which nothing replicates.</summary>
    public PlayerIntent OwnIntent { get; set; }
    /// <summary>Walk and run speeds, landings, the cold (player.json); read from content when not set.</summary>
    public PlayerTuning? PlayerTuning
    {
        get => _crewTuning ??= LoadPlayerTuning();
        set => _crewTuning = value;
    }

    PlayerTuning? _crewTuning;
    bool _crewTuningTried;

    PlayerTuning? LoadPlayerTuning()
    {
        if (_crewTuningTried || Bank.Directory is not { } sounds)
            return null;
        _crewTuningTried = true;
        // The bank is content/audio/sounds: content is two up.
        var root = Path.GetDirectoryName(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(sounds))));
        try
        {
            return root is null ? null : DataFile.Load<PlayerTuning>(Path.Combine(root, Sim.Player.PlayerTuning.File));
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Presentation, not design: how long a footstep is at a speed (a brisk walk's 0.9 m, a run's 1.6 m), and below what speed
    // someone's standing.
    const double StrideBase = 0.45, StridePerSpeed = 0.2, StrideMin = 0.6, StrideMax = 1.7, Standing = 0.4;
    /// <summary>Turning on the spot this far (rad) shuffles the feet; not more often than <see cref="ScuffGap"/>.</summary>
    const double ScuffTurn = 1.2, ScuffGap = 0.6;
    /// <summary>A handbrake wheel clicks this often while it's wound.</summary>
    const double RatchetEvery = 0.2;
    /// <summary>A sliding door rolls this long before it hits its stop; it latches this long after it's slammed.</summary>
    const double SlideRollSeconds = 0.9, SlideLatchAfter = 0.3;
    /// <summary>A coupling's pin drops this long after the knuckles close; a switch lever latches this long after it's thrown.</summary>
    const double PinDropAfter = 0.35, LeverLatchAfter = 0.4;
    /// <summary>A cannon's shot is the far one past this many metres from the ears.</summary>
    const double ShotFar = 60;
    /// <summary>Where on a body things happen: hands, hip, chest, head (m over the feet).</summary>
    const double HandsUp = 1.2, HipUp = 0.95, ChestUp = 1.3, HeadUp = 1.6;
    /// <summary>A body that's moved less than this a tick (m) for a few ticks has come to rest.</summary>
    const double AtRest = 0.004;
    // How fast a crate has to slide along a floor to be heard scraping (m/s): past a nudge, short of a shove.
    const double CrateSlides = 0.15;
    // A tool let go as its holder drops lands a beat after the body (s).
    const double ToolFallsAfter = 0.18;
    // The funny ones' timing (crew-mishaps), in seconds after what sets them off: the body tumbling back off the roof after
    // the tunnel's bonk; the crane's chain after the casting; a thrown body coming down; what was in hand skittering off
    // after a hard landing; the extinguisher's dregs after it runs dry; one boot after a body lands.
    const double TumbleAfterBonk = 0.25, ChainAfterCrush = 0.9, ThrownLandsAfter = 0.8, ScatterAfterLanding = 0.3,
        DregsAfterDry = 0.45, BootAfterBody = 0.22;

    sealed class CrewMember
    {
        public int Parent;
        public Double3 Position;
        public Surface Surface;
        public double Yaw, Action, Cold, Stride, Turn, LastScuff = double.NegativeInfinity, MovingFor, Speed, NextRatchet, NextSwing, TumbleUntil, NextBreath;
        public int Health;
        public byte Placed;
        public bool Alive, Moving, Shovelful, LeftTrain, BreathIn = true;
        public PlayerFlags Flags;
        public Tool Held;
        public Double3 AirVelocity;
        public PlayerState? Footing;
        public int Rung;

        public string LastFooting(World world) => Footing is { } f ? Sound.Footing.Under(f, world) ?? "ground" : "ground";
    }

    sealed class CrewCar
    {
        public byte Doors;
        public bool Lamp;
        public readonly double[] RollUntil = new double[8];
    }

    sealed class CrewGun
    {
        public bool Mounted, BallIn, Traversing, Laying;
        public int Ammo, Needed;
        public double Z, Progress, LastMove, Traverse, Elevation, LastLaid;
    }

    sealed class CrewBody
    {
        public int Carrier, Parent;
        public bool Lifted, Falling, Thrown, OnMount, Spraying;
        public double Charge, LastDrain = double.NegativeInfinity, ReleasedAt, Hint;
        public Double3 Local;
        public int Still;
        public int? CarrierSpace;
    }

    sealed class CrewEngine
    {
        public bool FireDoor, Vented, Lamp, WrenchOut, Ruptured, Sanding, Released, MendedSinceWrench;
        public double Tender, SinceShovel, Throttle, Brake, Sand, SandTrend, Whistle, WhistleLow, LampOut;
        public int Reverser, WhistleFalling, SandIdle;
    }

    partial void EndNightCrew()
    {
        _crewMembers.Clear();
        _heardSwings.Clear();
        _swingsPrimed = false;
        _crewCars.Clear();
        _crewGuns.Clear();
        _crewBodies.Clear();
        _crewRakes.Clear();
        _crewSwitches = null;
        _crewCouplings = null;
        _crewEngine = null;
        _crewLater.Clear();
    }

    readonly Dictionary<int, CrewMember> _crewMembers = new();
    readonly Dictionary<int, CrewCar> _crewCars = new();
    readonly Dictionary<int, CrewGun> _crewGuns = new();
    readonly Dictionary<int, CrewBody> _crewBodies = new();
    readonly Dictionary<int, (int Count, bool On)> _crewRakes = new();
    HashSet<(int, int)>? _crewCouplings;
    bool[]? _crewSwitches;
    CrewEngine? _crewEngine;
    bool _crewFireWas;
    readonly List<(double At, string Name, Double3 Where, float Occlusion)> _crewLater = new();

    partial void CrewSounds(World world)
    {
        var train = world.Train;
        if (train.Frames.Count == 0)
            return;
        CrewLater();
        bool firePressed = OwnIntent.Has(PlayerButtons.Fire) && !_crewFireWas;
        _crewFireWas = OwnIntent.Has(PlayerButtons.Fire);
        CrewGuns(world);
        CrewPeople(world);
        CrewVehicles(world);
        CrewEngineRoom(world);
        CrewCouplings(world);
        CrewSwitchStands(world);
        CrewCarried(world, firePressed);
    }

    /// <summary>Plays what was put off till now (a latch after a slam, a pin after the knuckles).</summary>
    void CrewLater()
    {
        for (int i = _crewLater.Count - 1; i >= 0; i--)
            if (_crewLater[i].At <= _time)
            {
                var (_, name, where, occlusion) = _crewLater[i];
                _crewLater.RemoveAt(i);
                Cue(name, where, occlusion);
            }
    }

    void CrewAfter(double seconds, string name, Double3 where, float occlusion) => _crewLater.Add((_time + seconds, name, where, occlusion));

    /// <summary>The enclosed space a point on a vehicle is in (as <see cref="PlayerMotor.Space"/> has a player's): for occlusion.</summary>
    static int SpaceOf(TrainOnLine train, int vehicle, Double3 local)
    {
        if (vehicle < 0 || vehicle >= train.Frames.Count)
            return PlayerMotor.Outside;
        var shape = train.Frames[vehicle].Shape;
        if (vehicle == 0 && shape.Cab is { } cab && cab.Contains(local))
            return 0;
        return shape.Interior is { } room && room.Contains(local) && train.Vehicles[vehicle].DoorsOpen == 0 ? vehicle : PlayerMotor.Outside;
    }

    float OccludedAt(TrainOnLine train, int vehicle, Double3 local) => Occlusion(SpaceOf(train, vehicle, local));

    static string ToolName(Tool t) => t switch { Tool.Shovel => "shovel", Tool.Wrench => "wrench", _ => "crowbar" };

    static double Stride(double speed) => Math.Clamp(StrideBase + StridePerSpeed * speed, StrideMin, StrideMax);

    /// <summary>Running rather than walking: faster than halfway between the surface's walk and run (player.json).</summary>
    bool Running(Surface surface, double speed)
    {
        var p = PlayerTuning;
        double walk = surface == Surface.Roof ? p?.RoofWalkSafe ?? 1.8 : p?.Walk ?? 2.4;
        double run = surface == Surface.Roof ? p?.RoofRun ?? 3.5 : p?.Run ?? 5.5;
        return speed > (walk + run) / 2;
    }

    // ------------------------------------------------------------------------------------------------ the crew themselves

    readonly HashSet<int> _heardSwings = [], _swungNow = [];
    bool _swingsPrimed;

    void CrewPeople(World world)
    {
        var train = world.Train;
        var tuning = PlayerTuning;
        var seen = new HashSet<int>();
        // Who started a swing since the last update (note 197's SwingEvent: one per recovery, landed or not), each once. On
        // the first update what's there is old news. Your own is heard from your intent instead, a round trip sooner.
        _swungNow.Clear();
        foreach (var w in world.Swings)
            if (_heardSwings.Add(w.Id) && _swingsPrimed && w.By != OwnId)
                _swungNow.Add(w.By);
        _swingsPrimed = true;
        if (_heardSwings.Count > 64)
            _heardSwings.IntersectWith(world.Swings.Select(w => w.Id));
        var weather = (world.Route ?? world.Run?.Route)?.Weather;
        foreach (var (id, s) in CrewStates)
        {
            seen.Add(id);
            bool onTrain = s.Parent != PlayerState.World;
            if (onTrain && s.Parent >= train.Frames.Count)
                continue;
            if (!_crewMembers.TryGetValue(id, out var c))
            {
                _crewMembers[id] = c = new CrewMember();
                Remember(c, s, world);
                continue;
            }
            var feet = PlayerMotor.WorldPosition(s, train);
            float occlusion = Occlusion(PlayerMotor.Space(s, train));
            bool placed = s.Placed != c.Placed;

            // Dead: the body going down, on whatever it's on, and a tool in hand clattering down after it. Where the crew did
            // it to themselves, the funny one (crew-mishaps, the director's call 3 Oct): a tunnel's lip at head height and the
            // body tumbling back off the roof; a casting let go on them and the crane's chain swinging loose after.
            if (c.Alive && !s.Alive && s.Death != DeathCause.Waiting)
            {
                string fell = Footing.Under(s, world) ?? c.LastFooting(world);
                if (s.Death == DeathCause.Struck && HasCue("crew-mishaps.tunnel-bonk"))
                {
                    Cue("crew-mishaps.tunnel-bonk", feet + Double3.Up * HeadUp, occlusion);
                    CrewAfter(TumbleAfterBonk, "crew-mishaps.tunnel-tumble", feet, occlusion);
                }
                else if (s.Death == DeathCause.Crushed && HasCue("crew-mishaps.crushed"))
                {
                    Cue("crew-mishaps.crushed", feet, occlusion);
                    CrewAfter(ChainAfterCrush, "crew-mishaps.crane-chain", feet + Double3.Up * 5, occlusion);
                }
                else if (s.Death == DeathCause.Thrown && placed && HasCue("crew-mishaps.thrown-flail"))
                {
                    // Over the side at speed: the flailing, and the body coming down a moment later.
                    Cue("crew-mishaps.thrown-flail", feet + Double3.Up * ChestUp, occlusion);
                    CrewAfter(ThrownLandsAfter, Surfaced("crew-hurt.body-fall", "ground") ?? "crew-hurt.body-fall", feet, occlusion);
                }
                else
                    Cue("crew-hurt.body-fall", fell, feet, occlusion);
                if (c.Held != Tool.None && Surfaced($"crew-melee.{ToolName(c.Held)}-drop", fell) is { } dropped)
                    CrewAfter(ToolFallsAfter, dropped, feet + Double3.Up * 0.2, occlusion);
            }
            // Thrown off a roof on a curve taken over its board (Lineside's throw: PullOff), and alive: the flailing. A Dragger's
            // pull (the same PullOff) comes out of a grab, so it isn't this.
            if (s.Alive && placed && c.Surface == Surface.Roof && s.Surface == Surface.Air && s.Parent == PlayerState.World
                && c.Parent != PlayerState.World && !c.Flags.HasFlag(PlayerFlags.Held))
                Cue("crew-mishaps.thrown-flail", feet + Double3.Up * ChestUp, occlusion);
            if (!s.Alive || placed)
            {
                Remember(c, s, world);
                continue;
            }

            bool sameFrame = s.Parent == c.Parent;
            bool landedHard = false;
            // ---- feet: jumping, landing, a step per stride, scuffing.
            if (c.Surface is Surface.Air or Surface.Ladder && s.Grounded)
            {
                var ground = onTrain ? train.Frames[s.Parent].Velocity : Double3.Zero;
                var rel = c.AirVelocity - ground;
                double across = Math.Sqrt(rel.X * rel.X + rel.Z * rel.Z), down = Math.Max(0, -c.AirVelocity.Y);
                string? mat = Footing.Under(s, world);
                double rollAbove = tuning?.Landing.RollAbove ?? 1.5;
                landedHard = s.Health < c.Health;
                if (c.Surface == Surface.Air || across > rollAbove)
                {
                    if (across > rollAbove)
                    {
                        // Off a moving train (spec B.3): the hit, and a roll along the ballast after.
                        Cue("crew-jump-off.impact", onTrain ? "grate" : "ground", feet, occlusion);
                        if (!onTrain)
                        {
                            c.TumbleUntil = _time + Math.Min(1.5, 0.4 + 0.08 * across);
                            CrewAfter(ScatterAfterLanding, "crew-mishaps.pocket-scatter", feet, occlusion);
                        }
                    }
                    else if (s.Surface == Surface.Coupler)
                        Cue("crew-ladder.gap-land", feet, occlusion);
                    else
                        Cue("crew-footsteps.land", mat ?? "ground", feet, occlusion, (float)Math.Clamp(0.5 + down / 6, 0.5, 1));
                }
                c.Moving = false;
                c.Stride = 0;
            }
            else if (c.Surface is not (Surface.Air or Surface.Ladder) && s.Surface == Surface.Air && s.Velocity.Y > 0.5)
                Cue("crew-footsteps.jump", c.LastFooting(world), feet, occlusion);
            else if (s.Grounded && c.Surface is not (Surface.Air or Surface.Ladder) && sameFrame && !s.Has(PlayerFlags.Held))
            {
                var d = s.Position - c.Position;
                double flat = Math.Sqrt(d.X * d.X + d.Z * d.Z);
                if (flat < 1.0)
                {
                    double speed = flat / SimConstants.TickSeconds;
                    if (speed > Standing)
                    {
                        if (!c.Moving)
                            c.Stride = Stride(speed) * 0.6; // the first foot goes down soon after you set off
                        c.Moving = true;
                        c.MovingFor += SimConstants.TickSeconds;
                        c.Speed = speed;
                        c.Stride += flat;
                        double stride = Stride(speed);
                        if (c.Stride >= stride)
                        {
                            c.Stride = Math.Min(c.Stride - stride, stride * 0.5);
                            Cue(Running(s.Surface, speed) ? "crew-footsteps.run" : "crew-footsteps.walk", Footing.Under(s, world) ?? "ground", feet, occlusion);
                        }
                        c.Turn = 0;
                    }
                    else
                    {
                        // Stopping short from a run, or turning on the spot.
                        bool stopped = c.Moving && c.MovingFor > 0.3 && Running(s.Surface, c.Speed);
                        c.Moving = false;
                        c.MovingFor = c.Stride = 0;
                        c.Turn += Math.Abs(Math.IEEERemainder(s.Yaw - c.Yaw, 2 * Math.PI));
                        if ((stopped || c.Turn > ScuffTurn) && _time - c.LastScuff > ScuffGap)
                        {
                            Cue("crew-footsteps.scuff", Footing.Under(s, world) ?? "ground", feet, occlusion);
                            c.LastScuff = _time;
                            c.Turn = 0;
                        }
                    }
                }
            }

            // ---- ladders: hands on, a rung at a time, off.
            var hands = feet + Double3.Up * HandsUp;
            if (s.Surface == Surface.Ladder)
            {
                int rung = (int)Math.Floor(s.Position.Y / TrainKit.RungPitch);
                if (c.Surface != Surface.Ladder)
                    Cue("crew-ladder.grab", hands, occlusion);
                else if (sameFrame && rung != c.Rung)
                    Cue(rung > c.Rung ? "crew-ladder.rung-up" : "crew-ladder.rung-down", hands, occlusion);
                c.Rung = rung;
            }
            else if (c.Surface == Surface.Ladder)
                Cue("crew-ladder.let-go", hands, occlusion);

            // ---- off the train and through the air (spec B.3): the rush, then the roll.
            if (s.Surface == Surface.Air)
            {
                if (c.Surface != Surface.Air)
                    c.LeftTrain = c.Parent != PlayerState.World;
                var v = PlayerMotor.WorldVelocity(s, train);
                if (c.LeftTrain && s.Parent == PlayerState.World && Math.Sqrt(v.X * v.X + v.Z * v.Z) > 4)
                    Hold("crew-jump-off.rush", id, feet + Double3.Up * HeadUp, occlusion);
            }
            if (_time < c.TumbleUntil)
                Hold("crew-jump-off.tumble", id, feet, occlusion);

            // ---- hurt.
            if (!c.Flags.HasFlag(PlayerFlags.Held) && s.Has(PlayerFlags.Held))
                Cue("crew-hurt.grabbed", feet + Double3.Up * ChestUp, occlusion);
            if (s.Health < c.Health && !landedHard)
                Cue(Burning(world, s) ? "crew-hurt.burned" : "crew-hurt.hit", feet + Double3.Up * ChestUp, occlusion,
                    (float)Math.Clamp(0.5 + (c.Health - s.Health) / 40.0, 0.5, 1));

            // ---- the cold (spec B.2): shivering past its onset; breath you can hear outside on a cold night.
            if (tuning is not null && PlayerMotor.Chilled(s, tuning))
                Hold("crew-cold.shiver", id, feet + Double3.Up * HeadUp, occlusion);
            bool outside = PlayerMotor.Space(s, train) == PlayerMotor.Outside;
            if (outside && (weather?.Cold >= 0.5 || tuning is not null && PlayerMotor.Chilled(s, tuning)))
            {
                if (_time >= c.NextBreath)
                {
                    bool hard = c.Moving && Running(s.Surface, c.Speed);
                    Cue(c.BreathIn ? "crew-cold.breath-in" : "crew-cold.breath-out", feet + Double3.Up * HeadUp, occlusion);
                    // A breath in, a longer one out; quicker running.
                    c.NextBreath = _time + (hard ? 0.7 : 1.4) * (c.BreathIn ? 1 : 1.3);
                    c.BreathIn = !c.BreathIn;
                }
            }
            else
                c.NextBreath = Math.Max(c.NextBreath, _time + 0.5 + 0.37 * (id % 5));

            // ---- tools (App. C.2): into the hand, back on the belt; your own swing and a crewmate's.
            var held = Kit.Held(s);
            if (held != c.Held)
            {
                if (c.Held != Tool.None)
                    Cue($"crew-melee.{ToolName(c.Held)}-stow", feet + Double3.Up * HipUp, occlusion);
                if (held != Tool.None)
                    Cue($"crew-melee.{ToolName(held)}-equip", hands, occlusion);
            }
            if (id == OwnId && OwnIntent.Has(PlayerActions.Swing) && !s.Has(PlayerFlags.Held) && CrewActs.Of(s, id, world) is null)
            {
                if (_time >= c.NextSwing)
                {
                    c.NextSwing = _time + (world.Enemies?.Melee.SwingSeconds ?? 0.8);
                    if (held != Tool.None)
                    {
                        Cue($"crew-melee.{ToolName(held)}-swing", hands, occlusion);
                        OwnBlow(world, s, held, occlusion);
                    }
                    else
                        Cue("crew-mishaps.bare-swing", hands, occlusion);   // a slot picked with nothing in it (T108): a sleeve
                }
            }
            else
                c.NextSwing = Math.Min(c.NextSwing, _time);
            if (_swungNow.Contains(id))
                Cue(held != Tool.None ? $"crew-melee.{ToolName(held)}-swing" : "crew-mishaps.bare-swing", hands, occlusion);

            // ---- hands at work.
            Working(world, id, s, c, occlusion);
            Remember(c, s, world);
        }
        foreach (var gone in _crewMembers.Keys.Where(k => !seen.Contains(k)).ToList())
            _crewMembers.Remove(gone);
    }

    void Remember(CrewMember c, in PlayerState s, World world)
    {
        var train = world.Train;
        if (s.Surface is Surface.Air)
            c.AirVelocity = PlayerMotor.WorldVelocity(s, train);
        else if (s.Surface == Surface.Ladder && s.Parent != PlayerState.World && s.Parent < train.Frames.Count)
            c.AirVelocity = train.Frames[s.Parent].Velocity; // stepping off the bottom rung, it's the car's speed you land at
        // Last footing, for a push-off or a fall that's left it (looked up then: it's a search of the ground off the train).
        if (s.Grounded)
            c.Footing = s;
        if (s.Surface == Surface.Ladder)
            c.Rung = (int)Math.Floor(s.Position.Y / TrainKit.RungPitch);
        c.Parent = s.Parent;
        c.Position = s.Position;
        c.Surface = s.Surface;
        c.Yaw = s.Yaw;
        c.Health = s.Health;
        c.Placed = s.Placed;
        c.Alive = s.Alive;
        c.Flags = s.Flags;
        c.Held = Kit.Held(s);
        c.Action = s.ActionProgress;
        c.Cold = s.Cold;
        c.Shovelful = s.Has(PlayerFlags.Shovelful);
    }

    /// <summary>
    /// Hurt by fire rather than a blow (the cause isn't sent till death): in a burning car near its fire (App. C.5's
    /// burn reach), or in the cab with a Stoker at the firebox.
    /// </summary>
    static bool Burning(World world, in PlayerState s)
    {
        double reach = (world.Enemies?.CarFire.BurnReach ?? 4) + 0.5;
        foreach (var e in world.ActiveEnemies)
        {
            if (e.Gone)
                continue;
            if (e is CarFire && e.Phase == SpinePhase.Punish && e.Attached == s.Parent && Math.Abs(s.Position.Z - e.Local.Z) <= reach)
                return true;
            if (e is Stoker && e.Phase is SpinePhase.Telegraph or SpinePhase.Commit or SpinePhase.Punish && PlayerMotor.InCab(s, world.Train))
                return true;
        }
        return false;
    }

    /// <summary>
    /// What a crewmate's hands are working by holding Use (CrewActions): the coupling pin, a sliding door's latch, a
    /// handbrake's wheel, the boiler with the wrench, the coal; and your own hand at a switch stand's lever.
    /// </summary>
    void Working(World world, int id, in PlayerState s, CrewMember c, float occlusion)
    {
        var train = world.Train;
        bool started = c.Action <= 0 && s.ActionProgress > 0, rising = s.ActionProgress > c.Action;
        // The shovel by hand (T29): coal onto the blade at the tender's face.
        if (s.Has(PlayerFlags.Shovelful) && !c.Shovelful)
            CoalFace(train, occlusion);
        // Your own hand on a switch stand's lever (its hold is host-only): the latch lifted as you take hold.
        if (id == OwnId && world.Switches is { } stands)
        {
            int? lever = OwnIntent.Has(PlayerButtons.Use) ? stands.InReach(s, train, world.Hand) : null;
            if (Rose("crew-switch.lever", id, lever is not null) && lever is { } b)
                Cue("crew-switch.lever-unlatch", stands.LeverAt(train.Line, b), Occlusion(PlayerMotor.Outside));
        }
        if (s.Parent == PlayerState.World || s.ActionProgress <= 0)
            return;
        var frame = train.Frames[s.Parent];
        var nearest = CrewActions.NearestInteractable(s, train, world.Hand);
        if (s.Surface == Surface.Coupler && nearest?.Thing.Kind != InteractableKind.Door)
        {
            // Cutting the coupling (T91; Use there works nothing else): the pin lifted as the hold begins.
            if (started)
                Cue("crew-coupling.pin-lift", frame.ToWorld(CouplerAt(frame.Shape)), occlusion);
            return;
        }
        if (nearest is not { } near)
            return;
        var thing = near.Thing;
        var at = train.Frames[near.Vehicle];
        switch (thing.Kind)
        {
            case InteractableKind.Door when thing.Index is 2 or 3 && started && !train.Vehicles[near.Vehicle].DoorOpen(thing.Index):
                Cue("crew-doors.slide-unlatch", at.ToWorld(DoorAt(at.Shape, thing.Index)), OccludedAt(train, near.Vehicle, thing.Position));
                break;
            case InteractableKind.Handbrake when rising:
                if (started || _time >= c.NextRatchet)
                {
                    Cue("crew-cab-controls.handbrake-ratchet", at.ToWorld(thing.Position + Double3.Up * 0.3), occlusion);
                    c.NextRatchet = _time + RatchetEvery;
                }
                break;
            case InteractableKind.Firebox when rising && train.Boiler.Ruptured && Kit.Held(s) == Tool.Wrench:
                Hold("crew-repair.ratchet", id, FireDoorWorld(train), Occlusion(0));
                break;
            // The keyboard's shovelling (no scoop of its own): a blade into the coal at each stroke's start, after each throw.
            case InteractableKind.Firebox when s.Hand == default && (started || s.ActionProgress < c.Action - 0.2):
                CoalFace(train, Occlusion(0));
                break;
        }
    }

    void CoalFace(TrainOnLine train, float occlusion)
    {
        var engine = train.Frames[0];
        if (engine.Shape.Interactables.FirstOrDefault(i => i.Kind == InteractableKind.Coal) is { Radius: > 0 } coal)
            Cue("crew-shovel.scoop", engine.ToWorld(coal.Position + Double3.Up * 0.4), occlusion);
    }

    /// <summary>The coupler plate behind a car (its frame), or where it would be.</summary>
    static Double3 CouplerAt(CarShape shape)
    {
        foreach (var solid in shape.Solids)
            if (solid.Part == PartKind.Coupler && solid.Box.Min.Z >= shape.HalfLength - 0.01)
                return solid.Box.Centre;
        return new Double3(0, 1.0, shape.HalfLength + 0.5);
    }

    static Double3 DoorAt(CarShape shape, int index)
    {
        foreach (var d in shape.DoorList)
            if (d.Index == index)
                return d.Box.Centre;
        return shape.Hatch is { } hatch && index == CarShape.HatchBit ? hatch.Centre with { Y = hatch.Max.Y } : Double3.Zero;
    }

    static Double3 FireDoorWorld(TrainOnLine train)
    {
        var engine = train.Frames[0];
        if (engine.Shape.Cab is null || !engine.Shape.Interactables.Any(i => i.Kind == InteractableKind.Firebox))
            return engine.ToWorld(new Double3(0, 2, 0));
        var door = TrainKit.FireDoor(engine.Shape);
        return engine.ToWorld(new Double3(door.X, door.Y, door.Z));
    }

    /// <summary>
    /// Your own blow landing on the train (hooks-map: the sim's swing only tests enemies): a ray from the eye along the look,
    /// as far as the tool reaches, onto the first solid; iron or wood by what it is. With something to hit in reach, it's
    /// that (the host's hit record, GameAudio.Strikes), not the train.
    /// </summary>
    void OwnBlow(World world, in PlayerState s, Tool held, float occlusion)
    {
        var train = world.Train;
        var melee = world.Enemies?.Melee ?? new MeleeTuning();
        var eye = PlayerMotor.WorldPosition(s, train) + Double3.Up * ChestUp;
        double yaw = PlayerMotor.WorldYaw(s, train);
        var facing = new Double3(-Math.Sin(yaw), 0, -Math.Cos(yaw));
        double cos = Math.Cos(melee.ConeDegrees * Math.PI / 180);
        foreach (var e in world.ActiveEnemies)
        {
            if (e.Gone || e.MeleeRadius <= 0)
                continue;
            var to = e.WorldPosition(train) + Double3.Up * 0.8 - eye;
            var flat = to with { Y = 0 };
            if (to.Length <= melee.Reach + e.MeleeRadius && (flat.Length <= 0.4 || Double3.Dot(flat.Normalized, facing) >= cos))
                return;
        }
        var look = new Double3(-Math.Sin(yaw) * Math.Cos(s.Pitch), Math.Sin(s.Pitch), -Math.Cos(yaw) * Math.Cos(s.Pitch));
        if (Strike(train, eye, look, melee.Reach) is { } hit)
            Cue($"crew-melee.{ToolName(held)}-hit-{(hit.Wood ? "wood" : "metal")}", hit.At, occlusion);
    }

    /// <summary>The first solid of the train along a ray within <paramref name="reach"/>: where, and whether it's wood.</summary>
    static (Double3 At, bool Wood)? Strike(TrainOnLine train, Double3 origin, Double3 dir, double reach)
    {
        double best = reach;
        bool wood = false, any = false;
        foreach (var frame in train.Frames)
        {
            if ((frame.Origin - origin).Length > 40)
                continue;
            var o = frame.ToLocal(origin);
            var d = frame.DirToLocal(dir);
            var v = train.Vehicles[frame.Index];
            foreach (var solid in frame.Shape.Solids)
                if (solid.Present(v) && Ray(o, d, solid.Box) is { } t && t < best)
                {
                    (best, any) = (t, true);
                    wood = !v.IsEngine && solid.Part is PartKind.Wall or PartKind.Cargo or PartKind.Locker or PartKind.Steps or PartKind.Chassis;
                }
            foreach (var door in frame.Shape.DoorList)
                if (!v.DoorOpen(door.Index) && Ray(o, d, door.Box) is { } t && t < best)
                    (best, any, wood) = (t, true, true);
        }
        return any ? (origin + dir * best, wood) : null;
    }

    static double? Ray(Double3 o, Double3 d, Box b)
    {
        double near = 0, far = double.PositiveInfinity;
        for (int axis = 0; axis < 3; axis++)
        {
            double oa = axis == 0 ? o.X : axis == 1 ? o.Y : o.Z, da = axis == 0 ? d.X : axis == 1 ? d.Y : d.Z;
            double lo = axis == 0 ? b.Min.X : axis == 1 ? b.Min.Y : b.Min.Z, hi = axis == 0 ? b.Max.X : axis == 1 ? b.Max.Y : b.Max.Z;
            if (Math.Abs(da) < 1e-12)
            {
                if (oa < lo || oa > hi)
                    return null;
                continue;
            }
            double t0 = (lo - oa) / da, t1 = (hi - oa) / da;
            if (t0 > t1)
                (t0, t1) = (t1, t0);
            near = Math.Max(near, t0);
            far = Math.Min(far, t1);
            if (near > far)
                return null;
        }
        return near;
    }

    // ------------------------------------------------------------------------------------------------ doors and lamps

    void CrewVehicles(World world)
    {
        var train = world.Train;
        foreach (var v in train.Vehicles)
        {
            if (v.Id >= train.Frames.Count)
                continue;
            var frame = train.Frames[v.Id];
            var shape = frame.Shape;
            if (!_crewCars.TryGetValue(v.Id, out var car))
            {
                _crewCars[v.Id] = new CrewCar { Doors = v.DoorsOpen, Lamp = v.LampLit };
                continue;
            }
            int changed = v.DoorsOpen ^ car.Doors;
            for (int bit = 0; bit <= CarShape.HatchBit; bit++)
            {
                if ((changed & (1 << bit)) == 0 && car.RollUntil[bit] <= 0)
                    continue;
                var local = DoorAt(shape, bit);
                var at = frame.ToWorld(local);
                float occlusion = OccludedAt(train, v.Id, local);
                bool open = v.DoorOpen(bit);
                if ((changed & (1 << bit)) != 0)
                {
                    if (bit == CarShape.HatchBit)
                        Cue(open ? "crew-doors.hatch-open" : "crew-doors.hatch-shut", at, occlusion);
                    else if (bit is 2 or 3)
                    {
                        if (open)
                        {
                            Cue("crew-doors.slide-start", at, occlusion);
                            car.RollUntil[bit] = _time + SlideRollSeconds;
                        }
                        else
                        {
                            car.RollUntil[bit] = 0;
                            Cue("crew-doors.slide-shut", at, occlusion);
                            CrewAfter(SlideLatchAfter, "crew-doors.slide-latch", at, occlusion);
                        }
                    }
                    else
                        Cue(open ? "crew-doors.end-open" : "crew-doors.end-shut", at, occlusion);
                }
                // The sliding door rolls along its track, and hits its stop.
                if (car.RollUntil[bit] > 0)
                {
                    if (_time < car.RollUntil[bit])
                        Hold("crew-doors.slide-roll", v.Id * 8 + bit, at, occlusion);
                    else
                    {
                        car.RollUntil[bit] = 0;
                        Cue("crew-doors.slide-open-stop", at, occlusion);
                    }
                }
            }
            car.Doors = v.DoorsOpen;
            // A car's lamp (App. A.5), by its ceiling lamps.
            if (v.LampLit != car.Lamp && shape.Interior is { } room)
            {
                var local = new Double3(0, room.Max.Y - 0.2, room.Centre.Z);
                Cue(v.LampLit ? "crew-lamps.car-lamp-on" : "crew-lamps.car-lamp-off", frame.ToWorld(local), OccludedAt(train, v.Id, local));
            }
            car.Lamp = v.LampLit;
        }
    }

    // ------------------------------------------------------------------------------------------------ the engine

    /// <summary>
    /// Whether the cord's whistle is a sound of its own rather than the train's (train-whistle): never, by spec A.4's rule for
    /// the Whistler's tell ("the same whistle the conductor blows, from the same dome, so the only tell is that nobody
    /// pulled it"). The crew's whistle cues (crew-cab-controls.whistle-start/whistle/whistle-stop) take over only together
    /// with the Whistler's, as one sound; until then the cord here is just the pull. <see cref="Whistle"/> asks.
    /// </summary>
    static bool CordWhistles(World world) => false;

    static bool WhistlerWhistling(World world) => world.ActiveEnemies.Any(e => e is Whistler w && !w.Gone && w.Whistling);

    void CrewEngineRoom(World world)
    {
        var train = world.Train;
        var engine = train.Frames[0];
        var shape = engine.Shape;
        var boiler = train.Boiler;
        var controls = world.Controls;
        double sinceShovel = boiler.SinceShovel;
        if (_crewEngine is not { } e)
        {
            _crewEngine = new CrewEngine
            {
                FireDoor = boiler.FireDoorOpen,
                Vented = boiler.Vented,
                Lamp = world.LampLit,
                WrenchOut = boiler.WrenchOut,
                Ruptured = boiler.Ruptured,
                Tender = boiler.Tender,
                SinceShovel = sinceShovel,
                Throttle = controls.Throttle,
                Brake = controls.Brake,
                Reverser = controls.Reverser,
                Sand = train.Sand,
                Whistle = world.WhistleSeconds,
                LampOut = world.LampOutSeconds,
            };
            return;
        }
        float cab = Occlusion(0);
        var fireDoor = FireDoorWorld(train);
        Double3 At(InteractableKind kind, double up = 0) =>
            shape.Interactables.FirstOrDefault(i => i.Kind == kind) is { Radius: > 0 } i ? engine.ToWorld(i.Position + Double3.Up * up) : engine.Origin;

        // The firebox door (App. A.5): open with a shovelful, shut a few seconds after; the fire heard through it meanwhile.
        if (boiler.FireDoorOpen != e.FireDoor)
            Cue(boiler.FireDoorOpen ? "crew-firebox-door.open" : "crew-firebox-door.shut", fireDoor, cab);
        if (boiler.FireDoorOpen && Hold("crew-firebox-door.fire-open", 0, fireDoor, cab) is { } fire && train.BoilerTuning is { } bt)
            fire.Params.Set("fire", boiler.FireFraction(bt));
        // A shovelful thrown in (spec B.6): coal out of the tender, or the door's clock started again (a full firebox
        // takes no coal, but the throw's still made). The old composite shovel sound until the throw has its own.
        if (boiler.Tender <= e.Tender - 0.5 || sinceShovel < e.SinceShovel - 0.05)
        {
            if (Cue("crew-shovel.throw", fireDoor, cab) is null && !HasCue("crew-shovel.throw"))
                Mixer.Play("shovel", fireDoor)?.Also(v => v.Occlusion = cab);
            if (_rng.Next() < 0.5)
                Cue("crew-shovel.knock", fireDoor, cab);
        }

        // The driver's levers (T29: where the hands go, as they're set).
        if (shape.Levers is { } levers)
        {
            if (Math.Abs(controls.Throttle - e.Throttle) > 0.01)
                Cue("crew-cab-controls.regulator-notch", engine.ToWorld(levers.RegulatorAt(controls.Throttle)), cab);
            if (Math.Sign(controls.Reverser) != Math.Sign(e.Reverser))
                Cue("crew-cab-controls.reverser-notch", engine.ToWorld(levers.ReverserAt(controls.Reverser)), cab);
            if (Math.Abs(controls.Brake - e.Brake) > 0.01)
                Cue("crew-cab-controls.brake-handle", engine.ToWorld(levers.BrakeAt(controls.Brake)), cab);
            // The brake applying against the train's motion (at a stand it's only held on).
            if (controls.Brake > 0 && Math.Abs(train.Dynamics.Velocity) > 0.5)
                Hold("crew-cab-controls.brake-apply", 0, engine.ToWorld(levers.Brake), cab);
        }

        // Sand (App. A.2): the lever pulled as it starts going down, and running onto the rail while it does. Sanding isn't
        // sent, only the grip it's brought back: rising, or full with someone still at a sandbox. A client re-steps the
        // fade over the host's value each tick, so it wobbles a little: the trend over a few ticks is what's read.
        e.SandTrend = 0.7 * e.SandTrend + 0.3 * (train.Sand - e.Sand);
        var box = Sandbox(world);
        e.SandIdle = e.SandTrend > 1e-6 ? 0 : e.SandIdle + 1;
        bool sanding = e.SandTrend > 1e-6 || e.Sanding && (e.SandIdle < 6 || train.Sand >= 0.999 && box is not null);
        var sandAt = box is { } b ? engine.ToWorld(b) : At(InteractableKind.Sandbox);
        if (sanding && !e.Sanding)
            Cue("crew-cab-controls.sander-lever", sandAt + Double3.Up * 0.9, Occlusion(PlayerMotor.Outside));
        if (sanding)
            Hold("crew-cab-controls.sand-flow", 0, sandAt with { Y = engine.ToWorld(Double3.Zero).Y + 0.3 }, Occlusion(PlayerMotor.Outside));
        e.Sanding = sanding;

        if (boiler.Vented && !e.Vented)
            Cue("crew-cab-controls.vent-handle", At(InteractableKind.Vent, 0.9), cab);

        // The whistle cord (GDD §12): pulled, the whistle sounds, holds while the cord's held (WhistleSeconds stays topped up
        // to a second), and tails off when it's let go (it falls, tick after tick). A client re-steps the countdown over the
        // host's value, so a held cord's reading wobbles by a tick or two: a release is a fall kept up, a fresh pull a jump
        // back up. The Whistler's whistle is its own (tell-whistler).
        double whistle = world.WhistleSeconds;
        bool cord = whistle > 0 && !WhistlerWhistling(world);
        var dome = engine.ToWorld(new Double3(0, shape.RoofHeight + 0.6, -2));
        float outside = Occlusion(PlayerMotor.Outside);
        if (cord && (e.Whistle <= 0 || e.Released && whistle > e.WhistleLow + 0.15))
        {
            var cordAt = shape.Cab is { } cabBox ? engine.ToWorld(new Double3(0, cabBox.Max.Y - 0.2, cabBox.Centre.Z)) : dome;
            Cue("crew-cab-controls.whistle-cord", cordAt, cab);
            if (CordWhistles(world))
                Cue("crew-cab-controls.whistle-start", dome, outside);
            e.Released = false;
            e.WhistleFalling = 0;
        }
        else if (cord && !e.Released)
        {
            e.WhistleFalling = whistle < e.Whistle - 1e-4 ? e.WhistleFalling + 1 : 0;
            if (e.WhistleFalling >= 3)
            {
                e.Released = true;
                e.WhistleLow = whistle;
                if (CordWhistles(world))
                    Cue("crew-cab-controls.whistle-stop", dome, outside);
            }
        }
        else if (cord)
            e.WhistleLow = Math.Min(e.WhistleLow, whistle);
        if (cord && !e.Released && CordWhistles(world))
            Hold("crew-cab-controls.whistle", 0, dome, outside);
        if (whistle <= 0)
        {
            e.Released = false;
            e.WhistleFalling = 0;
        }

        // The forward lamp's switch (T52), in the cab; not when the Lamplighters smash it out.
        if (world.LampLit != e.Lamp && !(world.LampOutSeconds > e.LampOut + 1))
            Cue("crew-lamps.cab-lamp", shape.Cab is { } c ? engine.ToWorld(new Double3(0, c.Max.Y - 0.3, c.Min.Z + 0.3)) : World.LampPosition(engine), cab);

        // The engineering kit's rack (T109): the wrench out to mend a burst boiler is the repair kit opened; otherwise it's the
        // rack. Back on it after a mend, the kit's shut.
        var rack = At(InteractableKind.ToolRack, 0.7);
        if (boiler.WrenchOut && !e.WrenchOut)
        {
            Cue(boiler.Ruptured ? "crew-repair.kit-open" : "crew-doors.locker-open", rack, cab);
            e.MendedSinceWrench = false;
        }
        if (!boiler.Ruptured && e.Ruptured)
        {
            Cue("crew-repair.done", fireDoor, cab);
            e.MendedSinceWrench = true;
        }
        if (!boiler.WrenchOut && e.WrenchOut)
            Cue(e.MendedSinceWrench || boiler.Ruptured ? "crew-repair.kit-shut" : "crew-doors.locker-shut", rack, cab);

        e.FireDoor = boiler.FireDoorOpen;
        e.Vented = boiler.Vented;
        e.Lamp = world.LampLit;
        e.LampOut = world.LampOutSeconds;
        e.WrenchOut = boiler.WrenchOut;
        e.Ruptured = boiler.Ruptured;
        e.Tender = boiler.Tender;
        e.SinceShovel = sinceShovel;
        e.Throttle = controls.Throttle;
        e.Brake = controls.Brake;
        e.Reverser = controls.Reverser;
        e.Sand = train.Sand;
        e.Whistle = whistle;
    }

    /// <summary>The sandbox (engine frame) a crewmate on the running boards is at, if anyone is.</summary>
    Double3? Sandbox(World world)
    {
        var train = world.Train;
        foreach (var (_, s) in CrewStates)
            if (s.Alive && s.Parent == 0 && s.Surface == Surface.Deck
                && CrewActions.NearestInteractable(s, train, world.Hand) is { Thing.Kind: InteractableKind.Sandbox } near)
                return near.Thing.Position;
        return null;
    }

    // ------------------------------------------------------------------------------------------------ couplings and handbrakes

    void CrewCouplings(World world)
    {
        var train = world.Train;
        var pairs = new HashSet<(int, int)>();
        foreach (var rake in train.Rakes)
        {
            var ids = rake.Consist.Vehicles;
            for (int i = 0; i + 1 < ids.Count; i++)
                pairs.Add((ids[i].Id, ids[i + 1].Id));
        }
        if (_crewCouplings is { } was)
        {
            foreach (var (front, rear) in was)
                if (!pairs.Contains((front, rear)) && front < train.Frames.Count && rear < train.Frames.Count)
                {
                    // Cut (T91, or the Car Hugger, the Passenger): the knuckle opens and the hoses pull apart.
                    var at = Gap(train, front);
                    Cue("crew-coupling.knuckle-release", at, Occlusion(PlayerMotor.Outside));
                    Cue("crew-coupling.hose-part", at, Occlusion(PlayerMotor.Outside));
                }
            foreach (var (front, rear) in pairs)
                if (!was.Contains((front, rear)) && front < train.Frames.Count)
                {
                    var at = Gap(train, front);
                    Cue("crew-coupling.knuckle-close", at, Occlusion(PlayerMotor.Outside));
                    CrewAfter(PinDropAfter, "crew-coupling.pin-drop", at, Occlusion(PlayerMotor.Outside));
                }
        }
        _crewCouplings = pairs;

        // A rake's handbrakes wound on or knocked off (a rake left standing has them; the engine's rake brakes on air).
        var keys = new HashSet<int>();
        foreach (var rake in train.Rakes)
        {
            var ids = rake.Consist.Vehicles;
            if (ids.Count == 0)
                continue;
            int key = ids[0].Id;
            keys.Add(key);
            if (_crewRakes.TryGetValue(key, out var r) && r.Count == ids.Count && r.On != rake.Handbrake)
                Cue(rake.Handbrake ? "crew-cab-controls.handbrake-set" : "crew-cab-controls.handbrake-release", Wheel(world, rake), Occlusion(PlayerMotor.Outside));
            _crewRakes[key] = (ids.Count, rake.Handbrake);
        }
        foreach (var gone in _crewRakes.Keys.Where(k => !keys.Contains(k)).ToList())
            _crewRakes.Remove(gone);
    }

    static Double3 Gap(TrainOnLine train, int front)
    {
        var frame = train.Frames[front];
        return frame.ToWorld(CouplerAt(frame.Shape));
    }

    /// <summary>The brake wheel someone's winding in this rake, or its first car's.</summary>
    Double3 Wheel(World world, TrainDynamics rake)
    {
        var train = world.Train;
        foreach (var (_, s) in CrewStates)
            if (s.Alive && s.Parent != PlayerState.World && s.Parent < train.Frames.Count && rake.Consist.IndexOf(s.Parent) >= 0
                && CrewActions.NearestInteractable(s, train, world.Hand) is { Thing.Kind: InteractableKind.Handbrake } near)
                return train.Frames[near.Vehicle].ToWorld(near.Thing.Position);
        foreach (var v in rake.Consist.Vehicles)
            if (v.Id < train.Frames.Count && train.Frames[v.Id].Shape.Interactables.FirstOrDefault(i => i.Kind == InteractableKind.Handbrake) is { Radius: > 0 } wheel)
                return train.Frames[v.Id].ToWorld(wheel.Position);
        return train.Frames[Math.Min(rake.Consist.Vehicles[0].Id, train.Frames.Count - 1)].Origin;
    }

    // ------------------------------------------------------------------------------------------------ switches

    void CrewSwitchStands(World world)
    {
        var train = world.Train;
        var line = train.Line;
        int n = line.Branches.Count;
        if (_crewSwitches is null || _crewSwitches.Length != n)
        {
            _crewSwitches = new bool[n];
            for (int i = 0; i < n; i++)
                _crewSwitches[i] = train.Diverging(i);
            return;
        }
        for (int i = 0; i < n; i++)
        {
            bool diverging = train.Diverging(i);
            if (diverging == _crewSwitches[i])
                continue;
            _crewSwitches[i] = diverging;
            var toe = line.Sample(line.Branches[i].Toe).Position;
            var lever = world.Switches?.LeverAt(line, i) ?? toe;
            float outside = Occlusion(PlayerMotor.Outside);
            Cue("crew-switch.lever-throw", lever, outside);
            Cue("crew-switch.points-move", toe, outside);
            CrewAfter(LeverLatchAfter, "crew-switch.lever-latch", lever, outside);
        }
    }

    // ------------------------------------------------------------------------------------------------ the cannons

    /// <summary>
    /// Everyone's cannons (App. C.3), from the replicated gun: a round gone from it (its ammunition, which
    /// <see cref="GunState.LastShotTick"/> goes with; that stamp is the predicting machine's own tick until the host's
    /// arrives, so it changes more than once a shot), the gun pushed along its rail, and each step of the reload.
    /// </summary>
    void CrewGuns(World world)
    {
        var train = world.Train;
        int steps = world.Combat?.Guns.ReloadSteps ?? 3;
        foreach (var v in train.Vehicles)
        {
            if (v.Id >= train.Frames.Count)
                continue;
            var g = v.Gun;
            if (!_crewGuns.TryGetValue(v.Id, out var m))
            {
                _crewGuns[v.Id] = new CrewGun
                {
                    Mounted = g.Mounted,
                    Ammo = g.Ammo,
                    Z = g.Z,
                    Needed = g.ReloadNeeded,
                    Progress = g.ReloadProgress,
                    Traverse = g.Traverse,
                    Elevation = g.Elevation
                };
                continue;
            }
            if (g.Mounted && Guns.Mount(train, v.Id) is { } mount)
            {
                var frame = train.Frames[v.Id];
                var muzzle = frame.ToWorld(mount.Position + mount.Facing * -TrainKit.CannonMuzzle.Z);
                float outside = Occlusion(PlayerMotor.Outside);
                if (m.Mounted && g.Ammo < m.Ammo && g.LastShotTick > 0)
                {
                    bool far = (muzzle - Mixer.Listener.Position).Length > ShotFar;
                    if (Cue(far ? "crew-cannon-fire.shot-far" : "crew-cannon-fire.shot-close", muzzle, outside) is null
                        && Cue(far ? "crew-cannon-fire.shot-close" : "crew-cannon-fire.shot-far", muzzle, outside) is null)
                        Mixer.Play("gunshot", muzzle)?.Also(x => x.Occlusion = outside);
                    Cue("crew-cannon-fire.ignite", frame.ToWorld(mount.Position - mount.Facing * 0.55 + Double3.Up * 0.15), outside);
                    Cue("crew-cannon-fire.recoil", frame.ToWorld(mount.Position), outside);
                }
                // Pushed along its rail (T93), or over the coupling onto this car.
                bool moving = !m.Mounted || Math.Abs(g.Z - m.Z) > 1e-4;
                if (moving)
                {
                    m.LastMove = _time;
                    m.Traversing = true;
                }
                if (m.Traversing && _time - m.LastMove < 0.15)
                    Hold("crew-cannon-fire.traverse", v.Id, frame.ToWorld(mount.Position), outside);
                else if (m.Traversing)
                {
                    m.Traversing = false;
                    Cue("crew-cannon-fire.traverse-stop", frame.ToWorld(mount.Position), outside);
                }
                // Laid by its seated gunner (T112): the steam motor and its gear while it turns or lifts, faster the faster it
                // goes (combat.json's traverse rate is full), and the gear's clunk as it stops.
                double laid = Math.Abs(g.Traverse - m.Traverse) + Math.Abs(g.Elevation - m.Elevation);
                if (laid > 1e-5)
                {
                    m.LastLaid = _time;
                    double full = (world.Combat?.Guns.TraverseDegreesPerSecond ?? 70) * Math.PI / 180 * SimConstants.TickSeconds;
                    m.Laying = true;
                    Hold("gun-lay", v.Id, frame.ToWorld(mount.Position), outside)?.Params.Set("speed", Math.Clamp(laid / full, 0, 1));
                }
                else if (m.Laying && _time - m.LastLaid < 0.1)
                    Hold("gun-lay", v.Id, frame.ToWorld(mount.Position), outside);
                else if (m.Laying)
                {
                    m.Laying = false;
                    Cue("crew-cannon-fire.traverse-stop", frame.ToWorld(mount.Position), outside, 0.6f);
                }
                // The reload: powder, ball, ram (App. C.3), a step at a time while Use is held.
                if (m.Mounted && g.ReloadNeeded < m.Needed)
                {
                    int done = steps - m.Needed;
                    Cue(g.ReloadNeeded == 0 ? "crew-cannon-ram.ram-home" : done == 0 ? "crew-cannon-reload.powder-done" : "crew-cannon-ball.ball-roll",
                        g.ReloadNeeded == 0 || done == 0 ? muzzle : frame.ToWorld(mount.Position), outside);
                }
                if (g.ReloadNeeded >= steps)
                    m.BallIn = false;
                if (g.ReloadNeeded > 0 && g.ReloadProgress > m.Progress + 1e-6)
                {
                    int step = steps - g.ReloadNeeded;
                    if (step <= 0)
                        Hold("crew-cannon-reload.powder", v.Id, muzzle, outside);
                    else if (step == 1 && steps > 2)
                    {
                        if (!m.BallIn)
                            Cue("crew-cannon-ball.ball-in", muzzle, outside);
                        m.BallIn = true;
                    }
                    else
                        Hold("crew-cannon-ram.ram", v.Id, muzzle, outside);
                }
            }
            else
                m.Traversing = false;
            m.Mounted = g.Mounted;
            m.Ammo = g.Ammo;
            m.Z = g.Z;
            m.Traverse = g.Traverse;
            m.Elevation = g.Elevation;
            m.Needed = g.ReloadNeeded;
            m.Progress = g.ReloadProgress;
        }
    }

    // ------------------------------------------------------------------------------------------------ what's carried

    /// <summary>
    /// The loose bodies (GDD §33) and the hands on them: picked up, and set down or thrown and landing on whatever they come
    /// to rest on (set or thrown isn't sent: it's how fast it went after it left the hands); the extinguisher off its mount,
    /// spraying while its charge falls (or your own trigger's held), and back on.
    /// </summary>
    void CrewCarried(World world, bool firePressed)
    {
        var train = world.Train;
        var hands = world.Bodies.Hands;
        var live = new HashSet<int>();
        foreach (var b in world.Bodies.All)
        {
            live.Add(b.Id);
            if (b.Parent != PlayerState.World && b.Parent >= train.Frames.Count)
                continue;
            var centre = Bodies.WorldCentre(b, train);
            float occlusion = b.Parent == PlayerState.World ? Occlusion(PlayerMotor.Outside) : OccludedAt(train, b.Parent, b.Centre);
            bool lifted = b.Kind == BodyKind.Heavy ? b.Lifted : b.Carrier >= 0;
            if (!_crewBodies.TryGetValue(b.Id, out var m))
            {
                _crewBodies[b.Id] = new CrewBody
                {
                    Carrier = b.Carrier,
                    Parent = b.Parent,
                    Lifted = lifted,
                    Charge = b.Charge,
                    Local = b.Centre,
                    OnMount = OnMount(train, b),
                    Hint = train.Dynamics.Distance
                };
                continue;
            }
            if (lifted && !m.Lifted)
            {
                m.Falling = false;
                string? lift = b.Kind switch
                {
                    BodyKind.Crate or BodyKind.Cargo or BodyKind.Heavy => "crew-carry.crate-lift",
                    BodyKind.Lamp => "crew-carry.lamp-lift",
                    BodyKind.Toy => "crew-carry.toy-lift",
                    BodyKind.Radio => "crew-carry.radio-lift",
                    BodyKind.Ragdoll or BodyKind.Child => "crew-carry.body-lift",
                    BodyKind.Loot => "crew-carry.loot-take",
                    BodyKind.Extinguisher => m.OnMount ? "crew-extinguisher.grab-mount" : "crew-extinguisher.equip",
                    _ => null,
                };
                if (lift is not null)
                    Cue(lift, centre, occlusion);
            }
            else if (!lifted && m.Lifted)
            {
                m.Falling = true;
                m.Thrown = false;
                m.Still = 0;
                m.ReleasedAt = _time;
            }
            else if (m.Falling)
            {
                // How it moves once it's left the hands: in its frame, or (off the train into the world) thrown.
                double moved = b.Parent == m.Parent ? (b.Centre - m.Local).Length : double.PositiveInfinity;
                if (moved / SimConstants.TickSeconds > 3)
                    m.Thrown = true;
                m.Still = moved < AtRest ? m.Still + 1 : 0;
                if (b.Pbd.Asleep || m.Still >= 3 || _time - m.ReleasedAt > 4)
                {
                    m.Falling = false;
                    Landed(world, b, m, centre, occlusion);
                }
            }

            // A body carried through a doorway (in or out of a car, into the cab): its dangling boots knock the frame.
            if (b.Kind is BodyKind.Ragdoll or BodyKind.Child && b.Carrier >= 0
                && CrewStates.FirstOrDefault(x => x.Id == b.Carrier) is { State.Alive: true } bearer && bearer.Id == b.Carrier)
            {
                int space = PlayerMotor.Space(bearer.State, train);
                if (m.CarrierSpace is { } was && was != space)
                    Cue("crew-mishaps.body-knock", PlayerMotor.WorldPosition(bearer.State, train) + Double3.Up * 0.9, occlusion);
                m.CarrierSpace = space;
            }
            else
                m.CarrierSpace = null;

            // A crate sliding along a car's floor under it (the train braking or pulling hard, a shove): it scrapes on what
            // it's on for as long as it's moving, louder the faster it goes. Lifted, flying or asleep, it isn't.
            // (Its height, not the particles' contact, says it's on the floor: a client's mirrored body has no contacts.)
            if (b.Kind is BodyKind.Crate or BodyKind.Cargo or BodyKind.Heavy && !lifted && !m.Falling && b.Parent == m.Parent
                && b.Parent != PlayerState.World && !b.Pbd.Asleep)
            {
                var step = (b.Centre - m.Local) * (1 / SimConstants.TickSeconds);
                double slide = (step with { Y = 0 }).Length;
                if (slide > CrateSlides && Math.Abs(step.Y) < 0.3)
                {
                    var lowest = b.Pbd.Particles.MinBy(p => p.Position.Y - p.Radius);
                    string mat = Footing.UnderBody(world, b.Parent, lowest.Position - Double3.Up * lowest.Radius, ref m.Hint);
                    if (Surfaced("crew-carry.crate-drag", mat) is { } drag && Hold(drag, b.Id, centre, occlusion) is { } scrape)
                        scrape.Volume = (float)Math.Clamp(slide / 1.2, 0.35, 1);
                }
            }

            // The extinguisher at work (App. C.5).
            if (b.Kind == BodyKind.Extinguisher)
            {
                bool carried = b.Carrier >= 0;
                if (carried && b.Charge < m.Charge - 1e-9)
                    m.LastDrain = _time;
                var carrier = carried ? CrewStates.FirstOrDefault(x => x.Id == b.Carrier) : default;
                bool own = carried && b.Carrier == OwnId && carrier.State.Alive && OwnIntent.Has(PlayerButtons.Fire);
                bool spraying = carried && b.Charge > 0 && (_time - m.LastDrain < 0.3 || own);
                var at = centre;
                if (carried && carrier.Id == b.Carrier && (carrier.State.Parent == PlayerState.World || carrier.State.Parent < train.Frames.Count))
                {
                    double yaw = PlayerMotor.WorldYaw(carrier.State, train);
                    at = PlayerMotor.WorldPosition(carrier.State, train) + Double3.Up * hands.CarryHeight
                        + new Double3(-Math.Sin(yaw), 0, -Math.Cos(yaw)) * hands.CarryForward;
                }
                if (spraying && !m.Spraying)
                    Cue("crew-extinguisher.spray-start", at, occlusion);
                if (spraying)
                    Hold("crew-extinguisher.spray", b.Id, at, occlusion);
                else if (m.Spraying)
                {
                    Cue(b.Charge <= 0 ? "crew-extinguisher.run-dry" : "crew-extinguisher.spray-stop", at, occlusion);
                    if (b.Charge <= 0)
                        CrewAfter(DregsAfterDry, "crew-mishaps.extinguisher-dregs", at, occlusion);
                }
                if (firePressed && carried && b.Carrier == OwnId && b.Charge <= 0)
                    Cue("crew-extinguisher.dry-trigger", at, occlusion);
                m.Spraying = spraying;
            }
            m.Carrier = b.Carrier;
            m.Lifted = lifted;
            m.Parent = b.Parent;
            m.Local = b.Centre;
            m.Charge = b.Charge;
            if (!lifted && !m.Falling)
                m.OnMount = OnMount(train, b);
        }
        foreach (var gone in _crewBodies.Keys.Where(k => !live.Contains(k)).ToList())
            _crewBodies.Remove(gone);
    }

    /// <summary>A body come to rest after it left someone's hands: set down, or thrown and landed, on what it lies on.</summary>
    void Landed(World world, Body b, CrewBody m, Double3 centre, float occlusion)
    {
        var lowest = b.Pbd.Particles.MinBy(p => p.Position.Y - p.Radius);
        string mat = Footing.UnderBody(world, b.Parent, lowest.Position - Double3.Up * lowest.Radius, ref m.Hint);
        string? name = b.Kind switch
        {
            BodyKind.Crate or BodyKind.Cargo or BodyKind.Heavy => m.Thrown ? "crew-carry.crate-land" : "crew-carry.crate-set",
            BodyKind.Lamp => "crew-carry.lamp-set",
            BodyKind.Toy => "crew-carry.toy-drop",
            BodyKind.Radio => "crew-carry.radio-drop",
            BodyKind.Ragdoll or BodyKind.Child => m.Thrown ? "crew-carry.body-land" : "crew-carry.body-set",
            BodyKind.Extinguisher when OnMount(world.Train, b) => "crew-extinguisher.return-mount",
            BodyKind.Extinguisher => "crew-extinguisher.drop",
            _ => null,
        };
        if (name == "crew-extinguisher.return-mount")
            Cue(name, centre, occlusion);
        else if (name is not null)
            Cue(name, mat, centre, occlusion);
        if (b.Kind is BodyKind.Ragdoll or BodyKind.Child && Surfaced("crew-mishaps.body-boot", mat) is { } boot)
            CrewAfter(BootAfterBody, boot, centre, occlusion);
    }

    /// <summary>An extinguisher standing on its car's mount (the art's own test: World.ExtinguisherMount, within 0.35 m).</summary>
    static bool OnMount(TrainOnLine train, Body b)
    {
        if (b.Kind != BodyKind.Extinguisher || b.Carrier >= 0 || b.Parent == PlayerState.World || b.Parent >= train.Frames.Count
            || train.Frames[b.Parent].Shape.Interior is not { } room)
            return false;
        var mount = World.ExtinguisherMount(train.Frames[b.Parent].Shape, room);
        return Math.Abs(b.Centre.X - mount.X) < 0.35 && Math.Abs(b.Centre.Z - mount.Z) < 0.35;
    }
}
