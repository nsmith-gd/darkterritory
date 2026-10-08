using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Bots;

/// <summary>
/// A crate hand working the rest of the yard on foot (GDD App. F.3: "work the yard together"; ARCHITECTURE §8 note 403).
/// Since queue #89 every siding has something beside it, and the crew only ever loaded the crates by its own site's stack
/// (<see cref="StopPlan.Loose"/>). Once those are in and a car still has room, a hand fetches the nearest crate lying loose
/// elsewhere in the yard on the train's working side, within <c>crew.yardReach</c> of where it stands and nobody else's
/// (<see cref="CrewCalls.ClaimSpot"/>, by the crate). It goes there round the sheds (<see cref="FootPath"/>), picks it up
/// and carries it back the same way to a car's steps, where its own way takes it in. It's out while it does
/// (<see cref="CrewCalls.Out"/>), so the driver waits. Before the village (note 326): the yard's certain, the village a
/// gamble. All through intent.
/// </summary>
public sealed partial class StopHand
{
    int _yardCrate = -1;
    bool _crateClaimed;

    /// <summary>A crate's key on the crew's calls: below zero, where a hiding spot's keys are above it.</summary>
    static int CrateKey(int body) => -(body + 1);

    /// <summary>Whether a hand is out fetching a crate from the yard (for tests and traces).</summary>
    public bool Fetching => _yardCrate >= 0;

    /// <summary>
    /// Off to the next crate lying loose in the yard (from <see cref="Carry"/>, the site's own crates in), and picking it
    /// up. Null when there's none for it (on to the village, or the doors and aboard).
    /// </summary>
    PlayerIntent? Yard(in PlayerState self, World world, StopPlan p)
    {
        // Just picked one up (our arms are full before we're slowed by it): it's ours, home with it.
        if (PlayerId is { } me && world.Bodies.CarriedBy(me) is not null)
            return null;
        var t = Crew(world);
        if (world.Run is not { } run || calls.Leaving || PlayerId is null || t.YardReach <= 0 || run.DawnIn < t.VillageDawnSpare
            || Roomiest(world, p, calls, member) < 0)
            return LetGoOfCrate();
        var train = world.Train;
        var here = PlayerMotor.WorldPosition(self, train);
        var crate = _yardCrate >= 0 ? world.Bodies.All.FirstOrDefault(b => b.Id == _yardCrate) : null;
        // Ours still lying where it was: on with it. Taken (by anyone) or stowed: the next.
        if (crate is null || crate.Carrier >= 0 || crate.Parent != PlayerState.World)
        {
            LetGoOfCrate();
            crate = NextCrate(world, p, here, t.YardReach);
            if (crate is null)
                return null;
        }
        var at = crate.Centre;
        // Off the car we've just put one in first, by its door or down off the roof: the way there's from the ground.
        if (self.Parent != PlayerState.World)
            return OffTheCar(self, world, p) ?? GetDown(self, train, p.Site.Side);
        // At it: face it and pick it up (ours, so it's carried home round the train if it lay across it).
        if ((Flat(here) - Flat(at)).Length < 1.0)
        {
            Claim(crate.Id);
            double yaw = DMath.Atan2(-(at.X - here.X), -(at.Z - here.Z));
            if (!Aligned(self, yaw))
                return new PlayerIntent { LookYaw = Turn(self, yaw) };
            Doing = "picking up a crate from the yard";
            return Press();
        }
        if (_yardCrate != crate.Id)
        {
            // Close in beside the train (at the foot of a car's steps, say): out from it first, square to the car, so the
            // way there doesn't start by walking back up the steps.
            if (OutFromTheTrain(self, train) is { } clear)
                return clear;
            // To the crate itself: the way ends as near it as a crewmate stands (it may lie in a shed's bay, by its wall).
            var path = FootPath.Plan(train, here, at);
            if (path is null || FootPath.Length(here, path) > t.YardReach * 1.4)
            {
                // No way there from here: another next tick.
                _cantReach.Add(CrateKey(crate.Id));
                Doing = "looking for a way to a crate";
                return new PlayerIntent();
            }
            Claim(crate.Id);
            _path = path;
            _pathAt = 0;
            _pathGoal = at;
        }
        Doing = "to a crate in the yard";
        return Follow(self, train, at);
    }

