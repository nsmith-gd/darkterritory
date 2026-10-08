using Ballast;
using Ballast.Audio;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The mix around the listener: the space they're in (spec A.6, <c>content/audio/spaces.json</c>: the cab, a car, a
/// tunnel, a facility's yard, or outside), which gives everything they hear its reverb, shuts out what it shuts out
/// (in a tunnel, the world outside) and carries voice its own way; and the night's music on its own bottom tier.
/// </summary>
public sealed partial class GameAudio
{
    /// <summary>The checklist's music cue (ui-music, tier 7): a low drone under the whole night.</summary>
    public const string MusicCue = "ui-music.drone";
    /// <summary>How far off the line's centre a listener can be and still be in a tunnel's bore (StructureKit.TunnelHalf is 3.1 m).</summary>
    const double TunnelReach = 6;
    /// <summary>How far from the line a listener still hears a facility's yard around them.</summary>
    const double FacilityReach = 80;

    HotData<SpacesDef>? _spaces;
    SoundInstance? _music;

    void EndNightMix() => _music = null;
    double _spaceHint = double.NaN;

    // The stop's building the listener's in this tick (its walls are between them and the outside: HearWalls).
    EarRoom? _earRoom;
    // The walk-in buildings near the listener (note 428: a sound in one is behind its walls), how often they're looked up
    // again and how far out (a stop's buildings don't move; the listener does, at a run at most), and the listener's place
    // along the line for it.
    readonly List<EarRoom> _roomsNear = [];
    double _roomsLookAt = double.NegativeInfinity, _roomsHint = double.NaN;
    const double RoomsLook = 0.25, RoomsReach = 120;

    /// <summary>The space the listener was in this tick, as spaces.json names it.</summary>
    public string Space { get; private set; } = "outside";

    /// <summary>Staging (the benches): hear everything as if in this space; null works it out from where the listener is.</summary>
    public string? SpaceOverride { get; set; }

    /// <summary>The work's drone (ui-music, tier 7) while it plays.</summary>
    public SoundInstance? Drone => _music;

    /// <summary>
    /// At startup: every space's response synthesised now rather than as the listener first walks into it, and the music's
    /// takes decoding on a worker (a 96 s loop is 1.7 s of decoding), so the night's first frame doesn't wait for either.
    /// </summary>
    void PrepareMix(string contentRoot)
    {
        _spaces = new HotData<SpacesDef>(Path.Combine(contentRoot, SpacesDef.File));
        PrepareSpaces();
        if (HasCue(MusicCue))
            Bank.Prefetch(MusicCue);
    }

    void PrepareSpaces()
    {
        foreach (var space in _spaces!.Value.Spaces.Values)
            if (space.Reverb is { } reverb)
                Mixer.Prepare(reverb);
    }

    /// <summary>Each tick: the listener's space into the mixer, and the drone on while the work's under way.</summary>
    void MixAround(World world, Listener listener)
    {
        if (_spaces!.Refresh())
            PrepareSpaces();
        Space = SpaceOf(world, listener.Position, ref _spaceHint, out _earRoom);
        if (SpaceOverride is { } staged)
            (Space, _earRoom) = (staged, null);
        if (_time - _roomsLookAt >= RoomsLook || _time < _roomsLookAt)
        {
            _roomsLookAt = _time;
            _roomsNear.Clear();
            if (SpaceOverride is null && (world.Route ?? world.Run?.Route) is { } route)
            {
                if (double.IsNaN(_roomsHint))
                    _roomsHint = world.Train.Dynamics.Distance;
                world.Train.Line.Nearest(listener.Position, ref _roomsHint);
                RoomsNear(route, world.Train.Line, listener.Position, _roomsHint, RoomsReach, _roomsNear);
            }
        }
        Mixer.Space = _spaces.Value.Spaces.GetValueOrDefault(Space);

        // While the work's under way: from the moment a run leaves the yard until the night's over or the train's off the
        // rails (then it's the opera's, GDD v1.4 App. E.1, or E.9's silence), flat (install.py writes ui- sounds flat), on
        // tier 7 under everything. Not installed, nothing plays.
        if (world.Run is { Phase: not RunPhase.Yard, Over: false } && !world.Derailed && HasCue(MusicCue))
        {
            if (_music is null || _music.Finished)
                _music = Mixer.Play(MusicCue);
        }
        else if (_music is not null)
        {
            _music.Stop();
            _music = null;
        }
    }

