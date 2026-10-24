using Ballast;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Sim.Stops;

/// <summary>
/// Holdouts and lairs (GDD App. D.4, B.6, B.8; level-design Part H): placed last, into a stop that's otherwise built,
/// from their own seeds so they never move anything else. A Holdout is searched for, not laid out: candidates are drawn
/// and the first that meets every one of D.4's rules is kept (the rules are the level design; any spot that meets them
/// is a good one). A stop where none does is rerolled.
/// </summary>
public static partial class StopGenerator
{
    /// <summary>Where the consist stops to work a stop: the middle of its first track's loading face, or the halt.</summary>
    static Pt StopPointOf(StopDraft g, Pt? halt, double zone)
    {
        if (g.Tracks.FirstOrDefault(tr => tr.Primary) is { } primary)
            return new Plan.Walker(primary.Path).At(primary.Loading).P;
        return halt ?? new Pt(zone / 2, 0);
    }

    /// <param name="walk">Walking distances from the consist over the stop as built (before its Holdouts: they're small, and
    /// the checks walk the finished stop again).</param>
    static List<StopHoldout> PlaceHoldouts(StopDraft g, Dice R, StopTuning t, in StopContext cx, Pt stopPoint, Pt? halt, StopWalk.Field walk)
    {
        var h = t.Holdouts;
        var placed = new List<StopHoldout>();
        int haltSide = halt is { } hs ? Math.Sign(hs.D) : 1;
        bool hasYard = g.Tracks.Count > 0;
        if (hasYard)
        {
            double pad = g.Tracks.Max(tr => tr.FaceEnd.S) - g.Tracks.Min(tr => tr.Toe);
            int count = pad >= h.SecondPadScale || cx.Facility == FacilityKind.Switchyard ? 2 : 1;
            string kind = cx.Facility is { } fk ? char.ToLowerInvariant(fk.ToString()[0]) + fk.ToString()[1..] : "";
            for (int i = 0; i < count; i++)
            {
                // D.4 types: a prison car where the kind prefers one (more often), a shelter otherwise; always the
                // portal lamp room at a mine head.
                double car = h.Prefers.Contains(kind) ? h.Preferred : h.PrisonCar;
                var type = cx.Facility == FacilityKind.MineHead ? BuildingKind.LampRoom
                    : R.Chance(car) ? BuildingKind.PrisonCar : ShelterOf(R, h);
                if (Place(g, R, t, cx, stopPoint, HoldoutSite.Facility, type, placed, halt, haltSide, walk) is { } ho)
                    placed.Add(ho with { Second = i > 0 });
            }
        }
        else if (halt is { } hp)
        {
            // A village halt (ARCHITECTURE §8): the halt's lockup, or a shelter in the village's station footprint.
            var type = R.Chance(h.VillageLockup) ? BuildingKind.Lockup : ShelterOf(R, h);
            var site = type == BuildingKind.Lockup ? HoldoutSite.Halt : HoldoutSite.Village;
            if (Place(g, R, t, cx, stopPoint, site, type, placed, hp, haltSide, walk) is { } ho)
                placed.Add(ho);
        }
        return placed;
    }

    static BuildingKind ShelterOf(Dice R, HoldoutPlacementTuning h) =>
        R.Pick<BuildingKind>(h.Shelters);

