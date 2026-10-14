using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// Jacob, the fisherman (the director, 8 Oct 2026: "an NPC named Jacob who can be found randomly in the world near water
/// edges, fishing. He's another legendary NPC who if found and talked to will cast magic over the train and repair
/// everything instantly, restoring it to brand new condition without affecting your loot count. He spawns very rarely";
/// GDD §3.2; ARCHITECTURE §8 note 489). On a very few nights he's at a lake's or the sea's edge beside the line, his rod
/// out over the water. Whoever holds Use by him (the host's to hear: it changes the train) has a word with him, and once a
/// night he mends the whole train as new (<see cref="World.Bless"/>), its cargo and loot left alone.
/// <para>
/// Built on <see cref="Enemy"/> as Dave is (note 570), for replication and a place in the world: a <see cref="Hazard"/>,
/// never struck, never anyone's quarry. <see cref="Enemy.Extra"/> is 1 once he's mended the train (replicated: the
/// blessing's seen and his card says so).
/// </para>
/// </summary>
public sealed class Jacob(int id) : Enemy(id)
{
    public override EnemyKind Kind => EnemyKind.Jacob;
    public override PressureZone Zone => PressureZone.Outside;
    public override Sense Sense => Sense.Sight;
    public override bool Hazard => true;
    public override bool Far => true;
    public override bool Exposed => false;
    public override string Called => "Jacob";

    /// <summary>The world yaw he faces, out over the water (−Z at 0, as a player's).</summary>
    public double Yaw => Lateral;
    /// <summary>Whether he's mended the train tonight (replicated).</summary>
    public bool Blessed => Extra >= 0.5;

    public static Jacob At(int id, Double3 world, double lineHint, double yaw) =>
        new(id) { Attached = Loose, Local = world, Lateral = yaw, LineDistance = lineHint };

    /// <summary>Host: he's mended the train; once a night.</summary>
    internal void Bless() => Extra = 1;

    protected override void Tick(EnemyContext ctx) { }

    /// <summary>The water's surface at a point (the lakes', the sea's), or null where there's none.</summary>
    public static double? WaterAt(Train.TrainOnLine train, Double3 p) => Combat.Guns.Water(train, p);

    /// <summary>
    /// Where Jacob is tonight, if anywhere (enemies.json <c>jacob</c>): on <see cref="JacobTuning.Chance"/> of nights, at a
    /// water's edge beside the main line past <see cref="JacobTuning.PastGate"/>: on dry ground with water a stride or two
    /// further out, clear of the track and of any wall, facing the water. From the route's seed; null for none.
    /// </summary>
    public static (Double3 At, double Along, double Yaw)? Site(World world, Route.Route route, JacobTuning t)
    {
        var dice = Streams.Rng(route.Seed, "jacob");
        if (!dice.Chance(t.Chance))
            return null;
        var train = world.Train;
        var line = train.Line;
        double end = line.PathLength(Rail.RailLine.MainPath) - t.ShortOfEnd;
        var found = new List<(Double3 At, double Along, double Yaw)>();
        for (double s = route.Gate + t.PastGate; s < end; s += t.Step)
        {
            var sample = line.Sample(s);
            var right = Double3.Cross(sample.Tangent, Double3.Up).Normalized;
            foreach (double side in (double[])[-1, 1])
                for (double off = t.Out[0]; off <= t.Out[1]; off += t.Step / 4)
                {
                    var at = sample.Position + right * (side * off);
                    double hint = s;
                    double ground = PlayerMotor.GroundAt(at, line, ref hint);
                    var spot = at with { Y = ground };
                    if (Combat.Guns.Water(train, spot) is { } w && w > ground - 0.05)
                        continue;
                    // Water just out past him, away from the line.
                    var outward = right * side;
                    var wet = spot + outward * t.Edge;
                    double h2 = s;
                    if (Combat.Guns.Water(train, wet) is not { } water || water <= PlayerMotor.GroundAt(wet, line, ref h2))
                        continue;
                    if (Moose.TrackOff(train, spot, hint) < t.TrackClearance || world.InFort(spot) || !Dave.Open(train, spot, 2))
                        continue;
                    found.Add((spot, s, DMath.Atan2(-outward.X, -outward.Z)));
                    break;
                }
        }
        return found.Count == 0 ? null : found[(int)(dice.NextDouble() * found.Count)];
    }
}

/// <summary>enemies.json <c>jacob</c> (note 572). Field docs live in that file.</summary>
public sealed record JacobTuning
{
    public double Chance { get; init; } = 0.05;
    public double PastGate { get; init; } = 1500;
    public double ShortOfEnd { get; init; } = 800;
    public double Step { get; init; } = 40;
    public double[] Out { get; init; } = [10, 45];
    public double Edge { get; init; } = 3;
    public double TrackClearance { get; init; } = 8;
    public double Reach { get; init; } = 3;
    public double HoldSeconds { get; init; } = 0.5;
}
