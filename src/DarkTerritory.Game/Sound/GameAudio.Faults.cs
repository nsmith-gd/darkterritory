using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The train's faults on the audio checklist: a cannon fouled (state-cannon-foul; GDD §23 "Cannon fouls: someone clears it
/// by hand, under fire") and a car breached (state-breach, decided 1 Oct), with the crew boarding it up (crew-repair's
/// board-place, hammer and done). Read off the replicated gun (<see cref="GunState.Jammed"/>, its <see cref="GunState.ReloadProgress"/>
/// while it's cleared), the replicated car (<see cref="Vehicle.Breached"/>, <see cref="Vehicle.BreachAt"/>) and each
/// crewmate's record (their hold's <see cref="PlayerState.ActionProgress"/> at the hole), so a client hears what the host does.
/// The breach's other sound is the train's: inside a breached car the outside isn't muffled (<see cref="PlayerMotor.Space"/>
/// counts it outside) and its wind blows in (<see cref="BreachedAround"/>).
/// </summary>
public sealed partial class GameAudio
{
    /// <summary>A clear's or a board's count that's stopped rising this long (s) has stopped: a lost packet isn't a pause.</summary>
    const double FaultHoldGrace = 0.2;
    // The fouled charge's pfft comes a beat after the misfire's click (s).
    const double FizzleAfterMisfire = 0.12;
    // Presentation, not design: the first nail this long after the board's put to the hole, then one a blow this often.
    const double HammerFirst = 0.35, HammerEvery = 0.55;

    sealed class FaultGun
    {
        public bool Mounted, Jammed;
        public double Progress, LastRise = double.NegativeInfinity;
    }

    sealed class FaultCar
    {
        public bool Breached;
        public Double3 At;
    }

    sealed class Boarder
    {
        public double Progress, LastRise = double.NegativeInfinity, NextBlow;
        public bool Placed;
    }

    readonly Dictionary<int, FaultGun> _faultGuns = new();
    readonly Dictionary<int, FaultCar> _faultCars = new();
    readonly Dictionary<int, Boarder> _boarders = new();
    bool _ownFireWas;

    partial void FaultSounds(World world)
    {
        FoulSounds(world);
        BreachSounds(world);
    }

    partial void EndNightFaults()
    {
        _faultGuns.Clear();
        _faultCars.Clear();
        _boarders.Clear();
        _ownFireWas = false;
    }

    /// <summary>
    /// A gun's foul: the dead click the moment it fouls (everyone's gun, from its state), and again at each pull of your own
    /// trigger on a fouled gun you're at; the bore worked by hand while it's being cleared; and cleared.
    /// </summary>
    void FoulSounds(World world)
    {
        var train = world.Train;
        float outside = Occlusion(PlayerMotor.Outside);
        bool fire = OwnIntent.Has(PlayerButtons.Fire), pulled = fire && !_ownFireWas;
        _ownFireWas = fire;
        int? mine = null;
        if (pulled && world.Combat is { } combat)
            foreach (var (id, s) in CrewStates)
                if (id == OwnId)
                    mine = Guns.MannedGun(s, train, combat.Guns);
        foreach (var v in train.Vehicles)
        {
            if (v.Id >= train.Frames.Count)
                continue;
            var g = v.Gun;
            if (!_faultGuns.TryGetValue(v.Id, out var m))
            {
                // Fouled already when first seen: that's how it is, not something happening.
                _faultGuns[v.Id] = new FaultGun { Mounted = g.Mounted, Jammed = g.Jammed, Progress = g.ReloadProgress };
                continue;
            }
            if (g.Mounted && m.Mounted && Guns.Mount(train, v.Id) is { } mount)
            {
                // At the touch hole, where the priming didn't take and where it's picked clear.
                var vent = train.Frames[v.Id].ToWorld(mount.Position - mount.Facing * 0.55 + Double3.Up * 0.15);
                if (g.Jammed && (!m.Jammed || mine == v.Id))
                    Cue("state-cannon-foul.misfire", vent, outside);
                // As it fouls, the damp charge's feeble pfft out of the vent after the click (crew-mishaps).
                if (g.Jammed && !m.Jammed)
                    CrewAfter(FizzleAfterMisfire, "crew-mishaps.foul-fizzle", vent, outside);
                if (g.Jammed && m.Jammed && g.ReloadProgress > m.Progress + 1e-6)
                    m.LastRise = _time;
                if (g.Jammed && g.ReloadProgress > 0 && _time - m.LastRise < FaultHoldGrace)
                    Hold("state-cannon-foul.clear", v.Id, vent, outside);
                if (m.Jammed && !g.Jammed)
                    Cue("state-cannon-foul.cleared", vent, outside);
            }
            m.Mounted = g.Mounted;
            m.Jammed = g.Jammed;
            m.Progress = g.ReloadProgress;
        }
    }

