using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// What a corrupted human said on the radio (note 1040): the Brakeman who said it (the enemy id the frames come from), the
/// crewmate whose voice it was, the tick, and the frames, a few words of that crewmate's own radio talk.
/// </summary>
public sealed record RadioVoicePlay(int Brakeman, int Voice, uint Tick, IReadOnlyList<byte[]> Frames)
{
    /// <summary>Its length in seconds, at the voice's 50 frames a second.</summary>
    public double Seconds => Frames.Count / (double)RadioVoice.FramesPerSecond;
}

/// <summary>
/// A corrupted human on the crew's radio (GDD Part Eleven q4: "A voice on your own channel that isn't crew is the single most
/// disturbing thing available in this design. Possibly too much."; queue #729, ARCHITECTURE §8 note 1040). Off until the
/// director answers note 1026's Q1 (enemies.json <c>brakeman.radio.on</c>).
/// <para>
/// The Brakeman, because his rules already make the moment: he's a railwayman still at a railwayman's job, on the train
/// itself, never with a crew of one (so there's always a friend's voice to take and someone else to hear it), and he spends
/// half his night out of sight under the cars (<see cref="BrakemanMode.Hidden"/>). The Passenger's one tell is that it never
/// speaks, so it can't be the Passenger. Hidden a while, with nobody near him, he says on the radio channel only a few words
/// a crewmate said on the radio earlier, to everyone with a set but that crewmate. Once a night at most, and the night's
/// report names it.
/// </para>
/// <para>
/// Host only: the voices it takes are the host's (<see cref="World.RadioVoices"/>). Nothing it does touches a player's or the
/// train's state, so a predicting client never needs it; the choice is the host's sim's alone, from the night's seed, the
/// tick, and what was said, so the same night said the same way says the same thing back.
/// </para>
/// </summary>
public static class RadioVoice
{
    /// <summary>Opus frames a second (20 ms each, spec A.5).</summary>
    public const int FramesPerSecond = 50;
    /// <summary>What's kept of each crewmate's radio talk: a minute of it.</summary>
    public const int FramesKept = 60 * FramesPerSecond;

    /// <summary>
    /// This tick's check, after the world's step: if it's on, not yet said tonight, and a Brakeman's been hidden long enough
    /// with nobody near, he says it, and the report gets its line. Returns what he said (the session plays it), or null.
    /// </summary>
    /// <param name="crew">The session's crew as they stand (id, state).</param>
    /// <param name="hasRadio">Whether a crewmate has a set to hear it on (T41).</param>
    public static RadioVoicePlay? Step(World world, IReadOnlyList<(int Id, PlayerState State)> crew, Func<int, bool> hasRadio)
    {
        if (world.Enemies is not { } tuning || !tuning.Brakeman.Radio.On || world.RadioVoiceSaid is not null)
            return null;
        var play = Choose(world, tuning.Brakeman.Radio, crew, hasRadio);
        if (play is null)
            return null;
        world.RadioVoiceSaid = play;
        var him = world.ActiveEnemies.First(e => e.Id == play.Brakeman);
        world.Attribution.Add(IncidentLog.Event(world, IncidentKind.Heard, "The Brakeman on the radio", play.Voice,
            "In {actor}'s voice. Not said by them.", him.WorldPosition(world.Train)));
        return play;
    }

    /// <summary>The choice alone (no report, no once-a-night): who, which words, or null if the moment isn't here.</summary>
    public static RadioVoicePlay? Choose(World world, RadioVoiceTuning t, IReadOnlyList<(int Id, PlayerState State)> crew, Func<int, bool> hasRadio)
    {
        var living = crew.Where(c => c.State.Alive).OrderBy(c => c.Id).ToList();
        if (living.Count < 2)
            return null;
        // Out of sight a while, and never near: hidden under the train, nobody living within farFrom of him.
        var brakeman = world.ActiveEnemies.OfType<Brakeman>()
            .Where(b => !b.Gone && b.Mode == BrakemanMode.Hidden && b.ModeSeconds >= t.HiddenFor)
            .OrderBy(b => b.Id)
            .FirstOrDefault(b => living.All(c => (PlayerMotor.WorldPosition(c.State, world.Train) - b.WorldPosition(world.Train)).Length > t.FarFrom));
        if (brakeman is null)
            return null;
        uint now = world.Tick;
        uint minAge = (uint)Math.Round(t.MinAge * SimConstants.TickRate), within = (uint)Math.Round(t.Within * SimConstants.TickRate);
        int least = (int)Math.Ceiling(t.MinSeconds * FramesPerSecond), most = Math.Max(1, (int)Math.Floor(t.MaxSeconds * FramesPerSecond));
        // Whose: a living crewmate with something said on the radio, old enough not to be an echo and not too old, and someone
        // else with a set to hear it in their voice.
        var choices = new List<(int Voice, Net.Utterance Said)>();
        foreach (var (id, _) in living)
        {
            if (!living.Any(o => o.Id != id && hasRadio(o.Id)))
                continue;
            foreach (var u in world.RadioVoices.Utterances(id))
                if (u.Frames.Count >= least && now - u.End >= minAge && now - u.End <= within)
                    choices.Add((id, u));
        }
        if (choices.Count == 0)
            return null;
        // Seeded (the night's line, the Brakeman, the tick): the same night, said the same way, says the same thing back.
        var dice = new Pcg32((world.Route?.Seed ?? 0) ^ (ulong)brakeman.Id * 0x9E3779B97F4A7C15UL ^ now, 0x5241_4449_4FUL);
        var (voice, said) = dice.Pick(choices);
        // A few words of it: the start of what they said, cut to maxSeconds.
        return new RadioVoicePlay(brakeman.Id, voice, now, [.. said.Frames.Take(most)]);
    }
}
