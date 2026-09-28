namespace DarkTerritory.Sim.Train;

/// <summary>The engine plus an ordered list of cars. Load is 0 (empty) to 1 (full) per car.</summary>
public sealed class Consist
{
    readonly List<double> _loads = new();

    public Consist(TrainTuning tuning) => Tuning = tuning;

    /// <summary>Swappable so edited tuning files apply to a running train.</summary>
    public TrainTuning Tuning { get; set; }
    public int CarCount => _loads.Count;
    public IReadOnlyList<double> Loads => _loads;

    public static Consist Uniform(TrainTuning tuning, int cars, double load)
    {
        var c = new Consist(tuning);
        for (int i = 0; i < cars; i++)
            c.AddCar(load);
        return c;
    }

    public void AddCar(double load) => _loads.Add(Math.Clamp(load, 0, 1));

    /// <summary>Uncouples everything from <paramref name="index"/> back. Returns how many cars were left behind.</summary>
    public int UncoupleFrom(int index)
    {
        int removed = _loads.Count - index;
        if (removed > 0)
            _loads.RemoveRange(index, removed);
        return Math.Max(0, removed);
    }

    public double MassTonnes
    {
        get
        {
            var m = Tuning.Mass;
            double total = m.EngineTonnes;
            foreach (var load in _loads)
                total += m.EmptyCarTonnes + load * (m.LoadedCarTonnes - m.EmptyCarTonnes);
            return total;
        }
    }

    /// <summary>Mass of this many cars when fully loaded; the reference for the performance table.</summary>
    public static double LoadedMassTonnes(TrainTuning t, int cars) => t.Mass.EngineTonnes + cars * t.Mass.LoadedCarTonnes;

    public double LengthMetres
    {
        get
        {
            var g = Tuning.Geometry;
            return g.EngineLength + CarCount * (g.CarLength + g.CouplingGap);
        }
    }
}
