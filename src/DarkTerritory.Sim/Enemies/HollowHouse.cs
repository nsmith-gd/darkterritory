using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim.Enemies;

/// <summary>What the Hollow House is doing (replicated in <see cref="Enemy.Height"/>), for its look and its sound.</summary>
public enum HollowMode : byte { Still, Rumble, Shut, Sink, Collapse, Settled }

/// <summary>
/// THE HOLLOW HOUSE · vibration · outside (GDD §21; ARCHITECTURE §8 note 585; the director, 9 Oct 2026: "start maybe rumbling
/// the ground underneath as a tell, and then start quickly closing all the doors. And any players who are trapped inside, like
/// a Venus flytrap, that grumbling should be the house starting to sink. And then it should collapse down on the player ...
/// The way to counter it really should just be awareness of the house and awareness of the surroundings";
/// docs/design/creatures/hollow-house.md). The house is the creature. Someone inside a moment and the ground rumbles under it
/// (the tell); if anyone's still inside when the rumble ends, its doors slam shut one after another, quickly, held shut, and
/// it sinks and comes down on whoever's inside (a crush: a structure coming down kills, as a fall does). Nobody inside by
/// then and it settles still again. Rule: know the house you're in.
/// </summary>
/// <remarks>
/// One to a house. <see cref="Enemy.Local"/> is the house's middle, on its floor; <see cref="Enemy.Extra2"/> the house (an
/// index into the stop walls' <c>OpenHouses</c>); <see cref="Enemy.Extra"/> how far it's sunk (metres, 0 standing);
/// <see cref="Enemy.Height"/> its <see cref="HollowMode"/>. The art lowers the house's drawing by the sink, shakes it while it
/// rumbles, and brings it down at <see cref="HollowMode.Collapse"/>.
/// </remarks>
public sealed class HollowHouse(int id) : Enemy(id)
{
    double _modeSeconds, _inside;

    public override EnemyKind Kind => EnemyKind.HollowHouse;
    public override PressureZone Zone => PressureZone.Outside;
    public override Sense Sense => Sense.Vibration;
    public override Want Want => Want.Kill;
    /// <summary>It belongs to its stop: the director never dismisses it for want of company.</summary>
    public override bool StaysAboard => true;
    /// <summary>It's a house: no blow lands on it.</summary>
    public override double MeleeRadius => 0;

    public HollowMode Mode => (HollowMode)(int)Height;
    public int House => (int)Extra2;
    /// <summary>How far it's sunk, in metres.</summary>
    public double Sunk => Extra;

    public static HollowHouse In(int id, StopWalls.OpenHouse house) =>
        new(id) { Attached = Loose, Local = house.Middle, Extra = 0, Extra2 = house.Index, Health = 1 };

    void SetMode(HollowMode mode)
    {
        if (Mode != mode)
            _modeSeconds = 0;
        Height = (double)mode;
    }

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.HollowHouse;
        double dt = SimConstants.TickSeconds;
        _modeSeconds += dt;
        if (ctx.Train.Walls is not { } walls)
            return;
        var inside = ctx.LivingCrew().Where(c => walls.InHouse(House, c.World + Double3.Up * 0.5)).OrderBy(c => c.Player.Id).ToList();
        var doors = walls.HouseDoors.Where(d => d.House == House).OrderBy(d => d.Key).Select(d => d.Key).ToList();
        switch (Mode)
        {
            case HollowMode.Still:
                _inside = inside.Count > 0 ? _inside + dt : 0;
                if (_inside >= t.WaitSeconds)
                {
                    Enter(ctx, SpinePhase.Telegraph);
                    SetMode(HollowMode.Rumble);
                }
                return;
            case HollowMode.Rumble:
                // The tell. Out by its end, and it's still again.
                if (_modeSeconds < t.RumbleSeconds)
                    return;
                if (inside.Count == 0)
                {
                    _inside = 0;
                    Enter(ctx, SpinePhase.Dormant);
                    SetMode(HollowMode.Still);
                    return;
                }
                if (Enter(ctx, SpinePhase.Commit))
                    SetMode(HollowMode.Shut);
                return;
            case HollowMode.Shut:
                {
                    // One door after another, quickly; held shut.
                    int shut = Math.Min(doors.Count, (int)(_modeSeconds / t.ShutEvery) + 1);
                    for (int i = 0; i < shut; i++)
                        walls.SetShut(doors[i], true);
                    if (shut >= doors.Count)
                        SetMode(HollowMode.Sink);
                    return;
                }
            case HollowMode.Sink:
                foreach (var k in doors)
                    walls.SetShut(k, true);
                Extra = Math.Min(t.SinkDepth, t.SinkDepth * _modeSeconds / t.SinkSeconds);
                if (_modeSeconds < t.SinkSeconds)
                    return;
                // Down on whoever's in it.
                Enter(ctx, SpinePhase.Punish);
                foreach (var c in inside)
                    ctx.Harm(c.Player.Id, t.Crush, DeathCause.Crushed);
                SetMode(HollowMode.Collapse);
                return;
            case HollowMode.Collapse:
                foreach (var k in doors)
                    walls.SetShut(k, true);
                if (_modeSeconds >= 1)
                    SetMode(HollowMode.Settled);
                return;
            case HollowMode.Settled:
                // It stays as it fell: shut, sunk, never entered again.
                foreach (var k in doors)
                    walls.SetShut(k, true);
                Enter(ctx, SpinePhase.Dormant);
                return;
        }
    }

    /// <summary>No blow does anything to a house.</summary>
    public override void Struck(EnemyContext ctx, int by, double damage) => Marked(ctx, by);
}
