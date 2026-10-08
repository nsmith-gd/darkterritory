using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The extinguisher heard on the fire (queue #205, ARCHITECTURE §8 note 469; the director, 8 Oct 2026: holding it on a fire
/// "still doesnt feel like its doing anything"; D1's note 467: the cell aimed at is out in a second). The jet's own sound is
/// the extinguisher's and the crackle is the fire's; this is where they meet, off the fire's replicated cells
/// (<see cref="CarFire.Heat"/>) and the sprayer's aim as the host finds it (<see cref="FireGrid.Hit"/>), so every machine
/// hears it: the jet on a burning cell, a cell knocked out under it, and the whole fire out.
/// </summary>
public sealed partial class GameAudio
{
    // How long after the last jet on a car its cells going out are the jet's doing (a cell burnt out on its own is the
    // fire's, and goes quietly), and how many cells going out in one tick are heard (a wet patch collapsing at once).
    const double DousedWithin = 1.0;
    const int CellsOutAtOnce = 2;

    /// <summary>Who's spraying this tick: the carrier and the extinguisher (filled by the extinguisher's own sounds).</summary>
    readonly List<(int Carrier, int Body)> _jets = [];
    /// <summary>Each fire as last heard (by its enemy id): its car, its heart on the car, its cells; and when a jet was last on each car.</summary>
    readonly Dictionary<int, (int Car, Double3 Heart, double[] Heat)> _fireWas = [];
    readonly Dictionary<int, double> _jetOnCar = [];

    void FireDoused(World world)
    {
        var train = world.Train;
        var tuning = world.Enemies?.CarFire ?? new CarFireTuning();
        double eye = train.Dynamics.Tuning.Pick.EyeHeight;
        var fires = world.ActiveEnemies.OfType<CarFire>().Where(f => !f.Gone && f.Attached >= 0 && f.Attached < train.Frames.Count).ToList();
        // The jet on the fire: the cell each sprayer's aimed at, as the host's Spraying finds it, while it burns.
        foreach (var (carrier, body) in _jets)
        {
            if (CrewStates.Where(p => p.Id == carrier).Select(p => (PlayerState?)p.State).FirstOrDefault() is not { Parent: >= 0 } s)
                continue;
            if (fires.FirstOrDefault(f => f.Attached == s.Parent) is not { } fire || FireGrid.Of(train, fire.Attached, tuning.CellSize) is not { } grid)
                continue;
            _jetOnCar[fire.Attached] = _time;
            int cell = grid.Hit(s.Position + Double3.Up * eye, Bookmarks.Forward(s.Yaw, s.Pitch), tuning.SprayReach);
            if (cell >= 0 && cell < fire.Heat.Length && fire.Heat[cell] > 0)
                HoldLevel("crew-extinguisher.on-fire", body, train.Frames[fire.Attached].ToWorld(grid.Centre[cell]), Occlusion(fire.Attached),
                    0.6 + 0.4 * Math.Clamp(fire.Heat[cell], 0, 1));
        }
        _jets.Clear();
        // Cells knocked out under the jet: alight as last heard, nothing now, with a jet on its car lately.
        foreach (var fire in fires)
        {
            var heat = fire.Heat;
            var was = _fireWas.TryGetValue(fire.Id, out var w) ? w.Heat : null;
            _fireWas[fire.Id] = (fire.Attached, fire.Local, (double[])heat.Clone());
            if (was is null || was.Length != heat.Length || !Doused(fire.Attached) || FireGrid.Of(train, fire.Attached, tuning.CellSize) is not { } grid)
                continue;
            int heard = 0;
            for (int i = 0; i < heat.Length && heard < CellsOutAtOnce; i++)
                if (was[i] > 0 && heat[i] <= 0)
                {
                    Cue("crew-extinguisher.cell-out", train.Frames[fire.Attached].ToWorld(grid.Centre[i]), Occlusion(fire.Attached));
                    heard++;
                }
        }
        // The fire out: its last cell gone, it's gone the same tick (CarFire.Out). Put out under a jet, the last long hiss
        // where its heart was; burnt out, or the car lost, it goes as the fire does.
        foreach (var id in _fireWas.Keys.Where(id => !fires.Any(f => f.Id == id)).ToList())
        {
            var (car, heart, last) = _fireWas[id];
            _fireWas.Remove(id);
            if (last.Any(h => h > 0) && Doused(car) && car < train.Frames.Count)
                Cue("crew-extinguisher.fire-out", train.Frames[car].ToWorld(heart), Occlusion(car));
        }
    }

    bool Doused(int car) => _time - _jetOnCar.GetValueOrDefault(car, double.NegativeInfinity) <= DousedWithin;
}
