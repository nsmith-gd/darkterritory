using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Horseplay (GDD §23.1; ARCHITECTURE §8 note 717; the director, 14 Oct 2026: "The griefer ideas are funny"; "if you're
/// standing on top of a car, you can be shoved off of it. And at speed, it kills you"): crewmates are solid to each other, a
/// shove staggers and can put you off a roof, coal comes off the shovel, a tool's swing bonks a friend. No damage between
/// crewmates; a death only by the world's own rules, and the report names who.
/// </summary>
public class HorseplayTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly HorseplayTuning H = P.Horseplay;
    const double Dt = SimConstants.TickSeconds;

    /// <summary>
    /// A night's crew stepped the way the host steps them (HostSession.Step): the crew as they stood at the start of the tick for
    /// whom a shove or a bonk finds and the crowd each is pushed from, a staggered crewmate's intent stripped, the crew's acts,
    /// the world, the knocks, then everyone's feet.
    /// </summary>
    sealed class Crew(World world, Dictionary<int, PlayerState> players, PlayerTuning? p = null)
    {
        public readonly World World = world;
        public readonly Dictionary<int, PlayerState> Players = players;
        public readonly PlayerTuning P = p ?? Tuning.Player;
        public double? Speed;
        public TrainOnLine Train => World.Train;
        public readonly List<HorseplayEvent> Seen = [];

        public void Run(double seconds, Func<int, PlayerIntent>? intent = null) => Ticks((int)Math.Round(seconds * SimConstants.TickRate), intent);

        public void Ticks(int ticks, Func<int, PlayerIntent>? intent = null)
        {
            World.Movement = P;
            for (int i = 0; i < ticks; i++)
            {
                if (Speed is { } v)
                    Train.Dynamics.Velocity = v;
                World.Mates = [.. Players.OrderBy(c => c.Key).Select(c => (c.Key, c.Value))];
                var crowd = Horseplay.Crowd(World.Mates);
                var ids = Players.Keys.Order().ToList();
                var intents = ids.ToDictionary(id => id, id => Horseplay.Staggered(Players[id], intent?.Invoke(id) ?? default));
                World.BeginTick();
                foreach (var id in ids)
                {
                    var s = Players[id];
                    World.CrewAct(ref s, intents[id], id);
                    Players[id] = s;
                }
                World.Step(new TrainControls { Reverser = 1 });
                World.ApplyDamage(id => Players.TryGetValue(id, out var s) ? s : null, (id, s) => Players[id] = s, ids);
                foreach (var id in ids)
                {
                    var s = Players[id];
                    PlayerMotor.Step(ref s, intents[id], Train, P, T, Dt, applyLook: false, id, crowd);
                    Players[id] = s;
                }
                World.StepBodies([.. ids.Select(id => (id, Players[id]))]);
                foreach (var h in World.Horseplays)
                    if (!Seen.Any(x => x.Id == h.Id))
                        Seen.Add(h);
            }
        }

        public Double3 At(int id) => PlayerMotor.WorldPosition(Players[id], Train);
        public double Apart(int a, int b) => ((At(a) - At(b)) with { Y = 0 }).Length;
    }

    static Crew Night(int cars, double speed, bool boiler = false, bool enemies = false, PlayerTuning? p = null)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(40_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, cars, 1)), line, 2_000, boiler ? Tuning.Boiler : null);
        train.Dynamics.Velocity = speed;
        var world = new World(train, Tuning.Combat);
        if (enemies)
            world.EnableEnemies(Tuning.Enemies, null, 1, crew: 2, authority: true);
        world.Names[1] = "Dave";
        world.Names[2] = "Priya";
        return new Crew(world, [], p) { Speed = speed };
    }

    static PlayerState Facing(PlayerState s, double yaw) => s with { Yaw = yaw };

    static readonly PlayerIntent Shove = new() { Moves = PlayerMoves.Shove };

    // ---- Crewmates are solid to each other.

    [Fact]
    public void TwoCrewmatesCantStandOnTheSameSpotOnTheGround()
    {
        var n = Night(3, 0);
        var spot = n.Train.Frames[1].ToWorld(new Double3(n.Train.Frames[1].Shape.HalfWidth + 3, 0, 0));
        n.Players[1] = PlayerMotor.SpawnOnGround(spot, n.Train.Line, n.Train.Cars[1].FrontDistance, P);
        n.Players[2] = PlayerMotor.SpawnOnGround(spot, n.Train.Line, n.Train.Cars[1].FrontDistance, P);
        n.Run(1);
        Assert.True(n.Apart(1, 2) >= 2 * H.Crowd.Radius - 0.02, $"{n.Apart(1, 2):0.000} m apart");
        // Walked straight at the other for two seconds: not through them. It shoulders them along softly, here into the car's
        // side, where they're pressed a little closer than two shoulders' breadth (crowd.share: the walker's own share of it).
        var other = n.At(2);
        double yaw = Math.Atan2(-(other.X - n.At(1).X), -(other.Z - n.At(1).Z));
        n.Players[1] = Facing(n.Players[1], yaw);
        n.Run(2, id => id == 1 ? new PlayerIntent { MoveZ = 1 } : default);
        Assert.True(n.Apart(1, 2) >= 2 * H.Crowd.Radius - 0.15, $"{n.Apart(1, 2):0.000} m apart");
        Assert.All(n.Players.Values, s => Assert.Equal(P.Health, s.Health));
    }

    [Fact]
    public void TwoCrewmatesCantStandOnTheSameSpotInAMovingCar()
    {
        var n = Night(4, 15);
        n.Players[1] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P);
        n.Players[2] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P);
        n.Run(1);
        Assert.All(n.Players.Values, s => Assert.Equal((2, Surface.Roof), (s.Parent, s.Surface)));
        Assert.True((n.Players[1].Position - n.Players[2].Position).Length >= 2 * H.Crowd.Radius - 0.02);
        // Walking into each other along the roof at 15 m/s: each stays on, in the car's frame, never through.
        n.Players[2] = n.Players[2] with { Position = n.Players[2].Position with { X = 0, Z = -2 } };
        n.Players[1] = n.Players[1] with { Position = n.Players[1].Position with { X = 0, Z = 0 }, Yaw = 0 };
        n.Run(2, id => id == 1 ? new PlayerIntent { MoveZ = 1 } : default);
        Assert.All(n.Players.Values, s => Assert.Equal((2, Surface.Roof), (s.Parent, s.Surface)));
        Assert.True((n.Players[1].Position - n.Players[2].Position).Length >= 2 * H.Crowd.Radius - 0.1);
        Assert.All(n.Players.Values, s => Assert.Equal(P.Health, s.Health));
    }

    [Fact]
    public void ALadderIsntBlockedByTheCrowd()
    {
        // On a ladder you're passed (a hatch's comes up through a crowded roof): the climber isn't pushed, nor pushes.
        var n = Night(3, 0);
        var ladder = n.Train.Frames[1].Shape.Ladders[0];
        n.Players[1] = PlayerMotor.SpawnOnRoof(n.Train, 1, 0, P) with { Surface = Surface.Ladder, Position = ladder.Foot with { Y = 1 } };
        Assert.False(Horseplay.Solid(n.Players[1]));
        Assert.Empty(Horseplay.Crowd([(1, n.Players[1])]));
    }

    // ---- The shove.

    [Fact]
    public void AShoveKnocksACrewmateAwayAndStaggersThemWithNoDamage()
    {
        var n = Night(3, 0);
        var spot = n.Train.Frames[1].ToWorld(new Double3(n.Train.Frames[1].Shape.HalfWidth + 4, 0, 0));
        n.Players[1] = Facing(PlayerMotor.SpawnOnGround(spot, n.Train.Line, n.Train.Cars[1].FrontDistance, P), 0);
        n.Players[2] = PlayerMotor.SpawnOnGround(spot + new Double3(0, 0, -0.8), n.Train.Line, n.Train.Cars[1].FrontDistance, P);
        var was = n.At(2);
        n.Ticks(1, id => id == 1 ? Shove : default);
        var hit = n.Players[2];
        Assert.True(hit.Stagger > 0);
        Assert.Equal(Surface.Air, hit.Surface);
        Assert.Contains(n.Seen, e => e is { Kind: KnockKind.Shove, By: 1, Target: 2 });
        // Staggered: they can't walk it off, only look.
        n.Run(H.Shove.StaggerSeconds - 2 * Dt, id => id == 2 ? new PlayerIntent { MoveZ = -1, LookYaw = 0.01f } : default);
        Assert.True(n.Players[2].Stagger > 0);
        var moved = n.At(2) - was;
        Assert.True(-moved.Z > 0.5, $"shoved {moved.Z:0.00} m");
        Assert.InRange(Math.Abs(moved.X), 0, 0.05);
        n.Run(1);
        Assert.Equal(0, n.Players[2].Stagger);
        Assert.Equal(P.Health, n.Players[2].Health);
    }

    [Fact]
    public void TheShoverWaitsOutTheCooldown()
    {
        var n = Night(3, 0);
        var spot = n.Train.Frames[1].ToWorld(new Double3(n.Train.Frames[1].Shape.HalfWidth + 4, 0, 0));
        double hint = n.Train.Cars[1].FrontDistance;
        n.Players[1] = Facing(PlayerMotor.SpawnOnGround(spot, n.Train.Line, hint, P), 0);
        n.Players[2] = PlayerMotor.SpawnOnGround(spot + new Double3(0, 0, -0.8), n.Train.Line, hint, P);
        n.Players[3] = PlayerMotor.SpawnOnGround(spot + new Double3(0.6, 0, -0.7), n.Train.Line, hint, P);
        n.Ticks(1, id => id == 1 ? Shove : default);
        Assert.True(n.Players[2].Stagger > 0);
        Assert.Equal(0, n.Players[3].Stagger);
        n.Ticks(1);
        n.Ticks(1, id => id == 1 ? Shove : default);
        Assert.Equal(0, n.Players[3].Stagger);
        n.Run(H.Shove.CooldownSeconds);
        n.Ticks(1, id => id == 1 ? Shove : default);
        Assert.True(n.Players[3].Stagger > 0);
    }

    [Fact]
    public void BotsNeverShove()
    {
        var n = Night(3, 0);
        var spot = n.Train.Frames[1].ToWorld(new Double3(n.Train.Frames[1].Shape.HalfWidth + 4, 0, 0));
        n.Players[1] = Facing(PlayerMotor.SpawnOnGround(spot, n.Train.Line, n.Train.Cars[1].FrontDistance, P), 0);
        n.Players[2] = PlayerMotor.SpawnOnGround(spot + new Double3(0, 0, -0.8), n.Train.Line, n.Train.Cars[1].FrontDistance, P);
        n.World.BotIds.Add(1);
        n.Ticks(1, id => id == 1 ? Shove : default);
        Assert.Equal(0, n.Players[2].Stagger);
        // ... but can be shoved.
        n.World.BotIds.Clear();
        n.World.BotIds.Add(2);
        n.Ticks(1);
        n.Ticks(1, id => id == 1 ? Shove : default);
        Assert.True(n.Players[2].Stagger > 0);
    }

    /// <summary>Two on car 2's roof: the shover in the middle, facing across, and the one shoved by the side's edge.</summary>
    static Crew AtTheEdge(double speed)
    {
        var n = Night(4, speed);
        double edge = n.Train.Frames[2].Shape.HalfWidth - P.Edge.Lip - 0.05;
        n.Players[1] = Facing(PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P, edge - 0.85), -Math.PI / 2);
        n.Players[2] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P, edge);
        return n;
    }

    [Fact]
    public void TheRoofsLipHoldsAWalkerButNotAShove()
    {
        var n = AtTheEdge(0);
        // Walking along the edge with a glance outward (not straight out): the lip holds them (note 273).
        n.Players[2] = Facing(n.Players[2], -Math.PI / 2 + 1.2);
        n.Run(1.5, id => id == 2 ? new PlayerIntent { MoveZ = 1 } : default);
        Assert.Equal((2, Surface.Roof), (n.Players[2].Parent, n.Players[2].Surface));
        n.Players[2] = n.Players[2] with { Position = n.Players[2].Position with { Z = 0 } };
        // A shove isn't walking: over the lip.
        n.Ticks(1, id => id == 1 ? Shove : default);
        n.Run(3);
        Assert.Equal(PlayerState.World, n.Players[2].Parent);
        Assert.Equal(Surface.Ground, n.Players[2].Surface);
    }

    [Fact]
    public void AShoveOffARoofAtAStandIsAFallByItsHeight()
    {
        // A car's roof is 4 m over the ballast: under note 573's fall knock, and the shove's own speed knocks nobody. Alive and
        // whole on the ballast (a bridge's deck would be another matter: the same rule as any fall).
        var n = AtTheEdge(0);
        n.Ticks(1, id => id == 1 ? Shove : default);
        n.Run(3);
        var s = n.Players[2];
        Assert.True(s.Alive);
        Assert.Equal((PlayerState.World, Surface.Ground), (s.Parent, s.Surface));
        Assert.Equal(P.Health, s.Health);
        Assert.False(s.Has(PlayerFlags.Shoved));
        Assert.Equal((2, Surface.Roof), (n.Players[1].Parent, n.Players[1].Surface));
    }

    [Fact]
    public void AShoveOffARoofAtSpeedKillsAndTheReportNamesTheShover()
    {
        // GDD §23: off the train faster than 16.5 m/s is death. At 18 m/s (65 km/h) a shove over the edge is.
        var n = AtTheEdge(18);
        n.Ticks(1, id => id == 1 ? Shove : default);
        n.Run(3);
        var s = n.Players[2];
        Assert.False(s.Alive);
        Assert.Equal(DeathCause.Shoved, s.Death);
        Assert.True(n.Players[1].Alive);
        var line = IncidentLog.Death(n.World, 2, s, null, n.Players.Select(c => (c.Key, c.Value)));
        Assert.Equal(1, line.Actor);
        Assert.Equal("Shoved off the train", line.What);
        Assert.Equal("Shoved off by Dave at 65 km/h.", line.Action.Replace("{actor}", "Dave"));
    }

    [Fact]
    public void AShoveOffARoofBelowTheJumpOffSpeedIsALandingNotADeath()
    {
        var n = AtTheEdge(10);
        n.Ticks(1, id => id == 1 ? Shove : default);
        n.Run(3);
        var s = n.Players[2];
        Assert.True(s.Alive);
        Assert.Equal(Surface.Ground, s.Surface);
        // Spec B.3's knock for leaving a train at 10 m/s, no more for the shove.
        Assert.InRange(P.Health - s.Health, 1, 60);
    }

    [Fact]
    public void AShovedCrewmateKnocksIntoTheOneBeyondThem()
    {
        var n = Night(4, 0);
        n.Players[1] = Facing(PlayerMotor.SpawnOnRoof(n.Train, 2, 2, P), 0);
        n.Players[2] = PlayerMotor.SpawnOnRoof(n.Train, 2, 1.2, P);
        n.Players[3] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0.6, P);
        n.Run(0.5);
        n.Ticks(1, id => id == 1 ? Shove : default);
        Assert.True(n.Players[2].Stagger > 0);
        Assert.True(n.Players[3].Stagger > 0);
        Assert.True(n.Players[3].Stagger < n.Players[2].Stagger);
        Assert.Equal(1, n.World.ShovedBy(3)?.By);
    }

    // ---- Coal off the shovel.

    [Fact]
    public void HoldingUseAtTheFireboxPutsCoalOnTheShovelBeforeItGoesIn()
    {
        // Note 717 with the shovelling as it is (boiler.json scoopSeconds): a unit still goes in every shovelSeconds.
        var n = Night(3, 0, boiler: true);
        n.Train.Boiler.Firebox = 0;
        // Stood a step short of the firebox door and looking at it (ShovelTests.At: note 264's pick by look).
        var fire = n.Train.Frames[0].Shape.Interactables.First(i => i.Kind == InteractableKind.Firebox);
        var cab = PlayerMotor.SpawnInCab(n.Train, P);
        cab.Position = (fire.Position + new Double3(0.2, 0, -0.6)) with { Y = cab.Position.Y };
        var to = fire.Position + Double3.Up * fire.Aim - (cab.Position + Double3.Up * T.Pick.EyeHeight);
        n.Players[1] = cab with { Yaw = Math.Atan2(-to.X, -to.Z), Pitch = Math.Atan2(to.Y, Math.Sqrt(to.X * to.X + to.Z * to.Z)) };
        Assert.Equal(InteractableKind.Firebox, CrewActions.Nearest(n.Players[1], n.Train));
        var use = new PlayerIntent { Buttons = PlayerButtons.Use };
        double box = n.Train.Boiler.Firebox, tender = n.Train.Boiler.Tender;
        n.Run(Tuning.Boiler.ScoopSeconds + 0.1, _ => use);
        Assert.True(n.Players[1].Has(PlayerFlags.Shovelful));
        Assert.Equal(tender, n.Train.Boiler.Tender, 6);
        n.Run(Tuning.Boiler.ShovelSeconds - Tuning.Boiler.ScoopSeconds, _ => use);
        Assert.Equal(tender - 1, n.Train.Boiler.Tender, 6);
        // Thrown instead: the tender's a unit short, the fire doesn't get it, and nothing counts it twice.
        n.Run(Tuning.Boiler.ScoopSeconds, _ => use);
        Assert.True(n.Players[1].Has(PlayerFlags.Shovelful));
        double before = n.Train.Boiler.Tender, burning = n.Train.Boiler.Firebox;
        n.Ticks(1, _ => new PlayerIntent { Buttons = PlayerButtons.Use | PlayerButtons.Throw });
        Assert.False(n.Players[1].Has(PlayerFlags.Shovelful));
        Assert.Equal(before - 1, n.Train.Boiler.Tender, 6);
        Assert.Contains(n.Seen, e => e is { Kind: KnockKind.Coal, By: 1, Target: -1 });
        n.Run(Tuning.Boiler.ShovelSeconds - 2 * Dt, _ => use);
        Assert.True(n.Train.Boiler.Firebox <= burning + 1e-6, "the thrown handful went on the fire");
        Assert.True(box >= 0);
    }

    [Fact]
    public void CoalFromTheShovelfulStaggersAndSootsACrewmate()
    {
        // Thrown on the move, along a car's roof at 12 m/s, at a friend 4 m off: it goes with the train, as anything thrown does.
        var n = Night(4, 12, boiler: true);
        n.Players[1] = Facing(PlayerMotor.SpawnOnRoof(n.Train, 2, 3, P), 0);
        n.Players[2] = PlayerMotor.SpawnOnRoof(n.Train, 2, -1, P);
        n.Ticks(1);
        n.Players[1] = n.Players[1] with { Flags = PlayerFlags.Shovelful };
        double tender = n.Train.Boiler.Tender;
        n.Ticks(1, id => id == 1 ? new PlayerIntent { Buttons = PlayerButtons.Throw } : default);
        Assert.False(n.Players[1].Has(PlayerFlags.Shovelful));
        Assert.Equal(tender - 1, n.Train.Boiler.Tender, 6);
        Assert.Single(n.World.Lumps);
        n.Run(0.5);
        var hit = n.Players[2];
        Assert.True(hit.Has(PlayerFlags.Sooted));
        Assert.Contains(n.Seen, e => e is { Kind: KnockKind.Coal, By: 1, Target: 2 });
        Assert.Equal(P.Health, hit.Health);
        Assert.Empty(n.World.Lumps);
        Assert.Equal((2, Surface.Roof), (hit.Parent, hit.Surface));
        // Sooty for the rest of the night; the stagger passes.
        n.Run(2);
        Assert.True(n.Players[2].Has(PlayerFlags.Sooted));
        Assert.Equal(0, n.Players[2].Stagger);
    }

    [Fact]
    public void CoalStaggersForLessThanAShove() =>
        Assert.True(H.Coal.StaggerSeconds < H.Shove.StaggerSeconds);

    [Fact]
    public void CoalDoesAQuarterOfABlowToACreature()
    {
        // The Car Hugger on car 2's roof, a pace and a half in front (HitConfirmTests.Staged): a blow lands on it as on any
        // creature, and it heals a little a tick, so it's the drop on the tick the lump lands that's read.
        var (night, e) = HitConfirmTests.Staged(EnemyKind.CarHugger);
        var n = new Crew(night.World, night.Crew);
        n.Players[1] = n.Players[1] with { Flags = PlayerFlags.Shovelful };
        double health = e.Health, dropped = 0;
        n.Ticks(1, id => new PlayerIntent { Buttons = PlayerButtons.Throw });
        for (int i = 0; i < 15 && dropped == 0; i++)
        {
            dropped = Math.Max(0, health - e.Health);
            health = Math.Max(health, e.Health);
            if (dropped == 0)
                n.Ticks(1);
        }
        Assert.InRange(dropped, H.Coal.Blow * Tuning.Enemies.Melee.Crowbar - 0.02, H.Coal.Blow * Tuning.Enemies.Melee.Crowbar + 1e-9);
        Assert.Contains(n.World.Hits, h => h.EnemyId == e.Id && h.By == 1 && h.Source == HitSource.Melee);
        Assert.Empty(n.World.Lumps);
    }

    // ---- The bonk.

    [Fact]
    public void ABonkStaggersAFriendDoesNoDamageAndIsCounted()
    {
        var n = Night(4, 0, enemies: true);
        n.Players[1] = Facing(PlayerMotor.SpawnOnRoof(n.Train, 2, 2, P), 0);
        n.Players[2] = PlayerMotor.SpawnOnRoof(n.Train, 2, 1, P);
        var swing = new PlayerIntent { Actions = PlayerActions.Swing };
        n.Ticks(1, id => id == 1 ? swing : default);
        Assert.True(n.Players[2].Stagger > 0);
        Assert.Equal(P.Health, n.Players[2].Health);
        Assert.Contains(n.Seen, e => e is { Kind: KnockKind.Bonk, By: 1, Target: 2 });
        Assert.Equal(1, n.World.BonksOn(2)[1]);
        Assert.Empty(n.World.Hits);
        // Swung again and again: counted, and told once there are enough of them.
        Assert.DoesNotContain(IncidentLog.Lines(n.World, 0, 0, _ => false), l => l.Kind == IncidentKind.Bonked);
        for (int i = 1; i < H.Bonk.ReportFrom; i++)
            n.Run(Tuning.Enemies.Melee.SwingSeconds + Dt, id => id == 1 ? swing : default);
        Assert.Equal(H.Bonk.ReportFrom, n.World.BonksOn(2).Values.Sum());
        var told = Assert.Single(IncidentLog.Lines(n.World, 0, 0, _ => false), l => l.Kind == IncidentKind.Bonked);
        Assert.Equal($"Bonked {H.Bonk.ReportFrom} times, mostly by Dave.", told.Text);
        Assert.Equal("Priya", told.Who);
        Assert.Equal(P.Health, n.Players[2].Health);
    }

    // ---- The switch.

    [Fact]
    public void WithHorseplayOffNobodyIsShovedBonkedOrHitWithCoal()
    {
        var off = P with { Horseplay = H with { Enabled = false } };
        var n = Night(4, 0, enemies: true, p: off);
        n.Players[1] = Facing(PlayerMotor.SpawnOnRoof(n.Train, 2, 2, off), 0);
        n.Players[2] = PlayerMotor.SpawnOnRoof(n.Train, 2, 1, off);
        n.Ticks(1, id => id == 1 ? Shove : default);
        n.Ticks(1, id => id == 1 ? new PlayerIntent { Actions = PlayerActions.Swing } : default);
        n.Players[1] = n.Players[1] with { Flags = n.Players[1].Flags | PlayerFlags.Shovelful };
        n.Ticks(1, id => id == 1 ? new PlayerIntent { Buttons = PlayerButtons.Throw } : default);
        n.Run(1);
        var s = n.Players[2];
        Assert.Equal(0, s.Stagger);
        Assert.False(s.Has(PlayerFlags.Sooted));
        Assert.Equal((2, Surface.Roof), (s.Parent, s.Surface));
        Assert.Empty(n.World.BonksOn(2));
        Assert.DoesNotContain(n.Seen, e => e.Target == 2);
        // Still solid to each other.
        n.Players[2] = n.Players[2] with { Position = n.Players[1].Position };
        n.Run(1);
        Assert.True((n.Players[1].Position - n.Players[2].Position).Length >= 2 * H.Crowd.Radius - 0.02);
    }

    // ---- The mark (the director, 14 Oct 2026: "if a player ever shoves another player and that results in their death all
    // the monsters in the game should target more heavily the murderer player ... (even if they rescue the player from a
    // holdout)").

    /// <summary>Dave (1) shoves Priya (2) off car 2's roof at 18 m/s, and she dies of it; Reyes (3) is along the roof.</summary>
    static Crew ShovedToDeath(bool enemies = false)
    {
        var n = Night(4, 18, enemies: enemies);
        n.World.Names[3] = "Reyes";
        double edge = n.Train.Frames[2].Shape.HalfWidth - P.Edge.Lip - 0.05;
        n.Players[1] = Facing(PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P, edge - 0.85), -Math.PI / 2);
        n.Players[2] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P, edge);
        n.Players[3] = PlayerMotor.SpawnOnRoof(n.Train, 2, -5, P);
        n.Ticks(1, id => id == 1 ? Shove : default);
        n.Run(3);
        Assert.Equal(DeathCause.Shoved, n.Players[2].Death);
        return n;
    }

    [Fact]
    public void AShoveThatKillsMarksTheShover()
    {
        var n = ShovedToDeath();
        Assert.True(n.World.IsMarked(1));
        Assert.False(n.World.IsMarked(2));
        Assert.False(n.World.IsMarked(3));
        Assert.Equal(H.Marked.TargetWeight, n.World.TargetBias(1));
        Assert.Equal(1, n.World.TargetBias(3));
        // Told to everyone, and in the report.
        var news = Assert.Single(n.World.NewsLog, e => e.Kind == NewsKind.Marked);
        Assert.Equal("MARKED: DAVE (SHOVED PRIYA OFF AT 65 KM/H)", News.Line(n.World, news));
        Assert.Contains(IncidentLog.Lines(n.World, 0, 0, _ => false), l => l.Kind == IncidentKind.Marked && l.Text == "Marked: Dave (shoved Priya off at 65 km/h).");
        // Weighed by whoever's choosing: 15 m off and marked counts as nearer than 2 m off and not.
        Assert.True(n.World.WeighTarget(1, 15) < n.World.WeighTarget(3, 2));
    }

    [Fact]
    public void AShoveThatDoesntKillMarksNobody()
    {
        foreach (double speed in new[] { 0.0, 10 })
        {
            var n = AtTheEdge(speed);
            n.Ticks(1, id => id == 1 ? Shove : default);
            n.Run(3);
            Assert.True(n.Players[2].Alive);
            Assert.False(n.World.IsMarked(1));
            Assert.DoesNotContain(n.World.NewsLog, e => e.Kind == NewsKind.Marked);
        }
    }

    [Fact]
    public void TheMarkStaysThroughTheVictimsReturnAndTheRescue()
    {
        // Back through a Holdout (a new body where she's freed), and stood beside the one who shoved her, rescued: the mark
        // stays.
        var n = ShovedToDeath();
        n.Players[2] = PlayerMotor.SpawnOnRoof(n.Train, 2, 1, P);
        n.Run(1);
        Assert.True(n.World.IsMarked(1));
        Assert.Equal(H.Marked.TargetWeight, n.World.TargetBias(1));
        Assert.Single(n.World.Marks);
    }

    [Fact]
    public void TheShoversDeathEndsTheMarkButNotTheReportsLine()
    {
        // Note 754 (the director, 14 Oct 2026: "Shovers mark should reset to normal targeting after they are killed"): Dave
        // dies (of anything), and back through a Holdout he's weighed like anyone. The news and the report keep the line.
        var n = ShovedToDeath();
        n.Players[1] = n.Players[1] with { Health = 0, Death = DeathCause.Mauled };
        n.Run(0.5);
        Assert.False(n.World.IsMarked(1));
        n.Players[1] = PlayerMotor.SpawnOnRoof(n.Train, 2, 2, P);
        n.Run(0.5);
        Assert.False(n.World.IsMarked(1));
        Assert.Equal(1, n.World.TargetBias(1));
        Assert.Equal(15.0, n.World.WeighTarget(1, 15));
        Assert.False(n.World.MarkedAfoot);
        Assert.Single(n.World.NewsLog, e => e.Kind == NewsKind.Marked);
        Assert.Single(n.World.Marks);
        Assert.Contains(IncidentLog.Lines(n.World, 0, 0, _ => false), l => l.Kind == IncidentKind.Marked && l.Text.StartsWith("Marked: Dave"));
    }

    [Fact]
    public void WithEndsAtDeathOffTheMarkOutlivesTheShover()
    {
        // horseplay.marked.endsAtDeath false: note 717 as built, the night's end alone clears it.
        var keep = P with { Horseplay = H with { Marked = H.Marked with { EndsAtDeath = false } } };
        var n = Night(4, 18, p: keep);
        double edge = n.Train.Frames[2].Shape.HalfWidth - keep.Edge.Lip - 0.05;
        n.Players[1] = Facing(PlayerMotor.SpawnOnRoof(n.Train, 2, 0, keep, edge - 0.85), -Math.PI / 2);
        n.Players[2] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, keep, edge);
        n.Ticks(1, id => id == 1 ? Shove : default);
        n.Run(3);
        Assert.Equal(DeathCause.Shoved, n.Players[2].Death);
        Assert.True(n.World.IsMarked(1));
        n.Players[1] = n.Players[1] with { Health = 0, Death = DeathCause.Mauled };
        n.Run(0.5);
        n.Players[1] = PlayerMotor.SpawnOnRoof(n.Train, 2, 2, keep);
        n.Run(0.5);
        Assert.True(n.World.IsMarked(1));
    }

    [Fact]
    public void MarkedTheCreaturesAndTheDirectorGoForTheShover()
    {
        var n = ShovedToDeath(enemies: true);
        Assert.True(n.World.IsMarked(1));
        // The director's next threat goes to the marked crewmate's post, whoever has the most slack.
        n.Run(2);
        Assert.Equal(1, n.World.Director!.Next?.Player);
        // A pack of Ribbits (App. A.6: whoever they outnumber on the ground) choosing between Reyes 6 m off and Dave 18 m off,
        // each alone: they go for Dave.
        n.Speed = 0;
        n.Train.Dynamics.Velocity = 0;
        n.Run(0.2);
        var frame = n.Train.Frames[2];
        double hint = n.Train.Dynamics.Distance - frame.Shape.HalfLength;
        PlayerState Ground(double outward) =>
            PlayerMotor.SpawnOnGround(frame.ToWorld(new Double3(frame.Shape.HalfWidth + outward, 0, 0)), n.Train.Line, hint, P);
        n.Players[1] = Ground(4);
        n.Players[3] = Ground(16);
        var at = frame.ToWorld(new Double3(frame.Shape.HalfWidth + 22, 0, 0));
        var a = n.World.AddEnemy(id => Ribbit.At(id, id, at, Tuning.Enemies.Ribbits));
        n.World.AddEnemy(id => Ribbit.At(id, a.Id, at + new Double3(0, 0, 1.5), Tuning.Enemies.Ribbits));
        n.Run(0.5);
        Assert.Equal(1, (int)Math.Round(a.Extra));
        // Unmarked, the same pack takes the nearer.
        var plain = Night(4, 0, enemies: true);
        var pf = plain.Train.Frames[2];
        double ph = plain.Train.Dynamics.Distance - pf.Shape.HalfLength;
        plain.Players[1] = PlayerMotor.SpawnOnGround(pf.ToWorld(new Double3(pf.Shape.HalfWidth + 4, 0, 0)), plain.Train.Line, ph, P);
        plain.Players[3] = PlayerMotor.SpawnOnGround(pf.ToWorld(new Double3(pf.Shape.HalfWidth + 16, 0, 0)), plain.Train.Line, ph, P);
        var pat = pf.ToWorld(new Double3(pf.Shape.HalfWidth + 22, 0, 0));
        var b = plain.World.AddEnemy(id => Ribbit.At(id, id, pat, Tuning.Enemies.Ribbits));
        plain.World.AddEnemy(id => Ribbit.At(id, b.Id, pat + new Double3(0, 0, 1.5), Tuning.Enemies.Ribbits));
        plain.Run(0.5);
        Assert.Equal(3, (int)Math.Round(b.Extra));
    }

    // ---- Over the wire: prediction still matches the host exactly.

    static readonly RailLine TestLoop = RailLine.Load(Path.Combine(DataFile.FindContentRoot(), "lines/test-loop.json"));

    [Fact]
    public void OnAPerfectLinkPredictionMatchesTheHostWithCrewmatesCollidingAndShoving()
    {
        var net = new LoopbackNetwork();
        var host = new HostSession(net.CreateHost(), new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), TestLoop, 600), T, P);
        var clients = Enumerable.Range(0, 2)
            .Select(_ => new ClientSession(net.CreateClient(), new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), TestLoop, 600), T, P))
            .ToArray();
        host.Controls = new TrainControls { Throttle = 0.5, Reverser = 1 };
        void Run(int ticks, Func<int, PlayerIntent> intent)
        {
            for (int t = 0; t < ticks; t++)
            {
                net.Advance(SimConstants.TickSeconds);
                host.Step();
                for (int i = 0; i < clients.Length; i++)
                    clients[i].Step(intent(i));
            }
        }
        Run(30, _ => default);
        byte a = clients[0].PlayerId!.Value, b = clients[1].PlayerId!.Value;
        host.SetPlayerState(a, PlayerMotor.SpawnOnRoof(host.Train, 2, 0, P) with { Yaw = 0 });
        host.SetPlayerState(b, PlayerMotor.SpawnOnRoof(host.Train, 2, -0.6, P));
        Run(10, _ => default);
        foreach (var c in clients)
            c.ResetStats();
        // Walking into a friend stood in the way (pushing them along the roof), then a shove, then both walking about, into each
        // other at an angle (sliding round).
        double gaps = double.MaxValue;
        for (int t = 0; t < 40; t++)
        {
            Run(1, i => i == 0 ? new PlayerIntent { MoveZ = 1 } : default);
            var (pa, pb) = (host.Players.First(p => p.Id == a).State, host.Players.First(p => p.Id == b).State);
            gaps = Math.Min(gaps, ((pa.Position - pb.Position) with { Y = 0 }).Length);
        }
        Assert.True(gaps >= 2 * H.Crowd.Radius - 0.1, $"{gaps:0.000} m apart");
        Run(1, i => i == 0 ? Shove : default);
        bool staggered = false;
        for (int t = 0; t < 10; t++)
        {
            Run(1, i => new PlayerIntent { MoveZ = i == 0 ? 1 : -0.5f });
            staggered |= host.Players.First(p => p.Id == b).State.Stagger > 0;
        }
        Run(60, i => i == 0 ? new PlayerIntent { MoveZ = 1 } : new PlayerIntent { MoveZ = 1, MoveX = 0.4f });
        Assert.True(staggered, "the shove didn't land");
        Assert.All(clients, c => Assert.True(c.MaxCorrection < 0.001, $"player {c.PlayerId} corrected by {c.MaxCorrection} m"));
        Assert.True(clients.Sum(c => c.SnapshotsReceived) > 200);
    }
}
