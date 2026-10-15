using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Art;

/// <summary>
/// What a crewmate is doing with their hands (X1), read off what the snapshot already carries about them: their flags,
/// their surface, their timed action and what's nearest it, what they're carrying, the gun they're at. Presentation only:
/// nothing here is sent or fed back to the sim, so it's free to guess (a frame's lag in an act doesn't matter), and the
/// crew's clips for it are tools/blender/crew_clips.py's (note 145).
/// </summary>
public static class CrewActs
{
    /// <summary>Falling faster than this (m/s down), someone in the air is falling, not at the top of a jump.</summary>
    const double Falling = 3;

    /// <summary>Rising faster than this (m/s up), someone in the air has jumped (a step off a ledge never rises).</summary>
    const double Rising = 0.5;

    /// <summary>
    /// The crewmate <paramref name="id"/> as they're drawn this frame: where they are and what they're doing. At a gun they're
    /// sat on its seat, facing its way (note 137's cannon: the seat 0.75 m behind the pivot, on the roof under it).
    /// </summary>
    /// <param name="crew">Everyone by id (themselves too), for note 722's emotes for two: how far a high five's partner stands,
    /// who's in a dance-off. Without it a met high five is drawn at a middling gap and nobody dances off.</param>
    public static Crewmate Crewmate(byte id, in PlayerState s, World world, IReadOnlyList<CarFrame> frames, IReadOnlyList<PlayerState>? others = null,
        IReadOnlyList<(int Id, PlayerState State)>? crew = null)
    {
        var act = Of(s, id, world, others);
        var placed = s.Has(PlayerFlags.Lounging) ? Lounging(s, world.Train)
            : s.Has(PlayerFlags.InCannon) && world.Combat is { } combat ? InTheCannon(s, world.Train, combat.Cannonball)
            : act is CrewPose.Gunner or CrewPose.Reload && Guns.Mount(world.Train, s.Parent) is { } gun ? Seated(s, gun) : s;
        var (feet, yaw) = Eyes.World(placed, frames);
        var (emote, emoteSeconds) = act is null ? EmoteOf(id, s, world) : (Emote.None, 0);
        int outfit = world.OutfitOf(id);
        bool lamp = world.Bodies.CarriedBy(id) is { Kind: BodyKind.Lamp };
        var (signal, signalSeconds) = lamp ? SignalOf(id, s, world) : (LampSignal.None, 0);
        var drawn = new Crewmate(id, feet, yaw, s.Alive, s.Hand, s.OtherHand, Looks: outfit != id ? outfit : null, Act: act, Holding: Sim.Player.Kit.Held(s),
            Reach: act is CrewPose.Drive or CrewPose.Whistle ? AtTheControls(world, frames, feet, yaw, act == CrewPose.Whistle)
                : s.Has(PlayerFlags.Pumping) ? AtTheHandle(world.Train, frames, feet, yaw, s) : null,
            Lamp: lamp, Survivor: SurvivorOf(id, world),
            Stressed: s.Alive && Stressed(world, PlayerMotor.WorldPosition(s, world.Train)), Health: s.Health,
            Phase: act == CrewPose.Reload ? ReloadPhase(s, world) : act == CrewPose.Pump ? PumpPhase(s, world.Train) : 0, Death: s.Death,
            Headset: s.Head > 0 ? new HeadsetBody(s.Head, s.Pitch, s.Parent, s.Position, s.Yaw) : null,
            Car: placed.Parent != PlayerState.World && placed.Parent < frames.Count ? placed.Parent : PlayerState.World, Local: placed.Position,
            Emote: emote, EmoteSeconds: emoteSeconds, Sooted: s.Has(PlayerFlags.Sooted), Signal: signal, SignalSeconds: signalSeconds,
            Paint: world.PaintOf(id), Hat: world.HatOf(id), Scorch: Sim.Enemies.CarFire.HeatOn(s, world));
        if (emote != Emote.None)
            drawn = WithEmote(drawn, id, s, world, frames, crew);
        // Over a friend's shoulder (note 723): draped over it, where the one carrying them is and on their clock (note 812).
        if (s.Alive && s.Has(PlayerFlags.Carried) && others is not null && CarrierOf(s, others) is { } carrier)
        {
            int carrierLooks = crew?.FirstOrDefault(m => m.State.Equals(carrier)) is { State.Alive: true } found ? world.OutfitOf(found.Id) : drawn.Variant;
            var (carrierFeet, carrierYaw) = Eyes.World(carrier, frames);
            drawn = OnTheShoulder(drawn, carrierFeet, carrierYaw, carrierLooks);
        }
        // On a crane's hook (note 726): stood on it, holding on to its cable.
        if (s.Alive && s.Has(PlayerFlags.Hoisted) && world.Run?.CurrentSite?.CraneNear(feet) is { } crane)
            drawn = OnTheHook(drawn, crane.HookAt);
        // Under the handcar as it comes off the van or goes back on (note 818): both hands under its side as it's drawn.
        if (act == CrewPose.Heave && world.Train.Handcar is { } handcar && handcar.Id < frames.Count)
            drawn = Heaving(drawn, world.Train.HandcarLiftedTo() is { } to ? GreyboxScene.Lifted(frames[handcar.Id], to, handcar.Lift) : frames[handcar.Id],
                world.Train.Dynamics.Tuning.Handcar);
        // A hand-up (note 723): the hand held down from a moving car's door and the runner's raised to it, on one point (note 818).
        // A headset player's hands are their own.
        if (s.Alive && others is not null && world.Movement is { } movement && drawn.Hand == default && drawn.Other == default)
            drawn = HandsUp(drawn, s, world, frames, others, movement.Rescue);
        return drawn;
    }

    /// <summary>
    /// Note 818: whether <paramref name="s"/> is drawn lifting the handcar (note 729): on their feet within its lift reach
    /// (<see cref="World.CanLiftHandcar"/>) while it's part-way off the van or onto it. Who's holding Use to lift it is the host's
    /// alone, so whoever's by it while it moves is.
    /// </summary>
    public static bool Lifting(in PlayerState s, World world) =>
        world.Train.Handcar is { Lift: > 0 } && s.Alive && s.Surface != Surface.Air && world.CanLiftHandcar(s);

    /// <summary>Note 818: crew_clips' heave, posed by how high the hands are: squatted under it at the clip's start, stood holding it overhead at its end (m).</summary>
    public const double HeaveLow = 0.35, HeaveHigh = 2.15;

