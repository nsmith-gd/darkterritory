namespace DarkTerritory.Sim.Run;

/// <summary>
/// A find sold to the town's buyer (ARCHITECTURE §8 note 1027): who sold it, what it was, what the tally would have paid for
/// it, what the buyer paid, and whether the night's tally had been read by then (run seconds).
/// </summary>
public sealed record Sale(int Player, string Item, double Worth, double Paid, bool AfterTally, double Seconds);

/// <summary>
/// The buyer in the square (GDD §3.1's "turning loot into scrip in the town itself"; note 1027; towns.json <c>buyer</c>), host
/// side: a find sold comes out of the world and its scrip into the night's takings. Before the tally it's in the tally
/// (<see cref="RunReport.Sold"/>); after it, it's <see cref="SoldAfter"/>, which the host's campaign takes into the wallet
/// (<see cref="TakeSoldAfter"/>). A find the tally paid for (banked aboard, or on a locker's shelf, as the train came home) is
/// never bought, so nothing is paid twice.
/// </summary>
public sealed partial class Run
{
    readonly List<Sale> _sales = [];
    readonly HashSet<int> _tallied = [];
    double _soldAfterUnclaimed;

    /// <summary>Scrip the buyer's paid tonight before the tally (in it, and in the autosave's takings).</summary>
    public double Sold { get; private set; }
    /// <summary>Scrip the buyer's paid tonight since the tally was read (host only): the campaign's to take into the wallet.</summary>
    public double SoldAfter { get; private set; }
    /// <summary>Every sale tonight, in order (host only), for tools and tests.</summary>
    public IReadOnlyList<Sale> Sales => _sales;

    /// <summary>Whether the night's tally paid for the find <paramref name="b"/> (host only): the buyer won't buy it again.</summary>
    public bool Tallied(Physics.Body b) => _tallied.Contains(b.Id);

    /// <summary>
    /// Host: what the buyer's paid since the tally and the campaign hasn't taken yet, taken now (the host app adds it to the
    /// crew's wallet as it comes, note 1027). Zero with nothing new.
    /// </summary>
    public double TakeSoldAfter()
    {
        double sold = _soldAfterUnclaimed;
        _soldAfterUnclaimed = 0;
        return sold;
    }

    /// <summary>
    /// Host: <paramref name="find"/> (its body, in <paramref name="player"/>'s hands) sold to the buyer for <paramref name="paid"/>:
    /// out of the world, and its scrip in the takings. Before the tally a find that had been banked is unbanked first, so
    /// it's paid once, by the buyer.
    /// </summary>
    public void Sell(World world, Physics.Body find, double paid, int player)
    {
        if (FindOf(find) is not { } f)
            return;
        Unbank(find.Id);
        find.Banked = false;
        _carriedBy.Remove(find.Id);
        _lootSettling.Remove(find.Id);
        world.Bodies.Remove(find);
        bool after = Report is not null;
        _sales.Add(new Sale(player, f.Item, f.Value, paid, after, Math.Round(Seconds, 1)));
        if (after)
        {
            SoldAfter += paid;
            _soldAfterUnclaimed += paid;
        }
        else
            Sold += paid;
    }

    /// <summary>At the tally (delivered): the finds it pays for, by body, so the buyer won't buy them after.</summary>
    void MarkTallied(World world, HashSet<int> attached)
    {
        foreach (int id in _banked.Keys)
            _tallied.Add(id);
        foreach (var b in world.Bodies.All.Where(b => b.Kind == Physics.BodyKind.Loot && b.Stowed && attached.Contains(b.Parent)))
            _tallied.Add(b.Id);
    }
}
