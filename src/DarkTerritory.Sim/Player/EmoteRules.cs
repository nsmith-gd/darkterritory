using Ballast;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Player;

/// <summary>Where an edge emote is taken (note 722): a roof's side, an open side door's sill (sat on), a doorway (leant out of).</summary>
public enum EdgeKind : byte { None, Roof, Sill, Doorway }

/// <summary>An edge a crewmate's at (note 722): which kind, and which side of their car (+1 its +X, −1 its −X).</summary>
public readonly record struct Edge(EdgeKind Kind, int Side);

/// <summary>
/// The emotes for two, and the edges (GDD §9; the director, 14 Oct 2026, App. F.6; ARCHITECTURE §8 note 722): the shared
/// rules, the host's for what it keeps, a bot's for what it answers, the scene's for how it draws them. Emotes still move
/// nothing: a high five is two records with the tick the hands met; a sit is a record held while its crewmate stays put.
/// </summary>
public static class EmoteRules
{
    /// <summary>An emote that takes two: offered, and answered by a crewmate picking it too.</summary>
    public static bool ForTwo(Emote e) => e is Emote.HighFive or Emote.FistBump;

    /// <summary>An emote held till its crewmate moves (the sit, the lean), not timed.</summary>
    public static bool Held(Emote e) => e is Emote.SitEdge or Emote.LeanOut;

    /// <summary>
    /// Whether <paramref name="s"/> can emote at all: alive, nothing has hold of them, not over a friend's shoulder (note 723:
    /// the carried look, talk and wriggle, and nothing else).
    /// </summary>
    public static bool CanEmote(in PlayerState s) => s.Alive && !s.Has(PlayerFlags.Held) && !s.Has(PlayerFlags.Carried);

    /// <summary>
    /// Steady where they stand, for an edge emote: on their feet in a car's frame, not knocked, not carrying a friend, and not
    /// in a gun's seat, at a crane, at the handcar's handle or pushing a gun.
    /// </summary>
    static bool Settled(in PlayerState s, TrainOnLine train) =>
        CanEmote(s) && s.Stagger == 0 && s.Grounded && s.Parent >= 0 && s.Parent < train.Frames.Count
        && (s.Flags & (PlayerFlags.Carrying | PlayerFlags.Seated | PlayerFlags.Operating | PlayerFlags.Pumping | PlayerFlags.Pushing | PlayerFlags.Shoved)) == 0;

    /// <summary>
    /// Where <paramref name="s"/> could sit with their legs over (note 722), or null. On a car's roof, or anything else of a car's
    /// they stand on outside it (a side door's landing, the guard van's platform, the engine's running board), within
    /// emotes.edgeReach of where its footing ends at the side and drops more than emotes.edgeDrop. Inside a car, in an open
    /// side doorway (<see cref="LeanAt"/>), the same out through it within edgeReach + sillReach: across the sill and the
    /// landing outside a cargo car's door to its edge (they're drawn sat there), or the cab's doorway onto its running board.
    /// </summary>
    public static Edge? SitAt(in PlayerState s, TrainOnLine train, EmoteTuning t)
    {
        if (!Settled(s, train) || s.Surface is not (Surface.Roof or Surface.Deck))
            return null;
        if (s.Surface == Surface.Deck && PlayerMotor.Indoors(s, train))
            return LeanAt(s, train, t) is { } door && Drops(s, train, door.Side, t.EdgeReach + t.SillReach, t) ? new Edge(EdgeKind.Sill, door.Side) : null;
        int side = s.Position.X >= 0 ? 1 : -1;
        return Drops(s, train, side, t.EdgeReach, t) ? new Edge(s.Surface == Surface.Roof ? EdgeKind.Roof : EdgeKind.Sill, side) : null;
    }

    /// <summary>
    /// Out from <paramref name="s"/> toward their car's <paramref name="side"/>, the last of the footing at their height (in the
    /// car's frame, its X), going no further than <paramref name="run"/>; and whether it ended within that (<c>Ended</c>).
    /// Footing a little lower (a running board a step down from the cab's floor) is still theirs.
    /// </summary>
    public static (double X, bool Ended) EdgeX(in PlayerState s, TrainOnLine train, int side, double run)
    {
        var shape = train.Frames[s.Parent].Shape;
        var vehicle = train.Vehicles[s.Parent];
        double x = s.Position.X, missed = 0;
        for (double gone = EdgeStep; gone <= run + 1e-9; gone += EdgeStep)
        {
            double next = s.Position.X + side * gone;
            if (shape.TopAt(next, s.Position.Z, vehicle, s.Position.Y + 0.05) is not { } top || top.Top < s.Position.Y - Level)
            {
                // A crack no wider than a wall (the open door's slot between the floor and its landing) is stepped over.
                missed += EdgeStep;
                if (missed > Crack)
                    return (x, true);
                continue;
            }
            missed = 0;
            x = next;
        }
        return (x, missed > 0);
    }

