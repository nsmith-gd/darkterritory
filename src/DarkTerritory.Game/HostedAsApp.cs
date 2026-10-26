using Ballast;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>
/// A night for the headless harness as the app hosts it (ARCHITECTURE §8 note 616; D1: "the director plays with the app's
/// bots, and every bot sweep we run is the harness's"): from the same <see cref="SessionSetup"/> <see cref="NetPlaySession.HostGame"/>
/// takes, the same line, tunings (upgrades, spare kits, stores), consist and freight; the train at the gate as the app stands
/// it (run.json departShortOfGateM), the crew aboard as the app boards them, cab first and the rest on the roofs; the
/// director seeded by the route, as the app's is; and the run, its facilities, Holdouts, lineside, upkeep and finds. What
/// stays the harness's: its perfect lock-step link, and that every crewmate is a bot (the app's own seat is somebody playing).
/// </summary>
public static class HostedAsApp
{
    /// <summary>
    /// A campaign night's setup, as the app's Start makes it (spec E: the host owns the campaign): the contract's route from
    /// the town the crew's in (note 591), the slot's cars, upgrades, spare kits and stores, who everyone is (note
    /// 181), the music's bag, the last town's custom (note 281) and the towns the crew has burnt houses in (note 612).
    /// </summary>
    public static SessionSetup Campaign(Sim.Campaign.CampaignState s, Sim.Campaign.Contract contract, bool enemies) =>
        new(Route: Sim.Campaign.Campaign.RouteOf(s, contract), Cars: s.Cars, Enemies: enemies)
        {
            Upgrades = s.Upgrades,
            EngineName = s.EngineName,
            SpareKits = s.SpareKits,
            MusicBag = s.Music,
            Identities = s.Identities,
            // GDD §9 (note 182): the stores bought for the night. A contract is just a route (note 637): no freight of its own.
            Powder = s.Stores.Powder,
            SpareLamps = s.Stores.Lamps,
            SpareExtinguishers = s.Stores.Extinguishers,
            Flares = s.Stores.Flares,
            Fusees = s.Stores.Fusees,
            LastTown = s.LastTown,
            Burnt = s.Burnt.Count > 0 ? s.Burnt : null,
            // Note 1024: and how cold each is to the crew.
            Cold = s.Standing.Count > 0 ? s.Standing : null,
            CampaignNight = true,
            Scrip = s.Scrip,
            // Note 721: whose logbook the night goes in, and which night it is.
            Crew = s.Name,
            CrewKey = Sim.Campaign.Logbook.Key(s),
            Night = s.Runs + 1,
            // Note 731: the crew's sigil, on the engine's side and every mask.
            Sigil = s.Sigil,
            // Note 1039: the corruption grown on the train, where the creatures got on.
            Growth = s.Growth is { Any: true } g ? g.Spec : null,
        };

    /// <summary>The harness's arguments for <paramref name="setup"/>'s night with <paramref name="bots"/> bots, for up to
    /// <paramref name="seconds"/> of it.</summary>
    public static (RailLine Line, TrainTuning Train, PlayerTuning Player, HarnessOptions Options, BoilerTuning Boiler) For(
        string content, SessionSetup setup, int bots, double seconds)
    {
        var loadout = setup.Loadout(content);
        var route = Sim.LineGen.Routes.Generate(content, setup.Route ?? throw new ArgumentException("a hosted night is on a route", nameof(setup)), setup.Cars);
        var routeTuning = RouteTuning.Load(content);
        var runTuning = DataFile.Load<Sim.Run.RunTuning>(Path.Combine(content, Sim.Run.RunTuning.File));
        double gate = route.GateOr(routeTuning.YardLength);
        var consist = Consist.Uniform(loadout.Train, setup.Cars, runTuning.DepartureLoad).Carrying(setup.Cargo);
        var options = new HarnessOptions
        {
            Bots = bots,
            Cars = setup.Cars,
            Seconds = seconds,
            Link = Ballast.Net.LinkConditions.Perfect,
            // As NetPlaySession's world stands the train and boards the crew.
            StartDistance = runTuning.DepartFrom(gate, consist.LengthMetres),
            WalkAboard = false,
            DirectorSeed = route.Seed,
            Combat = loadout.Combat,
            Enemies = setup.Enemies ? loadout.Enemies : null,
            Route = route,
            Run = runTuning,
            YardLength = gate,
            Facilities = DataFile.Load<Sim.Run.FacilityTuning>(Path.Combine(content, Sim.Run.FacilityTuning.File)),
            Holdouts = DataFile.Load<Sim.Run.HoldoutTuning>(Path.Combine(content, Sim.Run.HoldoutTuning.File)),
            Sight = DataFile.Load<SightTuning>(Path.Combine(content, SightTuning.File)),
            Upkeep = DataFile.Load<UpkeepTuning>(Path.Combine(content, UpkeepTuning.File)),
            Loot = DataFile.Load<Sim.Stops.LootTuning>(Path.Combine(content, Sim.Stops.LootTuning.File)),
            Cargo = setup.Cargo,
        };
        return (route.Build(), loadout.Train, DataFile.Load<PlayerTuning>(Path.Combine(content, PlayerTuning.File)), options, loadout.Boiler);
    }
}
