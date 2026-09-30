using System.Numerics;
using Ballast.Render;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>
/// The flat-screen HUD (T23), drawn in the low-res frame's own pixels with the pixel font. Sparse on purpose
/// (GDD §32: the screen is the night, not a dashboard):
/// <list type="bullet">
/// <item>top left: the engine (speed, regulator, the pressure gauge with its working band, fire and coal);</item>
/// <item>top right: the link, ping to host first and big (spec E: "shown prominently", non-optional);</item>
/// <item>centre: what's happening to you (dead, lobbied, cold, the night's result);</item>
/// <item>bottom centre: what your hands can do right here;</item>
/// <item>bottom left: the night (dawn clock, next landmark, a stop).</item>
/// </list>
/// </summary>
public static class Hud
{
    static readonly Vector4 Ink = new(0.88f, 0.84f, 0.74f, 1);
    static readonly Vector4 Dim = new(0.60f, 0.58f, 0.53f, 1);
    static readonly Vector4 Amber = new(1.00f, 0.70f, 0.30f, 1);
    static readonly Vector4 Red = new(0.95f, 0.26f, 0.18f, 1);
    static readonly Vector4 Green = new(0.55f, 0.82f, 0.45f, 1);
    static readonly Vector4 Panel = new(0.02f, 0.02f, 0.03f, 0.55f);
    static readonly Vector4 Track = new(0.25f, 0.24f, 0.22f, 0.9f);

    /// <param name="crosshair">The aiming cross at the middle. Not on a headset's panel (T36): it lags the head, which
    /// does the aiming, so a cross on it would point somewhere else.</param>
    /// <param name="dead">The dead phase's and the run end's choices (GDD App. D.10-D.12), for their panels.</param>
    public static void Build(Overlay o, int width, int height, IPlaySession s, bool crosshair = true, DeadPhaseControls? dead = null)
    {
        o.Clear();
        int line = o.Font.LineHeight;
        var p = s.Player;
        Engine(o, s, line);
        if (s.Link is { } link)
            Link(o, width, link, line);
        Radio(o, width, s, line);
        Alerts(o, width, height, s, line);
        if (s.Report is { } report)
            Report(o, width, height, s, report, dead, line);
        else if (DeadPhaseControls.Waiting(s))
            Waiting(o, width, s, line);
        if (s.Report is null && Prompt(s) is { } written)
        {
            string prompt = Bound(written);
            float w = o.Font.Measure(prompt) + 8;
            o.Rect(MathF.Round((width - w) / 2), height - 44, w, line + 4, Panel);
            o.TextCentred(width / 2f, height - 42, prompt, Ink);
        }
        Night(o, height, s, line);
        if (p.Alive && crosshair && s.Report is null)
        {
            // A small cross, for aiming and for "what am I looking at".
            float cx = width / 2f, cy = height / 2f;
            o.Rect(cx - 2, cy, 5, 1, Ink with { W = 0.55f });
            o.Rect(cx, cy - 2, 1, 5, Ink with { W = 0.55f });
        }
    }

    /// <summary>
    /// The crew roster (T69, held Q): everyone aboard by the figures, where each is, and when each was last heard. A crew
    /// counts heads and makes everyone speak by it (App. A.7): the Passenger is on it under the face it wears, one line too
    /// many, and never heard. <paramref name="heard"/>: seconds since a crewmate's voice last came in, null for never.
    /// </summary>
    public static void Roster(Overlay o, int width, int height, IReadOnlyList<RosterLine> lines, Func<byte, double?>? heard)
    {
        int line = o.Font.LineHeight;
        float w = 260, h = (lines.Count + 2) * line + 8;
        float x = MathF.Round((width - w) / 2), y = MathF.Round(height * 0.2f);
        o.Rect(x, y, w, h, Panel);
        o.Text(x + 6, y + 4, $"{lines.Count} ABOARD", Ink);
        y += 4 + 2 * line;
        foreach (var l in lines)
        {
            o.Text(x + 6, y, l.Name, l.Alive ? Ink : Dim);
            o.Text(x + 70, y, l.Where.ToUpperInvariant(), Dim);
            var (said, colour) = l.You ? ("", Dim)
                : !l.Alive ? ("", Dim)
                : (l.Voiced ? heard?.Invoke(l.Id) : null) is not { } ago ? ("NOT HEARD", Amber)
                : ago < 2 ? ("SPEAKING", Green)
                : ($"HEARD {ago:0}S AGO", Dim);
            o.TextRight(x + w - 6, y, said, colour);
            y += line;
        }
    }