    /// <summary>The heave clip's length (s): crew_clips.py's 30 frames, from <see cref="HeaveLow"/> to <see cref="HeaveHigh"/>.</summary>
    public const double HeaveSeconds = 1;

    /// <summary>Note 818: how far into crew_clips' heave a lifter whose hands are <paramref name="height"/> m over their feet is.</summary>
    public static double HeavePhase(double height) => Math.Clamp((height - HeaveLow) / (HeaveHigh - HeaveLow), 0, 1) * HeaveSeconds;

    /// <summary>How far apart a lifter's hands are under the handcar (m), either side of where they stand along it.</summary>
    const double HeaveHands = 0.6;

    /// <summary>
    /// Note 818: a crewmate lifting the handcar (note 729), as they're drawn: turned to the trolley where it's drawn
    /// (<paramref name="car"/>: GreyboxScene.Lifted), both hands under the edge of its deck (its side, or the end they're off),
    /// either side of where they stand, and posed by how high that is over their feet (<see cref="HeavePhase"/>).
    /// </summary>
    public static Crewmate Heaving(Crewmate c, CarFrame car, HandcarTuning t)
    {
        var at = car.ToLocal(c.Feet);
        double halfW = t.Width / 2, halfL = t.Length / 2, under = t.DeckHeight * 0.5;
        Double3 a, b;
        if (Math.Abs(at.X) * halfL >= Math.Abs(at.Z) * halfW)
        {
            double side = at.X < 0 ? -1 : 1, along = Math.Clamp(at.Z, -(halfL - 0.4), halfL - 0.4);
            (a, b) = (new Double3(side * halfW, under, along - HeaveHands / 2), new Double3(side * halfW, under, along + HeaveHands / 2));
        }
        else
        {
            double end = at.Z < 0 ? -1 : 1, across = Math.Clamp(at.X, -(halfW - 0.35), halfW - 0.35);
            (a, b) = (new Double3(across - HeaveHands / 2, under, end * halfL), new Double3(across + HeaveHands / 2, under, end * halfL));
        }
        var (wa, wb) = (car.ToWorld(a), car.ToWorld(b));
        var mid = (wa + wb) * 0.5;
        var to = (mid - c.Feet) with { Y = 0 };
        double yaw = to.Length > 0.05 ? Math.Atan2(-to.X, -to.Z) : c.Yaw;
        return c with
        {
            Act = CrewPose.Heave,
            Yaw = yaw,
            Reach = (Facing(wa, c.Feet, yaw), Facing(wb, c.Feet, yaw)),
            Phase = HeavePhase(mid.Y - c.Feet.Y),
        };
    }

    /// <summary>A world point in a crewmate's own frame: from their feet, as they face (x right, y up, z behind).</summary>
    static Double3 Facing(Double3 world, Double3 feet, double yaw)
    {
        var d = world - feet;
        return new Double3(d.X * Math.Cos(yaw) - d.Z * Math.Sin(yaw), d.Y, d.X * Math.Sin(yaw) + d.Z * Math.Cos(yaw));
    }

    /// <summary>
    /// Note 818: where crew_clips' hand_up has the helper's reaching hand (m, from their feet as they face: right, up, ahead), and
    /// how high a runner's raised hand goes (m over their feet), toward the helper.
    /// </summary>
    static readonly Double3 HandDown = new(0.23, 0.75, 0.85);
    const double HandRaised = 1.95, HandRaisedToward = 0.25;

    /// <summary>
    /// Note 818: where a hand-up's two hands meet (world): half way from the helper's reaching hand, as hand_up has it, to
    /// the runner's raised toward them. Each reaches it as far as an arm goes.
    /// </summary>
    public static Double3 HandUpMeet(Double3 helperFeet, double helperYaw, Double3 runnerFeet)
    {
        var forward = new Double3(-Math.Sin(helperYaw), 0, -Math.Cos(helperYaw));
        var right = new Double3(Math.Cos(helperYaw), 0, -Math.Sin(helperYaw));
        var down = helperFeet + right * HandDown.X + Double3.Up * HandDown.Y + forward * HandDown.Z;
        var toHelper = (helperFeet - runnerFeet) with { Y = 0 };
        var raised = runnerFeet + Double3.Up * HandRaised + (toHelper.Length > 1e-3 ? toHelper.Normalized * HandRaisedToward : default);
        return Double3.Lerp(down, raised, 0.5);
    }

    /// <summary>
    /// Note 818: the hand-up's two hands (note 723), put on one point (<see cref="HandUpMeet"/>): the helper's, held down from a
    /// moving car's door, to the runner it's for (<see cref="Sim.Player.Rescue.HandUpTarget"/>); a runner's, raised to the
    /// helper whose hand-up it is. The other hand stays with the clip. Whether it's taken hold of is the host's alone.
    /// </summary>
    static Crewmate HandsUp(Crewmate c, in PlayerState s, World world, IReadOnlyList<CarFrame> frames, IReadOnlyList<PlayerState> others,
        RescueTuning rescue)
    {
        var mates = others.Select((o, i) => (i, o)).ToList();
        if (c.Act == CrewPose.HandUp)
            return Sim.Player.Rescue.HandUpTarget(s, -1, mates, world.Train, rescue) is { } runner
                ? HandsMeet(c, Eyes.World(runner.State, frames).Feet, helping: true) : c;
        if (c.Act is not null || s.Parent != PlayerState.World)
            return c;
        var me = s;
        foreach (var o in others)
        {
            if (!o.Alive || o.Parent == PlayerState.World || o.Equals(me)
                || Sim.Player.Rescue.HandUpTarget(o, -1, mates, world.Train, rescue) is not { } target || !target.State.Equals(me))
                continue;
            var (helperFeet, helperYaw) = Eyes.World(o, frames);
            return HandsMeet(c, helperFeet, helping: false, helperYaw);
        }
        return c;
    }

    /// <summary>
    /// Note 818: one half of a hand-up's hands on <see cref="HandUpMeet"/>: the helper's (<paramref name="helping"/>; the other
    /// is the runner, stood at <paramref name="other"/>), held a few centimetres over it, or the runner's (the other the helper,
    /// stood at <paramref name="other"/> facing <paramref name="otherYaw"/>), a few under it, their palms together.
    /// </summary>
    public static Crewmate HandsMeet(Crewmate c, Double3 other, bool helping, double otherYaw = 0)
    {
        var meet = helping ? HandUpMeet(c.Feet, c.Yaw, other) : HandUpMeet(other, otherYaw, c.Feet);
        return c with { Hand = Facing(meet + Double3.Up * (helping ? HandsApart : -HandsApart), c.Feet, c.Yaw) };
    }

