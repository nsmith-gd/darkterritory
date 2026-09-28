namespace DarkTerritory.Sim;

public static class SimConstants
{
    /// <summary>Authoritative simulation and network tick (GDD §33).</summary>
    public const int TickRate = 30;
    public const double TickSeconds = 1.0 / TickRate;
}
