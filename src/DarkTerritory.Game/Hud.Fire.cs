using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game;

public static partial class Hud
{
    /// <summary>How far from a burning car's sides and ends (m) its cause is still shown: at its door, on the ground beside it.</summary>
    public const double FireLineReach = 3;

    /// <summary>
    /// Note 939 (the director's playtest of 20 Oct evening, line 9: "We've got car one. I open it. Oh my god, it's on fire. Why
    /// is it on fire? We've like barely been here"): the news says what lit a car as it starts, for five seconds, to whoever's
    /// looking; whoever comes to the fire after (in it, at its door, beside it) is told again, for as long as it burns:
    /// "CAR 1 ALIGHT AGAIN · A CINDER HOUND ON ITS ROOF". From what's replicated (the fire's cause rides on it), so a joiner
    /// reads it as the host does. Null when there's no fire there, or its cause is no one's (a fire set by hand), or the news is
    /// saying it now.
    /// </summary>
    public static string? FireLine(IPlaySession s)
    {
        var me = s.Player;
        if (!me.Alive || s.World.Enemies is null)
            return null;
        var at = PlayerMotor.WorldPosition(me, s.Train);
        CarFire? nearest = null;
        foreach (var e in s.World.ActiveEnemies)
        {
            if (e is not CarFire { Gone: false } fire || fire.Phase is SpinePhase.Dormant or SpinePhase.BreakOff
                || fire.Attached <= 0 || fire.Attached >= s.Train.Frames.Count)
                continue;
            if (me.Parent != fire.Attached && !Beside(s.Train.Frames[fire.Attached], at))
                continue;
            if (nearest is null || me.Parent == fire.Attached)
                nearest = fire;
        }
        if (nearest is not { } f || News.FireWhy(s.World, f.Cause, f.From, -1, f.HoundInside) is not { } why)
            return null;
        // The news is saying it now: once is enough.
        if (NewsUp(s.World) is { Kind: NewsKind.Fire } news && news.Cars.Length > 0 && news.Cars[0] == f.Attached
            && Shown((s.World.Tick - news.Tick) * SimConstants.TickSeconds, s.World.NewsTuning.Seconds) > 0)
            return null;
        return $"CAR {f.Attached} ALIGHT{(f.Again > 0 ? " AGAIN" : "")} · {why}";
    }

    static bool Beside(Sim.Train.CarFrame car, Double3 world)
    {
        var local = car.ToLocal(world);
        var box = car.Shape.Bounds;
        return local.X >= box.Min.X - FireLineReach && local.X <= box.Max.X + FireLineReach
            && local.Z >= box.Min.Z - FireLineReach && local.Z <= box.Max.Z + FireLineReach
            && local.Y >= box.Min.Y - FireLineReach && local.Y <= box.Max.Y + FireLineReach;
    }
}