    /// <summary>Half the gap between the two wrists of a hand-up's grip (m): a hand's thickness, palm to palm.</summary>
    const double HandsApart = 0.04;

    /// <summary>How far behind the cable a rider on the hook is drawn (m, note 726): stood on its far lip, the cable at their chest.</summary>
    const double CableAhead = 0.2;

    /// <summary>
    /// A crewmate stood on a crane's hook (note 726), as they're drawn: their feet on the hook's lip a step behind its cable (the
    /// sim has them on the hook itself), both hands up on the cable over it, one over the other.
    /// </summary>
    public static Crewmate OnTheHook(Crewmate c, Double3 hook)
    {
        var forward = new Double3(-DMath.Sin(c.Yaw), 0, -DMath.Cos(c.Yaw));
        var cable = hook with { Y = c.Feet.Y };
        var toCable = (cable - c.Feet) with { Y = 0 };
        // Two side by side keep where they stand; one alone is put behind the cable, facing it.
        var feet = toCable.Length < 0.05 ? cable - forward * CableAhead : c.Feet;
        Double3 Local(Double3 at)
        {
            var d = at - feet;
            return new Double3(d.X * Math.Cos(c.Yaw) - d.Z * Math.Sin(c.Yaw), d.Y, d.X * Math.Sin(c.Yaw) + d.Z * Math.Cos(c.Yaw));
        }
        return c with { Feet = feet, Act = CrewPose.OnHook, Reach = (Local(cable + Double3.Up * 1.75), Local(cable + Double3.Up * 1.4)) };
    }

    /// <summary>
    /// Note 813: in the air under a crane's hook, below it and within <see cref="OffTheHookReach"/> of its cable: off it (let go,
    /// or shaken off by the operator; who did it is the host's alone, so whoever's falling from under it is).
    /// </summary>
    static bool OffTheHook(in PlayerState s, World world)
    {
        var feet = PlayerMotor.WorldPosition(s, world.Train);
        return world.Run?.CurrentSite?.CraneNear(feet) is { } crane && feet.Y < crane.HookAt.Y + 0.5
            && ((crane.HookAt - feet) with { Y = 0 }).Length < OffTheHookReach;
    }

    /// <summary>How far across the ground from a crane's hook someone falling has come off it (m): the hook's spread and a step.</summary>
    const double OffTheHookReach = 1.5;

    /// <summary>
    /// Note 813: how far a crane's hook with someone on it is swung off plumb at <paramref name="time"/> (the scene's clock), and
    /// so they with it: a slow swing under their weight, a little each way across and along (m, horizontal). The same for the
    /// hook, its cable, its lamp and its riders, drawn on one clock.
    /// </summary>
    public static Double3 HookSway(double time) =>
        new(HookSwayReach * Math.Sin(time * 2 * Math.PI / HookSwayPeriod), 0, 0.6 * HookSwayReach * Math.Sin(time * 2 * Math.PI / HookSwayPeriod * 1.13 + 1.0));

    /// <summary>How far a loaded hook swings off plumb (m), and its swing's period (s: a few metres of cable's).</summary>
    const double HookSwayReach = 0.09, HookSwayPeriod = 3.4;

    /// <summary>The crewmate carrying <paramref name="s"/> (note 723): the nearest carrying one in their frame, under them.</summary>
    static PlayerState? CarrierOf(in PlayerState s, IReadOnlyList<PlayerState> others)
    {
        PlayerState? best = null;
        double bestD = 1.0;
        foreach (var o in others)
            if (o.Alive && o.Has(PlayerFlags.Carrying) && o.Parent == s.Parent && ((o.Position - s.Position) with { Y = 0 }).Length is var d && d < bestD)
                (best, bestD) = (o, d);
        return best;
    }

    /// <summary>
    /// A friend carried over the shoulder (note 723), as they're drawn (note 812): crew_clips' carried, posed in the carrier's
    /// own space on the carrier's keys, so drawn from the carrier's feet, facing their way, on their clock (the carrier's beat,
    /// <paramref name="carrierLooks"/>', less this one's own: <see cref="CreatureArt.BeatOffset"/>), the two as one.
    /// </summary>
    public static Crewmate OnTheShoulder(Crewmate c, Double3 carrierFeet, double carrierYaw, int carrierLooks) =>
        c with
        {
            Act = CrewPose.Carried,
            Yaw = carrierYaw,
            Feet = carrierFeet,
            Phase = CreatureArt.BeatOffset(carrierLooks) - CreatureArt.BeatOffset(c.Variant),
        };

    /// <summary>How long a shove's arms are out (s): the shover drawn shoving (note 717; crew_clips' shove, note 808), from the host's knock.</summary>
    public const double ShoveSeconds = 0.45;

    /// <summary>How long a throw off the shoulder is drawn (s): the thrower's drive up and the beat after (note 818; crew_clips' throw_off).</summary>
    public const double ThrowSeconds = 0.67;

    /// <summary>Whether <paramref name="id"/> shoved someone in the last <see cref="ShoveSeconds"/> (note 717, World.Horseplays).</summary>
    public static bool Shoving(int id, World world) => Knocked(id, world, KnockKind.Shove, ShoveSeconds);

    /// <summary>Whether <paramref name="id"/> threw a friend off the shoulder in the last <see cref="ThrowSeconds"/> (note 723's throw, note 818).</summary>
    public static bool Throwing(int id, World world) => Knocked(id, world, KnockKind.Throw, ThrowSeconds);

    static bool Knocked(int id, World world, KnockKind kind, double seconds)
    {
        foreach (var h in world.Horseplays)
            if (h.By == id && h.Kind == kind && ((double)world.Tick - h.Tick) * SimConstants.TickSeconds is >= 0 and var age && age < seconds)
                return true;
        return false;
    }

    /// <summary>
    /// The lamp signal <paramref name="id"/> is giving (note 720): their latest, while it lasts (player.json voices.lampSignals
    /// seconds), and how far into it they are. Only with the hand lamp in their fist (the caller's to know).
    /// </summary>
    public static (LampSignal Kind, double Seconds) SignalOf(int id, in PlayerState s, World world)
    {
        if (!s.Alive)
            return (LampSignal.None, 0);
        SignalEvent? latest = null;
        foreach (var e in world.Signals)
            if (e.By == id && (latest is null || e.Tick > latest.Value.Tick))
                latest = e;
        if (latest is not { } at)
            return (LampSignal.None, 0);
        double age = ((double)world.Tick - at.Tick) * SimConstants.TickSeconds;
        return age >= 0 && age < world.VoicesTuning.LampSignals.Seconds ? (at.Kind, age) : (LampSignal.None, 0);
    }