    /// <summary>
    /// One Holdout of type <paramref name="type"/> (note 650; the director, the inbox, 12 Oct: "Holdouts should never be on
    /// the line but should be essentially right next to the line. Minimum rescue time"): beside the track the consist stands
    /// on (a facility's first track at its loading face, else the main line), within <see cref="HoldoutPlacementTuning.Along"/>
    /// of where it stops, its near face <see cref="HoldoutPlacementTuning.Beside"/> off the track's centreline. A halt's
    /// lockup stands behind its platform as before. Candidates are drawn so and the first that meets D.4 kept.
    /// </summary>
    static StopHoldout? Place(StopDraft g, Dice R, StopTuning t, in StopContext cx, Pt stopPoint, HoldoutSite site, BuildingKind type,
        List<StopHoldout> placed, Pt? halt, int haltSide, StopWalk.Field walk)
    {
        var h = t.Holdouts;
        var board = new Pt(-(site == HoldoutSite.Facility ? h.Approach.Facility : h.Approach.Halt), 0);
        var rescue = RescueTrack(g, stopPoint);
        // Any of the yard's tracks, or the main line, near the consist will do when the first track's side is full (a
        // switchyard's second Holdout).
        var others = g.Tracks.Where(tr => !tr.Primary).Select(tr => new Plan.Walker(tr.Path))
            .Append(new Plan.Walker([new Pt(0, 0), new Pt(g.ZoneLength, 0)])).ToList();
        double[] size = h.Size(type);
        double length = type == BuildingKind.PrisonCar ? h.Siding : size[0], width = type == BuildingKind.PrisonCar ? size[1] + 1 : size[1];

        for (int k = 0; k < h.Candidates; k++)
        {
            Pt c;
            double yaw = 0;
            if (type == BuildingKind.Lockup)
            {
                // Behind the platform's back edge, within its length.
                var hp = halt!.Value;
                c = new Pt(hp.S + R.Range(-t.Village.Halt.Length / 2, t.Village.Halt.Length / 2),
                    haltSide * (Math.Abs(hp.D) + 2.2 + R.Range(h.LockupBehind) + width / 2));
            }
            else
            {
                // Beside the consist's track, along it from where the consist stops, on either side; every other candidate
                // beside another track near it (Sited keeps it within Along of the consist).
                double off = R.Range(h.Beside) + width / 2;
                var other = R.Pick(others);
                var (track, at) = k % 2 == 0 ? (rescue.Walker, rescue.Stop + R.Range(-h.Along, h.Along)) : (other, R.Range(0, other.Total));
                var (p, tan) = track.At(Math.Clamp(at, 0, track.Total));
                c = p + tan.Normal * (R.Sign() * off);
                yaw = DMath.Atan2(tan.D, tan.S);
            }
            var b = new StopBuilding(type, site == HoldoutSite.Facility ? StopZone.Yard : StopZone.Village, c.S, c.D, length, width, yaw) { Variant = R.Int(0, 3) };
            var ho = Judge(g, t, b, site, stopPoint, board, walk);
            if (ho is null)
                continue;
            int index = g.Add(b);
            var siding = type == BuildingKind.PrisonCar ? new List<Pt> { Plan.World(b, -length / 2, 0), Plan.World(b, length / 2, 0) } : [];
            return ho with { Index = placed.Count, Building = index, Siding = siding };
        }
        return type is BuildingKind.SignalBox or BuildingKind.WaterTower ? Place(g, R, t, cx, stopPoint, site, BuildingKind.LampRoom, placed, halt, haltSide, walk) : null;
    }

    /// <summary>The track the consist stands on to work a stop, and how far along it it stops (note 650): a facility's first
    /// track at its loading face, else the main line at the stop point.</summary>
    static (Plan.Walker Walker, double Stop) RescueTrack(StopDraft g, Pt stopPoint)
    {
        if (g.Tracks.FirstOrDefault(tr => tr.Primary) is { } primary)
            return (new Plan.Walker(primary.Path), primary.Loading);
        var main = new Plan.Walker([new Pt(0, 0), new Pt(g.ZoneLength, 0)]);
        return (main, Math.Clamp(stopPoint.S, 0, g.ZoneLength));
    }

