using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

public static partial class Staging
{
    /// <summary>The views of note 722's emotes (dt screenshot --view highfive | danceoff | sitedge | leanout).</summary>
    public static readonly string[] EmoteViews = ["highfive", "danceoff", "sitedge", "leanout"];

    /// <summary>
    /// The emotes for two, and the edges (note 722; dt screenshot --view highfive | danceoff | sitedge | leanout), each seen
    /// from about 6 m off the train's side, where it has to read. <c>highfive</c>: on car 2's roof a high five as the hands meet,
    /// a fist bump the same, and a high five offered to nobody, hanging there. <c>danceoff</c>: three dancing in a ring on car
    /// 2's roof, one leading, two clapping it. <c>sitedge</c>: one sat on car 2's roof edge, one in the side door car's open door
    /// sitting on its sill, legs over. <c>leanout</c>: one leant out of that door, looking up the line, and one out of the cab's
    /// doorway. Placed by the sim's own edge rules (<see cref="EmoteRules"/>) and drawn as a crewmate at the emote is
    /// (<see cref="CrewActs.WithEmote"/>).
    /// </summary>
    public static List<Crewmate> Emotes(TrainOnLine train, string content, string view)
    {
        var player = DataFile.Load<PlayerTuning>(Path.Combine(content, PlayerTuning.File));
        var t = player.Emotes;
        var roofCar = train.Frames[Math.Min(2, train.Frames.Count - 1)];
        double roof = roofCar.Shape.RoofHeight;
        // Stood on car 2's roof at (x, z), facing yaw (the car's frame), at an emote so far in.
        Crewmate OnRoof(byte id, double x, double z, double yaw, Emote emote, double seconds, EmoteDetail with = default)
        {
            var local = new Double3(x, roof, z);
            return new Crewmate(id, roofCar.ToWorld(local), roofCar.Heading + yaw, true, Car: roofCar.Index, Local: local, Emote: emote,
                EmoteSeconds: seconds, EmoteWith: with);
        }
        switch (view)
        {
            case "highfive":
                return
                [
                    // Met, a hair after the slap (their right hands, the middle of the 1.1 m between them).
                    OnRoof(40, 0.15, -0.55, Math.PI, Emote.HighFive, 1.1, new EmoteDetail(Met: true, SinceMeet: 0.08, Gap: 1.1)),
                    OnRoof(41, -0.15, 0.55, 0, Emote.HighFive, 0.9, new EmoteDetail(Met: true, SinceMeet: 0.08, Gap: 1.1)),
                    // A fist bump ahead of them, the same.
                    OnRoof(42, 0.15, -4.4, Math.PI, Emote.FistBump, 1.0, new EmoteDetail(Met: true, SinceMeet: 0.08, Gap: 1.0)),
                    OnRoof(43, -0.15, -3.4, 0, Emote.FistBump, 0.8, new EmoteDetail(Met: true, SinceMeet: 0.08, Gap: 1.0)),
                    // Behind them, one offered to nobody, still up, sagging.
                    OnRoof(44, 0.2, 4.2, -Math.PI / 2, Emote.HighFive, 2.6),
                ];
            case "danceoff":
                {
                    // Three in a ring 1.8 m across, each facing its middle; the first leads.
                    var ring = new List<Crewmate>();
                    for (int i = 0; i < 3; i++)
                    {
                        double a = i * Math.Tau / 3 + 0.4;
                        double x = 0.55 * Math.Sin(a), z = 0.9 * Math.Cos(a);
                        ring.Add(OnRoof((byte)(45 + i), x, z, Math.Atan2(x, z), Emote.Dance, 1.3 + 0.37 * i, new EmoteDetail(Dancers: 3, Leading: i == 0)));
                    }
                    return ring;
                }
            case "sitedge":
                {
                    var crew = new List<Crewmate>();
                    // On the side door car's roof, along from its door.
                    var top = train.Frames[SideDoorCar(train)];
                    var onTop = new Double3(top.Shape.HalfWidth - 0.3, top.Shape.RoofHeight, 3.4);
                    var roofSitter = new PlayerState { Parent = top.Index, Position = onTop, Surface = Surface.Roof, Health = player.Health };
                    var edge = EmoteRules.SitAt(roofSitter, train, t) ?? new Edge(EdgeKind.Roof, 1);
                    crew.Add(CrewActs.SatOn(new Crewmate(48, top.ToWorld(onTop), top.Heading, true, Car: top.Index, Local: onTop, Emote: Emote.SitEdge, EmoteSeconds: 1.3),
                        roofSitter, edge, train, train.Frames, t));
                    if (Doorway(train, out int car, out var sill))
                    {
                        var s = new PlayerState { Parent = car, Position = sill, Surface = Surface.Deck, Health = player.Health };
                        var at = train.Frames[car];
                        var c = new Crewmate(49, at.ToWorld(sill), at.Heading - Math.PI / 2, true, Car: car, Local: sill, Emote: Emote.SitEdge, EmoteSeconds: 2.1);
                        crew.Add(CrewActs.SatOn(c, s, EmoteRules.SitAt(s, train, t) ?? new Edge(EdgeKind.Sill, 1), train, train.Frames, t));
                    }
                    return crew;
                }
            case "leanout":
                {
                    var crew = new List<Crewmate>();
                    if (Doorway(train, out int car, out var sill))
                    {
                        // Looking up the line, toward the engine (the car's −Z), turned from facing out the door.
                        var s = new PlayerState { Parent = car, Position = sill, Surface = Surface.Deck, Health = player.Health, Yaw = -0.3 };
                        var at = train.Frames[car];
                        var c = new Crewmate(50, at.ToWorld(sill), at.Heading + s.Yaw, true, Car: car, Local: sill, Emote: Emote.LeanOut, EmoteSeconds: 1.4);
                        crew.Add(CrewActs.LeantOut(c, s, EmoteRules.LeanAt(s, train, t) ?? new Edge(EdgeKind.Doorway, 1), train, train.Frames));
                    }
                    // And out of the cab's doorway, by its steps on the +X side.
                    var engine = train.Frames[0];
                    if (engine.Shape.Cab is { } cab && engine.Shape.Ladders.Count > 0)
                    {
                        var steps = engine.Shape.Ladders[0];
                        var local = new Double3(cab.Max.X - 0.35, cab.Min.Y + 0.1, steps.Foot.Z);
                        var s = new PlayerState { Parent = 0, Position = local, Surface = Surface.Deck, Health = player.Health, Yaw = -1.2 };
                        var c = new Crewmate(51, engine.ToWorld(local), engine.Heading + s.Yaw, true, Car: 0, Local: local, Emote: Emote.LeanOut, EmoteSeconds: 0.7);
                        crew.Add(CrewActs.LeantOut(c, s, EmoteRules.LeanAt(s, train, t) ?? new Edge(EdgeKind.Doorway, 1), train, train.Frames));
                    }
                    return crew;
                }
        }
        return [];
    }

