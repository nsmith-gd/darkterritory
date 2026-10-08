using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>
/// Builds greybox geometry for the line and the train around a camera. Positions are emitted
/// relative to the camera (floating origin), so a train 40 km down the line renders as precisely
/// as one at the yard.
/// </summary>
public sealed partial class GreyboxScene
{
    public float DrawDistance { get; init; } = 400;
    public int Seed { get; init; } = 7;
    /// <summary>How hot the firebox is, 0..1: the glow in the cab is how the Boiler reads the fire.</summary>
    public float FireGlow { get; set; } = 0.7f;

    /// <summary>
    /// How the fire looks for the coal on it (T121 playtest: "firebox seems to go out even when there's fire in the UI ...
    /// Firebox should only go out once the number is at 0"): any coal at all is a fire you can see, a low one a bed of
    /// coals with short flames, building up through the furnace's flame heights to a roaring box at capacity. Square-root,
    /// so the first coal on a dying fire shows at once. Out, dark, only with the HUD's FIRE at 0.0.
    /// </summary>
    public static float FireLook(double firebox, double capacity) =>
        firebox < 0.05 ? 0 : (float)(0.3 + 0.7 * Math.Sqrt(Math.Clamp(firebox / Math.Max(1e-6, capacity), 0, 1)));

    /// <summary>The couplers that have been cut (T91, Art.SceneArt.Cuts): drawn with the knuckle open.</summary>
    public IReadOnlySet<int>? Cut { get; set; }

    int CutEnds(int vehicle) => Cut is not { } cut ? 0 : (cut.Contains(vehicle * 2) ? 1 : 0) | (cut.Contains(vehicle * 2 + 1) ? 2 : 0);

    /// <summary>A crewmate's on the whistle cord (GDD §12, Art.CrewActs.CrewWhistling): it's drawn hauled down.</summary>
    public bool CordPulled { get; set; }
    /// <summary>T109: the wrench is on its rack in the cab (the boiler's WrenchOut, the other way about).</summary>
    public bool WrenchRacked { get; set; } = true;
    /// <summary>Note 275: the fireman's shovel is home by the rack (the boiler's ShovelOut, the other way about).</summary>
    public bool ShovelRacked { get; set; } = true;
    /// <summary>The train off the rails (T117): its effects (sparks, dust, the engine's steam) are drawn from it.</summary>
    public Sim.Train.Wreck? Wreck { get; set; }

    /// <summary>
    /// Cars not drawn this frame: the derailment film's cutaway (GDD v1.4 App. E.4 O2, "PS2 style"), so a crewmate tumbling
    /// inside a car, or behind one, is seen through it.
    /// </summary>
    public IReadOnlySet<int>? CutAway { get; set; }

    /// <summary>Extra lights in the world (the film's rig on its subject, E.4 O12): where, their colour, their reach.</summary>
    public IReadOnlyList<(Double3 At, Vector3 Colour, float Radius)>? Lights { get; set; }
    /// <summary>The firebox door's open (the boiler's FireDoorOpen): a Stoker in the fire is seen through it.</summary>
    public bool FireDoorOpen { get; set; }
    /// <summary>Seconds since the last shovelful (the boiler's <c>SinceShovel</c>): the firebox flares just after one (§31).</summary>
    public double SinceShovel { get; set; } = double.PositiveInfinity;
    /// <summary>Emergency lighting (`dt screenshot --emergency`): the cars' lamps go to a dim red, the headlamp dark.</summary>
    public bool Emergency { get; set; }
    static readonly System.Numerics.Vector3 EmergencyRed = new(0.5f, 0.06f, 0.04f);
    /// <summary>Tunnels, bridges, facilities and hazards to draw along the line, when it's a generated route.</summary>
    public Route? Route { get; set; }

    /// <summary>The stops' walls, for the village houses' doors (note 401): which are shut. The train's own when built from it.</summary>
    public Sim.Run.StopWalls? Walls { get; set; }
    /// <summary>The engine's lamp is lit (it's what makes the boards shine back, sight.json).</summary>
    public bool LampLit { get; set; } = true;
    /// <summary>GDD v1.4 App. E.9: this many of the cars' lamps are out, from the last car forward (all of them: the engine's too).</summary>
    public int LampsOut { get; set; }

    /// <summary>Car <paramref name="index"/>'s lamps are out (its vehicle's LampLit), so it's drawn dark inside and out.</summary>
    /// <summary>
    /// A hand lamp's light (GDD §31), lying or carried. With the art pass it's a flame: it flickers and shivers a little in
    /// its glass (Art.SceneArt.Flicker, FlameDrift), as its glow does; the greybox's is steady. The one nearest the eye
    /// casts shadows, which swing as it swings (MeshBuilder.ShadowLight); the rest light unshadowed.
    /// </summary>
    void HandLamp(MeshBuilder mesh, Double3 at, Double3 eye, int id)
    {
        if ((at - eye).Length >= 60)
            return;
        var (drift, flicker) = Look is null ? (Vector3.Zero, 1f) : (Art.SceneArt.FlameDrift(Time, id), Art.SceneArt.Flicker(Time, id));
        var light = new PointLight(V(at, eye) + drift, Palette.LampAmber * Interiors.HandLamp * flicker, Interiors.HandLampRange);
        if (mesh.ShadowLight is { } nearer && nearer.Position.LengthSquared() <= light.Position.LengthSquared())
            mesh.PointLights.Add(light);
        else
        {
            if (mesh.ShadowLight is { } farther)
                mesh.PointLights.Add(farther);
            mesh.ShadowLight = light;
        }
    }

    bool CarDark(int index) => Vehicles is { } fleet && index < fleet.Count && (!fleet[index].LampLit || Sputtered(fleet[index], index));

    /// <summary>
    /// A guttering lamp (note 346) out for this moment of its flicker: a few times a second, at random, more of the time the
    /// nearer it is to going out (from a twelfth to over half).
    /// </summary>
    bool Sputtered(Vehicle v, int index)
    {
        if (v.Gutter <= 0)
            return false;
        double gone = Math.Clamp(v.Gutter / (Gutter?.OutAfter ?? 45), 0, 1);
        uint h = (uint)((long)Math.Floor(Time * 14) * 2654435761L + index * 40503L);
        h ^= h >> 15;
        h *= 2246822519u;
        h ^= h >> 13;
        return h % 1000 < 80 + 450 * gone;
    }
    /// <summary>
    /// GDD v1.4 App. E.9, the Stranded outro: the repair kit's locker (note 173) stands open, whatever its door is doing, on
    /// the empty shelf where the kit should be.
    /// </summary>
    public bool KitLockerOpen { get; set; }
    /// <summary>How far from the eye the cars' lamps are lit (a high wide shot sees the whole train's).</summary>
    public double LampRange { get; set; } = 60;
    /// <summary>
    /// Each lit car's lamp spilling out of its roof hatches and doors, so a high wide shot sees the train as lights (E.9's
    /// "small glowing machine in an enormous black world, going dark").
    /// </summary>
    public bool RoofGlow { get; set; }
    /// <summary>How far ahead the lamp makes a board out (sight.json lampSignRange).</summary>
    public double SignRange { get; set; } = new SightTuning().LampSignRange;
    /// <summary>The line's boards (sight.json). Unset on a route, they're worked out from it with the default tuning.</summary>
    public IReadOnlyList<Sign>? Signs { get; set; }
    /// <summary>Live enemies to draw. When set, the route's Sleepers come from here rather than its features.</summary>
    public IReadOnlyList<Enemy>? Enemies { get; set; }
    /// <summary>Blows and balls that landed on creatures lately (World.Hits, T121): each one's flinch and flash, by <see cref="Tick"/>.</summary>
    public IReadOnlyList<Sim.Combat.HitConfirm>? Hits { get; set; }
    /// <summary>Crewmates' swings, landed or not (note 197): the world's, for their swing clip.</summary>
    public IReadOnlyList<Sim.Combat.SwingEvent>? Swings { get; set; }
    /// <summary>Everyone's outfit (note 298), for the Passenger wearing one of the crew's faces.</summary>
    public IReadOnlyDictionary<int, byte>? Outfits { get; set; }
    /// <summary>Where cannonballs came down lately (World.Impacts, T121): each one's explosion, by <see cref="Tick"/>.</summary>
    public IReadOnlyList<Sim.Combat.CannonImpact>? Impacts { get; set; }

    /// <summary>
    /// The firebox's light: the fire's orange, or with a Stoker in it (T53) "wrong-coloured firebox glow" (App. A.5), a sick
    /// green that burns brighter, not dimmer, as the pressure climbs.
    /// </summary>
    Vector3 FireColour(float scale) => Enemies?.Any(e => e.Kind == EnemyKind.Stoker && !e.Gone) == true
        ? Palette.SignalGreen * (0.5f + 0.8f * scale) : Palette.FurnaceOrange * scale;
    /// <summary>Tonight's run, for facility machinery (the coaling chute pouring).</summary>
    public Sim.Run.Run? Run { get; set; }
    /// <summary>The route's Holdouts (GDD App. D): each one's lamp burns while somebody waits in it.</summary>
    public Sim.Run.Holdouts? Holdouts { get; set; }
    /// <summary>The departure fortress's town (GDD §3.1; note 281): its square, and its people where the town's folk stand.</summary>
    public Sim.Towns.Town? Town { get; set; }
    /// <summary>Somebody in the town turned to face whoever's talking to them (<see cref="TownTalk"/>), by person id.</summary>
    public (int Person, Double3 Toward)? TownFacing { get; set; }
    /// <summary>Seconds, for animating things that move on their own.</summary>
    public double Time { get; set; }
    /// <summary>Vehicle state for doors (open or shut). Without it every door is drawn shut.</summary>
    public IReadOnlyList<Vehicle>? Vehicles { get; set; }
    /// <summary>The hot boxes' tuning (note 331), for where a hot one smokes and how near it is to catching; null, the file's defaults.</summary>
    public Sim.Train.HotBoxTuning? HotBoxTuning { get; set; }
    /// <summary>The lamps' guttering tuning (note 346), for how near a guttering lamp is to going out; null, the file's defaults.</summary>
    public Sim.Train.GutterTuning? Gutter { get; set; }
    static readonly Sim.Train.HotBoxTuning DefaultHotBox = new();
    /// <summary>Loose bodies: crates, lamps, the dead.</summary>
    public IReadOnlyList<Sim.Physics.Body>? Bodies { get; set; }
    /// <summary>
    /// This player's own hands, where the eye is drawn from this frame: what they carry is drawn there rather than where the
    /// last snapshot put it (T92 playtest: a carried crate trailed the view and stepped at the snapshot rate).
    /// </summary>
    public (int Player, Double3 Hands, double Yaw)? HeldHere { get; set; }
    /// <summary>You as your own eyes see you (X3): your forearms and hands, and the tool in them; null for none (a chase
    /// camera, a headset's own hands, the dead).</summary>
    public OwnView? Own { get; set; }
    /// <summary>Other players, drawn as greybox figures.</summary>
    public IReadOnlyList<Crewmate>? Crew { get; set; }

    /// <summary>Whether the livestock look round at the eye and the crew (note 455); off for a still of them not (dt screenshot --unseen).</summary>
    public bool Onlook { get; set; } = true;
    /// <summary>For a still frame (<c>dt screenshot</c>), how fast staged enemies are going (m/s, by id): one frame can't measure it (Pace).</summary>
    public IReadOnlyDictionary<int, float>? StagedPaces { get; set; }
    /// <summary>How each branch's switch is set (true: for the branch), for its stand's lamp. Unset, all read main.</summary>
    public Func<int, bool>? Diverging { get; set; }
    /// <summary>
    /// Whether a mail crane's bag has been caught (the host's <see cref="Sim.Route.Lineside.Caught"/>), by drop id: the
    /// scene times the snatch itself from the first frame it reads true. Unset, every bag hangs until the train's by.
    /// </summary>
    public Func<int, bool>? DropCaught { get; set; }
    /// <summary>
    /// Cars drawn as utility cars (GDD §10) beside the crew cars the train has (<see cref="VehicleKind.Utility"/>, note 184),
    /// by vehicle index: fitted out for the crew in place of a load. A still frame sets it (<c>--utility i</c>) to see the
    /// fit-out in a cargo car's shell.
    /// </summary>
    public Func<int, bool>? Utility { get; set; }
    /// <summary>The train has roof handrails (spec F.3, train.json <c>composition.handrails</c>; note 184).</summary>
    public bool Handrails { get; set; }
    /// <summary>The cab has the powered switch thrower's lever (spec F.3, train.json <c>composition.switchThrower</c>; note 196).</summary>
    public bool SwitchThrower { get; set; }
    /// <summary>How cold the night is (0..1): the route weather's, or a still frame's (<c>dt screenshot --cold c</c>).</summary>
    public double Cold => StagedCold ?? Route?.Weather.Cold ?? 0;
    public double? StagedCold { get; set; }
    /// <summary>For a still frame (<c>dt screenshot --mail s</c>): a bag caught is that many seconds into its snatch.</summary>
    public double StagedCatch { get; set; }
    /// <summary>The switch stands, for where their levers are. Unset, they stand where the default tuning puts them.</summary>
    public SwitchStands? Stands { get; set; }
    /// <summary>The cab's controls, for where the levers' handles are.</summary>
    public TrainControls Controls { get; set; } = new() { Reverser = 1 };
    /// <summary>The sim's tick now, for effects timed from the sim (a gun's muzzle flash); unset, none are shown.</summary>
    public long Tick { get; set; } = -1;
    /// <summary>The boiler's pressure as a fraction of its maximum, for the cab's gauge (the sim's; unset, a working pressure).</summary>
    public float Pressure { get; set; } = 0.78f;
    /// <summary>
    /// How long the Stoker's been waiting on the smokestack (World.StokerWaiting: the fire's burned low), as the caller has
    /// seen it, or negative for not: squatting on its rim, then climbing down into it over the last
    /// <see cref="StokerDescendSeconds"/> before <see cref="StokerDownAt"/> (App. A.5: "down the stack").
    /// </summary>
    public double StokerLowFor { get; set; } = -1;
    /// <summary>When it goes down the stack (enemies.json stoker.lowPressureSeconds: the sim puts it in the firebox then).</summary>
    public double StokerDownAt { get; set; } = 45;
    bool? _doorWas;
    double _doorMovedAt = -1;

    /// <summary>The Stoker's descend clip's length (tools/blender/stoker.py, 90 frames).</summary>
    const double StokerDescendSeconds = 3;
    /// <summary>The coal left in the tender, 0..1: the backhead's sight glass (labelled TENDER) reads it.</summary>
    public float Tender { get; set; } = 0.72f;
    /// <summary>The blow-off's open (the boiler's <c>Vented</c>), and the safety valve's lifting: their steam (T101).</summary>
    public bool Venting { get; set; }
    public bool SafetyValve { get; set; }
    /// <summary>
    /// The boiler's ruptured (spec B.6, GDD §23): its torn flank, the burst's steam timed from the frame the scene first saw
    /// it (presentation only, like <see cref="Derailed"/>), a dead stack; and with <see cref="DriversLocked"/>, the drivers
    /// seized, not turning, sliding on the rail in sparks.
    /// </summary>
    public bool Ruptured { get; set; }
    /// <summary>
    /// The breaks the crew can mend, each called out where it is (note 301: <see cref="RepairCallouts.Of"/>), and which of them
    /// (by index) someone's wrench is at now. Null, none.
    /// </summary>
    public IReadOnlyList<BreakCallout>? Breaks { get; set; }
    public IReadOnlySet<int>? Mending { get; set; }
    /// <summary>Each car's strain on a bend taken too fast and its outer rail (BendStrain.PerCar): flange sparks off it.</summary>
    public IReadOnlyList<(float Stress, int Outer)>? BendStrain { get; set; }
    /// <summary>The cylinders seized and the train still dragging down to coasting speed (boiler.json ruptureCoastBelow).</summary>
    public bool DriversLocked { get; set; }
    /// <summary>For a still: how long ago the rupture was (the burst's moment in it).</summary>
    public double StagedRuptureSeconds { get; set; }
    double? _rupturedAt;
    /// <summary>The art pass's surfaces (T39, look.json). Unset, the greybox is flat colour.</summary>
    public Look? Look { get; set; }
    /// <summary>
    /// vr.json's body (T82) for the box figure's headset crewmates when there's no art pass (with one, <see cref="Look"/>'s):
    /// unset, the mirror's own numbers (note 223).
    /// </summary>
    public VrBodyTuning? VrBody { get; set; }
    readonly VrStrides _bodies = new();

    /// <summary>
    /// When set, what each part of <see cref="Build"/> cost (milliseconds, and the triangles it added to the frame's soup),
    /// added up over the builds since it was set: `dt perf`'s breakdown of the main thread's frame.
    /// </summary>
    public Dictionary<string, (double Ms, int Triangles)>? Timings { get; set; }
    readonly System.Diagnostics.Stopwatch _lap = new();
    int _lapCount;

    void Lap(MeshBuilder mesh, string part)
    {
        if (Timings is null)
            return;
        Timings.TryGetValue(part, out var sum);
        Timings[part] = (sum.Ms + _lap.Elapsed.TotalMilliseconds, sum.Triangles + (mesh.Count - _lapCount) / 3);
        _lapCount = mesh.Count;
        _lap.Restart();
    }

    /// <summary>Depth of the valley under a bridge.</summary>
    const double ValleyDepth = 18;

    /// <param name="frames">The cars as drawn, when not the train's own (leaning on a bend: CarLean).</param>
    public void Build(MeshBuilder mesh, TrainOnLine train, Double3 eye, IReadOnlyList<CarFrame>? frames = null)
    {
        Vehicles ??= train.Vehicles;
        Walls = train.Walls ?? Walls;
        Cut = Art.SceneArt.Cuts(train);
        Handrails = train.Dynamics.Tuning.Composition.Handrails;
        SwitchThrower = train.Dynamics.Tuning.Composition.SwitchThrower;
        Build(mesh, train.Line, frames ?? train.Frames, train.Dynamics.Distance, eye);
    }

    /// <param name="frames">Car frames to draw, e.g. interpolated between ticks.</param>
    /// <param name="hint">Any distance along the line near the eye, to start the nearest-point search.</param>
    public void Build(MeshBuilder mesh, RailLine line, IReadOnlyList<CarFrame> frames, double hint, Double3 eye)
    {
        mesh.Clear();
        _speed = frames.Count == 0 ? 0 : Vector3.Dot(ToF(frames[0].Velocity), ToF(frames[0].Back * -1));
        // How far the engine's rolled, for its turning wheels (Art.SceneArt.Gear): its speed run on over the frames' time.
        // (Seized, they slide: they don't turn, and take up turning where they stopped once she's down to coasting.)
        if (_wheelClock is { } then && Time > then && Time - then < 1 && !DriversLocked)
            _travelled += _speed * (Time - then);
        _wheelClock = Time;
        // (And as the Choir comes, everyone's breath shows: the cold comes with it, App. A.7.)
        if (Look is not null)
            Look.Art.Breath = Math.Max(Look.Tuning.Atmosphere.Cold.Breath(Cold),
                ChoirGathering > ChoirFrostFrom ? (ChoirGathering - ChoirFrostFrom) / (1 - ChoirFrostFrom) : 0);
        Fires(frames.Count);
        mesh.Style = Look?.Style;
        mesh.Seed = 0;
        _line = line;
        _hint = hint;
        _lap.Restart();
        _lapCount = 0;
        // Bare triangles (the ground, the ballast, the trees) take world texels, wrapped every few km so they fit a
        // float; the pattern jumps at a wrap, rarely and far off in the fog.
        const double Wrap = 4096;
        static float W(double v) => (float)(v - Math.Floor(v / Wrap) * Wrap);
        mesh.SurfaceOrigin = new Vector3(W(eye.X), W(eye.Y), W(eye.Z));
        double centre = NearestDistance(line, eye, hint);
        double from = Math.Max(0, centre - DrawDistance), to = Math.Min(line.Length, centre + DrawDistance);

        Track(mesh, line, eye, from, to, centre);
        foreach (var branch in line.Branches)
            if (branch.Toe < to && branch.End > from)
                Branch(mesh, line, branch, eye);
        Lap(mesh, "track");
        Lineside(mesh, line, eye, from, to);
        Lap(mesh, "lineside");
        if (Route is not null)
        {
            Features(mesh, line, eye, from, to);
            Boards(mesh, line, eye, from, to, hint);
            // A generated line's own land, boards, hazards, water and places (Art/PlanArt, linegen plan §12-13).
            if (Look is not null && Route.Plan is not null)
                Look.Art.World.Plan(mesh, line, Route, eye, centre, DrawDistance, Time);
            if (Run is not null)
                foreach (var site in Run.Sites)
                {
                    // The modules modelled by the art pass where it has them (SceneArt.Depots), boxes where not.
                    if (site is not null && (site.Capstan - eye).Length < DrawDistance && Look?.Art.Winch(mesh, site, eye) != true)
                        Winch(mesh, site, eye);
                    // GDD §18's set pieces (note 185): the elevator's spout, the slaughterhouse's pen and ramp, the works' hose.
                    if (site is not null && (site.Has(Sim.Run.ModuleKind.Spout) || site.Has(Sim.Run.ModuleKind.Ramp) || site.Has(Sim.Run.ModuleKind.Hose)
                        || site.Has(Sim.Run.ModuleKind.Lift) || site.Has(Sim.Run.ModuleKind.Conveyor) || site.Has(Sim.Run.ModuleKind.Tipple))
                        && (site.Track.Sample(site.Mid).Position - eye).Length < DrawDistance + 120)
                        // The art pass's models where it has them (#135); the conveyor line its own (note 430); the tipple (note 423)
                        // is the greybox's either way.
                        SetPieces(mesh, site, frames, eye, Time, artDrawn: Look?.Art.SetPieces(mesh, site, frames, eye, Time) == true,
                            conveyorDrawn: site.Has(Sim.Run.ModuleKind.Conveyor) && Look?.Art.Conveyor(mesh, site, eye, Time) == true,
                            tipple: Run.FacilityTuning?.Tipple);
                    // The wreck yard's heaps (note 187): the last train's cars on their sides, groaning when they're going to go;
                    // drawn as the train's own cars, wrecked, where the art pass has them (note 394).
                    if (site is { Heaps.Count: > 0 } && (site.Heaps[0].Centre - eye).Length < DrawDistance + 120
                        && Look?.Art.Wreckage(mesh, site, frames, eye, Time, DrawDistance) != true)
                        Wreckage(mesh, site, eye, Time);
                    // Its own gantry and the yard's (level-design P18: one over each craned loading face).
                    foreach (var crane in site?.Cranes ?? [])
                        if ((crane.HookAt - eye).Length < DrawDistance && Look?.Art.Crane(mesh, crane, frames, eye) != true)
                            Crane(mesh, crane, frames, eye);
                }
            // A yard's powerhouse with its power on (level-design D.2): the lamp over its door burns.
            if (Run is not null)
                foreach (var site in Run.Sites)
                    // (Not at a wreck yard: GDD §18 "unlit", note 187.)
                    if (site is { Power: Sim.Stops.PowerState.Live, Powerhouse: { } door } && !site.Has(Sim.Run.ModuleKind.Wreck) && (door - eye).Length < DrawDistance)
                    {
                        var at = V(door + Double3.Up * 3.2, eye);
                        mesh.PointLights.Add(new PointLight(at, Palette.LampAmber * 1.2f, 10));
                        mesh.Billboard(at, 0.3f, 0, new Vector4(Palette.LampAmber * 1.2f, 1), -1, FxBlend.Additive);
                    }
            // The open houses' insides (note 326; the director, 8 Oct): the night kept out, and each one's dim light.
            if (Look is not null)
                HouseInteriors(mesh, line, Route, eye);
            // An open house's hiding spots once searched (note 326): opened up, so a crew sees what's been gone through.
            if (Run is not null)
                SearchedSpots(mesh, line, Run, eye);
            // The houses' doors (note 401): hanging open into the room, or shut in the doorway.
            if (Walls is { HouseDoors.Count: > 0 } doored)
                HouseDoors(mesh, line, Route, doored, eye);
            // Each Holdout's way in, shut or broken open (App. D.7): its door, lock or barricade by its state.
            if (Holdouts is not null && Look is not null)
                Look.Art.World.Entrances(mesh, line, Route, Holdouts, eye, (float)ValleyDepth, Time);
            // A Holdout's lamp (App. D.7): lit while it's occupied, seen from the approach board; a world light, not a car's.
            if (Holdouts is not null)
                foreach (var h in Holdouts.All)
                {
                    if (!h.Lit || h.Site.Stop is not { } stop)
                        continue;
                    var kind = stop.Buildings[h.Layout.Building].Kind;
                    var lamp = Sim.Run.Run.StopWorld(line, h.Site, h.Layout.Lamp, Art.WorldArt.LampHeight(kind));
                    if ((lamp - eye).Length > 1100)
                        continue;
                    var at = V(lamp, eye);
                    mesh.PointLights.Add(new PointLight(at, Palette.LampAmber * 1.6f, 14));
                    mesh.Billboard(at, 0.35f, 0, new Vector4(Palette.LampAmber * 1.4f, 1), -1, FxBlend.Additive);
                    mesh.Billboard(at, 2.4f, 0, new Vector4(Palette.LampAmber * 0.35f, 1), -1, FxBlend.Additive);
                    // Its light in the fog over it (App. D.7: "visible from the 1 km board through fog"): a broad warm smudge
                    // on the fog, up over the trees and the roofs round it, that the eye finds long before the lamp.
                    float far = (float)Math.Clamp(((lamp - eye).Length - 80) / 500, 0, 1);
                    mesh.Billboard(at + Vector3.UnitY * 42, 120, 0, new Vector4(Palette.LampAmber * (0.35f + 2.4f * far), 1), -1, FxBlend.Additive);
                    mesh.Billboard(at + Vector3.UnitY * 6, 22, 0, new Vector4(Palette.LampAmber * (0.3f + 1.2f * far), 1), -1, FxBlend.Additive);
                }
            // GDD §9: the fortress yard behind the gates, and the terminus: "lights, then walls, then gun towers".
            double yard = Run?.YardLength ?? 600, terminus = Run?.Tuning.TerminusZone ?? 400;
            // (Where the sim stands their solids, T124.)
            foreach (var fort in Sim.Run.Fortresses.Of(Route, line, yard, terminus))
                Fortress(mesh, line, eye, from, to, fort.Start, fort.End, gateAt: fort.Gate, lit: fort.Lived);
            Lap(mesh, "route");
        }
        // Practical lights first, so everything built after is lit by them: each car's lamps, the firebox,
        // and any hand lamp lying about or being carried.
        // (Carried, where it swings in the carrier's fist: Art.SceneArt.LampInHand.)
        if (Bodies is not null)
            foreach (var b in Bodies.Where(b => b.Kind == Sim.Physics.BodyKind.Lamp))
                if (((b.Carrier >= 0 ? Look?.Art.LampInHand(b.Carrier, Time) : null) ?? BodyWorld(b, frames, b.Centre)) is { } at)
                    HandLamp(mesh, at, eye, b.Carrier >= 0 ? b.Carrier : b.Id);
        // (A staged crewmate's, dt screenshot --act lantern, has no body: its light is where the art hung it.)
        if (Crew is not null && Look is not null)
            foreach (var c in Crew.Where(c => c.Lamp && Bodies?.Any(b => b.Kind == Sim.Physics.BodyKind.Lamp && b.Carrier == c.Id) != true))
                if (Look.Art.LampInHand(c.Id, Time) is { } at)
                    HandLamp(mesh, at, eye, c.Id);
        if (Lights is not null)
            foreach (var (at, colour, radius) in Lights)
                mesh.PointLights.Add(new PointLight(V(at, eye), colour, radius));
        foreach (var frame in frames)
        {
            if ((frame.Origin - eye).Length > LampRange)
                continue;
            // A car whose lamps are out (the crew's put them out against the Fire Flies, a creature's done it, or a
            // switchyard's car standing as the night found them, note 187) is dark inside.
            bool dark = frame.Index >= frames.Count - LampsOut || CarDark(frame.Index);
            // Its interior as an enclosed space: the night stays outside it (Room).
            if (Look is not null && frame.Shape.Interior is { } inside)
                mesh.Rooms.Add(new Room(V(frame.ToWorld(inside.Centre), eye), ToF(frame.Right), ToF(frame.Up), ToF(frame.Back), ToF(inside.HalfSize)));
            // (A lamp in what a Car Hugger's eaten of the car has gone with its ceiling: Art/BiteKit.)
            var eatenBy = Look is null ? default : Art.Bite.For(Look.Tuning.Bite, frame.Shape, Vehicles is { } fleet && frame.Index < fleet.Count ? fleet[frame.Index] : null, frame.Index);
            if (RoofGlow && !dark && frame.Shape.Interior is { } lit)
                mesh.PointLights.Add(new PointLight(V(frame.ToWorld(new Double3(0, lit.Max.Y + 0.6, lit.Centre.Z)), eye), Palette.LampAmber * 1.4f, 9f));
            if (frame.Shape.Interior is { } room && !dark)
                foreach (double z in new[] { -room.HalfSize.Z * 0.5, room.HalfSize.Z * 0.5 })
                {
                    if (eatenBy.Eats(new Vector3(0, (float)room.Max.Y - 0.05f, (float)(room.Centre.Z + z))))
                        continue;
                    // With the art pass the lamps are flames, and flicker (Art.SceneArt.Flicker); the greybox's are steady.
                    float flicker = Look is null ? 1 : Art.SceneArt.Flicker(Time, frame.Index * 2 + (z < 0 ? 0 : 1));
                    mesh.PointLights.Add(Emergency
                        ? new PointLight(V(frame.ToWorld(new Double3(0, room.Max.Y - 0.2, room.Centre.Z + z)), eye), EmergencyRed, 4f)
                        : new PointLight(V(frame.ToWorld(new Double3(0, room.Max.Y - 0.2, room.Centre.Z + z)), eye), Palette.LampAmber * 1.6f * flicker, 7.5f));
                }
            foreach (var i in frame.Shape.Interactables.Where(i => i.Kind == InteractableKind.Firebox && FireGlow > 0))
                mesh.PointLights.Add(new PointLight(V(frame.ToWorld(i.Position + new Double3(0, 0.7, 0.3)), eye), FireColour(0.6f + 1.6f * FireGlow), 5f));
        }
        // Who the livestock look round at (note 455): the eye, and the crew's heads.
        if (Look is not null)
            Look.Art.Onlookers = Onlook ? [eye, .. (Crew ?? []).Where(c => c.Alive).Select(c => c.Feet + new Double3(0, 1.5, 0))] : null;
        foreach (var frame in frames)
            if (CutAway?.Contains(frame.Index) != true)
                Car(mesh, frame, eye);
            else
                CutFloor(mesh, frame, eye);
        Lap(mesh, "cars");
        if (Look is not null)
        {
            // The art pass's effects (Art/Effects): smoke, steam, sparks, the lamp's beam, and fog banks along the line.
            Look.Art.Effects.Train(mesh, frames, eye, Time, Controls, FireGlow, Emergency, Venting && !Ruptured, SafetyValve && !Ruptured,
                frames.Count == 0 ? default : Art.Bite.For(Look.Tuning.Bite, frames[^1].Shape, Vehicles is { } fleet && frames[^1].Index < fleet.Count ? fleet[frames[^1].Index] : null, frames[^1].Index),
                whistle: !Ruptured && (CordPulled || Enemies?.Any(e => e is Sim.Enemies.Whistler { Whistling: true } && !e.Gone) == true), dead: Ruptured, lamp: LampLit);
            // (And the crew on a straining car stumble: SceneArt.Crewmate, drawn after.)
            Look.Art.BendStrain = BendStrain;
            if (BendStrain is { } bends)
                Look.Art.Effects.Flanges(mesh, frames, eye, Time, bends);
            if (Ruptured && frames.Count > 0)
            {
                _rupturedAt ??= Time - StagedRuptureSeconds;
                Look.Art.Effects.Rupture(mesh, frames[0], eye, Time - _rupturedAt.Value, DriversLocked);
            }
            else
                _rupturedAt = null;
            // Every break to mend, called out (note 301), with a small hot light so the glow reads on the wall round it.
            if (Breaks is { Count: > 0 } breaks)
            {
                Look.Art.Effects.Repairs(mesh, frames, eye, Time, breaks, Mending);
                foreach (var b in breaks)
                    if (frames.FirstOrDefault(f => f.Index == b.Vehicle) is { Shape: not null } bf && (bf.Origin - eye).Length < 60)
                        mesh.PointLights.Add(new PointLight(V(bf.ToWorld(b.At), eye), Palette.LampAmber * (2.2f + 0.8f * (float)Math.Sin(Time * 6.9)), 4f));
            }
            // Derailed (GDD §14): timed from the frame the scene first saw it (presentation only; the sim just stops the train).
            if (Derailed)
            {
                _derailedAt ??= Time - StagedDerailSeconds;
                Look.Art.Effects.Derailment(mesh, frames, eye, Time - _derailedAt.Value);
            }
            else
                _derailedAt = null;
            // The derailment's (T117): sparks, dust and the engine's steam.
            if (Wreck is { } wreck)
            {
                double wreckHint = 0;
                Look.Art.Effects.Wreck(mesh, wreck, frames, eye, Time, (x, z) => Sim.Player.PlayerMotor.GroundAt(new Double3(x, 0, z), line, ref wreckHint));
            }
            var fog = Look.Apply(FrameLighting.Night).FogColor;
            Look.Art.Effects.Fog(mesh, line, eye, centre, Time, fog, (float)(Route?.Weather.FogDensity ?? 0.016));
            if (Route?.Weather is { Wet: true } weather)
                Look.Art.Effects.Rain(mesh, eye, Time, (float)weather.Wind, fog);
            // The Choir coming: the frost before it's seen (App. A.7), from halfway through its gathering, and while it's here.
            if (ChoirGathering > ChoirFrostFrom)
                Look.Art.Effects.Frost(mesh, eye, Time, ChoirCold(ChoirGathering));
            // Something out past the lamp heard the crew (note 287).
            if (Answer.Showing)
                Look.Art.Effects.Eyes(mesh, Answer.At, eye, Answer.Seconds, AnswerShowSeconds);
            // Something that lives at this stop, watching the crew afoot (note 327).
            if (Watcher.Showing)
                Look.Art.Effects.Eyes(mesh, Watcher.At, eye, Watcher.Seconds, WatcherShowSeconds);
            // The air of a corrupted stretch: ash, spores (GDD §30), or brass dust over a brass field.
            Look.Art.Effects.Corruption(mesh, eye, Time, StagedAir
                ?? (Art.WorldArt.NearBrass(Route, eye) ? Art.Effects.Air.Brass : Art.Effects.AirOf(Art.WorldArt.BiomeAt(Route, centre))));
            Strikes(mesh, Look.Art.Effects, line, hint, frames, eye);
        }
        Lap(mesh, "effects");
        mesh.Seed = 0;
        // When the firehole's door last moved, and where it is: whoever's at it then swings it (Art/SceneArt.Crewmate).
        if (_doorWas is { } was && was != FireDoorOpen)
            _doorMovedAt = Time;
        _doorWas = FireDoorOpen;
        if (Look is not null)
        {
            bool cab = frames.Count > 0 && frames[0].Shape.Cab is not null && frames[0].Shape.Interactables.Any(i => i.Kind == InteractableKind.Firebox);
            Look.Art.FireDoorAt = cab ? frames[0].ToWorld(new Double3(Art.TrainKit.FireDoor(frames[0].Shape).X, 0, Art.TrainKit.FireDoor(frames[0].Shape).Z)) : null;
            Look.Art.FireDoorSince = _doorMovedAt < 0 ? -1 : Time - _doorMovedAt;
        }
        // The firebox door, open or shut, for a Stoker in the fire (Art/CreatureArt.FireDoorOpen).
        if (Look?.Art.Creatures is { } creatures && frames.Count > 0 && frames[0].Shape.Cab is not null)
            creatures.FireDoorOpen = FireDoorOpen
                ? Art.TrainKit.FireDoor(frames[0].Shape) - ToF(frames[0].Shape.Interactables.First(i => i.Kind == InteractableKind.Firebox).Position)
                : null;
        // On the stack's rim, looking down the boiler at the cab (App. A.5 "it perches on the smokestack").
        double down = StokerLowFor - (StokerDownAt - StokerDescendSeconds);
        if (StokerLowFor >= 0 && down < StokerDescendSeconds && Look?.Art.Creatures is { } perching && frames.Count > 0
            && frames[0].Shape.Solids.FirstOrDefault(s => s.Part == PartKind.Stack) is { Box: var stack } && stack.Max.Y > 0)
        {
            var f0 = frames[0];
            var o = V(f0.ToWorld(new Double3(0, stack.Max.Y, stack.Centre.Z)), eye);
            // Facing forward along the boiler at the cab (note 276: the stack's at the rear now): the model faces its −Z, as
            // the engine does.
            var r = ToF(f0.Right);
            var b = ToF(f0.Back);
            var basis = Art.CreatureArt.Basis(o, r, ToF(f0.Up), b);
            if (down < 0 || !perching.Draw(mesh, "stoker", "descend", down, false, basis))
                perching.Draw(mesh, "stoker", "perch", Time, true, basis);
        }
        Look?.Art.Creatures?.Clutches.Clear();
        Look?.Art.Creatures?.Pins.Clear();
        // A fire on its cells draws its flames there (FireGrids), and only its smoke and light at its heart (note 267).
        if (Look?.Art.Effects is { } cells)
            cells.CellFlames = Enemies?.Any(e => e is Sim.Enemies.CarFire { Gone: false, Heat.Length: > 0 }) == true;
        if (Enemies is not null)
            foreach (var e in Enemies)
                if (e.Kind == EnemyKind.Passenger && Look?.Art.Creatures is { } passengers && passengers.Get("passenger") is not null)
                {
                    // The art pass's own (Art/CreatureArt, note 124): a conductor off a lost train, in the colour of the
                    // crewmate it copies; it walks as fast as it's been going (GreyboxScene's pace).
                    if (!e.Gone)
                        DrawEnemy(mesh, line, frames, e, eye, from, to, passengers, default, null, null, Pace(e), Flinch(e), HitAge(e));
                }
                else if (e is Sim.Enemies.Passenger passenger)
                {
                    // One of the crew, to look at (App. A.7 BLEND): drawn exactly as they are, their face and all.
                    if (!e.Gone && AsCrewmate(passenger, frames, Outfits) is { } double_ && Look?.Art.Crewmate(mesh, double_, eye, Time) != true)
                        DrawCrewmate(mesh, double_, eye);
                }
                else if (!e.Gone)
                {
                    // A Car Hugger's head goes in as far as it's eaten into its car (Art/BiteKit).
                    var bite = e.Kind == EnemyKind.CarHugger && e.Attached >= 0 && e.Attached < frames.Count && Look is { } look
                        ? Art.Bite.For(look.Tuning.Bite, frames[e.Attached].Shape, Vehicles is { } vs && e.Attached < vs.Count ? vs[e.Attached] : null, e.Attached)
                        : default;
                    // A Tippy Toesie faces who it's after (its Extra), and smothering them stands to them; a Ribbit faces
                    // its pack's mark, and its tongue goes to them; a Gaunt faces its waker; a Follower rides its carrier's
                    // back (Art/CreatureArt).
                    // A feral Grumbler, who it's after: the nearest of them (who hit it is the host's alone); a Soot Child's, the
                    // one it's on (the nearest: it's at their feet).
                    // (A Gaunt leaving, its extra is what it's carrying off, not who woke it: it faces where it's going.)
                    bool leaving = e.Kind == EnemyKind.Gaunt && e.Phase == SpinePhase.BreakOff;
                    var after = !leaving && e.Kind is EnemyKind.TippyToesie or EnemyKind.Ribbit or EnemyKind.Choir or EnemyKind.Gaunt or EnemyKind.Follower && e.Extra >= 0
                        ? Crew?.FirstOrDefault(c => c.Id == (int)e.Extra)
                        : (e.Kind == EnemyKind.Grumbler && e.Phase >= SpinePhase.Commit || e.Kind == EnemyKind.SootChildren && e.Phase is SpinePhase.Grab or SpinePhase.Punish)
                            && Crew is { } crew && crew.Any(c => c.Alive)
                            ? crew.Where(c => c.Alive).MinBy(c => (c.Feet - EnemyWorld(e, frames)).Length)
                            : null;
                    // A Moose pinning someone stands over them (note 339): the one it holds.
                    if (e.Kind is EnemyKind.Moose or EnemyKind.Gannet && e.Holding >= 0)
                        after = Crew?.FirstOrDefault(c => c.Id == e.Holding);
                    // A Knotter coils round whoever slipped (note 365).
                    if (e.Kind == EnemyKind.Knotter && e.Holding >= 0)
                        after = Crew?.FirstOrDefault(c => c.Id == e.Holding);
                    Art.CreatureArt.Prey? prey = leaving && GauntHeading(e, frames) is { } going
                        ? new(V(going, eye), Vector3.Zero)
                        : after is { } victim
                        ? new(V(victim.Feet, eye), new Vector3((float)-Math.Sin(victim.Yaw), 0, (float)-Math.Cos(victim.Yaw)))
                        : null;
                    // And stoops under a roof, ducks through a door (note 110); a Gaunt gets down (note 118).
                    Art.CreatureArt.Room? room = e.Kind is EnemyKind.TippyToesie or EnemyKind.Gaunt && e.Attached >= 0 && e.Attached < frames.Count
                        ? Art.CreatureArt.Room.Of(frames[e.Attached].Shape, e.Local)
                        : null;
                    // A Moose by the line as the train goes by (note 339): it takes it for a rival, tossing its head after it.
                    if (e.Kind == EnemyKind.Moose && Look?.Art.Creatures is { } herd)
                        herd.TrainPassing = Math.Abs(_speed) > 1 && frames.Count > 0
                            && frames.Min(f => ((f.Origin - e.Local) with { Y = 0 }).Length - f.Shape.HalfLength) <= herd.MooseTuning.TrainPassAt;
                    // A Gannet's clips go by what it's been doing and for how long, and what it did before (note 340).
                    double modeSeconds = e is Sim.Enemies.Moose moose ? MooseSince(moose) : -1;
                    if (e is Sim.Enemies.Gannet gannet)
                    {
                        var (since, before) = GannetSince(gannet);
                        modeSeconds = since;
                        if (Look?.Art.Creatures is { } flock)
                            flock.GannetWas = before;
                    }
                    // The Mourners', the Freight Beetle's and Tower Jaw's too (notes 362, 366, 363); Tower Jaw's tower, once
                    // it's down, lies across the line at its spout.
                    bool outside = e.Kind is EnemyKind.Mourners or EnemyKind.FreightBeetle or EnemyKind.TowerJaw;
                    if (outside)
                        modeSeconds = ModeSince(e);
                    // The train's own: their clips by their mode's time and what came before (notes 364, 365, 367).
                    if (e.Kind is EnemyKind.Brakeman or EnemyKind.Knotter or EnemyKind.Hotbox)
                        modeSeconds = Trainfolk(e, frames, Look?.Art.Creatures);
                    DrawEnemy(mesh, line, frames, e, eye, from, to, Look?.Art.Creatures, bite, prey, room,
                        e.Kind is EnemyKind.Gaunt or EnemyKind.Grumbler or EnemyKind.Moose || outside ? Pace(e) : 0, Flinch(e), HitAge(e),
                        modeSeconds: modeSeconds, place: e is Sim.Enemies.TowerJaw { Mode: Sim.Enemies.TowerJawMode.Wreck } ? TowerPlace(line, e.Local) : null);
                }
        Deaths(mesh, line, frames, eye, from, to);
        Lap(mesh, "enemies");
        if (Bodies is not null)
        {
            // Heavy crates only come from a facility's site, so its size is there (facilities.json "heavy").
            double heavyHalf = Run?.Sites.FirstOrDefault(x => x is not null)?.HeavyRadius ?? 0.5;
            if (Look is not null)
                Look.Art.Burned = Crew?.Where(c => c.Death is Sim.Player.DeathCause.Burned or Sim.Player.DeathCause.Stoker
                    or Sim.Player.DeathCause.Exploded or Sim.Player.DeathCause.Keg).Select(c => (int)c.Id).ToHashSet();
            foreach (var b in Bodies)
            {
                // In your own hands, drawn at them for the frame (the mirror's own pose is back before anything reads it).
                bool held = HeldHere is { } h && b.Carrier == h.Player && b.Kind is not (Sim.Physics.BodyKind.Heavy or Sim.Physics.BodyKind.Ragdoll or Sim.Physics.BodyKind.Radio);
                var (parent, at, yaw) = (b.Parent, b.Pbd.Particles[0].Position, b.Yaw);
                if (held)
                {
                    b.Parent = Sim.Player.PlayerState.World;
                    b.Pbd.Particles[0].Position = HeldHere!.Value.Hands;
                    b.Yaw = HeldHere.Value.Yaw;
                }
                if (Look is not null)
                    Look.Art.FindItem ??= body => Run?.FindOf(body)?.Item;
                if (Look?.Art.Body(mesh, frames, b, eye, heavyHalf, Time) != true)
                    DrawBody(mesh, frames, b, eye, heavyHalf);
                if (held)
                {
                    b.Parent = parent;
                    b.Pbd.Particles[0].Position = at;
                    b.Yaw = yaw;
                }
            }
        }
        Extinguishing(mesh, frames, eye);
        FireGrids(mesh, frames, eye);
        if (Crew is not null)
            foreach (var held in Crew)
            {
                var c = Hung(held, eye);
                if (c.Alive && Look?.Art.Crewmate(mesh, c, eye, Time, Swung(c.Id)) != true) // the dead are drawn as their bodies
                    // A headset crewmate's body leans, crouches and turns under the head on the box figure too (note 223);
                    // without the clips there's no gait to give way to, so only an act does.
                    DrawCrewmate(mesh, c, eye, _bodies.Pose(c, c.Act is null, Time, Look?.VrBody ?? VrBody ?? new VrBodyTuning()));
            }
        if (Own is { } own)
            Look?.Art.OwnArms(mesh, own, Time);
        Lap(mesh, "bodies and crew");
    }

