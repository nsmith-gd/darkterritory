using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Game;

/// <summary>Mirror of content/world/figures.json (note 570): the words of the figures met out in the Territory.</summary>
public sealed record FigureWords
{
    public const string File = "world/figures.json";

    public FigureReach Reach { get; init; } = new();
    public double TypePerSecond { get; init; } = 40;
    public double LingerSeconds { get; init; } = 6;
    public FigureLines Dave { get; init; } = new();
    public FigureLines Jacob { get; init; } = new();

    /// <summary>The words of <paramref name="figure"/>: Dave's or Jacob's.</summary>
    public FigureLines Of(Enemy figure) => figure is Jacob ? Jacob : Dave;

    public static FigureWords Load(string content)
    {
        string path = Path.Combine(content, File);
        return System.IO.File.Exists(path) ? DataFile.Load<FigureWords>(path) : new FigureWords();
    }
}

public sealed record FigureReach
{
    public double Talk { get; init; } = 3.2;
    public double LookDegrees { get; init; } = 22;
    public double CloseBeyond { get; init; } = 6;
    public double HearBeyond { get; init; } = 30;
}

/// <summary>One figure's words: what they're called, what they say a word at a time, their answers to blows, their last.</summary>
public sealed record FigureLines
{
    public string Name { get; init; } = "Dave";
    public string Title { get; init; } = "";
    public string[] Lines { get; init; } = [];
    public string[] Blows { get; init; } = [];
    public string Last { get; init; } = "";
    /// <summary>Jacob's, as the train's made new (note 572).</summary>
    public string Blessing { get; init; } = "";
}

/// <summary>
/// Talking with Dave (GDD §3.2; note 570), on this machine alone, as a town's talk is (<see cref="TownTalk"/>): a Use press at
/// his prompt opens his card and Use again hears his next line; walking off closes it. Nothing here changes the night, so
/// nothing is sent. What he says to a blow is heard here too: the host keeps his count of blows, and every client has the
/// last striker's (his replicated <see cref="Dave.StrikerBlows"/>), so whoever's near hears him answer it, and hears his
/// last words as he takes them.
/// </summary>
public sealed class FigureTalk
{
    /// <summary>The figures' words as loaded (the app's and the CLI's); their defaults until then.</summary>
    public static FigureWords Words { get; set; } = new();

    int _heard;
    // The blows last seen on him (striker, count) and whether he held somebody, so a change is answered once.
    (int Striker, int Blows) _seen = (-1, 0);
    bool _wasHolding, _talked;
    string _said = "";

    /// <summary>His enemy id while his card is open; null otherwise.</summary>
    public int? Open { get; private set; }
    public double Since { get; private set; }

    /// <summary>Dave or Jacob, if one's in front of <paramref name="s"/>'s player within reach to talk to: the least angle off the view's centre.</summary>
    public static Enemy? Target(IPlaySession s)
    {
        var p = s.Player;
        if (!p.Alive || p.Parent != PlayerState.World)
            return null;
        var r = Words.Reach;
        var eye = p.Position + Double3.Up * s.Train.Dynamics.Tuning.Pick.EyeHeight;
        double cp = DMath.Cos(p.Pitch);
        var view = new Double3(-DMath.Sin(p.Yaw) * cp, DMath.Sin(p.Pitch), -DMath.Cos(p.Yaw) * cp);
        double best = DMath.Cos(r.LookDegrees * Math.PI / 180);
        Enemy? found = null;
        foreach (var e in s.World.ActiveEnemies)
            if (e is Dave { Gone: false, Holding: < 0 } or Jacob { Gone: false })
            {
                var to = e.Local + Double3.Up * 1.45 - eye;
                double d = to.Length;
                if (d > r.Talk || d < 1e-6)
                    continue;
                double cos = Double3.Dot(view, to * (1 / d));
                if (cos >= best)
                    (best, found) = (cos, e);
            }
        return found;
    }

