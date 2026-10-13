using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The run-start draw (ARCHITECTURE §8 note 287; GDD App. F.1 "Run start: nothing makes the player feel they've done something
/// that draws a monster", §14, §23.1 "Owned", C.9): the night's first threat answers something a crewmate did, the dark answers
/// out loud first, and the incident report names the draw.
/// </summary>
public class DrawTests
{
    static readonly DrawTuning T = Tuning.Enemies.Director.Draw;
    static readonly PlayerTuning P = Tuning.Player;

    static Night Frontier(ulong seed = 3, EnemyTuning? enemies = null)
    {
        var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, seed);
        var n = new Night(6, speed: 12, route, seed: seed, enemies: enemies);
        n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, P);
        n.World.Names[1] = "Dave";
        return n;
    }

    /// <summary>The director's own sends so far (not the line's hazards).</summary>
    static List<DirectorSpawn> Sent(Night n) => [.. n.World.Director!.Log.Where(l => l.Kind is not (EnemyKind.Stoker or EnemyKind.Drift))];

    [Fact]
    public void AWhistleIsAnsweredFromTheDarkAndTheFirstThreatFollowsIt()
    {
        var n = Frontier();
        var d = n.World.Director!;
        n.Run(d.Grace + 3);
        Assert.Empty(Sent(n));
        Assert.False(n.World.Answer.Showing);
        // A proper blast on the cord: two seconds held.
        n.Run(2, id => new PlayerIntent { Actions = PlayerActions.Whistle });
        n.Run(1.05);
        var answer = n.World.Answer;
        Assert.True(answer.Showing);
        Assert.Equal(DrawCause.Whistle, answer.Cause);
        Assert.Equal(1, answer.Actor);
        // Heard from out at the lamp's edge, ahead and off to a side, not on the train.
        double off = (answer.At - n.Train.Frames[0].Origin).Length;
        Assert.InRange(off, T.AnswerDistance * 0.6, T.AnswerDistance * 1.6);
        double heard = n.World.Tick - (T.ShowSeconds - answer.Seconds) * SimConstants.TickRate;
        Assert.Empty(Sent(n));
        n.Run(T.LeadSeconds + 3);
        var first = Assert.Single(Sent(n));
        // The answer comes before the threat, by the lead: the crew hears what's coming.
        Assert.InRange((first.Tick - heard) / SimConstants.TickRate, T.LeadSeconds - 0.1, T.LeadSeconds + 1.1);
        var drawn = d.First!.Value;
        Assert.Equal(first.Kind, drawn.Kind);
        Assert.Equal(DrawCause.Whistle, drawn.Cause);
        Assert.Equal(1, drawn.Actor);
        Assert.True(drawn.Answered);
        // The incident report owns it (C.9, §23.1).
        var line = Assert.Single(IncidentLog.Lines(n.World, 0, 0, _ => false), l => l.Kind == IncidentKind.Drawn);
        Assert.EndsWith("Drawn by the whistle: Dave.", line.Text);
        Assert.Contains("came first", line.Text);
    }

    [Fact]
    public void AQuietCrewIsListenedForUntilThePressurePresses()
    {
        var quiet = Frontier();
        var d = quiet.World.Director!;
        var held = new HashSet<string>();
        double sentAt = -1;
        for (int s = 0; s < 400 && sentAt < 0; s++)
        {
            quiet.Run(1);
            held.Add(d.HeldBecause ?? "");
            if (Sent(quiet).Count > 0)
                sentAt = s;
        }
        Assert.True(sentAt > 0, "nothing came in 400 s");
        Assert.Contains("listening", held);
        var drawn = d.First!.Value;
        Assert.False(drawn.Answered);
        // Nobody did anything louder than driving: the engine's noise, in the driver's name if anyone drove.
        Assert.Equal(DrawCause.Engine, drawn.Cause);

        // Without the hold, the pressure model as it was: the threat comes sooner, and still names a draw.
        var off = Frontier(enemies: Tuning.Enemies with { Director = Tuning.Enemies.Director with { Draw = T with { HoldFirst = false } } });
        double offAt = -1;
        for (int s = 0; s < 400 && offAt < 0; s++)
        {
            off.Run(1);
            if (Sent(off).Count > 0)
                offAt = s;
        }
        Assert.InRange(offAt, 0, sentAt - 1);
    }

    [Fact]
    public void SwitchedOffNothingIsDrawnAndTheReportSaysNothing()
    {
        var n = Frontier(enemies: Tuning.Enemies with { Director = Tuning.Enemies.Director with { Draw = T with { Enabled = false } } });
        n.Run(n.World.Director!.Grace + 3);
        n.Run(3, id => new PlayerIntent { Actions = PlayerActions.Whistle });
        n.Run(8);
        Assert.False(n.World.Answer.Showing);
        Assert.DoesNotContain(n.World.Attribution.Log, i => i.Kind == IncidentKind.Drawn);
    }

    [Fact]
    public void TheSameNightDrawsTheSameFirstThreat()
    {
        FirstThreat? Play()
        {
            var n = Frontier(seed: 5);
            n.Run(n.World.Director!.Grace + 2);
            n.Run(2.5, id => new PlayerIntent { Actions = PlayerActions.Whistle });
            n.Run(12);
            return n.World.Director!.First;
        }
        var a = Play();
        Assert.NotNull(a);
        Assert.Equal(a, Play());
    }

    [Fact]
    public void TheLedgerFadesAndNamesTheBiggestDrawLately()
    {
        var ledger = new DrawLedger();
        ledger.Add(DrawCause.Voices, 2, 2);
        ledger.Add(DrawCause.Whistle, 1, 2);
        // A tie goes to the earlier cause (the whistle), whoever made it.
        Assert.Equal((DrawCause.Whistle, 1, 2.0), ledger.Top);
        ledger.Add(DrawCause.Voices, 2, 0.5);
        Assert.Equal(DrawCause.Voices, ledger.Top.Cause);
        // Half gone in about a half-life.
        for (int s = 0; s < 30; s++)
            ledger.Fade(1, 30);
        Assert.InRange(ledger.Of(DrawCause.Voices, 2), 2.5 * 0.47, 2.5 * 0.52);
        // The Whistler's blowing is nobody's, and nothing at all isn't a draw.
        ledger.Add(DrawCause.None, 3, 10);
        Assert.Equal(DrawCause.Voices, ledger.Top.Cause);
    }

    [Fact]
    public void CargoComeAboardIsDrawnInTheNameOfWhoeverWasNearestIt()
    {
        var n = Frontier();
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, 3, 0, P);
        var d = n.World.Director!;
        n.Run(1);
        var car = n.Train.Dynamics.Consist.Vehicles.First(v => v.Id == 3);
        Assert.Equal(VehicleKind.Cargo, car.Kind);
        // What she left the fortress with isn't a draw.
        Assert.Equal(0, d.Draws.Of(DrawCause.Cargo, 2));
        car.Cargo = CargoKind.Food;
        car.Load = Math.Max(0, car.Load - 0.5);
        n.Run(1);
        car.Load += 0.5;
        n.Run(1);
        Assert.True(d.Draws.Of(DrawCause.Cargo, 2) > 0.5 * T.Weight(DrawCause.Cargo) * 0.9, $"{d.Draws.Of(DrawCause.Cargo, 2)}");
        Assert.Equal(0, d.Draws.Of(DrawCause.Cargo, 1));
    }
}
