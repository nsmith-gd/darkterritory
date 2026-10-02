using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// GDD v1.4 App. D.12's bookmarks, as the host records them (note 176): a still at every GRAB start and every PUNISH, from
/// the nearest living crewmate with line of sight to the victim, else the victim's own eyes; one per crew member at a
/// derailment; the Stranded outro's frame; a dead player's own from the followed view, by intent. D.13 caps the automatic
/// ones (12) with derailment > PUNISH > GRAB. Each reaches every client as it's made, and the report puts it beside its line.
/// </summary>
public class BookmarkTests
{
    static readonly PlayerTuning P = Tuning.Player;

    sealed class Night
    {
        public readonly World World;
        public readonly TrainOnLine Train;
        public readonly List<(int Id, PlayerState State)> Crew = [];
        public readonly Route.Route Route;
        public double Speed;

        public Night(bool enemies = false)
        {
            Route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, 1);
            Train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 0.5)), Route.Build(), 3_000, Tuning.Boiler);
            World = new World(Train, Tuning.Combat);
            World.EnableBodies();
            World.EnableRun(Tuning.Run, Route, 600, authority: true);
            World.Run!.Resume(900, -1, Train.Boiler.Tender, 0);
            if (enemies)
                World.EnableEnemies(Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceSeconds = 1e9 } }, route: null, 1, crew: 3, authority: true);
            foreach (var (id, name) in new[] { (1, "Dave"), (2, "Priya"), (3, "Sam"), (4, "Okafor") })
                World.Names[id] = name;
        }

        public int Add(PlayerState s)
        {
            Crew.Add((Crew.Count + 1, s));
            return Crew.Count;
        }

        public PlayerState this[int id] => Crew[id - 1].State;

        public void Step(double seconds)
        {
            for (int t = 0; t < Math.Max(1, seconds * SimConstants.TickRate); t++)
            {
                Train.Dynamics.Velocity = Speed;
                World.BeginTick();
                for (int c = 0; c < Crew.Count; c++)
                {
                    var s = Crew[c].State;
                    World.CrewAct(ref s, default, Crew[c].Id);
                    Crew[c] = (Crew[c].Id, s);
                }
                World.Step(new TrainControls { Reverser = 1 });
                World.ApplyDamage(id => id >= 1 && id <= Crew.Count ? Crew[id - 1].State : null, (id, s) => Crew[id - 1] = (id, s), Crew.Select(c => c.Id).ToList());
                for (int c = 0; c < Crew.Count; c++)
                {
                    var s = Crew[c].State;
                    PlayerMotor.Step(ref s, default, Train, P, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
                    Crew[c] = (Crew[c].Id, s);
                }
                World.StepBodies(Crew);
                World.StepRun([.. Crew.Select(c => c.State)]);
            }
        }

        public Double3 Chest(int id) => PlayerMotor.WorldPosition(this[id], Train) + Double3.Up * World.Bookmarks.Tuning.ChestHeight;
    }

    [Fact]
    public void AGrabIsSeenByTheNearestCrewmateWithALineOfSightNotTheNearestBehindAWall()
    {
        var n = new Night();
        var shape = n.Train.Frames[3].Shape;
        int victim = n.Add(PlayerMotor.SpawnOnRoof(n.Train, 3, 0, P, 0.6));
        // Nearest of all, but on the ground on the far side of the car: its body is between them.
        var beside = n.Train.Frames[3].ToWorld(new Double3(-(shape.HalfWidth + 1), 0, -3));
        int blocked = n.Add(PlayerMotor.SpawnOnGround(beside, n.Train.Line, n.Train.Cars[3].FrontDistance, P));
        // Along the same roof, in plain view, farther.
        int seen = n.Add(PlayerMotor.SpawnOnRoof(n.Train, 3, shape.HalfLength - 0.5, P));
        // In the cab, far off and walled in.
        n.Add(PlayerMotor.SpawnInCab(n.Train, P));
        Double3 Eye(int id) => PlayerMotor.WorldPosition(n[id], n.Train) + Double3.Up * n.World.Bookmarks.Tuning.EyeHeight;
        Assert.True((Eye(blocked) - n.Chest(victim)).Length < (Eye(seen) - n.Chest(victim)).Length);

        var b = n.World.Bookmarks.Grab(n.World, victim, "Grabbed by the Dragger", n.Crew)!;
        Assert.Equal(BookmarkKind.Grab, b.Kind);
        Assert.Equal(seen, b.Viewer);
        Assert.Equal(victim, b.Victim);
        // From their eye, in the car they stand on, looking at the victim's chest.
        Assert.Equal(3, b.Frame);
        Assert.Equal(n[seen].Position.Y + n.World.Bookmarks.Tuning.EyeHeight, b.Eye.Y, 6);
        var f = n.Train.Frames[3];
        var toChest = (n.Chest(victim) - f.ToWorld(b.Eye)).Normalized;
        Assert.True(Double3.Dot(f.DirToWorld(b.Look), toChest) > 0.999);
        Assert.Equal(900, b.Seconds, 3);
        Assert.Contains("on the roof of car 3", b.Where);
    }

    [Fact]
    public void WithNobodyToSeeItTheStillIsTheVictimsOwnView()
    {
        var n = new Night();
        int victim = n.Add(PlayerMotor.SpawnOnRoof(n.Train, 3, 0, P) with { Yaw = 0.4, Pitch = -0.2 });
        n.Add(PlayerMotor.SpawnInCab(n.Train, P));
        // Over the witness range, in the open: too far off to have seen it at night.
        var far = n.Train.Line.Sample(n.Train.Dynamics.RearDistance - n.World.Bookmarks.Tuning.WitnessRange - 30);
        n.Add(PlayerMotor.SpawnOnGround(far.Position, n.Train.Line, n.Train.Dynamics.RearDistance - n.World.Bookmarks.Tuning.WitnessRange - 30, P));

        var b = n.World.Bookmarks.Grab(n.World, victim, "Grabbed by the Dragger", n.Crew)!;
        Assert.Equal(victim, b.Viewer);
        Assert.Equal(3, b.Frame);
        Assert.Equal(n[victim].Position + Double3.Up * n.World.Bookmarks.Tuning.EyeHeight, b.Eye);
        Assert.Equal(Bookmarks.Forward(0.4, -0.2), b.Look);
    }

    [Fact]
    public void ADraggersGrabAndItsPunishAreEachBookmarkedOnceAndSitBesideTheDeathTheyEndedIn()
    {
        var n = new Night(enemies: true) { Speed = 14 };
        var shape = n.Train.Frames[2].Shape;
        int victim = n.Add(PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P, shape.HalfWidth - Tuning.Enemies.Draggers.GrabRange * 0.5));
        int witness = n.Add(PlayerMotor.SpawnOnRoof(n.Train, 2, -5, P));
        var dragger = n.World.AddEnemy(id => Dragger.Under(id, n.Train, 2, 1, 0));
        for (int i = 0; i < 8 * SimConstants.TickRate && dragger.Phase != SpinePhase.Grab; i++)
            n.Step(SimConstants.TickSeconds);
        Assert.Equal(SpinePhase.Grab, dragger.Phase);
        var grab = Assert.Single(n.World.Bookmarks.All);
        Assert.Equal((BookmarkKind.Grab, victim, witness), (grab.Kind, grab.Victim, grab.Viewer));
        Assert.Equal("Grabbed by the Dragger", grab.What);
        // Beside App. A.9's attribution record, one for one.
        Assert.Single(n.World.Attribution.Of(IncidentKind.Grab));

        n.Step(Tuning.Enemies.Draggers.HangSeconds + 1);
        Assert.False(n[victim].Alive);
        var punish = Assert.Single(n.World.Bookmarks.All, b => b.Kind == BookmarkKind.Punish);
        Assert.Equal((victim, witness), (punish.Victim, punish.Viewer));
        Assert.Equal(2, n.World.Bookmarks.All.Count);

        var report = n.World.Run!.Tally(n.World, [.. n.Crew.Select(c => c.State)]);
        var death = Assert.Single(report.Lines, l => l.Kind == IncidentKind.Death);
        Assert.Equal([grab.Id, punish.Id], death.Marks);
        Assert.Equal([grab.Id, punish.Id], report.Bookmarks.Select(b => b.Id));
    }

    [Fact]
    public void AGrabNobodyDiedOfHasALineOfItsOwnInItsPlace()
    {
        var n = new Night();
        int victim = n.Add(PlayerMotor.SpawnOnRoof(n.Train, 3, 0, P));
        int other = n.Add(PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P));
        n.World.Bookmarks.Grab(n.World, victim, "Grabbed by the Dragger", n.Crew);
        n.World.Run!.Resume(1000, -1, n.Train.Boiler.Tender, 0);
        n.World.Attribution.Add(IncidentLog.Death(n.World, other, n[other] with { Death = DeathCause.Eaten }, null, n.Crew));
        var r = n.World.Run.Tally(n.World, [.. n.Crew.Select(c => c.State)]);
        Assert.Equal(2, r.Lines.Count);
        Assert.Equal(IncidentKind.Grab, r.Lines[0].Kind);
        Assert.Equal("Dave", r.Lines[0].Who);
        Assert.StartsWith("Grabbed by the Dragger on the roof of car 3", r.Lines[0].Text);
        Assert.EndsWith("Got away.", r.Lines[0].Text);
        Assert.Single(r.Lines[0].Marks);
        Assert.Empty(r.Lines[1].Marks);
    }

    [Fact]
    public void ADerailmentBookmarksEachCrewMemberItTookFromTheirOwnEyesBesideItsLine()
    {
        var n = new Night();
        int driver = n.Add(PlayerMotor.SpawnInCab(n.Train, P) with { Yaw = 0.3 });
        int roof = n.Add(PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P));
        int dead = n.Add(PlayerMotor.SpawnOnRoof(n.Train, 3, 0, P) with { Health = 0, Death = DeathCause.Eaten });
        n.Train.Dynamics.Velocity = 19;
        n.World.Derail("test");
        n.Step(0.2);
        var r = n.World.Run!.Report!;
        var derails = n.World.Bookmarks.All.Where(b => b.Kind == BookmarkKind.Derail).ToList();
        // The two it took, not the one already dead and watching.
        Assert.Equal([driver, roof], derails.Select(b => b.Viewer));
        Assert.DoesNotContain(derails, b => b.Victim == dead);
        Assert.All(derails, b => Assert.Equal(b.Viewer, b.Victim));
        var line = Assert.Single(r.Lines, l => l.Kind == IncidentKind.Derailed);
        Assert.Equal(derails.Select(b => b.Id), line.Marks);
    }

    [Fact]
    public void AStrandingBookmarksTheOutrosLastFrameBesideItsLine()
    {
        var n = new Night();
        n.Add(PlayerMotor.SpawnInCab(n.Train, P));
        n.World.Bookmarks.End(n.World, RunEnd.Stranded, n.Crew);
        n.World.Bookmarks.End(n.World, RunEnd.Stranded, n.Crew);
        var b = Assert.Single(n.World.Bookmarks.All);
        Assert.Equal((BookmarkKind.Stranded, -1), (b.Kind, b.Viewer));
    }

    static Bookmark Mark(int id, BookmarkKind kind, double seconds, int victim = 1) => new(id, kind, seconds, victim, victim, 0, default, new Double3(0, 0, -1));

    [Fact]
    public void OverTheCapTheDerailmentKeepsItsStillsThenPunishesThenGrabsAndThoseBesideADeathFirst()
    {
        var t = new BookmarkTuning();
        var all = new List<Bookmark>();
        int id = 1;
        for (int i = 0; i < 10; i++)
            all.Add(Mark(id++, BookmarkKind.Grab, 100 + i));
        for (int i = 0; i < 5; i++)
            all.Add(Mark(id++, BookmarkKind.Punish, 200 + i));
        all.Add(Mark(id++, BookmarkKind.Manual, 250));
        for (int i = 0; i < 4; i++)
            all.Add(Mark(id++, BookmarkKind.Derail, 300, victim: i));
        // The last grab ended in a death; the rest were got away from.
        var fatal = all.Single(b => b.Kind == BookmarkKind.Grab && b.Seconds == 109);
        var kept = Bookmarks.Kept(all, t, b => b == fatal);
        Assert.Equal(t.AutoCap, kept.Count);
        Assert.Equal(4, kept.Count(b => b.Kind == BookmarkKind.Derail));
        Assert.Equal(5, kept.Count(b => b.Kind == BookmarkKind.Punish));
        Assert.DoesNotContain(kept, b => b.Kind == BookmarkKind.Manual);
        // Three grabs left: the fatal one, then the two earliest.
        Assert.Equal([100, 101, 109], kept.Where(b => b.Kind == BookmarkKind.Grab).Select(b => b.Seconds).Order());
        // In the order they were made.
        Assert.Equal(kept.Select(b => b.Id).Order(), kept.Select(b => b.Id));
    }

    [Fact]
    public void APunishIsBookmarkedOncePerEnemyAndVictimAndRecordingStopsAtItsLimit()
    {
        var n = new Night();
        int victim = n.Add(PlayerMotor.SpawnOnRoof(n.Train, 3, 0, P));
        var at = PlayerMotor.WorldPosition(n[victim], n.Train);
        Assert.NotNull(n.World.Bookmarks.Punish(n.World, 7, "Drift", victim, at, n.Crew));
        Assert.Null(n.World.Bookmarks.Punish(n.World, 7, "Drift", victim, at, n.Crew));
        n.World.Bookmarks.Tuning = n.World.Bookmarks.Tuning with { RecordLimit = 3 };
        for (int e = 8; e < 20; e++)
            n.World.Bookmarks.Punish(n.World, e, "Drift", victim, at, n.Crew);
        Assert.Equal(3, n.World.Bookmarks.All.Count);
        // The night's end is always recorded.
        n.World.Bookmarks.End(n.World, RunEnd.Derailed, n.Crew);
        Assert.Equal(4, n.World.Bookmarks.All.Count);
    }

    /// <summary>A hosted night over loopback: the host, a living crewmate and a dead one watching them.</summary>
    sealed class Hosted
    {
        public readonly LoopbackNetwork Net = new();
        public readonly HostSession Host;
        public readonly ClientSession Living, Dead;

        public Hosted()
        {
            var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, 1);
            World Build(bool host)
            {
                var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 0.5)), route.Build(), 3_000, Tuning.Boiler);
                var w = new World(train, Tuning.Combat);
                w.EnableRun(Tuning.Run, route, 600, authority: host);
                return w;
            }
            var hostWorld = Build(true);
            hostWorld.Run!.Resume(900, -1, hostWorld.Train.Boiler.Tender, 0);
            Host = new HostSession(Net.CreateHost(), hostWorld, Tuning.Train, P);
            Living = new ClientSession(Net.CreateClient(), Build(false), Tuning.Train, P) { Name = "Dave" };
            Dead = new ClientSession(Net.CreateClient(), Build(false), Tuning.Train, P) { Name = "Priya" };
            Step(default, 30);
            var s = Host.Players.First(p => p.Id == Dead.PlayerId).State;
            Host.SetPlayerState(Dead.PlayerId!.Value, s with { Health = 0, Death = DeathCause.Eaten });
            Step(default, 10);
        }

        public void Step(PlayerIntent dead, int ticks)
        {
            for (int t = 0; t < ticks; t++)
            {
                Net.Advance(SimConstants.TickSeconds);
                Host.Step();
                Living.Step(default);
                Dead.Step(dead);
            }
        }

        public PlayerIntent Pressing => new() { Watch = Living.PlayerId!.Value, Actions = PlayerActions.Bookmark };
        public PlayerIntent Watching => new() { Watch = Living.PlayerId!.Value };
    }

    [Fact]
    public void ADeadPlayersBookmarkIsTheirIntentTakenOnThePressOfWhomTheyWatchAndReachesEveryone()
    {
        var h = new Hosted();
        // Held for a while (and resent by the input redundancy): one press, one bookmark.
        h.Step(h.Pressing, 20);
        h.Step(h.Watching, 10);
        var b = Assert.Single(h.Host.World.Bookmarks.All);
        Assert.Equal(BookmarkKind.Manual, b.Kind);
        Assert.Equal((int)h.Dead.PlayerId!.Value, b.Taker);
        Assert.Equal((int)h.Living.PlayerId!.Value, b.Viewer);
        Assert.Equal("Dave", b.Name);
        var followed = h.Host.Players.First(p => p.Id == h.Living.PlayerId).State;
        Assert.Equal(followed.Parent, b.Frame);
        // Every client has it as the host made it, to take the still from.
        foreach (var c in new[] { h.Living, h.Dead })
            Assert.Equal(b, Assert.Single(c.World.Bookmarks.All));
    }

    [Fact]
    public void TheLivingCantBookmarkAndTheDeadGetTheirShareOnly()
    {
        var h = new Hosted();
        // The living press it: nothing.
        for (int i = 0; i < 3; i++)
        {
            for (int t = 0; t < 5; t++)
            {
                h.Net.Advance(SimConstants.TickSeconds);
                h.Host.Step();
                h.Living.Step(new PlayerIntent { Actions = PlayerActions.Bookmark });
                h.Dead.Step(h.Watching);
            }
            h.Step(h.Watching, 5);
        }
        Assert.Empty(h.Host.World.Bookmarks.All);
        // The dead press it more often than their share.
        for (int i = 0; i < h.Host.World.Bookmarks.Tuning.ManualPerPlayer + 3; i++)
        {
            h.Step(h.Pressing, 3);
            h.Step(h.Watching, 3);
        }
        Assert.Equal(h.Host.World.Bookmarks.Tuning.ManualPerPlayer, h.Host.World.Bookmarks.All.Count);
    }

    [Fact]
    public void ABookmarkCrossesTheWireIntact()
    {
        var b = new Bookmark(5, BookmarkKind.Punish, 1234.5, 2, 3, 4, new Double3(0.25, 3.9, -2), new Double3(0, -0.6, -0.8),
            "Punished by the Car Hugger", "on the roof of car 3 at Hollin Halt");
        var w = new NetWriter();
        Messages.WriteBookmark(w, b);
        var r = new NetReader(w.Written.ToArray());
        Assert.Equal((byte)MessageType.Bookmark, r.U8());
        Assert.Equal(b, Messages.ReadBookmark(ref r));
    }
}