    /// <summary>Walking faster than this (m/s) ends an emote, as a step away from it does (note 298).</summary>
    const double EmoteStill = 0.5;

    /// <summary>
    /// The emote <paramref name="id"/> is at (note 298): their latest, while it lasts (player.json emotes) and while they
    /// stand, and how far into it they are. Anything they're doing (an act) comes first, so this is asked only when idle.
    /// </summary>
    public static (Emote Kind, double Seconds) EmoteOf(int id, in PlayerState s, World world)
    {
        if (!s.Alive)
            return (Emote.None, 0);
        if (LatestEmote(id, world) is not { } at)
            return (Emote.None, 0);
        // A step away ends it (note 298), but the conga's danced walking (note 714).
        if (at.Kind != Emote.Conga && s.Velocity.X * s.Velocity.X + s.Velocity.Z * s.Velocity.Z > EmoteStill * EmoteStill)
            return (Emote.None, 0);
        // (Note 722: a high five met lasts emotes.met from the meet, not its offer's length.)
        var (age, up) = world.EmoteTuning.Age(at, world.Tick);
        return up ? (at.Kind, age) : (Emote.None, 0);
    }

    /// <summary>The latest emote of <paramref name="id"/>'s the world holds, up or not.</summary>
    static EmoteEvent? LatestEmote(int id, World world)
    {
        EmoteEvent? latest = null;
        foreach (var e in world.Emotes)
            if (e.By == id && (latest is null || e.Tick > latest.Value.Tick))
                latest = e;
        return latest;
    }

    /// <summary>
    /// Note 722: a crewmate at an emote, with what its drawing needs beyond the kind (<see cref="EmoteDetail"/>): a high five
    /// or fist bump met, how long ago and how far apart the two stand; a dance, whether it's a dance-off and who leads
    /// (<see cref="EmoteRules.DanceOff"/>); the sit, put on the edge facing out from it (<see cref="SatOn"/>); the lean, at the
    /// doorway facing out of it, the head to where they look (<see cref="LeantOut"/>). An edge emote away from its edge (a
    /// snapshot's lag) isn't drawn.
    /// </summary>
    public static Crewmate WithEmote(Crewmate c, int id, in PlayerState s, World world, IReadOnlyList<CarFrame> frames, IReadOnlyList<(int Id, PlayerState State)>? crew)
    {
        var t = world.EmoteTuning;
        var train = world.Train;
        switch (c.Emote)
        {
            case Emote.HighFive or Emote.FistBump when LatestEmote(id, world) is { Met: true } met:
                {
                    double gap = t.Reach * 0.6, across = 0;
                    if (crew is not null)
                        foreach (var (other, o) in crew)
                            if (other == met.With && EmoteRules.Apart(s, o, train) is var d && double.IsFinite(d))
                            {
                                gap = d;
                                // How much of it is to their right (note 807), so both hands go to the one point between them.
                                var to = PlayerMotor.WorldPosition(o, train) - PlayerMotor.WorldPosition(s, train);
                                double yaw = PlayerMotor.WorldYaw(s, train);
                                across = to.X * Math.Cos(yaw) - to.Z * Math.Sin(yaw);
                            }
                    return c with
                    {
                        EmoteWith = new EmoteDetail(Met: true, SinceMeet: Math.Max(0, ((double)world.Tick - met.Meet) * SimConstants.TickSeconds), Gap: gap, Across: across)
                    };
                }
            case Emote.Dance when crew is not null && EmoteRules.DanceOff(id, world.Emotes, crew, train, t, world.Tick) is { } off:
                {
                    // Which turn and how far into it, the host's tick over the beat as EmoteRules.DanceOff has the lead (note 807).
                    long beat = Math.Max(1, (long)Math.Round(t.DanceOffBeat * SimConstants.TickRate));
                    return c with
                    {
                        EmoteWith = new EmoteDetail(Dancers: off.Count, Leading: off.Leader == id, Round: (int)(world.Tick / beat),
                            OnBeat: world.Tick % beat * SimConstants.TickSeconds)
                    };
                }
            case Emote.SitEdge when s.Parent >= 0 && s.Parent < frames.Count && EmoteRules.SitAt(s, train, t) is { } edge:
                return SatOn(c, s, edge, train, frames, t);
            case Emote.LeanOut when s.Parent >= 0 && s.Parent < frames.Count && EmoteRules.LeanAt(s, train, t) is { } door:
                return LeantOut(c, s, door, train, frames);
            case Emote.SitEdge or Emote.LeanOut:
                return c with { Emote = Emote.None, EmoteSeconds = 0 };
        }
        return c;
    }

    /// <summary>
    /// Sat on an edge (note 722), as they're drawn: out from where they stand to the last of the footing at their height (the
    /// roof's side, the sill's), <see cref="SitBack"/> in from it, facing out over it. The sim leaves them where they stood.
    /// </summary>
    public static Crewmate SatOn(Crewmate c, in PlayerState s, Edge edge, TrainOnLine train, IReadOnlyList<CarFrame> frames, EmoteTuning t)
    {
        var frame = frames[s.Parent];
        int side = edge.Side;
        var (x, _) = EmoteRules.EdgeX(s, train, side, t.EdgeReach + t.SillReach);
        // At the edge's height (a running board's a step under the cab's floor).
        var under = frame.Shape.TopAt(x, s.Position.Z, train.Vehicles[s.Parent], s.Position.Y + 0.05);
        var local = new Double3(x - side * SitBack, under?.Top ?? s.Position.Y, s.Position.Z);
        double yaw = side > 0 ? -Math.PI / 2 : Math.PI / 2;
        return c with { Feet = frame.ToWorld(local), Yaw = yaw + frame.Heading, Local = local };
    }

    /// <summary>How far in from the edge a sitter's feet (the model's origin, under the pelvis) are drawn (m): the seat on it, the backs of
    /// the knees just over it (crew_clips' sit_edge, SIT_BACK).</summary>
    const double SitBack = 0.3;