    /// <summary>
    /// The space inside the stop's building <paramref name="ear"/> is in, if it can be walked into (queue #129, note 392;
    /// C1's note 387 drew them walk-in): "shed" for a yard's sheds and its hero (iron halls, their bays open), "room" for
    /// the rest (a Holdout's signal box, lamp room, pump house, prison van or lockup; an open house). Null outside them,
    /// and on one's roof.
    /// </summary>
    public static string? RoomOf(Route route, RailLine line, Double3 ear, double along) => RoomAt(route, line, ear, along)?.Space;

    /// <summary>The walk-in building <paramref name="ear"/> is in, as <see cref="RoomOf"/> finds it, with where it stands.</summary>
    public static EarRoom? RoomAt(Route route, RailLine line, Double3 ear, double along)
    {
        var main = line.Sample(Math.Clamp(along, 0, line.Length));
        double lateral = Double3.Dot(ear - main.Position, Double3.Cross(main.Tangent, Double3.Up).Normalized);
        if (Art.WorldArt.BuildingAt(route, along, lateral) is not { Open: true } b || ear.Y - main.Position.Y > RoomHeight)
            return null;
        return Room(line, b.Feature, b.Stop.Buildings[b.Index]);
    }

    /// <summary>A stop's walk-in building where it stands, and whether it's a shed's hall or a small room.</summary>
    static EarRoom Room(RailLine line, RouteFeature stop, StopBuilding building)
    {
        bool shed = building.Kind is BuildingKind.Shed or BuildingKind.Hero or BuildingKind.GoodsShed or BuildingKind.Barn
            or BuildingKind.Powerhouse;
        var at = line.Sample(Math.Clamp(stop.Start + building.S, 0, line.Length));
        var across = Double3.Cross(at.Tangent, Double3.Up).Normalized;
        return new EarRoom(building, at.Position + across * building.D, at.Tangent, across, shed);
    }

    /// <summary>
    /// The stops' walk-in buildings near <paramref name="ear"/> (note 428): the ones a sound might be in that the ear hears
    /// through their walls. Every building of the stops within reach of the ear's place along the line, the way
    /// <see cref="Art.WorldArt.BuildingAt"/> tells one you can walk into, and only those whose middle is within
    /// <paramref name="reach"/> of the ear. Found now and then, not a line search per voice.
    /// </summary>
    public static void RoomsNear(Route route, RailLine line, Double3 ear, double along, double reach, List<EarRoom> into)
    {
        into.Clear();
        foreach (var f in route.Features)
        {
            if (f.Stop is not { } stop || along < f.Start - 250 || along > f.End + 250)
                continue;
            for (int i = 0; i < stop.Buildings.Count; i++)
            {
                var b = stop.Buildings[i];
                if (!(Sim.Run.StopWalls.Shelled(stop, i) || b.Open && Sim.Run.StopWalls.Walled(stop, i)))
                    continue;
                var room = Room(line, f, b);
                if ((room.Middle - ear).Length <= reach + Math.Max(b.Length, b.Width))
                    into.Add(room);
            }
        }
    }

