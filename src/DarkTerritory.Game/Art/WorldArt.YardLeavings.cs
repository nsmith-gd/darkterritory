using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Game.Art;

public sealed partial class WorldArt
{
    /// <summary>
    /// What a stop's yard has lying about its buildings (note 325's second slice; GDD App. F.3, the director: "the same three
    /// things over and over again"): the railway's leavings (<see cref="Leaving(string, int)"/>) in a cluster at each
    /// gable end and one against the back wall of every works building (a shed, the hero, a goods shed, the powerhouse,
    /// a barn, an outbuilding, a station, a signal box, a lamp room, a water tower): ties, rails, drums, a cable reel, a
    /// wheelset, spilt coal, stores dumped, fallen freight, a sandbagged post. On the side away from the tracks the building
    /// serves and its doors, tight to its walls (no wider than the eaves' drip), so the ways in and the yard's tracks stay
    /// clear. The kinds are dealt from one shuffled deck per stop, so the buildings of a yard don't wear the same pile.
    /// The houses are the villages' (B4's and B2's), the wells and the Holdouts their own.
    /// </summary>
    void YardLeavings(MeshBuilder mesh, RailLine line, Route route, RouteFeature f, StopLayout stop, int index, Double3 eye, float valleyDepth)
    {
        var b = stop.Buildings[index];
        var frame = Basis(line.Sample(Math.Clamp(f.Start + b.S, 0, line.Length)).Tangent, Footing(line, route, f, b, valleyDepth), eye, (float)-b.Yaw);
        foreach (var p in YardPiles(line, route, f, index, valleyDepth))
            foreach (var (piece, offset, lift, turn) in Leaving(p.Kind, p.Variant))
            {
                if (piece is null)
                    continue;
                var o = Vector3.Transform(offset, Matrix4x4.CreateRotationY(p.Yaw));
                var local = Matrix4x4.CreateRotationY(p.Yaw + turn) * Matrix4x4.CreateTranslation(p.Local + o + new Vector3(0, lift + 0.12f, 0));
                mesh.Instances.Add(new MeshInstance(piece, local * frame));
            }
    }

    /// <summary>
    /// A pile in a stop's yard: where (world, and in its building's frame), facing, and what's in it; and where its ends lie
    /// (world): a rack of rail's 4.6 m out either way along it, a stack of ties' 3 m to its right and 1.5 m to its left,
    /// anything else's where it stands.
    /// </summary>
    public readonly record struct YardPile(Double3 World, Vector3 Local, float Yaw, string Kind, int Variant, Double3 EndA, Double3 EndB);