    /// <summary>
    /// Leant out of a doorway (note 722), as they're drawn: on its sill, facing along the car the way they look, the head turned
    /// to where they look (as far as a neck goes: CreatureArt clamps it). Note 807: along the sill to <see cref="JambAhead"/> short
    /// of the doorway's jamb ahead of them, where crew_clips' lean_out has its hand round it (<see cref="JambFrom"/>).
    /// </summary>
    public static Crewmate LeantOut(Crewmate c, in PlayerState s, Edge door, TrainOnLine train, IReadOnlyList<CarFrame> frames)
    {
        var frame = frames[s.Parent];
        var room = PlayerMotor.InCab(s, train) ? frame.Shape.Cab!.Value : frame.Shape.Interior!.Value;
        // Facing along the car the way they look (up the line or down it), so the lean out reads side on from the lineside.
        double body = -DMath.Cos(s.Yaw) <= 0 ? 0 : Math.PI;
        int outward = door.Side * (body == 0 ? 1 : -1);
        double turn = Math.IEEERemainder(s.Yaw - body, 2 * Math.PI);
        double z = JambFrom(s, door.Side, body == 0 ? -1 : 1, train) is { } jamb ? jamb : s.Position.Z;
        var local = s.Position with { X = door.Side > 0 ? room.Max.X - LeanIn : room.Min.X + LeanIn, Z = z };
        return c with { Feet = frame.ToWorld(local), Yaw = body + frame.Heading, Local = local, EmoteWith = new EmoteDetail(Turn: turn, Out: outward) };
    }

    /// <summary>
    /// Where along the car (its Z) a leaner stands to have the doorway's jamb <see cref="JambAhead"/> ahead of them, going
    /// <paramref name="ahead"/> (−1 the car's −Z): out through the doorway at the knee and the chest (as <see cref="EmoteRules.LeanAt"/>
    /// tests it) a step at a time until the car's in the way. Kept a step inside the jamb behind them in a narrow doorway;
    /// null with no jamb within <see cref="JambSearch"/>.
    /// </summary>
    public static double? JambFrom(in PlayerState s, int side, int ahead, TrainOnLine train)
    {
        var at = s.Position;
        int car = s.Parent;
        double front = Edge(ahead);
        if (double.IsNaN(front))
            return null;
        double back = Edge(-ahead);
        // Short of the jamb by JambAhead, or as far back as the doorway goes (less a hand's breadth) when it's narrower.
        double from = Math.Max(front - JambAhead, double.IsNaN(back) ? front - JambAhead : -back + 0.1);
        return at.Z + ahead * Math.Min(from, front);

        double Edge(int way)
        {
            var frame = train.Frames[car];
            for (double d = JambStep; d <= JambSearch; d += JambStep)
            {
                double z = at.Z + way * d;
                foreach (double up in (ReadOnlySpan<double>)[0.5, 1.3])
                {
                    var from = frame.ToWorld(new Double3(at.X, at.Y + up, z));
                    var to = frame.ToWorld(new Double3(at.X + side * 0.8, at.Y + up, z));
                    if (!Sim.Player.Rescue.Clear(train, car, from, to))
                        return d - JambStep / 2;
                }
            }
            return double.NaN;
        }
    }

    /// <summary>How far ahead of a leaner's feet the jamb's face is (m): crew_clips' lean_out, JAMB_AHEAD; and how far along the
    /// sill it's looked for (m), in steps of (m).</summary>
    public const double JambAhead = 0.45;
    const double JambSearch = 2.5, JambStep = 0.05;

    /// <summary>How far past the doorway's inner face the leaner stands (m): out on the sill, in the door's slot, toes at its edge.</summary>
    const double LeanIn = -0.06;

    /// <summary>How near a waking threat has to be for a crewmate's run to be a hurried one (m).</summary>
    public const double StressRange = 30;

    /// <summary>Anything past its dormant phase within <see cref="StressRange"/> of <paramref name="at"/> (GDD §31: "hurried under stress").</summary>
    static bool Stressed(World world, Double3 at) =>
        world.ActiveEnemies.Any(e => !e.Gone && e.Phase is not (SpinePhase.Dormant or SpinePhase.Gone) && !e.Hazard
            && (e.WorldPosition(world.Train) - at).Length < StressRange);

    /// <summary>Who has hold of them (App. A.1 GRAB), as the pose that says so from across the car.</summary>
    static CrewPose HeldBy(int id, World world) => HeldPose(world.ActiveEnemies.FirstOrDefault(e => e.Holding == id)?.Kind);

    /// <summary>The pose someone held by a <paramref name="kind"/> is drawn in (App. A.1 GRAB).</summary>
    public static CrewPose HeldPose(EnemyKind? kind) =>
        kind switch
        {
            EnemyKind.Dragger => CrewPose.HeldHang,
            EnemyKind.CarHugger => CrewPose.HeldMouth,
            EnemyKind.Whistler => CrewPose.HeldCarried,
            EnemyKind.TippyToesie => CrewPose.HeldCover,
            EnemyKind.Ribbit => CrewPose.HeldFrozen,
            // (Ground into the peat under the Moose's rack: on their back, pushing at it. Note 311. Flat on the roof under the
            // Gannet's foot, the same: note 340.)
            EnemyKind.SootChildren or EnemyKind.Moose or EnemyKind.Gannet => CrewPose.HeldPinned,
            EnemyKind.Choir => CrewPose.HeldSeized,
            EnemyKind.Passenger => CrewPose.HeldDragged,
            _ => CrewPose.Held,
        };

    /// <summary>The seconds of crew_clips' reload clip that match how far the gun's reload is: 1.5 s a step (its beats), the
    /// steps done plus this one's progress.</summary>
    static double ReloadPhase(in PlayerState s, World world)
    {
        if (world.Combat is not { } combat || Guns.MannedGun(s, world.Train, combat.Guns) is not { } g)
            return 0;
        var gun = world.Train.Vehicles[g].Gun;
        int steps = combat.Guns.ReloadSteps;
        double done = steps - gun.ReloadNeeded + Math.Clamp(gun.ReloadProgress / combat.Guns.ReloadStepSeconds, 0, 1);
        return Math.Clamp(done, 0, steps) * ReloadBeatSeconds;
    }

    /// <summary>One reload step's length in crew_clips.py's reload clip (45 frames at 30 fps).</summary>
    const double ReloadBeatSeconds = 1.5;

    /// <summary>
    /// Who they came back as (GDD App. D.8: "a freed player becomes this character from then on"): the occupant of a freed
    /// Holdout, by its kind (a shelter's wildlander, a prison car's or a lockup's prisoner); the last one freed, if more.
    /// Every machine mirrors the Holdouts, so every machine draws the same.
    /// </summary>
    public static Survivor SurvivorOf(int id, World world)
    {
        // Tonight's freeing, else the look carried from an earlier night (Sim.Run.Identity; note 181).
        return Sim.Run.Identity.Of(world, id) switch
        {
            Sim.Run.Identity.Prisoner => Survivor.Prisoner,
            Sim.Run.Identity.Wildlander => Survivor.Wildlander,
            _ => Survivor.None,
        };
    }

