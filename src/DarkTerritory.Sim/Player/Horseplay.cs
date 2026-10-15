using Ballast;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Player;

/// <summary>
/// What knocked a crewmate (note 717, horseplay): a friend's shove, a shove passed on through them (a bump), a lump of coal off
/// the shovel, a tool's bonk. Staggers, soot and knocks only: none of it is damage (the director's rule for all of it, 14 Oct
/// 2026); a death comes only through the world's own rules (a fall, the jump off at speed).
/// </summary>
public enum KnockKind : byte { Shove = 1, Bump = 2, Coal = 3, Bonk = 4 }

/// <summary>
/// A crewmate knocked by another (note 717), as the crew see it: the host's, kept as long as a swing is and replicated, so the
/// shover's arm goes out, the bonk clangs and the coal's thud is heard on every machine. Nothing in the sim reads it back.
/// </summary>
/// <param name="Id">Unique for the night (the record's key, shared with swings and hits).</param>
/// <param name="By">Who did it (player id).</param>
/// <param name="Target">Whom it knocked; −1 for a shove at nobody (a shove's arm goes out all the same).</param>
public readonly record struct HorseplayEvent(int Id, uint Tick, int By, KnockKind Kind, int Target);

/// <summary>A lump of coal in flight (note 717), as every machine draws it: where it is (world), and whose it was.</summary>
public readonly record struct LumpInFlight(int Id, int By, Double3 Position);

/// <summary>
/// A crewmate where they stood at the start of the tick (note 717's crowd): their frame and their feet in it. The host and a
/// predicting client both separate a player from the others as the last snapshot had them, so the two agree exactly.
/// </summary>
public readonly record struct Shoulder(int Id, int Parent, Double3 Position);

/// <summary>
/// Horseplay among the crew (GDD §23.1, "failure has to be funny"; ARCHITECTURE §8 note 717; the director, 14 Oct 2026: "The
/// griefer ideas are funny"): crewmates are solid to each other, a shove staggers and can put you off a roof (at speed, §23's
/// jump-off rule kills, and the report names the shover), the shovelful can be thrown, and a tool's swing bonks a friend.
/// The shared rules, run alike on the host and a predicting client; the host's side is <see cref="World"/>'s.
/// </summary>
public static class Horseplay
{
    /// <summary>
    /// Solid to the others (the crowd): alive, on their feet, and not on a ladder, in a gun's seat, at a crane's controls or in
    /// something's grip. A ladder is passed on (a hatch's ladder comes up through a crowded roof), and the air jumped through.
    /// </summary>
    public static bool Solid(in PlayerState s) =>
        s.Alive && s.Grounded && !s.Has(PlayerFlags.Seated) && !s.Has(PlayerFlags.Operating) && !s.Has(PlayerFlags.Held);

    /// <summary>The crew as they stand, for <see cref="Push"/>: everyone solid, in id order (so every machine walks the same list).</summary>
    public static List<Shoulder> Crowd(IEnumerable<(int Id, PlayerState State)> crew)
    {
        var list = new List<Shoulder>();
        foreach (var (id, s) in crew.OrderBy(c => c.Id))
            if (Solid(s))
                list.Add(new Shoulder(id, s.Parent, s.Position));
        return list;
    }

    /// <summary>
    /// Note 717: how far the crowd pushes <paramref name="s"/> this tick (in their parent frame, across the floor; m): out of
    /// anyone closer than two shoulders' breadth (horseplay.crowd.radius each) at their own height, <c>share</c> of the
    /// overlap a tick (the other takes theirs on their own step), no faster than <c>maxSpeed</c>. Soft, so a doorway someone
    /// stands in is squeezed through, pushing them along a little. Two on the very same spot part by id, one each way.
    /// </summary>
    public static Double3 Push(in PlayerState s, int self, IReadOnlyList<Shoulder> crowd, TrainOnLine train, PlayerTuning p, double dt)
    {
        var t = p.Horseplay.Crowd;
        if (t.Radius <= 0 || !Solid(s) || s.Parent >= train.Frames.Count)
            return default;
        var me = World(s.Parent, s.Position, train);
        double reach = 2 * t.Radius, x = 0, z = 0;
        foreach (var o in crowd)
        {
            if (o.Id == self || o.Parent >= train.Frames.Count)
                continue;
            var them = World(o.Parent, o.Position, train);
            if (Math.Abs(them.Y - me.Y) >= p.Height * 0.9)
                continue;
            double dx = me.X - them.X, dz = me.Z - them.Z, d2 = dx * dx + dz * dz;
            if (d2 >= reach * reach)
                continue;
            double d = Math.Sqrt(d2);
            if (d < 1e-6)
                (dx, dz, d) = (self < o.Id ? 1 : -1, 0, 1);
            double overlap = (reach - Math.Sqrt(d2)) * t.Share;
            x += dx / d * overlap;
            z += dz / d * overlap;
        }
        double length = Math.Sqrt(x * x + z * z), most = t.MaxSpeed * dt;
        if (length < 1e-9)
            return default;
        if (length > most)
            (x, z) = (x * most / length, z * most / length);
        var push = new Double3(x, 0, z);
        return s.Parent == PlayerState.World ? push : train.Frames[s.Parent].DirToLocal(push) with { Y = 0 };
    }

