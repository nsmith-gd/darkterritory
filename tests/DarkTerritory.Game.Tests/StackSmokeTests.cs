using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The stack's smoke in the world's frame (note 997; the director's playtest of 23 Oct, line 16: "its render tied to the
/// camera rather than a render tied to the world"): a puff stays where it left the stack, drifting, whoever's looking and
/// however the engine turns or changes speed after.
/// </summary>
public class StackSmokeTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);
    static readonly TrainTuning Tuning = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    static readonly CarShape Engine = CarShape.Build(Tuning.Geometry, VehicleKind.Engine, hasCarBehind: true);
    static readonly TrainControls Working = new() { Throttle = 0.3, Reverser = 1 };

    static CarFrame Frame(Double3 origin, Double3 forward, double speed) =>
        new(0, origin, Double3.Cross(forward, Double3.Up).Normalized, Double3.Up, forward * -1, forward * speed, Engine);

    /// <summary>The smoke's puffs, each where it's drawn in the world.</summary>
    static List<Double3> Puffs(Effects fx, CarFrame engine, Double3 eye, double time)
    {
        var mesh = new MeshBuilder();
        fx.Train(mesh, [engine], eye, time, Working, 0.5f, false);
        int smoke = Look.Layer("fx_smoke");
        var puffs = new List<Double3>();
        for (int i = 0; i + 5 < mesh.AlphaFx.Count; i += 6)
            if ((int)mesh.AlphaFx[i].Layer == smoke)
            {
                // A billboard's first corner and its third are either side of its centre.
                var at = (mesh.AlphaFx[i].Position + mesh.AlphaFx[i + 2].Position) / 2;
                puffs.Add(eye + new Double3(at.X, at.Y, at.Z));
            }
        return puffs;
    }

    static double Nearest(Double3 p, List<Double3> among) => among.Min(q => (q - p).Length);

    [Fact]
    public void ThePuffsAreWhereTheyAreWhoeverLooks()
    {
        var fx = new Effects(Look);
        var engine = Frame(new Double3(0, 0, -600), new Double3(0, 0, -1), 20);
        var fromTheRoof = Puffs(fx, engine, engine.Origin + new Double3(0, 4.5, 6), 30);
        var fromTheBallast = Puffs(fx, engine, engine.Origin + new Double3(14, 1.7, -20), 30);
        Assert.NotEmpty(fromTheRoof);
        Assert.Equal(fromTheRoof.Count, fromTheBallast.Count);
        foreach (var p in fromTheBallast)
            Assert.True(Nearest(p, fromTheRoof) < 1e-3, "a puff moved when the eye did");
    }

    [Fact]
    public void APuffStaysWhereItLeftTheStackWhenSheTurnsAndSlows()
    {
        var fx = new Effects(Look);
        const double Dt = 1.0 / 60;
        var origin = new Double3(0, 0, 0);
        var ahead = new Double3(0, 0, -1);
        double time = 100;
        // Three seconds straight at 20 m/s, a frame at a time, the eye riding her roof.
        for (int f = 0; f < 180; f++, time += Dt, origin += ahead * (20 * Dt))
            Puffs(fx, Frame(origin, ahead, 20), origin + new Double3(0, 4.5, 6), time);
        var before = Puffs(fx, Frame(origin, ahead, 20), origin + new Double3(0, 4.5, 6), time);
        // Then (a hard knock: a frame's change, the worst case) she's on a heading 30° off and down to 5 m/s.
        var turned = Vector3.Transform(new Vector3(0, 0, -1), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 6));
        ahead = new Double3(turned.X, turned.Y, turned.Z);
        time += Dt;
        origin += ahead * (5 * Dt);
        var after = Puffs(fx, Frame(origin, ahead, 5), origin + new Double3(0, 4.5, 6), time);
        Assert.True(before.Count > 10);
        // Every puff a frame on is within its drift of one a frame before: none swung round with her, none pulled in.
        foreach (var p in after)
            Assert.True(Nearest(p, before) < 0.3, $"a puff jumped {Nearest(p, before):0.00} m when the engine turned and slowed");
    }

    [Fact]
    public void OneFrameDrawsTheSmokeAsItAlwaysDid()
    {
        // A screenshot's single frame: the stack worked back as if she'd held her speed, so the plume trails behind her.
        var engine = Frame(new Double3(0, 0, -600), new Double3(0, 0, -1), 20);
        var puffs = Puffs(new Effects(Look), engine, engine.Origin + new Double3(14, 1.7, 0), 0.37);
        Assert.NotEmpty(puffs);
        Assert.Contains(puffs, p => p.Z - engine.Origin.Z > 40);
        Assert.Equal(puffs, Puffs(new Effects(Look), engine, engine.Origin + new Double3(14, 1.7, 0), 0.37));
    }
}
