using System.Numerics;
using Ballast.Render;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>
/// The flat-screen HUD (T23), drawn in the low-res frame's own pixels with the pixel font. Sparse on purpose
/// (GDD §32: the screen is the night, not a dashboard):
/// <list type="bullet">
/// <item>top left: the engine (speed, regulator, the pressure gauge with its working band, fire and coal);</item>
/// <item>top right: the link, ping to host first and big (spec E: "shown prominently", non-optional);</item>
/// <item>centre: what's happening to you (dead, waiting at a Holdout, cold, the night's result);</item>
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
    public static void Build(Overlay o, int width, int height, IPlaySession s, bool crosshair = true)
    {
        o.Clear();
        int line = o.Font.LineHeight;
        var p = s.Player;
        Engine(o, s, line);
        RouteStrip(o, width, s, line);
        if (s.Link is { } link)
            Link(o, width, link, line);
        Radio(o, width, s, line);
        Alerts(o, width, height, s, line);
        if (Prompt(s) is { } written)
        {
            string prompt = Bound(written);
            float w = o.Font.Measure(prompt) + 8;
            o.Rect(MathF.Round((width - w) / 2), height - 44, w, line + 4, Panel);
            o.TextCentred(width / 2f, height - 42, prompt, Ink);
        }
        Night(o, height, s, line);
        if (p.Alive)
            Hotbar(o, width, height, p, line);
        if (p.Alive && crosshair)
        {
            // A small cross, for aiming and for "what am I looking at".
            float cx = width / 2f, cy = height / 2f;
            o.Rect(cx - 2, cy, 5, 1, Ink with { W = 0.55f });
            o.Rect(cx, cy - 2, 1, 5, Ink with { W = 0.55f });
        }
    }

    /// <summary>
    /// The hotbar (T108), bottom right: each slot with a tool in it, by its number key, the one in hand lit; an empty slot
    /// picked shows as hands. The wheel steps through the tools.
    /// </summary>
    static void Hotbar(Overlay o, int width, int height, PlayerState p, int line)
    {
        var slots = Enumerable.Range(0, Kit.Slots).Where(i => Kit.At(p.Kit, i) != Tool.None || i == p.HeldSlot).ToList();
        float x = width - 4, y = height - line - 8;
        for (int k = slots.Count - 1; k >= 0; k--)
        {
            int i = slots[k];
            var tool = Kit.At(p.Kit, i);
            string label = $"{i + 1} {(tool == Tool.None ? "HANDS" : tool.ToString().ToUpperInvariant())}";
            float w = o.Font.Measure(label) + 8;
            x -= w + 2;
            bool held = i == p.HeldSlot;
            o.Rect(x, y, w, line + 4, held ? Amber with { W = 0.35f } : Panel);
            o.Text(x + 4, y + 2, label, held ? Ink : Dim);
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
        // T97: with steam driving there's no regulator; what the pressure will make is the thing to read.
        string drive = s.Train.BoilerTuning is { SteamDrive: true } steam
            ? $"STEAM {s.Train.Boiler.SteamSpeed(steam, s.Train.Dynamics.Tuning.MaxSpeed) * 3.6,3:0} KM/H"
            : $"REG {c.Throttle * 100,3:0}%";
        o.Text(x, y, $"{drive}  BRAKE {(c.Brake > 0 ? "ON " : "OFF")}  {(c.Reverser > 0 ? "FWD" : "REV")}", Dim);
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
    /// <summary>
    /// At a Holdout with someone in it (GDD App. D.7): break them out, or, with the repair kit in hand at a lock, open it
    /// quietly (at a barricade the kit's no help: it's pried, and the kit stays in hand). Null away from one.
    /// </summary>
    static string? HoldoutPrompt(Sim.World world, in PlayerState p, TrainOnLine train, bool kit)
    {
        if (world.Holdouts is not { } ho || p.Parent != PlayerState.World)
            return null;
        var at = PlayerMotor.WorldPosition(p, train);
        foreach (var h in ho.All)
        {
            if (!h.Lit || ((h.Door - at) with { Y = 0 }).Length > ho.Tuning.BreachReach)
                continue;
            if (h.State == HoldoutState.Breaching)
                return h.Quiet ? $"OPENING THE LOCK {h.Progress / h.Breach(ho.Tuning).Seconds * 100:0}%. QUIETLY. KEEP AT IT"
                    : $"{(h.Layout.Kind == HoldoutKind.Shelter ? "PRYING" : "SMASHING")} IT OPEN {h.Progress / h.Breach(ho.Tuning).Seconds * 100:0}%. LOUD. KEEP AT IT";
            return kit && h.Lockable ? $"[E] HOLD: OPEN THE LOCK WITH THE KIT ({ho.Tuning.Open.Seconds:0}S, SILENT)"
                : $"[E] HOLD: {(h.Layout.Kind == HoldoutKind.Shelter ? "PRY THE BARRICADE" : "SMASH THE LOCK")} ({h.Breach(ho.Tuning).Seconds:0}S, LOUD)";
        }
        return null;
    }

    static void Radio(Overlay o, int width, IPlaySession s, int line)
    {
        var bodies = s.World.Bodies;
        if (!bodies.RadiosCarried || !s.Player.Alive)
            return;
        // Right mouse throws what's in your hands; with them empty, it sets the radio down to pass on.
        string wearing = Bound(bodies.CarriedBy(s.PlayerId) is null ? "RADIO [T]  [RMB] SET IT DOWN" : "RADIO [T]");
        o.TextRight(width - 6, 5 + 5 * line, bodies.HasRadio(s.PlayerId) ? wearing : "NO RADIO", bodies.HasRadio(s.PlayerId) ? Dim : Amber);
    }

    /// <summary>
    /// The night's line across the top (T95 playtest): how far there's left to go, and where the stops are, facilities and
    /// villages, ticked along it; the train's the bright mark. Stops behind it dim.
    /// </summary>
    static void RouteStrip(Overlay o, int width, IPlaySession s, int line)
    {
        if (s.Route is not { } route || route.Length <= 0)
            return;
        // Clear of the engine's panel top left and the link's top right.
        float w = MathF.Round(width * 0.34f), x = MathF.Round(width * 0.41f), y = 6, h = 5;
        double at = Math.Clamp(s.Train.Dynamics.Distance / route.Length, 0, 1);
        o.Rect(x - 4, y - 3, w + 8, h + line + 8, Panel);
        o.Rect(x, y, w, h, Track);
        o.Rect(x, y, MathF.Round(w * (float)at), h, Dim);
        foreach (var f in route.Features.Where(f => f.Kind is FeatureKind.Facility or FeatureKind.Village))
        {
            float fx = x + MathF.Round(w * (float)Math.Clamp(f.Start / route.Length, 0, 1));
            bool passed = f.Start < s.Train.Dynamics.Distance;
            var colour = passed ? Dim : f.Kind == FeatureKind.Facility ? Amber : Ink;
            o.Rect(fx - 1, y - 2, 3, h + 4, colour);
        }
        o.Rect(x + MathF.Round(w * (float)at) - 2, y - 3, 5, h + 6, Green);
        double left = Math.Max(0, route.Length - s.Train.Dynamics.Distance) / 1000;
        var next = route.Features.Where(f => f.Kind is FeatureKind.Facility or FeatureKind.Village && f.Start > s.Train.Dynamics.Distance)
            .OrderBy(f => f.Start).FirstOrDefault();
        string ahead = next is null ? "NO MORE STOPS" : $"STOP IN {(next.Start - s.Train.Dynamics.Distance) / 1000:0.0}";
        o.TextCentred(x + w / 2, y + h + 2, $"{left:0.0} KM LEFT  {ahead}", Ink);
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
        if (s.Link is { Waiting: { } waiting })
        {
            Big("WAITING", Amber);
            Small(waiting, Ink);
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
        if (!p.Alive)
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
                DeathCause.Waiting => "WAITING TO BE PICKED UP",
                _ => "",
            }, Ink);
            // App. D.10: the dead watch the living, through their eyes. Networked only: alone, there's nobody.
            if (s.Watching >= 0)
                Small($"WATCHING CREW {s.Watching}   [{Controls.KeyLabel(Keys.KeyFor(Control.Fire))}] OR [{Controls.KeyLabel(Keys.KeyFor(Control.Right))}] NEXT   " +
                    $"[{Controls.KeyLabel(Keys.KeyFor(Control.Left))}] BACK", Ink);
            else if (s.Link is not null && world.Run is not { Over: true })
                Small("NOBODY LEFT ALIVE TO WATCH", Dim);
            // GDD App. D: the way back is a Holdout at the next halt or yard, if the crew stops for you.
            if (world.Holdouts is { } holdouts)
                Small(holdouts.All.FirstOrDefault(h => h.Occupant == s.PlayerId && h.Lit) is { } mine
                    ? mine.State == HoldoutState.Breaching ? $"THEY'RE {(mine.Quiet ? "OPENING THE LOCK" : "BREAKING YOU OUT")}: {mine.Progress / mine.Breach(holdouts.Tuning).Seconds * 100:0}%"
                        : $"YOU'RE IN THE {HoldoutName(mine)}. [E] CALL OUT   [RMB] LET SOMEONE ELSE GO FIRST"
                    : "YOU'LL WAIT AT THE NEXT HALT OR YARD, IF THEY STOP FOR YOU   [RMB] LET SOMEONE ELSE GO FIRST", Dim);
        }
        if (p.Alive && PlayerMotor.Chilled(p, s.PlayerTuning))
            Small($"COLD: {Math.Max(0, s.PlayerTuning.Cold.DeathSeconds - p.Cold):0}S. GET INSIDE", p.Cold > s.PlayerTuning.Cold.DeathSeconds - 30 ? Red : Amber);
        if (world.Derailed)
            Big("DERAILED", Red);
        // T109 playtest ("feedback for the player to understand in multiple ways that pressure is too high"): the boiler in
        // the red, pinned at the top (the spec's 20 s to a rupture, counting), and ruptured.
        if (p.Alive && world.Train.BoilerTuning is { } bt)
        {
            var b = world.Train.Boiler;
            bool flash = world.Tick / 10 % 2 == 0;
            if (b.Ruptured)
            {
                Big("BOILER RUPTURED", Red);
                Small(b.WrenchOut ? "MEND IT AT THE FIREBOX WITH THE WRENCH" : "THE WRENCH IN THE CAB RACK MENDS IT, AT THE FIREBOX", Ink);
            }
            else if (b.AtMaxSeconds > 0)
                Big($"VENT! RUPTURE IN {Math.Max(0, bt.RuptureHoldSeconds - b.AtMaxSeconds):0}S", flash ? Red : Amber);
            else if (b.Pressure >= bt.Redline)
                Small("PRESSURE IN THE RED: VENT, OR LET THE FIRE BURN DOWN", flash ? Red : Amber);
        }
    }

    /// <summary>The player's keys (T80), for the prompts: the app sets them from the settings.</summary>
    public static Settings Keys { get; set; } = new();

    /// <summary>A prompt written with the default keys ([E], [RMB], [T]) as the player has them bound.</summary>
    public static string Bound(string prompt) =>
        // One pass, so a key bound where another default was isn't replaced twice (Use on F, the ladder's default).
        System.Text.RegularExpressions.Regex.Replace(prompt, @"\[(E|RMB|T|Z|F|R|B)\]", m => $"[{Controls.KeyLabel(Keys.KeyFor(m.Groups[1].Value switch
        {
            "E" => Control.Use,
            "RMB" => Control.Throw,
            "T" => Control.Radio,
            "Z" => Control.Uncouple,
            "F" => Control.Ladder,
            "R" => Control.RegulatorOpen,
            _ => Control.Brake,
        }))}]");

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
        // The repair kit at a Holdout's door: held, Use works the lock (quietly, GDD App. D.7), not the hands.
        if (world.Bodies.CarriedBy(s.PlayerId) is { Kind: BodyKind.RepairKit } && HoldoutPrompt(world, p, train, kit: true) is { } opening)
            return opening;
        if (world.Bodies.CarriedBy(s.PlayerId) is { } carried)
            return carried.Kind switch
            {
                // Spec D.2 "heavy items need two" (T43).
                BodyKind.Heavy when !carried.Lifted => "HOLDING AN END: IT NEEDS TWO   [E] LET GO",
                BodyKind.Heavy => "TOGETHER, INTO A CAR: [E] PUT IT DOWN",
                BodyKind.Cargo => "INTO A CAR TO LOAD IT: [E] PUT DOWN   [RMB] THROW",
                // A village find (level-design P12): it pays once it's put down aboard, in any car.
                BodyKind.Loot => $"{world.Run?.FindName(carried)?.ToUpperInvariant() ?? "A FIND"}: INTO ANY CAR TO KEEP IT   [E] PUT DOWN   [RMB] THROW",
                BodyKind.RepairKit => "THE REPAIR KIT: IT OPENS A LOCK QUIETLY   [E] PUT DOWN   [RMB] THROW",
                _ => "[E] PUT DOWN   [RMB] THROW",
            };
        if (world.Combat is { } combat && Guns.MannedGun(p, train, combat.Guns) is not null)
            return train.BoilerTuning is not null && train.Boiler.Pressure < combat.Guns.MinPressure ? "NO STEAM FOR THE TURRET"
                : train.Vehicles[Guns.MannedGun(p, train, combat.Guns)!.Value].Gun.ReloadNeeded > 0 ? "[E] HOLD: RELOAD"
                : "[LMB] FIRE   [E] + WALK: PUSH IT ALONG THE RAIL";
        // A headset player's prompts follow their reaching hand (T29), as the sim's reach does.
        var hand = world.Hand;
        var near = CrewActions.Nearest(p, train, hand);
        // T94: a ladder in reach, and the key that takes you onto it.
        if (PlayerMotor.LadderInReach(p, train, s.PlayerTuning))
            return "[F] GRAB LADDER";
        // T91: the coupling is cut with its own key, held, looking down at it.
        if (p.Surface == Surface.Coupler && near != InteractableKind.Door)
            return p.Hand != default ? "REACH DOWN AND GRIP: CUT THE COUPLING"
                : p.Pitch <= -train.Dynamics.Tuning.Couplings.UncoupleLookDownDegrees * Math.PI / 180 ? "[Z] HOLD: CUT THE COUPLING"
                : "LOOK DOWN AT THE COUPLER TO CUT IT";
        switch (near)
        {
            // A ruptured boiler (T109): mended here with the wrench in hand, and only so.
            case InteractableKind.Firebox when PlayerMotor.InCab(p, train) && train.Boiler.Ruptured && train.BoilerTuning is { } rt:
                return Kit.Held(p) == Tool.Wrench ? $"[E] HOLD: MEND THE BOILER ({p.ActionProgress / rt.RepairSeconds * 100:0}%)"
                    : Kit.Has(p.Kit, Tool.Wrench) ? "BOILER RUPTURED: THE WRENCH IN HAND TO MEND IT"
                    : "BOILER RUPTURED: THE WRENCH IS IN ITS RACK, RIGHT SIDE OF THE CAB";
            case InteractableKind.Firebox when PlayerMotor.InCab(p, train):
                return p.Hand != default && !p.Has(PlayerFlags.Shovelful) ? "SHOVEL COAL: FILL IT AT THE TENDER FIRST" : "[E] HOLD: SHOVEL COAL (FASTER)";
            // Only a reaching hand finds the coal face (T29).
            case InteractableKind.Coal when PlayerMotor.InCab(p, train):
                return p.Has(PlayerFlags.Shovelful) ? "SHOVEL FULL: INTO THE FIREBOX" : "GRIP: COAL ON THE SHOVEL";
            // T97: venting is how the train's slowed (steam sets its speed); T109, in the cab.
            case InteractableKind.Vent when PlayerMotor.InCab(p, train):
                return "[E] HOLD: VENT STEAM (SLOWER)";
            // T109: the engineering kit's rack.
            case InteractableKind.ToolRack when PlayerMotor.InCab(p, train):
                return Kit.Held(p) == Tool.Wrench ? "[E] PUT THE WRENCH BACK" : train.Boiler.WrenchOut ? "THE WRENCH IS OUT"
                    : "[E] TAKE THE WRENCH (MENDS THE BOILER)";
            case InteractableKind.Handbrake when p.Surface == Surface.Roof:
                return "[E] HOLD: HANDBRAKE";
            // T99: a cargo car's roof hatch, for the crane to lower a casting in through.
            case InteractableKind.Hatch when p.Surface == Surface.Roof:
                return train.Vehicles[p.Parent].DoorOpen(CarShape.HatchBit)
                    ? train.HatchBlocked?.Invoke(p.Parent) == true ? "THE CASTING'S IN THE HATCH" : "[E] HOLD: SHUT THE HATCH"
                    : "[E] HOLD: OPEN THE HATCH (CRANE LOADING)";
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
                BodyKind.RepairKit => "[E] TAKE THE REPAIR KIT",
                BodyKind.Loot => $"[E] TAKE {world.Run?.FindName(thing)?.ToUpperInvariant() ?? "IT"}",
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
        // A yard whose power's down (level-design D.2): restart it at the powerhouse.
        if (world.Run is { } powered && powered.PowerhouseInReach(p, train) && powered.CurrentSite is { } ps)
            return ps.Restart > 0 ? $"RESTARTING THE GENERATOR {ps.Restart / powered.PowerTuning.RestartSeconds * 100:0}%. KEEP HOLDING"
                : $"[E] HOLD: RESTART THE GENERATOR ({powered.PowerTuning.RestartSeconds:0}S, LOUD)";
        if (HoldoutPrompt(world, p, train, kit: false) is { } breach)
            return breach;
        // The crane (T48): at its controls, or at its hook on the ground.
        if (world.Run?.CurrentSite?.CraneNear(PlayerMotor.WorldPosition(p, train)) is { } crane)
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
        {
            // T97: steam drives it. At a stand on the brake, R lets it off; otherwise B brakes (coal and the vent do the rest).
            string drive = train.BoilerTuning?.SteamDrive != true ? "[R/F] REGULATOR   [B] BRAKE"
                : s.Controls.Brake > 0 && train.Dynamics.Speed < CabControls.StandingBelow ? "[R] RELEASE BRAKE" : "[B] BRAKE";
            // The lamp switch too (T52): out, smashed (the glass is out a while), or lit.
            return world.LampOutSeconds > 0 ? $"{drive}   [X] REVERSER   LAMP SMASHED ({world.LampOutSeconds:0}s)"
                : $"{drive}   [X] REVERSER   [L] LAMP {(world.LampLit ? "OFF" : "ON")}";
        }
        return null;
    }

    /// <summary>What a Holdout is, for the dead player in it (App. D.4 "fiction and art").</summary>
    static string HoldoutName(Holdout h) => h.Layout.Kind switch
    {
        HoldoutKind.PrisonCar => "PRISON CAR",
        HoldoutKind.Lockup => "HALT'S LOCKUP",
        _ => "BARRICADED SHELTER",
    };

    static void Night(Overlay o, int height, IPlaySession s, int line)
    {
        string status = PrototypeSession.RouteStatus(s.Route, s.World, s.Train);
        var parts = status.Split(" | ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            // The night's result has its own place in the middle.
            .Where(t => !t.StartsWith("VIGIL", StringComparison.Ordinal) && !t.StartsWith("DELIVERED", StringComparison.Ordinal) && !t.StartsWith("RUN LOST", StringComparison.Ordinal))
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
