namespace DarkTerritory.Game;

/// <summary>
/// The dead's ballot as a screen (GDD v1.4 App. D.11; note 202): pick a creature, then cast it, since a vote is locked on
/// submit and one stray key shouldn't spend it. A number key picks its option, and the same key again (or Enter) casts it;
/// in a headset the left stick steps through them and clicking it casts. Casting sends the option's number as the intent's
/// hotbar choice (the path a vote always took: the dead carry nothing), every tick until the host's ballot says it's cast.
/// Only what's shown and what's sent are here: the host decides.
/// </summary>
public sealed class BallotPicker
{
    /// <summary>The option picked (0-based), or −1 for none yet.</summary>
    public int Pick { get; private set; } = -1;
    /// <summary>Cast and sent: waiting on the host's word that it's locked.</summary>
    public bool Sent { get; private set; }

    /// <summary>Number key <paramref name="number"/> (1-based) on a ballot of <paramref name="count"/>: picks it, or casts it if it's picked.</summary>
    public void Key(int number, int count)
    {
        if (Sent || number < 1 || number > count)
            return;
        if (Pick == number - 1)
            Cast();
        else
            Pick = number - 1;
    }

    /// <summary>A step through the options (the headset's stick), wrapping; from none, down starts at the top and up at the foot.</summary>
    public void Step(int step, int count)
    {
        if (Sent || step == 0 || count <= 0)
            return;
        Pick = Pick < 0 ? (step > 0 ? 0 : count - 1) : ((Pick + step) % count + count) % count;
    }

    /// <summary>A headset's presses (T36's stick, one push at a time): up and down step through the ballot, the stick's click casts.</summary>
    public void Headset(VrMenuPress press, int count)
    {
        Step((press.HasFlag(VrMenuPress.Down) ? 1 : 0) - (press.HasFlag(VrMenuPress.Up) ? 1 : 0), count);
        if (press.HasFlag(VrMenuPress.Click))
            Cast();
    }

    /// <summary>Casts what's picked (nothing if nothing is).</summary>
    public void Cast()
    {
        if (Pick >= 0)
            Sent = true;
    }

    /// <summary>
    /// The hotbar choice to send this tick for <paramref name="ballot"/> (as the host last said it): the picked option's
    /// number while it's cast and not yet locked, else 0. A ballot gone (the run over) or locked forgets the picking.
    /// </summary>
    public byte Select((IReadOnlyList<Sim.Enemies.EnemyKind> Options, Sim.Enemies.EnemyKind? Cast)? ballot)
    {
        if (ballot is not { Cast: null } open || Pick >= open.Options.Count)
        {
            (Pick, Sent) = ballot is { Cast: { } cast } ? (ballot.Value.Options.ToList().IndexOf(cast), false) : (-1, false);
            return 0;
        }
        return Sent ? (byte)(Pick + 1) : (byte)0;
    }
}
