using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Your own radio, worn at your chest (note 997; the director's playtest of 23 Oct, line 17: "When I look down, the radio kind
/// of jitters as I move ... We don't need to show that"): not drawn through your own eyes, and still drawn on you for the crew.
/// </summary>
public class OwnRadioTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);

    [Fact]
    public void YourOwnRadioIsntDrawnThroughYourEyesButTheCrewSeeIt()
    {
        var s = new PrototypeSession(Content, "test-loop", 4);
        s.Player = PlayerMotor.SpawnOnRoof(s.Train, 2, 3, s.PlayerTuning);
        var radio = s.World.Bodies.SpawnCrate(s.Train, 2, new Double3(0, s.Train.Frames[2].Shape.RoofHeight + 1.3, 3), Sim.Physics.BodyKind.Radio);
        radio.Carrier = 1;
        var eye = PlayerMotor.WorldPosition(s.Player, s.Train) + Double3.Up * Eyes.Height;
        int Drawn(int viewer)
        {
            var scene = new GreyboxScene { DrawDistance = 400, Look = Look, Time = 0.37, Bodies = s.World.Bodies.All, HeldHere = (viewer, eye, 0) };
            var mesh = new MeshBuilder();
            scene.Build(mesh, s.Train.Line, s.Train.Frames, s.Train.Dynamics.Distance, eye);
            return mesh.Instances.Count + mesh.Vertices.Length;
        }
        // Behind the wearer's own eyes, one piece fewer than behind anyone else's at the same spot.
        Assert.True(Drawn(1) < Drawn(2));
    }
}
