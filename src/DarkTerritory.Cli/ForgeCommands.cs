using System.Net;
using Ballast;
using DarkTerritory.Game;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Route;

/// <summary>
/// `dt forge order` (queue #752, ARCHITECTURE §8 note 1054): a joiner buys at the forge, headless. A campaign night hosted on
/// loopback as the app hosts one, joiners aboard; each joiner orders every upgrade named, all in the same tick; the host buys
/// them from the crew's scrip in the order they came (GDD §11) and answers each, and the joiners say they're done.
/// <list type="bullet">
/// <item><c>dt forge order [--joiners 2] [--ids tenderCapacity,radioRange] [--scrip 900] [--route frontier:7]</c>: each
/// answer (to whom, bought or the words of the refusal), the crew's scrip before and after, what it was charged, and whether
/// the yard would be hosted again to fit it.</item>
/// </list>
/// </summary>
static class ForgeCommands
{
    public static object Order(string content, string[] args)
    {
        var t = DataFile.Load<CampaignTuning>(Path.Combine(content, CampaignTuning.File));
        string[] ids = Str(args, "--ids", "tenderCapacity").Split(',', StringSplitOptions.RemoveEmptyEntries);
        int joiners = Math.Clamp((int)Opt(args, "--joiners", 2), 1, 7);
        var crew = Campaign.New(t, 1, "Forge Crew", 9) with { Scrip = Opt(args, "--scrip", 900) };
        double before = crew.Scrip;
        var setup = HostedAsApp.Campaign(crew, new Contract(RouteTier.Frontier, 7, 450), enemies: false);
        if (Str(args, "--route", "") is { Length: > 0 } route)
            setup = setup with { Route = route };
        using var host = NetPlaySession.HostGame(content, setup, port: 0);
        var at = new IPEndPoint(IPAddress.Loopback, host.Port);
        var aboard = Enumerable.Range(0, joiners).Select(_ => NetPlaySession.Join(content, at, () => host.Step(default))).ToList();
        try
        {
            void Tick()
            {
                host.Step(default);
                foreach (var j in aboard)
                    j.Step(default);
            }
            for (int i = 0; i < SimConstants.TickRate / 2; i++)
                Tick();
            Purchase Buy(string id)
            {
                var p = Campaign.BuyUpgrade(t, crew, id);
                if (p.Ok)
                    crew = p.State;
                return p;
            }
            foreach (var j in aboard)
            {
                foreach (var id in ids)
                    j.Order(id);
                j.OrderDone();
            }
            var answers = new List<object>();
            bool refit = false;
            int expected = joiners * ids.Length;
            for (int i = 0; i < SimConstants.TickRate * 3 && (answers.Count < expected || !refit && crew.Upgrades.Count > 0); i++)
            {
                Tick();
                refit |= host.ServeOrders(t, () => crew, Buy);
                foreach (var j in aboard)
                    while (j.TakeOrderAnswer() is { } a)
                        answers.Add(new { joiner = j.PlayerId, a.Id, bought = a.Ok, words = a.Words, scripAfter = Math.Round(a.Scrip, 2) });
            }
            return new
            {
                protocol = Protocol.Version,
                joiners,
                ordered = ids,
                answers,
                scripBefore = before,
                scripAfter = Math.Round(crew.Scrip, 2),
                charged = Math.Round(before - crew.Scrip, 2),
                upgrades = crew.Upgrades,
                refit,
            };
        }
        finally
        {
            foreach (var j in aboard)
                j.Dispose();
        }
    }

    static string Str(string[] args, string name, string fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }

    static double Opt(string[] args, string name, double fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? double.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture) : fallback;
    }
}
