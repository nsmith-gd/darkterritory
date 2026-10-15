using Ballast;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Sim.Run;

public sealed partial class StopWalls
{
    // The coaling towers' keys (1 up) and where each stands; and those brought down tonight (note 758).
    List<(int Key, Double3 At)> _towers = [];
    HashSet<int> _felled = [];

    /// <summary>
    /// The coaling towers beside the line, solid (note 758; the director, 14 Oct, `docs/playtest/2026-10-14.md` line 8: "We're
    /// actually clipping a little bit through some of the ... things at the station"). A main-line facility has no stop of its
    /// own, so nothing stood where the art draws its tower (facility_pieces.py <c>coaling_tower</c>, 7 m off the line on the
    /// facility's side: four posts 9 m along and 7 m across on concrete footings, X-braced on every face from 0.4 m up, a
    /// ladder up its back): the crew walked through it. Now each face of the trestle is a wall to its top, with its footings
    /// and its ladder. A trestle doesn't hold
    /// a creature out (<see cref="Wall.Trestle"/>), and stands only until Tower Jaw brings it down (<see cref="Fell"/>).
    /// </summary>
    /// <param name="facilities">The night's facilities, in order (<see cref="Run.Facilities"/>): a tower's key is its index + 1.</param>
    public void AddTowers(IReadOnlyList<RouteFeature> facilities, RailLine line)
    {
        for (int i = 0; i < facilities.Count; i++)
        {
            var f = facilities[i];
            if (f.Facility != FacilityKind.CoalingTower || f.Start < 0 || f.End > line.Length)
                continue;
            int key = i + 1;
            double side = f.Side == 0 ? 1 : f.Side;
            var t = line.Sample((f.Start + f.End) / 2);
            var along = new Double3(t.Tangent.X, 0, t.Tangent.Z).Normalized;
            var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
            double ground = t.Position.Y;
            // In the model's frame (x along, y across, -y toward the line; StructureKit turns it to face the line either side).
            Wall Part(double x, double y, double halfX, double halfY, double top)
            {
                var centre = t.Position + right * (side * (TowerOut + y)) - along * (side * x);
                return new Wall(centre with { Y = 0 }, along, halfX, halfY, ground - 3, ground + top) { Trestle = true, Tower = key };
            }
            _towers.Add((key, t.Position + right * (side * TowerOut)));
            // The four faces, post to post, to the trestle's top: the braces' lattice from 0.4 m up and the girts above.
            Add(Part(0, -TowerHalfAcross, TowerHalfAlong + FaceHalf, FaceHalf, TowerTop));
            Add(Part(0, TowerHalfAcross, TowerHalfAlong + FaceHalf, FaceHalf, TowerTop));
            foreach (double x in new[] { -TowerHalfAlong, TowerHalfAlong })
                Add(Part(x, 0, FaceHalf, TowerHalfAcross, TowerTop));
            // The posts' concrete footings, proud of the faces at the corners.
            foreach (double x in new[] { -TowerHalfAlong, TowerHalfAlong })
                foreach (double y in new[] { -TowerHalfAcross, TowerHalfAcross })
                    Add(Part(x, y, FootingHalf, FootingHalf, FootingTop));
            // The ladder up its back, off the far face. (The lever's slim stand by the track is left to walk through: the one who
            // works it stands at it, StopCrew's shunter just beyond it from the track.)
            Add(Part(LadderAlong, TowerHalfAcross + LadderOut, 0.3, 0.2, TowerTop));
        }
    }

    /// <summary>
    /// A facility's buildings beyond its machinery, solid where the art stands them (note 758): the kit StructureKit.Facility
    /// draws down its spur (a main-line one's beside the line), its sheds and silos, stacks and works, each on the ground it
    /// covers at a crewmate's height (footprints.json's <see cref="FacilityPiece"/>, measured off the drawn kit, so what's seen
    /// is what's walked into). Stood as GreyboxScene stands the kit: down its spur 25 m short of its site's middle, 4 m out on
    /// its side. A night whose plan carries no footprints (a legacy route) has none.
    /// </summary>
    public void AddFacilities(Route.Route route, RailLine line, IReadOnlyList<Site?> sites)
    {
        if (route.Plan?.Rules.Footprints is not { Count: > 0 } footprints)
            return;
        foreach (var f in route.Features)
        {
            if (f.Kind != FeatureKind.Facility || f.Facility is not { } kind || kind == FacilityKind.CoalingTower
                || !footprints.TryGetValue(FacilityPiece(kind), out var variants))
                continue;
            var spur = line.Branches.FirstOrDefault(b => b.Kind == BranchKind.Spur && f.Contains(b.Toe));
            RailLine on;
            double mid, side, push;
            if (spur is not null)
            {
                double layout = sites.FirstOrDefault(s => s?.Spur == spur.Index)?.Mid ?? spur.Local.Length - 45;
                (on, mid, side, push) = (spur.Local, layout - KitBack, spur.Side, KitOut);
            }
            else
                (on, mid, side, push) = (line, (f.Start + f.End) / 2, f.Side, 0);
            side = side == 0 ? 1 : side;
            var boxes = variants[side < 0 ? 0 : Math.Min(1, variants.Length - 1)];
            var t = on.Sample(Math.Clamp(mid, 0, on.Length));
            var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
            var along = new Double3(t.Tangent.X, 0, t.Tangent.Z).Normalized;
            var at = t.Position + right * (side * push);
            // The kit's frame: x out to the right of the track, z back down it.
            foreach (var b in boxes)
                Add(new Wall((at + right * b[0] - along * b[1]) with { Y = 0 }, along, b[3], b[2], at.Y - 3, at.Y + b[5]));
        }
    }

    // Where GreyboxScene stands a spur facility's kit: back from its site's middle, out from its track. Not design numbers: the art's.
    const double KitBack = 25, KitOut = 4;

    /// <summary>
    /// A coaling tower brought down (Tower Jaw's wreck lying within <paramref name="within"/> of where it stood): its walls stand
    /// no more, the rest of the night. From the wreck's replicated place, so the host as it falls and a client from the snapshot
    /// that carries it come to the same walls.
    /// </summary>
    public void Fell(Double3 wreck, double within = 15)
    {
        foreach (var (key, at) in _towers)
            if (((at - wreck) with { Y = 0 }).Length <= within)
                _felled.Add(key);
    }

    /// <summary>The sim's name for a facility's kit in footprints.json (its variants: 0 its left side, 1 its right).</summary>
    public static string FacilityPiece(FacilityKind kind) => "facility" + kind;

    /// <summary>Whether coaling tower <paramref name="key"/> (its facility's index + 1) has been brought down tonight.</summary>
    public bool Felled(int key) => _felled.Contains(key);

    // The drawn tower's dimensions (facility_pieces.py coaling_tower; StructureKit stands it TowerOut off the line). Not
    // design numbers: the model's.
    const double TowerOut = 7, TowerHalfAlong = 4.5, TowerHalfAcross = 3.5, TowerTop = 12, FaceHalf = 0.3;
    const double FootingHalf = 0.55, FootingTop = 0.6, LadderAlong = 2.0, LadderOut = 0.15;
}
