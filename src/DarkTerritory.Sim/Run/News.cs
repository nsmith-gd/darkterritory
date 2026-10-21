using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Run;

public enum NewsKind : byte { CarsLost, EngineFailing, EngineBroken, Marked, Fire, Loose }

/// <summary>
/// A piece of the night's news (note 663): cars the engine's lost, or the engine failing or broken down, and what did it.
/// </summary>
/// <param name="Cars">The cars lost, front to back (none for the engine's).</param>
/// <param name="Cause">A <see cref="Parting"/> for cars lost, a <see cref="KnockCause"/> for the engine, an
/// <see cref="Enemies.FireCause"/> for a fire (note 851), a <see cref="LooseCause"/> for a pin just jumped (note 854).</param>
/// <param name="Detail">For a pin, what worked it loose (<see cref="LooseCause"/>); for a fire, the car it came from; for a pin just
/// jumped on a bend, the bend's posted board in km/h.</param>
/// <param name="Who">Whose hand: the crewmate who cut them, or who was driving when the pin worked loose, or who threw the lamp; −1 nobody's.</param>
/// <param name="Speed">The speed it was done at (m/s): the knock, or the pin's loosening; 0 when it's not said.</param>
/// <param name="Told">Said to the crew; a crewmate's cut at a stand isn't (it's only in the host's log, for the report).</param>
public readonly record struct NewsEvent(int Id, uint Tick, NewsKind Kind, int[] Cars, byte Cause, byte Detail, int Who, double Speed,
    bool Told = true);

/// <summary>
/// Note 663 (queue #394; the director: "did a coupler break on its own or was that a creature?"): the line the crew are told,
/// everyone alike, as it happens. The report reads the same events (<see cref="Run"/>'s lost cars), so the two never disagree.
/// </summary>
public static class News
{
    /// <summary>"CAR 4", "CARS 4-6", "CARS 2, 5" (the HUD's font has no en dash; F1).</summary>
    public static string Cars(IReadOnlyList<int> cars)
    {
        if (cars.Count == 1)
            return $"CAR {cars[0]}";
        var sorted = cars.Order().ToList();
        bool run = sorted[^1] - sorted[0] == sorted.Count - 1;
        return run ? $"CARS {sorted[0]}-{sorted[^1]}" : $"CARS {string.Join(", ", sorted)}";
    }

    static string Kmh(double metresPerSecond) => $"{Math.Abs(metresPerSecond) * 3.6:0} KM/H";

    /// <summary>
    /// Note 939 (the director's playtest of 20 Oct evening, line 9: "Why is it on fire? We've like barely been here"): what set
    /// a car alight, in the HUD's capitals, as the news says it as it starts and the HUD says it again to whoever's at the
    /// fire for as long as it burns (<c>Hud.FireLine</c>); null for a fire set by hand. <paramref name="from"/>: the car it came
    /// from, or −1; <paramref name="who"/>: whose lamp, or −1; <paramref name="inside"/>: a hound's, lit from inside the car.
    /// </summary>
    public static string? FireWhy(World world, Enemies.FireCause cause, int from, int who, bool inside) => cause switch
    {
        Enemies.FireCause.HotBox => "ITS HOT BOX RAN HOT",
        Enemies.FireCause.FireFlies => "FIRE FLIES AT ITS LAMP",
        Enemies.FireCause.Hounds => inside ? "A CINDER HOUND INSIDE IT" : "A CINDER HOUND ON ITS ROOF",
        Enemies.FireCause.Lamp => who >= 0 ? $"A LAMP THROWN BY {IncidentLog.NameOf(world, who).ToUpperInvariant()}" : "A LAMP BROKEN",
        Enemies.FireCause.Jumped => from >= 0 ? $"SPREAD FROM CAR {from}" : "SPREAD FROM THE NEXT CAR",
        Enemies.FireCause.Blast => from >= 0 ? $"CAR {from} WENT UP" : "THE NEXT CAR WENT UP",
        _ => null,
    };

    /// <summary>
    /// Note 939: a fire's news detail byte: the car it came from (a jump, a blast), or how many fires its car had had tonight
    /// before it (low four bits) and, for a hound's, whether it was lit from inside (the top bit).
    /// </summary>
    public static byte FireDetail(Enemies.CarFire fire) => fire.Cause is Enemies.FireCause.Jumped or Enemies.FireCause.Blast
        ? (byte)Math.Clamp(fire.From, 0, 255)
        : (byte)(Math.Clamp(fire.Again, 0, 15) | (fire.HoundInside ? 0x80 : 0));

