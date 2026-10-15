using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim;

/// <summary>
/// The crew's emotes, the host's side (note 298; GDD §9; note 722, the emotes for two and the edges, the director, 14 Oct
/// 2026, App. F.6). An emote is a record the crew see and nothing else acts on: a high five answered marks both of the pair's
/// records with the tick the hands met; a sit or a lean is held till its crewmate moves. The shared rules are
/// <see cref="EmoteRules"/>'s.
/// </summary>
public sealed partial class World
{
    /// <summary>
    /// <paramref name="playerId"/>'s emote this tick: a held one stood up from (a move, a jump, or no longer at its edge), and
    /// one picked taken, unless something has hold of them, they're over a friend's shoulder, it's an edge emote away from an
    /// edge, or it's on top of the last one picked (emotes.cooldown). A high five or fist bump picked answers an offer of the
    /// same made to them (<see cref="EmoteRules.Answering"/>, from where the crew stood at the tick's start, <see cref="Mates"/>),
    /// or is an offer itself.
    /// </summary>
    void EmoteAct(in PlayerState s, in PlayerIntent intent, int playerId)
    {
        var t = EmoteTuning;
        for (int i = Emotes.Count - 1; i >= 0; i--)
            if (Emotes[i].By == playerId && EmoteRules.Held(Emotes[i].Kind) && EmoteRules.StandsUp(Emotes[i].Kind, s, intent, Train, t))
                Emotes.RemoveAt(i);
        if (intent.Emote == Emote.None || !EmoteRules.CanTake(intent.Emote, s, Train, t))
            return;
        // The bow, the salute and the conga are the "more emotes" upgrade's (note 714): taken only from a train that has it.
        if (intent.Emote is Emote.Bow or Emote.Salute or Emote.Conga && !Train.Dynamics.Tuning.Composition.Dressed("moreEmotes"))
            return;
        foreach (var e in Emotes)
            if (e.By == playerId && (Tick - (double)e.Tick) * SimConstants.TickSeconds < t.Cooldown)
                return;
        Emotes.RemoveAll(e => e.By == playerId);
        int id = _nextFx;
        _nextFx = _nextFx % 0xFFFFFF + 1;
        if (EmoteRules.Answering(Emotes, intent.Emote, playerId, s, Mates, Train, t, Tick) is { } offer)
        {
            // The hands meet: both records say when, and with whom.
            Emotes[Emotes.IndexOf(offer)] = offer with { Meet = Tick, With = playerId };
            Emotes.Add(new EmoteEvent(id, Tick, playerId, intent.Emote, Tick, offer.By));
        }
        else
            Emotes.Add(new EmoteEvent(id, Tick, playerId, intent.Emote));
    }
}