    /// <summary>
    /// The piles round building <paramref name="index"/> of stop <paramref name="f"/> (<see cref="YardLeavings"/>'s spots,
    /// the ones a track runs by left out): no eye, so a test can hold them to the tracks.
    /// </summary>
    public static IEnumerable<YardPile> YardPiles(RailLine line, Route route, RouteFeature f, int index, float valleyDepth)
    {
        var stop = f.Stop!;
        var b = stop.Buildings[index];
        if (b.Kind is not (BuildingKind.Shed or BuildingKind.Hero or BuildingKind.GoodsShed or BuildingKind.Powerhouse or BuildingKind.Barn
            or BuildingKind.Outbuilding or BuildingKind.Station or BuildingKind.SignalBox or BuildingKind.LampRoom or BuildingKind.WaterTower))
            yield break;
        // A crane's runway through a shed is the yard's to keep clear (its castings lie there): no piles at that shed. Nor at
        // an open barn or shed (note 417): it's walked into, and a gable's length of rail laid along it reached in through the wall.
        if (CraneBay(stop, index) is not null || Sim.Run.StopWalls.OpenShed(b))
            yield break;
        double along = f.Start + b.S;
        var at = Footing(line, route, f, b, valleyDepth);
        // The building's frame about its own footing (eye there), so a spot's world position is that plus the spot.
        var frame = Basis(line.Sample(Math.Clamp(along, 0, line.Length)).Tangent, at, at, (float)-b.Yaw);
        // Its doors' side: a works shed's toward the track it serves (as Building draws it), anything else's toward the line.
        int door = b.Kind is BuildingKind.Shed or BuildingKind.Hero
            ? (b.Tracks.Count > 0 ? stop.Tracks[b.Tracks[0]].FaceStart.D : 0) >= b.D ? 1 : -1
            : b.D >= 0 ? -1 : 1;
        float back = -door, w = (float)b.Width / 2, l = (float)b.Length / 2;
        int seed = (int)Math.Round(f.Start);
        var deck = YardDeck(seed);
        // Round it: each gable end at both corners, and along both long walls a spot every YardSpacing; the ones a track
        // runs by (the doors' side, mostly) are left out below.
        var spots = new List<(Vector3 At, float Yaw)>();
        foreach (float x in new[] { back, -back })
        {
            spots.Add((new Vector3(x * w * 0.5f, 0, -(l + YardStandOff)), 0));
            spots.Add((new Vector3(x * w * 0.5f, 0, l + YardStandOff), MathF.PI));
        }
        int piles = Math.Max(1, (int)(2 * l / YardSpacing));
        foreach (float x in new[] { back, -back })
            for (int i = 0; i < piles; i++)
                spots.Add((new Vector3(x * (w + YardStandOff), 0, -l + (i + 0.5f) * 2 * l / piles), x > 0 ? -MathF.PI / 2 : MathF.PI / 2));
        for (int j = 0; j < spots.Count; j++)
        {
            int slot = index * 32 + j;
            if (Hash(slot * 1.93f + seed * 0.017f) > YardChance)
                continue;
            // Clear of every track (a shed stands between two, P5: its back wall can face the next one).
            var world = at + ToDouble(Vector3.Transform(spots[j].At, frame));
            if (!ClearOfTracks(line, world, along))
                continue;
            // And its ends (note 778): a rack of rail at a gable end lies along the building, and where the building stands
            // end on to a track its 9 m lengths reached out over the rails (frontier:7's and local:3's stops). Not there. A
            // stack of ties leans one out 3 m to its right (note 780), as far as its spot is kept from a track.
            float yaw = spots[j].Yaw + (Hash(slot * 2.71f) - 0.5f) * 0.5f;
            string kind = YardKinds[deck[slot % deck.Length]];
            var turned = Matrix4x4.CreateRotationY(yaw);
            Double3 Out(Vector3 local) => ToDouble(Vector3.TransformNormal(Vector3.TransformNormal(local, turned), frame));
            var (endA, endB) = kind switch
            {
                "rails" => (world + Out(Vector3.UnitZ * RailRackReach), world - Out(Vector3.UnitZ * RailRackReach)),
                "ties" => (world + Out(Vector3.UnitX * TieLeanReach), world - Out(Vector3.UnitX * TieStackReach)),
                _ => (world, world),
            };
            if ((endA != world && !ClearOfTracks(line, endA, along)) || (endB != world && !ClearOfTracks(line, endB, along)))
                continue;
            int variant = (int)(Hash(slot * 4.17f + seed * 0.031f) * 6);
            // And the whole of it (note 868): every piece's footprint (footprints.json) clear of the loading gauge, not only its
            // middle. A sandbagged wall 3.2 m off by its middle laid its turned-in end and its drums onto the main line's rails
            // by a halt's lamp room, 6.5 m out (deepTerritory:3, once the abandoned villages were on).
            if (route.Plan?.Rules.Footprints is { } prints && !FootprintClear(line, prints, kind, variant, world, Out, along))
                continue;
            yield return new YardPile(world, spots[j].At, yaw, kind, variant, endA, endB);
        }
    }

    /// <summary>Where a stop's building stands (world): its centre, on the levelled ground, as Building sets it.</summary>
    static Double3 Footing(RailLine line, Route route, RouteFeature f, StopBuilding b, float valleyDepth) =>
        Sim.Run.Run.StopWorld(line, f, b.Centre, Ground(route, f.Start + b.S, (float)b.D, valleyDepth) - 0.15);

