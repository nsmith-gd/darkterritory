using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Bots;

public sealed partial class StopHand
{
    /// <summary>
    /// Note 367: down off a standing train to a point on the ground, and on foot to within <paramref name="within"/> of it
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
                return OffTheCar(self, train, side, shutBehind: true) ?? new PlayerIntent();
            // On a ladder (the end ladder out of a gap, a side ladder): on up, and off the roof's edge from there.
            if (self.Surface == Surface.Ladder)
                return new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Use };
            return GetDown(self, train, side) ?? new PlayerIntent();
        }
        var here = PlayerMotor.WorldPosition(self, train);
        if ((Flat(target) - Flat(here)).Length <= within)
            return null;
        // Note 619: off a car's side-door steps onto the ballast, still over their treads: straight out from the car first. Back
        // along it to a Knotter's gap, local:435's walker (slot seed 7) went up its last tread onto the steps, was sent down them
        // again, and went up again, four ticks round, for 800 s at the stand, the door held open and the lamp lit; Fire Flies set
        // the car alight sixteen times.
        if (OnTheTreads(train, here) is { } car && OnTheTreads(train, target) is null)
        {
            var frame = train.Frames[car];
            var local = frame.ToLocal(here);
            var outside = local with { X = Math.Sign(local.X) * (frame.Shape.HalfWidth + StepsClear) };
            return OnFoot(self, train, train.Dynamics.Path, frame.ToWorld(outside), null).Step;
        }
        return OnFoot(self, train, train.Dynamics.Path, target, null).Step;
    }

    /// <summary>m out past a car's side-door steps a hand stands clear of them on the ballast (its body's width and a little).</summary>
    const double StepsClear = 1.2;

    /// <summary>The car whose side-door steps (their landing or a tread, a little round them) a point on the ground is over, if any.</summary>
    static int? OnTheTreads(TrainOnLine train, Double3 at)
    {
        for (int c = 1; c < train.Frames.Count; c++)
        {
            var local = train.Frames[c].ToLocal(at);
            foreach (var s in train.Frames[c].Shape.Solids)
                if (s.Part == PartKind.Steps && local.X >= s.Box.Min.X - 0.3 && local.X <= s.Box.Max.X + 0.3
                    && local.Z >= s.Box.Min.Z - 0.3 && local.Z <= s.Box.Max.Z + 0.3 && local.Y <= s.Box.Max.Y + 0.5)
                    return c;
        }
        return null;
    }
}