    /// <summary>The prompt for him in front of you: his name, and the action and its key (GDD §32's <c>ACTION : [KEY]</c>).</summary>
    public static string Prompt(Enemy figure) => $"{Words.Of(figure).Name.ToUpperInvariant()}   TALK : [E]";

    /// <summary>A Use press with Dave the prompt (null when it's something else's): opens his card or hears his next line.</summary>
    public bool Use(Enemy? target, double now)
    {
        var words = target is null ? null : Words.Of(target);
        if (target is null || words!.Lines.Length == 0)
            return false;
        Open = target.Id;
        _figure = target;
        Since = now;
        _talked = true;
        // Jacob, once he's mended the train, says that; until then, his next line (the press goes on to the host: a word
        // with him is what mends it).
        _said = target is Jacob { Blessed: true } && words.Blessing.Length > 0 ? words.Blessing : words.Lines[_heard++ % words.Lines.Length];
        return true;
    }

    Enemy? _figure;
    bool _wasBlessed;

    /// <summary>
    /// Closes the card once you've walked off or it's been said and left long enough; and opens it on what he says to a blow
    /// or as he takes somebody, when you're near enough to hear.
    /// </summary>
    public void Step(Sim.World world, Double3 eye, double now)
    {
        // Jacob's blessing, heard by whoever's near him as it comes (note 572).
        if (world.ActiveEnemies.OfType<Jacob>().FirstOrDefault(j => !j.Gone) is { } jacob)
        {
            if (jacob.Blessed && !_wasBlessed && Words.Jacob.Blessing.Length > 0 && (jacob.Local - eye).Length <= Words.Reach.HearBeyond)
                (Open, _figure, Since, _talked, _said) = (jacob.Id, jacob, now, false, Words.Jacob.Blessing);
            _wasBlessed = jacob.Blessed;
            if (Open == jacob.Id)
            {
                double gone = (jacob.Local + Double3.Up * 1.45 - eye).Length;
                if (gone > (_talked ? Words.Reach.CloseBeyond : Words.Reach.HearBeyond) || now - Since > _said.Length / Math.Max(1, Words.TypePerSecond) + Words.LingerSeconds)
                    Open = null;
                return;
            }
        }
        var dave = world.ActiveEnemies.OfType<Dave>().FirstOrDefault(d => !d.Gone);
        if (dave is null)
        {
            if (_figure is not Jacob)
                Open = null;
            return;
        }
        var words = Words.Dave;
        double far = (dave.Local + Double3.Up * 1.45 - eye).Length;
        bool holding = dave.Holding >= 0;
        var blows = (dave.Striker, dave.StrikerBlows);
        if (holding && !_wasHolding && words.Last.Length > 0)
            Say(words.Last);
        else if (blows != _seen && blows.StrikerBlows > 0 && blows.StrikerBlows <= words.Blows.Length)
            Say(words.Blows[blows.StrikerBlows - 1]);
        _seen = blows;
        _wasHolding = holding;
        if (Open is null)
            return;
        bool said = now - Since > _said.Length / Math.Max(1, Words.TypePerSecond) + Words.LingerSeconds;
        // A word with him closes as you walk off; what you overheard, once you're out of earshot.
        if (far > (_talked ? Words.Reach.CloseBeyond : Words.Reach.HearBeyond) || said)
            Open = null;

        void Say(string line)
        {
            if (far > Words.Reach.HearBeyond)
                return;
            Open = dave.Id;
            _figure = dave;
            Since = now;
            _talked = false;
            _said = line;
        }
    }

    /// <summary>His card now, or null: what he's saying, typed out so far.</summary>
    public TownCard? Card(double now)
    {
        if (Open is null)
            return null;
        var words = _figure is { } f ? Words.Of(f) : Words.Dave;
        int typed = (int)Math.Clamp((now - Since) * Words.TypePerSecond, 0, _said.Length);
        string heading = words.Title.Length > 0 ? $"{words.Name}, {words.Title}" : words.Name;
        return new TownCard(TownCardKind.Speech, heading, _said[..typed], typed == _said.Length && words.Lines.Contains(_said) ? "AGAIN : [E]" : "");
    }
}