    static Double3 ToDouble(Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>
    /// Whether a pile at <paramref name="p"/> (world) stands at least <see cref="YardTrackClear"/> from the centre of every
    /// track near it: the main line about <paramref name="near"/>, and every branch (a yard's tracks) whose span is.
    /// </summary>
    public static bool ClearOfTracks(RailLine line, Double3 p, double near) => Clear(line, p, near, YardTrackClear);

    static bool Clear(RailLine line, Double3 p, double near, double clear)
    {
        double clear2 = clear * clear;
        bool Close(Double3 q) => (q.X - p.X) * (q.X - p.X) + (q.Z - p.Z) * (q.Z - p.Z) < clear2;
        for (double s = Math.Max(0, near - 60); s <= Math.Min(line.Length, near + 60); s += 2)
            if (Close(line.Sample(s).Position))
                return false;
        foreach (var b in line.Branches)
        {
            if (b.Definition.Toe > near + 400 || b.Definition.Toe + b.Definition.Length < near - 400)
                continue;
            for (double s = 0; s <= b.Local.Length; s += 2)
                if (Close(b.Local.Sample(s).Position))
                    return false;
        }
        return true;
    }

    /// <summary>How far a pile stands from the centre of any track (m): the loading gauge's half and a step.</summary>
    public const double YardTrackClear = 3.2;

    /// <summary>How far any of a pile's pieces stands from the centre of any track (m): the loading gauge's half and a little.</summary>
    public const double YardFootprintClear = 1.8;

    /// <summary>
    /// Whether every piece of a pile of <paramref name="kind"/> (its cluster, <see cref="Sim.Run.Leavings.Cluster"/>), each of
    /// its footprint's boxes taken as the circle round it, stands <see cref="YardFootprintClear"/> from every track near it.
    /// </summary>
    static bool FootprintClear(RailLine line, IReadOnlyDictionary<string, double[][][]> prints, string kind, int variant, Double3 world,
        Func<Vector3, Double3> turn, double near)
    {
        foreach (var piece in Sim.Run.Leavings.Cluster(kind, variant))
        {
            if (!prints.TryGetValue(piece.Name, out var variants) || variants.Length == 0)
                continue;
            foreach (var box in variants[piece.Variant % variants.Length])
            {
                double c = DMath.Cos(piece.Turn), sn = DMath.Sin(piece.Turn);
                // The box's middle in the pile's frame (x across, z along): turned with the piece, then off the pile's spot.
                var at = world + turn(new Vector3((float)(piece.X + box[0] * c + box[1] * sn), 0, (float)(piece.Z + box[1] * c - box[0] * sn)));
                if (!Clear(line, at, near, YardFootprintClear + double.Hypot(box[2], box[3])))
                    return false;
            }
        }
        return true;
    }

    /// <summary>How far a rack of rail reaches either way from its middle (m): half its 9 m lengths and a bent one's swing (JunkKit.Rails).</summary>
    const float RailRackReach = 4.6f;

    /// <summary>How far a stack of ties reaches (m, JunkKit.Ties): its leant tie 3 m out to its right, its stack 1.44 m to its left.</summary>
    const float TieLeanReach = 3.05f, TieStackReach = 1.5f;

    /// <summary>The kinds a yard's piles are dealt from (the leavings' that belong by a building, not out on the line).</summary>
    static readonly string[] YardKinds = ["ties", "rails", "drums", "reel", "wheelset", "coal", "stores", "freight", "sandbags"];

    /// <summary>How far out from a building's wall or gable its piles stand (m).</summary>
    const float YardStandOff = 1.3f;

    /// <summary>How far apart the piles along a building's back wall are (m).</summary>
    const float YardSpacing = 7f;

    /// <summary>How many of a building's spots have a pile in them.</summary>
    const float YardChance = 0.7f;

    /// <summary>A stop's deck of yard kinds: every kind once, in its own order (the next building takes the next card).</summary>
    static int[] YardDeck(int seed)
    {
        var deck = Enumerable.Range(0, YardKinds.Length).ToArray();
        new Random(unchecked(seed * 7919 + 104729)).Shuffle(deck);
        return deck;
    }
}