    /// <summary>
    /// A driver's hands where the levers are (GDD §12): one on the regulator, at its notch, and one on the brake valve; or,
    /// whistling, one up on the cord's handle (<see cref="TrainKit.WhistleCord"/>, hauled down). From the feet, facing frame.
    /// </summary>
    static (Double3, Double3)? AtTheControls(World world, IReadOnlyList<CarFrame> frames, Double3 feet, double yaw, bool whistling) =>
        AtTheControls(world.Controls, frames, feet, yaw, whistling);

    /// <summary>
    /// A pumper's hands on the handcar's handle (note 729): its grip bar at their end, each hand to its side, where the swing has
    /// it now; in their frame (from the feet, as they face), as the controls' are.
    /// </summary>
    public static (Double3, Double3)? AtTheHandle(TrainOnLine train, IReadOnlyList<CarFrame> frames, Double3 feet, double yaw, in PlayerState s)
    {
        if (train.Handcar is not { } h || h.Id >= frames.Count)
            return null;
        return AtTheHandle(train.Dynamics.Tuning.Handcar, frames[h.Id], h.Phase, Sim.Train.Handcar.EndAt(s.Position.Z), feet, yaw);
    }

    /// <summary>
    /// Note 814: how far into crew_clips' pump a pumper at <paramref name="s"/>'s end of the handcar's handle is, by its swing
    /// (<see cref="Sim.Train.Handcar.EndHeight"/>): the clip's one swing over <see cref="PumpSeconds"/> starts with their own
    /// end at the top, which is the front end's at a swing of 0 and the back end's half a swing on.
    /// </summary>
    public static double PumpPhase(in PlayerState s, TrainOnLine train) =>
        train.Handcar is { } h ? PumpPhase(h.Phase, Sim.Train.Handcar.EndAt(s.Position.Z)) : 0;

    /// <summary>The same, for a swing and an end as given (a staged pumper).</summary>
    public static double PumpPhase(double swing, int end)
    {
        double turn = (swing + (end > 0 ? Math.PI : 0)) / (2 * Math.PI);
        return (turn - Math.Floor(turn)) * PumpSeconds;
    }

    /// <summary>The pump clip's one swing of the handle (s; crew_clips.py's 60 frames at 30 fps).</summary>
    public const double PumpSeconds = 2;

    /// <summary>The same, for a handle as given (a staged pumper: Staging.Pumpers).</summary>
    public static (Double3, Double3) AtTheHandle(HandcarTuning t, CarFrame car, double phase, int end, Double3 feet, double yaw)
    {
        Double3 Local(Double3 inCar)
        {
            var d = car.ToWorld(inCar) - feet;
            return new Double3(d.X * Math.Cos(yaw) - d.Z * Math.Sin(yaw), d.Y, d.X * Math.Sin(yaw) + d.Z * Math.Cos(yaw));
        }
        // Facing the handle down the car, their left hand's on the grip's far side from the car's left.
        return (Local(GreyboxScene.HandleGrip(t, phase, end, -end)), Local(GreyboxScene.HandleGrip(t, phase, end, end)));
    }

    /// <summary>The same, for the controls as given (a staged driver: Staging.Driver).</summary>
    public static (Double3, Double3)? AtTheControls(TrainControls c, IReadOnlyList<CarFrame> frames, Double3 feet, double yaw, bool whistling)
    {
        if (frames.Count == 0 || frames[0].Shape.Levers is not { } levers)
            return null;
        Double3 Local(Double3 inCab)
        {
            var d = frames[0].ToWorld(inCab) - feet;
            return new Double3(d.X * Math.Cos(yaw) - d.Z * Math.Sin(yaw), d.Y, d.X * Math.Sin(yaw) + d.Z * Math.Cos(yaw));
        }
        var brake = Local(levers.BrakeAt(c.Brake));
        var other = whistling ? Local(TrainKit.WhistleCordHandle(frames[0].Shape, pulled: true)) : Local(levers.RegulatorAt(c.Throttle));
        return (other, brake);
    }

    /// <summary>The real whistle is blowing, a crewmate's (GDD §12): the Whistler's blast is from nobody's hand on the cord (App. A.4).</summary>
    public static bool CrewWhistling(World world) =>
        world.WhistleSeconds > 0 && !world.ActiveEnemies.OfType<Sim.Enemies.Whistler>().Any(w => w.Phase == Sim.Enemies.SpinePhase.Telegraph && w.Extra > 0.5);

    /// <summary>How near the regulator (across the floor, m) a crewmate in the cab is at the controls.</summary>
    const double AtControls = 0.9;

    /// <summary><paramref name="s"/> moved onto <paramref name="gun"/>'s seat (their feet on the roof under it), facing the gun's way.</summary>
    public static PlayerState Seated(in PlayerState s, GunMount gun)
    {
        var seat = gun.Position - gun.Facing * TrainKit.CannonSeat.Z;
        return s with
        {
            Position = seat with { Y = gun.Position.Y + TrainKit.CannonSeat.Y - GunnerPan },
            Yaw = Math.Atan2(-gun.Facing.X, -gun.Facing.Z),
        };
    }

    /// <summary>
    /// <paramref name="s"/> sat in a Muskoka chair (note 698): the seated clip's pan let down to the chair's slung seat
    /// (<see cref="ChairPan"/> over its feet), facing down the line as the chair does.
    /// </summary>
    public static PlayerState Lounging(in PlayerState s, TrainOnLine train)
    {
        double pan = Bbq.LoungingIn(s, train)?.Kind switch
        {
            LoungeKind.Hammock => HammockPan,
            LoungeKind.HotTub => TubPan,
            _ => ChairPan,
        };
        return s with { Position = s.Position with { Y = s.Position.Y + pan - GunnerPan } };
    }

    /// <summary>
    /// <paramref name="s"/> in the guard van's gun (note 746): crouched in the barrel's mouth, up at the muzzle's height over the
    /// roof the sim holds their feet on.
    /// </summary>
    public static PlayerState InTheCannon(in PlayerState s, TrainOnLine train, CannonballTuning t)
    {
        if (Guns.Mount(train, s.Parent) is not { } mount)
            return s;
        var muzzle = HumanCannonball.Muzzle(train, s.Parent, t);
        return s with { Position = new Ballast.Double3(muzzle.X, muzzle.Y - InCannonDrop, muzzle.Z) };
    }