    /// <summary>
    /// The line as the crew read it, in capitals (the HUD's): what happened and why, no instructions (note 402's rule; F1's
    /// wording). What worked a pin loose is said as it jumps and again as it parts (note 854); who was driving is the report's
    /// (it's longer than five seconds' reading).
    /// </summary>
    public static string Line(World world, NewsEvent e)
    {
        // Note 717: a shove that killed marks the shover, and everyone's told: "MARKED: DAVE (SHOVED PRIYA OFF AT 58 KM/H)".
        // Note 851 (the director, 16 Oct: "how'd that even get started?"): a fire, where and from what, as it starts.
        if (e.Kind == NewsKind.Fire)
        {
            // Note 939: a car alight again tonight says so; a hound's fire, where the hound was.
            var cause = (Enemies.FireCause)e.Cause;
            bool from = cause is Enemies.FireCause.Jumped or Enemies.FireCause.Blast;
            int again = from ? 0 : e.Detail & 15;
            bool inside = !from && (e.Detail & 0x80) != 0;
            string head = $"FIRE IN {Cars(e.Cars)}{(again > 0 ? " AGAIN" : "")}";
            return FireWhy(world, cause, from ? e.Detail : -1, e.Who, inside) is { } why ? $"{head} · {why}" : head;
        }
        // Note 854 (the director, 16 Oct: "What is making these couplings loose?"): a pin jumped, the gap and what jumped it, as
        // it does, with the 90 s still to tighten it in.
        if (e.Kind == NewsKind.Loose)
        {
            string gap = e.Cars.Length > 0 ? $"COUPLING LOOSE BEHIND CAR {e.Cars[0]}" : "COUPLING LOOSE";
            return (LooseCause)e.Cause switch
            {
                LooseCause.RunIn => $"{gap} · A HARD BRAKE AT {Kmh(e.Speed)}",
                LooseCause.Knock => $"{gap} · COUPLED UP AT {Kmh(e.Speed)}",
                LooseCause.Bend => e.Detail > 0 ? $"{gap} · A {e.Detail} KM/H BEND TAKEN AT {Kmh(e.Speed)}" : $"{gap} · A BEND TAKEN AT {Kmh(e.Speed)}",
                // Note 865: jumped clear, and it takes two (what it is, not what to do: note 402).
                // The warning first (the director, 19 Oct: "hella telegraphing"), where and why, and that it wants two now; then
                // the jump itself.
                LooseCause.Working => $"{(e.Cars.Length > 0 ? $"PIN WORKING LOOSE BEHIND CAR {e.Cars[0]}" : "PIN WORKING LOOSE")} · A RAIL JOINT AT {Kmh(e.Speed)} · GET TWO TO IT",
                LooseCause.Jumped => $"{(e.Cars.Length > 0 ? $"PIN JUMPED BEHIND CAR {e.Cars[0]}" : "PIN JUMPED")} · TWO TO HOLD IT",
                _ => gap,
            };
        }
        if (e.Kind == NewsKind.Marked)
            return $"MARKED: {IncidentLog.NameOf(world, e.Who).ToUpperInvariant()} (SHOVED {IncidentLog.NameOf(world, e.Cars.Length > 0 ? e.Cars[0] : -1).ToUpperInvariant()} OFF"
                + (e.Speed >= 1 ? $" AT {Kmh(e.Speed)})" : ")");
        if (e.Kind != NewsKind.CarsLost)
            return $"{(e.Kind == NewsKind.EngineBroken ? "ENGINE BROKEN DOWN" : "ENGINE FAILING")} · {Knock((KnockCause)e.Cause, e.Speed)}";
        string cars = Cars(e.Cars);
        string it = e.Cars.Length == 1 ? "IT" : "THEM";
        return (Parting)e.Cause switch
        {
            // Note 854: and what worked it loose, so the parting reads as the jump the crew were told of 90 s before.
            Parting.Pin => (LooseCause)e.Detail switch
            {
                LooseCause.RunIn => $"{cars} LOST · THE PIN THE HARD BRAKE WORKED LOOSE",
                LooseCause.Knock => $"{cars} LOST · THE PIN THE HARD COUPLING WORKED LOOSE",
                LooseCause.Bend => $"{cars} LOST · THE PIN THE BEND WORKED LOOSE",
                // Note 865: a jumped pin nobody held, or one of the two holding it let go (and who).
                LooseCause.Jumped => $"{cars} LOST · THE PIN THE RAIL JOINT JUMPED",
                LooseCause.LetGoPin => $"{cars} LOST · {IncidentLog.NameOf(world, e.Who).ToUpperInvariant()} LET GO OF THE PIN",
                LooseCause.LetGoBrake => $"{cars} LOST · {IncidentLog.NameOf(world, e.Who).ToUpperInvariant()} LET GO OF THE BRAKE WHEEL",
                LooseCause.LetGoThrottle => $"{cars} LOST · {IncidentLog.NameOf(world, e.Who).ToUpperInvariant()} OPENED THE THROTTLE",
                _ => $"{cars} LOST · THE PIN WORKED LOOSE",
            },
            Parting.Knotter => $"{cars} LOST · A KNOTTER PARTED {it}",
            Parting.Crew => $"{cars} LOST · CUT LOOSE BY {IncidentLog.NameOf(world, e.Who).ToUpperInvariant()}",
            Parting.CarHugger => $"{cars} LOST · THE CAR HUGGER TOOK {it}",
            Parting.Passenger => $"{cars} LOST · THE PASSENGER ROLLED {it} AWAY",
            Parting.Apart => e.Cars.Length == 1 ? $"{cars} CAME APART" : $"{cars} LOST · CAR {e.Cars[0]} CAME APART",
            _ => $"{cars} LOST",
        };
    }

    /// <summary>What took the engine down: what it hit and how fast, or BATTERED when it isn't known.</summary>
    static string Knock(KnockCause cause, double speed) => cause switch
    {
        KnockCause.BufferStop => $"HIT THE BUFFER STOP AT {Kmh(speed)}",
        KnockCause.Collision => $"HIT CARS AT {Kmh(speed)}",
        KnockCause.Wreck => $"HIT THE FALLEN TOWER AT {Kmh(speed)}",
        KnockCause.Sleepers => $"HIT DEBRIS AT {Kmh(speed)}",
        KnockCause.Points => "RAN THROUGH THE POINTS",
        KnockCause.Brass => $"THE BRASS FIELD AT {Kmh(speed)}",
        KnockCause.Bend => $"TOOK A BEND AT {Kmh(speed)}",
        KnockCause.Fire => "BURNED",
        KnockCause.Blast => "A KEG'S BLAST",
        KnockCause.Worn => "RUN ON FAILING",
        _ => "BATTERED",
    };
}