    /// <summary>
    /// A car's breach: the shell giving way, once, at the hole, loud enough to carry; for a listener inside a breached car, the
    /// outside coming in through it; and its boarding up: a board put to the hole as each hold begins, a nail hammered every
    /// so often while the hold goes on, and done when it's shut.
    /// </summary>
    void BreachSounds(World world)
    {
        var train = world.Train;
        float outside = Occlusion(PlayerMotor.Outside);
        foreach (var v in train.Vehicles)
        {
            if (v.Id >= train.Frames.Count)
                continue;
            var hole = train.Frames[v.Id].ToWorld(v.BreachAt);
            if (!_faultCars.TryGetValue(v.Id, out var c))
            {
                _faultCars[v.Id] = new FaultCar { Breached = v.Breached, At = hole };
                continue;
            }
            if (v.Breached && !c.Breached)
                Cue("state-breach.breach", hole, outside);
            // Boarded up: the last nail inside a car that's shut again (heard through its walls from another).
            if (c.Breached && !v.Breached)
                Cue("crew-repair.done", hole, Occlusion(v.Id));
            c.Breached = v.Breached;
            c.At = hole;
        }
        if (BreachedAround(world, Mixer.Listener.Position) is { } car)
            Hold("state-breach.open-to-outside", car, train.Frames[car].ToWorld(train.Vehicles[car].BreachAt));

        foreach (var (id, s) in CrewStates)
        {
            if (Breaches.Within(s, train, world.Hand) is not { } at)
            {
                _boarders.Remove(id);
                continue;
            }
            if (!_boarders.TryGetValue(id, out var b))
                _boarders[id] = b = new Boarder();
            var hole = train.Frames[at].ToWorld(train.Vehicles[at].BreachAt);
            if (s.ActionProgress > b.Progress + 1e-6)
            {
                if (!b.Placed)
                {
                    Cue("crew-repair.board-place", hole, outside);
                    b.NextBlow = _time + HammerFirst;
                }
                b.Placed = true;
                b.LastRise = _time;
            }
            // Let go, and the next hold puts up a board again.
            if (s.ActionProgress <= 0)
                b.Placed = false;
            // Nailing it, a blow at a time, while the hold goes on.
            if (b.Placed && _time - b.LastRise < FaultHoldGrace && _time >= b.NextBlow)
            {
                Cue("crew-repair.hammer", hole, outside);
                b.NextBlow = _time + HammerEvery;
            }
            b.Progress = s.ActionProgress;
        }
    }

    /// <summary>The breached car a listener's ear is inside the walls of, if any: there, the night comes in as on the roof.</summary>
    public static int? BreachedAround(World world, Double3 ear)
    {
        var train = world.Train;
        foreach (var frame in train.Frames)
        {
            if (frame.Index <= 0 || (frame.Origin - ear).Length > frame.Shape.HalfLength + 5 || !train.Vehicles[frame.Index].Breached)
                continue;
            if (frame.Shape.Interior is { } room && room.Contains(frame.ToLocal(ear)))
                return frame.Index;
        }
        return null;
    }
}
