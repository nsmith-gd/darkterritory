namespace DarkTerritory.Sim.Enemies;

/// <summary>What the Householder is doing (replicated in <see cref="Enemy.Height"/>), for its clips and cues.</summary>
public enum HouseholderMode : byte { Sit, Watch, Rise, Hunt, Grab, Return }

/// <summary>
/// THE HOUSEHOLDER · sight · outside (GDD §21; ARCHITECTURE §8 note 584; the director, 9 Oct 2026; docs/design/creatures/householder.md).
/// A gaunt figure at its set table in a lived-in-looking house, harmless to guests. Take one of its house's things out of
/// the door unpaid and it gets up and hunts whoever carries it (a grab a friend can break); leave something of yours on its
/// table and that's paid for. Rule: pay for what you take.
/// </summary>
/// <remarks>
/// Loose in the world (<see cref="Enemy.Local"/> its world position: its chair at the table while it sits).
/// <see cref="Enemy.Lateral"/> its heading (a yaw; sitting, toward its table), <see cref="Enemy.Height"/> its
/// <see cref="HouseholderMode"/>, <see cref="Enemy.Extra"/> the player it's hunting (−1 none), <see cref="Enemy.Extra2"/> its
/// house (an index into the stop walls' <c>OpenHouses</c>). Its table stands <see cref="TableOut"/> m in front of its chair.
/// </remarks>
public sealed class Householder(int id) : Enemy(id)
{
    public override EnemyKind Kind => EnemyKind.Householder;
    public override PressureZone Zone => PressureZone.Outside;
    public override Sense Sense => Sense.Sight;
    public override Want Want => Want.Cargo;
    /// <summary>It belongs to its house: the director never dismisses it for want of company.</summary>
    public override bool StaysAboard => true;

    public HouseholderMode Mode => (HouseholderMode)(int)Height;

    /// <summary>How far in front of its chair its table stands (the art draws the set table there).</summary>
    public const double TableOut = 0.7;

    protected override void Tick(EnemyContext ctx)
    {
    }
}
