using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Run;

/// <summary>A bookmark (GDD App. D.12): when, and whose view it was. The still capture stays on the machine that took it.</summary>
public sealed record Bookmark(double Seconds, int By, int Followed);

/// <summary>
/// Being dead (GDD App. D.10): watching a living crewmate and talking with the other dead. The dead and lobbied affect the
/// run in exactly two ways (D.1): calling out from a Holdout, and one creature vote. Everything they ask for comes here as
/// a request (clients send intent, never state), and is checked against the rules before it's allowed. Host-authoritative.
/// </summary>
public sealed class DeadPhase
{
    readonly Dictionary<int, int> _following = new();

    /// <summary>Whom each dead or lobbied player is watching (D.10 "locked to a living crew member's view").</summary>
    public IReadOnlyDictionary<int, int> Following => _following;
    public List<Bookmark> Bookmarks { get; } = new();
    /// <summary>Requests refused, for the harness (a client asking for what it may not have).</summary>
    public int Refused { get; private set; }

    /// <summary>Client side: whom the host says this player watches (only their own is sent them).</summary>
    public void Mirror(int player, int target) => _following[player] = target;

    /// <summary>The living crewmate a waiting player watches, or −1 if there's nobody alive to.</summary>
    public int FollowedBy(int player) => _following.GetValueOrDefault(player, -1);

    /// <summary>
    /// Host, each tick: every waiting player watches someone alive. The choice holds until it dies (then it's the next
    /// living player after it, D.10 "if the target dies, it switches to the next living player"); a player back in the crew
    /// watches nobody.
    /// </summary>
    public void Step(IReadOnlyCollection<(int Id, PlayerState State)> crew)
    {
        var living = crew.Where(c => c.State.Alive).Select(c => c.Id).Order().ToList();
        foreach (var (id, s) in crew)
        {
            if (s.Alive)
            {
                _following.Remove(id);
                continue;
            }
            int now = _following.GetValueOrDefault(id, -1);
            if (living.Contains(now))
                continue;
            _following[id] = living.Count == 0 ? -1 : living.FirstOrDefault(l => l > now, living[0]);
        }
        foreach (int gone in _following.Keys.Where(k => crew.All(c => c.Id != k)).ToList())
            _following.Remove(gone);
    }

    /// <summary>
    /// A request from a player (host). Returns whether it was allowed. Nothing here touches the loudness meter, the voice
    /// the Soot Children and the Gaunt listen to, or the director, but the vote (D.1).
    /// </summary>
    public bool Handle(World world, int player, in PlayerState state, in Request q, IReadOnlyCollection<(int Id, PlayerState State)> crew)
    {
        bool ok = q.Kind switch
        {
            RequestKind.Follow => Follow(player, state, q.A, crew),
            RequestKind.Defer => !state.Alive && world.Holdouts?.Queue.Defer(player, q.A) == true,
            RequestKind.CallOut => CallOut(world, state, q.A, crew),
            RequestKind.LiveMic => LiveMic(world, player, state, q.A != 0),
            RequestKind.Vote => world.Vote(player, state, (EnemyKind)q.A),
            RequestKind.Bookmark => Mark(world, player, state),
            RequestKind.Commend => world.Commend(player, q.A, q.B, crew),
            _ => false,
        };
        if (!ok)
            Refused++;
        return ok;
    }

    bool Follow(int player, in PlayerState state, int target, IReadOnlyCollection<(int Id, PlayerState State)> crew)
    {
        // Living crew only: no free camera, and nobody watches the dead.
        if (state.Alive || !crew.Any(c => c.Id == target && c.State.Alive))
            return false;
        _following[player] = target;
        return true;
    }

    /// <summary>
    /// D.7 Call Out: any dead or lobbied player, from a Holdout that's Occupied or Breaching, with a living crew member
    /// within its active radius, and its shared cooldown run down. It plays one of its occupant's sounds, from the Holdout.
    /// </summary>
    public static bool CallOut(World world, in PlayerState state, int holdout, IReadOnlyCollection<(int Id, PlayerState State)> crew)
    {
        if (state.Alive || world.Holdouts is not { } holdouts || holdout < 0 || holdout >= holdouts.All.Count)
            return false;
        var h = holdouts.All[holdout];
        var t = holdouts.Tuning;
        if (h.Phase is not (HoldoutPhase.Occupied or HoldoutPhase.Breaching) || h.CallOutCooldown > 0)
            return false;
        if (!crew.Any(c => c.State.Alive && (PlayerMotor.WorldPosition(c.State, world.Train) - h.Centre).Length <= t.CallOut.ActiveRadiusM))
            return false;
        var sounds = t.Survivors.VoiceSets[Math.Clamp(h.VoiceSet, 0, t.Survivors.VoiceSets.Length - 1)].Sounds;
        h.CallOuts++;
        h.CallOutSound = (int)((h.SubSeed >> 7) + (ulong)h.CallOuts) % Math.Max(1, sounds.Length);
        h.CallOutCooldown = t.CallOut.CooldownSeconds;
        return true;
    }

    /// <summary>Whether a Call Out could be made from this Holdout now (the dead phase's UI; the host decides).</summary>
    public static bool CanCallOut(World world, Holdout h, IEnumerable<PlayerState> crew) =>
        world.Holdouts is { } holdouts && h.Phase is HoldoutPhase.Occupied or HoldoutPhase.Breaching && h.CallOutCooldown <= 0
        && crew.Any(c => c.Alive && (PlayerMotor.WorldPosition(c, world.Train) - h.Centre).Length <= holdouts.Tuning.CallOut.ActiveRadiusM);

    /// <summary>D.7 Live Mic: offered only to the player assigned to that Holdout. Off when they're freed or released.</summary>
    static bool LiveMic(World world, int player, in PlayerState state, bool on)
    {
        if (state.Alive || world.Holdouts?.All.FirstOrDefault(h => h.Assigned == player && h.Phase is HoldoutPhase.Occupied or HoldoutPhase.Breaching) is not { } h)
            return false;
        h.LiveMic = on;
        return true;
    }

    /// <summary>The Holdout whose Live Mic is carrying this player's voice, if any.</summary>
    public static Holdout? LiveMicOf(World world, int player) =>
        world.Holdouts?.All.FirstOrDefault(h => h.LiveMic && h.Assigned == player && h.Phase is HoldoutPhase.Occupied or HoldoutPhase.Breaching);

    /// <summary>D.12 bookmark: made while dead, of whoever they're watching.</summary>
    bool Mark(World world, int player, in PlayerState state)
    {
        int followed = FollowedBy(player);
        if (state.Alive || followed < 0)
            return false;
        Bookmarks.Add(new Bookmark(Math.Round(world.Run?.Seconds ?? world.ElapsedSeconds, 1), player, followed));
        return true;
    }
}