    /// <summary>
    /// D.4's rules for a candidate, in order of cost: it fits; it's where its site says; it can be walked to from the
    /// consist; its lamp is seen from the approach. Null if it fails any.
    /// </summary>
    static StopHoldout? Judge(StopDraft g, StopTuning t, StopBuilding b, HoldoutSite site, Pt stopPoint, Pt board, StopWalk.Field walk)
    {
        var h = t.Holdouts;
        if (!g.Fits(b, new Fit(Gap: 3, Rail: b.Kind == BuildingKind.PrisonCar ? 3.5 : 3, Road: 2)))
            return null;
        if (!Sited(h, b, site, stopPoint, g) || site == HoldoutSite.Facility && !ClearOfTheLoading(h, b, g))
            return null;
        var door = DoorOf(b, stopPoint);
        if (walk.To(door) is not { } metres)
            return null;
        var lamp = LampOf(b, board);
        // (The candidate isn't in the draft yet, so it can't hide its own lamp.)
        if (!StopWalk.Seen(board, lamp, g.Buildings, -1, g.ZoneLength))
            return null;
        var kind = b.Kind switch { BuildingKind.PrisonCar => HoldoutKind.PrisonCar, BuildingKind.Lockup => HoldoutKind.Lockup, _ => HoldoutKind.Shelter };
        return new StopHoldout(0, kind, site, -1, door, lamp) { Walk = Math.Round(metres, 1) };
    }

    /// <summary>
    /// D.4's placement (note 650): a halt's lockup within <see cref="HoldoutPlacementTuning.HaltDistance"/> of the main line;
    /// any other beside a track (its near face within the far end of <see cref="HoldoutPlacementTuning.Beside"/>) and within
    /// <see cref="HoldoutPlacementTuning.Along"/> of where the consist stops.
    /// </summary>
    internal static bool Sited(HoldoutPlacementTuning h, StopBuilding b, HoldoutSite site, Pt stopPoint, StopDraft g)
    {
        if (site == HoldoutSite.Halt)
            return Plan.Corners(b).Max(p => Math.Abs(p.D)) <= h.HaltDistance;
        return g.TouchesRail(b, h.Beside[1]) && Pt.Distance(b.Centre, stopPoint) <= h.Along + b.Length / 2;
    }

    /// <summary>Clear of every loading container by <see cref="HoldoutPlacementTuning.LoadingClear"/> (note 650): out of the hands' way.</summary>
    internal static bool ClearOfTheLoading(HoldoutPlacementTuning h, StopBuilding b, StopDraft g) =>
        g.Containers.Where(c => c.Zone == StopZone.Yard).All(c => Pt.Distance(c.At, b.Centre) >= h.LoadingClear);

    /// <summary>A Holdout's door: on the face turned most towards the consist, just outside it.</summary>
    internal static Pt DoorOf(StopBuilding b, Pt towards)
    {
        var (x, y) = Plan.Local(towards, b);
        // A prison car's door is in its side, as any car's (note 650: beside the track, the consist can be off its end).
        if (b.Kind == BuildingKind.PrisonCar)
            return Plan.World(b, 0, (y >= 0 ? 1 : -1) * (b.Width / 2 + 1.2));
        return Math.Abs(x) / b.Length > Math.Abs(y) / b.Width
            ? Plan.World(b, Math.Sign(x) * (b.Length / 2 + 1.2), 0)
            : Plan.World(b, 0, Math.Sign(y) * (b.Width / 2 + 1.2));
    }

    /// <summary>Its lamp: on the corner nearest the approach board, where the train coming in sees it.</summary>
    internal static Pt LampOf(StopBuilding b, Pt board) => Plan.Corners(b, 0.4).MinBy(p => Pt.Distance(p, board));