    /// <summary>
    /// The step the footing's walked out in (m); how far below the feet it can be and still be the same footing (m); and the
    /// widest gap in it that's stepped over, not an edge (m).
    /// </summary>
    const double EdgeStep = 0.025, Level = 0.15, Crack = 0.2;

    /// <summary>Whether the footing out toward <paramref name="side"/> ends within <paramref name="run"/>, and what's past it is more than emotes.edgeDrop down.</summary>
    static bool Drops(in PlayerState s, TrainOnLine train, int side, double run, EmoteTuning t)
    {
        var (x, ended) = EdgeX(s, train, side, run);
        if (!ended)
            return false;
        var beyond = train.Frames[s.Parent].Shape.TopAt(x + side * 0.1, s.Position.Z, train.Vehicles[s.Parent], s.Position.Y + 0.05);
        return beyond is not { } top || top.Top < s.Position.Y - t.EdgeDrop;
    }

    /// <summary>
    /// The doorway <paramref name="s"/> could lean out of (note 722), or null: stood on the floor inside a car or the engine's
    /// cab, within emotes.edgeReach of its side, and nothing of the car between them and the outside there, at the knee and
    /// at the chest (an open side door, the cab's doorway; not a shut door, nor over the cab's waist-high side under its
    /// windows).
    /// </summary>
    public static Edge? LeanAt(in PlayerState s, TrainOnLine train, EmoteTuning t)
    {
        if (!Settled(s, train) || s.Surface != Surface.Deck || !PlayerMotor.Indoors(s, train))
            return null;
        var frame = train.Frames[s.Parent];
        var shape = frame.Shape;
        Box room = PlayerMotor.InCab(s, train) ? shape.Cab!.Value : shape.Interior!.Value;
        int side = s.Position.X >= 0 ? 1 : -1;
        double outX = s.Position.X + side * t.EdgeReach;
        if (side > 0 ? outX <= room.Max.X : outX >= room.Min.X)
            return null;
        foreach (double up in Probes)
        {
            var from = frame.ToWorld(s.Position + Double3.Up * up);
            var to = frame.ToWorld(new Double3(outX + side * 0.2, s.Position.Y + up, s.Position.Z));
            if (!Rescue.Clear(train, s.Parent, from, to))
                return null;
        }
        return new Edge(EdgeKind.Doorway, side);
    }

    /// <summary>The heights over the feet a doorway is tested at (m): the knee, under a waist-high side; the chest, under a lintel.</summary>
    static readonly double[] Probes = [0.5, 1.3];

    /// <summary>Whether the emote <paramref name="e"/> can be taken where <paramref name="s"/> is (the edges need one; the rest anywhere).</summary>
    public static bool CanTake(Emote e, in PlayerState s, TrainOnLine train, EmoteTuning t) => e switch
    {
        Emote.SitEdge => SitAt(s, train, t) is not null,
        Emote.LeanOut => LeanAt(s, train, t) is not null,
        _ => CanEmote(s),
    };

    /// <summary>
    /// Whether a held emote of <paramref name="s"/>'s (<paramref name="e"/>) stands them up this tick: they moved or jumped (any
    /// move stands you), or they're no longer at the edge they took it at (shoved, lifted, knocked, the door shut on them).
    /// </summary>
    public static bool StandsUp(Emote e, in PlayerState s, in PlayerIntent intent, TrainOnLine train, EmoteTuning t) =>
        intent.MoveX != 0 || intent.MoveZ != 0 || intent.Has(PlayerButtons.Jump) || !CanTake(e, s, train, t);

    /// <summary>
    /// Whether <paramref name="a"/> faces <paramref name="b"/>: <paramref name="b"/>'s feet within <paramref name="degrees"/>
    /// of where <paramref name="a"/> faces, along the ground.
    /// </summary>
    public static bool Faces(in PlayerState a, in PlayerState b, TrainOnLine train, double degrees)
    {
        var to = (PlayerMotor.WorldPosition(b, train) - PlayerMotor.WorldPosition(a, train)) with { Y = 0 };
        if (to.Length < 0.05)
            return true;
        double yaw = PlayerMotor.WorldYaw(a, train);
        var facing = new Double3(-DMath.Sin(yaw), 0, -DMath.Cos(yaw));
        return Double3.Dot(to.Normalized, facing) >= DMath.Cos(degrees * Math.PI / 180);
    }

    /// <summary>How far apart two crewmates' feet are along the ground (m), or infinity when they aren't on about the same level.</summary>
    public static double Apart(in PlayerState a, in PlayerState b, TrainOnLine train)
    {
        var d = PlayerMotor.WorldPosition(b, train) - PlayerMotor.WorldPosition(a, train);
        return Math.Abs(d.Y) > SameLevel ? double.PositiveInfinity : (d with { Y = 0 }).Length;
    }