    /// <summary>
    /// The nearest crate lying loose in the yard that isn't the site's own (another siding's, or across the train from the
    /// steps: a way round the train's found now), in reach and nobody's.
    /// </summary>
    Physics.Body? NextCrate(World world, StopPlan p, Double3 here, double reach) =>
        world.Bodies.All.Where(b => b.Kind == Physics.BodyKind.Cargo && b.Parent == PlayerState.World && b.Carrier < 0 && b.Pbd.Asleep
                && !p.Loose(world, b) && (Flat(b.Centre) - Flat(here)).Length <= reach
                && !calls.SpotClaimed(CrateKey(b.Id), member) && !_cantReach.Contains(CrateKey(b.Id))
                && world.Run?.Groaning(b.Centre, 0.5) is null)
            .OrderBy(b => (Flat(b.Centre) - Flat(here)).Length).ThenBy(b => b.Id).FirstOrDefault();

    /// <summary>
    /// Standing within a car's steps' reach of its side (where <see cref="FootPath"/> keeps clear of): a step straight out
    /// from it, else null.
    /// </summary>
    PlayerIntent? OutFromTheTrain(in PlayerState self, TrainOnLine train)
    {
        var here = PlayerMotor.WorldPosition(self, train);
        foreach (var f in train.Frames)
        {
            var l = f.ToLocal(here with { Y = f.Origin.Y });
            var b = f.Shape.Bounds;
            if (l.Z < b.Min.Z - 1 || l.Z > b.Max.Z + 1 || l.X < b.Min.X - 1.4 || l.X > b.Max.X + 1.4)
                continue;
            int side = l.X >= 0 ? 1 : -1;
            return Head(self, f.ToWorld(new Double3(side * (Math.Max(b.Max.X, -b.Min.X) + 2.2), 0, l.Z)) with { Y = here.Y }, "out from the train");
        }
        return null;
    }

    /// <summary>On the site's side of its track: where the steps the crates go up are.</summary>
    static bool OnTheWorkingSide(StopPlan p, Double3 at)
    {
        double hint = p.Site.Mid;
        var s = p.Site.Track.Sample(p.Site.Track.Nearest(at, ref hint).Distance);
        return Math.Sign(Double3.Dot(at - s.Position, Double3.Cross(s.Tangent, Double3.Up))) == p.Site.Side;
    }

    /// <summary>
    /// With a crate from the yard in our arms: back round the sheds to the foot of the car's steps (<paramref name="foot"/>),
    /// where the crate hand's own way takes it up and in. Null once it's near enough.
    /// </summary>
    PlayerIntent? CrateHome(in PlayerState self, World world, StopPlan p, Physics.Body mine, Double3 foot)
    {
        var train = world.Train;
        if (_yardCrate < 0)
            return null;
        // It's ours now (the one we went for, or the one beside it the hands took): the next hand can have the next.
        if (_crateClaimed)
            calls.ReleaseSpot(member, CrateKey(_yardCrate));
        _crateClaimed = false;
        _yardCrate = mine.Id;
        var here = PlayerMotor.WorldPosition(self, train);
        if ((Flat(here) - Flat(foot)).Length < 8 && OnTheWorkingSide(p, here))
        {
            _path = null;
            return null;
        }
        Doing = "carrying a crate back from the yard";
        return Follow(self, train, foot);
    }

    /// <summary>A crate in the yard ours to fetch, said on the crew's calls; and we're out till it's in.</summary>
    void Claim(int crate)
    {
        if (_yardCrate == crate)
            return;
        LetGoOfCrate();
        _yardCrate = crate;
        calls.ClaimSpot(member, CrateKey(crate));
        _crateClaimed = true;
        calls.Out(member, true);
    }

    /// <summary>Done with the yard's crate we were after (or never had one): not ours any more. Always null.</summary>
    PlayerIntent? LetGoOfCrate()
    {
        if (_yardCrate >= 0 && _crateClaimed)
            calls.ReleaseSpot(member, CrateKey(_yardCrate));
        _yardCrate = -1;
        _crateClaimed = false;
        return null;
    }
}
