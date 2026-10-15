using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>The Mouse Moose staged for a look (note 790).</summary>
public static partial class Staging
{
    public const int MouseMooseId = 790;

    /// <summary>
    /// The staged Mouse Moose (<c>dt screenshot --mousemoose</c>; note 790) in the headlamp's spill off the line's left, 14-20 m
    /// up from the engine: <c>yard</c> one creeping in, one with its teeth in a load and tugging it off, one down its hole (its
    /// antlers' tips over the earth); <c>drag</c> close to, the one tugging. The load stands in for a crane's casting (a heavy
    /// crate in <paramref name="bodies"/>: the view has no crane); the paces in <paramref name="paces"/>.
    /// </summary>
    public static List<Enemy> MouseMoose(List<Enemy> threats, TrainOnLine train, string content, string mode, out Sim.Physics.Bodies bodies,
        out Dictionary<int, float> paces)
    {
        threats.RemoveAll(e => e is MouseMoose);
        var t = DataFile.Load<EnemyTuning>(Path.Combine(content, EnemyTuning.File)).MouseMoose;
        var made = new Sim.Physics.Bodies();
        var paced = new Dictionary<int, float>();
        var tangent = train.Line.Sample(train.Dynamics.Distance + 17).Tangent;
        var away = Double3.Cross(tangent, Double3.Up).Normalized * -1;
        double Yaw(Double3 way) => Math.Atan2(-way.X, -way.Z);
        int n = 0;
        void Add(Double3 at, double yaw, MouseMooseMode m, SpinePhase phase, double seconds, float pace)
        {
            var mm = new MouseMoose(MouseMooseId + n);
            mm.Restore(phase, seconds, 1, Enemy.Loose, at, train.Dynamics.Distance, yaw, (double)m, 0, 0);
            paced[MouseMooseId + n] = pace;
            n++;
            threats.Add(mm);
        }
        // The load it tugs, and it at the load's far side from the line, facing it, braced back.
        var load = Lineside(train, mode == "drag" ? 18.0 : 17.0, -4.5);
        made.SpawnCargo(load, train.Dynamics.Distance + 17);
        Add(OnGround(train, load + away * (0.5 + t.Grip)), Yaw(away * -1), MouseMooseMode.Drag, SpinePhase.Commit, 1.1, (float)t.Drag);
        if (mode != "drag")
        {
            var creeping = Lineside(train, 14.0, -6.5);
            Add(creeping, Yaw(Lineside(train, 15.5, -3.0) - creeping), MouseMooseMode.Creep, SpinePhase.Telegraph, 0.7, (float)t.Creep);
            Add(Lineside(train, 20.5, -9.0), Yaw(away * -1 + tangent * -0.4), MouseMooseMode.Hide, SpinePhase.BreakOff, 2, 0);
        }
        var tuning = DataFile.Load<TrainTuning>(Path.Combine(content, TrainTuning.File));
        for (int i = 0; i < 40; i++)
            made.Step(train, tuning, _ => null);
        (bodies, paces) = (made, paced);
        return threats;
    }

    /// <summary>The <c>mousemoose</c> and <c>mousemoosedrag</c> views: from beside the engine out at the staged Mouse Moose in
    /// the headlamp's spill, or crouched close to the one tugging its load.</summary>
    public static Camera MouseMooseCamera(TrainOnLine train, string view)
    {
        // Front-quarter on, close, to the one creeping in: its face, its eyes and its antlers.
        if (view == "mousemooseface")
            return Camera.LookAt(Lineside(train, 12.6, -4.6) + Double3.Up * 0.75, Lineside(train, 14.0, -6.5) + Double3.Up * 0.4, 38);
        if (view == "mousemoosedrag")
            return Camera.LookAt(Lineside(train, 16.2, -6.6) + Double3.Up * 0.55, Lineside(train, 18.0, -5.4) + Double3.Up * 0.35, 48);
        return Camera.LookAt(Lineside(train, 10.0, -1.5) + Double3.Up * 1.4, Lineside(train, 17.0, -5.5) + Double3.Up * 0.3, 56);
    }
}
