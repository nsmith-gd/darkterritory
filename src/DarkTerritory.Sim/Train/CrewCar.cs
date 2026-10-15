namespace DarkTerritory.Sim.Train;

/// <summary>
/// The big cab as the crew car (ARCHITECTURE §8 note 716; the director, 14 Oct 2026: "the cab itself becomes the crew car.
/// It becomes the place where players hang out together. It becomes the place where coordinated driving happens"): what its
/// fittings show, worked out from state every machine already has (the crew's replicated places, the boiler's pressure, the
/// night's clock). Nothing here changes the sim; presentation draws and sounds it, and the tests pin it.
/// </summary>
public static class CrewCar
{
    /// <summary>The big cab's chart table (note 716), in the engine's frame: its top the run map's. Null on an engine without one.</summary>
    public static Box? Table(CarShape engine) =>
        engine.Solids.Where(s => s.Part == PartKind.ChartTable).Select(s => (Box?)s.Box).FirstOrDefault();

    /// <summary>
    /// The chart table's lamp for a crewmate (note 716: "the navigator sees who's missing"): lit while they're alive and on the
    /// train (standing in any vehicle's frame, the engine's or a car's), dark when they're down on the ground or dead.
    /// <paramref name="parent"/> is their replicated <see cref="Player.PlayerState.Parent"/>; <paramref name="vehicles"/> how
    /// many vehicles the train has.
    /// </summary>
    public static bool Aboard(bool alive, int parent, int vehicles) => alive && parent >= 0 && parent < vehicles;

    /// <summary>
    /// How hard the kettle on the back wall's warm backhead is singing (note 716), 0 to 1: silent below
    /// <see cref="CrewCarLayout.KettleFrom"/> under the redline, rising to full at it and over. The cab's own sign the gauge
    /// is climbing, heard by the crew on the gallery with their backs to the dials.
    /// </summary>
    public static double KettleSong(double pressure, BoilerTuning boiler, CrewCarLayout layout)
    {
        if (layout.KettleFrom <= 0)
            return pressure >= boiler.Redline ? 1 : 0;
        return Math.Clamp((pressure - (boiler.Redline - layout.KettleFrom)) / layout.KettleFrom, 0, 1);
    }

    /// <summary>
    /// The cuckoo clock's bird (note 716): out over the last <paramref name="stirSeconds"/> before dawn (enemies.json
    /// wakers.stirSeconds, when the stir starts), on each <see cref="CrewCarLayout.CuckooEvery"/> seconds before it, for
    /// <see cref="CrewCarLayout.CuckooOutSeconds"/>. Null while it's in; else how far through its call it is (0 to 1). Three
    /// calls in the last three minutes: dawn is coming, and the Wakers with it.
    /// </summary>
    public static double? Cuckoo(double dawnIn, double stirSeconds, CrewCarLayout layout)
    {
        double every = layout.CuckooEvery, out_ = layout.CuckooOutSeconds;
        if (every <= 0 || out_ <= 0 || dawnIn <= 0 || dawnIn > stirSeconds + 1e-9)
            return null;
        // The call on the minute at or after this moment (counting down): out from it for out_ seconds.
        double toCall = dawnIn - Math.Floor(dawnIn / every) * every;
        double since = toCall == 0 ? 0 : every - toCall;
        if (dawnIn + since > stirSeconds + 1e-9 || since >= out_)
            return null;
        return since / out_;
    }
}
