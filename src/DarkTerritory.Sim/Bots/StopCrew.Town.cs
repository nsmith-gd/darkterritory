using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim.Bots;

/// <summary>
/// The town's streets between nights (ARCHITECTURE §8 note 1031; level-design T.4: "the bots in its streets"; note 692's
/// handoff: "they work the yard, not the streets"). A bot hand off duty in a town walks it as the crew do: down off the
/// train, out to one of the streets nearest the square (its own, by its place in the crew), along it a way, a while stood
/// looking, then on to another spot along it, round the houses (<see cref="FootPath"/>). The crew delivered (the terminus's
/// town, <see cref="World.Arrival"/>), or at the gate while someone playing is out in the town (note 995: they're waited
/// on); when that's over it walks back to the foot of the nearest car's steps and its own part takes it aboard. All through
/// intent: it walks, as anyone does. Every choice is its place in the crew and the town's plan: the same on every machine.
/// </summary>
public sealed partial class StopHand
{
    Double3? _strollTo;
    int _strollLeg, _strollTicks;
    bool _strolling;

    /// <summary>Whether it's out in the town's streets (for tests and traces).</summary>
    public bool Strolling => _strolling;

    /// <summary>
    /// Its stroll in the town this tick, when it's off duty in one (<paramref name="free"/>: delivered, or someone playing out
    /// in it at the gate): off the train, out along a street, a look round, on. Not free, and out: back to the train's side.
    /// Null when there's nothing for it to do in the town (its own part has it).
    /// </summary>
    public PlayerIntent? Town(in PlayerState self, World world, bool free)
    {
        var t = Crew(world);
        var town = world.Run?.Phase == RunPhase.Arrived ? world.Arrival : world.Town;
        if (!self.Alive || t.TownStroll <= 0 || town?.Plan.Bounds is not { Streets.Count: > 0 } bounds)
            return _strolling ? Home(self, world) : null;
        if (!free)
            return _strolling ? Home(self, world) : null;
        var train = world.Train;
        // Off the train first, on the town's side of it (or the other, where only that has ground).
        if (self.Parent != PlayerState.World)
        {
            if (self.Parent >= 0 && Math.Abs(train.RakeOf(self.Parent).Velocity) > 0.05)
                return null;
            _strolling = true;
            Doing = "down into the town";
            int side = town.Plan.Square.Side == 0 ? 1 : town.Plan.Square.Side;
            return OffTheCar(self, train, side) ?? GetDown(self, train, side);
        }
        _strolling = true;
        var here = PlayerMotor.WorldPosition(self, train);
        if (_strollTo is not { } goal)
        {
            _strollTo = goal = Spot(town, bounds, t);
            _strollTicks = 0;
        }
        if ((Flat(here) - Flat(goal)).Length < 1.2 || _strollTicks > t.TownLegSeconds * SimConstants.TickRate)
        {
            // There (or as near as it'll get): a look round, then on along the street.
            if (_strollTicks <= t.TownLegSeconds * SimConstants.TickRate)
                _strollTicks = (int)(t.TownLegSeconds * SimConstants.TickRate) + 1;
            if (++_strollTicks > (t.TownLegSeconds + t.TownLinger) * SimConstants.TickRate)
            {
                _strollLeg++;
                _strollTo = null;
            }
            Doing = "looking round the town";
            return new PlayerIntent { LookYaw = (float)(0.004 * ((member + _strollLeg) % 2 == 0 ? 1 : -1)) };
        }
        _strollTicks++;
        Doing = "out in the town";
        var step = Follow(self, train, goal);
        // A stroll, not a run.
        return step with { Buttons = step.Buttons & ~PlayerButtons.Run };
    }

    /// <summary>
    /// The next spot on its stroll: the street its place picks among those nearest the square (either side of the line), a
    /// point along it out from the square's middle, a little further each leg and either way, within the stroll's reach.
    /// </summary>
    Double3 Spot(Towns.Town town, Towns.TownBounds bounds, StopCrewTuning t)
    {
        var sq = town.Plan.Square;
        double mid = (sq.S0 + sq.S1) / 2;
        var near = bounds.Streets.OrderBy(x => Math.Abs(x.D)).ThenBy(x => x.D).Take(Math.Max(1, t.TownStreets)).ToList();
        var street = near[(member + _strollLeg / 2) % near.Count];
        double reach = t.TownStroll * (0.35 + 0.65 * ((member * 7 + _strollLeg * 3) % 5) / 4.0);
        double s = Math.Clamp(mid + ((member + _strollLeg) % 2 == 0 ? 1 : -1) * reach, street.S0 + 4, street.S1 - 4);
        return town.World(s, street.At(s));
    }

    /// <summary>Back from the town to the foot of the nearest car's steps, where its own part takes it aboard; null once it's there.</summary>
    PlayerIntent? Home(in PlayerState self, World world)
    {
        var train = world.Train;
        if (self.Parent != PlayerState.World)
        {
            (_strolling, _strollTo) = (false, null);
            return null;
        }
        var here = PlayerMotor.WorldPosition(self, train);
        int car = -1;
        double best = double.MaxValue;
        for (int i = 1; i < train.Frames.Count; i++)
            if ((Flat(train.Frames[i].Origin) - Flat(here)).Length is var dist && dist < best)
                (car, best) = (i, dist);
        if (car < 0)
        {
            (_strolling, _strollTo) = (false, null);
            return null;
        }
        var frame = train.Frames[car];
        double w = frame.Shape.Bounds.Max.X + Clear;
        int side = frame.ToLocal(here).X >= 0 ? 1 : -1;
        var foot = frame.ToWorld(new Double3(side * w, 0, 0));
        if ((Flat(here) - Flat(foot)).Length < 1.5)
        {
            (_strolling, _strollTo) = (false, null);
            return null;
        }
        Doing = "back to the train";
        return Follow(self, train, foot);
    }
}
