using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Bots;

/// <summary>
/// The crew bots' answers to the six creatures of 8 Oct (ARCHITECTURE §8 note NNN; the director: "have the bots handle the six
/// new creatures too"), each the counter-play its design page gives (docs/design/creatures/), read off what every client has
/// of them (their modes, where they are, the cars' wound, seized and knotted state). What takes a bot off the train only does
/// so with the train standing under it (<see cref="CanGoDown"/>); done, it climbs back aboard as from any stop. The driver's
/// part (stopping for a Hotbox, a Knotter or a wreck, and coupling up after) is <see cref="ConductorBot"/>'s. Numbers are
/// enemies.json <c>crewBots</c>.
/// </summary>
public static partial class Heed
{
    /// <summary>m/s: a car slower than this is standing (a stop's hands get down off nothing moving: <see cref="StopHand"/>).</summary>
    const double StandingStill = 0.05;

    /// <summary>On the ground already, or on a car of a rake that's standing: free to get down to something.</summary>
    static bool CanGoDown(in PlayerState self, TrainOnLine train) =>
        self.Parent == PlayerState.World
        || self.Parent >= 0 && self.Parent < train.Frames.Count && Math.Abs(train.RakeOf(self.Parent).Velocity) < StandingStill;

    static bool Free(in PlayerState self) => self.Alive && !self.Has(PlayerFlags.Held);

    /// <summary>Up out of a gun's seat first (T112: Jump, seated, is getting up), as for a Holdout.</summary>
    static PlayerIntent Up(in PlayerState self, PlayerIntent intent) =>
        self.Has(PlayerFlags.Seated) ? intent with { Buttons = intent.Buttons | PlayerButtons.Jump } : intent;

    static double Flat(Double3 v) => (v with { Y = 0 }).Length;

    /// <summary>
    /// Down off the standing train and on foot toward a point on the ground; within <see cref="RunAt"/> of it, straight at it
    /// at a run (what's being clubbed may be backing off: the Mourners at their <c>scatter</c>, under a run) and a swing.
    /// </summary>
    static PlayerIntent? Club(in PlayerState self, World world, StopHand hand, Double3 at, double reach)
    {
        if (self.Parent == PlayerState.World && Flat(at - PlayerMotor.WorldPosition(self, world.Train)) <= RunAt)
            return Strike(self, world.Train, at, reach);
        return hand.Afoot(self, world, at, RunAt - 1) ?? Strike(self, world.Train, at, reach);
    }

    /// <summary>m: nearer than this on the ground, a bot runs straight at what it's clubbing (the way round is for further).</summary>
    const double RunAt = 8;

    /// <summary>Turned to a point, standing (a press of Use, a hold, a look).</summary>
    static PlayerIntent Facing(in PlayerState self, TrainOnLine train, Double3 at)
    {
        var to = at - PlayerMotor.WorldPosition(self, train);
        double yaw = DMath.Atan2(-to.X, -to.Z);
        return new PlayerIntent { LookYaw = (float)Math.IEEERemainder(yaw - PlayerMotor.WorldYaw(self, train), 2 * Math.PI) };
    }

    /// <summary>A Mourner is frail (health 1, <c>meleeRadius</c> 0.9, a blow reaching 3.1 m): this near it on the flat, a swing (m).</summary>
    const double MournerReach = 2.4;

