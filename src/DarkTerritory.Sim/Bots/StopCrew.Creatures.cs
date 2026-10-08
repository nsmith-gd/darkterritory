using Ballast;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Bots;

public sealed partial class StopHand
{
    /// <summary>
    /// Note 491: down off a standing train to a point on the ground, and on foot to within <paramref name="within"/> of it
    /// (flat), for what the six creatures of 8 Oct ask of a crew there (a body to stand over, a beaver to club, a wreck to
    /// clear, a Knotter or a Hotbox to get at): out of the cab or a car by its side door, up off a ladder and off a roof's or
    /// a gap's edge on the side the point is, as a stop's hands get down (<see cref="GetDown"/>, <see cref="OffTheCar"/>);
    /// on the ballast by <see cref="OnFoot"/>. Never off anything moving (it stands). Null once it's there.
    /// </summary>
    public PlayerIntent? Afoot(in PlayerState self, World world, Double3 target, double within)
    {
        var train = world.Train;
        if (self.Surface == Surface.Air)
            return new PlayerIntent();
        if (self.Parent != PlayerState.World)
        {
            if (self.Parent < 0 || self.Parent >= train.Frames.Count)
                return new PlayerIntent();
            int side = train.Frames[self.Parent].ToLocal(target).X >= 0 ? 1 : -1;
            if (self.Parent > 0 && self.Surface == Surface.Deck)
                return OffTheCar(self, train, side) ?? new PlayerIntent();
            // On a ladder (the end ladder out of a gap, a side ladder): on up, and off the roof's edge from there.
            if (self.Surface == Surface.Ladder)
                return new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Use };
            return GetDown(self, train, side) ?? new PlayerIntent();
        }
        var here = PlayerMotor.WorldPosition(self, train);
        if ((Flat(target) - Flat(here)).Length <= within)
            return null;
        return OnFoot(self, train, train.Dynamics.Path, target, null).Step;
    }
}