    static string Name(IPlaySession s, int id) => id == s.PlayerId ? "YOU" : $"CREW {id}";

    /// <summary>A creature's name as the crew would say it (CinderHound: CINDER HOUND).</summary>
    public static string Words(string name) =>
        string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + c : c.ToString())).ToUpperInvariant();

    static string Ordinal(int n) => n + (n % 100 is 11 or 12 or 13 ? "TH" : (n % 10) switch { 1 => "ST", 2 => "ND", 3 => "RD", _ => "TH" });

    /// <summary>
    /// The dead phase (GDD App. D.10), on the right under the link: whom you're watching, the respawn queue as the host
    /// keeps it (D.6: only the dead and lobbied see it), your Holdout, and what you can do: Call Out and the Live Mic
    /// (D.7), let the next go first, the creature vote (D.11), a bookmark (D.12). Nothing on it is free-camera or replay.
    /// Narrow, so the centre's DEAD and its cause stay clear.
    /// </summary>
    static void Waiting(Overlay o, int width, IPlaySession s, int line)
    {
        var world = s.World;
        var rows = new List<(string Text, Vector4 Colour)>();
        int following = s.Following;
        rows.Add((following >= 0 ? $"WATCHING {Name(s, following)}  [LMB/RMB]" : "NOBODY LEFT ALIVE", following >= 0 ? Ink : Red));
        if (world.Holdouts is { } holdouts)
        {
            var queue = holdouts.Queue.Entries;
            int at = holdouts.Queue.PositionOf(s.PlayerId);
            if (at >= 0)
                rows.Add(($"QUEUE {Ordinal(at + 1)} OF {queue.Count}" + (at + 1 < queue.Count ? "  [N] LET NEXT GO" : ""), Ink));
            foreach (var (e, i) in queue.Select((e, i) => (e, i)).Take(5))
            {
                string where = e.Holdout is { } id && holdouts.Of(id) is { } h ? $" {Short(h.Site.Name)}" + (e.Locked ? " (BREACH)" : "") : "";
                rows.Add(($" {i + 1} {Name(s, e.Player)}{(e.Kind == QueueKind.Lobbied ? " (JOINED)" : "")}{where}", e.Player == s.PlayerId ? Amber : Dim));
            }
            if (DeadPhaseControls.Assigned(s) is { } mine)
            {
                rows.Add(($"HOLDOUT: {Short(mine.Site.Name)}, LIT", Green));
                rows.Add(($"[T] LIVE MIC {(mine.LiveMic ? "ON: HEARD AT ITS DOOR" : "OFF")}", mine.LiveMic ? Amber : Dim));
            }
            if (DeadPhaseControls.CallOutFrom(s) is { } from)
                rows.Add(($"[E] CALL OUT: {Short(from.Site.Name)}", Ink));
            else if (holdouts.All.Any(h => h.Phase is HoldoutPhase.Occupied or HoldoutPhase.Breaching))
                rows.Add(("CALL OUT: NOBODY IN EARSHOT", Dim));
        }
        if (DeadPhaseControls.CanVote(s))
        {
            rows.Add(("VOTE, ONCE A RUN:", Ink));
            foreach (var (k, i) in world.VoteOptions.Select((k, i) => (k, i)).Take(9))
                rows.Add(($" [{i + 1}] {Words(k.ToString())}" + (world.Votes.GetValueOrDefault(k) is > 0 and var n ? $"  {n}" : ""), Dim));
        }
        else if (world.VoteLog.Any(v => v.Player == s.PlayerId))
            rows.Add(("YOUR VOTE IS IN", Dim));
        if (following >= 0)
            rows.Add(("[P] BOOKMARK", Dim));
        rows = [.. rows.Select(r => (Bound(r.Text), r.Colour))];
        float w = rows.Max(r => o.Font.Measure(r.Text)) + 10, x = width - w - 2, y = 5 + 6 * line;
        o.Rect(x, y - 3, w, rows.Count * line + 5, Panel);
        foreach (var (text, colour) in rows)
        {
            o.Text(x + 5, y, text, colour);
            y += line;
        }
    }

    /// <summary>A place's name, short enough for a panel row.</summary>
    static string Short(string name) => (name.Length > 18 ? name[..18] : name).ToUpperInvariant();

    static string Died(DeathCause c) => c switch
    {
        DeathCause.Cold => "FROZE",
        DeathCause.Mauled => "MAULED",
        DeathCause.JumpedAtSpeed => "JUMPED",
        DeathCause.Dragged => "DRAGGED OFF",
        DeathCause.Crushed => "CRUSHED",
        DeathCause.Burned => "BURNED",
        DeathCause.Taken => "TAKEN",
        _ => Words(c.ToString()),
    };

    /// <summary>
    /// The run-end screen's incident report (GDD App. D.12), under the night's result: the deaths and where, the rescues,
    /// the bodies and cars on the left; what the dead voted for, the bookmarks and the commendations on the right; and
    /// across the bottom, the keys to give yours (one, never to yourself).
    /// </summary>
    static void Report(Overlay o, int width, int height, IPlaySession s, IncidentReport r, DeadPhaseControls? dead, int line)
    {
        static string Clock(double seconds) => $"{(int)(seconds / 60)}:{(int)seconds % 60:00}";
        List<(string Text, Vector4 Colour)> Column(params (string Title, IReadOnlyList<string> Items)[] sections)
        {
            var rows = new List<(string, Vector4)>();
            foreach (var (title, items) in sections)
            {
                if (items.Count == 0)
                    continue;
                rows.Add((title, Ink));
                foreach (var item in items.Take(2))
                    rows.Add(("  " + item, Dim));
                if (items.Count > 2)
                    rows.Add(($"  AND {items.Count - 2} MORE", Dim));
            }
            return rows;
        }
        var left = Column(
            ("DEATHS", [.. r.Deaths.Select(d => $"{Name(s, d.Player)} {(d.DropOut ? "LEFT" : Died(d.Cause))}, {Short(d.Where)} {Clock(d.Seconds)}")]),
            ("RESCUES", [.. r.Rescues.Select(x => $"{Name(s, x.Player)} FREED BY {Name(s, x.By)}, {Short(x.Site)}")]));
        left.Add(($"BODIES HOME {r.BodiesDelivered}, LOST {r.BodiesLost}", Ink));
        left.Add(($"CARS LOST {r.CarsLost}   FEES {r.Run.CrewLossFees:0} BACK {r.Run.BodyRefunds:0}", Dim));
        var right = Column(
            ("THE DEAD VOTED FOR", [.. r.Votes.Select(v => $"{Name(s, v.Player)}: {Words(v.Kind.ToString())}")]),
            ("BOOKMARKS", [.. r.Bookmarks.Select(b => $"{Name(s, b.By)} ON {Name(s, b.Followed)} {Clock(b.Seconds)}" + (b.By == s.PlayerId && dead?.Stills.Count > 0 ? " (SAVED)" : ""))]),
            ("COMMENDATIONS", [.. r.Commendations.Select(c => $"{Name(s, c.From)} TO {Name(s, c.To)}: {c.Award.ToUpperInvariant()}")]));
        if (right.Count == 0)
            right.Add(("NO VOTES, BOOKMARKS OR COMMENDATIONS", Dim));
        var bottom = new List<(string Text, Vector4 Colour)>();
        var others = DeadPhaseControls.Commendable(s);
        if (dead is not null && others.Count > 0 && s.World.HoldoutTuning is { } t)
        {
            if (DeadPhaseControls.Commended(s))
                bottom.Add(("YOUR COMMENDATION IS GIVEN", Dim));
            else
            {
                int chosen = others[Math.Clamp(dead.CommendChoice, 0, others.Count - 1)];
                bottom.Add((Bound($"COMMEND {Name(s, chosen)}  [LMB/RMB] SOMEONE ELSE"), Amber));
                var awards = t.Commendations.Awards.Take(9).Select((a, i) => $"[{i + 1}] {a.ToUpperInvariant()}").ToList();
                for (int i = 0; i < awards.Count; i += 3)
                    bottom.Add(("  " + string.Join("  ", awards.Skip(i).Take(3)), Amber));
            }
        }
        float y0 = MathF.Round(height * 0.28f) + 3 * line + 6, bottomEdge = height - 30;
        int columnRows = Math.Max(left.Count, right.Count);
        int fits = Math.Max(1, (int)((bottomEdge - y0) / line) - bottom.Count);
        float half = MathF.Floor((width - 12) / 2f);
        o.Rect(4, y0 - 3, width - 8, (Math.Min(columnRows, fits) + bottom.Count) * line + 5, Panel);
        void Draw(List<(string Text, Vector4 Colour)> rows, float x)
        {
            float y = y0;
            foreach (var (text, colour) in rows.Take(fits))
            {
                o.Text(x, y, text, colour);
                y += line;
            }
        }
        Draw(left, 9);
        Draw(right, 9 + half);
        float by = y0 + Math.Min(columnRows, fits) * line;
        foreach (var (text, colour) in bottom)
        {
            o.Text(9, by, text, colour);
            by += line;
        }
    }

    static void Engine(Overlay o, IPlaySession s, int line)
    {
        var train = s.Train;
        var d = train.Dynamics;
        var c = s.Controls;
        o.Rect(2, 2, 150, 4 * line + 6, Panel);
        float x = 6, y = 5;
        var band = SpeedBands.Classify(d.Tuning, d.Speed);
        o.Text(x, y, $"{Math.Abs(d.Speed) * 3.6,3:0} KM/H", Ink);
        o.Text(x + 64, y, band.ToString().ToUpperInvariant(), band >= SpeedBand.Cruise ? Amber : Dim);
        y += line;
        o.Text(x, y, $"REG {c.Throttle * 100,3:0}%  BRAKE {(c.Brake > 0 ? "ON " : "OFF")}  {(c.Reverser > 0 ? "FWD" : "REV")}", Dim);
        y += line;
        var b = train.Boiler;
        if (train.BoilerTuning is not { } bt)
            return;
        if (b.Ruptured)
        {
            o.Text(x, y, "BOILER RUPTURED", Red);
            return;
        }
        // The gauge: the working band marked, the fill coloured by where the needle is.
        float gx = x + 12, gw = 100;
        o.Text(x, y, "P", Dim);
        o.Rect(gx, y + 1, gw, 5, Track);
        float Mark(double pressure) => gx + (float)(pressure / bt.PressureMax) * gw;
        o.Rect(Mark(bt.WorkingBandMin), y + 1, Mark(bt.WorkingBandMax) - Mark(bt.WorkingBandMin), 5, Green with { W = 0.25f });
        var fill = b.Pressure >= bt.Redline ? Red : b.Pressure >= bt.WorkingBandMin ? Green : Amber;
        o.Rect(gx, y + 2, Mark(b.Pressure) - gx, 3, fill);
        // Ticks at the working band's edges, over the fill, so the band reads whatever the needle does.
        o.Rect(Mark(bt.WorkingBandMin), y, 1, 7, Ink);
        o.Rect(Mark(bt.WorkingBandMax), y, 1, 7, Ink);
        o.Rect(Mark(bt.Redline), y, 1, 7, Red);
        o.Text(gx + gw + 4, y, $"{b.Pressure:0}", b.SafetyValveLifting ? Red : Ink);
        y += line;
        var fire = b.LowFire(bt) ? Amber : Dim;
        o.Text(x, y, $"FIRE {b.Firebox:0.0}", fire);
        o.Text(x + 64, y, $"COAL {b.Tender:0}", b.Tender < 40 ? Amber : Dim);
    }

    static void Link(Overlay o, int width, LinkInfo link, int line)
    {
        float right = width - 6;
        if (link.PingMs is { } ping)
        {
            var colour = ping < 80 ? Green : ping < 150 ? Amber : Red;
            o.TextRight(right, 5, $"PING {ping:0} MS", colour, scale: 2);
        }
        else
        {
            o.TextRight(right, 5, "HOST", Ink, scale: 2);
        }
        o.TextRight(right, 5 + 2 * line, $"{link.Aboard} ABOARD", Dim);
        o.TextRight(right, 5 + 3 * line, link.Role, Dim);
        if (link.Lost)
            o.TextRight(right, 5 + 4 * line, "CONNECTION LOST", Red);
    }

    /// <summary>Whether you've a radio on you (T41), under the link: without one, T does nothing and nobody's on it for you.</summary>
    static void Radio(Overlay o, int width, IPlaySession s, int line)
    {
        var bodies = s.World.Bodies;
        if (!bodies.RadiosCarried || !s.Player.Alive)
            return;
        // Right mouse throws what's in your hands; with them empty, it sets the radio down to pass on.
        string wearing = Bound(bodies.CarriedBy(s.PlayerId) is null ? "RADIO [T]  [RMB] SET IT DOWN" : "RADIO [T]");
        o.TextRight(width - 6, 5 + 5 * line, bodies.HasRadio(s.PlayerId) ? wearing : "NO RADIO", bodies.HasRadio(s.PlayerId) ? Dim : Amber);
    }

    static void Alerts(Overlay o, int width, int height, IPlaySession s, int line)
    {
        var p = s.Player;
        var world = s.World;
        float y = height * 0.28f;
        void Big(string text, Vector4 colour)
        {
            o.TextCentred(width / 2f, y, text, colour, scale: 2);
            y += 2 * line + 2;
        }
        void Small(string text, Vector4 colour)
        {
            o.TextCentred(width / 2f, y, text, colour);
            y += line;
        }
        if (world.Run?.Report is { } r)
        {
            if (r.End == RunEnd.Delivered)
            {
                Big("DELIVERED", Green);
                Small($"{r.CarsDelivered} CARS, {r.CarsLost} LOST. {r.Net:0} SCRIP. CREW HOME {r.CrewHome}", Ink);
            }
            else
            {
                Big("RUN LOST", Red);
                Small(r.End switch { RunEnd.Derailed => "DERAILED", RunEnd.CrewLost => "THE WHOLE CREW IS DEAD", _ => "STILL OUT WHEN THE LINE WENT LIVE" }, Ink);
            }
        }
        if (p.Has(PlayerFlags.Lobbied))
        {
            // GDD App. D.3: joined once the run had left the gate. A Holdout is the only way in.
            Big("LOBBIED", Amber);
            Small("A HOLDOUT IS THE WAY IN", Ink);
        }
        else if (!p.Alive)
        {
            Big("DEAD", Red);
            Small(p.Death switch
            {
                DeathCause.Cold => "FROZE",
                DeathCause.JumpedAtSpeed => "JUMPED AT SPEED",
                DeathCause.Derailed => "DERAILED",
                DeathCause.Mauled => "MAULED",
                DeathCause.Hollow => "THE HOLLOW",
                DeathCause.Choir => "THE CHOIR",
                DeathCause.Taken => "TAKEN. IT WASN'T THEM OUTSIDE",
                DeathCause.Dragged => "DRAGGED OFF THE EDGE",
                DeathCause.Crushed => "CRUSHED UNDER A DROPPED LOAD",
                DeathCause.PulledUnder => "PULLED UNDER BETWEEN THE CARS",
                DeathCause.Lamplighter => "TORN DOWN AT THE LAMP",
                DeathCause.Deadman => "KILLED TAKING BACK THE CAB",
                DeathCause.Gaunt => "NOBODY WAS WATCHING IT",
                DeathCause.Replaced => "IT WASN'T ONE OF YOU. IT IS NOW",
                DeathCause.Nested => "SOMETHING CAME ABOARD ON SOMEONE'S BACK",
                DeathCause.Drift => "THE GROUND CAME UP. YOU KEPT MOVING",
                DeathCause.TornOff => "WENT OFF THE RAILS WITH THE REAR CAR",
                DeathCause.Climbed => "SOMETHING CAME IN OFF THE ROOF",
                DeathCause.Struck => "STRUCK BY THE TUNNEL MOUTH",
                DeathCause.Thrown => "THROWN OFF ON THE CURVE",
                DeathCause.Burned => "BURNED IN A BLAZING CAR",
                DeathCause.Gnawed => "EATEN BY THE GNAWERS",
                DeathCause.Ferryman => "SLOWED FOR THE LANTERN",
                DeathCause.Stoker => "BURNED DRIVING IT OUT OF THE FIREBOX",
                _ => "",
            }, Ink);
            // GDD App. D.1: once the run has left the gate, a Holdout is the only way back (the panel on the right has
            // the queue; this is the rule, short enough to leave it room).
            if (world.Holdouts is not null && world.Run is { Phase: not Sim.Run.RunPhase.Yard })
                Small("A HOLDOUT IS THE WAY BACK", Dim);
        }
        if (p.Alive && PlayerMotor.Chilled(p, s.PlayerTuning))
            Small($"COLD: {Math.Max(0, s.PlayerTuning.Cold.DeathSeconds - p.Cold):0}S. GET INSIDE", p.Cold > s.PlayerTuning.Cold.DeathSeconds - 30 ? Red : Amber);
        if (world.Derailed)
            Big("DERAILED", Red);
    }

    /// <summary>The player's keys (T80), for the prompts: the app sets them from the settings.</summary>
    public static Settings Keys { get; set; } = new();

    /// <summary>
    /// A prompt written with the default keys ([E], [RMB], [T]; the dead phase's [LMB/RMB], [N], [P]) as the player has them
    /// bound.
    /// </summary>
    public static string Bound(string prompt) => prompt
        .Replace("[LMB/RMB]", $"[{Controls.KeyLabel(Keys.KeyFor(Control.Fire))}/{Controls.KeyLabel(Keys.KeyFor(Control.Throw))}]", StringComparison.Ordinal)
        .Replace("[E]", $"[{Controls.KeyLabel(Keys.KeyFor(Control.Use))}]", StringComparison.Ordinal)
        .Replace("[RMB]", $"[{Controls.KeyLabel(Keys.KeyFor(Control.Throw))}]", StringComparison.Ordinal)
        .Replace("[T]", $"[{Controls.KeyLabel(Keys.KeyFor(Control.Radio))}]", StringComparison.Ordinal)
        .Replace("[N]", $"[{Controls.KeyLabel(Keys.KeyFor(Control.LetNextGo))}]", StringComparison.Ordinal)
        .Replace("[P]", $"[{Controls.KeyLabel(Keys.KeyFor(Control.Bookmark))}]", StringComparison.Ordinal);

    /// <summary>What your hands can do right here, with the key that does it.</summary>
    public static string? Prompt(IPlaySession s)
    {
        var p = s.Player;
        var train = s.Train;
        var world = s.World;
        if (!p.Alive)
            return null;
        // The Draggers (T46): grabbed at the edge, or near someone who is.
        if (world.Enemies is { } et)
            foreach (var e in world.ActiveEnemies)
                if (e is Sim.Enemies.Dragger { Phase: Sim.Enemies.SpinePhase.Punish } d && d.Target is { } held)
                {
                    if (held == s.PlayerId)
                        return "GRABBED AT THE EDGE! SOMEONE PULL YOU FREE";
                    if ((d.WorldPosition(train) - PlayerMotor.WorldPosition(p, train)).Length <= et.Draggers.FreeReach + 1)
                        return "[E] HOLD: PULL THEM FREE";
                }
        // GDD App. D.7: at a Holdout's door, lamp lit, with the tool for it in your hands (the tool's own put-down waits).
        if (world.Holdouts is { } holdouts && holdouts.DoorInReach(p, train, world.Hand) is { } door)
        {
            static string Verb(BreachMethod m) => m switch
            {
                BreachMethod.Smash => "SMASH THE LOCK",
                BreachMethod.Pry => "PRY THE BARRICADE",
                _ => "OPEN THE LOCK",
            };
            if (door.Phase == HoldoutPhase.Breaching)
                return door.Breacher == s.PlayerId ? $"{Verb(door.Method)} {100 * door.Progress / Math.Max(0.01, door.Needed):0}%  KEEP HOLDING"
                    : $"SOMEONE'S BREAKING IT OPEN: {100 * door.Progress / Math.Max(0.01, door.Needed):0}%";
            if (door.Phase == HoldoutPhase.Occupied)
            {
                if (holdouts.MethodFor(door, world.Bodies.CarriedBy(s.PlayerId)?.Kind) is { } method)
                    return $"[E] HOLD: {Verb(method)}";
                var ways = holdouts.Tuning.Methods[door.Type].Select(m => holdouts.Tuning.Step(m).Tool == BreachTool.RepairKit ? "THE REPAIR KIT" : "A SHOVEL, WRENCH OR CROWBAR").Distinct();
                return $"SOMEONE'S INSIDE. IT NEEDS {string.Join(" OR ", ways)}";
            }
        }
        if (world.Bodies.CarriedBy(s.PlayerId) is { } carried)
            return carried.Kind switch
            {
                // Spec D.2 "heavy items need two" (T43).
                BodyKind.Heavy when !carried.Lifted => "HOLDING AN END: IT NEEDS TWO   [E] LET GO",
                BodyKind.Heavy => "TOGETHER, INTO A CAR: [E] PUT IT DOWN",
                BodyKind.Cargo => "INTO A CAR TO LOAD IT: [E] PUT DOWN   [RMB] THROW",
                _ => "[E] PUT DOWN   [RMB] THROW",
            };
        if (world.Combat is { } combat && Guns.MannedGun(p, train, combat.Guns) is not null)
            return train.BoilerTuning is not null && train.Boiler.Pressure < combat.Guns.MinPressure ? "NO STEAM FOR THE TURRET" : "[LMB] FIRE";
        // A headset player's prompts follow their reaching hand (T29), as the sim's reach does.
        var hand = world.Hand;
        var near = CrewActions.Nearest(p, train, hand);
        if (p.Surface == Surface.Coupler && near != InteractableKind.Door)
            return "[E] HOLD: CUT THE COUPLING";
        switch (near)
        {
            case InteractableKind.Firebox when PlayerMotor.InCab(p, train):
                return p.Hand != default && !p.Has(PlayerFlags.Shovelful) ? "SHOVEL COAL: FILL IT AT THE TENDER FIRST" : "[E] HOLD: SHOVEL COAL";
            // Only a reaching hand finds the coal face (T29).
            case InteractableKind.Coal when PlayerMotor.InCab(p, train):
                return p.Has(PlayerFlags.Shovelful) ? "SHOVEL FULL: INTO THE FIREBOX" : "GRIP: COAL ON THE SHOVEL";
            case InteractableKind.Vent when PlayerMotor.InCab(p, train):
                return "[E] HOLD: VENT";
            case InteractableKind.Handbrake when p.Surface == Surface.Roof:
                return "[E] HOLD: HANDBRAKE";
            // Out on the running board (App. A.2): what the sand does is only worth it on greased rail.
            case InteractableKind.Sandbox when p.Parent == 0 && p.Surface == Surface.Deck:
                return train.Traction < 1 || train.Sand > 0 ? $"[E] HOLD: SAND THE RAIL ({train.Traction * 100:0}% GRIP)" : "[E] HOLD: SAND";
            case InteractableKind.Door:
                return "[E] DOOR";
        }
        bool wearing = world.Bodies.RadiosCarried && world.Bodies.HasRadio(s.PlayerId);
        if (world.Bodies.InReach(p, train, hand, wearing, s.PlayerId) is { } thing)
            return thing.Kind switch
            {
                BodyKind.Ragdoll => "[E] PICK UP THE BODY",
                BodyKind.Radio => "[E] TAKE THE RADIO",
                // A reaching hand takes its end with both hands on it (T43).
                BodyKind.Heavy when thing.Carrier >= 0 => p.Hand != default ? "BOTH HANDS ON IT: TAKE THE OTHER END" : "[E] TAKE THE OTHER END",
                BodyKind.Heavy => p.Hand != default ? "HEAVY: BOTH HANDS ON AN END (IT NEEDS TWO)" : "[E] TAKE AN END (IT NEEDS TWO)",
                _ => "[E] PICK UP",
            };
        if (world.Run?.LeverInReach(p, train, hand) == true)
            return "[E] HOLD: CHUTE LEVER";
        if (world.Switches?.InReach(p, train, hand) is { } branch)
        {
            // Say which way it'll go, and when it won't: the points don't move with a wheel on them.
            string to = train.Diverging(branch) ? "THE MAIN LINE" : $"THE {(train.Line.Branches[branch].Kind == BranchKind.Spur ? "SPUR" : "DEAD LINE")}";
            return train.PointsOccupied(branch, world.Switches.Tuning.PointsLength)
                ? "SWITCH: POINTS HELD, A WHEEL IS ON THEM"
                : $"[E] HOLD: THROW THE SWITCH TO {to}";
        }
        // The crane (T48): at its controls, or at its hook on the ground.
        if (world.Run?.CurrentSite?.Crane is { } crane)
        {
            if (p.Has(PlayerFlags.Operating))
                return crane.Hooked is null ? "CRANE: WASD BRIDGE AND TROLLEY   SPACE/B HOOK   LET GO OF E TO STEP DOWN"
                    : "CRANE: WASD BRIDGE AND TROLLEY   SPACE/B HOOK   [LMB] LET GO (SET IT DOWN FIRST)";
            if (p.Parent == PlayerState.World && ((PlayerMotor.WorldPosition(p, train) - crane.Controls) with { Y = 0 }).Length <= crane.Tuning.ControlsReach)
                return "[E] HOLD: THE CRANE'S CONTROLS (UP IN THE CAB)";
            if (p.Parent == PlayerState.World && crane.Riggable(PlayerMotor.WorldPosition(p, train)) is not null)
                return crane.Rigging > 0 ? $"RIGGING THE CASTING {crane.Rigging * 100:0}%" : "[E] HOLD: RIG THE CASTING TO THE HOOK";
        }
        if (world.Run?.HandleInReach(p, train, hand) is not null && world.Run.CurrentSite is { } site)
            // A headset turns the crank round with the hand (T43); out of rhythm, the drum stalls (spec D.2).
            return site.OutOfRhythm ? "OUT OF RHYTHM: MATCH THE OTHER CRANK"
                : p.Hand != default ? site.Turning ? "CRANK: OVER THE TOP, TOWARDS THE TRACK. KEEP TOGETHER" : "CRANK: OVER THE TOP, TOWARDS THE TRACK (IT NEEDS TWO)"
                : site.Turning ? "[E] HOLD: CRANK. KEEP TOGETHER" : "[E] HOLD: CRANK (IT NEEDS TWO)";
        if (CabControls.CanDrive(p, train))
            // The lamp switch too (T52): out, smashed (the glass is out a while), or lit.
            return world.LampOutSeconds > 0 ? $"[R/F] REGULATOR   [B] BRAKE   [X] REVERSER   LAMP SMASHED ({world.LampOutSeconds:0}s)"
                : $"[R/F] REGULATOR   [B] BRAKE   [X] REVERSER   [L] LAMP {(world.LampLit ? "OFF" : "ON")}";
        return null;
    }

    static void Night(Overlay o, int height, IPlaySession s, int line)
    {
        string status = PrototypeSession.RouteStatus(s.Route, s.World, s.Train);
        var parts = status.Split(" | ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            // The night's result has its own place in the middle.
            .Where(t => !t.StartsWith("DELIVERED", StringComparison.Ordinal) && !t.StartsWith("RUN LOST", StringComparison.Ordinal))
            .ToList();
        if (parts.Count == 0)
            return;
        float y = height - 4 - parts.Count * line;
        o.Rect(2, y - 3, parts.Max(t => o.Font.Measure(t)) + 8, parts.Count * line + 4, Panel);
        foreach (var t in parts)
        {
            o.Text(6, y, t, t.Contains("DAWN", StringComparison.Ordinal) || t.StartsWith("STOPPED", StringComparison.Ordinal) ? Amber : Dim);
            y += line;
        }
    }
}
