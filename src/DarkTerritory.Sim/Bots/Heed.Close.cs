using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Bots;

public static partial class Heed
{
    /// <summary>
    /// The Close (note 1013; Sloe's rule: "when the roof bows, someone get out"), a walker's answer to it through its own intent.
    /// In the car it's on and free (the roof bowing, or let out): out by the nearer end door with a car beyond it, opened if
    /// it's shut. Pinned: the door it's against opened, if one's in reach (the pinned can still Use a door), else it waits for
    /// a friend. Outside the car: up on its roof (or the next roof along, the gap jumped) and clubbing it till it bursts. Null
    /// with no Close about, or nothing of this walker's to do about it.
    /// </summary>
    public static PlayerIntent? TheClose(in PlayerState self, World world)
    {
        if (!self.Alive)
            return null;
        var close = world.ActiveEnemies.OfType<Close>().Where(e => !e.Gone && e.Attached > 0 && e.Mode is CloseMode.Bow or CloseMode.Pour or CloseMode.Pin)
            .OrderBy(e => e.Id).FirstOrDefault();
        if (close is null)
            return null;
        var train = world.Train;
        int car = close.Attached;
        if (car >= train.Frames.Count)
            return null;
        var shape = train.Frames[car].Shape;
        if (Close.InCar(self, train, car))
        {
            // Out by an end with footing beyond it (never a side door at speed: note 380), the nearer one.
            int end = self.Position.Z >= 0 ? 1 : -1;
            if (!WarmUp.Floor(train, car, end))
                end = -end;
            if (!WarmUp.Floor(train, car, end))
                return self.Has(PlayerFlags.Held) ? null : new PlayerIntent();
            int door = shape.DoorList.Where(d => Math.Sign(d.Box.Centre.Z) == end && Math.Abs(d.Box.Centre.Z) > 1).Select(d => d.Index).First();
            var (stand, yaw) = WarmUp.Inside(shape, door);
            bool shut = !train.Vehicles[car].DoorOpen(door);
            if (self.Has(PlayerFlags.Held))
            {
                // Pinned where it stands: only a door in reach is any use, faced and worked.
                if (!shut || CrewActions.NearestInteractable(self, train, world.Hand) is not ({ Kind: InteractableKind.Door }, var v) || v != car)
                    return new PlayerIntent();
                var (turn, faced) = WarmUp.Steer(self, self.Position, yaw);
                return faced ? new PlayerIntent { Buttons = PlayerButtons.Use } : turn;
            }
            var (step, there) = WarmUp.Steer(self, stand, yaw);
            if (shut)
                return there ? new PlayerIntent { Buttons = PlayerButtons.Use } : step;
            // Open: on through it onto the plate beyond.
            var beyond = stand + new Double3(0, 0, end * 1.6);
            return WarmUp.Steer(self, beyond, yaw).Step;
        }
        if (self.Has(PlayerFlags.Held) || self.Surface != Surface.Roof || self.Parent <= 0 || self.Parent >= train.Frames.Count)
            return null;
        if (self.Parent == car)
            return Strike(self, train, close.WorldPosition(train), 1.2);
        // A car or two along: along the roofs to it, the gaps jumped.
        var consist = train.Dynamics.Consist;
        int from = consist.IndexOf(self.Parent), to = consist.IndexOf(car);
        if (from < 0 || to < 0 || Math.Abs(from - to) > 2)
            return null;
        return StopHand.AlongRoofs(self, train, to < from ? -1 : 1, jumpGaps: true);
    }
}
