using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using HouseSite = DarkTerritory.Sim.Run.StopWalls.OpenHouse;

namespace DarkTerritory.Game;

/// <summary>
/// G1's house creatures staged in a generated night's open house (notes 584–586; GreyboxScene.Houses): the Hanger in the
/// roof space over its strands, and on someone (<c>hanger</c>, <c>hangergrab</c>); a Hollow House sinking and come down
/// (<c>hollowhouse</c>, <c>hollowcollapse</c>); the Householder's house kept, its table set (<c>householderhouse</c>).
/// </summary>
public static partial class Staging
{
    public const int HangerId = 5860, HollowHouseId = 5850, HouseholderHouseId = 5840;

    /// <summary>The views that stage a house creature.</summary>
    public static bool IsHouseView(string view) => view is "hanger" or "hangergrab" or "hollowhouse" or "hollowcollapse" or "householderhouse";

    /// <summary>
    /// The house the views stage in: the night's first plain open house (one room, no back room, one door), at least 5 m by
    /// 7, so its room reads from its door.
    /// </summary>
    public static HouseSite StagedHouse(Sim.Run.StopWalls walls, int skip = 0) =>
        walls.OpenHouses.Where(h => h.Feature >= 0 && h.B.Parts.Count <= 1 && !Sim.Run.StopWalls.Composite(h.B) && h.B.Width >= 5 && h.B.Length >= 7
            && h.Doors.Count == 1).Skip(skip).First();

    /// <summary>The house's boards' height in the world, as the art stands them (WorldArt.Building: its frame 0.15 m under the ground, the boards 0.17 over).</summary>
    public static double FloorOf(RailLine line, Route route, HouseSite house)
    {
        var f = route.Features[house.Feature];
        var b = house.B;
        return Sim.Run.Run.StopWorld(line, f, b.Centre, Art.WorldArt.Ground(route, f.Start + b.S, (float)b.D, (float)GreyboxScene.ValleyDepth) - 0.15 + 0.17).Y;
    }

