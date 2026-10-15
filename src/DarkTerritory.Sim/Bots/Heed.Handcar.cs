using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Bots;

public static partial class Heed
{
    /// <summary>m: a bot on the ground this near a standing handcar with someone playing at its handle comes to the other end.</summary>
    const double HandcarComeFrom = 15;

    /// <summary>
    /// The handcar (GDD §24.1; note 729): the other end of the handle for a crewmate playing. A bot never takes the handcar out
    /// on its own; with someone playing at one end of the handle and the other free, the nearest bot on the ground nearby (it
    /// at a stand) walks aboard by a side step and takes it. At the handle it pumps in time the way the car's going (it follows
    /// its mate: it doesn't start it, or fight it, and lets the brake be), and lets go when its mate does. What it sees is what
    /// every client has: who's where and pumping, the handle's swing and the car's speed.
    /// </summary>
    public static PlayerIntent Handcar(PlayerIntent intent, in PlayerState self, World world, int me,
        IReadOnlyList<(int Id, PlayerState State)> others, CrewCalls? calls)
    {
        var train = world.Train;
        if (train.Handcar is not { Carried: false } h || !Free(self) || train.HandcarRake is not { } rake)
            return intent;
        var t = train.Dynamics.Tuning.Handcar;
        // Someone playing at an end of the handle: whose, and which end.
        int? playing = null;
        foreach (var (id, s) in others)
            if (id != me && (calls is null || !calls.IsBot(id)) && s.Alive && s.Parent == h.Id && s.Has(PlayerFlags.Pumping))
            {
                playing = Train.Handcar.EndAt(s.Position.Z);
                break;
            }
        if (self.Has(PlayerFlags.Pumping))
        {
            // Its mate's let go: so does it, and stands.
            if (playing is null)
                return new PlayerIntent { Buttons = PlayerButtons.Jump };
            // In time the way it's running; standing, or on the brake, still.
            if (rake.Speed < 0.2 || h.Brake > 0)
                return new PlayerIntent();
            return new PlayerIntent { MoveZ = (float)Train.Handcar.InTime(h.Phase, Train.Handcar.EndAt(self.Position.Z), Math.Sign(rake.Velocity)) };
        }
        if (playing is not { } theirs)
            return intent;
        int mine = -theirs;
        // Someone (a bot or a friend) already has the other end.
        if (others.Any(o => o.Id != me && o.State.Alive && o.State.Parent == h.Id && o.State.Has(PlayerFlags.Pumping) && Train.Handcar.EndAt(o.State.Position.Z) == mine))
            return intent;
        if (self.Parent == h.Id && self.Grounded)
        {
            // Aboard: to the free end, then take it (the seat's press, as at the crane's stand).
            if (world.AtHandle(self) == mine)
                return new PlayerIntent { Actions = PlayerActions.Seat };
            var place = Train.Handcar.Place(t, mine);
            return Toward(self, place - self.Position);
        }
        // On the ground by it, at a stand: the nearest of the bots there comes.
        if (self.Parent != PlayerState.World || rake.Speed > 0.2 || train.Dynamics.Speed > 0.2)
            return intent;
        var car = train.Frames[h.Id];
        var here = PlayerMotor.WorldPosition(self, train);
        double mineFrom = Flat(here - car.Origin);
        if (mineFrom > HandcarComeFrom)
            return intent;
        foreach (var (id, s) in others)
            if (id != me && calls?.IsBot(id) == true && s.Alive && s.Parent == PlayerState.World
                && Flat(PlayerMotor.WorldPosition(s, train) - car.Origin) is var d && (d < mineFrom || d == mineFrom && id < me))
                return intent;
        // To the side step nearer it, then up onto the deck and along to the end.
        var local = car.ToLocal(here);
        int side = local.X >= 0 ? 1 : -1;
        var step = car.ToWorld(new Double3(side * (t.Width / 2 + 0.6), 0, 0));
        if (Flat(here - step) > 0.4 && Math.Abs(local.X) > t.Width / 2 + 0.3)
            return Toward(self, train, step);
        return Toward(self, train, car.ToWorld(Train.Handcar.Place(t, mine)));
    }

    /// <summary>Walking toward a point in the world (its own frame's ground, or the handcar's deck): turned to it, forward.</summary>
    static PlayerIntent Toward(in PlayerState self, TrainOnLine train, Double3 at)
    {
        var turn = Facing(self, train, at);
        return turn with { MoveZ = 1 };
    }

    /// <summary>Walking along an offset in its own frame (aboard the handcar, to an end of the handle).</summary>
    static PlayerIntent Toward(in PlayerState self, Double3 offset)
    {
        double yaw = DMath.Atan2(-offset.X, -offset.Z);
        return new PlayerIntent { LookYaw = (float)Math.IEEERemainder(yaw - self.Yaw, 2 * Math.PI), MoveZ = Flat(offset) > 0.15 ? 1 : 0 };
    }
}
