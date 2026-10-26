namespace DarkTerritory.Sim.Bots;

public sealed partial class CrewCalls
{
    /// <summary>
    /// The walker gone forward to fight the Stoker at the firebox (note 1051, <see cref="Heed.AtTheFirebox"/>) while the driver
    /// keeps driving. Claimed by the first to hear of it, and held while it lives and the Stoker's about. Instant, like the claims.
    /// </summary>
    public int? StokerHand { get; set; }
    /// <summary>The tick the Stoker's hand last said so: a claim not said again for a second is nobody's (as <see cref="ReliefSaid"/>).</summary>
    public uint StokerHandSaid { get; set; }
    /// <summary>A bot heard as a walker (not the driver, not a gunner); one never heard is taken for a walker.</summary>
    public bool Walker(int bot) => _roles.GetValueOrDefault(bot, BotRole.Walker) == BotRole.Walker;
}