    static Double3 World(int parent, Double3 position, TrainOnLine train) =>
        parent == PlayerState.World ? position : train.Frames[parent].ToWorld(position);

    /// <summary>
    /// Note 717: a staggered crewmate's intent (<see cref="PlayerState.Stagger"/>): they can look and talk, and nothing else, till
    /// it passes: no walking, no keys (the cab's controls included), no swing, no shove. The dead's own keys (a bookmark, the
    /// skip) aren't a body's, so they stay.
    /// </summary>
    public static PlayerIntent Staggered(in PlayerState s, in PlayerIntent intent)
    {
        if (s.Stagger == 0 || !s.Alive)
            return intent;
        return intent with
        {
            MoveX = 0,
            MoveZ = 0,
            Buttons = intent.Buttons & PlayerButtons.Hand,
            ThrottleNotch = 0,
            Lamp = LampSwitch.None,
            Actions = intent.Actions & PlayerActions.Bookmark,
            Moves = intent.Moves & PlayerMoves.Crouch,
            Emote = Emote.None,
        };
    }

    /// <summary>Whether a crewmate can be shoved: on their feet, and not on a ladder, in a gun's seat, at a crane or held.</summary>
    public static bool Shoveable(in PlayerState s) => Solid(s);

    /// <summary>Whether <paramref name="s"/> can shove at all: alive, steady, hands free of the shovel, their feet their own.</summary>
    public static bool CanShove(in PlayerState s) =>
        s.Alive && s.Stagger == 0 && !s.Has(PlayerFlags.Shovelful) && !s.Has(PlayerFlags.Held) && !s.Has(PlayerFlags.Seated)
        && !s.Has(PlayerFlags.Operating) && s.Surface != Surface.Ladder;

    /// <summary>
    /// The crewmate a shove from <paramref name="s"/> would land on (note 717): the nearest shoveable one in front, within
    /// horseplay.shove.reach of the shover's chest and its coneDegrees of where they face; null for nobody. The HUD asks it
    /// for its prompt, the host for the shove.
    /// </summary>
    public static (int Id, PlayerState State)? ShoveTarget(in PlayerState s, int self, IEnumerable<(int Id, PlayerState State)> mates, TrainOnLine train,
        PlayerTuning p)
    {
        var t = p.Horseplay.Shove;
        if (!CanShove(s) || s.Parent >= train.Frames.Count)
            return null;
        var chest = PlayerMotor.WorldPosition(s, train) + Double3.Up * Chest;
        double yaw = PlayerMotor.WorldYaw(s, train), cos = DMath.Cos(t.ConeDegrees * Math.PI / 180);
        var facing = new Double3(-DMath.Sin(yaw), 0, -DMath.Cos(yaw));
        (int, PlayerState)? best = null;
        double bestD = double.MaxValue;
        foreach (var (id, o) in mates)
        {
            if (id == self || !Shoveable(o) || o.Parent >= train.Frames.Count)
                continue;
            var to = PlayerMotor.WorldPosition(o, train) + Double3.Up * Chest - chest;
            double d = to.Length;
            if (d > t.Reach || Math.Abs(to.Y) > p.Height * 0.6)
                continue;
            var flat = to with { Y = 0 };
            if (flat.Length > 0.05 && Double3.Dot(flat.Normalized, facing) < cos)
                continue;
            if (d < bestD || d == bestD && id < best!.Value.Item1)
                (best, bestD) = ((id, o), d);
        }
        return best;
    }

    /// <summary>The height of a crewmate's chest over their feet (m): where a shove lands and a lump is caught.</summary>
    public const double Chest = 1.2;

    /// <summary>
    /// The way a shove sends its target (world, horizontal unit): from the shover to them, or, square on the same spot, the
    /// way the shover faces.
    /// </summary>
    public static Double3 Away(Double3 from, Double3 to, double yaw)
    {
        var d = (to - from) with { Y = 0 };
        return d.Length > 0.05 ? d.Normalized : new Double3(-DMath.Sin(yaw), 0, -DMath.Cos(yaw));
    }