    /// <summary>
    /// The Mourners (note 362: "stand over your dead, or carry them home"). A bot that can get down, within
    /// <c>crewBots.mournersWithin</c> of a crewmate's body they're about, goes to it: over it (inside their <c>dropWithin</c>)
    /// they drop it and start back, and it clubs the nearest of them within <c>mournersLeash</c> of the body (one blow kills
    /// one and scatters the rest), and stands over it while none are near. Not carried home (see note NNN).
    /// </summary>
    public static PlayerIntent Mourners(PlayerIntent intent, in PlayerState self, World world, int selfId, StopHand? hand)
    {
        if (hand is null || !Free(self) || world.Enemies is not { } et || !CanGoDown(self, world.Train))
            return intent;
        var train = world.Train;
        var b = et.CrewBots;
        var me = PlayerMotor.WorldPosition(self, train);
        Body? body = null;
        var bodyAt = default(Double3);
        double best = b.MournersWithin;
        foreach (var m in world.ActiveEnemies.OfType<Mourner>().OrderBy(m => m.Id))
        {
            if (m.Gone || m.Mode == MournerMode.Leave || body?.Id == m.BodyId)
                continue;
            // Carried, they only follow it: that's the carrier's.
            if (world.Bodies.All.FirstOrDefault(x => x.Id == m.BodyId) is not { Carrier: < 0 } lying)
                continue;
            var at = Bodies.WorldCentre(lying, train);
            if (Flat(at - me) < best)
                (body, bodyAt, best) = (lying, at, Flat(at - me));
        }
        if (body is null)
            return intent;
        var near = world.ActiveEnemies.OfType<Mourner>().Where(m => !m.Gone && m.BodyId == body.Id && m.Mode != MournerMode.Leave)
            .Select(m => (m, At: m.WorldPosition(train))).Where(x => Flat(x.At - bodyAt) <= b.MournersLeash)
            .OrderBy(x => x.m.Mode is MournerMode.Drag or MournerMode.Creep ? 0 : 1).ThenBy(x => Flat(x.At - me)).ThenBy(x => x.m.Id).FirstOrDefault();
        var go = near.m is not null ? Club(self, world, hand, near.At, MournerReach)
            : hand.Afoot(self, world, bodyAt, et.Mourners.DropWithin - 1) ?? new PlayerIntent();
        return go is { } going ? Up(self, going) : intent;
    }

    /// <summary>Tower Jaw is a bear's size (<c>meleeRadius</c> 1.6): this near on the flat, a swing (m).</summary>
    const double JawReach = 1.6;

    /// <summary>
    /// Tower Jaw (note 363: "hear the chewing, find the tower, get it off before it falls"). A bot that can get down, within
    /// <c>crewBots.towerJawWithin</c> of it at its post (gnawing, rearing, lunging), goes and clubs it: four blows inside its
    /// window drive it off (it bites, never kills). Gnawed past <c>towerJawClearFrom</c>, it's left to fall, and a bot keeps
    /// <c>crushRadius</c> + <c>towerJawClearBy</c> off where it falls, as from a groaning wreck heap (note 187). Down, a wreck
    /// across the line within <c>wreckWithin</c> of a standing train is cleared: Use held beside it, every crewmate's seconds
    /// counted, while the driver stands short of it (<see cref="ConductorBot"/>).
    /// </summary>
    public static PlayerIntent TowerJaw(PlayerIntent intent, in PlayerState self, World world, int selfId, StopHand? hand)
    {
        if (hand is null || !Free(self) || world.Enemies is not { } et || !CanGoDown(self, world.Train))
            return intent;
        var train = world.Train;
        var t = et.TowerJaw;
        var b = et.CrewBots;
        var me = PlayerMotor.WorldPosition(self, train);
        foreach (var jaw in world.ActiveEnemies.OfType<TowerJaw>().Where(j => !j.Gone).OrderBy(j => j.Id))
        {
            var at = jaw.WorldPosition(train);
            if (jaw.Mode == TowerJawMode.Wreck)
            {
                if (!WreckOnTheLine(train, jaw, t) || Flat(at - me) > b.WreckWithin)
                    continue;
                // Beside it (in its reach, not on top of it), Use held: the crew's seconds clear it.
                var clearing = hand.Afoot(self, world, at, t.ClearReach * 0.6);
                return Up(self, clearing ?? Facing(self, train, at) with { Buttons = PlayerButtons.Use });
            }
            if (jaw.Mode == TowerJawMode.Away)
                continue;
            if (jaw.Gnawed >= b.TowerJawClearFrom)
            {
                // Coming down: clear of where it falls (the line under its structure, the support being beside it).
                var falls = FallsNear(train, jaw);
                double clear = t.CrushRadius + b.TowerJawClearBy;
                var away = (me - falls) with { Y = 0 };
                if (away.Length >= clear)
                    continue;
                var off = falls + (away.Length > 0.01 ? away.Normalized : train.Frames[0].DirToWorld(new Double3(1, 0, 0))) * (clear + 1);
                return Up(self, hand.Afoot(self, world, off, 0.5) ?? new PlayerIntent());
            }
            if (Flat(at - me) > b.TowerJawWithin)
                continue;
            return Club(self, world, hand, at, JawReach) is { } clubbing ? Up(self, clubbing) : intent;
        }
        return intent;
    }