    /// <summary>
    /// What landed (T121), timed from the sim's tick as a gun's muzzle flash is: each cannonball's explosion where it came
    /// down, and each blow or ball's pop and flash on the creature it landed on.
    /// </summary>
    /// <summary>
    /// Balls from the train's own guns that came down on it (note 370), kept in the car they struck so the scorch rides on
    /// with it: the impact's id, the car, where on it (its frame) and the face's way out, and when (this scene's clock).
    /// </summary>
    readonly List<(int Id, int Car, Double3 Local, Double3 Out, float Room, double Born)> _trainScorches = [];
    const int MaxTrainScorches = 32;

    /// <summary>
    /// The car a ball that landed on the train came down on (its impact replicated in the world's frame, where the car
    /// was then: carried on by the car's way since), the point on its body's face, that face's way out and how far the face
    /// runs on round it (so the burn stays on the plate); null if none.
    /// </summary>
    static (int Car, Double3 Local, Double3 Out, float Room)? OnTheTrain(in Sim.Combat.CannonImpact i, IReadOnlyList<CarFrame> frames, double since)
    {
        (int, Double3, Double3, float)? best = null;
        double nearest = 1.0;
        for (int k = 0; k < frames.Count; k++)
        {
            var f = frames[k];
            var local = f.ToLocal(i.At + f.Velocity * since);
            var s = f.Shape;
            // How far outside its body (0 inside), and the face it's nearest.
            double dx = Math.Abs(local.X) - s.HalfWidth, dz = Math.Abs(local.Z) - s.HalfLength, dy = local.Y - s.RoofHeight;
            double outside = Math.Sqrt(Math.Pow(Math.Max(0, dx), 2) + Math.Pow(Math.Max(0, dz), 2) + Math.Pow(Math.Max(0, dy), 2));
            if (outside >= nearest || local.Y < -0.5)
                continue;
            nearest = outside;
            // Onto the nearest face: the roof, a side or an end.
            var (onto, way) = dy > Math.Max(dx, dz) - 0.3
                ? (local with { Y = s.RoofHeight }, Double3.Up)
                : dx > dz
                    ? (local with { X = (local.X < 0 ? -1 : 1) * s.HalfWidth }, new Double3(local.X < 0 ? -1 : 1, 0, 0))
                    : (local with { Z = (local.Z < 0 ? -1 : 1) * s.HalfLength }, new Double3(0, 0, local.Z < 0 ? -1 : 1));
            // The body's side runs from just under its floor to the roof; the face's edges bound the burn.
            double bottom = (s.Interior?.Min.Y ?? 1.0) - 0.15;
            double room = way.Y > 0.5 ? Math.Min(s.HalfWidth - Math.Abs(onto.X), s.HalfLength - Math.Abs(onto.Z))
                : Math.Min(Math.Min(s.RoofHeight - onto.Y, onto.Y - bottom), way.X != 0 ? s.HalfLength - Math.Abs(onto.Z) : s.HalfWidth - Math.Abs(onto.X));
            best = (k, onto + way * 0.03, way, (float)Math.Max(0.15, room));
        }
        return best;
    }

    void Strikes(MeshBuilder mesh, Art.Effects fx, RailLine line, double hint, IReadOnlyList<CarFrame> frames, Double3 eye)
    {
        if (Tick < 0)
            return;
        // The train's scorches, riding with their cars; a car no longer drawn takes its own with it.
        _trainScorches.RemoveAll(x => x.Car >= frames.Count);
        foreach (var (id, car, local, way, room, born) in _trainScorches)
        {
            var at = frames[car].ToWorld(local);
            if ((at - eye).Length < DrawDistance)
                fx.TrainScorch(mesh, V(at, eye), ToF(frames[car].DirToWorld(way)), id, Time - born, room);
        }
        if (Impacts is not null)
            foreach (var i in Impacts)
            {
                double age = (Tick - i.Tick) * Sim.SimConstants.TickSeconds;
                if (i.Surface == Sim.Combat.ImpactSurface.Train && age >= 0 && !_trainScorches.Exists(x => x.Id == i.Id)
                    && OnTheTrain(i, frames, age) is { } hit)
                {
                    _trainScorches.Add((i.Id, hit.Car, hit.Local, hit.Out, hit.Room, Time - age));
                    if (_trainScorches.Count > MaxTrainScorches)
                        _trainScorches.RemoveAt(0);
                }
                if (age < 0 || age > Art.Effects.ImpactSeconds || (i.At - eye).Length > DrawDistance)
                    continue;
                // A creature's insides come down on the ground under it (note 290).
                Vector3? ground = null;
                if (i.Surface == Sim.Combat.ImpactSurface.Creature)
                {
                    double near = hint;
                    ground = V(i.At with { Y = Sim.Player.PlayerMotor.GroundAt(i.At, line, ref near) }, eye);
                }
                fx.CannonImpact(mesh, V(i.At, eye), ToF(i.Direction), i.Surface, i.Struck, age, i.Id, ground);
            }
        if (Hits is null)
            return;
        foreach (var h in Hits)
        {
            double age = (Tick - h.Tick) * Sim.SimConstants.TickSeconds;
            if (age < 0 || age > 1 || (h.At - eye).Length > DrawDistance)
                continue;
            // Its body's middle: where it is now if it's still about, or where the blow was.
            var body = Enemies?.FirstOrDefault(e => e.Id == h.EnemyId && !e.Gone) is { } e ? EnemyWorld(e, frames) + Double3.Up * 0.8 : h.At;
            fx.HitFlash(mesh, V(h.At, eye), V(body, eye), ToF(h.From), h.Source, h.Killed, age, h.Id);
        }
    }

    /// <summary>
    /// Where a Gaunt leaving with what it took is walking to (Sim.Enemies.Gaunt.Leave): aboard, its car's nearest door; loose,
    /// straight off from the nearest car. Null if it's nowhere to be drawn.
    /// </summary>
    static Double3? GauntHeading(Enemy e, IReadOnlyList<CarFrame> frames)
    {
        if (e.Attached >= 0 && e.Attached < frames.Count)
        {
            var f = frames[e.Attached];
            var here = e.Local;
            var doors = f.Shape.DoorList;
            var exit = doors.Count > 0 ? doors.MinBy(d => ((d.Box.Centre - here) with { Y = 0 }).Length).Box.Centre with { Y = here.Y }
                : new Double3((here.X >= 0 ? 1 : -1) * f.Shape.HalfWidth, here.Y, here.Z);
            return f.ToWorld(exit);
        }
        if (e.Attached != Enemy.Loose || frames.Count == 0)
            return null;
        var at = e.Local;
        var near = frames.MinBy(f => ((f.Origin - at) with { Y = 0 }).Length);
        double across = Double3.Dot((at - near.Origin) with { Y = 0 }, near.Right);
        return at + near.Right * (across >= 0 ? 5 : -5);
    }

    /// <summary>
    /// Seconds since crewmate <paramref name="id"/>'s latest swing: one that landed (its HitConfirm, T121) or one at nothing
    /// (its SwingEvent, note 197; the blow is decided on the tick the swing starts, so both time the clip alike), or −1.
    /// </summary>
    double Swung(int id)
    {
        if (Tick < 0)
            return -1;
        double best = -1;
        void Consider(uint tick)
        {
            double age = (Tick - tick) * Sim.SimConstants.TickSeconds;
            if (age >= 0 && (best < 0 || age < best))
                best = age;
        }
        foreach (var h in Hits ?? [])
            if (h.By == id && h.Source == Sim.Combat.HitSource.Melee)
                Consider(h.Tick);
        foreach (var w in Swings ?? [])
            if (w.By == id)
                Consider(w.Tick);
        return best;
    }

    /// <summary>How long a creature flinches from a blow (s), and how far it's knocked (m) and tipped (rad) at the most.</summary>
    const double FlinchSeconds = 0.32;
    const float FlinchPush = 0.16f, FlinchTip = 0.22f;

