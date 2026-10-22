using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// A ping heard (queue #507, note 777; A1's #1024, note 739): each of the crew's pings, the order passed down the train
/// that everyone sees on the HUD and the world's mark, heard by everyone once as it's made, through the radio every uniform
/// has (note 749): flat, as the set in your hand. Off <see cref="World.Pings"/>, which every machine has (their own record
/// kind). An ear that joins mid-night doesn't hear the pings already standing.
/// And the bot who takes one answering (queue #687, note 987; Finn's note 936: "a bot's 'on it' voice is the audio chat's"):
/// its answer said over the same radio as the ping's taker comes on it, in that bot's own voice all night (the answer's
/// cue's takes are the bots' voices in order, and a bot's is its id's place among them). Only bots take pings (BotCrew sends
/// the answer), and the taker is on the ping's record, so every machine hears it.
/// </summary>
public sealed partial class GameAudio
{
    /// <summary>The pings already heard (or standing when this ear first looked), by their ids, unique for the night.</summary>
    readonly HashSet<int> _pingsHeard = [];
    /// <summary>Who each ping's taker was when last looked at (-1 none), by its id: a new taker answers.</summary>
    readonly Dictionary<int, int> _pingTakers = [];
    /// <summary>The bots' voices made from an answer's cue (one take of it), by name, and the cue's definition they're made from.</summary>
    readonly Dictionary<string, Ballast.Audio.SoundDef> _botVoices = [];

    void PingsHeard(World world, bool primed)
    {
        foreach (var p in world.Pings)
        {
            if (_pingsHeard.Add(p.Id) && primed)
                Ui(UiCue.Ping);
            if (p.Taker >= 0 && _pingTakers.GetValueOrDefault(p.Id, -1) != p.Taker && primed && BotAnswer(p, world) is { } cue
                && BotVoice(cue, p.Taker) is { } voice)
                Mixer.Play(voice);
            _pingTakers[p.Id] = p.Taker;
        }
    }

    /// <summary>The answer's cue (PingMarks.Answer's, said): "ON THE SPUR" is <c>voice-bot.spur</c>.</summary>
    static string? BotAnswer(PingEvent p, World world) => p.Kind switch
    {
        PingKind.Fire => "voice-bot.fire",
        PingKind.Cab => "voice-bot.controls",
        PingKind.Loot => "voice-bot.loot",
        PingKind.Crane => "voice-bot.crane",
        PingKind.Town => "voice-bot.town",
        PingKind.Points => PingMarks.Spur(p, world) ? "voice-bot.spur" : "voice-bot.points",
        PingKind.Cargo => "voice-bot.cargo",
        _ => null,
    };

    /// <summary>
    /// The cue said in this bot's voice: its one take of the cue's (the bot's id's place among them), as a sound of its own
    /// named <c>cue.vN</c> (so the caption is the cue's), made once and again if the cue's definition is reloaded.
    /// </summary>
    string? BotVoice(string cue, int bot)
    {
        if (Bank.Get(cue) is not { Layers: [{ Sample: { } folder } layer, ..] } def)
            return null;
        var takes = Bank.Samples.Takes(folder);
        if (takes.Count == 0)
            return null;
        int k = bot % takes.Count;
        string name = $"{cue}.v{k}";
        if (!_botVoices.TryGetValue(name, out var from) || !ReferenceEquals(from, def) || Bank.Get(name) is null)
        {
            Bank.Add(name, def with { Layers = [layer with { Sample = takes[k] }, .. def.Layers[1..]] });
            _botVoices[name] = def;
        }
        return name;
    }
}
