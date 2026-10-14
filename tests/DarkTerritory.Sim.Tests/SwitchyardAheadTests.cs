using Ballast;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 618 (queue #346; D1's find from the campaign of #345, Fort Yeld): a switchyard's cars are picked up ahead of the engine
/// (note 187), and their roofs are no way back to the train's. On local:9491 with a campaign's three cars and a crew of three,
/// the walker climbed onto one at Wren Yard, and from then on it and the gunner rode in the cab: at the gate's stand nobody
/// cleared the train, and seven creatures were brought into Fort Yeld. The bots board the cars behind the engine.
/// </summary>
public class SwitchyardAheadTests
{
    [Fact]
    public void AThreeCarCrewNeverBoardsTheCarsAheadOfTheEngineAndClearsTheTrainAtTheGate()
    {
        var content = DataFile.FindContentRoot();
        const int cars = 3;
        var route = LineGen.Routes.Generate(content, "local:9491", cars);
        double gate = route.GateOr(Tuning.Route.YardLength);
        double from = Tuning.Run.DepartFrom(gate, Train.Consist.Uniform(Tuning.Train, cars, 1).LengthMetres);
        int ahead = 0;
        var report = Harness.Run(route.Build(), Tuning.Train, Tuning.Player, new HarnessOptions
        {
            Observe = (_, crew, world) =>
            {
                foreach (var (_, s) in crew)
                    if (s.Alive && s.Parent > 0 && StopHandAhead(world.Train, s.Parent))
                        ahead++;
            },
            Bots = 3,
            Cars = cars,
            Seconds = 1500,
            Seed = 1,
            Link = Ballast.Net.LinkConditions.Perfect,
            StartDistance = from,
            WalkAboard = from < gate,
            Combat = Tuning.Combat,
            Enemies = Tuning.Enemies,
            Route = route,
            Run = Tuning.Run,
            Facilities = DataFile.Load<FacilityTuning>(Path.Combine(content, FacilityTuning.File)),
            Sight = DataFile.Load<Route.SightTuning>(Path.Combine(content, Route.SightTuning.File)),
            YardLength = gate,
            Holdouts = Tuning.Holdouts,
        }, Tuning.Boiler);
        var run = Assert.IsType<RunReport>(report.Run);
        Assert.Equal(RunEnd.Delivered, run.End);
        Assert.Equal(0, ahead);
        // The Knotter in the engine's own gap and the Brakeman (on whichever car he's on) may still come in; not the pack.
        Assert.True(run.BroughtIn <= 2, $"{run.BroughtIn} brought in");
    }

    static bool StopHandAhead(Train.TrainOnLine train, int car) => Bots.StopHand.AheadOfTheEngine(train, car);
}