    /// <summary>
    /// A creature's flinch from the latest blow or ball to land on it (T121's hit confirm): its basis knocked back along the
    /// blow and tipped away from it, sharp in and easing back; a ball's twice a blow's. Identity when nothing's landed lately.
    /// </summary>
    (Vector3 Push, Quaternion Tip) Flinch(Enemy e)
    {
        if (Hits is null || Tick < 0)
            return (Vector3.Zero, Quaternion.Identity);
        Sim.Combat.HitConfirm? latest = null;
        foreach (var h in Hits)
            if (h.EnemyId == e.Id && (latest is null || h.Tick > latest.Value.Tick))
                latest = h;
        if (latest is not { } hit)
            return (Vector3.Zero, Quaternion.Identity);
        double age = (Tick - hit.Tick) * Sim.SimConstants.TickSeconds;
        if (age < 0 || age > FlinchSeconds)
            return (Vector3.Zero, Quaternion.Identity);
        float k = (float)(age < 0.05 ? age / 0.05 : 1 - (age - 0.05) / (FlinchSeconds - 0.05));
        k *= hit.Source == Sim.Combat.HitSource.Cannon ? 2 : 1;
        var flat = new Vector3((float)hit.From.X, 0, (float)hit.From.Z);
        if (flat.LengthSquared() < 1e-6f)
            return (Vector3.Zero, Quaternion.Identity);
        flat = Vector3.Normalize(flat);
        return (flat * (FlinchPush * k), Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(Vector3.UnitY, flat)), FlinchTip * k));
    }

    // What was drawn last frame, by id, and the ones killed since (with the tick of the blow): a kill takes the creature
    // out of the sim at once (it's Gone, then removed), and its hit lasts a second on the wire, so the death is the scene's
    // own memory of it (presentation only; nothing here goes back to the sim).
    readonly Dictionary<int, Enemy> _seen = new();
    readonly Dictionary<int, (Enemy Body, uint Tick, Vector3 From)> _dying = new();
    // The Choir's ghosts driven off since (with the tick they went): the sim dismisses the swarm at once.
    readonly Dictionary<int, (Enemy Body, uint Tick)> _leaving = new();
    // The Track Dolls as they stood last frame (copies: the sim moves its own), the toys about then, and where a doll's
    // vanished from since (with the tick, and the toy it took or −1).
    readonly Dictionary<int, Enemy> _dollWas = new();
    readonly HashSet<int> _toysWas = new();
    readonly List<(Enemy Was, uint Tick, int Toy)> _vanished = new();
    // The Car Huggers cut loose with their cars since (with the tick): the sim's done with one once its car is off the train.
    readonly Dictionary<int, (Enemy Body, uint Tick)> _riding = new();
    // The Cinder Hounds on the line scattered by a ball or driven off since (with the tick, and how fast they were running
    // along the line then): the sim has one gone the tick it breaks off.
    readonly Dictionary<int, (Enemy Body, uint Tick, double Speed)> _fleeing = new();
    // The rest let go of in sight since, not killed (note 458): a copy as it was last seen, the tick, how fast it was going
    // along the line then (the train's, aboard or pacing it), and where it set off from in the world (on its first frame).
    readonly Dictionary<int, Retreat> _retreating = new();
    sealed class Retreat(Enemy body, uint tick, double speed)
    {
        public Enemy Body { get; } = body;
        public uint Tick { get; } = tick;
        public double Speed { get; } = speed;
        public Double3? From;
        public Double3 Along, Out;
        public double Hint;
    }
    readonly List<(string Beat, Enemy Body)> _newBeats = new();

    /// <summary>What began in the last <see cref="Remember"/>, each as it was last seen: "killed", "dispersed" (a Choir
    /// ghost), "cut-loose" (a Car Hugger, its car off the train), "vanished" (a Track Doll), "scattered" (a Cinder Hound
    /// off the line). For dt playthrough's shots.</summary>
    public IReadOnlyList<(string Beat, Enemy Body)> NewBeats => _newBeats;

    /// <summary>
    /// The killed (T121's "hit confirm", the checklist's "shot or clubbed", "killed"): a creature that dies doesn't blink out.
    /// It keels over away from the blow about its feet, held in its hit pose, lies a moment, then crumbles into ash and soot
    /// and is gone (<see cref="Art.Effects.DeathSeconds"/>): every creature that stands on something; what floats, swarms,
    /// burns or is the train's own (the Choir, the Fire Flies, a car fire, the Stoker in its box, the Car Hugger) has its own.
    /// </summary>
    void Deaths(MeshBuilder mesh, RailLine line, IReadOnlyList<CarFrame> frames, Double3 eye, double from, double to)
    {
        if (Tick < 0)
            return;
        Remember(frames.Count);
        Leaving(mesh, line, frames, eye, from, to);
        Vanishing(mesh, line, frames, eye, from, to);
        Riding(mesh, line, frames, eye, from, to);
        Fleeing(mesh, line, frames, eye, from, to);
        Retreating(mesh, line, frames, eye, from, to);
        if (_dying.Count == 0)
            return;
        var fx = Look?.Art.Effects;
        foreach (var (id, (body, tick, blow)) in _dying.ToArray())
        {
            double age = (Tick - tick) * Sim.SimConstants.TickSeconds;
            if (age > Art.Effects.DeathSeconds || age < 0 || _seen.ContainsKey(id))
            {
                _dying.Remove(id);
                continue;
            }
            var (push, roll) = Fallen(blow, (float)age);
            var was = body;
            // The Car Hugger clubbed to death (note 458): its grip on the car's end gone, it's left where it was as the train
            // runs on, down onto the track. (Its own clip is the latch's; dead, it's rolled and crumbles as anything does.)
            if (was.Kind == EnemyKind.CarHugger && was.Attached >= 0 && was.Attached < frames.Count)
            {
                var dropped = Enemy.Blank(EnemyKind.CarHugger, id);
                // Clear of the car's end it hung on (a train stood at a stop doesn't pull away from it): out past it 1.5 m.
                var end = frames[was.Attached];
                double off = Math.Sign(was.Local.Z) * 1.5;
                dropped.Restore(SpinePhase.BreakOff, 0, 0, Enemy.Loose, end.ToWorld(was.Local) + end.Back * off, was.LineDistance, 0, 0, 0, 0);
                _dying[id] = (was = dropped, tick, blow);
            }
            if (was.Kind == EnemyKind.CarHugger && was.Attached == Enemy.Loose)
            {
                double hint = was.LineDistance;
                double ground = Sim.Player.PlayerMotor.GroundAt(was.Local, line, ref hint);
                var falling = Enemy.Blank(EnemyKind.CarHugger, id);
                falling.Restore(SpinePhase.BreakOff, age, 0, Enemy.Loose, was.Local with { Y = Math.Max(ground, was.Local.Y - 0.5 * 9.81 * age * age) }, was.LineDistance, 0, 0, 0, 0);
                was = falling;
            }
            // The Gannet has its own fall (gannet.py death: crashing across the roof, the wings crumpling): not rolled
            // over, and shot out of the air over a car, it falls to that roof first (note 340).
            var fallen = was;
            if (was is Sim.Enemies.Gannet g)
                (roll, fallen) = (0, Falling(g, frames, age));
            // So have the train's own (their death clips: pitched off the roof, unlaid, over on its back).
            if (body.Kind is EnemyKind.Brakeman or EnemyKind.Knotter or EnemyKind.Hotbox)
                roll = 0;
            DrawEnemy(mesh, line, frames, fallen, eye, from, to, Look?.Art.Creatures, flinch: (push, Quaternion.Identity), hitAge: age, dying: true, roll: roll);
            if (fx is not null && BodyAt(fallen, line, frames) is var at && (at - eye).Length < DrawDistance)
                fx.Crumble(mesh, V(at, eye), (float)age, id);
        }
    }

    /// <summary>
    /// What this frame's world has done since the last that the scene's seen (presentation only: the sim's done with it in
    /// a tick, the scene's own memory draws it going): the killed, the Choir driven off, a Car Hugger cut loose, a Track
    /// Doll gone from where it stood. Drawing the scene does this; something that photographs a night now and then (dt
    /// playthrough) calls it every tick between, so what it shows is what a crew watching all along would see.
    /// </summary>
    public void Remember(int frameCount)
    {
        _newBeats.Clear();
        if (Tick < 0)
            return;
        if (Hits is not null)
            foreach (var h in Hits)
                if (h.Killed && !_dying.ContainsKey(h.EnemyId) && _seen.TryGetValue(h.EnemyId, out var body) && (Falls(body.Kind) || body.Kind == EnemyKind.CarHugger))
                {
                    _dying[h.EnemyId] = (body, h.Tick, new Vector3((float)h.From.X, 0, (float)h.From.Z));
                    _newBeats.Add(("killed", body));
                }
        // The Choir driven off (A.7: its quiet held, or its one taken): every ghost the sim's dismissed this tick, not killed.
        foreach (var (id, was) in _seen)
            if (was.Kind == EnemyKind.Choir && !_dying.ContainsKey(id) && !_leaving.ContainsKey(id) && Enemies?.Any(e => e.Id == id && !e.Gone) != true)
            {
                _leaving[id] = (was, (uint)Tick);
                _newBeats.Add(("dispersed", was));
            }
        // The Car Hugger whose car's been cut from the train, or that ate through it and dropped away with it (A.3: "it goes
        // with its car into the dark"): the sim's done with it that tick, the car's still there, rolling away.
        foreach (var (id, was) in _seen)
            if (was.Kind is EnemyKind.CarHugger or EnemyKind.CarFire && was.Attached >= 0 && was.Attached < frameCount && Adrift(was.Attached)
                && !_dying.ContainsKey(id) && !_riding.ContainsKey(id) && Enemies?.Any(e => e.Id == id && !e.Gone) != true)
            {
                // A fire too (note 458): the car's cut loose burning, and burns on as it rolls away (the sim's done with it).
                _riding[id] = (Copy(was), (uint)Tick);
                _newBeats.Add(("cut-loose", was));
            }
        // A Cinder Hound running the line gone and not killed (note 451): scattered by a ball landing near it (note 328), driven
        // off by the guns (A.3) or left behind. The sim's done with it that tick; it's seen running off.
        foreach (var (id, was) in _seen)
            if (was.Kind == EnemyKind.CinderHound && was.Attached == -1 && !_dying.ContainsKey(id) && !_fleeing.ContainsKey(id)
                && Enemies?.Any(e => e.Id == id && !e.Gone) != true)
            {
                _fleeing[id] = (was, (uint)Tick, Math.Max(0, _speed));
                _newBeats.Add(("scattered", was));
            }
        // The rest let go of, not killed (note 458): a Climber outnumbered or giving up, a Whistler found in its gap, a pack
        // that's eaten, the Gaunt going with its loot, a Switchman at the lever, a hound aboard over the side. The sim has
        // them gone the tick it's done with them; they're seen going.
        foreach (var (id, was) in _seen)
            if (Art.CreatureArt.Retreat(was.Kind) is not null && !(was.Kind == EnemyKind.CinderHound && was.Attached < 0)
                && !(was.Kind == EnemyKind.CarHugger) && !_dying.ContainsKey(id) && !_retreating.ContainsKey(id)
                && Enemies?.Any(e => e.Id == id && !e.Gone) != true)
            {
                var copy = Enemy.Blank(was.Kind, id, was.Extra);
                copy.Restore(SpinePhase.BreakOff, 0, was.Health, was.Attached, was.Local, was.LineDistance, was.Lateral, was.Height, was.Extra, was.Extra2);
                _retreating[id] = new Retreat(copy, (uint)Tick, was.Attached >= 0 || was.Kind == EnemyKind.Climber ? Math.Max(0, _speed) : 0);
                _newBeats.Add(("retreated", was));
            }
        _seen.Clear();
        foreach (var e in Enemies ?? [])
            if (!e.Gone)
                _seen[e.Id] = e;
        var toys = (Bodies ?? []).Where(b => b.Kind == Sim.Physics.BodyKind.Toy).Select(b => b.Id).ToHashSet();
        foreach (var (id, was) in _dollWas)
        {
            var now = Enemies?.FirstOrDefault(e => e.Id == id && !e.Gone);
            if (_dying.ContainsKey(id) || now is not null && now.Attached == was.Attached && (now.Local - was.Local).Length < 1)
                continue;
            int toy = now is null ? _toysWas.Where(t => !toys.Contains(t)).DefaultIfEmpty(-1).Min() : -1;
            _vanished.Add((was, (uint)Tick, toy));
            _newBeats.Add(("vanished", was));
        }
        _dollWas.Clear();
        foreach (var e in Enemies ?? [])
            if (e.Kind == EnemyKind.TrackDoll && !e.Gone)
            {
                var copy = new Sim.Enemies.TrackDoll(e.Id);
                copy.Restore(e.Phase, e.PhaseSeconds, e.Health, e.Attached, e.Local, e.LineDistance, e.Lateral, e.Height, e.Extra, e.Extra2);
                _dollWas[e.Id] = copy;
            }
        _toysWas.Clear();
        _toysWas.UnionWith(toys);
    }

    /// <summary>
    /// The Choir dispersing (GDD v1.2 App. A.7, the checklist's "a disperse when the crew hushes"): the sim takes the swarm
    /// out the tick it's driven off; here each ghost is seen going (<see cref="Art.CreatureArt.ChoirLeaveSeconds"/>): turned
    /// away from the train mouth first in its swoop, swept up and out into the dark faster and faster, its cold going out.
    /// </summary>
    void Leaving(MeshBuilder mesh, RailLine line, IReadOnlyList<CarFrame> frames, Double3 eye, double from, double to)
    {
        foreach (var (id, (body, tick)) in _leaving.ToArray())
        {
            double age = (Tick - tick) * Sim.SimConstants.TickSeconds;
            if (age > Art.CreatureArt.ChoirLeaveSeconds || age < 0 || _seen.ContainsKey(id))
            {
                _leaving.Remove(id);
                continue;
            }
            var at = BodyAt(body, line, frames);
            var near = frames.Count == 0 ? at : frames.MinBy(f => (f.ToWorld(default) - at).Length)!.ToWorld(default);
            var off = new Double3(at.X - near.X, 0, at.Z - near.Z);
            off = off.Length > 1e-6 ? off.Normalized : new Double3(1, 0, 0);
            float go = (float)(age / Art.CreatureArt.ChoirLeaveSeconds);
            go *= go;
            var push = new Vector3((float)off.X, 0, (float)off.Z) * (LeaveOut * go) + Vector3.UnitY * (LeaveUp * go);
            var ghost = new Sim.Enemies.ChoirGhost(body.Id);
            ghost.Restore(SpinePhase.BreakOff, age, body.Health, body.Attached, body.Local, body.LineDistance, body.Lateral, body.Height, -1, body.Extra2);
            DrawEnemy(mesh, line, frames, ghost, eye, from, to, Look?.Art.Creatures, flinch: (push, Quaternion.Identity));
        }
    }

    /// <summary>
    /// The Track Doll vanishing (GDD v1.2 App. A.2, the checklist's "vanish with no walk-off"; "takes a toy and goes"): the
    /// sim moves it to another car when it's come at, or takes it out of the run (stopped short of, appeased), from one tick
    /// to the next. It's never seen to walk. Where it was, it flickers out in its last pose, held stock still, and leaves a
    /// puff of porcelain dust and a few chips of glaze (<see cref="Art.Effects.Vanish"/>); given a toy, the toy goes with it,
    /// in its hand (the toy body gone the same tick).
    /// </summary>
    void Vanishing(MeshBuilder mesh, RailLine line, IReadOnlyList<CarFrame> frames, Double3 eye, double from, double to)
    {
        var creatures = Look?.Art.Creatures;
        for (int i = _vanished.Count - 1; i >= 0; i--)
        {
            var (was, tick, toy) = _vanished[i];
            double age = (Tick - tick) * Sim.SimConstants.TickSeconds;
            if (age > Art.Effects.VanishSeconds || age < 0)
            {
                _vanished.RemoveAt(i);
                continue;
            }
            // A few frames of it, on and off, then nothing: there, and not.
            if (age < DollFlicker && (int)(age * 30) % 2 == 0)
            {
                if (creatures is not null)
                    creatures.DollHolding = toy >= 0 ? Look?.Art.Toy(toy, Bodies?.FirstOrDefault(b => b.Id == toy)?.Noise ?? default) : null;
                DrawEnemy(mesh, line, frames, was, eye, from, to, creatures);
                if (creatures is not null)
                    creatures.DollHolding = null;
            }
            if (Look?.Art.Effects is { } fx && BodyAt(was, line, frames) is var at && (at - eye).Length < DrawDistance)
                fx.Vanish(mesh, V(at, eye), (float)age, was.Id);
        }
    }

    /// <summary>
    /// The Car Hugger cut loose (GDD v1.2 App. A.3, "cut loose, it goes with its car into the dark"; the checklist's "rides the
    /// cut car away"): uncoupled from the train, or eaten through so the car drops away, the sim takes it out that tick, but
    /// the car's still there, rolling free and falling behind. It doesn't let go: here it's seen still clamped on that car's
    /// end, feeding, grinding, as the car goes off into the dark (until it's out of sight, or the car's coupled up again).
    /// </summary>
    void Riding(MeshBuilder mesh, RailLine line, IReadOnlyList<CarFrame> frames, Double3 eye, double from, double to)
    {
        foreach (var (id, (body, tick)) in _riding.ToArray())
        {
            double age = (Tick - tick) * Sim.SimConstants.TickSeconds;
            int car = body.Attached;
            if (age < 0 || _seen.ContainsKey(id) || car >= frames.Count || !Adrift(car)
                || (frames[car].ToWorld(default) - eye).Length > DrawDistance)
            {
                _riding.Remove(id);
                continue;
            }
            // A fire, kept burning on its car as it rolls away: its flames here, and its car's smoke (Fires: _burns).
            if (body is Sim.Enemies.CarFire burning)
            {
                var fire = new Sim.Enemies.CarFire(id);
                fire.Restore(burning.Phase, burning.PhaseSeconds + age, burning.Health, car, burning.Local, 0, 0, 0, burning.Extra, burning.Extra2);
                fire.RestoreHeat(burning.Heat);
                DrawEnemy(mesh, line, frames, fire, eye, from, to, Look?.Art.Creatures);
                continue;
            }
            var hugger = new Sim.Enemies.CarHugger(id);
            hugger.Restore(SpinePhase.Commit, body.PhaseSeconds + age, body.Health, car, body.Local, 0, 0, 0, body.Extra, body.Extra2);
            var bite = Look is { } look
                ? Art.Bite.For(look.Tuning.Bite, frames[car].Shape, Vehicles is { } vs && car < vs.Count ? vs[car] : null, car)
                : default;
            DrawEnemy(mesh, line, frames, hugger, eye, from, to, Look?.Art.Creatures, bite);
        }
    }

    /// <summary>
    /// A Cinder Hound scattered (note 451; the guns' effect on creatures, note 290): a ball landing near a runner sends it off
    /// (note 328), and fire drives a pack off (A.3), and the sim has it gone that tick. It doesn't blink out where it stood:
    /// it runs on, slowing (so the train draws away from it), and peels off the line on its own side, turned the way it's
    /// going, out into the dark with its embers going out (<see cref="Art.CreatureArt.HoundRunOffSeconds"/>).
    /// </summary>
    void Fleeing(MeshBuilder mesh, RailLine line, IReadOnlyList<CarFrame> frames, Double3 eye, double from, double to)
    {
        foreach (var (id, (body, tick, speed)) in _fleeing.ToArray())
        {
            double age = (Tick - tick) * Sim.SimConstants.TickSeconds;
            if (age > Art.CreatureArt.HoundRunOffSeconds || age < 0 || _seen.ContainsKey(id))
            {
                _fleeing.Remove(id);
                continue;
            }
            var (along, lateral, turn) = RunOff(body, speed, age);
            var hound = new Sim.Enemies.CinderHound(id, (int)body.Extra);
            hound.Restore(SpinePhase.BreakOff, age, body.Health, -1, body.Local, along, lateral, body.Height, body.Extra, body.Extra2);
            DrawEnemy(mesh, line, frames, hound, eye, from, to, Look?.Art.Creatures, flinch: (Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitY, turn)));
        }
    }

    /// <summary>
    /// The ones let go of in sight going (note 458; <see cref="Art.CreatureArt.Retreat"/>): from where each was last seen, down
    /// to the ground under it if it was up on a car, falling behind as the train it was going with runs on (pulling up along
    /// the line), and out from the line on its own side, turned away from the train, in its break-off; then lost in the dark.
    /// </summary>
    void Retreating(MeshBuilder mesh, RailLine line, IReadOnlyList<CarFrame> frames, Double3 eye, double from, double to)
    {
        foreach (var (id, r) in _retreating.ToArray())
        {
            double age = (Tick - r.Tick) * Sim.SimConstants.TickSeconds;
            if (Art.CreatureArt.Retreat(r.Body.Kind) is not { } go || age > go.Seconds || age < 0 || _seen.ContainsKey(id))
            {
                _retreating.Remove(id);
                continue;
            }
            if (r.From is null)
            {
                // Where it was, in the world: on its car, on the line beside it, or stood loose.
                var b = r.Body;
                Double3 at;
                if (b.Attached >= 0 && b.Attached < frames.Count)
                    at = frames[b.Attached].ToWorld(b.Local);
                else if (b.Attached == Enemy.Loose)
                    at = b.Local;
                else
                {
                    var s = line.Sample(b.LineDistance);
                    at = s.Position + Double3.Cross(s.Tangent, Double3.Up).Normalized * b.Lateral + Double3.Up * b.Height;
                }
                double hint = b.LineDistance;
                var (path, d) = line.Nearest(at, ref hint);
                var near = line.Sample(path, d);
                r.Hint = hint;
                r.From = at;
                r.Along = (near.Tangent with { Y = 0 }).Normalized;
                var off = (at - near.Position) with { Y = 0 };
                var side = Double3.Cross(near.Tangent, Double3.Up).Normalized;
                r.Out = Double3.Dot(off, side) >= 0 ? side : side * -1;
            }
            var (along, outward) = Away(r.Speed, go.Out, age);
            var p = r.From.Value + r.Along * along + r.Out * outward;
            double h = r.Hint;
            double ground = Sim.Player.PlayerMotor.GroundAt(p, line, ref h);
            // Down off whatever it was on (a roof, a car's floor, the gap's plate), as anything dropped falls.
            double y = Math.Max(ground, r.From.Value.Y - 0.5 * 9.81 * age * age);
            var copy = Enemy.Blank(r.Body.Kind, id, r.Body.Extra);
            copy.Restore(SpinePhase.BreakOff, age, r.Body.Health, Enemy.Loose, p with { Y = y }, r.Body.LineDistance, r.Body.Lateral, 0, r.Body.Extra, r.Body.Extra2);
            // Loose, it's drawn facing the nearest car: turned about, it faces away, the way it's going.
            DrawEnemy(mesh, line, frames, copy, eye, from, to, Look?.Art.Creatures, flinch: (Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI)));
        }
    }

    // One let go of going: how hard it pulls up along the line (m/s²), from the speed it was going with the train.
    const double AwaySlowing = 7;

    /// <summary>How far one let go of <paramref name="age"/> s ago has gone along the line (going at <paramref name="speed"/>
    /// with the train, pulling up at <see cref="AwaySlowing"/>) and out from it (at <paramref name="outSpeed"/>).</summary>
    internal static (double Along, double Out) Away(double speed, double outSpeed, double age)
    {
        double run = Math.Min(age, speed / AwaySlowing);
        return (speed * run - 0.5 * AwaySlowing * run * run, outSpeed * age);
    }

    /// <summary>A copy of <paramref name="e"/> as it is now (the sim moves its own, and a snapshot's is replaced): its phase,
    /// place and state, and a fire's heat.</summary>
    static Enemy Copy(Enemy e)
    {
        var copy = Enemy.Blank(e.Kind, e.Id, e.Extra);
        copy.Restore(e.Phase, e.PhaseSeconds, e.Health, e.Attached, e.Local, e.LineDistance, e.Lateral, e.Height, e.Extra, e.Extra2);
        if (e is Sim.Enemies.CarFire fire && copy is Sim.Enemies.CarFire into)
            into.RestoreHeat([.. fire.Heat]);
        return copy;
    }

    /// <summary>Staged (<c>dt screenshot --retreat kind:s</c>): <paramref name="e"/> let go of at <paramref name="tick"/>, going
    /// with the train at <paramref name="speed"/> (it's not in <see cref="Enemies"/> any more).</summary>
    public void Retreated(Enemy e, uint tick, double speed)
    {
        var copy = Enemy.Blank(e.Kind, e.Id, e.Extra);
        copy.Restore(SpinePhase.BreakOff, 0, e.Health, e.Attached, e.Local, e.LineDistance, e.Lateral, e.Height, e.Extra, e.Extra2);
        _retreating[e.Id] = new Retreat(copy, tick, speed);
    }

    // A scattered hound running off: how hard it pulls up along the line (m/s²) and how fast it goes out across it (m/s).
    const double RunOffSlowing = 7, RunOffOut = 6;

    /// <summary>
    /// Where a hound scattered <paramref name="age"/> s ago is (along the line, and off it), and its turn off the line (about
    /// +Y, from facing along it): it was running at <paramref name="speed"/>, pulls up along the line at
    /// <see cref="RunOffSlowing"/> and goes out on its own side at <see cref="RunOffOut"/>, facing the way it's going.
    /// </summary>
    internal static (double Along, double Lateral, float Turn) RunOff(Enemy was, double speed, double age)
    {
        double stop = speed / RunOffSlowing, run = Math.Min(age, stop);
        double along = was.LineDistance + speed * run - 0.5 * RunOffSlowing * run * run;
        double side = was.Lateral >= 0 ? 1 : -1;
        double lateral = was.Lateral + side * RunOffOut * age;
        // Facing its way over the ground: a positive turn about +Y swings a heading along the line to its left, so its side is −.
        double going = Math.Atan2(RunOffOut, Math.Max(0, speed - RunOffSlowing * age));
        return (along, lateral, (float)(-side * going));
    }

    /// <summary>Staged (<c>dt screenshot --scattered s</c>): the Cinder Hound <paramref name="e"/> scattered at
    /// <paramref name="tick"/>, running at <paramref name="speed"/> (it's not in <see cref="Enemies"/> any more).</summary>
    public void Scattered(Enemy e, uint tick, double speed) => _fleeing[e.Id] = (e, tick, speed);

    /// <summary>Whether <paramref name="car"/> is off the engine's train: a coupling's cut somewhere between it and the engine
    /// (<see cref="Cut"/>, a car's front end open where the car ahead of it is in another rake).</summary>
    bool Adrift(int car) => Cut is { } cut && cut.Any(end => end % 2 == 0 && end / 2 >= 1 && end / 2 <= car);

    /// <summary>Staged (<c>dt screenshot --cut n --hugger ride</c>): the Car Hugger <paramref name="e"/>, its car cut from the
    /// train at <paramref name="tick"/> (it's not in <see cref="Enemies"/> any more).</summary>
    public void Rode(Enemy e, uint tick) => _riding[e.Id] = (e, tick);

    // How long the Track Doll is seen flickering out where it was (s).
    const double DollFlicker = 0.3;

    /// <summary>Staged (<c>dt screenshot --vanish s[:toy]</c>): the Track Doll <paramref name="e"/> gone at <paramref name="tick"/>
    /// (out of <see cref="Enemies"/>), with the toy body <paramref name="toy"/> if it was given one.</summary>
    public void Vanished(Enemy e, uint tick, int toy = -1) => _vanished.Add((e, tick, toy));

    // How far a dispersing ghost has gone at the end of its going: out from the train and up (m).
    const float LeaveOut = 10, LeaveUp = 14;

    /// <summary>Staged (<c>dt screenshot --dispersing s</c>): the Choir's ghost <paramref name="e"/> driven off at
    /// <paramref name="tick"/> (it's not in <see cref="Enemies"/> any more).</summary>
    public void Dispersed(Enemy e, uint tick) => _leaving[e.Id] = (e, tick);

    /// <summary>
    /// Staged (<c>dt screenshot --killed kind:s</c>): <paramref name="e"/> killed at <paramref name="tick"/> by a blow going
    /// <paramref name="blow"/> (it's not in <see cref="Enemies"/> any more: the sim's taken it out).
    /// </summary>
    public void Killed(Enemy e, uint tick, Vector3 blow) => _dying[e.Id] = (e, tick, blow);

    /// <summary>Where a creature stands, as DrawEnemy places it: in its car, loose in the world, or at its place on the line.</summary>
    static Double3 BodyAt(Enemy e, RailLine line, IReadOnlyList<CarFrame> frames)
    {
        if (e.Attached >= 0 || e.Attached == Enemy.Loose)
            return EnemyWorld(e, frames);
        var t = line.Sample(Math.Clamp(e.LineDistance, 0, line.Length));
        return t.Position + Double3.Cross(t.Tangent, Double3.Up).Normalized * e.Lateral + Double3.Up * e.Height;
    }

    /// <summary>A Gannet killed in the air over a car, <paramref name="age"/> s on: dropped toward that car's roof as dead
    /// weight falls (its stand-in: the sim's done with it), and stopped there.</summary>
    static Sim.Enemies.Gannet Falling(Sim.Enemies.Gannet g, IReadOnlyList<CarFrame> frames, double age)
    {
        if (g.Attached < 0 || g.Attached >= frames.Count)
            return g;
        double roof = frames[g.Attached].Shape.RoofHeight;
        if (g.Local.Y <= roof + 0.05)
            return g;
        var fell = new Sim.Enemies.Gannet(g.Id);
        fell.Restore(g.Phase, g.PhaseSeconds, g.Health, g.Attached, g.Local with { Y = Math.Max(roof, g.Local.Y - 0.5 * 9.81 * age * age) },
            g.LineDistance, g.Lateral, g.Height, g.Extra, g.Extra2, g.Holding, g.GrabWindow);
        return fell;
    }

    internal static bool Falls(EnemyKind k) => k is not (EnemyKind.Choir or EnemyKind.FireFlies or EnemyKind.CarFire or EnemyKind.Stoker or EnemyKind.CarHugger
        or EnemyKind.Sleepers or EnemyKind.Drift);

    /// <summary>How far over a killed creature has gone, <paramref name="age"/> seconds after the blow: pushed along it and
    /// rolled over onto its side (eased in, as dead weight goes; DrawEnemy rolls it about its own length), then sinking as it
    /// crumbles.</summary>
    static (Vector3 Push, float Roll) Fallen(Vector3 blow, float age)
    {
        var flat = blow.LengthSquared() > 1e-6f ? Vector3.Normalize(blow) : Vector3.UnitX;
        float over = Math.Clamp(age / 0.5f, 0, 1);
        over = over * over * (3 - 2 * over);
        float sink = Math.Clamp((age - (float)Art.Effects.DeathSeconds * 0.55f) / ((float)Art.Effects.DeathSeconds * 0.4f), 0, 1);
        return (flat * (0.25f * over) - Vector3.UnitY * (0.45f * sink), 1.35f * over + 1e-4f);
    }

    /// <summary>Seconds since a blow or a ball last landed on <paramref name="e"/>, or −1 when none has in the last second.</summary>
    double HitAge(Enemy e)
    {
        if (Hits is null || Tick < 0)
            return -1;
        double best = -1;
        foreach (var h in Hits)
            if (h.EnemyId == e.Id)
            {
                double age = (Tick - h.Tick) * Sim.SimConstants.TickSeconds;
                if (age >= 0 && age <= 1 && (best < 0 || age < best))
                    best = age;
            }
        return best;
    }

    /// <summary>Staged: the air to draw whatever the biome (dt screenshot --air ash|spores).</summary>
    public Art.Effects.Air? StagedAir { get; set; }

    /// <summary>
    /// How far the Choir's come (World.Choir: its gathering, 0 to 1, and 1 while the swarm's here): from
    /// <see cref="ChoirFrostFrom"/> on, the frost in the air and everyone's breath showing (App. A.7's arrival beat).
    /// </summary>
    public float ChoirGathering { get; set; }

    /// <summary>
    /// The dark's answer to a draw (World.Answer, note 287): eyes at the lamp's edge while it shows, over
    /// <see cref="AnswerShowSeconds"/> (enemies.json director.draw.showSeconds).
    /// </summary>
    public Sim.Enemies.DrawAnswer Answer { get; set; }
    public double AnswerShowSeconds { get; set; } = 7;

    /// <summary>
    /// A sign shown a crewmate afoot off the train (World.Watcher, note 327): eyes toward what lives at the stop while it shows,
    /// over <see cref="WatcherShowSeconds"/> (enemies.json director.afoot.signSeconds).
    /// </summary>
    public Sim.Enemies.Watcher Watcher { get; set; }
    public double WatcherShowSeconds { get; set; } = 3.5;

    // From how far through its gathering the Choir's cold is felt.
    const float ChoirFrostFrom = 0.5f;

    /// <summary>How much of the Choir's cold is on the air at <paramref name="gathering"/> (0 to 1, eased): none before
    /// <see cref="ChoirFrostFrom"/>, all of it once it's here. The frost in the air, and the frame's chill (Look.Chill).</summary>
    public static float ChoirCold(float gathering) => gathering > ChoirFrostFrom ? SmoothStep((gathering - ChoirFrostFrom) / (1 - ChoirFrostFrom)) : 0;

    static float SmoothStep(float x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - 2 * x);
    }

    /// <summary>The train's come off the rails (World.Derailed): its sparks, dust and burst boiler (Art/Effects.Derailment).</summary>
    public bool Derailed { get; set; }

    /// <summary>Staged: how long ago it derailed, when the scene is first built derailed (dt screenshot --derailed s).</summary>
    public double StagedDerailSeconds { get; set; }

    double? _derailedAt;

    /// <summary>Staged: a spray onto every car fire, from the aisle (dt screenshot --spray), whoever's holding it.</summary>
    public bool StagedSpray { get; set; }

    // Each fire's intensity and when it last fell: an extinguisher's charge isn't replicated, but a fire going down with
    // an extinguisher held near it is being sprayed (presentation only, held a moment so it doesn't blink between ticks).
    readonly Dictionary<int, (double Extra, double Fell)> _fires = new();
    readonly HashSet<int> _spraying = new();

    /// <summary>Extinguishers at work (App. C.5), from what's replicated: a fire going down, an extinguisher carried within reach of it.</summary>
    void Extinguishing(MeshBuilder mesh, IReadOnlyList<CarFrame> frames, Double3 eye)
    {
        // Who's spraying, for their pose (SceneArt.Spraying: braced and kicking, not stood with it); the crew are drawn
        // after this.
        _spraying.Clear();
        if (Look is not null)
            Look.Art.Spraying = _spraying;
        if (Look?.Art.Effects is not { HasFlames: true } fx || Enemies is null)
            return;
        foreach (var e in Enemies)
        {
            if (e.Kind != EnemyKind.CarFire || e.Gone || e.Attached < 0 || e.Attached >= frames.Count)
                continue;
            double fell = _fires.TryGetValue(e.Id, out var was) ? (e.Extra < was.Extra - 1e-9 ? Time : was.Fell) : double.NegativeInfinity;
            _fires[e.Id] = (e.Extra, fell);
            var car = frames[e.Attached];
            var foot = car.ToWorld(e.Local + new Double3(-0.2, 0.5, 0)).RelativeTo(eye);
            if (StagedSpray)
                fx.Spray(mesh, car.ToWorld(e.Local + new Double3(-1.0, 1.0, 1.7)).RelativeTo(eye), foot, Time);
            if (Time - fell > 0.4 || Bodies is null)
                continue;
            foreach (var b in Bodies)
            {
                if (b.Kind != Sim.Physics.BodyKind.Extinguisher || b.Carrier < 0)
                    continue;
                var held = HeldHere is { } h && b.Carrier == h.Player ? h.Hands
                    : b.Parent >= 0 && b.Parent < frames.Count ? frames[b.Parent].ToWorld(b.Pbd.Particles[0].Position) : b.Pbd.Particles[0].Position;
                var nozzle = held.RelativeTo(eye);
                if ((nozzle - foot).Length() < 3.2f)
                {
                    fx.Spray(mesh, nozzle, foot, Time);
                    _spraying.Add(b.Carrier);
                }
            }
        }
    }

    /// <summary>
    /// The fire grid (GDD App. F.1; note 267), from what's replicated: every burning car's cells alight, each where it is on
    /// the floor, walls and roof, and every car's char where its cells have burnt.
    /// </summary>
    void FireGrids(MeshBuilder mesh, IReadOnlyList<CarFrame> frames, Double3 eye)
    {
        if (Look?.Art.Effects is not { HasFlames: true } fx || Look.Art.Creatures is not { } creatures)
            return;
        double cell = creatures.CarFire.CellSize;
        (Vector3 At, Vector3 Along, Vector3 Across, Vector3 Normal) Patch(CarFrame car, Sim.Enemies.FireGrid grid, int i)
        {
            var c = grid.Centre[i];
            var size = grid.Room.Max - grid.Room.Min;
            double hz = size.Z / grid.Along / 2;
            var n = grid.Normal(i);
            // Across the floor and roof is X; up a wall is Y.
            var across = grid.Face[i] is Sim.Enemies.FireFace.Floor or Sim.Enemies.FireFace.Ceiling
                ? new Double3(size.X / grid.Across / 2, 0, 0) : new Double3(0, size.Y / grid.Up / 2, 0);
            var at = car.ToWorld(c).RelativeTo(eye);
            return (at, car.ToWorld(c + new Double3(0, 0, hz)).RelativeTo(eye) - at, car.ToWorld(c + across).RelativeTo(eye) - at,
                Vector3.Normalize(car.ToWorld(c + n).RelativeTo(eye) - at));
        }
        if (Vehicles is { } fleet)
            foreach (var car in frames)
                if (car.Index < fleet.Count && fleet[car.Index].Char is { Length: > 0 } burnt && car.Shape.Interior is { } room
                    && Sim.Enemies.FireGrid.For(room, cell) is { } grid && grid.Count == burnt.Length)
                    for (int i = 0; i < grid.Count; i++)
                        if (burnt[i] > 0)
                        {
                            var (at, along, across, normal) = Patch(car, grid, i);
                            Art.Effects.Char(mesh, at, along, across, normal, burnt[i] / (float)((1 << Sim.Enemies.CarFire.Bits) - 1), car.Index * 131 + i);
                        }
        foreach (var e in Enemies ?? [])
        {
            if (e is not Sim.Enemies.CarFire { Gone: false, Heat.Length: > 0 } fire || e.Attached < 0 || e.Attached >= frames.Count)
                continue;
            var car = frames[e.Attached];
            if (car.Shape.Interior is not { } room || Sim.Enemies.FireGrid.For(room, cell) is not { } grid || grid.Count != fire.Heat.Length)
                continue;
            bool alight = e.Phase is SpinePhase.Commit or SpinePhase.Punish;
            for (int i = 0; i < grid.Count; i++)
            {
                var (at, along, across, normal) = Patch(car, grid, i);
                fx.FireCell(mesh, at, along, across, normal, grid.Face[i], (float)fire.Heat[i],
                    alight && fire.Heat[i] >= creatures.CarFire.BurnFrom * 0.8, Time, e.Id * 97 + i);
            }
        }
    }

    /// <summary>
    /// The Passenger as the crewmate whose face it wears: their look, walking as they would. Its id is theirs offset by 200,
    /// so the gait each figure is smoothed by is its own and not the one it copies.
    /// </summary>
    /// <summary>
    /// Someone the Whistler's carrying off (App. A.4 GRAB), hung in its forelegs as it was drawn this frame (Art/CreatureArt
    /// Clutches): by the armpits from its hooks, facing the way it runs. The sim has them at its middle; this is where
    /// they're seen. Someone a Car Hugger's swallowing (A.3) stands bent into its mouth wherever in reach the sim caught them. Put down at its nest (no longer in its forelegs), they're on their back in it,
    /// paralysed (App. A.4).
    /// </summary>
    Crewmate Hung(Crewmate c, Double3 eye)
    {
        // Pinned under a Moose's rack (note 339; Art/CreatureArt.Pins): where the sim has them, on their back, laid with their
        // head toward it (it's stood over them).
        if (Look?.Art.Creatures?.Pins.TryGetValue(c.Id, out var pin) == true)
            return c with { Yaw = Math.Atan2(-pin.Forward.X, -pin.Forward.Z), Act = Art.CrewPose.HeldPinned };
        if (Look?.Art.Creatures?.Clutches.TryGetValue(c.Id, out var clutch) != true)
            return c.Act == Art.CrewPose.HeldCarried ? c with { Act = Art.CrewPose.HeldPinned } : c;
        var yaw = Math.Atan2(-clutch.Forward.X, -clutch.Forward.Z);
        if (!clutch.Hung)
        {
            // In the Car Hugger's mouth: stood in front of it on their own floor, facing it, bent into it.
            var mouth = eye + new Double3(clutch.At.X, clutch.At.Y, clutch.At.Z);
            var back = new Double3(clutch.Forward.X, 0, clutch.Forward.Z) * Art.CreatureArt.SwallowReach;
            return c with { Feet = (mouth - back) with { Y = c.Feet.Y }, Yaw = yaw };
        }
        var at = eye + new Double3(clutch.At.X, clutch.At.Y - Art.CreatureArt.CarriedUnderarm, clutch.At.Z);
        return c with { Feet = at, Yaw = yaw };
    }

    /// <param name="outfits">Everyone's outfits (note 298): the face it wears comes in its owner's outfit.</param>
    public static Crewmate? AsCrewmate(Sim.Enemies.Passenger p, IReadOnlyList<CarFrame> frames, IReadOnlyDictionary<int, byte>? outfits = null)
    {
        if (p.Attached < 0 || p.Attached >= frames.Count)
            return null;
        var frame = frames[p.Attached];
        var forward = frame.DirToWorld(new Double3(-Math.Sin(p.Extra2), 0, -Math.Cos(p.Extra2)));
        int looks = outfits is not null && outfits.TryGetValue(p.Looks, out byte worn) ? worn : p.Looks;
        return new Crewmate((byte)(200 + p.Looks), frame.ToWorld(p.Local), Math.Atan2(-forward.X, -forward.Z), true, Looks: looks,
            Car: p.Attached, Local: p.Local);
    }

    static Double3? BodyWorld(Sim.Physics.Body b, IReadOnlyList<CarFrame> frames, Double3 local) =>
        b.Parent == Sim.Player.PlayerState.World ? local : b.Parent < frames.Count ? frames[b.Parent].ToWorld(local) : null;

    /// <summary>Crates and lamps as boxes turned by their yaw; a body as bones between its joints.</summary>
    /// <param name="heavyHalf">A heavy crate's half-size, its body's radius (a client's stand-in bodies don't carry their radius).</param>
    static void DrawBody(MeshBuilder mesh, IReadOnlyList<CarFrame> frames, Sim.Physics.Body b, Double3 eye, double heavyHalf)
    {
        var up = b.Parent == Sim.Player.PlayerState.World || b.Parent >= frames.Count ? Double3.Up : frames[b.Parent].Up;
        double heading = b.Parent == Sim.Player.PlayerState.World || b.Parent >= frames.Count ? 0 : frames[b.Parent].Heading;
        var ps = b.Pbd.Particles;
        if (b.Kind != Sim.Physics.BodyKind.Ragdoll)
        {
            if (BodyWorld(b, frames, ps[0].Position) is not { } at)
                return;
            double yaw = b.Yaw + heading;
            var right = new Vector3((float)Math.Cos(yaw), 0, (float)-Math.Sin(yaw));
            var back = new Vector3((float)Math.Sin(yaw), 0, (float)Math.Cos(yaw));
            if (b.Kind == Sim.Physics.BodyKind.Cargo)
            {
                // Freight: bigger than the train's own stores, stencilled, strapped.
                mesh.Box(V(at, eye), right, ToF(up), back, new Vector3(0.44f, 0.44f, 0.44f), Palette.BlueGrey);
                mesh.Box(V(at, eye), right, ToF(up), back, new Vector3(0.45f, 0.05f, 0.45f), Palette.SootBlack);
                mesh.Box(V(at, eye), right, ToF(up), back, new Vector3(0.05f, 0.45f, 0.45f), Palette.SootBlack);
            }
            else if (b.Kind == Sim.Physics.BodyKind.Heavy)
            {
                // Two-man freight (T43): a long iron-banded case with a rope grip at each end, so it reads as one for two.
                float h = (float)heavyHalf;
                mesh.Box(V(at, eye), right, ToF(up), back, new Vector3(h * 1.3f, h * 0.8f, h * 0.8f), Palette.RustRed * 0.8f);
                mesh.Box(V(at, eye), right, ToF(up), back, new Vector3(h * 1.31f, 0.05f, h * 0.81f), Palette.IronGrey);
                foreach (float end in new[] { -1f, 1f })
                {
                    mesh.Box(V(at, eye) + right * (end * h * 1.1f), right, ToF(up), back, new Vector3(0.05f, h * 0.81f, h * 0.81f), Palette.IronGrey);
                    mesh.Box(V(at, eye) + right * (end * (h * 1.3f + 0.06f)) + ToF(up) * (h * 0.3f), right, ToF(up), back, new Vector3(0.05f, 0.03f, 0.18f), Palette.DeepBrown);
                }
            }
            else if (b.Kind == Sim.Physics.BodyKind.Crate)
            {
                mesh.Box(V(at, eye), right, ToF(up), back, new Vector3(0.34f, 0.34f, 0.34f), Palette.TarnishedBrass * 0.8f);
                mesh.Box(V(at, eye), right, ToF(up), back, new Vector3(0.35f, 0.06f, 0.35f), Palette.DeepBrown);
            }
            else if (b.Kind == Sim.Physics.BodyKind.Loot)
            {
                // A village find (level-design P12): a small bundle in sacking with a brass-buckled strap.
                mesh.Box(V(at, eye), right, ToF(up), back, new Vector3(0.18f, 0.13f, 0.14f), Palette.DeepBrown);
                mesh.Box(V(at, eye) + ToF(up) * 0.03f, right, ToF(up), back, new Vector3(0.185f, 0.02f, 0.145f), Palette.TarnishedBrass);
            }
            else if (b.Kind == Sim.Physics.BodyKind.Radio)
            {
                // A walkie-talkie (T41): an iron brick with its aerial up and a pinprick of a lamp, so a dropped one's found.
                mesh.Box(V(at, eye), right, ToF(up), back, new Vector3(0.07f, 0.12f, 0.04f), Palette.IronGrey);
                mesh.Box(V(at, eye) + ToF(up) * 0.2f + right * 0.04f, right, ToF(up), back, new Vector3(0.008f, 0.1f, 0.008f), Palette.SootBlack);
                mesh.Emissive = 1;
                mesh.Box(V(at, eye) + ToF(up) * 0.09f - back * 0.042f, right, ToF(up), back, new Vector3(0.012f, 0.012f, 0.004f), Palette.SignalRed);
                mesh.Emissive = 0;
            }
            else
            {
                mesh.Box(V(at, eye), right, ToF(up), back, new Vector3(0.1f, 0.14f, 0.1f), Palette.IronGrey);
                mesh.Emissive = 1;
                mesh.Box(V(at, eye) + ToF(up) * 0.02f, right, ToF(up), back, new Vector3(0.07f, 0.07f, 0.11f), Palette.LampAmber);
                mesh.Emissive = 0;
            }
            return;
        }
        // Bones: head-chest, chest-pelvis, arms, legs.
        (int, int, float)[] bones = [(0, 1, 0.12f), (1, 2, 0.17f), (1, 3, 0.07f), (3, 4, 0.06f), (1, 5, 0.07f), (5, 6, 0.06f), (2, 7, 0.09f), (7, 8, 0.08f), (2, 9, 0.09f), (9, 10, 0.08f)];
        foreach (var (a, c, r) in bones)
        {
            if (a >= ps.Length || c >= ps.Length || BodyWorld(b, frames, ps[a].Position) is not { } pa || BodyWorld(b, frames, ps[c].Position) is not { } pc)
                continue;
            var axis = pc - pa;
            double len = axis.Length;
            if (len < 1e-4)
                continue;
            var dir = axis * (1 / len);
            var side = Double3.Cross(dir, Math.Abs(dir.Y) > 0.9 ? new Double3(1, 0, 0) : Double3.Up).Normalized;
            var third = Double3.Cross(side, dir);
            var colour = a == 0 ? Palette.Corrupted : Palette.DeepBrown;
            mesh.Box(V((pa + pc) * 0.5, eye), ToF(side), ToF(dir), ToF(third), new Vector3(r, (float)len / 2 + r * 0.5f, r), colour);
        }
    }

    /// <summary>
    /// A crewmate: coat, head and a lamp at the chest so you can find each other in the dark. Dead ones lie down. A headset
    /// crewmate's <paramref name="body"/> (T82, note 223), when there is one: the coat leans over from the hips and twists
    /// towards the head, the hips go down in a crouch and turn to their own yaw, the legs bend to the feet where they're
    /// planted, and the arms reach from where the shoulders have gone.
    /// </summary>
    static void DrawCrewmate(MeshBuilder mesh, Crewmate c, Double3 eye, VrBodyPose? body = null)
    {
        var right = new Vector3((float)Math.Cos(c.Yaw), 0, (float)-Math.Sin(c.Yaw));
        var back = new Vector3((float)Math.Sin(c.Yaw), 0, (float)Math.Cos(c.Yaw));
        var o = V(c.Feet, eye);
        if (!c.Alive)
        {
            mesh.Box(o + Vector3.UnitY * 0.15f, right, Vector3.UnitY, back, new Vector3(0.25f, 0.15f, 0.9f), Palette.DeepBrown);
            return;
        }
        // In the frame they face, from the feet (x right, y up, z behind): Arms' frame, and VrBodyPose's.
        Vector3 At(Double3 local) => o + right * (float)local.X + Vector3.UnitY * (float)local.Y + back * (float)local.Z;
        void Segment(Double3 a, Double3 b, float r, Vector3 colour)
        {
            var axis = At(b) - At(a);
            float len = axis.Length();
            if (len < 1e-4f)
                return;
            var dir = axis / len;
            var side = Vector3.Normalize(Vector3.Cross(dir, MathF.Abs(dir.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY));
            mesh.Box((At(a) + At(b)) * 0.5f, side, dir, Vector3.Cross(side, dir), new Vector3(r, len * 0.5f + r * 0.5f, r), colour);
        }
        // A box turned with a frame (its axes in the facing frame), centred at a point in it.
        void Turned(Double3 at, Double3 x, Double3 y, Double3 z, Vector3 half, Vector3 colour) =>
            mesh.Box(At(at), At(x) - o, At(y) - o, At(z) - o, half, colour);
        var b = body ?? default;
        // The torso's frame: leant forward about its own right by the lean, then turned to the hips' yaw and twisted back
        // toward the head by the torso's share. The hips' own frame is the yaw alone; the hips go down by the crouch.
        double torsoYaw = b.Hips + b.Twist;
        Double3 Torso(Double3 v) => Yawed(Leant(v, b.Lean), torsoYaw);
        var hips = new Double3(0, Hips - b.Crouch, 0);
        Double3 Body(Double3 standing) => hips + Torso(standing - new Double3(0, Hips, 0));
        if (body is null)
            mesh.Box(o + Vector3.UnitY * 0.45f, right, Vector3.UnitY, back, new Vector3(0.16f, 0.45f, 0.12f), Palette.Charcoal);
        else
        {
            // Two legs, hip to knee to the ankle over each foot, the knee bent forward of the hips by what the crouch takes up.
            var forward = Yawed(new Double3(0, 0, -1), b.Hips);
            foreach (var (side, foot) in new[] { (-1, b.LeftFoot), (1, b.RightFoot) })
            {
                var hip = hips + Yawed(new Double3(side * 0.09, 0, 0), b.Hips);
                var ankle = new Double3(foot.X, foot.Y + 0.06, foot.Z);
                double half = (Hips - 0.06) / 2, d = (hip - ankle).Length;
                var knee = (hip + ankle) * 0.5 + forward * Math.Sqrt(Math.Max(0, half * half - d * d / 4));
                Segment(hip, knee, 0.08f, Palette.Charcoal);
                Segment(knee, ankle, 0.07f, Palette.Charcoal);
                Turned(ankle with { Y = foot.Y + 0.04 } + forward * 0.05, Yawed(new Double3(1, 0, 0), b.Hips), new Double3(0, 1, 0),
                    Yawed(new Double3(0, 0, 1), b.Hips), new Vector3(0.06f, 0.04f, 0.12f), Palette.Charcoal);
            }
        }
        var (xT, yT, zT) = (Torso(new Double3(1, 0, 0)), Torso(new Double3(0, 1, 0)), Torso(new Double3(0, 0, 1)));
        Turned(Body(new Double3(0, 1.2, 0)), xT, yT, zT, new Vector3(0.24f, 0.33f, 0.15f), Palette.DeepBrown);
        // The head on the neck, turned the head's way (the figure's own facing) whatever the torso does.
        Turned(Body(new Double3(0, 1.68, 0)), new Double3(1, 0, 0), new Double3(0, 1, 0), new Double3(0, 0, 1), new Vector3(0.12f, 0.13f, 0.12f), Palette.Corrupted);
        // Arms (T47): to a headset player's hands where they are, hanging for everyone else (from the shoulders, as they've gone).
        var (left, rightHand) = Arms.Hands(c.Hand, c.Other);
        foreach (var (armSide, reported) in new[] { (-1, left), (1, rightHand) })
        {
            var shoulder = Body(Arms.Shoulder(armSide));
            var target = reported == Arms.Hanging(armSide) ? Body(reported) : reported;
            var (elbow, hand) = Arms.Solve(shoulder, target, Arms.Pole(armSide));
            Segment(shoulder, elbow, 0.06f, Palette.DeepBrown);
            Segment(elbow, hand, 0.05f, Palette.DeepBrown);
            // A glove, a shade lighter: the hand is what the others watch.
            mesh.Box(At(hand), right, Vector3.UnitY, back, new Vector3(0.05f, 0.05f, 0.05f), Palette.Corrupted * 0.8f);
        }
        mesh.Emissive = 1;
        Turned(Body(new Double3(0, 1.3, -0.16)), xT, yT, zT, new Vector3(0.05f, 0.05f, 0.02f), Palette.LampAmber);
        mesh.Emissive = 0;
    }

    /// <summary>The box figure's hips over its feet, standing (m): the top of its legs.</summary>
    const double Hips = 0.9;

    /// <summary>A facing-frame vector leant forward (toward −z) about x by <paramref name="lean"/> radians.</summary>
    static Double3 Leant(Double3 v, double lean) =>
        new(v.X, v.Y * Math.Cos(lean) + v.Z * Math.Sin(lean), -v.Y * Math.Sin(lean) + v.Z * Math.Cos(lean));

    /// <summary>A facing-frame vector turned about y by <paramref name="yaw"/> radians (positive left, as a player's yaw).</summary>
    static Double3 Yawed(Double3 v, double yaw) =>
        new(v.X * Math.Cos(yaw) + v.Z * Math.Sin(yaw), v.Y, -v.X * Math.Sin(yaw) + v.Z * Math.Cos(yaw));

    /// <summary>Where <paramref name="e"/> stands in the world: in its car's frame aboard, as it is loose.</summary>
    static Double3 EnemyWorld(Enemy e, IReadOnlyList<CarFrame> frames) =>
        e.Attached >= 0 && e.Attached < frames.Count ? frames[e.Attached].ToWorld(e.Local) : e.Local;

    // Where each Gaunt and Grumbler was last drawn (its car's frame aboard, the world off it), when, and how fast it's been going.
    readonly Dictionary<int, (int Attached, Double3 Local, double Time, float Pace)> _paces = new();

    /// <summary>
    /// How fast <paramref name="e"/> has been going (m/s, across the ground, eased over a fraction of a second): a Gaunt
    /// lopes after its waker while they go and stands over them listening when they stop; a Grumbler scuttles, or bites
    /// (Art/CreatureArt). Drawn twice in
    /// one frame (both eyes), it's the pace it had.
    /// </summary>
    float Pace(Enemy e)
    {
        if (StagedPaces is { } staged && staged.TryGetValue(e.Id, out float given))
            return given;
        if (!_paces.TryGetValue(e.Id, out var was) || was.Attached != e.Attached || Time < was.Time)
        {
            _paces[e.Id] = (e.Attached, e.Local, Time, 0);
            return 0;
        }
        double dt = Time - was.Time;
        if (dt <= 0)
            return was.Pace;
        var d = e.Local - was.Local;
        float now = (float)(Math.Sqrt(d.X * d.X + d.Z * d.Z) / dt);
        float pace = was.Pace + (now - was.Pace) * (float)(1 - Math.Exp(-dt / PaceEasing));
        _paces[e.Id] = (e.Attached, e.Local, Time, pace);
        return pace;
    }

    // Seconds a pace takes to come round to a new speed (the sim moves things in tick steps; this smooths them out).
    const double PaceEasing = 0.25;

    // What each Moose was doing when last drawn, and when it started (scene time).
    readonly Dictionary<int, (Sim.Enemies.MooseMode Mode, double Since)> _mooseModes = new();

    /// <summary>
    /// How long a Moose has been at what it's doing (its <see cref="Sim.Enemies.MooseMode"/>, s), for its clips to start with
    /// it: its square-up, its skid and wheel (Art/CreatureArt.MooseClip). Its mode isn't timed on the wire, only its phase, so
    /// this watches it change; first seen (a screenshot, a client joining), its phase's time stands in.
    /// </summary>
    double MooseSince(Sim.Enemies.Moose m)
    {
        if (!_mooseModes.TryGetValue(m.Id, out var was) || Time < was.Since)
            _mooseModes[m.Id] = was = (m.Mode, Time - m.PhaseSeconds);
        else if (was.Mode != m.Mode)
            _mooseModes[m.Id] = was = (m.Mode, Time);
        return Time - was.Since;
    }

    // What each of the Mourners, the Freight Beetle and Tower Jaw was doing when last drawn (its mode, in its Height), since
    // when (scene time).
    readonly Dictionary<int, (int Mode, double Since)> _outsideModes = new();

    /// <summary>
    /// How long a Mourner, the Freight Beetle or Tower Jaw has been at what it's doing (its mode, s; notes 362, 366, 363), for
    /// its clips to start with it (a startle, the beetle rearing, the beaver's turn into its threat and its lunge). As
    /// <see cref="MooseSince"/>: first seen, its phase's time stands in.
    /// </summary>
    double ModeSince(Enemy e)
    {
        int mode = (int)e.Height;
        if (!_outsideModes.TryGetValue(e.Id, out var was) || Time < was.Since)
            _outsideModes[e.Id] = was = (mode, Time - e.PhaseSeconds);
        else if (was.Mode != mode)
            _outsideModes[e.Id] = was = (mode, Time);
        return Time - was.Since;
    }

    /// <summary>
    /// Where a coaling tower Tower Jaw's brought down lies (note 363): on the line at the spout nearest <paramref name="at"/>,
    /// its +X out to the side the tower stood on (StructureKit.CoalingTowerFallen's frame). Null without the night's run.
    /// </summary>
    (Double3 Origin, Double3 Right)? TowerPlace(RailLine line, Double3 at)
    {
        if (Run is not { } run || TowerAt(line, at) is not { } f)
            return null;
        var t = line.Sample(run.ChuteAt(f, line).SpoutAlong);
        var right = Double3.Cross(t.Tangent, Double3.Up).Normalized * (f.Side < 0 ? -1 : 1);
        return (t.Position, right);
    }

    /// <summary>The night's coaling tower nearest <paramref name="at"/> (within 60 m), if any.</summary>
    RouteFeature? TowerAt(RailLine line, Double3 at)
    {
        if (Run is not { } run)
            return null;
        RouteFeature? best = null;
        double near = 60;
        foreach (var f in run.Facilities)
        {
            if (f.Facility != FacilityKind.CoalingTower)
                continue;
            double d = (line.Sample((f.Start + f.End) / 2).Position - at).Length;
            if (d < near)
                (best, near) = (f, d);
        }
        return best;
    }

    // Coaling towers Tower Jaw's brought down this night, by facility, and whether the wreck's been cleared off the line
    // (its creature gone): the scene remembers what the wire's done with.
    readonly Dictionary<int, bool> _towersDown = new();

    /// <summary>
    /// A coaling tower's state for its drawing (note 363): leaning as far as the Tower Jaw gnawing it has got (any of the
    /// enemies near it: its facility isn't on the wire), or down (its wreck the creature's draw), or down and cleared.
    /// </summary>
    (float Lean, Art.TowerDown Down) TowerState(RailLine line, RouteFeature f)
    {
        if (Run is not { } run)
            return (0, Art.TowerDown.Standing);
        int index = -1;
        for (int i = 0; i < run.Facilities.Count; i++)
            if (ReferenceEquals(run.Facilities[i], f))
                index = i;
        var jaw = Enemies?.OfType<Sim.Enemies.TowerJaw>().Where(j => !j.Gone && (j.FacilityIndex == index
                || j.FacilityIndex < 0 && TowerAt(line, j.Local) is { } near && ReferenceEquals(near, f)))
            .FirstOrDefault();
        if (jaw is { Mode: Sim.Enemies.TowerJawMode.Wreck })
        {
            _towersDown[index] = false;
            return (0, Art.TowerDown.Fallen);
        }
        if (_towersDown.ContainsKey(index))
        {
            _towersDown[index] = true;
            return (0, Art.TowerDown.Cleared);
        }
        if (jaw is null)
            return (0, Art.TowerDown.Standing);
        var tune = Look?.Art.Creatures.TowerJawTuning ?? new Sim.Enemies.TowerJawTuning();
        return (Art.CreatureArt.TowerLean(jaw.Gnawed, tune, Time), Art.TowerDown.Standing);
    }

    // What each Gannet was doing when last drawn, since when (scene time), and what it was doing before that.
    readonly Dictionary<int, (Sim.Enemies.GannetMode Mode, double Since, Sim.Enemies.GannetMode? Before)> _gannetModes = new();

    /// <summary>
    /// How long a Gannet has been at what it's doing (its <see cref="Sim.Enemies.GannetMode"/>, s) and what it was doing
    /// before, for its clips (Art/CreatureArt.GannetClip: its fold from the hang, the tear free at the end of its stuck, the
    /// stab out of a dive, the lurch off a pin). Its mode isn't timed on the wire, only its phase, so this watches it change;
    /// first seen (a screenshot, a client joining), its phase's time stands in and what came before isn't known.
    /// </summary>
    (double Since, Sim.Enemies.GannetMode? Before) GannetSince(Sim.Enemies.Gannet g)
    {
        if (!_gannetModes.TryGetValue(g.Id, out var was) || Time < was.Since)
            _gannetModes[g.Id] = was = (g.Mode, Time - g.PhaseSeconds, null);
        else if (was.Mode != g.Mode)
            _gannetModes[g.Id] = was = (g.Mode, Time, was.Mode);
        return (Time - was.Since, was.Before);
    }

    /// <summary>
    /// The Whistler's trail (App. A.4: "findable in a chase on foot"), from the gap it took them at to where it's got to
    /// with them: two heel furrows dragged through the ground, the dirt kicked up dark either side where they fought it,
    /// and now and then something of theirs torn off and left (a scrap of coat, a glove). It follows the land; it's laid
    /// from the creature's id, so every machine's is the same trail.
    /// </summary>
    static void DragTrail(MeshBuilder mesh, RailLine line, Double3 at, Double3 eye, int id)
    {
        double hint = 0;
        var (path, along) = line.Nearest(at, ref hint);
        var rail = line.Sample(path, along);
        var outward = (at - rail.Position) with { Y = 0 };
        double length = outward.Length - TrailStart;
        if (length < 1)
            return;
        var dir = outward.Normalized;
        var side = Double3.Cross(Double3.Up, dir).Normalized;
        var start = rail.Position + dir * TrailStart;
        double g = 0;
        double Ground(Double3 p) => Sim.Player.PlayerMotor.GroundAt(p, line, ref g);
        int n = (int)(length / TrailStep);
        for (int i = 0; i < n; i++)
        {
            uint h = (uint)(i * 2654435761u ^ (uint)id * 40503u);
            double s = i * TrailStep;
            // It ran in a near-straight line; each heel's furrow wanders on its own as they kicked, breaks off where a leg
            // came up, and goes deep and broad where they dug in.
            double wander = Math.Sin(s * 0.7 + id) * 0.12;
            var c = start + dir * (s + TrailStep / 2) + side * wander;
            c = c with { Y = Ground(c) + 0.012 };
            float fade = (float)Math.Min(1, (length - s) / 3 + 0.35);
            foreach (int heel in (ReadOnlySpan<int>)[-1, 1])
            {
                uint k = h ^ (uint)(heel + 2) * 0x9E3779B9u;
                if (k % 7 == 0)
                    continue;
                double dx = heel * (0.13 + Math.Sin(s * 1.9 + heel * 2.3 + id) * 0.05);
                var p = c + side * dx;
                p = p with { Y = Ground(p) + 0.012 };
                float width = 0.035f + (k >> 3) % 4 * 0.012f, len = (float)TrailStep * (0.38f + (k >> 6) % 3 * 0.07f);
                float lean = (float)Math.Sin(s * 2.7 + heel) * 0.12f;
                var r = Vector3.Transform(ToF(side), Quaternion.CreateFromAxisAngle(Vector3.UnitY, lean));
                var b = Vector3.Transform(ToF(dir), Quaternion.CreateFromAxisAngle(Vector3.UnitY, lean));
                mesh.Box(V(p, eye), r, Vector3.UnitY, b, new Vector3(width, 0.012f, len), Palette.TrailFurrow * fade);
            }
            // Where they fought it: the ground churned in a broad scuff across both furrows.
            if (h % 11 == 3)
            {
                float turn = (h >> 12) % 7 * 0.4f;
                var r = Vector3.Transform(ToF(side), Quaternion.CreateFromAxisAngle(Vector3.UnitY, turn));
                var b = Vector3.Transform(ToF(dir), Quaternion.CreateFromAxisAngle(Vector3.UnitY, turn));
                var p = c with { Y = Ground(c) + 0.008 };
                mesh.Box(V(p, eye), r, Vector3.UnitY, b, new Vector3(0.42f, 0.008f, 0.3f), Palette.TrailFurrow * 1.4f);
            }
            // The kicked-up earth: dark clods thrown out to either side, more where they fought it.
            if (h % 3 == 0)
            {
                double x = ((h >> 4) % 2 == 0 ? -1 : 1) * (0.35 + (h >> 6) % 5 * 0.06);
                var p = c + side * x + dir * (((h >> 9) % 5 - 2) * 0.1);
                p = p with { Y = Ground(p) + 0.02 };
                mesh.Box(V(p, eye), ToF(side), Vector3.UnitY, ToF(dir), new Vector3(0.09f, 0.03f, 0.07f), Palette.TrailFurrow * 1.25f);
            }
            // Torn off and dropped: a scrap of the coat's cloth or a glove, every dozen metres or so.
            if (h % 23 == 5 && s > 4)
            {
                double x = ((h >> 5) % 2 == 0 ? -1 : 1) * 0.6;
                var p = c + side * x;
                p = p with { Y = Ground(p) + 0.015 };
                float turn = (h >> 11) % 6 * 0.5f;
                var r = Vector3.Transform(ToF(side), Quaternion.CreateFromAxisAngle(Vector3.UnitY, turn));
                var b = Vector3.Transform(ToF(dir), Quaternion.CreateFromAxisAngle(Vector3.UnitY, turn));
                mesh.Box(V(p, eye), r, Vector3.UnitY, b, (h >> 7) % 2 == 0 ? new Vector3(0.16f, 0.01f, 0.11f) : new Vector3(0.05f, 0.025f, 0.1f),
                    (h >> 7) % 2 == 0 ? Palette.TrailCloth : Palette.TrailGlove);
            }
        }
    }

    // The trail starts off the ballast's shoulder (the stone takes no mark), in strides of this along it.
    const double TrailStart = 2.6, TrailStep = 0.7;

    /// <summary>
    /// Greybox stand-ins, each readable by silhouette and by its telegraph (App. A.1: the tell must be
    /// perceivable). The real creatures come with the art pass; these exist to make pacing watchable.
    /// </summary>
    static void DrawEnemy(MeshBuilder mesh, RailLine line, IReadOnlyList<CarFrame> frames, Enemy e, Double3 eye, double from, double to, Art.CreatureArt? creatures = null,
        Art.Bite bite = default, Art.CreatureArt.Prey? prey = null, Art.CreatureArt.Room? room = null, float pace = 0,
        (Vector3 Push, Quaternion Tip) flinch = default, double hitAge = -1, bool dying = false, float roll = 0, double modeSeconds = -1,
        (Double3 Origin, Double3 Right)? place = null)
    {
        // A basis for the enemy: its car's, or the line's at its distance.
        Double3 origin, right, up = Double3.Up, back;
        if (place is { } placed)
        {
            // Put where it lies (Tower Jaw's tower across the line, note 363): +X its way out.
            origin = placed.Origin;
            right = placed.Right;
            back = Double3.Cross(right, Double3.Up).Normalized;
        }
        else if (e.Kind is EnemyKind.Moose or EnemyKind.Gannet or EnemyKind.Mourners or EnemyKind.FreightBeetle or EnemyKind.TowerJaw && e.Attached == Enemy.Loose)
        {
            // The Moose goes its own way (note 339): it faces its heading (Moose.Yaw, a player's yaw: −Z at 0), not the train.
            // So does a Gannet down on someone on the ground (note 340), and the Mourners, the Freight Beetle and Tower Jaw
            // (their Lateral; notes 362, 366, 363).
            origin = e.Local;
            back = new Double3(Math.Sin(e.Lateral), 0, Math.Cos(e.Lateral));
            right = Double3.Cross(Double3.Up, back).Normalized;
        }
        else if (e.Kind == EnemyKind.Gannet && e.Attached >= 0)
        {
            // The Gannet rides in a car's moving air (note 340): it faces its heading in that car's frame (its Lateral, a
            // player's yaw there). Stood on someone on a roof, it's turned along the car, the long way, so who it has lies
            // along the roof.
            if (e.Attached >= frames.Count)
                return;
            var f = frames[e.Attached];
            double yaw = e is Sim.Enemies.Gannet { Mode: Sim.Enemies.GannetMode.Pin } ? (Math.Cos(e.Lateral) >= 0 ? 0 : Math.PI) : e.Lateral;
            origin = f.ToWorld(e.Local);
            up = f.Up;
            back = f.Right * Math.Sin(yaw) + f.Back * Math.Cos(yaw);
            right = f.Right * Math.Cos(yaw) - f.Back * Math.Sin(yaw);
        }
        else if (e.Attached >= 0)
        {
            if (e.Attached >= frames.Count)
                return;
            var f = frames[e.Attached];
            var local = e.Local;
            // Fire Flies are on a lamp: the sim has them at the car's (one lamp each, Vehicle.LampLit); drawn, they're on
            // the nearer of its two lanterns (Art/SceneArt), round its flame.
            if (e.Kind == EnemyKind.FireFlies && creatures is not null && f.Shape.Interior is { } lit)
            {
                var near = Art.SceneArt.LampPositions(lit).MinBy(p => Math.Abs(p.Z - local.Z));
                local = new Double3(near.X, near.Y, near.Z);
            }
            origin = f.ToWorld(local);
            (right, up, back) = (f.Right, f.Up, f.Back);
            // A hound aboard faces the way the sim has it in its car (note 472): up or down the car, or to a side door. Its
            // stand-in's head is at −Z, so turned as the art turns its model (CreatureArt), a player's yaw in the car's frame.
            if (e is Sim.Enemies.CinderHound hound)
            {
                double yaw = hound.Facing switch { 1 => Math.PI, 2 => -Math.PI / 2, 3 => Math.PI / 2, _ => 0 };
                back = f.Right * Math.Sin(yaw) + f.Back * Math.Cos(yaw);
                right = f.Right * Math.Cos(yaw) - f.Back * Math.Sin(yaw);
            }
        }
        else if (e.Attached == Enemy.Loose)
        {
            // Stood free in the world (on the ground beside the train, in the air over it): facing the nearest car, since
            // what's loose out there has come for the train.
            origin = e.Local;
            var near = frames.Count == 0 ? origin : frames.MinBy(f => (f.ToWorld(default) - origin).Length)!.ToWorld(default);
            var away = new Double3(origin.X - near.X, 0, origin.Z - near.Z);
            back = away.Length > 1e-6 ? away.Normalized : new Double3(0, 0, 1);
            right = Double3.Cross(Double3.Up, back).Normalized;
            // The Whistler carrying its catch off (App. A.4 GRAB): over the land, not at the rail's height it took them
            // at, and the way it went torn into the ground behind it, to be followed on foot.
            if (e.Kind == EnemyKind.Whistler && e.Phase is SpinePhase.Grab or SpinePhase.Punish && creatures is not null)
            {
                double hint = e.LineDistance;
                origin = origin with { Y = Sim.Player.PlayerMotor.GroundAt(origin, line, ref hint) };
                DragTrail(mesh, line, origin, eye, e.Id);
                // Its nest is straight out from the gap it took them at (Sim.Enemies.Whistler): it faces square off the line,
                // not off the nearest car's middle (a gap's at a car's end).
                if (frames.Count > 0)
                {
                    var car = frames.MinBy(f => (f.ToWorld(default) - origin).Length)!;
                    var side = car.DirToWorld(new Double3(1, 0, 0)) with { Y = 0 };
                    if (side.Length > 1e-6)
                    {
                        side = side.Normalized;
                        back = Double3.Dot(away, side) >= 0 ? side : side * -1;
                        right = Double3.Cross(Double3.Up, back).Normalized;
                    }
                }
            }
        }
        else
        {
            if (e.LineDistance < from || e.LineDistance > to)
            {
                return;
            }
            var t = line.Sample(e.LineDistance);
            right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
            back = t.Tangent * -1;
            origin = t.Position + right * e.Lateral + Double3.Up * e.Height;
        }
        var o = V(origin, eye);
        var (r, u, b) = (ToF(right), ToF(up), ToF(back));
        // Killed (Deaths): rolled over onto its side about its own length, away from the blow (a biped falls sideways, a
        // crawler goes over on its back's edge), and pushed along it.
        if (dying && roll != 0)
        {
            float away = Vector3.Dot(new Vector3(flinch.Push.X, 0, flinch.Push.Z), r) >= 0 ? -1 : 1;
            flinch = (flinch.Push, Quaternion.CreateFromAxisAngle(Vector3.Normalize(b), roll * away));
        }
        // Flinching from a blow (T121): knocked back and tipped away from it, about its feet. (Going, Deaths: carried off.)
        o += flinch.Push;
        if (flinch.Tip != default && flinch.Tip != Quaternion.Identity)
            (r, u, b) = (Vector3.Transform(r, flinch.Tip), Vector3.Transform(u, flinch.Tip), Vector3.Transform(b, flinch.Tip));
        // The art pass's creature, where it has one (Art/CreatureArt): the same place, the thing itself.
        if (creatures is not null && creatures.Enemy(mesh, Art.CreatureArt.Basis(o, r, u, b), e, bite, prey, room, pace, hitAge, dying, modeSeconds))
            return;
        // The dead are the art pass's (Deaths): the greybox's boxes don't fall over.
        if (dying)
            return;
        Vector3 L(double x, double y, double z) => o + r * (float)x + u * (float)y + b * (float)z;
        void Draw(double x, double y, double z, double hx, double hy, double hz, Vector3 colour) =>
            mesh.Box(L(x, y, z), r, u, b, new Vector3((float)hx, (float)hy, (float)hz), colour);
        float pulse = (float)(0.5 + 0.5 * Math.Sin(e.PhaseSeconds * 9));

        if (Art.IncidentArt.Draw(mesh, o, r, u, b, e.Kind, e.Phase, e.PhaseSeconds, e.Extra, e.Health))
            return;
        switch (e.Kind)
        {
            case EnemyKind.Mourners:
                {
                    // A small grey hunch with heavy shoulders and long arms down to the ground (note 362); leant back, the arms
                    // out in front, dragging.
                    bool hauling = e is Sim.Enemies.Mourner { Mode: Sim.Enemies.MournerMode.Drag };
                    Draw(0, 0.3, 0, 0.08, 0.3, 0.08, Palette.BoardEnamel * 0.6f);
                    Draw(0, 0.75, hauling ? 0.08 : 0, 0.22, 0.17, 0.14, Palette.BoardEnamel * 0.7f);
                    Draw(0, 0.88, -0.1, 0.07, 0.07, 0.07, Palette.BoardEnamel * 0.55f);
                    foreach (double x in new[] { -0.24, 0.24 })
                        Draw(x, hauling ? 0.55 : 0.45, hauling ? -0.4 : -0.1, 0.05, hauling ? 0.05 : 0.3, hauling ? 0.3 : 0.05, Palette.BoardEnamel * 0.6f);
                    break;
                }
            case EnemyKind.FreightBeetle:
                {
                    // A domed back like a painted crate, the shovel out in front, six legs (note 366).
                    Draw(0, 0.85, 0.25, 0.7, 0.5, 0.95, Palette.TarnishedBrass);
                    Draw(0, 0.55, -1.0, 0.36, 0.06, 0.15, Palette.IronGrey);
                    foreach (double x in new[] { -0.85, 0.85 })
                        foreach (double z in new[] { -0.8, 0.0, 0.8 })
                            Draw(x, 0.3, z, 0.06, 0.3, 0.06, Palette.DeepBrown);
                    break;
                }
            case EnemyKind.TowerJaw when e is Sim.Enemies.TowerJaw { Mode: Sim.Enemies.TowerJawMode.Wreck }:
                // Its tower down across the line (note 363): a heap of timber over the rails.
                Draw(0, 0.6, 0, 6, 0.6, 2.5, Palette.DeepBrown);
                break;
            case EnemyKind.TowerJaw:
                {
                    // A dark hump bigger than a man with a ridge of spines, and two long teeth (note 363).
                    Draw(0, 0.9, 1.0, 0.55, 0.65, 1.0, Palette.DeepBrown * 0.6f);
                    Draw(0, 1.7, 1.0, 0.1, 0.3, 0.8, Palette.SootBlack);
                    Draw(0, 0.8, -0.6, 0.3, 0.25, 0.3, Palette.DeepBrown * 0.5f);
                    foreach (double x in new[] { -0.05, 0.05 })
                        Draw(x, 0.45, -0.85, 0.03, 0.2, 0.02, Palette.IronGrey);
                    break;
                }
            case EnemyKind.Brakeman or EnemyKind.Knotter or EnemyKind.Hotbox:
                TrainfolkBoxes(e, Draw);
                break;
            case EnemyKind.Gannet:
                {
                    // A pale cross 7 m across with a spear for a head (note 340): wings spread flying, a dart diving, the
                    // wings down round it on a roof; nothing when it's gone off.
                    var mode = (e as Sim.Enemies.Gannet)?.Mode ?? Art.CreatureArt.GannetModeOf(e.Phase);
                    if (mode == Sim.Enemies.GannetMode.Away)
                        break;
                    var pale = Palette.BoardEnamel;
                    bool down = mode is Sim.Enemies.GannetMode.Stuck or Sim.Enemies.GannetMode.Pin;
                    if (mode == Sim.Enemies.GannetMode.Fold)
                    {
                        Draw(0, 2.3, 0, 0.35, 1.3, 0.35, pale);
                        Draw(0, 0.5, 0, 0.06, 0.5, 0.06, Palette.Charcoal);
                        Draw(0, 3.2, 0, 0.9, 0.9, 0.08, Palette.SootBlack);
                        break;
                    }
                    Draw(0, 1.05, 0, 0.45, 0.42, 0.85, pale);
                    Draw(0, 1.5, -1.3, 0.12, 0.12, 0.5, Palette.HazardYellow);
                    Draw(0, 1.45, -2.2, 0.05, 0.05, 0.5, Palette.BoardEnamel * 0.8f);
                    foreach (double x in new[] { -2.2, 2.2 })
                        Draw(x, down ? 0.9 : 1.2, 0, 1.3, down ? 0.5 : 0.04, 0.4, x < 0 ? pale : pale * 0.9f);
                    foreach (double x in new[] { -3.2, 3.2 })
                        Draw(x, down ? 0.3 : 1.2, 0.1, 0.3, down ? 0.3 : 0.03, 0.3, Palette.SootBlack);
                    break;
                }
            case EnemyKind.Moose:
                {
                    // A pale bulk on long legs under a slab of a rack wider than a doorway (note 339): the head up listening,
                    // down warning, and the rack stood up level in front of it squaring up and charging.
                    var hide = Palette.BlueGrey * 1.5f;
                    bool levelled = e.Phase is SpinePhase.Telegraph or SpinePhase.Commit or SpinePhase.Grab or SpinePhase.Punish;
                    var tells = creatures?.MooseTuning ?? new Sim.Enemies.MooseTuning();
                    double head = levelled || e.Extra2 >= tells.WarnAt ? 1.1 : e.Extra2 >= tells.ListenAt ? 2.15 : 1.0;
                    Draw(0, 1.78, 0, 0.42, 0.45, 1.1, hide);
                    Draw(0, 2.4, -0.8, 0.3, 0.22, 0.32, hide);
                    foreach (double x in new[] { -0.28, 0.28 })
                        foreach (double z in new[] { -0.8, 0.85 })
                            Draw(x, 0.68, z, 0.07, 0.68, 0.07, hide * 0.8f);
                    Draw(0, (head + 2.2) / 2, -1.3, 0.16, Math.Abs(2.2 - head) / 2 + 0.15, 0.18, hide);
                    Draw(0, head, -1.7, 0.14, 0.18, 0.35, hide);
                    foreach (double x in new[] { -0.95, 0.95 })
                        if (levelled)
                            Draw(x, head + 0.4, -1.85, 0.62, 0.42, 0.05, Palette.Charcoal);
                        else
                            Draw(x, head + 0.55, -1.45, 0.62, 0.05, 0.38, Palette.Charcoal);
                    break;
                }
            case EnemyKind.Sleepers:
                // Ties that aren't: across the rail, and braced (lifting, writhing) once they've telegraphed.
                bool braced = e.Phase >= SpinePhase.Telegraph;
                for (int i = 0; i < 6; i++)
                {
                    double lift = braced ? 0.12 + 0.08 * Math.Sin(e.PhaseSeconds * 6 + i) : 0;
                    Draw(0, lift, -i * 2.6, 1.5, 0.14, 0.45, Palette.Corrupted);
                }
                break;
            case EnemyKind.CinderHound:
                // Low, long, and lit from inside: the heat they hunt by is what you see of them at night.
                bool aboard = e.Attached >= 0;
                double y = e.Attached >= 0 ? 0.35 : 0; // boarded: standing on the roof, not centred in it
                Draw(0, y, 0, 0.22, 0.28, 0.65, Palette.SootBlack);
                Draw(0, y + 0.2, -0.75, 0.16, 0.16, 0.25, Palette.SootBlack);
                mesh.Emissive = 1;
                // Cracks of ember along the flanks and back, proud of the body so they read from any side.
                Draw(0, y + 0.02, 0, 0.24, 0.1, 0.45, Palette.FurnaceOrange * (0.5f + 0.5f * (aboard ? pulse : 0.7f)));
                Draw(0, y + 0.29, 0.1, 0.08, 0.02, 0.4, Palette.FurnaceOrange * 0.8f);
                Draw(0.07, y + 0.26, -1.01, 0.03, 0.03, 0.02, Palette.LampAmber);
                Draw(-0.07, y + 0.26, -1.01, 0.03, 0.03, 0.02, Palette.LampAmber);
                mesh.Emissive = 0;
                break;
            case EnemyKind.Dragger when e.Phase is SpinePhase.Telegraph or SpinePhase.Grab:
                {
                    // Out of sight under the edge until it reaches (App. A.4): then a long limb comes up just outside the eave
                    // and hooks in over the roof, rising through the telegraph's second; grabbing, two of them, further in.
                    // Corrupted flesh, pale: the one light-coloured thing at a car's dark edge, so the reach reads in time.
                    double inward = -Math.Sign(e.Local.X);
                    double rise = e.Phase == SpinePhase.Grab ? 1 : Math.Clamp(e.PhaseSeconds / 1.0, 0.2, 1);
                    int limbs = e.Phase == SpinePhase.Grab ? 2 : 1;
                    var flesh = Palette.Corrupted * 1.7f;
                    double outside = -inward * 0.14;
                    for (int i = 0; i < limbs; i++)
                    {
                        double z = (i - (limbs - 1) * 0.5) * 0.4;
                        double top = -0.1 + 0.8 * rise;
                        Draw(outside, (top - 0.4) * 0.5, z, 0.08, (top + 0.4) * 0.5, 0.08, flesh);
                        double reach = 0.3 + (e.Phase == SpinePhase.Grab ? 0.3 : 0.12) * rise;
                        Draw(outside + inward * reach * 0.5, top, z, reach * 0.5 + 0.04, 0.06, 0.07, flesh);
                        // Fingers splayed on the roof sheet.
                        Draw(outside + inward * (reach + 0.08), top - 0.04, z, 0.09, 0.03, 0.12, Palette.Corrupted * 1.3f);
                    }
                    break;
                }
            case EnemyKind.Switchman:
                {
                    // A railwayman, stooped wrong, standing at the switch with a lantern held out: the lantern's the only
                    // light on it, and it swings while it waits for the train.
                    Draw(0, 0.45, 0, 0.12, 0.45, 0.12, Palette.SootBlack);
                    Draw(0, 1.2, 0.05, 0.22, 0.35, 0.14, Palette.SootBlack);
                    Draw(0, 1.62, -0.12, 0.13, 0.13, 0.13, Palette.Corrupted);
                    double swing = e.Phase == SpinePhase.Telegraph ? 0.15 * Math.Sin(e.PhaseSeconds * 2.2) : 0;
                    Draw(0.32, 1.0, -0.1 + swing, 0.05, 0.28, 0.05, Palette.SootBlack);
                    mesh.Emissive = 1;
                    Draw(0.32, 0.7, -0.1 + swing, 0.08, 0.1, 0.08, Palette.LampAmber);
                    mesh.Emissive = 0;
                    break;
                }
            case EnemyKind.Drift:
                {
                    // A dark spreading mat over the roof and down the sides, as wide as it's spread.
                    double spread = Math.Clamp(e.Extra, 1, 12);
                    Draw(0, 0.05, 0, spread * 0.7, 0.05, spread * 0.7, Palette.SootBlack);
                    Draw(0, 0.15, 0, spread * 0.4, 0.1, spread * 0.4, Palette.Corrupted * 0.4f);
                    break;
                }
            case EnemyKind.Follower:
                {
                    // Low and bent at someone's back, matching their step; nested, a heap in the car's dark corner.
                    double low = e.Phase == SpinePhase.Punish ? -0.3 : 0;
                    Draw(0, low + 0.35, 0, 0.14, 0.35, 0.14, Palette.SootBlack);
                    Draw(0, low + 0.85, -0.2, 0.2, 0.22, 0.26, Palette.SootBlack);
                    Draw(0, low + 1.0, -0.45, 0.1, 0.1, 0.1, Palette.Corrupted * 0.6f);
                    break;
                }
            case EnemyKind.Gaunt:
                // Tall and thin on the roof, dead still.
                Draw(0, 0.8, 0, 0.09, 0.8, 0.09, Palette.SootBlack);
                Draw(0, 2.0, 0, 0.16, 0.45, 0.1, Palette.SootBlack);
                Draw(0, 2.6, -0.05, 0.1, 0.13, 0.1, Palette.Corrupted * 0.5f);
                break;
            case EnemyKind.Climber:
                {
                    // Thin, soot-black, bent over; in the cab it stands on the deck (the origin is the cab's centre).
                    double y0 = 0;
                    double lean = e.Phase == SpinePhase.Telegraph ? 0.25 * Math.Sin(e.PhaseSeconds * 14) : 0;
                    Draw(0, y0 + 0.45, 0, 0.1, 0.45, 0.1, Palette.SootBlack);
                    Draw(0, y0 + 1.15, -0.15 + lean, 0.18, 0.32, 0.12, Palette.SootBlack);
                    Draw(0, y0 + 1.45, -0.35 + lean, 0.1, 0.1, 0.1, Palette.Corrupted * 0.6f);
                    break;
                }
            case EnemyKind.SootChildren:
                {
                    // One small figure crouched out in the dark; a Soot Child's eyes are black (Extra2), a real child's aren't.
                    Draw(0, 0.28, 0, 0.16, 0.28, 0.14, Palette.SootBlack);
                    Draw(0, 0.66, -0.05, 0.1, 0.1, 0.1, Palette.Corrupted * 0.7f);
                    var eyes = e.Extra2 > 0.5 ? Palette.SootBlack : Palette.BoardEnamel;
                    Draw(0.035, 0.68, -0.16, 0.015, 0.012, 0.01, eyes);
                    Draw(-0.035, 0.68, -0.16, 0.015, 0.012, 0.01, eyes);
                    break;
                }
            case EnemyKind.Stoker or EnemyKind.CarFire:
                break;
            default:
                // A figure, where the greybox has nothing of its own for it: tall, dark, and a pale head.
                Draw(0, 0.6, 0, 0.14, 0.6, 0.12, Palette.SootBlack);
                Draw(0, 1.35, 0, 0.1, 0.12, 0.1, Palette.Corrupted * 0.6f);
                break;
        }
    }

    /// <summary>Finds the along-line distance nearest a point, starting from a guess.</summary>
    public static double NearestDistance(RailLine line, Double3 p, double guess)
    {
        double s = guess;
        for (int i = 0; i < 8; i++)
        {
            var sample = line.Sample(s);
            s = Math.Clamp(sample.Distance + Double3.Dot(p - sample.Position, sample.Tangent), 0, line.Length);
        }
        return s;
    }

    static Vector3 V(Double3 p, Double3 eye) => p.RelativeTo(eye);

    /// <summary>Which of <paramref name="breaks"/> someone's wrench is at now (note 301): its callout showers sparks off each strike.</summary>
    public static HashSet<int>? MendingAt(IReadOnlyList<BreakCallout> breaks, IEnumerable<Sim.Player.PlayerState> crew, TrainOnLine train)
    {
        HashSet<int>? at = null;
        foreach (var s in crew)
        {
            if (s.ActionProgress <= 0 || !Repairs.WrenchInHand(s) || Repairs.At(s, train) is not (var kind and not BreakKind.None))
                continue;
            int car = kind is BreakKind.Rupture or BreakKind.Lamp ? 0 : s.Parent;
            for (int i = 0; i < breaks.Count; i++)
                if (breaks[i].Kind == kind && breaks[i].Vehicle == car)
                    (at ??= []).Add(i);
        }
        return at;
    }

    /// <summary>
    /// A car the film cuts away (E.4 O2) keeps its floor: someone tumbling inside it is seen lying in a car with its shell
    /// lifted off, not on the track under nothing (note 251's "a cutaway that keeps the floor").
    /// </summary>
    static void CutFloor(MeshBuilder mesh, in CarFrame frame, Double3 eye)
    {
        var shape = frame.Shape;
        // The film's floor for it (World.Derail): a walk-in car's, or the engine's cab's.
        double floor = (shape.Interior ?? shape.Cab)?.Min.Y ?? 0;
        if (floor <= 0.05)
            return;
        var (right, up, back) = (ToF(frame.Right), ToF(frame.Up), ToF(frame.Back));
        mesh.Box(V(frame.ToWorld(new Double3(0, floor / 2, 0)), eye), right, up, back,
            new Vector3((float)shape.HalfWidth, (float)floor / 2, (float)shape.HalfLength), Palette.DeepBrown);
        // A sill along each side, so it reads as a car's floor and not a plank.
        foreach (int side in new[] { -1, 1 })
            mesh.Box(V(frame.ToWorld(new Double3(side * (shape.HalfWidth - 0.05), floor + 0.1, 0)), eye), right, up, back,
                new Vector3(0.05f, 0.1f, (float)shape.HalfLength), Palette.RustRed);
    }

    void Track(MeshBuilder mesh, RailLine line, Double3 eye, double from, double to, double centre)
    {
        if (Look is not null)
        {
            // The art pass's line (WorldArt): the ground, the track and the lineside, cooked in cells. Nothing wild grows
            // inside the fortresses' walls (T100): they're told where those are before a cell's built.
            if (Route is not null)
                Look.Art.World.Walls = (Run?.YardLength ?? 600, Route.Plan?.Terminus.GateM ?? line.Length - (Run?.Tuning.TerminusZone ?? 400) - 200);
            Look.Art.World.TownSquare = Town?.Plan.Square;
            Look.Art.World.TownBounds = Town?.Plan.Bounds;
            Look.Art.World.Cells(mesh, line, Route, eye, from, to, Seed, (float)ValleyDepth);
            return;
        }
        const double step = 5, gauge = 0.72, sleeperPitch = 0.75;
        const double groundHalfWidth = 90, bedHalfWidth = 1.8;
        for (double s = from; s < to; s += step)
        {
            var a = line.Sample(s);
            var b = line.Sample(Math.Min(s + step, to));
            var ra = Double3.Cross(a.Tangent, Double3.Up).Normalized;
            var rb = Double3.Cross(b.Tangent, Double3.Up).Normalized;
            var down = new Double3(0, -0.02, 0);
            bool bridge = Route?.BridgeAt(s + step / 2) is not null;

            // Ground either side, then the raised ballast bed, then rails. Under a bridge the ground
            // drops into a valley and there's no ballast: the deck is drawn with the features.
            var valley = bridge ? new Double3(0, -ValleyDepth, 0) : default;
            double inner = bridge ? 0 : bedHalfWidth;
            mesh.Quad(V(a.Position - ra * groundHalfWidth + down * 10 + valley, eye), V(a.Position - ra * inner + down + valley, eye),
                      V(b.Position - rb * inner + down + valley, eye), V(b.Position - rb * groundHalfWidth + down * 10 + valley, eye), Palette.MuddyOlive);
            mesh.Quad(V(a.Position + ra * inner + down + valley, eye), V(a.Position + ra * groundHalfWidth + down * 10 + valley, eye),
                      V(b.Position + rb * groundHalfWidth + down * 10 + valley, eye), V(b.Position + rb * inner + down + valley, eye), Palette.MuddyOlive);
            if (!bridge)
                mesh.Quad(V(a.Position - ra * bedHalfWidth, eye), V(a.Position + ra * bedHalfWidth, eye),
                          V(b.Position + rb * bedHalfWidth, eye), V(b.Position - rb * bedHalfWidth, eye), Palette.Ballast);
            foreach (var side in new[] { -1.0, 1.0 })
            {
                var p0 = V(a.Position + ra * (side * gauge), eye);
                var p1 = V(b.Position + rb * (side * gauge), eye);
                var mid = (p0 + p1) / 2;
                var fwd = Vector3.Normalize(p1 - p0);
                var right = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitY));
                mesh.Box(mid + new Vector3(0, 0.12f, 0), right, Vector3.UnitY, -fwd, new Vector3(0.04f, 0.07f, (p1 - p0).Length() / 2), Palette.IronGrey);
            }
        }
        // Sleepers only near the eye: past ~150 m the fog has eaten them anyway.
        double sleeperFrom = Math.Max(from, centre - 150), sleeperTo = Math.Min(to, centre + 150);
        for (double s = Math.Ceiling(sleeperFrom / sleeperPitch) * sleeperPitch; s < sleeperTo; s += sleeperPitch)
        {
            var t = line.Sample(s);
            var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
            mesh.Box(V(t.Position, eye) + new Vector3(0, 0.03f, 0), ToF(right), Vector3.UnitY, ToF(t.Tangent * -1), new Vector3(1.25f, 0.05f, 0.12f), Palette.DeepBrown);
        }
    }

    void Lineside(MeshBuilder mesh, RailLine line, Double3 eye, double from, double to)
    {
        if (Look is not null)
        {
            return; // cooked with the track (WorldArt.Cells)
        }
        // Telegraph poles every 50 m and a scatter of pines: depth cues for fog and speed.
        for (double s = Math.Ceiling(from / 50) * 50; s < to; s += 50)
        {
            if (Route is not null && (Route.InTunnel(s) || Route.BridgeAt(s) is not null))
                continue;
            var t = line.Sample(s);
            var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
            var foot = V(t.Position + right * 4.5, eye);
            mesh.AxisBox(foot + new Vector3(-0.12f, 0, -0.12f), foot + new Vector3(0.12f, 7.5f, 0.12f), Palette.DeepBrown);
            mesh.AxisBox(foot + new Vector3(-0.9f, 6.8f, -0.06f), foot + new Vector3(0.9f, 6.95f, 0.06f), Palette.DeepBrown);
        }
        for (double s = Math.Floor(from / 12) * 12; s < to; s += 12)
        {
            if (Route is not null && (Route.InTunnel(s) || Route.BridgeAt(s) is not null))
                continue;
            // A fixed hash, not HashCode.Combine: that one's seeded afresh every process, and the pines would move.
            var rng = new Random(unchecked(Seed * 73856093 ^ (int)(s / 12) * 19349663));
            mesh.Seed = (float)(s / 12 % 997);
            for (int k = 0; k < 3; k++)
            {
                double side = rng.Next(2) == 0 ? -1 : 1;
                double offset = side * (9 + rng.NextDouble() * 70);
                double along = s + rng.NextDouble() * 12;
                // Not on a branch: clear of a dead line's track, and out of a facility's yard beside its spur.
                if (line.Branches.Any(b => along > b.Toe - 20 && along < b.End + 20 && Math.Sign(offset) == b.Side
                        && Math.Abs(offset) < (b.Kind == BranchKind.Spur ? 60 : 16)))
                    continue;
                var t = line.Sample(along);
                var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
                var foot = V(t.Position + right * offset + new Double3(0, -0.2, 0), eye);
                float h = 7 + (float)rng.NextDouble() * 9;
                mesh.AxisBox(foot + new Vector3(-0.2f, 0, -0.2f), foot + new Vector3(0.2f, h * 0.35f, 0.2f), Palette.DeepBrown);
                mesh.Pyramid(foot + new Vector3(0, h * 0.25f, 0), h * 0.22f, h * 0.8f, Palette.PineDark);
            }
        }
    }

    /// <summary>
    /// A branch off the main line (GDD §17, App. A.7): its own ballast, rails and sleepers from the points, the
    /// switch stand beside them with its target lamp (green set for the main line, red for the branch: from the cab,
    /// the lamp is how you read a switch before you're on it), and a buffer stop at the far end.
    /// </summary>
    void Branch(MeshBuilder mesh, RailLine main, Branch branch, Double3 eye)
    {
        if (Look is not null)
        {
            var at = (Stands ?? DefaultStands).LeverAt(main, branch.Index);
            // The Switchman gripping this lever to throw it under the train (App. A.8): the lamp flickers.
            bool gripped = Enemies?.Any(e => e is Sim.Enemies.Switchman { Gripping: true } s && s.Branch == branch.Index && !e.Gone) == true;
            float flicker = gripped ? (MathF.Sin((float)Math.Floor(Time * 14) * 12.9898f) * 43758.5f % 1 is var j && MathF.Abs(j) < 0.45f ? 0.08f : 1) : 1;
            Look.Art.World.Branch(mesh, branch, eye, DrawDistance, at, main.Sample(branch.Toe).Tangent, Diverging?.Invoke(branch.Index) ?? false, flicker);
            return;
        }
        const double step = 5, gauge = 0.72, sleeperPitch = 0.75, bedHalfWidth = 1.8, start = 4;
        var local = branch.Local;
        for (double s = start; s < local.Length; s += step)
        {
            var a = local.Sample(s);
            var b = local.Sample(Math.Min(s + step, local.Length));
            if ((a.Position - eye).Length > DrawDistance)
                continue;
            var ra = Double3.Cross(a.Tangent, Double3.Up).Normalized;
            var rb = Double3.Cross(b.Tangent, Double3.Up).Normalized;
            var up = new Double3(0, 0.01, 0); // over the main line's bed where the two still overlap
            mesh.Quad(V(a.Position - ra * bedHalfWidth + up, eye), V(a.Position + ra * bedHalfWidth + up, eye),
                      V(b.Position + rb * bedHalfWidth + up, eye), V(b.Position - rb * bedHalfWidth + up, eye), Palette.Ballast);
            // Its shoulders run down into the ground: the main line's ground falls away from it, so out here the
            // branch sits on a low embankment of its own.
            var shoulder = new Double3(0, -0.35, 0);
            mesh.Quad(V(a.Position + ra * bedHalfWidth + up, eye), V(a.Position + ra * (bedHalfWidth + 0.8) + shoulder, eye),
                      V(b.Position + rb * (bedHalfWidth + 0.8) + shoulder, eye), V(b.Position + rb * bedHalfWidth + up, eye), Palette.Ballast);
            mesh.Quad(V(a.Position - ra * (bedHalfWidth + 0.8) + shoulder, eye), V(a.Position - ra * bedHalfWidth + up, eye),
                      V(b.Position - rb * bedHalfWidth + up, eye), V(b.Position - rb * (bedHalfWidth + 0.8) + shoulder, eye), Palette.Ballast);
            foreach (var side in new[] { -1.0, 1.0 })
            {
                var p0 = V(a.Position + ra * (side * gauge), eye);
                var p1 = V(b.Position + rb * (side * gauge), eye);
                var fwd = Vector3.Normalize(p1 - p0);
                var right = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitY));
                mesh.Box((p0 + p1) / 2 + new Vector3(0, 0.12f, 0), right, Vector3.UnitY, -fwd, new Vector3(0.04f, 0.07f, (p1 - p0).Length() / 2), Palette.IronGrey);
            }
        }
        for (double s = start; s < local.Length; s += sleeperPitch)
        {
            var t = local.Sample(s);
            if ((t.Position - eye).Length > 150)
                continue;
            var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
            mesh.Box(V(t.Position, eye) + new Vector3(0, 0.035f, 0), ToF(right), Vector3.UnitY, ToF(t.Tangent * -1), new Vector3(1.25f, 0.05f, 0.12f), Palette.DeepBrown);
        }

        // The buffer stop: a timber-and-iron block across the rails, with a red lamp on it.
        var end = local.Sample(local.Length);
        // An alternate has no end of its own: it runs back into the main line (linegen plan §6.2).
        if (!branch.Rejoins && (end.Position - eye).Length < DrawDistance)
        {
            var right = Double3.Cross(end.Tangent, Double3.Up).Normalized;
            mesh.Box(V(end.Position + Double3.Up * 0.6, eye), ToF(right), Vector3.UnitY, ToF(end.Tangent * -1), new Vector3(1.3f, 0.6f, 0.4f), Palette.RustRed);
            mesh.Emissive = 1;
            mesh.Box(V(end.Position + Double3.Up * 1.35, eye), ToF(right), Vector3.UnitY, ToF(end.Tangent * -1), new Vector3(0.12f, 0.12f, 0.12f), Palette.SignalRed);
            mesh.Emissive = 0;
        }

        // The stand at the points: a post, the throw lever at hand height, and the target lamp on top.
        var lever = (Stands ?? DefaultStands).LeverAt(main, branch.Index);
        if ((lever - eye).Length > DrawDistance)
            return;
        var toe = main.Sample(branch.Toe);
        var across = Double3.Cross(toe.Tangent, Double3.Up).Normalized;
        var foot = lever - Double3.Up * 0.9;
        mesh.Box(V(foot + Double3.Up * 0.8, eye), ToF(across), Vector3.UnitY, ToF(toe.Tangent * -1), new Vector3(0.08f, 0.8f, 0.08f), Palette.IronGrey);
        mesh.Box(V(lever - across * (branch.Side * 0.35), eye), ToF(across), Vector3.UnitY, ToF(toe.Tangent * -1), new Vector3(0.35f, 0.035f, 0.035f), Palette.TarnishedBrass);
        bool diverging = Diverging?.Invoke(branch.Index) ?? false;
        var lamp = diverging ? Palette.SignalRed : Palette.SignalGreen;
        var lampAt = foot + Double3.Up * 1.75;
        mesh.PointLights.Add(new PointLight(V(lampAt, eye), lamp * 0.6f, 5));
        mesh.Emissive = 1;
        mesh.Box(V(lampAt, eye), ToF(across), Vector3.UnitY, ToF(toe.Tangent * -1), new Vector3(0.16f, 0.16f, 0.16f), lamp);
        mesh.Emissive = 0;
    }

    static readonly SwitchStands DefaultStands = new(new JunctionTuning());

    IReadOnlyList<Sign>? _defaultSigns;

    /// <summary>
    /// A lineside mail crane (the playtest's rewards): a post a little out from the track on its side, an arm reaching in
    /// over the cess, and the bag hung from it at a car's doorway height, where the hook out of a side door takes it. Its
    /// colour says what's in it: mail sacks grey canvas, coal black, rounds olive, spares brass. Without word of the catch
    /// the bag goes once the engine's by (<paramref name="by"/>); the crane stays.
    /// </summary>
    void Crane(MeshBuilder mesh, RailLine line, Double3 eye, Drop drop, bool by)
    {
        var t = line.Sample(drop.At);
        var right = Double3.Cross(t.Tangent, Double3.Up).Normalized * drop.Side;
        var (x, y, z) = (ToF(right), Vector3.UnitY, ToF(t.Tangent * -1));
        var foot = t.Position + right * 3.2;
        // Caught: how long ago, timed from the first frame that saw it (a client sees it when the host's word does).
        double? caught = null;
        if (DropCaught?.Invoke(drop.Id) == true)
        {
            if (!_caughtAt.TryGetValue(drop.Id, out var when))
                _caughtAt[drop.Id] = when = Time - StagedCatch;
            caught = Time - when;
        }
        // The art pass's crane (tools/models mail_crane: its arms reaching in toward the line, the bag in their clamps; once
        // caught, the bag snatched in through the door going by, and the arms dropping to the post).
        // Where the catch is known, the bag hangs till it's taken (a missed one's still there); without word of it (a
        // client: the host keeps the catch), it's gone once the engine's by, as it always was.
        bool hung = DropCaught is not null || !by;
        if (Look?.Art.MailCrane(mesh, V(foot, eye), ToF(right * -1), drop.Kind, caught, ToF(t.Tangent) * (float)_speed, hung) == true)
            return;
        if (caught is not null || !hung)
            return;
        mesh.Box(V(foot + Double3.Up * 1.9, eye), x, y, z, new Vector3(0.1f, 1.9f, 0.1f), Palette.DeepBrown);
        mesh.Box(V(foot + Double3.Up * 3.5 - right * 0.45, eye), x, y, z, new Vector3(0.5f, 0.06f, 0.06f), Palette.IronGrey);
        var bag = drop.Kind switch
        {
            DropKind.Coal => Palette.SootBlack,
            DropKind.Ammo => Palette.MuddyOlive,
            DropKind.Spares => Palette.TarnishedBrass,
            _ => Palette.BlueGrey,
        };
        mesh.Box(V(foot + Double3.Up * 3.0 - right * 0.85, eye), x, y, z, new Vector3(0.02f, 0.45f, 0.02f), Palette.IronGrey);
        mesh.Box(V(foot + Double3.Up * 2.35 - right * 0.85, eye), x, y, z, new Vector3(0.22f, 0.3f, 0.22f), bag);
    }
    Route? _signsFor;

    /// <summary>The line's boards: the session's, or (a screenshot, the editor) the route's own at the default sight tuning.</summary>
    IReadOnlyList<Sign> BoardList()
    {
        if (Signs is not null)
            return Signs;
        if (Route is null)
            return [];
        if (!ReferenceEquals(_signsFor, Route))
        {
            _defaultSigns = [.. Sim.Route.Lineside.Boards(new SightTuning(), Route)];
            _signsFor = Route;
        }
        return _defaultSigns ?? [];
    }

    /// <summary>
    /// The car fires the scene has seen, by car (GDD App. C.5): how bad each got, and when it was last burning. The sim
    /// keeps no record of a fire that's out; the car it gutted does, so the scene remembers it (presentation only: a
    /// client that joins after sees the car's scars, not its smoke). <see cref="StagedBurnt"/> stages one for a still.
    /// </summary>
    readonly Dictionary<int, (double Peak, double Last, double Burn, bool Alight)> _burns = new();

    /// <summary>For a still frame (<c>dt screenshot --burnt car,s</c>): that car gutted by a fire that went out s seconds ago.</summary>
    public (int Car, double Since)? StagedBurnt { get; set; }

    void Fires(int cars)
    {
        if (StagedBurnt is { } staged && !_burns.ContainsKey(staged.Car))
            _burns[staged.Car] = (1, Time - staged.Since, 0, false);
        foreach (var e in Enemies ?? [])
            if (e.Kind == EnemyKind.CarFire && !e.Gone && e.Attached >= 0 && e.Attached < cars)
            {
                double peak = _burns.TryGetValue(e.Attached, out var was) ? Math.Max(was.Peak, e.Extra) : e.Extra;
                _burns[e.Attached] = (peak, Time, e.Extra, e.Phase == SpinePhase.Punish);
            }
        // A car cut loose burning (note 458): its fire's gone from the sim, and it burns on as it rolls away (Riding).
        foreach (var (_, (body, _)) in _riding)
            if (body.Kind == EnemyKind.CarFire && body.Attached >= 0 && body.Attached < cars && Adrift(body.Attached))
            {
                double peak = _burns.TryGetValue(body.Attached, out var was) ? Math.Max(was.Peak, body.Extra) : body.Extra;
                _burns[body.Attached] = (peak, Time, body.Extra, body.Phase == SpinePhase.Punish);
            }
        // Clocks run backwards across a reload or a fresh run: forget what was.
        foreach (var car in _burns.Keys.Where(k => _burns[k].Last > Time + 1).ToList())
            _burns.Remove(car);
    }

    /// <summary>A car's fire, burning (this frame) or out (and how long since), or null if it's never burned.</summary>
    Art.Effects.Burning? Burnt(int car)
    {
        if (!_burns.TryGetValue(car, out var f))
            return null;
        double since = Math.Max(0, Time - f.Last);
        bool now = since < 0.5;
        return new Art.Effects.Burning(now ? (float)f.Burn : 0, now && f.Alight, now ? 0 : (float)since, (float)Math.Clamp(f.Peak, 0, 1));
    }
    readonly Dictionary<int, double> _caughtAt = new();
    double _speed;
    double? _wheelClock;

    /// <summary>How far the engine's wheels have rolled (m): run on by the frames' speed and time; set to stage a turn.</summary>
    public double Rolled { get => _travelled; set => _travelled = value; }
    double _travelled;

    /// <summary>
    /// The line's boards (sight.json), on posts to the right of the line facing the oncoming train: a posted speed is a pale
    /// enamel plate with the figure's bar across it, the clearance board yellow and black. Paint, not lamps: the headlamp
    /// picks them out, and lamps down they're nothing (the point of them, after the playtest). Reflective paint sends the
    /// lamp's light straight back up the line: a board shines out once it's within the lamp's reading range, exactly when
    /// the sim says it's read (<see cref="SightTuning.LampSignRange"/>).
    /// </summary>
    void Boards(MeshBuilder mesh, RailLine line, Double3 eye, double from, double to, double front)
    {
        foreach (var sign in BoardList())
            if (sign.Drop is { } drop && drop.At >= from && drop.At <= to)
                Crane(mesh, line, eye, drop, drop.At <= front - 30);
        foreach (var sign in BoardList())
        {
            if (sign.Board < from || sign.Board > to)
                continue;
            var t = line.Sample(sign.Board);
            var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
            var foot = t.Position + right * 3.4;
            var (x, y, z) = (ToF(right), Vector3.UnitY, ToF(t.Tangent * -1));
            double read = sign.Board - front;
            // The art pass's boards (SignKit, the plan's own), lit back at the lamp while it's reading them.
            if (Look is not null && Look.Art.LinesideBoard(mesh, sign, V(foot, eye), x, z, LampLit && read > 0 && read <= SignRange))
                continue;
            mesh.Box(V(foot + Double3.Up * 1.8, eye), x, y, z, new Vector3(0.08f, 1.8f, 0.08f), Palette.IronGrey);
            var plate = foot + Double3.Up * 3.6 - t.Tangent * 0.1;
            double ahead = sign.Board - front;
            mesh.Emissive = LampLit && ahead > 0 && ahead <= SignRange ? 0.7f : 0;
            if (sign.Kind == SignKind.SpeedLimit)
            {
                mesh.Box(V(plate, eye), x, y, z, new Vector3(0.9f, 0.6f, 0.04f), Palette.BoardEnamel);
                mesh.Box(V(plate - t.Tangent * 0.05, eye), x, y, z, new Vector3(0.6f, 0.14f, 0.01f), Palette.SootBlack);
            }
            else if (sign.Kind == SignKind.Drop && sign.Drop is { } drop)
            {
                // A mail crane ahead: green, and a bar at the side it's on.
                mesh.Box(V(plate, eye), x, y, z, new Vector3(0.8f, 0.5f, 0.04f), Palette.SignalGreen);
                mesh.Box(V(plate + right * (drop.Side * 0.45) - t.Tangent * 0.05, eye), x, y, z, new Vector3(0.18f, 0.35f, 0.01f), Palette.BoardEnamel);
            }
            else if (sign.Kind == SignKind.Terminus)
            {
                // The terminus: enamel and black in quarters.
                for (int q = 0; q < 4; q++)
                    mesh.Box(V(plate + right * ((q % 2 == 0 ? -1 : 1) * 0.4) + Double3.Up * ((q < 2 ? -1 : 1) * 0.3), eye), x, y, z,
                        new Vector3(0.4f, 0.3f, 0.04f), (q == 0 || q == 3) ? Palette.BoardEnamel : Palette.SootBlack);
            }
            else
                for (int band = 0; band < 5; band++)
                    mesh.Box(V(plate + Double3.Up * (-0.52 + band * 0.26), eye), x, y, z, new Vector3(1.0f, 0.13f, 0.04f),
                        band % 2 == 0 ? Palette.HazardYellow : Palette.SootBlack);
            mesh.Emissive = 0;
        }
    }

    /// <summary>A box following the line: <paramref name="lateral"/> metres right of centre, base at <paramref name="y"/> above rail.</summary>
    static void Along(MeshBuilder mesh, RailLine line, Double3 eye, double s, double length, double lateral, double y, double halfWidth, double height, Vector3 color)
    {
        var t = line.Sample(s + length / 2);
        var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
        var centre = t.Position + right * lateral + Double3.Up * (y + height / 2);
        mesh.Box(V(centre, eye), ToF(right), Vector3.UnitY, ToF(t.Tangent * -1), new Vector3((float)halfWidth, (float)height / 2, (float)length / 2), color);
    }

    void Features(MeshBuilder mesh, RailLine line, Double3 eye, double from, double to)
    {
        foreach (var f in Route!.Features)
        {
            if (f.End < from || f.Start > to)
                continue;
            double a = Math.Max(f.Start, from), b = Math.Min(f.End, to);
            // The art pass's structures (Art/StructureKit): viaducts and trestles, portals and bores.
            if (Look is not null && f.Kind == FeatureKind.Bridge)
            {
                Look.Art.World.Bridge(mesh, line, f, eye, from, to, Art.WorldArt.SpanDepth(Route, f) ?? (float)ValleyDepth, Art.WorldArt.SpanType(Route, f));
                continue;
            }
            if (Look is not null && f.Kind == FeatureKind.Tunnel)
            {
                Look.Art.World.Tunnel(mesh, line, f, eye, from, to);
                continue;
            }
            switch (f.Kind)
            {
                case FeatureKind.Tunnel:
                    // Walls and roof close round the track, a hill sits on top, and the portals are faced in stone.
                    for (double s = a; s < b; s += 10)
                    {
                        double len = Math.Min(10, b - s);
                        Along(mesh, line, eye, s, len, -3.2, -0.2, 0.6, 7.2, Palette.Charcoal);
                        Along(mesh, line, eye, s, len, 3.2, -0.2, 0.6, 7.2, Palette.Charcoal);
                        Along(mesh, line, eye, s, len, 0, 6.6, 3.8, 1.2, Palette.Charcoal);
                        Along(mesh, line, eye, s, len, 0, 7.8, 40, 14, Palette.MuddyOlive);
                    }
                    foreach (double portal in new[] { f.Start, f.End - 1.5 })
                        if (portal >= from && portal <= to)
                        {
                            Along(mesh, line, eye, portal, 1.5, -5.5, 0, 2.2, 8, Palette.IronGrey);
                            Along(mesh, line, eye, portal, 1.5, 5.5, 0, 2.2, 8, Palette.IronGrey);
                            Along(mesh, line, eye, portal, 1.5, 0, 7.4, 7.7, 1.6, Palette.IronGrey);
                        }
                    break;
                case FeatureKind.Bridge:
                    // Weak bridges are timber trestles; sound ones are iron girders on stone piers.
                    var deck = f.MaxCars > 0 ? Palette.DeepBrown : Palette.IronGrey;
                    for (double s = a; s < b; s += 10)
                    {
                        double len = Math.Min(10, b - s);
                        Along(mesh, line, eye, s, len, 0, -0.6, 2.2, 0.6, deck);
                        Along(mesh, line, eye, s, len, -2.3, -0.6, 0.12, 1.6, deck);
                        Along(mesh, line, eye, s, len, 2.3, -0.6, 0.12, 1.6, deck);
                    }
                    for (double s = Math.Ceiling(a / 25) * 25; s < b; s += 25)
                        Along(mesh, line, eye, s, 3, 0, -ValleyDepth, f.MaxCars > 0 ? 1.6 : 2.0, ValleyDepth - 0.6, f.MaxCars > 0 ? Palette.DeepBrown : Palette.Charcoal);
                    break;
                case FeatureKind.Sleepers when Enemies is null:
                    // Shaped like ties, lying across the rail: invisible until the lamp finds them (App. A.2).
                    for (double s = a; s < b; s += 2.6)
                        Along(mesh, line, eye, s, 0.9, 0, 0.12, 1.5, 0.28, Palette.Corrupted);
                    break;
                case FeatureKind.Grease:
                    mesh.Emissive = 0.15f;
                    foreach (double side in new[] { -0.72, 0.72 })
                        Along(mesh, line, eye, a, b - a, side, 0.19, 0.05, 0.02, Palette.GreaseSheen);
                    mesh.Emissive = 0;
                    break;
                case FeatureKind.Junction:
                    // The branch and its switch stand are drawn with the track.
                    break;
                case FeatureKind.Facility when line.Branches.FirstOrDefault(b => b.Kind == BranchKind.Spur && f.Contains(b.Toe)) is { } spur:
                    {
                        // Down its spur (GDD §17): the buildings stand back beyond the machinery, behind the crate stack
                        // and short of the winch's haul, so the modules stay in the open where you work them.
                        var site = Run?.Sites.FirstOrDefault(s => s?.Spur == spur.Index);
                        double layout = site?.Mid ?? spur.Local.Length - 45;
                        FacilityBuildings(mesh, spur.Local, eye, f.Facility, layout - 25, spur.Side, push: 4);
                        break;
                    }
                case FeatureKind.Facility:
                    Facility(mesh, line, eye, f);
                    if (f.Facility == FacilityKind.CoalingTower && Run is { } run)
                        Chute(mesh, line, eye, f, run);
                    break;
            }
        }
    }

    /// <summary>One of a fortress's people, standing at <paramref name="feet"/> facing <paramref name="facing"/>: the crew's
    /// model in their own drab, idling on their own beat (note 107).</summary>
    /// <param name="drab">How much darker than the crew they're dressed (a town's people, near and talked to, are lighter).</param>
    /// <param name="pose">How they're standing (a town's people, note 281): "idle", "seated" (at a table, in a chair),
    /// "crouch" (at the range), "lantern" (out in the street with a lamp, lit).</param>
    /// <param name="gear">A town's person (note 353): what they breathe through (<see cref="Sim.Towns.TownGear"/>), worn on the
    /// survivors' bare-headed figure, never the crew's masked one; null for note 107's folk in the crew's own.</param>
    /// <param name="home">At home in an open house: some have the mask down on the chest.</param>
    /// <param name="lamp">A town's person carrying a lit hand lamp (out in the street at night).</param>
    void Folk(MeshBuilder mesh, Double3 eye, Double3 feet, Double3 facing, int variant, float drab = 0.45f, string pose = "idle",
        string? gear = null, bool home = false, int who = 0, bool lamp = false)
    {
        drab += variant % 3 * 0.05f;
        string clip = gear is null
            ? pose switch { "seated" => "gunner", "crouch" => "crouch_idle", "lantern" => "lantern", "walk" => "lantern_walk", _ => "idle" }
            : Art.TownsfolkKit.Clip(pose, lamp, who);
        var creatures = Look!.Art.Creatures;
        string figure = "crew";
        Matrix4x4 m;
        // A town's people are the survivors' figure, bare-headed, with what they breathe through (note 353), set down on
        // their feet (the director's 8 Oct shots: a resident crouched in the air by the range).
        if (gear is not null && Look.Art.Townsfolk.Person(creatures, mesh, V(feet, eye), ToF(facing), clip, pose == "seated", gear, home, variant, who,
            Time + variant * 0.73, drab) is { } drawn)
            (figure, m) = drawn;
        else
        {
            var back = -ToF(facing);
            m = Art.CreatureArt.Basis(V(feet, eye), Vector3.Cross(Vector3.UnitY, back), Vector3.UnitY, back);
            if (!creatures.Draw(mesh, "crew", clip, Time + variant * 0.73, true, m, variant, seed: variant * 13,
                adjust: (_, l) => l with { Colour = l.Colour * new Vector3(drab, drab * 0.95f, drab * 0.9f) }))
                return;
        }
        // The street's lamp-carriers: the hand lamp hung from the fist, burning.
        if ((gear is null ? pose is "lantern" or "walk" : lamp) && Art.PropArt.Of(Look).Get("hand_lantern") is { } lantern && creatures.Hang(mesh, lantern, m, figure))
        {
            var flame = creatures.LastHanging;
            mesh.PointLights.Add(new PointLight(flame, Palette.LampAmber * 1.2f, 6));
            mesh.Billboard(flame, 0.35f, 0, new Vector4(Palette.LampAmber * 0.6f, 1), -1, FxBlend.Additive);
        }
    }

    /// <summary>Walls both sides, gun towers with lamps, and a gatehouse over the line.</summary>
    void Fortress(MeshBuilder mesh, RailLine line, Double3 eye, double from, double to, double start, double end, double gateAt, bool lit = true)
    {
        if (Look is not null)
        {
            // The departure fortress is a town (note 281): its square, and its people where note 107's folk stood.
            var town = start == 0 && lit ? Town : null;
            Look.Art.World.Fortress(mesh, line, eye, from, to, start, end, gateAt, platform: start == 0, lit, Time, town?.Plan.Square, town?.Plan.Bounds);
            if (town is not null)
            {
                Look.Art.World.Square(mesh, line, eye, town, from, to);
                Look.Art.World.Civic(mesh, line, eye, town, from, to);
                Look.Art.World.Houses(mesh, line, eye, town, from, to);
                Look.Art.World.Streets(mesh, line, eye, town, from, to);
                // A town that's lived in (App. F.3, the director: the fortresses feel static): smoke from its chimneys, and
                // its watch walking the wall with their lanterns.
                foreach (var top in Look.Art.World.Chimneys(line, eye, town, 160))
                    Look.Art.Effects.Chimney(mesh, top, Time, (int)(top.X * 7 + top.Z * 13));
                foreach (var (feet, facing, variant) in Art.WorldArt.Watch(town, gateAt, Time))
                    if ((feet - eye).Length < 260)
                        Folk(mesh, eye, feet, facing, variant, drab: 0.6f, "walk", gear: "respirator", who: variant, lamp: true);
                // The statue on the green (note 353): the survivors' figure, frozen in its pose on the plinth, cast in bronze or
                // cut in stone; the Lamplighter's lamp lit.
                foreach (var f in town.Plan.Fixtures.Where(f => f.Kind == "statue"))
                {
                    var at = town.World(f.S, f.D, Art.CivicKit.PlinthTop);
                    if ((at - eye).Length > 160)
                        continue;
                    var (clip, scale, away, stone) = Art.CivicKit.StatueClip(Art.CivicKit.Variant(f.Kind, f.Name));
                    var face = ToF(town.Direction(f.S, f.FaceS, f.FaceD)) * (away ? -1 : 1);
                    var back = -face * scale;
                    var right = Vector3.Cross(Vector3.UnitY, -face) * scale;
                    var m = Art.CreatureArt.Basis(V(at, eye), right, Vector3.UnitY * scale, back);
                    var cast = stone ? new Vector3(0.42f, 0.41f, 0.38f) : new Vector3(0.24f, 0.18f, 0.1f);
                    Look.Art.Creatures.Draw(mesh, "survivor_prisoner", clip, 0, false, m, 0,
                        adjust: (_, l) => l with { Layer = -1, Colour = cast, Emissive = 0, Shine = stone ? 0.05f : 0.75f, Wear = 0.6f });
                    if (clip == "lantern" && Art.PropArt.Of(Look).Get("hand_lantern") is { } held && Look.Art.Creatures.Hang(mesh, held, m, "survivor_prisoner"))
                        mesh.PointLights.Add(new PointLight(Look.Art.Creatures.LastHanging, Palette.LampAmber * 1.4f, 8));
                }
                foreach (var p in town.Plan.People)
                {
                    // Where they are on their round now (note 353): walking between stops, or at one doing what's done there.
                    var now = town.Now(p);
                    var feet = now.Feet;
                    if ((feet - eye).Length > 160)
                        continue;
                    bool talking = TownFacing is { } f && f.Person == p.Id;
                    // Whoever sits or crouches at their work stays put when you talk to them: turned to you, they'd swing
                    // off their chair or out from the range (the director, 8 Oct: "some of the animation positions are off").
                    bool settled = now.Act is "seated" or "crouch" or "mend";
                    var facing = talking && !settled && new Double3(TownFacing!.Value.Toward.X - feet.X, 0, TownFacing.Value.Toward.Z - feet.Z) is { Length: > 0.1 } toward
                        ? toward.Normalized : now.Facing;
                    // Held to talk mid-stride, they stand.
                    string act = talking && now.Walking ? "idle" : now.Act;
                    Folk(mesh, eye, feet, facing, p.Look % 7 + 1, drab: 0.72f, act, p.Gear, home: p.House >= 0, who: p.Id, lamp: p.Pose == "lantern");
                    // Whoever you're talking to has the lamplight on their face, so you can see who it is (most stand with
                    // a lit door or a fire at their back).
                    if (talking)
                        mesh.PointLights.Add(new PointLight(V(feet + facing * 1.1 + Double3.Up * 1.8, eye), Palette.LampAmber * 1.1f, 4.5f));
                }
                return;
            }
            // Its people (T100): a few about at night, in their own drab, idling on their own beat. A dark town has none.
            if (lit)
                foreach (var (feet, facing, variant) in Art.WorldArt.FortFolk(line, eye, Math.Max(start, from - 20), Math.Min(end, to + 20), gateAt, start == 0))
                    Folk(mesh, eye, feet, facing, variant);
            return;
        }
        double a = Math.Max(start, from), b = Math.Min(end, to);
        if (a >= b)
            return;
        for (double s = Math.Floor(a / 10) * 10; s < b; s += 10)
            foreach (int side in new[] { -1, 1 })
                Along(mesh, line, eye, s, 10, side * 14, 0, 0.8, 7, Palette.IronGrey);
        for (double s = Math.Ceiling(a / 120) * 120; s < b; s += 120)
            foreach (int side in new[] { -1, 1 })
            {
                Along(mesh, line, eye, s, 4, side * 14, 0, 2.2, 11, Palette.Charcoal);
                mesh.Emissive = 1;
                Along(mesh, line, eye, s + 1.5, 1, side * 11.6, 9.5, 0.2, 0.4, Palette.LampAmber);
                mesh.Emissive = 0;
            }
        if (gateAt >= from && gateAt <= to)
        {
            foreach (int side in new[] { -1, 1 })
                Along(mesh, line, eye, gateAt - 3, 6, side * 5, 0, 1.5, 10, Palette.Charcoal);
            Along(mesh, line, eye, gateAt - 3, 6, 0, 8.5, 6.5, 1.5, Palette.Charcoal);
            mesh.Emissive = 1;
            Along(mesh, line, eye, gateAt, 0.4, 0, 8.2, 0.5, 0.3, Palette.LampAmber);
            mesh.Emissive = 0;
        }
    }

    /// <summary>The coaling lever on the ground, and coal falling from the spout while the chute is open.</summary>
    void Chute(MeshBuilder mesh, RailLine line, Double3 eye, RouteFeature f, Sim.Run.Run run)
    {
        var (spout, lever) = run.ChuteAt(f, line);
        var t = line.Sample(spout);
        var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
        bool open = run.ChuteOpen && run.FacilityFeature == f;
        if (Look?.Art.ChuteLever(mesh, lever, t.Tangent, f.Side, open, eye) != true)
        {
            mesh.Box(V(lever - Double3.Up * 0.45, eye), ToF(right), Vector3.UnitY, ToF(t.Tangent * -1), new Vector3(0.06f, 0.45f, 0.06f), Palette.IronGrey);
            // The handle: down when pouring, up when shut.
            mesh.Box(V(lever + Double3.Up * (open ? -0.1 : 0.25) + right * (f.Side * 0.15), eye), ToF(right), Vector3.UnitY, ToF(t.Tangent * -1), new Vector3(0.2f, 0.04f, 0.04f), Palette.TarnishedBrass);
        }
        if (!open)
            return;
        // A curtain of coal from the spout onto whatever's under it, and dust lit by the tower's lamp.
        var top = t.Position + right * (f.Side * 0.4) + Double3.Up * 8.8;
        if (Look?.Art.Effects is { HasFlames: true } fx)
        {
            fx.CoalPour(mesh, V(top, eye), ToF(right), ToF(t.Tangent), 5.4f, Time);
            return;
        }
        for (int i = 0; i < 24; i++)
        {
            double fall = (Time * 7 + i * 0.37) % 5.4;
            var p = top - Double3.Up * fall + right * (0.3 * Math.Sin(i * 2.1)) + t.Tangent * (0.35 * Math.Cos(i * 1.3));
            mesh.Box(V(p, eye), ToF(right), Vector3.UnitY, ToF(t.Tangent * -1), new Vector3(0.22f, 0.26f, 0.22f), i % 3 == 0 ? Palette.IronGrey : Palette.Charcoal);
        }
    }

    /// <summary>
    /// GDD §18's set pieces (note 185), drawn from the sim's state: the grain elevator's bin on its legs astride the track,
    /// its spout down over the cars and grain falling while the lever's held; the slaughterhouse's pen, its herd (one going up
    /// the ramp as far as it's been driven) and the ramp to the car; the chemical works' hose stand, its gauge reading the
    /// pressure, the hose to the car it's on, and the leak's cloud.
    /// </summary>
    /// <param name="artDrawn">The art pass drew the site's modelled set pieces (#135): only what it doesn't model here.</param>
    /// <param name="conveyorDrawn">The art pass drew the conveyor line (note 430).</param>
    /// <param name="tipple">The tipple's tuning (note 423): how far over its cradle turns at the top of the roll.</param>
    static void SetPieces(MeshBuilder mesh, Sim.Run.Site site, IReadOnlyList<CarFrame> frames, Double3 eye, double time, bool artDrawn = false,
        bool conveyorDrawn = false, Sim.Run.TippleTuning? tipple = null)
    {
        static (Vector3 Along, Vector3 Across) Axes(Double3 from, Double3 to)
        {
            var d = (to - from) with { Y = 0 };
            var across = ToF(d.Length > 1e-6 ? d.Normalized : new Double3(1, 0, 0));
            return (Vector3.Cross(Vector3.UnitY, across), across);
        }
        void Rod(Double3 a, Double3 b, float r, Vector3 colour)
        {
            var d = b - a;
            if (d.Length < 1e-6)
                return;
            var dir = ToF(d.Normalized);
            var side = Vector3.Normalize(Vector3.Cross(dir, MathF.Abs(dir.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY));
            mesh.Box(V((a + b) * 0.5, eye), dir, Vector3.Cross(side, dir), side, new Vector3((float)d.Length * 0.5f, r, r), colour);
        }
        var grain = new Vector3(0.72f, 0.6f, 0.36f);
        if (!artDrawn && site.Has(Sim.Run.ModuleKind.Spout))
        {
            // The bin up on four legs astride the track, the spout's pipe down to just over a car's roof.
            var mouth = site.Spout;
            var ground = mouth with { Y = mouth.Y - 5.2 };
            var (along, across) = Axes(mouth, site.SpoutLever);
            var a = ToD(along);
            var x = ToD(across);
            foreach (int i in new[] { -1, 1 })
                foreach (int j in new[] { -1, 1 })
                    Rod(ground + a * (i * 2.4) + x * (j * 2.8) - Double3.Up * 0.3, mouth + Double3.Up * 3.2 + a * (i * 1.8) + x * (j * 2.2), 0.12f, Palette.DeepBrown);
            mesh.Box(V(mouth + Double3.Up * 4.6, eye), along, Vector3.UnitY, across, new Vector3(2.2f, 1.5f, 2.6f), Palette.IronGrey);
            mesh.Box(V(mouth + Double3.Up * 6.2, eye), along, Vector3.UnitY, across, new Vector3(2.5f, 0.12f, 2.9f), Palette.RustRed);
            Rod(mouth + Double3.Up * 3.1, mouth, 0.28f, Palette.TarnishedBrass);
            // Grain left in the bin, its level in a sight glass on the bin's track side.
            float level = (float)Math.Clamp(site.Bin / 3.0, 0, 1);
            mesh.Box(V(mouth + Double3.Up * (3.2 + 1.4 * level) + x * 2.62, eye), along, Vector3.UnitY, across, new Vector3(0.3f, 1.4f * level + 0.02f, 0.02f), grain);
            // The lever: a post, its handle down while it's held.
            var lever = site.SpoutLever;
            Rod(lever - Double3.Up * 0.9, lever, 0.06f, Palette.IronGrey);
            Rod(lever, lever + Double3.Up * (site.Pouring ? -0.15 : 0.35) + x * 0.45, 0.04f, Palette.HazardYellow);
            if (site.Pouring)
                for (int i = 0; i < 18; i++)
                {
                    double fall = (time * 6 + i * 0.31) % 2.6;
                    var p = mouth - Double3.Up * fall + a * (0.18 * Math.Sin(i * 2.3)) + x * (0.18 * Math.Cos(i * 1.7));
                    mesh.Box(V(p, eye), along, Vector3.UnitY, across, new Vector3(0.12f, 0.2f, 0.12f), grain * (i % 2 == 0 ? 1f : 0.8f));
                }
        }
        if (!artDrawn && site.Has(Sim.Run.ModuleKind.Lift))
        {
            // The mine head's steam lift (note 368): the ore bin on its legs astride the track, fed down a sloping trough from the
            // headframe (the art's, where it has one), the skip riding up the frame's track-side face as far as it's wound, ore
            // down the chute as a skip tips, and the lever, its handle down while it winds.
            var mouth = site.LiftChute;
            var ground = mouth with { Y = mouth.Y - 4.6 };
            var frame = site.Headframe;
            var (along, across) = Axes(mouth, frame);
            var a = ToD(along);
            var x = ToD(across);
            foreach (int i in new[] { -1, 1 })
                foreach (int j in new[] { -1, 1 })
                    Rod(ground + a * (i * 1.9) + x * (j * 2.6) - Double3.Up * 0.3, mouth + Double3.Up * 1.6 + a * (i * 1.4) + x * (j * 2.0), 0.11f, Palette.DeepBrown);
            mesh.Box(V(mouth + Double3.Up * 2.4, eye), along, Vector3.UnitY, across, new Vector3(1.6f, 0.9f, 2.2f), Palette.IronGrey);
            Rod(mouth + Double3.Up * 1.5, mouth, 0.3f, Palette.RustRed);
            // The trough, from the frame's tipping point well up the headframe down onto the bin.
            var tip = frame + x * -2.2 + Double3.Up * 11;
            Rod(tip, mouth + Double3.Up * 3.3 + x * 1.6, 0.35f, Palette.RustRed * 0.9f);
            // The skip on its guides up the frame's track-side face.
            var guide = frame + x * -2.4;
            foreach (int i in new[] { -1, 1 })
                Rod(guide + a * (i * 0.8), guide + a * (i * 0.8) + Double3.Up * 12, 0.07f, Palette.IronGrey);
            var skip = guide + Double3.Up * (0.9 + 9.8 * Math.Clamp(site.Wind, 0, 1));
            mesh.Box(V(skip, eye), along, Vector3.UnitY, across, new Vector3(0.7f, 0.8f, 0.6f), Palette.SootBlack * 1.5f);
            // Ore left down the shaft, in a gauge on the bin's track side.
            float left = (float)Math.Clamp(site.Ore / 3.0, 0, 1);
            mesh.Box(V(mouth + Double3.Up * (1.6 + 1.6 * left) + x * -2.22, eye), along, Vector3.UnitY, across, new Vector3(0.25f, 1.6f * left + 0.02f, 0.02f), Palette.Charcoal);
            var lever = site.LiftLever;
            Rod(lever - Double3.Up * 0.9, lever, 0.06f, Palette.IronGrey);
            Rod(lever, lever + Double3.Up * (site.Winding ? -0.15 : 0.35) - x * 0.45, 0.04f, Palette.HazardYellow);
            // Ore down the chute just after a skip tips (the wind's back near nothing).
            if (site.Winding && site.Wind < 0.15)
                for (int i = 0; i < 16; i++)
                {
                    double fall = (time * 6 + i * 0.29) % 2.2;
                    var p = mouth - Double3.Up * fall + a * (0.2 * Math.Sin(i * 2.3)) + x * (0.2 * Math.Cos(i * 1.7));
                    mesh.Box(V(p, eye), along, Vector3.UnitY, across, new Vector3(0.16f, 0.16f, 0.16f), i % 2 == 0 ? Palette.Charcoal : Palette.IronGrey * 0.7f);
                }
        }
        if (!conveyorDrawn && site.Has(Sim.Run.ModuleKind.Conveyor))
        {
            // The grain elevator's conveyor line (note 400): its belt low on trestles from the drive house at the elevator's end
            // to the knee beside the track, the riser up from there to its head over the track on a frame astride it, the
            // drive house with its starter and a lamp (green running, red stopped), grain riding the belt while it carries, and
            // a jam a heap spilled off the belt where it is, the grain behind it stood still.
            var tail = site.ConveyorTail;
            var knee = site.ConveyorKnee;
            var head = site.ConveyorHead;
            var (along, across) = Axes(knee, tail);
            var a = ToD(along);
            var x = ToD(across);
            var run = knee - tail;
            double length = run.Length;
            var dir = run * (1 / Math.Max(1e-6, length));
            var side = ToD(Vector3.Normalize(Vector3.Cross(ToF(dir), Vector3.UnitY)));
            // The belt and its rails.
            Rod(tail, knee, 0.24f, Palette.SootBlack * 1.4f);
            foreach (int j in new[] { -1, 1 })
                Rod(tail + side * (j * 0.32) + Double3.Up * 0.12, knee + side * (j * 0.32) + Double3.Up * 0.12, 0.04f, Palette.IronGrey);
            // Trestles every 3 m: a leg each side down to the ground.
            for (double d = 0; d <= length + 1e-6; d += 3)
            {
                var top = tail + dir * d;
                foreach (int j in new[] { -1, 1 })
                    Rod(top + side * (j * 0.36) - Double3.Up * 1.3, top + side * (j * 0.36), 0.05f, Palette.DeepBrown);
            }
            // The riser up to the head, and the frame astride the track it hangs from, its chute down over a car's roof.
            Rod(knee, head + Double3.Up * 0.3, 0.26f, Palette.SootBlack * 1.4f);
            var foot = head with { Y = knee.Y - 1.0 };
            foreach (int i in new[] { -1, 1 })
                foreach (int j in new[] { -1, 1 })
                    Rod(foot + a * (i * 1.4) + x * (j * 2.6) - Double3.Up * 0.3, head + Double3.Up * 0.9 + a * (i * 1.2) + x * (j * 2.2), 0.09f, Palette.DeepBrown);
            mesh.Box(V(head + Double3.Up * 0.9, eye), along, Vector3.UnitY, across, new Vector3(1.5f, 0.12f, 2.5f), Palette.RustRed);
            Rod(head + Double3.Up * 0.6, head - Double3.Up * 0.4, 0.22f, Palette.TarnishedBrass);
            // The drive house past the tail, its starter and its lamp.
            var house = tail + (tail - knee with { Y = tail.Y }) * (2.5 / Math.Max(1e-6, length)) + Double3.Up * (1.4 - 1.0);
            mesh.Box(V(house, eye), ToF(dir), Vector3.UnitY, ToF(side), new Vector3(1.6f, 1.4f, 1.4f), Palette.IronGrey * 0.9f);
            mesh.Box(V(house + Double3.Up * 1.5, eye), ToF(dir), Vector3.UnitY, ToF(side), new Vector3(1.8f, 0.1f, 1.6f), Palette.RustRed);
            var lamp = house + Double3.Up * 1.1 - dir * 1.62;
            mesh.Box(V(lamp, eye), ToF(dir), Vector3.UnitY, ToF(side), new Vector3(0.05f, 0.12f, 0.12f), site.Running && site.Jam < 0 ? Palette.SignalGreen : Palette.SignalRed);
            var starter = site.ConveyorStarter;
            Rod(starter - Double3.Up * 0.9, starter, 0.06f, Palette.IronGrey);
            Rod(starter, starter + Double3.Up * (site.Running ? -0.15 : 0.35) + side * 0.4, 0.04f, Palette.HazardYellow);
            // Grain on the belt: riding toward the head while it carries; stood still behind a jam, a heap spilled at it.
            double jam = site.Jam >= 0 ? site.Jam : 1;
            for (int i = 0; i < 24; i++)
            {
                double u = (i + (site.Carrying ? time * 1.2 % 1 : 0)) / 24.0;
                if (u > jam || !site.Running && site.Jam < 0)
                    continue;
                var p = tail + dir * (u * length) + Double3.Up * 0.16 + side * (0.12 * Math.Sin(i * 2.1));
                mesh.Box(V(p, eye), ToF(dir), Vector3.UnitY, ToF(side), new Vector3(0.18f, 0.06f, 0.14f), grain * (i % 2 == 0 ? 1f : 0.85f));
            }
            if (site.Jam >= 0)
            {
                var at = site.JamAt;
                mesh.Box(V(at + Double3.Up * 0.3, eye), ToF(dir), Vector3.UnitY, ToF(side), new Vector3(0.45f, 0.3f, 0.5f), grain * 0.9f);
                mesh.Box(V(at - Double3.Up * 0.85 + side * 0.4, eye), ToF(dir), Vector3.UnitY, ToF(side), new Vector3(0.6f, 0.15f, 0.5f), grain * 0.75f);
            }
            if (site.Carrying)
                for (int i = 0; i < 12; i++)
                {
                    double fall = (time * 6 + i * 0.37) % 2.0;
                    var p = head - Double3.Up * (0.4 + fall) + a * (0.15 * Math.Sin(i * 2.3)) + x * (0.15 * Math.Cos(i * 1.7));
                    mesh.Box(V(p, eye), along, Vector3.UnitY, across, new Vector3(0.12f, 0.2f, 0.12f), grain * (i % 2 == 0 ? 1f : 0.8f));
                }
        }
        if (site.Has(Sim.Run.ModuleKind.Tipple))
        {
            // The mine head's tipple (note 423): the cradle's two hoops round the track a car's length apart on their rollers,
            // turning with the car clamped in them (TippleTilt rolls the car itself), the clamp beam down on its roof once it's
            // clamped and the side platen it's rolled against; the ore bin up on its legs out on the site's side with its chute
            // reaching over where the car's open top comes round to, ore in it as much as is left and falling at the top of a
            // roll; the lever on its post, over while it's worked.
            var c = site.Cradle;
            var (along, across) = Axes(c, site.TippleBin);
            var a = ToD(along);
            var x = ToD(across);
            var axis = c + Double3.Up * TippleTilt.AxisHeight;
            double turned = tipple is { } tp && site.Clamped >= 0 ? site.Roll * tp.RollDegrees * Math.PI / 180 : 0;
            // Up and toward the bin, turned about the axis as the car is (its top comes round to the bin).
            var up = Double3.Up * Math.Cos(turned) + x * Math.Sin(turned);
            var toBin = x * Math.Cos(turned) - Double3.Up * Math.Sin(turned);
            const double Radius = 2.9, Half = 7.6;
            var ore = Palette.Charcoal;
            foreach (int end in new[] { -1, 1 })
            {
                var hub = axis + a * (end * Half);
                for (int i = 0; i < 20; i++)
                {
                    double t0 = i * Math.Tau / 20, t1 = (i + 1) * Math.Tau / 20;
                    Rod(hub + (up * Math.Cos(t0) + toBin * Math.Sin(t0)) * Radius, hub + (up * Math.Cos(t1) + toBin * Math.Sin(t1)) * Radius, 0.16f,
                        i % 5 == 0 ? Palette.RustRed : Palette.IronGrey * 0.8f);
                }
                // Its rollers under it either side, and their bed.
                foreach (int j in new[] { -1, 1 })
                    mesh.Box(V(c + a * (end * Half) + x * (j * 1.7) + Double3.Up * 0.3, eye), along, Vector3.UnitY, across, new Vector3(0.35f, 0.3f, 0.3f), Palette.SootBlack * 1.3f);
                mesh.Box(V(c + a * (end * Half) - Double3.Up * 0.05, eye), along, Vector3.UnitY, across, new Vector3(0.6f, 0.1f, 2.4f), Palette.DeepBrown);
            }
            // The clamp beam over the car's roof (down on it once clamped, up clear of it otherwise) and the side platen it's
            // rolled against, both turning with the hoops.
            double clamp = site.Clamped >= 0 ? 1.65 : 2.5;
            Rod(axis + up * clamp - a * Half, axis + up * clamp + a * Half, 0.18f, Palette.HazardYellow * 0.8f);
            Rod(axis + toBin * 1.75 - a * Half, axis + toBin * 1.75 + a * Half, 0.16f, Palette.IronGrey);
            // The bin on its four legs, the ore in it, its chute down toward the cradle.
            var bin = site.TippleBin;
            foreach (int i in new[] { -1, 1 })
                foreach (int j in new[] { -1, 1 })
                    Rod(bin + a * (i * 1.6) + x * (j * 1.3) - Double3.Up * 1.2, (bin + a * (i * 1.6) + x * (j * 1.3)) with { Y = c.Y }, 0.12f, Palette.DeepBrown);
            mesh.Box(V(bin, eye), along, Vector3.UnitY, across, new Vector3(1.9f, 1.2f, 1.6f), Palette.RustRed * 0.85f);
            if (tipple is { Ore: > 0 } full && site.TippleOre > 0)
            {
                float fill = (float)Math.Clamp(site.TippleOre / full.Ore, 0, 1);
                mesh.Box(V(bin + Double3.Up * (1.2 + 0.25 * fill), eye), along, Vector3.UnitY, across, new Vector3(1.7f, 0.25f * fill + 0.02f, 1.4f), ore);
            }
            var mouth = c + x * 3.1 + Double3.Up * (TippleTilt.AxisHeight + 1.4);
            Rod(bin - Double3.Up * 0.9 - x * 1.2, mouth, 0.45f, Palette.IronGrey * 0.9f);
            // Ore coming down the chute at the top of the roll, into the car's open top come round under it.
            if (site.Clamped >= 0 && site.RollingBack && site.Roll > 0.75)
                for (int i = 0; i < 16; i++)
                {
                    double fall = (time * 5 + i * 0.29) % 1.6;
                    var p = mouth - x * (0.5 + fall * 0.4) - Double3.Up * fall + a * (0.5 * Math.Sin(i * 2.3));
                    mesh.Box(V(p, eye), along, Vector3.UnitY, across, new Vector3(0.18f, 0.16f, 0.18f), i % 2 == 0 ? ore : Palette.IronGrey * 0.7f);
                }
            // The lever on its post: over while the cradle's clamped or turning.
            var lever = site.TippleLever;
            Rod(lever with { Y = c.Y }, lever, 0.07f, Palette.IronGrey);
            double pulled = site.Clamped >= 0 ? 0.9 : site.Clamp > 0 ? 0.5 : 0;
            Rod(lever, lever + Double3.Up * (0.55 * Math.Cos(pulled)) + x * (0.55 * Math.Sin(pulled)), 0.04f, Palette.HazardYellow);
        }
        if (!artDrawn && site.Has(Sim.Run.ModuleKind.Ramp))
        {
            // The pen's rails round the herd, the ramp up from it to a car's doorway, the head still penned.
            var pen = site.Pen;
            var (along, across) = Axes(site.RampTop, pen);
            var a = ToD(along);
            var x = ToD(across);
            double r = site.PenRadius;
            for (int i = 0; i < 16; i++)
            {
                double t0 = i * Math.PI / 8, t1 = (i + 1) * Math.PI / 8;
                var p0 = pen + a * (r * Math.Cos(t0)) + x * (r * Math.Sin(t0));
                var p1 = pen + a * (r * Math.Cos(t1)) + x * (r * Math.Sin(t1));
                // A gap where the ramp leaves the pen, towards the track.
                if (Math.Sin((t0 + t1) / 2) < -0.92)
                    continue;
                Rod(p0, p0 + Double3.Up * 1.3, 0.07f, Palette.DeepBrown);
                foreach (double h in new[] { 0.55, 1.15 })
                    Rod(p0 + Double3.Up * h, p1 + Double3.Up * h, 0.04f, Palette.DeepBrown);
            }
            var foot = pen - x * r;
            var top = site.RampTop;
            var rise = top - foot;
            var dir = ToF(rise.Normalized);
            var side = Vector3.Normalize(Vector3.Cross(dir, Vector3.UnitY));
            mesh.Box(V((foot + top) * 0.5 - Double3.Up * 0.1, eye), dir, Vector3.Cross(side, dir), side, new Vector3((float)rise.Length * 0.5f, 0.08f, 0.9f), Palette.DeepBrown);
            foreach (int k in new[] { -1, 1 })
                Rod(foot + ToD(side) * (k * 0.95) + Double3.Up * 1.0, top + ToD(side) * (k * 0.95) + Double3.Up * 1.0, 0.04f, Palette.DeepBrown);
            Vector3 hide(int i) => i % 3 == 0 ? new Vector3(0.82f, 0.78f, 0.7f) : i % 3 == 1 ? Palette.DeepBrown * 1.3f : Palette.Charcoal * 1.6f;
            void Beast(Double3 at, double yaw, int i)
            {
                var f = new Vector3(MathF.Sin((float)yaw), 0, MathF.Cos((float)yaw));
                var sd = Vector3.Cross(Vector3.UnitY, f);
                mesh.Box(V(at + Double3.Up * 0.85, eye), sd, Vector3.UnitY, f, new Vector3(0.38f, 0.38f, 0.85f), hide(i));
                mesh.Box(V(at + Double3.Up * 1.05 + ToD(f) * 1.0, eye), sd, Vector3.UnitY, f, new Vector3(0.2f, 0.22f, 0.3f), hide(i) * 0.85f);
                foreach (int u in new[] { -1, 1 })
                    foreach (int w in new[] { -1, 1 })
                        mesh.Box(V(at + ToD(sd) * (u * 0.25) + ToD(f) * (w * 0.6) + Double3.Up * 0.25, eye), sd, Vector3.UnitY, f, new Vector3(0.06f, 0.25f, 0.06f), hide(i) * 0.7f);
            }
            int penned = site.Herding || site.Herd > 0 ? site.Head - 1 : site.Head;
            for (int i = 0; i < Math.Max(0, penned); i++)
            {
                double ang = i * 2.4 + 0.6, rad = r * (0.25 + 0.5 * ((i * 37) % 10) / 10.0);
                var at = pen + a * (rad * Math.Cos(ang)) + x * (rad * Math.Sin(ang));
                Beast(at, ang + time * 0.2 * (i % 2 == 0 ? 1 : -1) + (site.Stirred ? Math.Sin(time * 3 + i) * 0.4 : 0), i);
            }
            if (site.Head > 0 && (site.Herding || site.Herd > 0))
            {
                var on = foot + rise * Math.Clamp(site.Herd, 0, 1) - Double3.Up * 0.1;
                var d = rise with { Y = 0 };
                Beast(on, DMath.Atan2(d.X, d.Z), site.Head);
            }
        }
        if (!artDrawn && site.Has(Sim.Run.ModuleKind.Hose))
        {
            // The stand: a post and its valve wheel, a gauge going from green to red with the pressure, the hose.
            var stand = site.HoseStand;
            Rod(stand - Double3.Up * 0.2, stand + Double3.Up * 1.6, 0.1f, Palette.IronGrey);
            Rod(stand + Double3.Up * 1.6, stand + Double3.Up * 3.6, 0.07f, Palette.RustRed);
            mesh.Box(V(stand + Double3.Up * 1.2, eye), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(0.25f, 0.25f, 0.04f), Palette.TarnishedBrass);
            float pr = (float)Math.Clamp(site.Pressure, 0, 1);
            var gauge = Vector3.Lerp(Palette.SignalGreen, Palette.SignalRed, pr) * (site.Leaking ? 1.6f : 1f);
            mesh.Box(V(stand + Double3.Up * 1.75, eye), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(0.12f, 0.12f, 0.12f), gauge);
            var outlet = stand + Double3.Up * 3.4;
            if (site.HoseCar >= 0 && site.HoseCar < frames.Count)
            {
                // Over the car's roof to its filler, sagging in the middle.
                var frame = frames[site.HoseCar];
                var filler = frame.ToWorld(new Double3(0, frame.Shape.RoofHeight + 0.2, 0));
                var mid = (outlet + filler) * 0.5 - Double3.Up * 0.8;
                // Canvas-wrapped and tarred, banded so it reads against a car's side in the dark.
                Rod(outlet, mid, 0.12f, Palette.HazardYellow * 0.55f);
                Rod(mid, filler, 0.12f, Palette.HazardYellow * 0.55f);
            }
            else
                Rod(outlet, outlet - Double3.Up * 3.2 + new Double3(0.3, 0, 0.3), 0.12f, Palette.HazardYellow * 0.55f);
            if (site.Leaking)
                for (int i = 0; i < 6; i++)
                {
                    double t = (time * 0.4 + i / 6.0) % 1;
                    var p = outlet + new Double3(Math.Sin(i * 1.9) * 2.5 * t, -1.5 + 2.5 * t, Math.Cos(i * 1.3) * 2.5 * t);
                    mesh.Billboard(V(p, eye), 1.5f + 3f * (float)t, (float)(i + time * 0.3), new Vector4(0.55f, 0.65f, 0.25f, 0.5f * (1 - (float)t)), -1, FxBlend.Alpha);
                }
        }
    }

    /// <summary>
    /// An open house in the world: its frame's origin on its floor, its axes, its parts, its light, and how high its walls
    /// stand over that floor (an open barn's or shed's eaves, note 462).
    /// </summary>
    sealed record OpenHouse(Double3 Origin, Double3 X, Double3 Y, IReadOnlyList<Sim.Stops.FootprintPart> Parts, Double3? Light, bool Lamp, int Id, float Height = 3.0f, bool Shed = false);

    /// <summary>The light indoors (note 475): the look's, or the old numbers for the greybox.</summary>
    InteriorTuning Interiors => Look?.Tuning.Atmosphere.Interiors ?? DefaultInteriors;
    static readonly InteriorTuning DefaultInteriors = new();

    (Sim.Route.Route Route, RailLine Line, List<OpenHouse> Houses)? _openHouses;

    /// <summary>
    /// Inside the open houses (the director, 8 Oct: "some lighting inside, dim to keep it scary"): each part an enclosed space
    /// (Room), so the moon and the sky stay out, and its one light, a candle guttering or a lamp turned down
    /// (TownKit.HouseLight), the only light in there but a crewmate's lamp. An open barn, outbuilding or goods shed (note
    /// 417) is a Room too, up to its eaves (queue #198, note 462), with no light of its own: bring a lamp; and so is a yard's
    /// walk-in shed or strongroom, along its roofed lengths (queue #201, note 465). Only the houses near the eye.
    /// </summary>
    void HouseInteriors(MeshBuilder mesh, RailLine line, Sim.Route.Route route, Double3 eye)
    {
        if (_openHouses is not { } cached || cached.Route != route || cached.Line != line)
        {
            var houses = new List<OpenHouse>();
            foreach (var f in route.Features)
            {
                if (f.Stop is not { } stop || f.Start < 0 || f.End > line.Length)
                    continue;
                for (int i = 0; i < stop.Buildings.Count; i++)
                {
                    var b = stop.Buildings[i];
                    // An open barn or shed (note 462), or a yard's walk-in shed or its strongroom (note 465): its walls from the
                    // frame (0.15 m under the ground at its middle, as WorldArt stands it) to its eaves, no light of its own. A
                    // yard shed is a room along each roofed length (a gantry's cut through it is the open air, the sky over the
                    // castings). Not one a Holdout's in: that's the Holdout's shell.
                    bool yard = b.Kind is Sim.Stops.BuildingKind.Shed or Sim.Stops.BuildingKind.Hero;
                    if ((yard || Sim.Run.StopWalls.OpenShed(b)) && Sim.Run.StopWalls.Shelled(stop, i) && stop.Holdouts.All(h => h.Building != i))
                    {
                        double frame = Sim.Run.Run.StopWorld(line, f, b.Centre, Art.WorldArt.Ground(route, f.Start + b.S, (float)b.D, (float)ValleyDepth) - 0.15).Y;
                        Double3 On(double x, double y) => Sim.Run.Run.StopWorld(line, f, Sim.Run.StopWalls.InHouse(b, x, y)) with { Y = frame };
                        var origin = On(0, 0);
                        IReadOnlyList<Sim.Stops.FootprintPart> lengths = yard
                            ? [.. Sim.Run.StopWalls.Roofed(stop, i).Select(r => new Sim.Stops.FootprintPart((r.Lo + r.Hi) / 2, 0, r.Hi - r.Lo, b.Width))]
                            : [new Sim.Stops.FootprintPart(0, 0, b.Length, b.Width)];
                        if (lengths.Count == 0)
                            continue;
                        houses.Add(new OpenHouse(origin, (On(1, 0) - origin).Normalized, (On(0, 1) - origin).Normalized, lengths, null, false,
                            (int)(f.Start * 7 + i), yard ? Art.WorldArt.YardShedHeight(b) : Art.WorldArt.OpenShedHeight(b.Kind), Shed: true));
                        continue;
                    }
                    if (!b.Open || !Sim.Run.StopWalls.Walled(stop, i))
                        continue;
                    // Its floor, as the art stands it (WorldArt.Building: the frame 0.15 m under the ground at its middle,
                    // the boards 0.17 over that).
                    double floor = Sim.Run.Run.StopWorld(line, f, b.Centre, Art.WorldArt.Ground(route, f.Start + b.S, (float)b.D, (float)ValleyDepth) - 0.15 + 0.17).Y;
                    Double3 At(double x, double y) => Sim.Run.Run.StopWorld(line, f, Sim.Run.StopWalls.InHouse(b, x, y)) with { Y = floor };
                    var o = At(0, 0);
                    int index = i;
                    var light = Art.TownKit.HouseLight(b, stop.Containers.Where(c => c.Building == index),
                        Sim.Run.StopWalls.ClutterOf(stop, index), Sim.Run.StopWalls.Nest(stop, index));
                    var parts = b.Parts.Count > 0 ? b.Parts : [new Sim.Stops.FootprintPart(0, 0, b.Length, b.Width)];
                    houses.Add(new OpenHouse(o, (At(1, 0) - o).Normalized, (At(0, 1) - o).Normalized, parts,
                        light is var (lx, ly, height, _) ? At(lx, ly) + Double3.Up * height : null, light?.Lamp ?? false, (int)(f.Start * 7 + i)));
                }
            }
            _openHouses = cached = (route, line, houses);
        }
        const double Near = 40;
        foreach (var h in cached.Houses)
        {
            float wallHeight = h.Height;
            // (Near its walls, not its middle: a long yard shed is walked into at an end.)
            if ((h.Origin - eye).Length > Near + h.Parts.Max(p => Math.Abs(p.X) + p.Length / 2))
                continue;
            var right = ToF(h.X);
            var back = ToF(h.Y);
            // The shader's frame has up = back x right.
            if (Vector3.Cross(back, right).Y < 0)
                back = -back;
            var lit = Interiors;
            foreach (var part in h.Parts)
            {
                // From under its floor (the shader fades a room out over its last 15 cm: a box that began at the boards
                // left them outside, moonlit blue; note 475) to its eaves.
                const float Under = 0.3f;
                var centre = h.Origin + h.X * part.X + h.Y * part.Y + Double3.Up * ((wallHeight - Under) / 2);
                mesh.Rooms.Add(new Room(V(centre, eye), right, Vector3.UnitY, back, new Vector3((float)part.Length / 2, (wallHeight + Under) / 2, (float)part.Width / 2)));
                // A barn's, a shed's or a yard shed length's hurricane lantern turned low, hung from a beam in its middle
                // (note 475: the director's "functional"), steady and dim: enough to see the stacks, the loft, the bench by.
                // A long yard shed has one each shedLanternSpacing along it.
                if (h.Shed && lit.ShedLantern > 0)
                {
                    int lanterns = Math.Max(1, (int)Math.Round(part.Length / Math.Max(1, lit.ShedLanternSpacing)));
                    for (int k = 0; k < lanterns; k++)
                    {
                        double along = part.X + part.Length * ((k + 0.5) / lanterns - 0.5);
                        var hung = h.Origin + h.X * along + h.Y * part.Y + Double3.Up * Math.Min(wallHeight - 0.5, lit.ShedLanternHeight);
                        if ((hung - eye).Length > Near)
                            continue;
                        double s = Time * 2.3 + h.Id * 1.3 + along;
                        float sway = (float)(0.93 + 0.05 * Math.Sin(s) + 0.02 * Math.Sin(s * 3.7));
                        mesh.PointLights.Add(new PointLight(V(hung, eye), Palette.LampAmber * lit.ShedLantern * sway, lit.ShedLanternRange));
                        mesh.Billboard(V(hung, eye), 0.3f * sway, 0, new Vector4(Palette.LampAmber * 0.45f * sway, 1), -1, FxBlend.Additive);
                    }
                }
            }
            // The Gaunt's house has no light: its dark is the tell (TownKit.HouseLight).
            if (h.Light is not { } light)
                continue;
            // Dim and warm, a candle's guttering more than a lamp's.
            double t = Time * (h.Lamp ? 3 : 9) + h.Id * 1.7;
            float gutter = (float)(0.8 + 0.12 * Math.Sin(t) + 0.08 * Math.Sin(t * 2.9 + 0.7) * Math.Sin(t * 0.37));
            mesh.PointLights.Add(new PointLight(V(light, eye), Palette.LampAmber * (h.Lamp ? lit.Lamp : lit.Candle) * gutter, h.Lamp ? lit.LampRange : lit.CandleRange));
            // The flame's own small halo, so the light reads as coming from it.
            mesh.Billboard(V(light, eye), (h.Lamp ? 0.35f : 0.22f) * gutter, 0, new Vector4(Palette.LampAmber * 0.45f * gutter, 1), -1, FxBlend.Additive);
        }
    }

    /// <summary>
    /// An open house's hiding spots that have been searched (note 326), drawn over the house's own furniture (TownKit.OpenHouse,
    /// which stands it shut): a cupboard with its doors swung back and its inside dark, a cabinet with its drawer out, the
    /// cellar's hatch up on its hinge over a black hole, the boards lifted out and laid by the gap. From the sim's spots and
    /// what's searched, so a client sees what the host has.
    /// </summary>
    /// <summary>
    /// Each open house's door (note 401), by the sim's state so a client sees what the host has: a plank door hung on the
    /// doorway's side, swung in against the room and left hanging as the ransack left it, or shut in the doorway. Ledged
    /// on its inside, the same weathered deal as an opened cupboard's doors.
    /// </summary>
    void HouseDoors(MeshBuilder mesh, RailLine line, Route route, Sim.Run.StopWalls walls, Double3 eye)
    {
        var wood = new Vector3(0.13f, 0.1f, 0.07f);
        var up = Vector3.UnitY;
        const float Lintel = 2.15f, Half = (float)(Sim.Run.StopWalls.DoorWidth / 2), Thick = 0.025f;
        foreach (var d in walls.HouseDoors)
        {
            if ((d.At - eye).Length > 60)
                continue;
            int code = d.Key - 1, feature = code >> 12, building = code >> 2 & 1023;
            if (feature >= route.Features.Count || route.Features[feature] is not { Stop: { } stop } f || building >= stop.Buildings.Count)
                continue;
            // The floor, as the art stands it (SearchedSpots): the frame 0.15 m under the ground at the house's middle, the boards 0.17 over.
            var b = stop.Buildings[building];
            float floor = (float)(Sim.Run.Run.StopWorld(line, f, b.Centre, Art.WorldArt.Ground(route, f.Start + b.S, (float)b.D, (float)ValleyDepth) - 0.15 + 0.17).Y);
            var outward = new Vector3((float)d.Out.X, 0, (float)d.Out.Z);
            var side = Vector3.Cross(up, outward);
            // Hung on one side of the doorway, just inside the wall's outer face; shut, it runs across the doorway to the other
            // side, and open, it's swung in about its hinge into the room, a little past square.
            double t = Sim.Run.StopWalls.WallThickness;
            var hingeAt = new Double3(d.At.X, floor, d.At.Z) + ToD(side * (Half - 0.03f) - outward * (float)(t * 0.35));
            float swing = walls.Shut(d.Key) ? 0 : 100 * MathF.PI / 180;
            var along = Vector3.Normalize(-side * MathF.Cos(swing) - outward * MathF.Sin(swing));
            var face = Vector3.Cross(up, along);
            float width = Half * 2 - 0.08f, height = Lintel - 0.04f;
            Vector3 On(float a, float h, float off = 0) => V(hingeAt + ToD(along * a + face * off) + new Double3(0, h, 0), eye);
            mesh.Box(On(width / 2, height / 2), face, up, along, new Vector3(Thick, height / 2, width / 2), wood);
            // Its ledges, top and bottom, and the brace between, on the room's side.
            float inside = Vector3.Dot(face, -outward) >= 0 ? 1 : -1;
            foreach (float h in new[] { 0.3f, height - 0.3f })
                mesh.Box(On(width / 2, h, inside * (Thick + 0.012f)), face, up, along, new Vector3(0.012f, 0.06f, width / 2 - 0.04f), wood * 0.8f);
        }
    }

    void SearchedSpots(MeshBuilder mesh, RailLine line, Sim.Run.Run run, Double3 eye)
    {
        // Weathered deal, paler than the furniture's faces so an opened door or a lifted board reads by a lamp.
        var wood = new Vector3(0.26f, 0.19f, 0.12f);
        var dark = new Vector3(0.012f, 0.01f, 0.008f);
        IReadOnlyList<Sim.Route.RouteFeature>? stops = null;
        foreach (var spot in run.HidingSpots)
        {
            if ((spot.Kept - eye).Length > 60 || !run.Searched(spot.Stop, spot.Container.Index))
                continue;
            stops ??= run.Stops;
            if (stops[spot.Stop] is not { Stop: { } stop } f)
                continue;
            // The house's floor, as the art stands it: its frame 0.15 m under the ground at its middle, the boards 0.17 over that
            // (an open barn's or shed's, 0.21: WorldArt.OpenShed's boards over the shell's concrete).
            var b = stop.Buildings[spot.Container.Building];
            double boards = Sim.Run.StopWalls.OpenShed(b) ? 0.21 : 0.17;
            float floor = (float)(Sim.Run.Run.StopWorld(line, f, b.Centre, Art.WorldArt.Ground(Route, f.Start + b.S, (float)b.D, (float)ValleyDepth) - 0.15 + boards).Y);
            var into = new Vector3((float)spot.Facing.X, 0, (float)spot.Facing.Z);
            var up = Vector3.UnitY;
            var side = Vector3.Cross(up, into);
            Vector3 At(float out_, float height, float across = 0) =>
                V(new Double3(spot.Kept.X, floor + height, spot.Kept.Z) + ToD(into * out_ + side * across), eye);
            switch (spot.Container.Kind)
            {
                case Sim.Stops.ContainerKind.Cupboard:
                    // Its face is 0.25 out from its middle, 1 m wide and up to 1.9: inside dark, a door swung back from each side
                    // past square (115°), so it reads from in front, not edge on.
                    mesh.Box(At(0.255f, 0.86f), side, up, into, new Vector3(0.46f, 0.66f, 0.005f), dark);
                    foreach (float s in new[] { -1f, 1f })
                    {
                        float swing = 115 * MathF.PI / 180;
                        // Closed, a door runs from its hinge (the face's edge) in across the face; open, it's turned out about the hinge.
                        var along = Vector3.Normalize(-s * side * MathF.Cos(swing) + into * MathF.Sin(swing));
                        var face = Vector3.Cross(up, along);
                        var hinge = side * (s * 0.5f) + into * 0.25f;
                        var middle = hinge + along * 0.25f;
                        mesh.Box(V(new Double3(spot.Kept.X, floor + 0.88, spot.Kept.Z) + ToD(middle), eye), face, up, along, new Vector3(0.015f, 0.7f, 0.24f), wood);
                    }
                    break;
                case Sim.Stops.ContainerKind.Cabinet:
                    // A drawer pulled out of its face (0.22 out, up to 1.0), the slot it came from dark.
                    mesh.Box(At(0.225f, 0.72f), side, up, into, new Vector3(0.36f, 0.09f, 0.005f), dark);
                    mesh.Box(At(0.22f + 0.2f, 0.72f), side, up, into, new Vector3(0.36f, 0.08f, 0.2f), Palette.RustRed * 0.6f);
                    break;
                case Sim.Stops.ContainerKind.Cellar:
                    // The hatch (1 m square, flush) gone: a black hole, and the lid stood up on its back edge.
                    mesh.Box(At(0, 0.056f), side, up, into, new Vector3(0.46f, 0.004f, 0.46f), dark);
                    mesh.Box(At(-0.52f, 0.5f), side, up, into, new Vector3(0.5f, 0.5f, 0.025f), Palette.SootBlack * 0.8f + wood * 0.3f);
                    break;
                case Sim.Stops.ContainerKind.UnderFloor:
                    // The boards lifted out and laid beside the gap they came from.
                    mesh.Box(At(0, 0.004f), side, up, into, new Vector3(0.3f, 0.004f, 0.55f), dark);
                    for (int i = 0; i < 3; i++)
                        mesh.Box(At(-0.15f + i * 0.17f, 0.02f + i * 0.004f, 0.75f + (i % 2) * 0.05f), side, up, into, new Vector3(0.08f, 0.015f, 0.55f), wood);
                    break;
                case Sim.Stops.ContainerKind.Bench:
                    // An open shed's workbench (note 417): its drawer pulled out under the top, the slot dark, a tin knocked to the floor.
                    mesh.Box(At((float)Sim.Run.StopWalls.BenchDepth + 0.005f, 0.72f), side, up, into, new Vector3(0.3f, 0.06f, 0.005f), dark);
                    mesh.Box(At((float)Sim.Run.StopWalls.BenchDepth + 0.15f, 0.72f), side, up, into, new Vector3(0.3f, 0.055f, 0.17f), wood);
                    mesh.Box(At((float)Sim.Run.StopWalls.BenchDepth + 0.45f, 0.06f, -0.4f), side, up, into, new Vector3(0.08f, 0.06f, 0.08f), Palette.IronGrey);
                    break;
                case Sim.Stops.ContainerKind.Hayloft:
                    // A barn's hayloft: an armful of hay pulled down off the loft into a heap beside the ladder's foot.
                    float foot = (float)(Sim.Run.StopWalls.LoftDepth + Sim.Run.StopWalls.LadderLean - Sim.Run.StopWalls.BenchDepth);
                    mesh.Box(At(foot - 0.2f, 0.14f, 0.75f), side, up, into, new Vector3(0.55f, 0.14f, 0.45f), Palette.HazardYellow * 0.45f);
                    mesh.Box(At(foot + 0.25f, 0.05f, 0.6f), side, up, into, new Vector3(0.4f, 0.05f, 0.3f), Palette.HazardYellow * 0.4f);
                    break;
            }
        }
    }

    /// <summary>
    /// The wreck yard's heaps (GDD §18 "pull cargo off derailed trains. Unstable, unlit"; note 187), drawn from the sim's state:
    /// each a car on its side, its roof towards the line and its wheelsets in the air, tipped further each time it's shifted;
    /// shuddering and shedding dust while it groans (the tell before it goes).
    /// </summary>
    void Wreckage(MeshBuilder mesh, Sim.Run.Site site, Double3 eye, double time)
    {
        var rust = new Vector3(0.2f, 0.11f, 0.07f);
        foreach (var heap in site.Heaps)
        {
            if ((heap.Centre - eye).Length > DrawDistance)
                continue;
            // Along the track's heading where it lies, turned by its own yaw; rolled onto its side, a little more per shift.
            double hint = site.Track.Length;
            var sample = site.Track.Sample(Math.Clamp(site.Track.Nearest(heap.Centre, ref hint).Distance, 0, site.Track.Length));
            double yaw = DMath.Atan2(-sample.Tangent.X, -sample.Tangent.Z) + heap.Yaw;
            var along = new Vector3(-MathF.Sin((float)yaw), 0, -MathF.Cos((float)yaw));
            var flat = Vector3.Cross(along, Vector3.UnitY);
            float roll = MathF.PI / 2 - 0.25f + 0.18f * heap.Shifts;
            float shake = heap.Groan > 0 ? 0.04f * MathF.Sin((float)time * 37) : 0;
            var up = Vector3.Normalize(Vector3.UnitY * MathF.Cos(roll + shake) + flat * MathF.Sin(roll + shake));
            var side = Vector3.Cross(up, along);
            var centre = heap.Centre + Double3.Up * (1.45 - 0.1 * heap.Shifts);
            // The body: 14 m long, 2.9 wide, 3.2 high (a cargo car's), its roof and floor; the wheelsets up off what was its floor.
            mesh.Box(V(centre, eye), side, up, along, new Vector3(1.45f, 1.6f, 7f), rust);
            mesh.Box(V(centre + ToD(up) * 1.65, eye), side, up, along, new Vector3(1.55f, 0.08f, 7.1f), Palette.IronGrey * 0.7f);
            foreach (float z in new[] { -4.6f, 4.6f })
                foreach (float x in new[] { -0.75f, 0.75f })
                    mesh.Box(V(centre - ToD(up) * 2.05 + ToD(along) * z + ToD(side) * x, eye), side, up, along, new Vector3(0.08f, 0.45f, 0.45f), Palette.IronGrey);
            // Groaning: dust shaken off it.
            if (heap.Groan > 0)
                for (int i = 0; i < 5; i++)
                {
                    double t = (time * 0.7 + i / 5.0) % 1;
                    var p = heap.Centre + new Double3(Math.Sin(i * 2.1) * 3 * t, 0.3 + 2.2 * t, Math.Cos(i * 1.7) * 3 * t);
                    mesh.Billboard(V(p, eye), 1.2f + 2.4f * (float)t, (float)(i + time), new Vector4(0.45f, 0.4f, 0.33f, 0.45f * (1 - (float)t)), -1, FxBlend.Alpha);
                }
        }
    }

    /// <summary>
    /// A capstan winch (spec D.2): the drum by the track with its two handles, the rope out across the ground, and the
    /// sled of freight on it, as far along as the crew have hauled it.
    /// </summary>
    static void Winch(MeshBuilder mesh, Sim.Run.Site site, Double3 eye)
    {
        if (!site.Has(Sim.Run.ModuleKind.Winch))
            return;
        // The drum on its trestle, lying along the track between the cranks, turned by the crank's angle (T43).
        var axis = ToF(site.Axis);
        var outward = ToF(site.Outward);
        var hub = site.Capstan + Double3.Up * 0.9;
        float half = (float)((site.Handles[1] - site.Handles[0]).Length * 0.5) - 0.15f;
        foreach (float end in new[] { -1f, 1f })
            mesh.Box(V(site.Capstan + site.Axis * (end * (half - 0.05f)) + Double3.Up * 0.45, eye), axis, Vector3.UnitY, outward, new Vector3(0.05f, 0.45f, 0.35f), Palette.DeepBrown);
        var turn = new Vector3(0, (float)Math.Sin(site.Crank), 0) + outward * (float)Math.Cos(site.Crank);
        var turnUp = Vector3.Cross(axis, turn);
        mesh.Box(V(hub, eye), axis, turn, turnUp, new Vector3(half - 0.1f, 0.22f, 0.22f), Palette.IronGrey);
        mesh.Box(V(hub, eye), axis, turn, turnUp, new Vector3(half - 0.3f, 0.235f, 0.06f), Palette.DeepBrown);
        for (int i = 0; i < site.Handles.Length; i++)
        {
            // Each crank: an arm from the hub out to its grip, and the grip sticking out from the drum's end.
            var h = site.Handles[i];
            var grip = site.Grip(i);
            var arm = grip - h;
            var dir = ToF(arm.Normalized);
            var side = Vector3.Cross(axis, dir);
            // Brass, worn bright by hands, so the crank reads in the dark as the thing to take hold of.
            mesh.Box(V(h + arm * 0.5, eye), dir, side, axis, new Vector3((float)arm.Length * 0.5f + 0.04f, 0.035f, 0.025f), Palette.TarnishedBrass);
            float outOf = i == 0 ? -1 : 1;
            mesh.Box(V(grip + site.Axis * (outOf * 0.12), eye), axis, dir, side, new Vector3(0.12f, 0.035f, 0.035f), Palette.TarnishedBrass * 1.3f);
        }
        var drum = site.Capstan;
        var sled = site.Sled;
        var rope = sled - drum;
        // The rope comes off the top of the drum and down to the sled.
        var from = hub + Double3.Up * 0.22;
        var line = sled + Double3.Up * 0.3 - from;
        if (line.Length > 0.5)
        {
            var dir = line.Normalized;
            var ropeSide = Double3.Cross(dir, Double3.Up).Normalized;
            mesh.Box(V(from + line * 0.5, eye), ToF(dir), ToF(Double3.Cross(ropeSide, dir)), ToF(ropeSide), new Vector3((float)line.Length * 0.5f, 0.02f, 0.02f), Palette.DeepBrown);
        }
        if (site.SledsLeft > 0)
        {
            var across = ToF(rope.Length > 0.1 ? rope.Normalized : Double3.Cross(Double3.Up, new Double3(1, 0, 0)));
            mesh.Box(V(sled + Double3.Up * 0.15, eye), across, Vector3.UnitY, Vector3.Cross(across, Vector3.UnitY), new Vector3(1.2f, 0.15f, 0.8f), Palette.RustRed);
            mesh.Box(V(sled + Double3.Up * 0.75, eye), across, Vector3.UnitY, Vector3.Cross(across, Vector3.UnitY), new Vector3(0.9f, 0.45f, 0.6f), Palette.BlueGrey);
        }
    }

    /// <summary>
    /// A gantry crane (spec D.2, T48): four legs astride the track, the rails along the top, the bridge across them where it
    /// is, the trolley on it, the hook on its cable, and the castings: stacked, on the hook, or lashed on a car's roof. The
    /// cab's up at the near end, a box on the leg with its window looking along the gantry.
    /// </summary>
    static void Crane(MeshBuilder mesh, Sim.Run.Crane crane, IReadOnlyList<CarFrame> frames, Double3 eye)
    {
        void Beam(Double3 a, Double3 b, float r, Vector3 colour)
        {
            var axis = b - a;
            double len = axis.Length;
            if (len < 1e-4)
                return;
            var dir = axis * (1 / len);
            var side = Double3.Cross(dir, Math.Abs(dir.Y) > 0.9 ? new Double3(1, 0, 0) : Double3.Up).Normalized;
            mesh.Box(V((a + b) * 0.5, eye), ToF(side), ToF(dir), ToF(Double3.Cross(side, dir)), new Vector3(r, (float)len * 0.5f + r, r), colour);
        }
        double top = crane.Top;
        for (int end = 0; end < 2; end++)
            for (int side = 0; side < 2; side++)
            {
                var foot = crane.Corner(end, side);
                Beam(foot, foot + Double3.Up * top, 0.18f, Palette.IronGrey);
            }
        // The rails along the top, one over each row of legs.
        for (int side = 0; side < 2; side++)
            Beam(crane.Corner(0, side) + Double3.Up * top, crane.Corner(1, side) + Double3.Up * top, 0.15f, Palette.RustRed);
        // The bridge where it is, and the trolley on it.
        Beam(crane.BridgeEnd(0), crane.BridgeEnd(1), 0.22f, Palette.RustRed * 1.1f);
        var hook = crane.HookAt;
        var trolley = hook with { Y = crane.BridgeEnd(0).Y };
        Beam(trolley - Double3.Up * 0.3, trolley + Double3.Up * 0.3, 0.35f, Palette.IronGrey);
        Beam(trolley, hook, 0.02f, Palette.SootBlack);
        Beam(hook, hook - Double3.Up * 0.3, 0.07f, Palette.TarnishedBrass);
        // The cab up at the near end, and its control stand at the foot of the leg.
        var cab = crane.Cab;
        mesh.Box(V(cab, eye), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(0.9f, 0.8f, 0.9f), Palette.DeepBrown);
        mesh.Emissive = 1;
        mesh.Box(V(cab + Double3.Up * 0.3, eye), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(0.92f, 0.12f, 0.6f), Palette.LampAmber * 0.5f);
        mesh.Emissive = 0;
        mesh.Box(V(crane.Controls + Double3.Up * 0.55, eye), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(0.25f, 0.55f, 0.2f), Palette.IronGrey);
        var size = crane.Tuning.CastingSize;
        foreach (var c in crane.Castings)
            if (crane.Where(c, frames) is { } at)
            {
                var up = c.State == Sim.Run.CastingState.Loaded && c.Car >= 0 && c.Car < frames.Count ? frames[c.Car].Up : Double3.Up;
                var centre = at + up * (size[1] * 0.5);
                // A foundry casting: a dark iron block with a lifting eye on top, rust on its edges.
                mesh.Box(V(centre, eye), Vector3.UnitX, ToF(up), Vector3.UnitZ, new Vector3((float)size[0] * 0.5f, (float)size[1] * 0.5f, (float)size[2] * 0.5f), Palette.SootBlack * 1.4f);
                mesh.Box(V(centre + up * (size[1] * 0.5 + 0.1), eye), Vector3.UnitX, ToF(up), Vector3.UnitZ, new Vector3(0.08f, 0.1f, 0.03f), Palette.RustRed);
            }
    }

    /// <summary>Placeholder silhouettes until facility modules exist: oversized, dark, one working lamp (GDD §30).</summary>
    void Facility(MeshBuilder mesh, RailLine line, Double3 eye, RouteFeature f)
    {
        // A coaling tower Tower Jaw's at leans as it's gnawed, and once it's down isn't standing (note 363).
        var (lean, down) = f.Facility == FacilityKind.CoalingTower ? TowerState(line, f) : (0, Art.TowerDown.Standing);
        FacilityBuildings(mesh, line, eye, f.Facility, (f.Start + f.End) / 2, f.Side, push: 0, lean, down);
    }

    /// <summary>A facility's buildings beside a track (the main line, or its spur), centred along it at <paramref name="mid"/>.</summary>
    void FacilityBuildings(MeshBuilder mesh, RailLine line, Double3 eye, FacilityKind? kind, double mid, double side, double push, float lean = 0,
        Art.TowerDown down = Art.TowerDown.Standing)
    {
        if (Look is not null)
        {
            Look.Art.World.Facility(mesh, line, eye, kind, mid, side, push, lean, down);
            return;
        }
        if (down != Art.TowerDown.Standing)
            return;
        side = side == 0 ? 1 : side;
        double Out(double lateral) => side * (lateral + push);
        switch (kind)
        {
            case FacilityKind.CoalingTower:
                Along(mesh, line, eye, mid - 6, 12, Out(7), 0, 5, 22, Palette.Charcoal);
                Along(mesh, line, eye, mid - 2, 4, Out(2.6), 9, 1.6, 1.2, Palette.IronGrey);
                break;
            case FacilityKind.GrainElevator:
                for (int i = 0; i < 3; i++)
                    Along(mesh, line, eye, mid - 20 + i * 12, 10, Out(12), 0, 5, 26, Palette.BlueGrey);
                break;
            case FacilityKind.Foundry:
                Along(mesh, line, eye, mid - 40, 80, Out(22), 0, 14, 16, Palette.RustRed);
                Along(mesh, line, eye, mid + 10, 6, Out(26), 16, 2, 18, Palette.SootBlack);
                break;
            default:
                Along(mesh, line, eye, mid - 30, 60, Out(20), 0, 12, 10, Palette.DeepBrown);
                break;
        }
        mesh.Emissive = 1;
        Along(mesh, line, eye, mid, 0.4, Out(4), 5, 0.2, 0.3, Palette.LampAmber);
        mesh.Emissive = 0;
    }

    void Car(MeshBuilder mesh, CarFrame frame, Double3 eye)
    {
        var right = ToF(frame.Right);
        var up = ToF(frame.Up);
        var back = ToF(frame.Back);
        var o = V(frame.Origin, eye);
        Vector3 L(double x, double y, double z) => o + right * (float)x + up * (float)y + back * (float)z;
        void Draw(Box box, Vector3 color) => mesh.Box(L(box.Centre.X, box.Centre.Y, box.Centre.Z), right, up, back, ToF(box.HalfSize), color);

        var shape = frame.Shape;
        bool engine = frame.Index == 0;
        // Each car wears its own way (the grime pattern is in the car's own frame, so it rides with it).
        mesh.Seed = frame.Index + 1;
        var vehicle = Vehicles is { } vs && frame.Index < vs.Count ? vs[frame.Index] : null;
        // The art pass's kit (TrainKit): the body, doors and gun as cooked pieces; what's left here is what glows and moves.
        // A damaged engine's boiler leaking steam out of its split seams (Art/DamageKit.Leaks), as hard as the pressure's up.
        if (Look is not null && frame.Shape.Cab is not null && vehicle is { } eng && Look.Tuning.Damage.StateOf(eng.Integrity) is > 0 and var hurt)
            Look.Art.Effects.SteamLeaks(mesh, o, right, up, back, Art.DamageKit.Leaks(frame.Shape, hurt, frame.Index), Pressure, (float)_speed, Time, frame.Index);
        var burnt = Burnt(frame.Index);
        uint openLockers = KitLockerOpen && frame.Shape.Lockers.Count > 0 && Sim.World.KitLocker(frame.Shape) is { } kitBay
            ? 1u << kitBay.Index : 0;
        if (Look is not null && burnt is { } fire)
            Look.Art.Effects.CarSmoke(mesh, o, right, up, back, shape, fire, (float)_speed, Time, frame.Index);
        // A hot axle box (note 331): smoke off its rear bogie, on both sides (a box on each rail).
        if (Look is not null && vehicle is { HotBox: > 0 } hotCar)
        {
            var hb = HotBoxTuning ?? DefaultHotBox;
            var box = Sim.Train.HotBoxes.Box(frame.Shape, hb);
            float heat = (float)Math.Clamp(hotCar.HotBox / hb.FireAfter, 0, 1);
            foreach (int side in (ReadOnlySpan<int>)[1, -1])
                Look.Art.Effects.HotBoxSmoke(mesh, o + right * (float)(box.X * side) + up * (float)box.Y + back * (float)box.Z, up, back, heat,
                    (float)_speed, Time, frame.Index * 2 + (side > 0 ? 1 : 0));
        }
        // A wound brake's shoes and a seized axle's dragged wheel (notes 364, 367).
        TrainCar(mesh, frame, vehicle, eye);
        bool utility = Utility?.Invoke(frame.Index) == true || vehicle is { Kind: VehicleKind.Utility };
        // A lived-in car's stove smoking through its pipe: a utility car's, and the guard van's (TrainKit).
        if (Look is not null && frame.Shape.Interior is { } inside && frame.Shape.Cab is null && (utility || frame.Shape.Gun is not null))
        {
            var pipe = utility
                ? Art.TrainKit.StoveAt(frame.Shape) with { Y = (float)frame.Shape.RoofHeight + 0.9f }
                : new Vector3((float)frame.Shape.HalfWidth - 0.35f, (float)frame.Shape.RoofHeight + 0.9f, -(float)frame.Shape.HalfLength + 2.6f);
            Look.Art.Effects.StoveSmoke(mesh, o + right * pipe.X + up * pipe.Y + back * pipe.Z, up, back, (float)_speed, Time, frame.Index);
        }
        // Note 267: the lockers with something on their shelves, tagged.
        uint tagged = 0;
        if (frame.Shape.Lockers.Count > 0 && Bodies is { } stowed)
            foreach (var b in stowed)
                if (b.Stowed && b.Parent == frame.Index && b.Locker < 32)
                    tagged |= 1u << b.Locker;
        if (Look is not null && Look.Art.Car(mesh, frame, eye, vehicle, Emergency, Tick, CutEnds(frame.Index), burnt?.Char ?? 0, utility, openLockers, Handrails,
            dark: frame.Shape.Cab is null && (CarDark(frame.Index) || frame.Index >= (Vehicles?.Count ?? int.MaxValue) - LampsOut), taggedLockers: tagged))
        {
            if (engine)
                Look.Art.Gear(mesh, frame, eye, _travelled, Emergency ? 0.06f : 1);
            if (engine && Ruptured)
                Look.Art.RuptureTear(mesh, frame, eye);
            // The lamp out (switched off, or a Climber's smashed it): the lens dark glass over the art's lit one.
            if (engine && !LampLit)
                Look.Art.HeadlampOut(mesh, frame, eye);
            CarWorkings(mesh, frame, eye, Draw);
            if (engine)
            {
                // The dials: pressure from the boiler, heat from the fire, the water glass (no water model yet: steady),
                // and speed against the line's 80 km/h top.
                float speed = (float)frame.Velocity.Length / 22.2f;
                Look.Art.Gauges(mesh, frame, eye, [Pressure, FireGlow, Tender, speed]);
            }
            return;
        }
        // What you see is what you collide with: every solid is drawn, coloured by what it is. Long interior
        // surfaces go down in 2 m slices so the per-vertex lamp light has vertices to land on.
        foreach (var solid in shape.Solids)
        {
            if (vehicle is not null && !solid.Present(vehicle))
            {
                // An open roof hatch's two leaves (T99), swung up on their hinges at the sides.
                var lid = solid.Box;
                double across = (lid.Max.X - lid.Min.X) / 2, thick = lid.Max.Y - lid.Min.Y;
                Draw(new Box(new Double3(lid.Max.X, lid.Max.Y, lid.Min.Z), new Double3(lid.Max.X + thick, lid.Max.Y + across, lid.Max.Z)), Palette.IronGrey);
                Draw(new Box(new Double3(lid.Min.X - thick, lid.Max.Y, lid.Min.Z), new Double3(lid.Min.X, lid.Max.Y + across, lid.Max.Z)), Palette.IronGrey);
                continue;
            }
            var colour = PartColour(solid.Part, frame.Index);
            var b = solid.Box;
            double length = b.Max.Z - b.Min.Z;
            if (shape.Interior is null || length <= 2.5 || solid.Part is not (PartKind.Wall or PartKind.Chassis or PartKind.Body or PartKind.Cargo))
            {
                Draw(b, colour);
                continue;
            }
            int slices = (int)Math.Ceiling(length / 2);
            for (int k = 0; k < slices; k++)
            {
                double z0 = b.Min.Z + length * k / slices, z1 = b.Min.Z + length * (k + 1) / slices;
                Draw(new Box(b.Min with { Z = z0 }, b.Max with { Z = z1 }), colour);
            }
        }

        double half = shape.HalfLength;
        if (engine)
        {
            // The headlamp, on the cab's nose (note 276): dark under emergency lighting, and out when it's switched off or smashed.
            mesh.Emissive = LampLit ? 1 : 0;
            Draw(Box.FromCentre(new Double3(0, Sim.World.LampHeight, -half - 0.05), new Double3(0.35, 0.35, 0.1)),
                !LampLit ? Palette.SootBlack : Emergency ? Palette.LampAmber * 0.08f : Palette.LampAmber);
            mesh.Emissive = 0;
        }
        CarWorkings(mesh, frame, eye, Draw);
        if (!engine)
        {
            // Roof walkway plank down the safe centreline (across a shut roof hatch, T99, and not an open one).
            if (shape.Hatch is { } hatch && vehicle?.DoorOpen(CarShape.HatchBit) == true)
            {
                Draw(new Box(new Double3(-0.35, shape.RoofHeight, -half + 0.2), new Double3(0.35, shape.RoofHeight + 0.04, hatch.Min.Z)), Palette.TarnishedBrass);
                Draw(new Box(new Double3(-0.35, shape.RoofHeight, hatch.Max.Z), new Double3(0.35, shape.RoofHeight + 0.04, half - 0.2)), Palette.TarnishedBrass);
            }
            else
                Draw(new Box(new Double3(-0.35, shape.RoofHeight, -half + 0.2), new Double3(0.35, shape.RoofHeight + 0.04, half - 0.2)), Palette.TarnishedBrass);
        }
        // Doors: shut in the doorway, or slid aside when open: an end door along the end wall inside, a side door back
        // along the outside of the car (a boxcar's sliding door).
        foreach (var door in shape.DoorList)
        {
            bool open = vehicle?.DoorOpen(door.Index) ?? false;
            var box = door.Box;
            bool side = box.Max.Z - box.Min.Z > box.Max.X - box.Min.X;
            if (open)
            {
                var move = side
                    ? new Double3(box.Min.X < 0 ? -0.12 : 0.12, 0, box.Max.Z - box.Min.Z)
                    : new Double3(box.Max.X - box.Min.X, 0, box.Min.Z < 0 ? 0.12 : -0.12);
                box = new Box(box.Min + move, box.Max + move);
            }
            Draw(box, Palette.DeepBrown);
            var handle = side
                ? new Double3(box.Min.X < 0 ? box.Min.X - 0.03 : box.Max.X + 0.03, box.Min.Y + 1.0, open ? box.Min.Z + 0.1 : box.Max.Z - 0.12)
                : new Double3(open ? box.Min.X + 0.1 : box.Max.X - 0.12, box.Min.Y + 1.0, box.Centre.Z);
            Draw(Box.FromCentre(handle, side ? new Double3(0.08, 0.04, 0.04) : new Double3(0.04, 0.04, 0.08)), Palette.TarnishedBrass);
        }
        var vehicleNow = Vehicles is { } fleet && frame.Index < fleet.Count ? fleet[frame.Index] : null;
        if ((vehicleNow is null ? shape.Gun : vehicleNow.HasGun ? Sim.Combat.Guns.Mount(shape, vehicleNow.Gun) : null) is { } gun)
        {
            // Barrel along the gun's facing: its arc is readable from its silhouette (GDD §26).
            Draw(Box.FromCentre(gun.Position + gun.Facing * 0.9, new Double3(0.08, 0.08, 0.9)), Palette.SootBlack);
            Draw(Box.FromCentre(gun.Position, new Double3(0.3, 0.25, 0.35)), Palette.IronGrey);
        }
        foreach (var ladder in shape.Ladders)
        {
            // Rails run up the face the ladder is fixed to: thin across it, a hand-width wide along it.
            bool side = Math.Abs(ladder.Inward.X) > 0;
            double rise = ladder.Top - ladder.Foot.Y;
            var h = new Double3(side ? 0.05 : 0.25, rise / 2, side ? 0.25 : 0.05);
            Draw(Box.FromCentre(ladder.Foot + new Double3(0, rise / 2, 0), h), Palette.IronGrey);
        }
        // Wheel sets under both ends.
        foreach (double z in new[] { -half * 0.6, half * 0.6 })
            Draw(Box.FromCentre(new Double3(0, 0.45, z), new Double3(shape.HalfWidth * 0.8, 0.4, 1.2)), Palette.SootBlack);
    }

    /// <summary>
    /// What glows and moves on a car, with or without the kit: the firebox's glow, the vent valve, the driver's levers
    /// where the controls have them, and the lamp in every car.
    /// </summary>
    RailLine? _line, _mapLine;
    double _hint;
    readonly List<(double X, double Z)> _mapDots = [];
    (double U0, double V0, double Scale, double Ax, double Ay) _mapFit;

    /// <summary>A world point (X, Z) in the run map's turned chart coordinates: along the night's start-to-end, and across it.</summary>
    (double U, double V) Chart(double wx, double wz)
    {
        double qx = wx, qy = -wz;
        return (qx * _mapFit.Ax + qy * _mapFit.Ay, _mapFit.Ax * qy - _mapFit.Ay * qx);
    }

    static readonly Vector3 MapPaper = new(0.24f, 0.20f, 0.14f), MapInk = new(0.10f, 0.07f, 0.04f), MapLimit = new(0.42f, 0.06f, 0.03f);

    /// <summary>
    /// The night's run map, pinned to the cab's front wall left of the firebox (T98 playtest): the line as an inked dotted
    /// track on a lamp-lit chart, north up, its stops marked (facilities amber, villages pale), and where the train is now
    /// in red, moving along it.
    /// </summary>
    void CabMap(MeshBuilder mesh, CarFrame frame, Action<Box, Vector3> draw)
    {
        if (_line is not { } line || frame.Shape.Cab is not { } cab)
            return;
        double length = line.PathLength(RailLine.MainPath);
        if (length <= 0)
            return;
        // On the plate over the boiler, between the windows (T101): in plain sight of the driver looking ahead.
        var plate = Art.TrainKit.MapPlate(frame.Shape);
        double W = plate.Width, H = plate.Height;
        double x0 = plate.Corner.X, y0 = plate.Corner.Y, z = plate.Corner.Z;
        if (!ReferenceEquals(_mapLine, line))
        {
            _mapLine = line;
            _mapDots.Clear();
            int n = 180;
            for (int i = 0; i <= n; i++)
            {
                var p = line.Sample(RailLine.MainPath, length * i / n).Position;
                _mapDots.Add((p.X, p.Z));
            }
            // Turned so the night runs left to right along the plate, start to end (T101: the plate is long and low), as a
            // view from above: a turn, never a mirror.
            var (first, last) = (_mapDots[0], _mapDots[^1]);
            double ax = last.X - first.X, ay = -(last.Z - first.Z), al = Math.Sqrt(ax * ax + ay * ay);
            (ax, ay) = al > 1 ? (ax / al, ay / al) : (1, 0);
            _mapFit = (0, 0, 1, ax, ay);
            var chart = _mapDots.Select(d => Chart(d.X, d.Z)).ToList();
            double minU = chart.Min(c => c.U), maxU = chart.Max(c => c.U), minV = chart.Min(c => c.V), maxV = chart.Max(c => c.V);
            double scale = Math.Min((W - 0.06) / Math.Max(1, maxU - minU), (H - MapMargin) / Math.Max(1, maxV - minV));
            // Centred on the chart.
            _mapFit = ((minU + maxU) / 2, (minV + maxV) / 2, scale, ax, ay);
        }
        // A point of the world on the chart, in the cab's frame.
        Double3 On(double wx, double wz, double lift)
        {
            var (u, v) = Chart(wx, wz);
            return new(x0 + W / 2 + (u - _mapFit.U0) * _mapFit.Scale, y0 + H / 2 + (v - _mapFit.V0) * _mapFit.Scale, z + lift);
        }
        // Flat ink on paper: no surface treatment (it'd take the paper's colour for stone).
        var style = mesh.Style;
        mesh.Style = null;
        mesh.Emissive = 0.08f;
        draw(new Box(new Double3(x0, y0, z), new Double3(x0 + W, y0 + H, z + 0.008)), MapPaper);
        draw(new Box(new Double3(x0 - 0.015, y0 - 0.015, z - 0.002), new Double3(x0 + W + 0.015, y0 + H + 0.015, z + 0.004)), Palette.DeepBrown);
        MapChart(draw, x0, y0, z, W, H);
        foreach (var (dx, dz) in _mapDots)
            draw(Box.FromCentre(On(dx, dz, 0.01), new Double3(0.005, 0.005, 0.002)), MapInk);
        if (Route is { } route)
            foreach (var f in route.Features.Where(f => f.Kind is FeatureKind.Facility or FeatureKind.Village))
            {
                var p = MapStop(line, f, length);
                draw(Box.FromCentre(On(p.X, p.Z, 0.012), new Double3(0.009, 0.009, 0.003)), f.Kind == FeatureKind.Facility ? Palette.LampAmber : Palette.BoardEnamel);
            }
        // T121 playtest ("on bends on the map put a number there that shows the top speed the bend can be taken"): each
        // posted stretch inked over in red, and its board's figure in km/h beside it, on the outside of the bend. Where two
        // would print over each other the slower one wins: it's the one that derails you.
        // Note 266 (the director, 6 Oct: every point that can derail the train "clearly identifiable on the cab map as a
        // derailment point with its limit"): those (a bend, a weak bridge) in red, with a key; a limit that only costs the
        // engine (brass) in the map's ink, with what it is.
        var labelled = new List<(double X, double Y)>();
        bool anyDerails = false;
        foreach (var (s0, s1, kmh, why) in PostedBends(line, length).OrderBy(b => b.Kmh))
        {
            bool derails = why != "BRASS";
            anyDerails |= derails;
            var ink = derails ? MapLimit : MapInk;
            int steps = Math.Max(2, (int)((s1 - s0) / (length / 180)));
            for (int i = 0; i <= steps; i++)
            {
                var q = line.Sample(RailLine.MainPath, s0 + (s1 - s0) * i / steps).Position;
                draw(Box.FromCentre(On(q.X, q.Z, 0.011), new Double3(0.006, 0.006, 0.002)), ink);
            }
            var a = line.Sample(RailLine.MainPath, s0).Position;
            var b = line.Sample(RailLine.MainPath, s1).Position;
            var m = line.Sample(RailLine.MainPath, (s0 + s1) / 2).Position;
            var mid = On(m.X, m.Z, 0);
            // Out from the bend: from its chord's middle through the arc's; a straight stretch (a bridge) puts it above.
            var chord = (On(a.X, a.Z, 0) + On(b.X, b.Z, 0)) * 0.5;
            var outward = new Double3(mid.X - chord.X, mid.Y - chord.Y, 0);
            outward = outward.Length > 0.004 ? outward.Normalized : new Double3(0, 1, 0);
            string figure = kmh.ToString(System.Globalization.CultureInfo.InvariantCulture) + (why is null ? "" : " " + why);
            var font = BitmapFont.Default;
            const double px = 0.0045;
            double w = font.Measure(figure) * px, h = font.Height * px;
            double cx = mid.X + outward.X * (0.012 + w / 2), cy = mid.Y + outward.Y * (0.012 + h / 2);
            cx = Math.Clamp(cx, x0 + w / 2 + 0.005, x0 + W - w / 2 - 0.005);
            cy = Math.Clamp(cy, y0 + h / 2 + 0.005, y0 + H - h / 2 - 0.005);
            if (labelled.Any(l => Math.Abs(l.X - cx) < w + 0.01 && Math.Abs(l.Y - cy) < h + 0.008))
                continue;
            labelled.Add((cx, cy));
            MapFigure(draw, figure, cx - w / 2, cy + h / 2, z + 0.013, px, ink);
        }
        if (anyDerails)
        {
            // Top right, under the title: the bottom edge is behind the valve wheels from the driver's seat.
            const string key = "IN RED: KM/H OR OFF THE RAILS";
            MapFigure(draw, key, x0 + W - 0.024 - BitmapFont.Default.Measure(key) * 0.0035, y0 + H - 0.064, z + 0.011, 0.0035, MapLimit);
        }
        // A scale bar under it, bottom right: a kilometre (five, if one's too short to read), in ink.
        double km = _mapFit.Scale * 1000 >= 0.04 ? 1 : 5, bar = _mapFit.Scale * 1000 * km;
        if (bar < W * 0.4)
        {
            double bx1 = x0 + W - 0.03, bx0 = bx1 - bar, by = y0 + 0.022;
            draw(new Box(new Double3(bx0, by, z + 0.009), new Double3(bx1, by + 0.004, z + 0.011)), MapInk);
            for (int i = 0; i <= 2; i++)
                draw(new Box(new Double3(bx0 + bar * i / 2 - 0.0015, by, z + 0.009), new Double3(bx0 + bar * i / 2 + 0.0015, by + (i == 1 ? 0.007 : 0.011), z + 0.011)), MapInk);
            string label = $"{km:0} KM";
            MapFigure(draw, label, bx0 - BitmapFont.Default.Measure(label) * 0.004 - 0.008, by + 0.014, z + 0.011, 0.004, MapInk);
        }
        mesh.Emissive = 1;
        // The engine where it is: its distance is along its own path, which on a spur or an alternate isn't the main line's.
        var at = frame.Origin;
        float pulse = 0.7f + 0.3f * MathF.Sin((float)Time * 4);
        draw(Box.FromCentre(On(at.X, at.Z, 0.014), new Double3(0.013, 0.013, 0.003)), Palette.SignalRed * pulse);
        mesh.Emissive = 0;
        mesh.Style = style;
    }

    /// <summary>
    /// Where the run map marks a stop: where the train stands at it (its layout's stop point: a halt's platform, a yard's
    /// working track), not where its zone begins, 150 to 450 m short of it on frontier:7; a hand-laid stop's middle.
    /// </summary>
    public static Double3 MapStop(RailLine line, RouteFeature f, double length) =>
        f.Stop is { } stop ? Sim.Run.Run.StopWorld(line, f, stop.StopPoint)
            : line.Sample(RailLine.MainPath, Math.Clamp((f.Start + f.End) / 2, 0, length)).Position;

    // The chart's room above and below the line for its title and its scale bar (m of plate).
    const double MapMargin = 0.11;

    /// <summary>
    /// The chart round the line (the art pass on T98's map): a surveyor's sheet, a faint grid ruled over it, a double ink
    /// rule inside its edge, the night's name lettered across the top, and a brass drawing pin in each corner.
    /// </summary>
    void MapChart(Action<Box, Vector3> draw, double x0, double y0, double z, double W, double H)
    {
        var rule = MapPaper * 0.8f;
        for (int i = 1; i < 10; i++)
            draw(new Box(new Double3(x0 + W * i / 10 - 0.001, y0 + 0.012, z + 0.0085), new Double3(x0 + W * i / 10 + 0.001, y0 + H - 0.012, z + 0.009)), rule);
        for (int i = 1; i < 4; i++)
            draw(new Box(new Double3(x0 + 0.012, y0 + H * i / 4 - 0.001, z + 0.0085), new Double3(x0 + W - 0.012, y0 + H * i / 4 + 0.001, z + 0.009)), rule);
        foreach (var (inset, t) in new[] { (0.008, 0.0025), (0.013, 0.001) })
        {
            draw(new Box(new Double3(x0 + inset, y0 + inset, z + 0.009), new Double3(x0 + W - inset, y0 + inset + t, z + 0.0095)), MapInk);
            draw(new Box(new Double3(x0 + inset, y0 + H - inset - t, z + 0.009), new Double3(x0 + W - inset, y0 + H - inset, z + 0.0095)), MapInk);
            draw(new Box(new Double3(x0 + inset, y0 + inset, z + 0.009), new Double3(x0 + inset + t, y0 + H - inset, z + 0.0095)), MapInk);
            draw(new Box(new Double3(x0 + W - inset - t, y0 + inset, z + 0.009), new Double3(x0 + W - inset, y0 + H - inset, z + 0.0095)), MapInk);
        }
        // The night's name, as the depot lettered it (what the font has of it).
        var font = BitmapFont.Default;
        string name = new((Route?.Name ?? "the line").ToUpperInvariant().Select(c => char.IsLetterOrDigit(c) || c == ' ' ? c : ' ').ToArray());
        string title = $"RUN OF {name}".Trim();
        const double tp = 0.005;
        MapFigure(draw, title, x0 + 0.024, y0 + H - 0.022, z + 0.011, tp, MapInk);
        foreach (var (px, py) in new[] { (x0 + 0.006, y0 + 0.006), (x0 + W - 0.006, y0 + 0.006), (x0 + 0.006, y0 + H - 0.006), (x0 + W - 0.006, y0 + H - 0.006) })
        {
            draw(Box.FromCentre(new Double3(px, py, z + 0.012), new Double3(0.009, 0.009, 0.004)), Palette.TarnishedBrass);
            draw(Box.FromCentre(new Double3(px - 0.0015, py + 0.0015, z + 0.0145), new Double3(0.003, 0.003, 0.001)), Palette.TarnishedBrass * 1.6f);
        }
    }

    LinePlan? _bendsFor;
    Route? _bendsForRoute;
    List<(double S0, double S1, int Kmh, string? Why)> _bends = [];

    /// <summary>
    /// The main line's posted stretches, for the run map: a generated line's speed boards (LineBuilder.Signage: every bend
    /// that would derail the engine at full steam, and each demand's), its figure and the bend it stands before; a
    /// prototype route's own boards (Lineside) where there's no plan.
    /// </summary>
    List<(double S0, double S1, int Kmh, string? Why)> PostedBends(RailLine line, double length)
    {
        if (ReferenceEquals(_bendsForRoute, Route) && ReferenceEquals(_bendsFor, Route?.Plan))
            return _bends;
        _bendsForRoute = Route;
        _bendsFor = Route?.Plan;
        _bends = [];
        if (Route?.Plan is { } plan)
            _bends = LineGen.PostedSpeeds.Of(plan, line, length);
        else
            foreach (var sign in BoardList().Where(b => b.Kind == SignKind.SpeedLimit && b.Limit > 0))
                if (Math.Clamp(sign.End, 0, length) > Math.Clamp(sign.Start, 0, length))
                    _bends.Add((Math.Clamp(sign.Start, 0, length), Math.Clamp(sign.End, 0, length), sign.LimitKmh, sign.Bridge ? "BRIDGE" : null));
        return _bends;
    }

    /// <summary>A figure inked on the run map in the 5×7 font, from its top left; each row's runs of pixels one box.</summary>
    static void MapFigure(Action<Box, Vector3> draw, string text, double left, double top, double z, double px, Vector3? ink = null)
    {
        var font = BitmapFont.Default;
        double x = left;
        foreach (char ch in text)
        {
            var g = font.Glyph(ch);
            for (int gy = 0; gy < g.GetLength(0); gy++)
                for (int gx = 0; gx < g.GetLength(1); gx++)
                {
                    if (!g[gy, gx] || gx > 0 && g[gy, gx - 1])
                        continue;
                    int run = 1;
                    while (gx + run < g.GetLength(1) && g[gy, gx + run])
                        run++;
                    double y = top - gy * px;
                    draw(new Box(new Double3(x + gx * px, y - px, z), new Double3(x + (gx + run) * px, y, z + 0.002)), ink ?? MapLimit);
                }
            x += font.Advance * px;
        }
    }

    void CarWorkings(MeshBuilder mesh, CarFrame frame, Double3 eye, Action<Box, Vector3> draw)
    {
        var shape = frame.Shape;
        if (frame.Index == 0)
        {
            // Only read from the footplate: from farther off it's a pale square, and not worth its dots in the frame budget.
            if ((frame.Origin - eye).Length < 16)
                CabMap(mesh, frame, draw);
            mesh.Emissive = 1;
            // The fire, through the firehole: the back of the firebox dull with its light, and the bed of coals along the
            // bottom bright (the firehole's sides frame it, Art/TrainKit).
            // (Placed through the backhead's frame: behind its face is in the fire, wherever the backhead stands: note 276.)
            if (shape.Cab is not null && shape.Interactables.Any(i => i.Kind == InteractableKind.Firebox) && FireGlow > 0)
            {
                draw(Box.FromCentre(Art.TrainKit.InFirebox(shape, 0, 0.09), new Double3(0.32, 0.22, 0.02)), FireColour(0.03f + 0.18f * FireGlow) * 0.35f);
                // (Open, with the art pass's fire, the bed is its heap of coals: Art.Effects.Furnace.)
                if (!FireDoorOpen || Look?.Art.Effects is not { HasFlames: true })
                    draw(Box.FromCentre(Art.TrainKit.InFirebox(shape, -0.15, 0.07), new Double3(0.32, 0.07, 0.02)), FireColour(0.1f + 0.5f * FireGlow) * 0.7f);
            }
            mesh.Emissive = 0;
            // A Stoker in the fire: soot coming down in the cab (Art.Effects.SootFall).
            if (Look?.Art.Effects is { } sootFx && shape.Cab is { } sootCab && (frame.Origin - eye).Length < 40
                && Enemies?.Any(e => e.Kind == EnemyKind.Stoker && !e.Gone) == true)
            {
                var floor = frame.ToWorld(new Double3(0, sootCab.Min.Y, sootCab.Centre.Z)).RelativeTo(eye);
                sootFx.SootFall(mesh, floor, ToF(frame.Right), ToF(frame.Up), ToF(frame.Back),
                                    new Vector2((float)sootCab.HalfSize.X * 0.85f, (float)sootCab.HalfSize.Z * 0.85f), (float)(sootCab.Max.Y - sootCab.Min.Y), Time, 1);
            }
            // The door shut: its leaves over the hole, the fire only at the seam (Art.SceneArt.FireDoorShut).
            if (!FireDoorOpen && Look is not null)
                Look.Art.FireDoorShut(mesh, frame, eye, FireGlow, FireColour(1));
            // The door open, the art pass's fire: flames off the bed, cinders out of the hole, its light into the cab.
            // (Its "back", towards the cab, is out of the backhead's face: the engine's −Z, cab forward, note 276.)
            if (FireDoorOpen && Look?.Art.Effects is { HasFlames: true } fx && shape.Cab is not null && shape.Interactables.Any(i => i.Kind == InteractableKind.Firebox))
            {
                var bed = frame.ToWorld(Art.TrainKit.InFirebox(shape, -0.1, -0.03)).RelativeTo(eye);
                var toCab = ToF(frame.DirToWorld(Art.TrainKit.OutOfBackhead(shape)));
                var across = Vector3.Cross(ToF(frame.Up), toCab);
                fx.Furnace(mesh, bed, across, ToF(frame.Up), toCab, FireGlow, FireColour(1), Time, SinceShovel);
            }
            // The vent valve and the driver's levers: modelled by the art pass where it has them (SceneArt.CabControls).
            bool modelled = Look?.Art.CabControls(mesh, frame, eye, Controls, WrenchRacked, CordPulled, ShovelRacked, Time) == true;
            foreach (var i in shape.Interactables.Where(i => i.Kind == InteractableKind.Vent && !modelled))
                draw(Box.FromCentre(i.Position + new Double3(0, 1.1, 0), new Double3(0.12, 0.12, 0.04)), Palette.TarnishedBrass);
            // The driver's levers, their handles where the controls have them (T29): a headset player takes hold of
            // these. The regulator comes back as it opens, the brake handle as it goes on, the reverser forward for ahead.
            if (shape.Levers is { } levers && !modelled)
            {
                void Lever(Double3 handle, double rod)
                {
                    draw(Box.FromCentre(handle - new Double3(0, rod / 2, 0), new Double3(0.02, rod / 2, 0.02)), Palette.IronGrey);
                    draw(Box.FromCentre(handle, new Double3(0.07, 0.03, 0.03)), Palette.TarnishedBrass);
                }
                Lever(levers.RegulatorAt(Controls.Throttle), 0.3);
                Lever(levers.BrakeAt(Controls.Brake), 0.2);
                Lever(levers.ReverserAt(Controls.Reverser), 0.9);
            }
            // The powered switch thrower's lever (note 196), fitted: an iron stand from the floor and its handle in signal
            // red, the colour a lineside lever frame paints its points levers.
            if (SwitchThrower)
                foreach (var i in shape.Interactables.Where(i => i.Kind == InteractableKind.Points))
                {
                    draw(Box.FromCentre(i.Position + new Double3(0, 0.45, 0), new Double3(0.05, 0.45, 0.05)), Palette.IronGrey);
                    draw(Box.FromCentre(i.Position + new Double3(0, 0.95, 0), new Double3(0.02, 0.07, 0.16)), Palette.IronGrey);
                    draw(Box.FromCentre(i.Position + new Double3(0, 1.25, -0.06), new Double3(0.018, 0.28, 0.018)), Palette.SignalRed);
                    draw(Box.FromCentre(i.Position + new Double3(0, 1.55, -0.06), new Double3(0.03, 0.05, 0.03)), Palette.TarnishedBrass);
                }
        }
        if (shape.Interior is not null && Look is not null)
        {
            // Lanterns hanging where the car's lights are (the art pass's), and the car's fittings: its extinguisher's
            // board and cradle, a gun car's powder and shot.
            var fitted = Vehicles is { } lampsOf && frame.Index < lampsOf.Count ? lampsOf[frame.Index] : null;
            var bitten = Art.Bite.For(Look.Tuning.Bite, frame.Shape, fitted, frame.Index);
            Look.Art.CarLamps(mesh, frame, eye, Emergency, bitten, lit: !CarDark(frame.Index) && frame.Index < (Vehicles?.Count ?? int.MaxValue) - LampsOut);
            Look.Art.Fittings(mesh, frame, eye, fitted, bitten);
        }
        else if (shape.Interior is { } room)
        {
            // A lamp in every car: the warm interior against the hostile exterior (GDD §26).
            mesh.Emissive = 1;
            draw(Box.FromCentre(new Double3(0, room.Max.Y - 0.08, 0), new Double3(0.12, 0.06, 0.12)), Emergency ? EmergencyRed : Palette.LampAmber);
            mesh.Emissive = 0;
        }
    }

    static Vector3 PartColour(PartKind part, int car) => part switch
    {
        PartKind.Body => car % 3 == 0 ? Palette.RustRed : Palette.DeepBrown,
        PartKind.Chassis => Palette.Charcoal,
        PartKind.Boiler or PartKind.Stack => Palette.SootBlack,
        PartKind.CabWall or PartKind.CabRoof or PartKind.Coupler => Palette.IronGrey,
        PartKind.Tender => Palette.Charcoal,
        PartKind.Wall => car % 3 == 0 ? Palette.RustRed : Palette.DeepBrown,
        PartKind.Cargo => Palette.MuddyOlive,
        PartKind.Locker or PartKind.Steps => Palette.IronGrey,
        _ => Palette.IronGrey,
    };

    static Vector3 ToF(Double3 d) => new((float)d.X, (float)d.Y, (float)d.Z);
    static Double3 ToD(Vector3 v) => new(v.X, v.Y, v.Z);
}
