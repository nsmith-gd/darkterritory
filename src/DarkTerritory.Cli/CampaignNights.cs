using System.Diagnostics;
using System.Globalization;
using Ballast;
using DarkTerritory.Game;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Towns;

/// <summary>
/// `dt campaign nights` (ARCHITECTURE §8 note 613): a campaign's nights back to back in one process, each hosted as the app
/// hosts it (<see cref="NetPlaySession.HostGame"/> with the crew's bots, the slot's setup, settled and arrived as the app's
/// Autosave does), so whatever one night leaves behind is still there for the next. Each night: the contract, the town it
/// left and the one it got to (by name, seed and burnt houses), how it ended, what it paid, its bots at the end, and the
/// process's memory once the night's let go. --lose-at s forces a night lost at dawn s seconds in (--lose-night n: only the
/// nth).
/// </summary>
static class CampaignNights
{
    public static object Run(string content, SaveSlots saves, int slot, string[] args)
    {
        var t = DataFile.Load<CampaignTuning>(Path.Combine(content, CampaignTuning.File));
        var runTuning = DataFile.Load<RunTuning>(Path.Combine(content, RunTuning.File));
        var identities = LineGenContent.Cached(content).Config.Tiers.Fortress.Identities;
        int nights = (int)Opt(args, "--nights", 3), bots = (int)Opt(args, "--bots", 3), loseNight = (int)Opt(args, "--lose-night", 0);
        double loseAt = Opt(args, "--lose-at", -1), limit = Opt(args, "--seconds", 3600);
        // The player at the host's own seat (note 616): "bot", played by one more of the crew's bots, as near as the tools come to
        // somebody playing (the default); "idle", nobody at the keys, which the bots pay for (they rescue, wait for and count on
        // a crewmate who never moves, and the director's census hunts them).
        bool humanBot = Str(args, "--human", "bot") == "bot";
        var combat = DataFile.Load<DarkTerritory.Sim.Combat.CombatTuning>(Path.Combine(content, DarkTerritory.Sim.Combat.CombatTuning.File));
        var playerTuning = DataFile.Load<DarkTerritory.Sim.Player.PlayerTuning>(Path.Combine(content, DarkTerritory.Sim.Player.PlayerTuning.File));
        var results = new List<object>();
        for (int n = 1; n <= nights; n++)
        {
            var s = saves.Load(slot) ?? throw new InvalidOperationException($"slot {slot} is empty: dt campaign new --slot {slot}");
            // Note 632: taken at dawn, the campaign's over, and the nights stop with it.
            if (s.Over)
                break;
            var contract = Campaign.Offers(t, runTuning, s)[(int)Opt(args, "--contract", 0)];
            s = Campaign.Begin(s, contract);
            // As the app's Start has it (HostedAsApp.Campaign): the slot's night, from the town the crew's in.
            var setup = HostedAsApp.Campaign(s, contract, enemies: !args.Contains("--no-enemies"));
            var clock = Stopwatch.StartNew();
            RunReport? report;
            object night;
            using (var session = NetPlaySession.HostGame(content, setup, port: null, bots: bots))
            {
                bool lose = loseAt >= 0 && (loseNight == 0 || loseNight == n);
                var human = humanBot ? DarkTerritory.Sim.Bots.BotCrew.Make(bots, bots + 1, session.BotCrew?.Calls, combat, playerTuning, (int)(session.Route?.Seed ?? 1)) : null;
                int ticks = 0;
                while (session.World.Run is not { Over: true } && ticks < limit * SimConstants.TickRate)
                {
                    if (lose && session.Host!.World.Run is { Seconds: var at } && at >= loseAt)
                        session.Host.World.TakenAtDawn = true;
                    session.Step(human is null ? default : DarkTerritory.Sim.Bots.BotCrew.Think(session.Client, human, (uint)ticks, session.BotCrew?.Calls));
                    ticks++;
                }
                report = session.World.Run?.Report;
                var crew = session.BotCrew?.Bots.Select(b => session.Host!.Players.First(p => p.Id == b.Session.PlayerId).State).ToList() ?? [];
                night = new
                {
                    route = setup.Route,
                    from = session.World.Town?.Plan.Name,
                    fromBurnt = session.World.Town?.Plan.Burnt ?? 0,
                    to = session.World.Arrival?.Plan.Name,
                    toBurntOnArrival = session.World.Arrival?.Plan.Burnt ?? 0,
                    end = report?.End.ToString() ?? "unfinished",
                    seconds = Math.Round(session.World.Run?.Seconds ?? 0, 1),
                    bots = new { count = crew.Count, alive = crew.Count(c => c.Alive), aboard = crew.Count(c => c.Alive && c.Parent >= 0) },
                    broughtIn = report?.BroughtIn ?? 0,
                    net = report?.Net ?? 0,
                    // Where the night's scrip went (spec F.2's settlement): what the freight and mail made, and what it cost.
                    money = report is null ? null : new
                    {
                        report.Gross,
                        report.Mail,
                        report.CoalCost,
                        report.AmmoCost,
                        report.RepairCost,
                        report.CrewLossFees,
                        report.BodyRefunds,
                        report.Recovery,
                        report.BroughtInFees,
                        report.CarsLost,
                        report.CrewLost,
                        report.Deaths,
                    },
                    lines = report?.Lines.Select(l => $"{l.Kind}: {l.Text}").ToArray(),
                    wall = Math.Round(clock.Elapsed.TotalSeconds, 1),
                };
                // As the app's Autosave settles it: the night into the slot, the town it left, the town it got to.
                if (report is not null)
                {
                    var settled = Campaign.Settle(s, report, t) with { Music = session.MusicBag ?? s.Music, LastTown = session.World.Town?.Plan.Culture ?? s.LastTown };
                    if (session.Route is { } arrived)
                        settled = Campaign.Arrived(settled, report, TownAt.Terminus(arrived, identities));
                    saves.Save(settled);
                }
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var after = saves.Load(slot)!;
            results.Add(new
            {
                n,
                night,
                scrip = Math.Round(after.Scrip),
                after.Cars,
                after.Runs,
                after.Over,
                town = after.Town?.Spec,
                board = Campaign.Offers(t, runTuning, after).Select(o => o.Route).ToArray(),
                managedMB = Math.Round(GC.GetTotalMemory(false) / 1048576.0, 1),
                workingSetMB = Math.Round(Process.GetCurrentProcess().WorkingSet64 / 1048576.0, 1),
            });
            Console.Error.WriteLine($"night {n}: {results[^1]}");
        }
        return new { slot, nights = results };
    }

    static string Str(string[] args, string name, string fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }

    static double Opt(string[] args, string name, double fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && double.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }
}