    /// <summary>
    /// A Tower Jaw wreck that closes the line (the coaling tower's, across the main line at the spout; not the crane gantry's,
    /// which lies on its facility): within its <c>wreckHalf</c> of the main line where it lies. Read off what every client has
    /// of it (where it is, its line distance), as the host's <see cref="Enemies.TowerJaw.Blocks"/> isn't sent.
    /// </summary>
    public static bool WreckOnTheLine(TrainOnLine train, TowerJaw jaw, TowerJawTuning t)
    {
        if (jaw.Mode != TowerJawMode.Wreck || jaw.LineDistance < 0 || jaw.LineDistance > train.Line.PathLength(RailLine.MainPath))
            return false;
        var on = train.Line.Sample(RailLine.MainPath, jaw.LineDistance);
        return Flat(jaw.WorldPosition(train) - on.Position) <= t.WreckHalf;
    }

    /// <summary>
    /// Where its structure comes down, as a client can tell: the line nearest it (the coaling tower straddles the line and
    /// falls across it at the spout, its leg beside it; the gantry's fall, over its loading track, is near enough so).
    /// </summary>
    static Double3 FallsNear(TrainOnLine train, TowerJaw jaw)
    {
        var at = jaw.WorldPosition(train);
        double hint = jaw.LineDistance;
        var (path, d) = train.Line.Nearest(at, ref hint);
        return train.Line.Sample(path, d).Position;
    }

    /// <summary>The Freight Beetle is broad (<c>meleeRadius</c> 1.4): this near on the flat, a swing (m).</summary>
    const double BeetleReach = 1.4;

    /// <summary>
    /// The Freight Beetle (note 366: "it pushes away from whoever's nearest. Stand where you want it not to go"). It never
    /// harms anyone, and three blows inside its window drive it off its load. A bot on foot (at a stop) within
    /// <c>crewBots.beetleWithin</c> of it, but for one that's been driven off, clubs it (the stop's crate hands leave the
    /// crate it has: <see cref="StopHand"/>'s crates).
    /// </summary>
    public static PlayerIntent Beetle(PlayerIntent intent, in PlayerState self, World world, int selfId)
    {
        if (!Free(self) || self.Parent != PlayerState.World || world.Enemies is not { } et)
            return intent;
        var train = world.Train;
        var me = PlayerMotor.WorldPosition(self, train);
        var beetle = world.ActiveEnemies.OfType<FreightBeetle>().Where(f => !f.Gone && f.Mode != BeetleMode.Away)
            .Select(f => (f, At: f.WorldPosition(train))).Where(x => Flat(x.At - me) <= et.CrewBots.BeetleWithin)
            .OrderBy(x => Flat(x.At - me)).ThenBy(x => x.f.Id).FirstOrDefault();
        return beetle.f is null ? intent : Strike(self, train, beetle.At, BeetleReach) ?? intent;
    }

    /// <summary>
    /// The Knotter (note 365: "don't walk the rope. Stop, kill it, couple up"). Running, a bot never walks its back nor jumps
    /// its gap (<see cref="Knotted"/>, read by the walkers' ways along the train). At a stand it's slack and blows land: a
    /// bot that can get down goes round to the ground beside its gap, on its own side of the train, and clubs it from there
    /// (its <c>meleeRadius</c> reaches the gap's middle from either side). Killed, the cars stand uncoupled where it held
    /// them, and the driver backs onto them (<see cref="ConductorBot"/>).
    /// </summary>
    public static PlayerIntent Knotter(PlayerIntent intent, in PlayerState self, World world, int selfId, StopHand? hand)
    {
        if (hand is null || !Free(self) || !CanGoDown(self, world.Train))
            return intent;
        var train = world.Train;
        var knot = world.ActiveEnemies.OfType<Knotter>().Where(k => !k.Gone && k.Mode == KnotterMode.Slack && k.Attached > 0 && k.Attached < train.Frames.Count)
            .OrderBy(k => k.Id).FirstOrDefault();
        if (knot is null)
            return intent;
        var frame = train.Frames[knot.Attached];
        var at = knot.WorldPosition(train);
        var me = PlayerMotor.WorldPosition(self, train);
        int side = frame.ToLocal(me).X >= 0 ? 1 : -1;
        double outside = frame.Shape.HalfWidth + BesideTheCar;
        var beside = frame.ToWorld(new Double3(side * outside, 0, knot.Local.Z));
        if (hand.Afoot(self, world, beside, 0.6) is { } going)
            return Up(self, going);
        // From beside the gap: turned to it and swinging (the reach covers the step across to it).
        return Strike(self, train, at, outside + BesideTheCar) ?? intent;
    }