    /// <summary>How far under the muzzle's middle a crouched crewmate's feet are, so their head and shoulders show (m).</summary>
    const double InCannonDrop = 0.75;

    /// <summary>How high the hammock's sag and the hot tub's bench are over the floor or the roof under them (m; note 744).</summary>
    public const double HammockPan = 0.55, TubPan = 0.25;

    /// <summary>How high a Muskoka chair's seat is over the deck under it (m): low and slung, TrainKit.MuskokaChair's.</summary>
    public const double ChairPan = 0.34;

    /// <summary>How close a crewmate who's held has to be for someone to be hauling at them (m).</summary>
    const double HaulReach = 1.6;

    /// <summary>One of <paramref name="others"/>, alive and held, within <see cref="HaulReach"/> of <paramref name="s"/> in its frame, or null.</summary>
    static PlayerState? HeldNear(in PlayerState s, IReadOnlyList<PlayerState> others)
    {
        foreach (var o in others)
            if (o.Alive && o.Has(PlayerFlags.Held) && o.Parent == s.Parent && (o.Position - s.Position).Length is > 0.05 and < HaulReach)
                return o;
        return null;
    }

    /// <summary>
    /// How a friend held by something is hauled at, by what has them (note 378; App. A.1's rescue): out of the Car Hugger's
    /// mouth, heaved back hand over hand; the Tippy Toesie's fingers prised off their face; lifted away by the Whistler or
    /// the Choir, hauled back down by the legs; over an edge below (a Dragger's), hauled up; otherwise down on a knee at
    /// their collar. What has them is the holding creature nearest them (every machine mirrors the enemies).
    /// </summary>
    static CrewPose Rescue(in PlayerState friend, in PlayerState s, World world)
    {
        var train = world.Train;
        var at = PlayerMotor.WorldPosition(friend, train);
        Enemy? holder = null;
        double best = double.MaxValue;
        foreach (var e in world.ActiveEnemies)
            if (e.Holding >= 0 && (e.WorldPosition(train) - at).Length is var d && d < best)
                (holder, best) = (e, d);
        return RescueOf(HeldPose(holder?.Kind), below: friend.Position.Y < s.Position.Y - 0.5);
    }

    /// <summary>The rescuer's act for a friend held in <paramref name="held"/> (<see cref="HeldPose"/>), and whether they're below the edge.</summary>
    public static CrewPose RescueOf(CrewPose held, bool below) => held switch
    {
        CrewPose.HeldMouth => CrewPose.PullMouth,
        CrewPose.HeldCover => CrewPose.PryOff,
        CrewPose.HeldCarried or CrewPose.HeldSeized => CrewPose.HaulDown,
        _ => below ? CrewPose.HaulUp : CrewPose.Haul,
    };

    /// <summary>
    /// Stood at the door of a Holdout that's being breached (App. D.7): the act by its kind: a barricade pried, a lock picked
    /// with the repair kit (Holdout.Quiet), or smashed.
    /// </summary>
    static CrewPose? Breaching(in PlayerState s, World world)
    {
        if (world.Holdouts is not { } holdouts || s.Parent != PlayerState.World)
            return null;
        foreach (var h in holdouts.All)
            if (h.State == Sim.Run.HoldoutState.Breaching && ((h.Door - s.Position) with { Y = 0 }).Length <= holdouts.Tuning.BreachReach)
                return h.Layout.Kind == Sim.Stops.HoldoutKind.Shelter ? CrewPose.Pry : h.Quiet ? CrewPose.Pick : CrewPose.Smash;
        return null;
    }

    /// <summary>How near its mount (across the floor, m) an extinguisher in hand is being lifted off it or hung back.</summary>
    const double MountReach = 0.7;

    static bool AtMount(Body extinguisher, in PlayerState s, TrainOnLine train) =>
        s.Parent == extinguisher.Home && s.Parent > 0 && s.Parent < train.Frames.Count && train.Frames[s.Parent].Shape.Interior is { } room
        && ((World.ExtinguisherMount(train.Frames[s.Parent].Shape, room) - s.Position) with { Y = 0 }).Length < MountReach + 0.6;

    /// <summary>The seat pan's height over the gunner's feet in crew_clips.py's gunner clip (m).</summary>
    public const double GunnerPan = 0.48;