    /// <summary>
    /// Note 717: a knock put on <paramref name="s"/>, the host's authoritative move (a predicting client adopts it as it does a
    /// pull off the roof: <see cref="PlayerState.Placed"/>). A shove (or a bump through a friend) takes them off their feet, in
    /// the frame they stand in, <paramref name="away"/> (world) at its speed and up a little: on a roof's edge that's over the
    /// lip, which only holds a walker (note 273). Every knock staggers; coal soots their face for the night.
    /// </summary>
    public static void Knock(ref PlayerState s, KnockKind kind, Double3 away, TrainOnLine train, HorseplayTuning t)
    {
        double seconds = kind switch
        {
            KnockKind.Shove => t.Shove.StaggerSeconds,
            KnockKind.Bump => t.Shove.ChainStaggerSeconds,
            KnockKind.Coal => t.Coal.StaggerSeconds,
            _ => t.Bonk.StaggerSeconds,
        };
        int ticks = (int)Math.Round(seconds * SimConstants.TickRate);
        s.Stagger = (byte)Math.Clamp(Math.Max(s.Stagger, ticks), 0, byte.MaxValue);
        if (kind == KnockKind.Coal)
            s.Flags |= PlayerFlags.Sooted;
        if (kind is KnockKind.Shove or KnockKind.Bump && Shoveable(s) && s.Parent < train.Frames.Count)
        {
            double speed = kind == KnockKind.Shove ? t.Shove.Speed : t.Shove.Speed * t.Shove.ChainShare;
            var along = s.Parent == PlayerState.World ? away : train.Frames[s.Parent].DirToLocal(away) with { Y = 0 };
            // Off their feet in the frame they stand in, as a jump takes off (the car has already moved this tick).
            s.Velocity = along * speed + Double3.Up * t.Shove.Lift;
            s.Surface = Surface.Air;
            s.Flags = (s.Flags | PlayerFlags.Shoved) & ~(PlayerFlags.Pushing | PlayerFlags.Crouched);
            s.ActionProgress = 0;
        }
        s.Placed++;
    }
}

/// <summary>
/// Horseplay's numbers (note 717). Mirror of player.json <c>horseplay</c>, where each field is documented. <see cref="Enabled"/>
/// is the switch for the lobby's "crew mischief" to come: off, nobody shoves, bonks a friend or hits one with coal. Crewmates
/// stay solid to each other either way.
/// </summary>
public sealed record HorseplayTuning
{
    public bool Enabled { get; init; } = true;
    /// <summary>Note 724: a public lobby FEELING COMPETITIVE starts with the crew mischief switch off (player.json <c>horseplay.competitiveOff</c>).</summary>
    public bool CompetitiveOff { get; init; } = true;
    public CrowdTuning Crowd { get; init; } = new();
    public ShoveTuning Shove { get; init; } = new();
    public CoalTuning Coal { get; init; } = new();
    public BonkTuning Bonk { get; init; } = new();
    public MarkedTuning Marked { get; init; } = new();
}

/// <summary>A shover whose shove killed, marked for the night (note 717). Docs in player.json <c>horseplay.marked</c>.</summary>
public sealed record MarkedTuning
{
    public bool Enabled { get; init; } = true;
    public double TargetWeight { get; init; } = 6;
    public double AllowanceM { get; init; } = 20;
    public double PressurePerSecond { get; init; } = 0.1;
}

/// <summary>Crewmates solid to each other (note 717). Docs in player.json <c>horseplay.crowd</c>.</summary>
public sealed record CrowdTuning
{
    public double Radius { get; init; } = 0.25;
    public double Share { get; init; } = 0.75;
    public double MaxSpeed { get; init; } = 6;
}

/// <summary>The shove (note 717). Docs in player.json <c>horseplay.shove</c>.</summary>
public sealed record ShoveTuning
{
    public double Reach { get; init; } = 1.2;
    public double ConeDegrees { get; init; } = 45;
    public double Speed { get; init; } = 3;
    public double Lift { get; init; } = 1.5;
    public double StaggerSeconds { get; init; } = 0.6;
    public double CooldownSeconds { get; init; } = 1.5;
    public double ChainReach { get; init; } = 0.8;
    public double ChainShare { get; init; } = 0.5;
    public double ChainStaggerSeconds { get; init; } = 0.3;
}

/// <summary>A lump of coal thrown off the shovel (note 717). Docs in player.json <c>horseplay.coal</c>.</summary>
public sealed record CoalTuning
{
    public double Speed { get; init; } = 12;
    public double Lift { get; init; } = 1;
    public double Radius { get; init; } = 0.08;
    public double StaggerSeconds { get; init; } = 0.35;
    public double Blow { get; init; } = 0.25;
    public double LifeSeconds { get; init; } = 3;
}

/// <summary>A tool's swing landing on a friend (note 717). Docs in player.json <c>horseplay.bonk</c>.</summary>
public sealed record BonkTuning
{
    public double StaggerSeconds { get; init; } = 0.3;
    public int ReportFrom { get; init; } = 5;
}