    /// <summary>Out past a car's side a crewmate stands clear of it on the ballast (m: a body's width and a little).</summary>
    const double BesideTheCar = 0.6;

    /// <summary>
    /// Whether the gap between two cars side by side in a rake is a Knotter's (note 365: <see cref="Vehicle.Knot"/> on the front
    /// one of them): never jumped, never stepped down into. A gap opened to <c>knotter.gap</c> is no jump (a roof jump carries
    /// about 2.8 m), and walking its back at speed is slipping under the train.
    /// </summary>
    public static bool Knotted(TrainOnLine train, int car, int other)
    {
        if (car < 0 || other < 0 || car >= train.Vehicles.Count || other >= train.Vehicles.Count)
            return false;
        int front = train.VehicleBehind(car) == other ? car : train.VehicleBehind(other) == car ? other : -1;
        return front >= 0 && train.Vehicles[front].Knot > 0;
    }

    /// <summary>
    /// Hotbox (note 367: "hear the knock, find the wheel, stop to pull it"). The driver stops for it once it glows; at the
    /// stand it half unfolds out of its truck, and a bot that can get down goes to the ground beside it with a crowbar or the
    /// wrench in hand and holds Use there until it's prised out. It stands off it at between its bite's reach and the prise's
    /// (<c>snapReach</c>, <c>priseReach</c>): prised from the side, out of its bite. Someone else at it already, it's theirs.
    /// No tool to hand, it's clubbed instead (it snaps; a hurt, never a kill). Then a car left seized has its axle freed: the
    /// wrench held at one of its trucks from beside it (<see cref="Axles.At"/>).
    /// </summary>
    public static PlayerIntent Hotbox(PlayerIntent intent, in PlayerState self, World world, int selfId, StopHand? hand,
        IReadOnlyList<(int Id, PlayerState State)> crew)
    {
        if (hand is null || !Free(self) || world.Enemies is not { } et || !CanGoDown(self, world.Train))
            return intent;
        var train = world.Train;
        var t = et.Hotbox;
        var me = PlayerMotor.WorldPosition(self, train);
        var box = world.ActiveEnemies.OfType<Hotbox>().Where(h => !h.Gone && h.Mode == HotboxMode.Unfolded && h.Attached > 0 && h.Attached < train.Frames.Count)
            .Select(h => (h, At: h.WorldPosition(train))).OrderBy(x => Flat(x.At - me)).ThenBy(x => x.h.Id).FirstOrDefault();
        if (box.h is not null)
        {
            var at = box.At;
            bool mine = (me - at).Length <= t.PriseReach;
            if (!mine && crew.Any(c => c.Id != selfId && c.State.Alive && (PlayerMotor.WorldPosition(c.State, train) - at).Length <= t.PriseReach + 0.3))
                return intent;
            bool tool = Kit.Held(self) is Tool.Crowbar or Tool.Wrench;
            byte key = tool ? (byte)0 : PriseKey(self);
            if (!tool && key == 0)
                return Club(self, world, hand, at, JawReach) is { } clubbing ? Up(self, clubbing) : intent;
            if (hand.Afoot(self, world, PriseSpot(train, box.h, at, t), 0.3) is { } going)
                return Up(self, going);
            if (key > 0)
                return new PlayerIntent { Select = key };
            return Facing(self, train, at) with { Buttons = PlayerButtons.Use };
        }
        // Prised (or killed), the car it was in may be left seized: freed with the wrench at one of its trucks.
        if (!Repairs.WrenchInHand(self) && Repairs.WrenchKey(self) == 0)
            return intent;
        int? seized = null;
        var spot = default(Double3);
        double best = double.MaxValue;
        foreach (var v in train.Dynamics.Consist.Vehicles)
        {
            if (!v.Seized || v.Id <= 0 || v.Id >= train.Frames.Count
                || world.ActiveEnemies.Any(e => e is Hotbox { Gone: false } h && h.Attached == v.Id && h.Mode != HotboxMode.Prised))
                continue;
            var frame = train.Frames[v.Id];
            foreach (double end in new[] { -1.0, 1.0 })
                foreach (double side in new[] { -1.0, 1.0 })
                {
                    var at = frame.ToWorld(new Double3(side * (frame.Shape.HalfWidth + BesideTheCar), 0, end * Math.Max(0, frame.Shape.HalfLength - t.BogieInset)));
                    if (Flat(at - me) < best)
                        (seized, spot, best) = (v.Id, at, Flat(at - me));
                }
        }
        if (seized is null)
            return intent;
        if (Axles.At(self, train, t) is null && hand.Afoot(self, world, spot, 0.3) is { } toTheTruck)
            return Up(self, toTheTruck);
        if (Repairs.WrenchKey(self) is var wrench and > 0)
            return new PlayerIntent { Select = wrench };
        return new PlayerIntent { Buttons = PlayerButtons.Use };
    }