    /// <summary>
    /// A stop's building the ear is in (note 396): which, its middle and the line's way there (along it, and across it as
    /// the stop's D runs), and whether it's a shed's hall or a small room.
    /// </summary>
    public readonly record struct EarRoom(StopBuilding Building, Double3 Middle, Double3 Along, Double3 Across, bool Shed)
    {
        public string Space => Shed ? "shed" : "room";

        /// <summary>Whether a point is in its footprint (the line taken as straight over a building's length).</summary>
        public bool Holds(Double3 p)
        {
            var d = p - Middle;
            return Art.WorldArt.Inside(Building, new Pt(Building.S + Double3.Dot(d, Along), Building.D + Double3.Dot(d, Across)));
        }

        /// <summary>
        /// Whether a sound at <paramref name="p"/> is shut in it from an ear outside at <paramref name="ear"/> (note 428): in its
        /// footprint and under its roof, and still in it <paramref name="edge"/> nearer the ear. One that close to a face is
        /// at its door or a window and heard out of it: a door shut in a house's doorway, a crewmate at a window.
        /// </summary>
        public bool ShutsIn(Double3 p, Double3 ear, double edge)
        {
            double up = p.Y - Middle.Y;
            if (up < -RoomBelow || up > RoomHeight)
                return false;
            var flat = new Double3(ear.X - p.X, 0, ear.Z - p.Z);
            double far = flat.Length;
            return far > edge && Holds(p) && Holds(p + flat * (edge / far));
        }
    }

    /// <summary>How far over the rail a listener can be and still be in a building (its roof's at most this).</summary>
    const double RoomHeight = 8;
    /// <summary>How far under the rail a sound can be and still be in a building (down its cellar's hatch).</summary>
    const double RoomBelow = 3;

    /// <summary>
    /// Which space a listener at <paramref name="ear"/> is in. Inside a car's walls is "car" (doors open or shut: it's
    /// still a wooden box around you), even in a tunnel. In a tunnel's bore (the route's, on the main line) is "tunnel":
    /// the cab is open-backed, so the bore swallows it too. Then the engine's cab, "cab"; along a facility's yard,
    /// "facility"; anywhere else, "outside". <paramref name="hint"/> carries the listener's place along the line from
    /// tick to tick (NaN to start from the train).
    /// </summary>
    public static string SpaceOf(World world, Double3 ear, ref double hint) => SpaceOf(world, ear, ref hint, out _);

    /// <summary>The same, and the stop's building the ear's in when it's in one (its "shed" or "room").</summary>
    public static string SpaceOf(World world, Double3 ear, ref double hint, out EarRoom? room)
    {
        room = null;
        var train = world.Train;
        bool cab = false;
        foreach (var frame in train.Frames)
        {
            if ((frame.Origin - ear).Length > frame.Shape.HalfLength + 5)
                continue;
            var local = frame.ToLocal(ear);
            if (frame.Shape.Interior is { } interior && interior.Contains(local))
                return "car";
            cab |= frame.Shape.Cab is { } c && c.Contains(local);
        }
        if ((world.Route ?? world.Run?.Route) is { } route)
        {
            if (double.IsNaN(hint))
                hint = train.Dynamics.Distance;
            train.Line.Nearest(ear, ref hint);
            double along = hint;
            var on = train.Line.Sample(along).Position;
            double off = Math.Sqrt((on.X - ear.X) * (on.X - ear.X) + (on.Z - ear.Z) * (on.Z - ear.Z));
            if (off <= TunnelReach && route.InTunnel(along))
                return "tunnel";
            // Down the mine spur (note 250): the adit closing in, the outside gone.
            if (world.Run is { } run && run.Underground(new PlayerState { Parent = PlayerState.World, Position = ear, LineHint = hint }, train))
                return "mine";
            // In a stop's building you can walk into (queue #129, note 392): a shed's iron hall, or a small room.
            if (!cab && RoomAt(route, train.Line, ear, along) is { } inside)
            {
                room = inside;
                return inside.Space;
            }
            if (!cab && off <= FacilityReach && route.Features.Any(f => f.Kind == FeatureKind.Facility && f.Contains(along)))
                return "facility";
        }
        return cab ? "cab" : "outside";
    }
}