    /// <summary>
    /// What <paramref name="s"/> (player <paramref name="id"/>) is doing, or null for nothing but standing or walking.
    /// <paramref name="others"/>: the rest of the crew as they stand (a friend held close by is being hauled at).
    /// </summary>
    public static CrewPose? Of(in PlayerState s, int id, World world, IReadOnlyList<PlayerState>? others = null)
    {
        if (!s.Alive)
            return null;
        var train = world.Train;
        if (s.Has(PlayerFlags.Held))
            return HeldBy(id, world);
        // In the guard van's gun (note 746): crouched down in the barrel, the head and shoulders out of the muzzle.
        if (s.Has(PlayerFlags.InCannon))
            return CrewPose.Crouch;
        // Over a friend's shoulder (note 723; Crewmate lays them across it).
        if (s.Has(PlayerFlags.Carried))
            return CrewPose.HeldPinned;
        // Stood on a crane's hook (note 726), both hands up on its cable (OnTheHook; note 813's clip).
        if (s.Has(PlayerFlags.Hoisted))
            return CrewPose.OnHook;
        // Knocked by a crewmate (note 717): rocked back, arms out, while the stagger lasts; and the shover's arms out at them.
        if (s.Stagger > 0)
            return CrewPose.Stagger;
        if (Shoving(id, world))
            return CrewPose.Shove;
        // A friend thrown off the shoulder (note 723): the drive up pushing them off (note 818).
        if (Throwing(id, world))
            return CrewPose.ThrowOff;
        // Carrying a friend over the shoulder (note 723), both arms round their legs (note 812).
        if (s.Has(PlayerFlags.Carrying))
            return CrewPose.Fireman;
        // At a Holdout's door while it's breached (App. D.7): smashing its lock, prying its barricade, or picking the lock
        // with the repair kit. (Who's breaching isn't sent; whoever stands at the door is at it.)
        if (Breaching(s, world) is { } breach)
            return breach;
        // Freed, still where they came back inside it (App. D.8): up off the floor.
        var feet = s.Position;
        if (world.Holdouts is { } holdouts && s.Parent == PlayerState.World
            && holdouts.All.Any(h => h.State == Sim.Run.HoldoutState.Freed && h.Occupant == id && ((h.Inside - feet) with { Y = 0 }).Length < 0.3))
            return CrewPose.GetUp;
        if (s.Surface == Surface.Ladder)
            return s.Has(PlayerFlags.SoloCarry) ? CrewPose.ClimbCarry : CrewPose.Climb;
        // In the air: going up off a jump, the leap (note 375; SceneArt holds it over the top); coming down hard, falling.
        // (Note 813: off a crane's hook, let go or shaken off, or off their feet by a shove or a throw, flailing all the way.)
        if (s.Surface == Surface.Air)
            return s.Has(PlayerFlags.Shoved) || OffTheHook(s, world) ? CrewPose.Flail
                : s.Velocity.Y < -Falling ? CrewPose.Fall : s.Velocity.Y > Rising ? CrewPose.Jump : null;
        if (s.Has(PlayerFlags.Pushing))
            return CrewPose.Push;
        if (s.Has(PlayerFlags.Operating))
            return CrewPose.Lever;
        // At the handcar's handle (note 729): both hands on its grip bar, going up and down with it (AtTheHandle), pumping it
        // (note 814: by its swing, PumpPhase).
        if (s.Has(PlayerFlags.Pumping))
            return CrewPose.Pump;
        // Sat back in a Muskoka chair on the BBQ deck (note 698), a beer in hand or not: the gunner's seated clip, in the chair.
        if (s.Has(PlayerFlags.Lounging))
            return CrewPose.Gunner;
        if (world.Bodies.CarriedBy(id) is { } carried)
            return carried.Kind switch
            {
                // Over the shoulder (App. C.4), the arm round its legs: where the sim holds it (Bodies.Shoulder).
                BodyKind.Ragdoll => CrewPose.Shoulder,
                // The child in the arms, clinging to them (App. C.4; Bodies.ChildAt).
                BodyKind.Child => CrewPose.Cradle,
                // The hand lamp out low in one hand; the extinguisher on the hip, aimed (App. C.5).
                BodyKind.Lamp => CrewPose.Lantern,
                // Just off its bracket (or about to go back on it), by the mount, it's being lifted.
                BodyKind.Extinguisher when AtMount(carried, s, train) => CrewPose.TakeDown,
                BodyKind.Extinguisher => CrewPose.Extinguish,
                // The repair kit at work on a burst boiler (T109): down at the firebox mending it.
                BodyKind.RepairKit when s.ActionProgress > 0 && CrewActions.AtTheRupture(s, train) => CrewPose.Mend,
                // Otherwise the kit by its handle at the side, a toolbox (note 513), not held out in front like a crate.
                BodyKind.RepairKit => CrewPose.Toolbox,
                _ => CrewPose.Carry,
            };
        // Lifting the handcar off the guard van or onto it (note 729), hands free: heaving it (note 818; Heaving draws it).
        if (Lifting(s, world))
            return CrewPose.Heave;
        // The wrench at work on a break (note 301): down at it, mending.
        if (s.ActionProgress > 0 && Repairs.WrenchInHand(s) && Repairs.ByWrench(train) && Repairs.At(s, train) != BreakKind.None)
            return CrewPose.Mend;
        if (s.Has(PlayerFlags.Shovelful))
            return CrewPose.Shovel;
        // Crouched on the key (note 664): down on crew.py's crouch clip, still or moving (there's no crouched walk yet). A
        // headset rider's body crouches with their own head (HeadsetBody) instead.
        if (s.Has(PlayerFlags.Crouched) && s.Head <= 0)
            return CrewPose.Crouch;
        if (world.Combat is { } combat && Guns.MannedGun(s, train, combat.Guns) is { } manned)
            return train.Vehicles[manned].Gun.ReloadNeeded > 0 ? CrewPose.Reload : CrewPose.Gunner;
        // On the ground at the line's own levers: a switch stand's (spec C.6), a coaling chute's or a spout's (spec D.3).
        if (s.Parent == PlayerState.World)
        {
            if (world.Switches?.InReach(s, train) is not null)
                return CrewPose.Throw;
            if (world.Run is { } run && run.LeverInReach(s, train))
                return CrewPose.Chute;
        }
        if (world.Run is { } r && (r.SpoutLeverInReach(s, train) is not null || r.LiftLeverInReach(s, train) is not null))
            return CrewPose.Spout;
        if (s.Parent == PlayerState.World || s.Parent >= train.Frames.Count)
            return null;
        // Beside a friend something's got hold of (App. A.1's rescue), down hauling them free; one hanging over the edge
        // below (a Dragger's, App. A.4), stood leaning back hauling them up.
        if (others is not null && HeldNear(s, others) is { } friend)
            return Rescue(friend, s, world);
        // In a moving car's door, looking at a friend running beside it in a hand-up's reach (note 723): hauling them up.
        if (others is not null && world.Movement is { } movement
            && Sim.Player.Rescue.HandUpTarget(s, -1, others.Select((o, i) => (i, o)), train, movement.Rescue) is not null)
            return CrewPose.HandUp;
        var near = CrewActions.Nearest(s, train);
        // Something timed under way (CrewActions: the progress resets the moment it stops).
        if (s.ActionProgress > 0)
            return s.Surface == Surface.Coupler && near != InteractableKind.Door ? CrewPose.Uncouple : near switch
            {
                InteractableKind.Firebox or InteractableKind.Coal => CrewPose.Shovel,
                InteractableKind.Door => CrewPose.Door,
                InteractableKind.Handbrake => CrewPose.Handbrake,
                InteractableKind.Hatch => CrewPose.Hatch,
                _ => (CrewPose?)null,
            };
        // Stood on the coupling plate between two cars, balancing over the gap (GDD §32).
        if (s.Surface == Surface.Coupler)
            return CrewPose.Gap;
        // The held valves keep no progress: the boiler venting, the rail being sanded, by whoever's at it.
        if (s.Parent == 0 && s.Surface == Surface.Deck)
        {
            var valve = near switch
            {
                InteractableKind.Vent when train.Boiler.Venting => CrewPose.Vent,
                InteractableKind.Sandbox when train.Sanding => CrewPose.Lever,
                _ => (CrewPose?)null,
            };
            if (valve is not null)
                return valve;
            // At the controls in the cab (GDD §12): hands on the regulator and the brake, or up on the whistle cord while it's
            // blowing. The Whistler's blast has no hand on the cord (App. A.4): that's its tell.
            if (PlayerMotor.InCab(s, train) && train.Frames[0].Shape.Levers is { } levers
                && ((levers.Regulator - s.Position) with { Y = 0 }).Length < AtControls)
                return CrewWhistling(world) ? CrewPose.Whistle : CrewPose.Drive;
        }
        return null;
    }
}
