namespace DarkTerritory.Sim.Enemies;

/// <summary>What the Lodger is doing (replicated in <see cref="Enemy.Height"/>), for its clips and cues.</summary>
public enum LodgerMode : byte { Hidden, Shriek, Lunge, Chase, Break, Return }

/// <summary>
/// THE LODGER · sound · outside (GDD §21; ARCHITECTURE §8 note 583; the director, 9 Oct 2026; docs/design/creatures/lodger.md).
/// It hides in the dark of a village house (a back room, under the stairs), heard breathing from the next room. It shrieks
/// (a second's shrill scream: the telegraph) and lunges, a one-hit kill; it chases a little further out of its house than
/// most (about 25 m), shrieking before every lunge, then goes back to its room. A shut door stops a lunge: it breaks
/// through. Rule: when it shrieks, put a door between you.
/// </summary>
/// <remarks>
/// Loose in the world (<see cref="Enemy.Loose"/>, <see cref="Enemy.Local"/> its world position, on the floor).
/// <see cref="Enemy.Lateral"/> its heading (a yaw), <see cref="Enemy.Height"/> its <see cref="LodgerMode"/>,
/// <see cref="Enemy.Extra"/> the player it's after (−1 none), <see cref="Enemy.Extra2"/> its house (an index into the stop
/// walls' <c>OpenHouses</c>).
/// </remarks>
public sealed class Lodger(int id) : Enemy(id)
{
    public override EnemyKind Kind => EnemyKind.Lodger;
    public override PressureZone Zone => PressureZone.Outside;
    public override Sense Sense => Sense.Sound;
    public override Want Want => Want.Kill;
    /// <summary>It belongs to its house: the director never dismisses it for want of company.</summary>
    public override bool StaysAboard => true;

    public LodgerMode Mode => (LodgerMode)(int)Height;

    protected override void Tick(EnemyContext ctx)
    {
    }
}
