namespace DarkTerritory.Sim.Run;

/// <summary>
/// Who a player is (GDD v1.4 App. D.8, "the survivor becomes that player's character from then on, carried into future
/// runs until they die and are freed again. It's stored in the host's campaign save against the player's ID"; note 181).
/// </summary>
public static class Identity
{
    public const string Prisoner = "prisoner", Wildlander = "wildlander";

    /// <summary>
    /// The look <paramref name="player"/> has now: the survivor of the last Holdout they were freed from tonight (a shelter's
    /// wildlander; a prison car's or a lockup's prisoner), else the one they came into the night with
    /// (<see cref="World.Looks"/>), else none (the railwayman they first signed on as).
    /// </summary>
    public static string Of(World world, int player)
    {
        if (world.Holdouts?.All.LastOrDefault(h => h.State == HoldoutState.Freed && h.Occupant == player) is { } freed)
            return freed.Layout.Kind == Stops.HoldoutKind.Shelter ? Wildlander : Prisoner;
        return world.Looks.GetValueOrDefault(player, "");
    }

    /// <summary>
    /// At the night's end, everyone's look by name, for the campaign to keep: the player's ID across nights is their name
    /// (the session's player ids are only tonight's).
    /// </summary>
    public static Dictionary<string, string> ByName(World world, IEnumerable<int> crew) =>
        crew.Select(id => (Name: world.Names.GetValueOrDefault(id, ""), Look: Of(world, id)))
            .Where(x => x.Name.Length > 0 && x.Look.Length > 0)
            .GroupBy(x => x.Name).ToDictionary(g => g.Key, g => g.Last().Look);
}