    /// <summary>The slot key of a crowbar or the wrench in the kit (the crowbar first), or 0.</summary>
    static byte PriseKey(in PlayerState self)
    {
        foreach (var want in new[] { Tool.Crowbar, Tool.Wrench })
            for (int i = 0; i < Kit.Slots; i++)
                if (Kit.At(self.Kit, i) == want)
                    return (byte)(i + 1);
        return 0;
    }

    /// <summary>
    /// Where to prise a Hotbox from: on the ground outside its car's side by its truck, toward the car's middle, as far off it
    /// as halfway between its bite's reach and the prise's (both measured from its truck to where you stand).
    /// </summary>
    static Double3 PriseSpot(TrainOnLine train, Hotbox h, Double3 at, HotboxTuning t)
    {
        var frame = train.Frames[h.Attached];
        double ground = frame.ToWorld(new Double3(0, 0, h.Local.Z)).Y;
        double dy = Math.Max(0, at.Y - ground);
        double reach = (t.SnapReach + t.PriseReach) / 2;
        double flat = Math.Sqrt(Math.Max(0, reach * reach - dy * dy));
        double outward = Math.Max(0, frame.Shape.HalfWidth + BesideTheCar - Math.Abs(h.Local.X));
        double along = Math.Sqrt(Math.Max(0, flat * flat - outward * outward));
        double x = h.Local.X + h.Side * outward, z = h.Local.Z - Math.Sign(h.Local.Z) * along;
        return frame.ToWorld(new Double3(x, 0, z));
    }

    /// <summary>
    /// The Brakeman (note 364: "chase it alone, catch it together"). The two lowest ids of the bots up on the roofs are the
    /// pincer: the lower takes the front, the other the back. While he's out of sight each keeps its end of the train (he
    /// comes up at an end, so between them); seen, each closes on him from its side to <c>crewBots.brakemanStandOff</c> (he
    /// runs from the nearer into the other), and cornered (both within his <c>cornerSpan</c>) they club him. Everyone else up
    /// there, and a lone bot that can't corner him, unwinds the cars he's wound: along the roofs to the car's brake wheel and
    /// Use held there, only while the car's wound (else the wheel turns the rake's handbrakes: <see cref="CrewActions"/>).
    /// </summary>
    public static PlayerIntent Brakeman(PlayerIntent intent, in PlayerState self, World world, int selfId,
        IReadOnlyList<(int Id, PlayerState State)> crew, CrewCalls? calls)
    {
        var train = world.Train;
        if (!Free(self) || self.Has(PlayerFlags.Seated) || self.Surface != Surface.Roof || !OnTheRake(train, self.Parent)
            || world.Enemies is not { } et)
            return intent;
        double mine = Along(train, self);
        var him = world.ActiveEnemies.OfType<Enemies.Brakeman>().Where(x => !x.Gone && OnTheRake(train, x.Attached)).OrderBy(x => x.Id).FirstOrDefault();
        if (him is not null)
        {
            // The pair: the two lowest ids of the bots on the roofs (ourselves among them).
            var pair = crew.Where(c => c.Id != selfId && c.State.Alive && c.State.Surface == Surface.Roof && OnTheRake(train, c.State.Parent)
                    && !c.State.Has(PlayerFlags.Seated) && (calls?.IsBot(c.Id) ?? true)).Select(c => c.Id)
                .Append(selfId).Order().Take(2).ToList();
            if (pair.Count == 2 && pair.Contains(selfId))
            {
                int side = pair[0] == selfId ? 1 : -1; // +1: the front (toward the engine), the way along grows
                var (head, tail) = Span(train);
                double mark;
                if (him.Mode == BrakemanMode.Hidden)
                    mark = side > 0 ? head - PostIn : tail + PostIn;
                else
                    mark = Math.Clamp(BrakemanAlong(train, him) + side * et.CrewBots.BrakemanStandOff, tail + PostIn, head - PostIn);
                if (him.Mode == BrakemanMode.Cornered && Math.Abs(mine - BrakemanAlong(train, him)) <= et.CrewBots.BrakemanStandOff + 0.75)
                    return Strike(self, train, him.WorldPosition(train), et.CrewBots.BrakemanStandOff) ?? intent;
                return ToAlong(self, train, mark, side > 0 ? Math.PI : 0) ?? Facing(self, train, him.WorldPosition(train));
            }
        }
        return Unwind(self, world, selfId, crew, mine) ?? intent;
    }

