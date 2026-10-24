using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The wilds heard (note 1006; the director, 23 Oct 2026: "We should also have some unique sound design out there in the wilds
/// for when players stop in a forest to deal with an issue"): stood in the big forests (<see cref="DeepWoods"/>), the night's
/// open country gives way to the deep woods' own: their hush (tools/audio/cues.py "world-woods"), far calls, a branch going
/// somewhere close, and now and then something big moving out in the trees. Only with the train stood: running, the train
/// is all you hear. All of it the world's (tier 6) and quiet under the tells (spec A.3; AudioTests' woods audit).
/// </summary>
public sealed partial class GameAudio
{
    /// <summary>How far the woods have come in round the stood train (0..1): up over <see cref="WoodsInSeconds"/> once it stands, gone in <see cref="WoodsOutSeconds"/> once it moves.</summary>
    double _woods, _nextWoodsFar, _nextWoodsBranch, _nextWoodsHeavy;
    const double WoodsInSeconds = 6, WoodsOutSeconds = 2;
    /// <summary>Under this (m/s) the train's stood, and the woods are heard.</summary>
    const double WoodsStoodBelow = 0.5;

    /// <summary>world-woods: the deep woods round a train stood in them, by how deep in them its engine stands.</summary>
    void WorldWoods(TrainOnLine train, Places places, Run? run, Double3 ear, double front, bool tunnel, bool underground, double dt)
    {
        double deep = run is { Phase: RunPhase.Underway or RunPhase.AtFacility } && places.Plan is { } plan && !double.IsNaN(front)
            ? DeepWoods.Of(plan).Depth(front) : 0;
        bool stood = Math.Abs(train.Dynamics.Speed) < WoodsStoodBelow;
        double want = tunnel || underground || !stood ? 0 : deep;
        _woods = Math.Clamp(want, _woods - dt / WoodsOutSeconds, _woods + dt / WoodsInSeconds);
        if (_woods <= 0.01)
        {
            _nextWoodsFar = _nextWoodsBranch = _nextWoodsHeavy = 0;
            return;
        }
        float occlusion = Math.Max(Occlusion(PlayerMotor.Outside), AirWalls());
        HoldLevel("world-woods.hush", 0, ear + Double3.Up * 3, occlusion, _woods);
        var t = places.Line.Sample(Math.Clamp(front, 0, places.Line.Length));
        var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
        // Out in the trees beside the line: either side, along the train, out past the first trunks.
        Double3 Out(double near, double far, double along) =>
            t.Position + right * ((OutsideOdds() < 0.5 ? -1 : 1) * (near + (far - near) * OutsideOdds()))
            + t.Tangent * ((OutsideOdds() - 0.5) * 2 * along) + Double3.Up * 1.5;
        // The first of each a while after the woods come in, then each on its own clock: a far call every 15-35 s, a branch
        // every 6-16 s, something big every 40-90 s.
        if (_nextWoodsFar == 0)
            (_nextWoodsFar, _nextWoodsBranch, _nextWoodsHeavy) = (_time + 4 + 8 * OutsideOdds(), _time + 2 + 4 * OutsideOdds(), _time + 15 + 20 * OutsideOdds());
        if (_time >= _nextWoodsFar)
        {
            double a = OutsideOdds() * 2 * Math.PI, r = 150 + 150 * OutsideOdds();
            Cue("world-woods.far", ear + new Double3(Math.Cos(a) * r, 8 + 10 * OutsideOdds(), Math.Sin(a) * r), occlusion, (float)(_woods * (0.6 + 0.4 * OutsideOdds())));
            _nextWoodsFar = _time + 15 + 20 * OutsideOdds();
        }
        if (_time >= _nextWoodsBranch)
        {
            Cue("world-woods.branch", Out(12, 45, 40), occlusion, (float)(_woods * (0.5 + 0.5 * OutsideOdds())));
            _nextWoodsBranch = _time + 6 + 10 * OutsideOdds();
        }
        if (_time >= _nextWoodsHeavy)
        {
            Cue("world-woods.heavy", Out(40, 80, 60), occlusion, (float)_woods);
            _nextWoodsHeavy = _time + 40 + 50 * OutsideOdds();
        }
    }
}
