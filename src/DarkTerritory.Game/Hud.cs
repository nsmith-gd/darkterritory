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
/// <item>centre: what's happening to you (dead, waiting, a Vigil, cold, the night's result);</item>
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
        if (s.Link is { } link)
            Link(o, width, link, line);
        Radio(o, width, s, line);
        Alerts(o, width, height, s, line);
        if (Prompt(s) is { } prompt)
        {
            float w = o.Font.Measure(prompt) + 8;
            o.Rect(MathF.Round((width - w) / 2), height - 44, w, line + 4, Panel);
            o.TextCentred(width / 2f, height - 42, prompt, Ink);
        }
        Night(o, height, s, line);
        if (p.Alive && crosshair)
        {
            // A small cross, for aiming and for "what am I looking at".
            float cx = width / 2f, cy = height / 2f;
            o.Rect(cx - 2, cy, 5, 1, Ink with { W = 0.55f });
            o.Rect(cx, cy - 2, 1, 5, Ink with { W = 0.55f });
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
        string wearing = bodies.CarriedBy(s.PlayerId) is null ? "RADIO [T]  [RMB] SET IT DOWN" : "RADIO [T]";
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
                DeathCause.Struck => "STRUCK BY THE TUNNEL MOUTH",
                DeathCause.Thrown => "THROWN OFF ON THE CURVE",
                DeathCause.Burned => "BURNED IN A BLAZING CAR",
                DeathCause.Gnawed => "EATEN BY THE GNAWERS",
                DeathCause.Ferryman => "SLOWED FOR THE LANTERN",
                DeathCause.Stoker => "BURNED DRIVING IT OUT OF THE FIREBOX",
                _ => "",
            }, Ink);
            if (world.Vigil is { Permitted: true })
                Small("A VIGIL COULD BRING YOU BACK: YOUR BODY IN THE ENGINE, THE TRAIN STOPPED", Dim);
        }
        if (world.Vigil is { Active: true } v)
        {
            Big($"VIGIL {v.Left:0}", Red);
            Small("ENGINE OFF. LIGHTS OUT. GUNS DEAD. THE CHOIR IS COMING", Ink);
        }
        if (p.Alive && PlayerMotor.Chilled(p, s.PlayerTuning))
            Small($"COLD: {Math.Max(0, s.PlayerTuning.Cold.DeathSeconds - p.Cold):0}S. GET INSIDE", p.Cold > s.PlayerTuning.Cold.DeathSeconds - 30 ? Red : Amber);
        if (p.Alive && p.Has(PlayerFlags.Revived))
            Small("REVIVED: COLD, LIGHT THINGS ONLY, NO GUNS UNTIL THE NEXT STOP", Dim);
        if (world.Derailed)
            Big("DERAILED", Red);
    }

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
            return p.Has(PlayerFlags.Revived) ? "NO GUNS UNTIL THE NEXT STOP"
                : world.EmergencyLights || train.BoilerTuning is not null && train.Boiler.Pressure < combat.Guns.MinPressure ? "NO STEAM FOR THE TURRET"
                : "[LMB] FIRE";
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
                // Everyone can see a body laid in the engine; whether its owner is dead, the host decides.
                bool body = world.Bodies.All.Any(b => b.Kind == BodyKind.Ragdoll && b.Parent == 0 && b.Carrier < 0);
                return world.Vigil is { Active: false, Permitted: true } v && body && v.Still(train)
                    ? $"[E] HOLD: VENT AND BEGIN THE VIGIL ({v.NextSeconds:0}S)"
                    : "[E] HOLD: VENT";
            case InteractableKind.Handbrake when p.Surface == Surface.Roof:
                return "[E] HOLD: HANDBRAKE";
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
            // The Vigil and the night's result have their own place in the middle.
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