    /// <summary>How far in from a roof's end the pincer keeps its end of the train (m: on the roof, just short of its edge).</summary>
    const double PostIn = 0.2;

    /// <summary>
    /// The nearest car he's wound that nobody else is at (by its wheel along the train), unwound at its wheel. Use held only
    /// while it's wound, or for a while after it's been held long enough to have been (the snapshot that says it's unwound
    /// can come a few ticks late); a press begun on a car that turned out unwound is let go at once. Either way, a second
    /// press on an unwound car's wheel would wind the whole rake's handbrakes on.
    /// </summary>
    static PlayerIntent? Unwind(in PlayerState self, World world, int selfId, IReadOnlyList<(int Id, PlayerState State)> crew, double mine)
    {
        var train = world.Train;
        double hold = train.Dynamics.Tuning.Couplings.HandbrakeSeconds;
        // Still on a wheel we unwound: hold on a while.
        if (self.ActionProgress >= hold * 0.5 && self.ActionProgress < hold * 2 && Wheel(train, self.Parent) is { } still
            && Flat(train.Frames[self.Parent].ToWorld(still.Position) - PlayerMotor.WorldPosition(self, train)) <= still.Radius)
            return new PlayerIntent { Buttons = PlayerButtons.Use };
        (int Car, Interactable Wheel, double Along)? best = null;
        foreach (var v in train.Dynamics.Consist.Vehicles)
        {
            if (!v.Wound || !OnTheRake(train, v.Id) || Wheel(train, v.Id) is not { } wheel)
                continue;
            var pose = train.Cars[v.Id];
            double along = pose.FrontDistance - wheel.Position.Z - pose.Length / 2;
            var at = train.Frames[v.Id].ToWorld(wheel.Position);
            if (crew.Any(c => c.Id != selfId && c.State.Alive && Flat(PlayerMotor.WorldPosition(c.State, train) - at) <= wheel.Radius + 0.7))
                continue;
            if (best is not { } b || Math.Abs(along - mine) < Math.Abs(b.Along - mine))
                best = (v.Id, wheel, along);
        }
        if (best is not { } goal)
            return null;
        if (goal.Car != self.Parent)
            return ToAlong(self, train, goal.Along, 0);
        // Beside the wheel, a little in from the car's end, facing the end it's at.
        double end = Math.Sign(goal.Wheel.Position.Z);
        var spot = new Double3(goal.Wheel.Position.X, 0, goal.Wheel.Position.Z - end * 0.3);
        var (step, there) = WarmUp.Steer(self, spot, end > 0 ? Math.PI : 0);
        return there ? new PlayerIntent { Buttons = PlayerButtons.Use } : step;
    }

    static Interactable? Wheel(TrainOnLine train, int car)
    {
        if (car <= 0 || car >= train.Frames.Count)
            return null;
        foreach (var i in train.Frames[car].Shape.Interactables)
            if (i.Kind == InteractableKind.Handbrake)
                return i;
        return null;
    }