    /// <summary>
    /// The house creature a house view stages (<paramref name="mode"/>, if given, its mode by name: a HangerMode, a HollowMode
    /// or a HouseholderMode), and the camera on it; the crewmate it has, if it has one. It replaces any of their kinds staged.
    /// </summary>
    public static (List<Enemy> Threats, Camera Camera, Crewmate? Caught, (Double3 At, double Yaw)? Seat) House(List<Enemy> threats, string view, string mode, Route route, RailLine line,
        Sim.Run.StopWalls walls, HangerTuning hanger, int skip = 0, bool inside = false)
    {
        threats.RemoveAll(e => e is Hanger or HollowHouse or DarkTerritory.Sim.Enemies.Householder);
        var house = StagedHouse(walls, skip);
        double floor = FloorOf(line, route, house);
        var b = house.B;
        Double3 At(double x, double y, double up) => house.World(x, y) with { Y = floor + up };
        // The home's middle (a pair's cottage's own, an L's biggest part's), where the sim keeps a Hollow House and a Hanger.
        Double3 Mid(double up) => house.Middle with { Y = floor + up };
        // The door: its middle on the outside edge, and the way in (in the house's plan).
        var door = walls.HouseDoors.First(d => d.Key == house.Doors[0]);
        var inward = door.Out * -1;
        var d0 = door.At - house.Origin;
        double dx = d0.X * house.Ex.X + d0.Z * house.Ex.Z, dy = d0.X * house.Ey.X + d0.Z * house.Ey.Z;
        double ix = inward.X * house.Ex.X + inward.Z * house.Ex.Z, iy = inward.X * house.Ey.X + inward.Z * house.Ey.Z;
        Double3 In(double along, double across, double up) => At(dx + ix * along - iy * across, dy + iy * along + ix * across, up);
        Crewmate? caught = null;
        (Double3, double)? chair = null;
        Camera camera;
        switch (view)
        {
            case "hanger" or "hangergrab":
                {
                    var hm = Enum.Parse<HangerMode>(mode.Length > 0 ? mode : view == "hanger" ? "hidden" : "haul", true);
                    var strands = Hanger.Strands(house, HangerId, hanger.Strands);
                    // On someone: the strand furthest in from the door, its victim hauled up it off the boards (haulTo).
                    var (sx, sy) = strands.MaxBy(s => (s.X - dx) * ix + (s.Y - dy) * iy);
                    bool on = hm is HangerMode.Grab or HangerMode.Haul or HangerMode.Hold;
                    // Its body up under the roof over the home's middle, as Hanger.In puts it.
                    var body = Mid(Hanger.Ceiling - 0.3);
                    var e = new Hanger(HangerId);
                    e.Restore(SpinePhase.Grab, 0.8, 1, Enemy.Loose, body, 0, 0, (double)hm, on ? LoneId : -1, house.Index);
                    threats.Add(e);
                    if (on)
                    {
                        var feet = At(sx, sy, hm == HangerMode.Grab ? 0 : hanger.HaulTo);
                        var toDoor = door.At - feet;
                        caught = new Crewmate(LoneId, feet, Math.Atan2(-toDoor.X, -toDoor.Z), true, Act: Art.CrewPose.Held);
                        camera = inside
                            // (inside: from under it, up the strand at it.)
                            ? Camera.LookAt(feet + (door.At - feet).Normalized * 2.4 + Double3.Up * 0.9, body + Double3.Up * 0.1, 75)
                            : Camera.LookAt(In(2.3, 0, 1.6), At(sx, sy, 1.75), 70);
                    }
                    else
                        camera = Camera.LookAt(In(1.8, 0, 1.6), Mid(inside ? 2.9 : 2.1), 80);
                    break;
                }
            case "hollowhouse" or "hollowcollapse":
                {
                    var hm = Enum.Parse<HollowMode>(mode.Length > 0 ? mode : view == "hollowhouse" ? "sink" : "settled", true);
                    double sunk = hm switch { HollowMode.Sink => 0.9, HollowMode.Collapse or HollowMode.Settled => 1.3, _ => 0 };
                    var e = new HollowHouse(HollowHouseId);
                    // (Its doors just slammed, or just come down; else a while at it.)
                    double at = hm switch { HollowMode.Shut => 0.3, HollowMode.Collapse => 0.5, _ => 2.5 };
                    e.Restore(SpinePhase.Grab, at, 1, Enemy.Loose, Mid(0), 0, 0, (double)hm, sunk, house.Index);
                    threats.Add(e);
                    // From out in front of its door, back and off to one side, a crewmate's eye; crewmate 4 got out, backing off from
                    // it with their lamp up, its light on the earth heaved round it.
                    camera = Camera.LookAt(In(-7.5, 4.0, 1.7), In(2.0, -0.5, 0.7), 66);
                    var stood = In(-3.0, -2.6, 0);
                    var toHouse = Mid(0) - stood;
                    caught = new Crewmate(LoneId, stood, Math.Atan2(-toHouse.X, -toHouse.Z), true, Lamp: true);
                    // (inside: from just in at its door, across the room, as whoever's still in it sees it go.)
                    if (inside)
                    {
                        double deep = Math.Abs(ix) > 0.5 ? b.Length : b.Width;
                        camera = Camera.LookAt(In(1.6, 0.4, 1.6), In(deep - 0.5, -0.8, 1.4), 80);
                        caught = null;
                    }
                    break;
                }
            default:
                {
                    // ("away": up and gone after someone, out of the door; its chair and its table left as they were.)
                    bool away = mode == "away";
                    var hm = away ? HouseholderMode.Hunt : Enum.Parse<HouseholderMode>(mode.Length > 0 ? mode : "sit", true);
                    // Its chair: across the room from the door, a little to one side, the table between it and the door.
                    double deep = Math.Abs(ix) > 0.5 ? b.Length : b.Width;
                    var seat = In(deep - 2.0, 0.6, 0);
                    var toDoor = (door.At - seat) with { Y = 0 };
                    var e = new DarkTerritory.Sim.Enemies.Householder(HouseholderHouseId);
                    double yaw = Math.Atan2(-toDoor.X, -toDoor.Z);
                    e.Restore(SpinePhase.Dormant, 3, 1, Enemy.Loose, away ? door.At + door.Out * 12 : seat, 0, yaw, (double)hm, -1, house.Index);
                    chair = (seat, yaw);
                    threats.Add(e);
                    camera = inside
                        // (inside: close at its place, over the table.)
                        ? Camera.LookAt(In(deep - 3.6, -0.7, 1.45), In(deep - 2.7, 0.55, 0.78), 60)
                        : Camera.LookAt(In(1.5, -0.3, 1.65), In(deep - 2.4, 0.5, 0.75), 72);
                    break;
                }
        }
        return (threats, camera, caught, chair);
    }
}
