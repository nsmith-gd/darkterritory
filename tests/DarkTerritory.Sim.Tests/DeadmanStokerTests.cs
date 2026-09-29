using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The Deadman and the Stoker (T53, App. A.5, B.5): the cab and the firebox, the two places a crew that all piles out at a
/// stop leaves open. Rules: never leave the cab empty; vent, or the boiler goes.
/// </summary>
public class DeadmanStokerTests
{
    static readonly DeadmanTuning D = Tuning.Enemies.Deadman;
    static readonly StokerTuning S = Tuning.Enemies.Stoker;
    static readonly PlayerTuning P = Tuning.Player;

    static Night OnA(RouteTier tier, double speed = 8) =>
        new(4, speed, RouteGenerator.Generate(Tuning.Route, tier, 7), boiler: true);

    static Deadman? Deadman(Night n) => n.World.ActiveEnemies.OfType<Deadman>().SingleOrDefault();

    [Fact]
    public void LeaveTheCabEmptyAndTheDeadmanTakesItAtTheSpecsTime()
    {
        var n = OnA(RouteTier.Frontier);
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P);
        // Its approach begins with its telegraph's worth to go, and it's heard for all of it.
        n.Run(D.EmptySeconds - D.TelegraphSeconds - 1);
        Assert.Null(Deadman(n));
        n.Run(1.1);
        var deadman = Assert.IsType<Deadman>(Deadman(n));
        Assert.Equal(SpinePhase.Telegraph, deadman.Phase);
        n.Run(D.TelegraphSeconds);
        Assert.True(deadman.Holding);
        // At the controls: the regulator open, the brake dead, whatever the cab controls say.
        n.Controls = new TrainControls { Reverser = 1, Brake = 1 };
        double before = n.Train.Dynamics.Speed;
        n.Run(3, holdSpeed: false);
        Assert.True(n.Train.Dynamics.Speed > before);
        Assert.Contains(n.World.Director!.Log, l => l.Kind == EnemyKind.Deadman);
        n.AssertFair();
    }

    [Fact]
    public void BackInTheCabInTimeAndItsGoneFree()
    {
        var n = OnA(RouteTier.Frontier);
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P);
        n.Run(D.EmptySeconds - D.TelegraphSeconds + 2);
        var deadman = Assert.IsType<Deadman>(Deadman(n));
        n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, P);
        n.Run(0.2);
        Assert.True(deadman.Gone);
        // Free to prevent: it never cost the director anything.
        Assert.DoesNotContain(n.World.Director!.Log, l => l.Kind == EnemyKind.Deadman);
        Assert.Equal(P.Health, n.Crew[1].Health);
    }

    [Fact]
    public void TakingTheCabBackTakesAFewSecondsAndItHurts()
    {
        var n = OnA(RouteTier.Frontier);
        n.Run(D.EmptySeconds + 1);
        var deadman = Assert.IsType<Deadman>(Deadman(n));
        Assert.True(deadman.Holding);
        n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, P);
        n.Run(D.EvictSeconds - 0.5);
        Assert.True(deadman.Holding);
        Assert.Equal(P.Health - D.EvictDamage, n.Crew[1].Health);
        n.Run(1);
        Assert.True(deadman.Gone);
    }

    [Fact]
    public void NotOnLocalRoutesAndSoonerInDeepTerritory()
    {
        var local = OnA(RouteTier.Local);
        local.Run(D.EmptySeconds + 5);
        Assert.Null(Deadman(local));
        var deep = OnA(RouteTier.DeepTerritory);
        deep.Run(D.EmptySecondsDeep + 0.5);
        Assert.True(Assert.IsType<Deadman>(Deadman(deep)).Holding);
    }

    static (Night Night, Stoker Stoker) StokerIn()
    {
        // On a Local route, so no Deadman comes for the cab while the Stoker's at work.
        var n = OnA(RouteTier.Local, speed: 0);
        n.Train.Boiler.Pressure = 80;
        n.Train.Boiler.Firebox = 2; // a fire burning low: the climb is the Stoker's, not the fire's
        var stoker = n.World.AddEnemy(id => Stoker.InFirebox(id, n.Train));
        return (n, stoker);
    }

    [Fact]
    public void TheStokerFeedsTheBoilerPastTheValveUntilItGoes()
    {
        var (n, stoker) = StokerIn();
        // Nobody at the firebox.
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, 1, 0, P);
        var bt = Tuning.Boiler;
        n.Run(2);
        Assert.Equal(SpinePhase.Telegraph, stoker.Phase);
        Assert.True(n.Train.Boiler.SafetyValveJammed);
        // The gauge climbs with no fuel going in, past where the valve would have lifted, to the maximum.
        n.Run((bt.PressureMax - 80) / S.FeedRate + 5);
        Assert.Equal(bt.PressureMax, n.Train.Boiler.Pressure, 3);
        Assert.Equal(SpinePhase.Punish, stoker.Phase);
        n.Run(bt.RuptureHoldSeconds + 1);
        Assert.True(n.Train.Boiler.Ruptured);
        Assert.True(stoker.Gone);
        n.AssertFair();
    }

    [Fact]
    public void VentingHoldsItAndDrivingItOutEndsItButItHurts()
    {
        var (n, stoker) = StokerIn();
        n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, P);
        var vent = n.Train.Frames[0].Shape.Interactables.First(i => i.Kind == InteractableKind.Vent);
        var firebox = n.Train.Frames[0].Shape.Interactables.First(i => i.Kind == InteractableKind.Firebox);
        var hold = new PlayerIntent { Buttons = PlayerButtons.Use };
        // At the vent, holding it open: the pressure falls however it feeds.
        n.Crew[1] = n.Crew[1] with { Position = vent.Position with { Y = n.Crew[1].Position.Y } };
        Assert.Equal(InteractableKind.Vent, CrewActions.Nearest(n.Crew[1], n.Train));
        n.Run(5, _ => hold);
        Assert.True(n.Train.Boiler.Pressure < 80, $"{n.Train.Boiler.Pressure}");
        Assert.False(stoker.Gone);
        // At the firebox, Use held: it lashes out once, and it's driven out.
        n.Crew[1] = n.Crew[1] with { Position = firebox.Position with { Y = n.Crew[1].Position.Y } };
        Assert.Equal(InteractableKind.Firebox, CrewActions.Nearest(n.Crew[1], n.Train));
        n.Run(S.DriveOutSeconds + 0.2, _ => hold);
        Assert.True(stoker.Gone);
        Assert.Equal(P.Health - S.DriveOutDamage, n.Crew[1].Health);
        Assert.False(n.Train.Boiler.SafetyValveJammed);
        Assert.Equal(0, n.Train.Boiler.ExternalHeat);
    }

    [Fact]
    public void TheDirectorPutsAStokerInOnlyAtAStopWithTheCabEmpty()
    {
        var d = Tuning.Enemies.Director;
        var only = Tuning.Enemies with
        {
            Director = d with { GraceSeconds = 0, CooldownSeconds = [1, 1], Costs = d.Costs.ToDictionary(c => c.Key, c => c.Key == "stoker" ? c.Value : 1e9) },
        };
        // Running, nobody in the cab: no.
        var running = new Night(4, 8, boiler: true);
        running.World.EnableEnemies(only, route: null, 1, crew: 2, authority: true);
        running.Run(40);
        Assert.DoesNotContain(running.World.ActiveEnemies, e => e.Kind == EnemyKind.Stoker);
        // Stood, someone in the cab: no.
        var attended = new Night(4, 0, boiler: true);
        attended.World.EnableEnemies(only, route: null, 1, crew: 2, authority: true);
        attended.Crew[1] = PlayerMotor.SpawnInCab(attended.Train, P);
        attended.Run(40);
        Assert.DoesNotContain(attended.World.ActiveEnemies, e => e.Kind == EnemyKind.Stoker);
        // Stood with the cab empty: in it gets.
        var stood = new Night(4, 0, boiler: true);
        stood.World.EnableEnemies(only, route: null, 1, crew: 2, authority: true);
        stood.Run(S.UnattendedSeconds + 3);
        Assert.Single(stood.World.ActiveEnemies, e => e.Kind == EnemyKind.Stoker);
    }

    [Fact]
    public void AClientSeesWhoHoldsTheCab()
    {
        var n = OnA(RouteTier.Frontier);
        n.Run(D.EmptySeconds + 1);
        var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), n.Train.Line, 400, Tuning.Boiler), Tuning.Combat);
        client.EnableEnemies(Tuning.Enemies, route: null, 1, crew: 1, authority: false);
        var controls = new TrainControls();
        WorldRecords.Apply(WorldRecords.Capture(n.World, controls, []), client, ref controls, []);
        Assert.True(Assert.IsType<Deadman>(Assert.Single(client.ActiveEnemies, e => e.Kind == EnemyKind.Deadman)).Holding);
    }
}