    /// <summary>
    /// The side door car's +X door opened (note 722's views, as note 723's hand-up opens it), and a spot on its floor just
    /// inside it, in the car's frame. False without a car with side doors.
    /// </summary>
    static bool Doorway(TrainOnLine train, out int car, out Double3 sill)
    {
        car = SideDoorCar(train);
        sill = default;
        var shape = train.Frames[car].Shape;
        if (shape.Interior is not { } room || !shape.DoorList.Any(d => d.Index == 3))
            return false;
        var door = shape.DoorList.First(d => d.Index == 3).Box;
        train.Vehicles[car].DoorsOpen |= 1 << 3;
        sill = new Double3(room.Max.X - 0.3, room.Min.Y + 0.1, (door.Min.Z + door.Max.Z) / 2);
        return true;
    }

    /// <summary>
    /// The cameras of note 722's views: off the train's +X side on the ballast, about 6 m out (the distance an emote has to read
    /// from), at car 2's roof crew (highfive, danceoff), at its roof edge and the side door car's sill (sitedge), at that door and,
    /// further, the cab's (leanout).
    /// </summary>
    public static Camera EmoteCamera(TrainOnLine train, string view)
    {
        var roofCar = train.Frames[Math.Min(2, train.Frames.Count - 1)];
        double roof = roofCar.Shape.RoofHeight, w = roofCar.Shape.HalfWidth;
        var door = train.Frames[SideDoorCar(train)];
        return view switch
        {
            "highfive" => Camera.LookAt(roofCar.ToWorld(new Double3(w + 5.6, roof + 0.4, 0.2)), roofCar.ToWorld(new Double3(0, roof + 1.25, 0)), 55),
            "danceoff" => Camera.LookAt(roofCar.ToWorld(new Double3(w + 5.2, roof + 1.0, 0)), roofCar.ToWorld(new Double3(0, roof + 1.0, 0)), 45),
            // (A little ahead along the car, so the sill sitter's legs show over the landing's face, not lost against it.)
            "sitedge" => Camera.LookAt(door.ToWorld(new Double3(door.Shape.HalfWidth + 4.6, 2.3, -3.4)), door.ToWorld(new Double3(door.Shape.HalfWidth + 0.3, 2.3, 1.4)), 55),
            // (From ahead along the car, the way the leaner looks: their lean out past the car's side shows against the sky.)
            _ => Camera.LookAt(door.ToWorld(new Double3(door.Shape.HalfWidth + 3.2, 2.0, -5.4)), door.ToWorld(new Double3(door.Shape.HalfWidth + 0.2, 2.1, 0)), 55),
        };
    }
}
