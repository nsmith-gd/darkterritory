using Ballast;
using DarkTerritory.Game;
using DarkTerritory.Sim.LineGen;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Where the run ends, told (queue #495, ARCHITECTURE §8 note 765; the director, 13 Oct: "It's unclear if I have to go right
/// to the end of the line or if I can just stop the train inside the town"). The last town's name says END OF THE RUN as it
/// comes up; in through its gate the HUD says to bring the whole train in, then to stop anywhere; the route card says it.
/// </summary>
public sealed class EndOfTheRunTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Sim.Route.Route Route = Routes.Generate(Content, "frontier:7", 6);

    [Fact]
    public void InThroughTheGateItSaysBringItAllInThenStop()
    {
        Assert.False(Route.Plan!.Terminus.Silent);
        var approach = Staging.Terminus(Content, Route, 6, "approach");
        Assert.Null(Hud.EndLine(approach.Route, approach.World, approach.Train));
        var inside = Staging.Terminus(Content, Route, 6, "inside");
        Assert.Equal(("KEEP COMING: THE WHOLE TRAIN INSIDE", false), Hud.EndLine(inside.Route, inside.World, inside.Train));
        var home = Staging.Terminus(Content, Route, 6, "home");
        Assert.Equal(("STOP ANYWHERE: THE RUN ENDS HERE", true), Hud.EndLine(home.Route, home.World, home.Train));
        // Stopped there, the run's delivered: nothing more to say.
        home.Controls = new Sim.Train.TrainControls { Reverser = 1, Brake = 1 };
        for (int t = 0; t < 30 * Sim.SimConstants.TickRate && home.World.Run is { Over: false }; t++)
        {
            home.Train.Dynamics.Velocity *= 0.9;
            home.Step(default);
        }
        Assert.Equal(Sim.Run.RunEnd.Delivered, home.World.Run!.End);
        Assert.Null(Hud.EndLine(home.Route, home.World, home.Train));
    }

    [Fact]
    public void TheRouteCardSaysWhereTheRunEnds()
    {
        var o = new Ballast.Render.Overlay();
        LineGen.PlanHud.RouteCard(o, 480, 1200, Route.Plan!);
        Assert.EndsWith("stop with the whole train inside.", LineGen.PlanHud.EndOfTheRunCard(Route.Plan!));
        Assert.Contains(Route.Plan!.RouteCard.Timetable, t => t.Kind == "terminus");
    }
}