    /// <summary>How far apart in height two crewmates can be and still be stood together (m): not a roof and the ballast.</summary>
    const double SameLevel = 0.6;

    /// <summary>
    /// Whether two crewmates are placed for an emote for two: within emotes.reach of each other, on a level, each facing the
    /// other within emotes.facingDegrees.
    /// </summary>
    public static bool Placed(in PlayerState a, in PlayerState b, TrainOnLine train, EmoteTuning t) =>
        a.Alive && b.Alive && Apart(a, b, train) <= t.Reach && Faces(a, b, train, t.FacingDegrees) && Faces(b, a, train, t.FacingDegrees);

    /// <summary>
    /// The offer <paramref name="by"/> answers by picking <paramref name="kind"/> (a high five or fist bump), or null: another
    /// crewmate's of the same kind, unmet and still up at <paramref name="tick"/>, the two placed for it (<see cref="Placed"/>).
    /// The nearest, by the offerer's id on a tie.
    /// </summary>
    public static EmoteEvent? Answering(IEnumerable<EmoteEvent> emotes, Emote kind, int by, in PlayerState s,
        IReadOnlyList<(int Id, PlayerState State)> crew, TrainOnLine train, EmoteTuning t, long tick)
    {
        if (!ForTwo(kind))
            return null;
        EmoteEvent? best = null;
        double bestD = double.MaxValue;
        foreach (var e in emotes)
        {
            if (e.Kind != kind || e.Met || e.By == by || !t.Lasts(e, tick) || StateOf(crew, e.By) is not { } other || !CanEmote(other) || !Placed(s, other, train, t))
                continue;
            double d = Apart(s, other, train);
            if (d < bestD || d == bestD && e.By < best!.Value.By)
                (best, bestD) = (e, d);
        }
        return best;
    }

    /// <summary>
    /// An offer made to <paramref name="me"/> (for a bot to answer, note 722): a high five or fist bump held up by someone else,
    /// unmet and still up, the offerer within emotes.reach and facing them (whichever way they face themselves). The nearest.
    /// </summary>
    public static EmoteEvent? OfferedTo(IEnumerable<EmoteEvent> emotes, int me, in PlayerState s, IReadOnlyList<(int Id, PlayerState State)> crew,
        TrainOnLine train, EmoteTuning t, long tick)
    {
        EmoteEvent? best = null;
        double bestD = double.MaxValue;
        foreach (var e in emotes)
        {
            if (!ForTwo(e.Kind) || e.Met || e.By == me || !t.Lasts(e, tick) || StateOf(crew, e.By) is not { } other || !CanEmote(other))
                continue;
            double d = Apart(s, other, train);
            if (d > t.Reach || !Faces(other, s, train, t.FacingDegrees))
                continue;
            if (d < bestD || d == bestD && e.By < best!.Value.By)
                (best, bestD) = (e, d);
        }
        return best;
    }

    /// <summary>
    /// A dance-off <paramref name="id"/> is in (note 722), or null: the crewmates dancing at <paramref name="tick"/> within
    /// emotes.danceOffReach of them, each pair facing each other within danceOffFacingDegrees, <paramref name="id"/> among them;
    /// two or more. Who leads it now goes round them by id every danceOffBeat seconds, from the host's tick, so every machine
    /// draws the same lead (the rest answer).
    /// </summary>
    public static (int Leader, int Count)? DanceOff(int id, IEnumerable<EmoteEvent> emotes, IReadOnlyList<(int Id, PlayerState State)> crew,
        TrainOnLine train, EmoteTuning t, long tick)
    {
        var dancing = new List<int>();
        foreach (var e in emotes)
            if (e.Kind == Emote.Dance && t.Lasts(e, tick))
                dancing.Add(e.By);
        if (!dancing.Contains(id) || StateOf(crew, id) is not { } me || !me.Alive)
            return null;
        var group = new List<int> { id };
        foreach (int other in dancing)
            if (other != id && StateOf(crew, other) is { Alive: true } o && Apart(me, o, train) <= t.DanceOffReach
                && Faces(me, o, train, t.DanceOffFacingDegrees) && Faces(o, me, train, t.DanceOffFacingDegrees))
                group.Add(other);
        if (group.Count < 2)
            return null;
        group.Sort();
        long beat = Math.Max(1, (long)Math.Round(t.DanceOffBeat * SimConstants.TickRate));
        return (group[(int)(tick / beat % group.Count)], group.Count);
    }

    static PlayerState? StateOf(IReadOnlyList<(int Id, PlayerState State)> crew, int id)
    {
        foreach (var (i, s) in crew)
            if (i == id)
                return s;
        return null;
    }
}
