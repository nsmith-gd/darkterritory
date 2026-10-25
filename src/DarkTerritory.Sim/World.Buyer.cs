using Ballast;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim;

/// <summary>
/// The buyer in the square (GDD §3.1's "turning loot into scrip in the town itself"; ARCHITECTURE §8 note 1027; towns.json
/// <c>buyer</c>): a find carried to them is sold with a press of Use, host side (<see cref="Run.Run.Sell"/>). Who's in reach
/// and what's offered are worked out the same on every machine from the town's plan and the find's body, so a client's prompt
/// says what the host will do; only whether the tally already paid for it is the host's alone.
/// </summary>
public sealed partial class World
{
    // Host: who had Use down last tick, for the press that sells (not a hold).
    readonly HashSet<int> _buyerUseWas = [];

    /// <summary>
    /// The town's buyer within <c>buyer.reach</c> of <paramref name="s"/> on foot and alive (note 1027), and their town; null
    /// with none, or the buyer off.
    /// </summary>
    public (Towns.Town Town, Towns.Townsperson Buyer)? BuyerInReach(in PlayerState s)
    {
        if (!s.Alive || TownOf(s) is not { Tuning.Buyer: { Enabled: true } t } town || town.Plan.People.FirstOrDefault(p => p.Role == "buyer") is not { } buyer)
            return null;
        var at = PlayerMotor.WorldPosition(s, Train);
        return ((town.World(buyer.S, buyer.D, buyer.Up) - at) with { Y = 0 }).Length <= t.Reach ? (town, buyer) : null;
    }

    /// <summary>
    /// What the buyer in reach of <paramref name="s"/> would pay for the find in their hands (note 1027): the find's body, what
    /// it is and the price (towns.json <c>buyer.pays</c> of the tally's, less in a town cold to the crew). Null with no buyer in
    /// reach or no find in hand. A client's guess as good as the host's but for the tally: <see cref="Run.Run.Tallied"/>.
    /// </summary>
    public (Physics.Body Find, Stops.LootFind What, double Price)? BuyerOffer(in PlayerState s, int playerId)
    {
        if (Run is not { } run || BuyerInReach(s) is not { } at || Bodies.CarriedBy(playerId) is not { Kind: Physics.BodyKind.Loot } held
            || run.FindOf(held) is not { } find)
            return null;
        var tuning = at.Town.Tuning;
        return (held, find, tuning.Buyer!.Price(find.Value, tuning.Standing.Of(at.Town.Plan.Cold)));
    }

    /// <summary>
    /// Host (note 1027): a crewmate at the buyer with a find in hand pressing Use sells it, unless the tally's paid for it.
    /// True while that's what their Use is for (a find in hand at the buyer), so the press doesn't put it down instead.
    /// </summary>
    bool SellAct(in PlayerState s, in PlayerIntent intent, int playerId)
    {
        bool use = intent.Has(PlayerButtons.Use), pressed = use && !_buyerUseWas.Contains(playerId);
        if (use)
            _buyerUseWas.Add(playerId);
        else
            _buyerUseWas.Remove(playerId);
        if (BuyerOffer(s, playerId) is not { } offer)
            return false;
        if (pressed && !Run!.Tallied(offer.Find))
            Run.Sell(this, offer.Find, offer.Price, playerId);
        return true;
    }
}