    /// <summary>
    /// Where the outside creatures live (GDD B.6, B.8): the Ribbits' warrens on open ground in the yard and the village,
    /// the Gaunt asleep in the building furthest from the train, Followers on the facility's loading ground, a child
    /// calling from the open beyond the stop's edge, the Grumbler on each yard gantry, and the Whistler's nest out on the
    /// empty side.
    /// </summary>
    static List<StopLair> PlaceLairs(StopDraft g, Dice R, StopTuning t, StopTier tt, Pt stopPoint, StopWalk.Field walk)
    {
        var lt = t.Lairs;
        var lairs = new List<StopLair>();
        bool Open(Pt p, double clear) =>
            Math.Abs(p.D) <= g.MaxLateral - 5 && p.S >= 5 && p.S <= g.ZoneLength - 5
            && !g.Buildings.Any(b => Plan.Distance(p, b) < clear)
            && !g.TouchesRail(new StopBuilding(BuildingKind.Well, StopZone.Yard, p.S, p.D, 1, 1, 0), 4);

        // Ribbits: open ground in each zone, clear of buildings and track, away from the train and the main line.
        var w = lt.Warren;
        void Warrens(StopZone zone, double s0, double s1, double d0, double d1)
        {
            int want = R.Int(tt.Warrens);
            for (int tries = 0, got = 0; tries < 80 && got < want; tries++)
            {
                var p = new Pt(R.Range(s0, s1), R.Range(Math.Min(d0, d1), Math.Max(d0, d1)));
                if (Math.Abs(p.D) < w.FromMain || Pt.Distance(p, stopPoint) < w.FromTrain || !Open(p, w.Clear + w.Radius)
                    || lairs.Any(l => l.Kind == LairKind.Warren && Pt.Distance(l.At, p) < 3 * w.Radius) || walk.To(p) is null)
                    continue;
                lairs.Add(new StopLair(LairKind.Warren, zone, p, w.Radius));
                got++;
            }
        }
        if (g.Tracks.Count > 0)
        {
            var near = g.Tracks.Where(tr => !tr.Across).ToList();
            int side = near[0].Side;
            Warrens(StopZone.Yard, g.Tracks.Min(tr => tr.Toe), g.Tracks.Max(tr => tr.FaceEnd.S), side * w.FromMain, side * (near.Max(tr => tr.Offset) + 30));
        }
        var houses = g.Buildings.Select((b, i) => (b, i)).Where(x => x.b.Zone == StopZone.Village && x.b.Kind is BuildingKind.House or BuildingKind.Barn or BuildingKind.Outbuilding).ToList();
        if (houses.Count > 0)
            Warrens(StopZone.Village, houses.Min(x => x.b.S) - 20, houses.Max(x => x.b.S) + 20, houses.Min(x => x.b.D) - 20, houses.Max(x => x.b.D) + 20);

        // The Gaunt: asleep in the building furthest from the train, on foot (a village house before a yard shed).
        // (Not in a house of floors, note 1005: its nest is a ground floor's, and the Gaunt's not been taught the stairs.)
        var roosts = houses.Any(x => !Run.StopWalls.Storeyed(x.b)) ? houses.Where(x => !Run.StopWalls.Storeyed(x.b)).ToList()
            : g.Buildings.Select((b, i) => (b, i)).Where(x => x.b.Kind is BuildingKind.Shed or BuildingKind.Hero).ToList();
        var roost = roosts.Select(x => (x.b, x.i, m: walk.To(DoorOf(x.b, stopPoint)))).Where(x => x.m is not null).OrderByDescending(x => x.m).FirstOrDefault();
        if (roost.b is not null)
        {
            // In an open house of parts (an L, a cross, a pair) its middle can be in a wall: the nest's in the middle of its
            // biggest stretch of floor (note 326). Anywhere else, the building's middle, as before.
            var at = roost.b.Centre;
            if (roost.b.Open && Run.StopWalls.Composite(roost.b))
            {
                var (x0, y0, x1, y1) = Run.StopWalls.Outline(roost.b).Cells.MaxBy(c => (c.X1 - c.X0) * (c.Y1 - c.Y0));
                at = Plan.World(roost.b, (x0 + x1) / 2, (y0 + y1) / 2);
            }
            lairs.Add(new StopLair(LairKind.GauntRoost, roost.b.Zone, at, Math.Max(roost.b.Length, roost.b.Width) / 2, roost.i));
        }

        // Followers: the facility's loading ground, spread out (the nearest loading first, then the furthest from those).
        var loading = g.Containers.Where(c => c.Zone == StopZone.Yard).ToList();
        if (loading.Count > 0)
        {
            var picks = new List<StopContainer> { loading.MinBy(c => Pt.Distance(c.At, stopPoint))! };
            while (picks.Count < lt.Followers.Most)
            {
                var next = loading.MaxBy(c => picks.Min(p => Pt.Distance(p.At, c.At)))!;
                if (picks.Min(p => Pt.Distance(p.At, next.At)) < 2 * lt.Followers.Radius)
                    break;
                picks.Add(next);
            }
            lairs.AddRange(picks.Select(c => new StopLair(LairKind.FollowerGround, StopZone.Yard, c.At, lt.Followers.Radius, c.Building)));
        }

        // The Soot Child: calling from the open beyond the built edge (the village's, else the yard's), in sight of the train
        // (the consist where it's working, or the cars waiting on the main line).
        var built = (houses.Count > 0 ? houses.Select(x => x.b) : g.Buildings.Where(b => b.Zone == StopZone.Yard)).ToList();
        if (built.Count > 0)
        {
            var mid = new Pt(built.Average(b => b.S), built.Average(b => b.D));
            for (int tries = 0; tries < 80; tries++)
            {
                double a = R.Range(0, 2 * Math.PI);
                var dir = new Pt(DMath.Cos(a), DMath.Sin(a));
                double edge = built.Max(b => (b.Centre - mid).S * dir.S + (b.Centre - mid).D * dir.D) + 8;
                var p = mid + dir * (edge + R.Range(lt.SootCall));
                if (Math.Abs(p.D) < t.Village.Halt.Offset + 6 || !Open(p, 4) || !(StopWalk.Seen(stopPoint, p, g.Buildings, -1, g.ZoneLength) || StopWalk.Seen(new Pt(Math.Clamp(p.S, 0, g.ZoneLength), 0), p, g.Buildings, -1, g.ZoneLength)))
                    continue;
                lairs.Add(new StopLair(LairKind.SootCall, houses.Count > 0 ? StopZone.Village : StopZone.Yard, p, 5));
                break;
            }
        }

        // The Grumbler on every yard gantry.
        foreach (var tr in g.Tracks)
            if (tr.Crane is { Bays: > 0 } rw)
                lairs.Add(new StopLair(LairKind.GrumblerPerch, StopZone.Yard, new Pt((rw.From + rw.To) / 2, tr.FaceStart.D), (rw.To - rw.From) / 2, Track: tr.Index));

        // The Whistler's nest: out on the side with the least built on it, abreast of where the train stands (note 314: it
        // snatches from the train's gaps and runs there).
        int empty = g.Buildings.Count(b => b.D > 0) <= g.Buildings.Count(b => b.D < 0) ? 1 : -1;
        double along = lt.WhistlerNestAlong;
        // Its run there in the open: every building stands solid now (note 279), the sheds and Holdouts too, so the way from the
        // front of the stopped train misses each by a metre (the Whistler keeps half a metre clear of a wall).
        var clear = g.Buildings.Select(b => b with { Length = b.Length + 2, Width = b.Width + 2 }).ToList();
        for (int tries = 0; tries < 60; tries++)
        {
            double s = along > 0
                ? Math.Clamp(stopPoint.S + R.Range(-along, along), 0, g.ZoneLength)
                : R.Range(g.ZoneLength * 0.15, g.ZoneLength * 0.85);
            var p = new Pt(s, empty * R.Range(lt.WhistlerNest));
            if (!Open(p, 6) || !new[] { 20.0, 50.0 }.All(back => StopWalk.Seen(new Pt(stopPoint.S - back, 0), p, clear, -1, g.ZoneLength)))
                continue;
            lairs.Add(new StopLair(LairKind.WhistlerNest, g.Tracks.Count > 0 ? StopZone.Yard : StopZone.Village, p, 4));
            break;
        }
        return lairs;
    }
}
