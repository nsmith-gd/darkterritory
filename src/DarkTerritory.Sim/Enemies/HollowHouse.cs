namespace DarkTerritory.Sim.Enemies;

/// <summary>What the HollowHouse is doing (replicated in <see cref="Enemy.Height"/>), for its clips and cues.</summary>
public enum HollowMode : byte { Still, Rumble, Shut, Sink, Collapse, Settled }

/// <summary>
/// THE HOLLOW HOUSE · vibration · outside (GDD §21; ARCHITECTURE §8 note 585; the director, 9 Oct 2026; docs/design/creatures/hollow-house.md).
/// The house is the creature. Someone inside, the ground rumbles under it (the tell); then its doors slam shut one after
/// another, quickly, and it sinks and comes down on whoever's still inside (a kill). Rule: know the house you're in.
/// </summary>
/// <remarks>
/// One to a house. <see cref="Enemy.Local"/> is the house's middle, on its floor; <see cref="Enemy.Extra2"/> the house (an
/// index into the stop walls' <c>OpenHouses</c>); <see cref="Enemy.Extra"/> how far it's sunk (metres, 0 standing);
/// <see cref="Enemy.Height"/> its <see cref="HollowMode"/>. The art lowers the house's drawing by the sink, shakes it while it
/// rumbles, and brings it down at <see cref="HollowMode.Collapse"/>.
/// </remarks>
public sealed class HollowHouse(int id) : Enemy(id)
{
    public override EnemyKind Kind => EnemyKind.HollowHouse;
    public override PressureZone Zone => PressureZone.Outside;
    public override Sense Sense => Sense.Vibration;
    public override Want Want => Want.Kill;
    /// <summary>It belongs to its house: the director never dismisses it for want of company.</summary>
    public override bool StaysAboard => true;

    public HollowMode Mode => (HollowMode)(int)Height;

    protected override void Tick(EnemyContext ctx)
    {
    }
}