    /// <summary>A car of the engine's rake, bar the engine (the cars he works and the roofs he's walked on).</summary>
    static bool OnTheRake(TrainOnLine train, int car) =>
        car > 0 && car < train.Frames.Count && train.Dynamics.Consist.IndexOf(car) >= 0;

    /// <summary>Where a crewmate on a roof is along the train (m along the line: the more, the nearer the engine).</summary>
    static double Along(TrainOnLine train, in PlayerState s)
    {
        var pose = train.Cars[s.Parent];
        return pose.FrontDistance - s.Position.Z - pose.Length / 2;
    }

    static double BrakemanAlong(TrainOnLine train, Enemies.Brakeman b)
    {
        var pose = train.Cars[b.Attached];
        return pose.FrontDistance - b.Local.Z - pose.Length / 2;
    }

    /// <summary>The engine's rake's cars bar the engine, front to tail, as distances along the line (his span).</summary>
    static (double Head, double Tail) Span(TrainOnLine train)
    {
        var cars = train.Dynamics.Consist.Vehicles.Where(v => OnTheRake(train, v.Id)).ToList();
        if (cars.Count == 0)
            return (0, 0);
        var last = train.Cars[cars[^1].Id];
        return (train.Cars[cars[0].Id].FrontDistance, last.FrontDistance - last.Length);
    }

    /// <summary>
    /// Along the roofs to a point along the train (its gaps jumped, but never a Knotter's), and there, turned to
    /// <paramref name="yaw"/>. Null once there.
    /// </summary>
    static PlayerIntent? ToAlong(in PlayerState self, TrainOnLine train, double along, double yaw)
    {
        var pose = train.Cars[self.Parent];
        double half = train.Frames[self.Parent].Shape.HalfLength;
        double z = pose.FrontDistance - along - pose.Length / 2;
        // On this roof, or in the gap off one of its ends (a mark between two roofs is this roof's end from here).
        if (Math.Abs(z) <= half + train.Dynamics.Tuning.Geometry.CouplingGap)
        {
            var (step, there) = WarmUp.Steer(self, new Double3(0, 0, Math.Clamp(z, -half + PostIn, half - PostIn)), yaw);
            return there ? null : step;
        }
        return StopHand.AlongRoofs(self, train, z < 0 ? -1 : 1, jumpGaps: true);
    }

    /// <summary>
    /// A crewmate the Knotter's coiled round (note 365: "broken by a friend at the gap"), as the rescue's generic way can't:
    /// what holds them is the rope's middle, its gap's width off both cars' ends, and walking out onto it at speed is the slip
    /// itself (note NNN). Within <c>grab.pullReach</c> of the one held, Use held where we stand: hauled up. On the roof of
    /// the car in front of its gap (whose end ladder goes down into it), to the top of that ladder and down it toward them.
    /// Anywhere else, nothing: its coil is over in <c>coilSeconds</c>, and only someone at the gap is in time.
    /// </summary>
    static PlayerIntent? KnotRescue(in PlayerState self, World world, Knotter knot, IReadOnlyList<(int Id, PlayerState State)>? crew)
    {
        var train = world.Train;
        if (world.Enemies is not { } et || crew is null || knot.Attached <= 0 || knot.Attached >= train.Frames.Count)
            return null;
        int held = knot.Holding;
        if (!crew.Any(c => c.Id == held))
            return null;
        var them = PlayerMotor.WorldPosition(crew.First(c => c.Id == held).State, train);
        if ((them - PlayerMotor.WorldPosition(self, train)).Length <= et.Grab.PullReach)
            return new PlayerIntent { Buttons = PlayerButtons.Use };
        if (self.Parent != knot.Attached)
            return null;
        if (self.Surface == Surface.Ladder)
            return new PlayerIntent { MoveZ = -1 };
        if (self.Surface != Surface.Roof)
            return null;
        double l = train.Frames[knot.Attached].Shape.HalfLength;
        var (step, there) = WarmUp.Steer(self, new Double3(train.Dynamics.Tuning.Geometry.EndLadderX, 0, l - 0.35), Math.PI);
        return there ? new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Use } : step;
    }
}
