namespace DarkTerritory.Sim.Enemies;

/// <summary>What the Hanger is doing (replicated in <see cref="Enemy.Height"/>), for its clips and cues.</summary>
public enum HangerMode : byte { Hidden, Grab, Haul, Hold, Hit, Retreat }

/// <summary>
/// THE HANGER · movement · outside (GDD §21; ARCHITECTURE §8 note 586; the director, 9 Oct 2026; docs/design/creatures/hanger.md).
/// It lives in a house's roof space; its strands hang from the ceiling. Touch a strand and it grabs you and hauls you up (a
/// friend's blow on the line frees you). Rule: don't touch the strands.
/// </summary>
/// <remarks>
/// One to a house, in its roof space: <see cref="Enemy.Local"/> its body (in the world, up under the roof);
/// <see cref="Enemy.Extra2"/> the house (an index into the stop walls' <c>OpenHouses</c>); <see cref="Enemy.Extra"/> the
/// player it has (−1 none); <see cref="Enemy.Height"/> its <see cref="HangerMode"/>. Its strands are
/// <see cref="Strands"/>: alike on every machine from the house and its id, so they're never sent.
/// </remarks>
public sealed class Hanger(int id) : Enemy(id)
{
    public override EnemyKind Kind => EnemyKind.Hanger;
    public override PressureZone Zone => PressureZone.Outside;
    public override Sense Sense => Sense.Movement;
    public override Want Want => Want.Kill;
    /// <summary>It belongs to its house: the director never dismisses it for want of company.</summary>
    public override bool StaysAboard => true;

    public HangerMode Mode => (HangerMode)(int)Height;

    /// <summary>The strands' bottoms hang this high off the floor (a hand at about chest height brushes them), from the ceiling.</summary>
    public const double StrandBottom = 0.9, Ceiling = 2.6;

    /// <summary>
    /// Where its strands hang, in the house's own plan (metres along and across it from its middle): <paramref name="count"/>
    /// of them, spread over the house's floor clear of its walls, from a hash of the house and <paramref name="seed"/> (the
    /// Hanger's id), so every machine has the same.
    /// </summary>
    public static List<(double X, double Y)> Strands(Stops.StopBuilding b, int seed, int count)
    {
        var dice = new Ballast.Pcg32((ulong)(uint)seed, 0x48_414E_4745UL);
        double hx = Math.Max(0.4, b.Length / 2 - 0.6), hy = Math.Max(0.4, b.Width / 2 - 0.6);
        var found = new List<(double, double)>();
        for (int i = 0; i < count; i++)
            found.Add(((dice.NextDouble() * 2 - 1) * hx, (dice.NextDouble() * 2 - 1) * hy));
        return found;
    }

    protected override void Tick(EnemyContext ctx)
    {
    }
}
