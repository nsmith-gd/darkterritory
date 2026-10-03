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
public sealed class GreyboxScene
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
    /// <summary>Emergency lighting (`dt screenshot --emergency`): the cars' lamps go to a dim red, the headlamp dark.</summary>
    public bool Emergency { get; set; }
    static readonly System.Numerics.Vector3 EmergencyRed = new(0.5f, 0.06f, 0.04f);
    /// <summary>Tunnels, bridges, facilities and hazards to draw along the line, when it's a generated route.</summary>
    public Route? Route { get; set; }
    /// <summary>The engine's lamp is lit (it's what makes the boards shine back, sight.json).</summary>
    public bool LampLit { get; set; } = true;
    /// <summary>GDD v1.4 App. E.9: this many of the cars' lamps are out, from the last car forward (all of them: the engine's too).</summary>
    public int LampsOut { get; set; }
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
    /// <summary>Seconds, for animating things that move on their own.</summary>
    public double Time { get; set; }
    /// <summary>Vehicle state for doors (open or shut). Without it every door is drawn shut.</summary>
    public IReadOnlyList<Vehicle>? Vehicles { get; set; }
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
    /// <summary>The blow-off's open (the boiler's <c>Vented</c>), and the safety valve's lifting: their steam (T101).</summary>
    public bool Venting { get; set; }
    public bool SafetyValve { get; set; }
    /// <summary>The art pass's surfaces (T39, look.json). Unset, the greybox is flat colour.</summary>
    public Look? Look { get; set; }

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

    public void Build(MeshBuilder mesh, TrainOnLine train, Double3 eye)
    {
        Vehicles ??= train.Vehicles;
        Cut = Art.SceneArt.Cuts(train);
        Handrails = train.Dynamics.Tuning.Composition.Handrails;
        Build(mesh, train.Line, train.Frames, train.Dynamics.Distance, eye);
    }

    /// <param name="frames">Car frames to draw, e.g. interpolated between ticks.</param>
    /// <param name="hint">Any distance along the line near the eye, to start the nearest-point search.</param>
    public void Build(MeshBuilder mesh, RailLine line, IReadOnlyList<CarFrame> frames, double hint, Double3 eye)
    {
        mesh.Clear();
        _speed = frames.Count == 0 ? 0 : Vector3.Dot(ToF(frames[0].Velocity), ToF(frames[0].Back * -1));
        if (Look is not null)
            Look.Art.Breath = Look.Tuning.Atmosphere.Cold.Breath(Cold);
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
                    if (site is not null && (site.Has(Sim.Run.ModuleKind.Spout) || site.Has(Sim.Run.ModuleKind.Ramp) || site.Has(Sim.Run.ModuleKind.Hose))
                        && (site.Track.Sample(site.Mid).Position - eye).Length < DrawDistance + 120)
                        SetPieces(mesh, site, frames, eye, Time);
                    // The wreck yard's heaps (note 187): the last train's cars on their sides, groaning when they're going to go.
                    if (site is { Heaps.Count: > 0 } && (site.Heaps[0].Centre - eye).Length < DrawDistance + 120)
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
            // Each Holdout's way in, shut or broken open (App. D.7): its door, lock or barricade by its state.
            if (Holdouts is not null && Look is not null)
                Look.Art.World.Entrances(mesh, line, Route, Holdouts, eye, (float)ValleyDepth);
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
                }
            // GDD §9: the fortress yard behind the gates, and the terminus: "lights, then walls, then gun towers".
            double yard = Run?.YardLength ?? 600, terminus = Run?.Tuning.TerminusZone ?? 400;
            Fortress(mesh, line, eye, from, to, 0, yard, gateAt: yard);
            double home = Route.Plan?.Terminus.GateM ?? line.Length - terminus - 200;
            Fortress(mesh, line, eye, from, to, home, line.Length, gateAt: home, lit: Route.Plan?.Terminus.Silent != true);
            Lap(mesh, "route");
        }
        // Practical lights first, so everything built after is lit by them: each car's lamps, the firebox,
        // and any hand lamp lying about or being carried.
        if (Bodies is not null)
            foreach (var b in Bodies.Where(b => b.Kind == Sim.Physics.BodyKind.Lamp))
                // (Carried, where it swings in the carrier's fist: Art.SceneArt.LampInHand.)
                if (((b.Carrier >= 0 ? Look?.Art.LampInHand(b.Carrier, Time) : null) ?? BodyWorld(b, frames, b.Centre)) is { } at && (at - eye).Length < 60)
                    mesh.PointLights.Add(new PointLight(V(at, eye), Palette.LampAmber * 1.8f, 7f));
        if (Lights is not null)
            foreach (var (at, colour, radius) in Lights)
                mesh.PointLights.Add(new PointLight(V(at, eye), colour, radius));
        foreach (var frame in frames)
        {
            if ((frame.Origin - eye).Length > LampRange)
                continue;
            // A switchyard's cars standing as the night found them have nobody to light them (note 187).
            bool dark = frame.Index >= frames.Count - LampsOut
                || Vehicles is { } fleetLit && frame.Index < fleetLit.Count && fleetLit[frame.Index] is { YardCar: true, LampLit: false };
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
        foreach (var frame in frames)
            if (CutAway?.Contains(frame.Index) != true)
                Car(mesh, frame, eye);
        Lap(mesh, "cars");
        if (Look is not null)
        {
            // The art pass's effects (Art/Effects): smoke, steam, sparks, the lamp's beam, and fog banks along the line.
            Look.Art.Effects.Train(mesh, frames, eye, Time, Controls, FireGlow, Emergency, Venting, SafetyValve,
                frames.Count == 0 ? default : Art.Bite.For(Look.Tuning.Bite, frames[^1].Shape, Vehicles is { } fleet && frames[^1].Index < fleet.Count ? fleet[frames[^1].Index] : null, frames[^1].Index));
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
            // The air of a corrupted stretch: ash, spores (GDD §30).
            Look.Art.Effects.Corruption(mesh, eye, Time, StagedAir ?? Art.Effects.AirOf(Art.WorldArt.BiomeAt(Route, centre)));
            Strikes(mesh, Look.Art.Effects, frames, eye);
        }
        Lap(mesh, "effects");
        mesh.Seed = 0;
        // The firebox door, open or shut, for a Stoker in the fire (Art/CreatureArt.FireDoorOpen).
        if (Look?.Art.Creatures is { } creatures && frames.Count > 0 && frames[0].Shape.Cab is not null)
            creatures.FireDoorOpen = FireDoorOpen
                ? Art.TrainKit.FireDoor(frames[0].Shape) - ToF(frames[0].Shape.Interactables.First(i => i.Kind == InteractableKind.Firebox).Position)
                : null;
        if (Enemies is not null)
            foreach (var e in Enemies)
                if (e.Kind == EnemyKind.Passenger && Look?.Art.Creatures is { } passengers && passengers.Get("passenger") is not null)
                {
                    // The art pass's own (Art/CreatureArt, note 124): a conductor off a lost train, in the colour of the
                    // crewmate it copies; it walks as fast as it's been going (GreyboxScene's pace).
                    if (!e.Gone)
                        DrawEnemy(mesh, line, frames, e, eye, from, to, passengers, default, null, null, Pace(e), Flinch(e));
                }
                else if (e is Sim.Enemies.Passenger passenger)
                {
                    // One of the crew, to look at (App. A.7 BLEND): drawn exactly as they are, their face and all.
                    if (!e.Gone && AsCrewmate(passenger, frames) is { } double_ && Look?.Art.Crewmate(mesh, double_, eye, Time) != true)
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
                    // A Shy Thing faces the one it has; a Huddle, who it's after.
                    var after = e.Kind is EnemyKind.TippyToesie or EnemyKind.Ribbit or EnemyKind.Choir or EnemyKind.Gaunt or EnemyKind.Follower
                        or EnemyKind.ShyThing or EnemyKind.Huddle && e.Extra >= 0
                        ? Crew?.FirstOrDefault(c => c.Id == (int)e.Extra)
                        : (e.Kind == EnemyKind.Grumbler && e.Phase >= SpinePhase.Commit || e.Kind == EnemyKind.SootChildren && e.Phase is SpinePhase.Grab or SpinePhase.Punish)
                            && Crew is { } crew && crew.Any(c => c.Alive)
                            ? crew.Where(c => c.Alive).MinBy(c => (c.Feet - EnemyWorld(e, frames)).Length)
                            : null;
                    Art.CreatureArt.Prey? prey = after is { } victim
                        ? new(V(victim.Feet, eye), new Vector3((float)-Math.Sin(victim.Yaw), 0, (float)-Math.Cos(victim.Yaw)))
                        : null;
                    // And stoops under a roof, ducks through a door (note 110); a Gaunt gets down (note 118).
                    Art.CreatureArt.Room? room = e.Kind is EnemyKind.TippyToesie or EnemyKind.Gaunt && e.Attached >= 0 && e.Attached < frames.Count
                        ? Art.CreatureArt.Room.Of(frames[e.Attached].Shape, e.Local)
                        : null;
                    DrawEnemy(mesh, line, frames, e, eye, from, to, Look?.Art.Creatures, bite, prey, room, e.Kind is EnemyKind.Gaunt or EnemyKind.Grumbler ? Pace(e) : 0, Flinch(e));
                }
        Lap(mesh, "enemies");
        if (Bodies is not null)
        {
            // Heavy crates only come from a facility's site, so its size is there (facilities.json "heavy").
            double heavyHalf = Run?.Sites.FirstOrDefault(x => x is not null)?.HeavyRadius ?? 0.5;
            var mimics = Enemies?.OfType<Sim.Enemies.Mimic>().Where(m => !m.Gone).GroupBy(m => m.BodyId).ToDictionary(g => g.Key, g => g.First());
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
                if (Look?.Art.Body(mesh, frames, b, eye, heavyHalf, Time) != true)
                    DrawBody(mesh, frames, b, eye, heavyHalf);
                // A Mimic is a crate until it isn't (GDD v1.5 §21): its lid, over the crate.
                if (mimics?.GetValueOrDefault(b.Id) is { } mimic)
                    DrawMimic(mesh, frames, b, mimic, eye);
                if (held)
                {
                    b.Parent = parent;
                    b.Pbd.Particles[0].Position = at;
                    b.Yaw = yaw;
                }
            }
        }
        Extinguishing(mesh, frames, eye);
        // The yards' crate counts, chalked on their boards (level-design P12), near enough to read.
        if (Run is { } countRun)
            foreach (var (at, along, count) in countRun.CrateCounts())
                if ((at - eye).Length < 90)
                    DrawCrateCount(mesh, at, along, count, eye);
        if (Crew is not null)
            foreach (var c in Crew)
                if (c.Alive && Look?.Art.Crewmate(mesh, c, eye, Time) != true) // the dead are drawn as their bodies
                    DrawCrewmate(mesh, c, eye);
        if (Own is { } own)
            Look?.Art.OwnArms(mesh, own, Time);
        Lap(mesh, "bodies and crew");
    }

    /// <summary>
    /// What landed (T121), timed from the sim's tick as a gun's muzzle flash is: each cannonball's explosion where it came
    /// down, and each blow or ball's pop and flash on the creature it landed on.
    /// </summary>
    void Strikes(MeshBuilder mesh, Art.Effects fx, IReadOnlyList<CarFrame> frames, Double3 eye)
    {
        if (Tick < 0)
            return;
        if (Impacts is not null)
            foreach (var i in Impacts)
            {
                double age = (Tick - i.Tick) * Sim.SimConstants.TickSeconds;
                if (age < 0 || age > Art.Effects.ImpactSeconds || (i.At - eye).Length > DrawDistance)
                    continue;
                fx.CannonImpact(mesh, V(i.At, eye), ToF(i.Direction), i.Surface, i.Struck, age, i.Id);
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

    /// <summary>Staged: the air to draw whatever the biome (dt screenshot --air ash|spores).</summary>
    public Art.Effects.Air? StagedAir { get; set; }

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

    /// <summary>Extinguishers at work (App. C.5), from what's replicated: a fire going down, an extinguisher carried within reach of it.</summary>
    void Extinguishing(MeshBuilder mesh, IReadOnlyList<CarFrame> frames, Double3 eye)
    {
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
                    fx.Spray(mesh, nozzle, foot, Time);
            }
        }
    }

    /// <summary>
    /// The Passenger as the crewmate whose face it wears: their look, walking as they would. Its id is theirs offset by 200,
    /// so the gait each figure is smoothed by is its own and not the one it copies.
    /// </summary>
    public static Crewmate? AsCrewmate(Sim.Enemies.Passenger p, IReadOnlyList<CarFrame> frames)
    {
        if (p.Attached < 0 || p.Attached >= frames.Count)
            return null;
        var frame = frames[p.Attached];
        var forward = frame.DirToWorld(new Double3(-Math.Sin(p.Extra2), 0, -Math.Cos(p.Extra2)));
        return new Crewmate((byte)(200 + p.Looks), frame.ToWorld(p.Local), Math.Atan2(-forward.X, -forward.Z), true, Looks: p.Looks);
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
            if (b.Kind == Sim.Physics.BodyKind.Embers)
                DrawEmbers(mesh, V(at, eye), right, ToF(up), back, 0, b.Id);
            else if (b.Kind == Sim.Physics.BodyKind.Cargo)
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

    /// <summary>A crewmate: coat, head and a lamp at the chest so you can find each other in the dark. Dead ones lie down.</summary>
    static void DrawCrewmate(MeshBuilder mesh, Crewmate c, Double3 eye)
    {
        var right = new Vector3((float)Math.Cos(c.Yaw), 0, (float)-Math.Sin(c.Yaw));
        var back = new Vector3((float)Math.Sin(c.Yaw), 0, (float)Math.Cos(c.Yaw));
        var o = V(c.Feet, eye);
        if (!c.Alive)
        {
            mesh.Box(o + Vector3.UnitY * 0.15f, right, Vector3.UnitY, back, new Vector3(0.25f, 0.15f, 0.9f), Palette.DeepBrown);
            return;
        }
        mesh.Box(o + Vector3.UnitY * 0.45f, right, Vector3.UnitY, back, new Vector3(0.16f, 0.45f, 0.12f), Palette.Charcoal);
        mesh.Box(o + Vector3.UnitY * 1.2f, right, Vector3.UnitY, back, new Vector3(0.24f, 0.33f, 0.15f), Palette.DeepBrown);
        mesh.Box(o + Vector3.UnitY * 1.68f, right, Vector3.UnitY, back, new Vector3(0.12f, 0.13f, 0.12f), Palette.Corrupted);
        // Arms (T47): to a headset player's hands where they are, hanging for everyone else.
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
        var (left, rightHand) = Arms.Hands(c.Hand, c.Other);
        foreach (var (armSide, target) in new[] { (-1, left), (1, rightHand) })
        {
            var shoulder = Arms.Shoulder(armSide);
            var (elbow, hand) = Arms.Solve(shoulder, target, Arms.Pole(armSide));
            Segment(shoulder, elbow, 0.06f, Palette.DeepBrown);
            Segment(elbow, hand, 0.05f, Palette.DeepBrown);
            // A glove, a shade lighter: the hand is what the others watch.
            mesh.Box(At(hand), right, Vector3.UnitY, back, new Vector3(0.05f, 0.05f, 0.05f), Palette.Corrupted * 0.8f);
        }
        mesh.Emissive = 1;
        mesh.Box(o + Vector3.UnitY * 1.3f - back * 0.16f, right, Vector3.UnitY, back, new Vector3(0.05f, 0.05f, 0.02f), Palette.LampAmber);
        mesh.Emissive = 0;
    }

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
        (Vector3 Push, Quaternion Tip) flinch = default)
    {
        // A basis for the enemy: its car's, or the line's at its distance.
        Double3 origin, right, up = Double3.Up, back;
        if (e.Attached >= 0)
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
        // Flinching from a blow (T121): knocked back and tipped away from it, about its feet.
        if (flinch.Tip != default && flinch.Tip != Quaternion.Identity)
        {
            o += flinch.Push;
            (r, u, b) = (Vector3.Transform(r, flinch.Tip), Vector3.Transform(u, flinch.Tip), Vector3.Transform(b, flinch.Tip));
        }
        // The art pass's creature, where it has one (Art/CreatureArt): the same place, the thing itself.
        if (creatures is not null && creatures.Enemy(mesh, Art.CreatureArt.Basis(o, r, u, b), e, bite, prey, room, pace))
            return;
        Vector3 L(double x, double y, double z) => o + r * (float)x + u * (float)y + b * (float)z;
        void Draw(double x, double y, double z, double hx, double hy, double hz, Vector3 colour) =>
            mesh.Box(L(x, y, z), r, u, b, new Vector3((float)hx, (float)hy, (float)hz), colour);
        float pulse = (float)(0.5 + 0.5 * Math.Sin(e.PhaseSeconds * 9));

        if (Art.IncidentArt.Draw(mesh, o, r, u, b, e.Kind, e.Phase, e.PhaseSeconds, e.Extra, e.Health))
            return;
        switch (e.Kind)
        {
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
            case EnemyKind.ShyThing:
                Art.StandIns.ShyThing(mesh, o, r, u, b, e.Phase, e.PhaseSeconds, e.GrabWindow, prey?.Feet);
                break;
            case EnemyKind.Huddle:
                Art.StandIns.Huddle(mesh, o, r, u, b, e.Phase, e.PhaseSeconds, e.Health, e.Id);
                break;
            // A Mimic is its crate (drawn with the bodies: DrawMimic).
            case EnemyKind.Stoker or EnemyKind.CarFire or EnemyKind.Mimic:
                break;
            default:
                // A figure, where the greybox has nothing of its own for it: tall, dark, and a pale head.
                Draw(0, 0.6, 0, 0.14, 0.6, 0.12, Palette.SootBlack);
                Draw(0, 1.35, 0, 0.1, 0.12, 0.1, Palette.Corrupted * 0.6f);
                break;
        }
    }

    /// <summary>
    /// A Mimic's crate (GDD v1.5 §21), drawn over the crate the bodies draw. Shut, nothing: it's a crate. Breathing (Extra2),
    /// its lid lifts a finger's width and settles, slowly. Waking on someone (the telegraph) the lid lifts at the front on
    /// its back hinges, a black gape under it and teeth round the rim; on them, wide.
    /// </summary>
    void DrawMimic(MeshBuilder mesh, IReadOnlyList<CarFrame> frames, Sim.Physics.Body body, Sim.Enemies.Mimic m, Double3 eye)
    {
        if (BodyWorld(body, frames, body.Pbd.Particles[0].Position) is not { } at)
            return;
        double angle = m.Phase switch
        {
            SpinePhase.Telegraph => 0.08 + 0.45 * Math.Clamp(m.PhaseSeconds / 1.8, 0, 1),
            SpinePhase.Commit or SpinePhase.Grab or SpinePhase.Punish => 0.75,
            _ => m.Breathing ? 0.025 * (0.5 + 0.5 * Math.Sin(Time * 2 * Math.PI * 0.28)) : 0,
        };
        if (angle <= 0.002)
            return;
        bool onCar = body.Parent >= 0 && body.Parent < frames.Count;
        var upD = onCar ? frames[body.Parent].Up : Double3.Up;
        double yaw = body.Yaw + (onCar ? frames[body.Parent].Heading : 0);
        var right = new Vector3((float)Math.Cos(yaw), 0, (float)-Math.Sin(yaw));
        var back = new Vector3((float)Math.Sin(yaw), 0, (float)Math.Cos(yaw));
        var up = ToF(upD);
        var forward = -back;
        const float half = 0.45f;
        var o = V(at, eye);
        // The lid, hinged along its back edge, turned up by the angle about the crate's right.
        var hinge = o + up * half + back * half;
        float c = (float)Math.Cos(angle), sn = (float)Math.Sin(angle);
        var lidForward = forward * c + up * sn;
        var lidUp = up * c - forward * sn;
        mesh.Box(hinge + lidForward * half + lidUp * 0.035f, right, lidUp, -lidForward, new Vector3(half + 0.01f, 0.035f, half + 0.01f), Palette.BlueGrey * 0.9f);
        // Inside: the black of a mouth, and teeth round its rim, on the lid's edge and the crate's.
        float gape = 2 * half * sn;
        mesh.Box(o + up * (half + gape * 0.45f) + forward * (half * 0.55f), right, up, back, new Vector3(half * 0.92f, gape * 0.45f, half * 0.4f), Palette.SootBlack);
        if (angle > 0.06)
            for (int i = -4; i <= 4; i++)
            {
                var along = right * (i * 0.095f);
                mesh.Box(hinge + lidForward * (2 * half - 0.03f) - lidUp * 0.03f + along, right, lidUp, -lidForward, new Vector3(0.018f, 0.04f, 0.012f), Palette.BoardEnamel);
                mesh.Box(o + up * (half + 0.035f) + forward * (half - 0.03f) + along, right, up, back, new Vector3(0.018f, 0.04f, 0.012f), Palette.BoardEnamel);
            }
    }

    /// <summary>
    /// A shovelful of live coals flung out of the firebox (GDD v1.5 §21, the Huddle's counter): a low heap of embers, glowing,
    /// and their light on whatever's round them.
    /// </summary>
    static void DrawEmbers(MeshBuilder mesh, Vector3 o, Vector3 right, Vector3 up, Vector3 back, double time, int id)
    {
        float flicker = 0.8f + 0.2f * (float)Math.Sin(time * 7.3 + id) * (float)Math.Sin(time * 3.1 + id * 0.5);
        mesh.Emissive = 1;
        for (int i = 0; i < 6; i++)
        {
            double a = i * 2.39996 + id;
            var at = o + right * (float)(Math.Cos(a) * 0.12 * (1 + i % 2)) + back * (float)(Math.Sin(a) * 0.12 * (1 + i % 2)) - up * 0.08f;
            mesh.Box(at, right, up, back, new Vector3(0.06f, 0.04f, 0.05f), (i % 2 == 0 ? Palette.FurnaceOrange : Palette.LampAmber) * (0.7f + 0.3f * flicker));
        }
        mesh.Emissive = 0;
        mesh.PointLights.Add(new PointLight(o + up * 0.3f, Palette.FurnaceOrange * (0.9f * flicker), 4.5f));
    }

    /// <summary>
    /// A yard crate stack's count (level-design P12: "crate counts shown"), chalked on a board on a post at the head of its
    /// row: tallies in fives, white enough to read in a hand lamp. One crate more than the tally is the Mimic (GDD v1.5).
    /// </summary>
    static void DrawCrateCount(MeshBuilder mesh, Double3 at, Double3 along, int count, Double3 eye)
    {
        var o = V(at, eye);
        var a = ToF(along);
        var up = Vector3.UnitY;
        var side = Vector3.Normalize(Vector3.Cross(up, a));
        mesh.Box(o + up * 0.5f, side, up, a, new Vector3(0.04f, 0.5f, 0.04f), Palette.DeepBrown);
        var board = o + up * 1.0f - a * 0.06f;
        mesh.Box(board, side, up, a, new Vector3(0.34f, 0.17f, 0.02f), Palette.Charcoal);
        mesh.Emissive = 0.35f;
        var chalk = Palette.BoardEnamel * 1.1f;
        // On both faces: it's read from whichever side the crew come up to the stack.
        foreach (float facing in new[] { -1f, 1f })
        {
            var face = board + a * (0.025f * facing);
            var across = side * -facing;
            for (int k = 0; k < Math.Min(count, 15); k++)
            {
                int group = k / 5, stroke = k % 5;
                float gx = -0.26f + group * 0.19f;
                if (stroke < 4)
                    mesh.Box(face + across * (gx + stroke * 0.035f), side, up, a, new Vector3(0.006f, 0.08f, 0.004f), chalk);
                else
                {
                    float t = 0.9f;
                    var s2 = across * MathF.Cos(t) + up * MathF.Sin(t);
                    var u2 = up * MathF.Cos(t) - across * MathF.Sin(t);
                    mesh.Box(face + across * (gx + 0.0525f), s2, u2, a, new Vector3(0.1f, 0.006f, 0.004f), chalk);
                }
            }
        }
        mesh.Emissive = 0;
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

    void Track(MeshBuilder mesh, RailLine line, Double3 eye, double from, double to, double centre)
    {
        if (Look is not null)
        {
            // The art pass's line (WorldArt): the ground, the track and the lineside, cooked in cells. Nothing wild grows
            // inside the fortresses' walls (T100): they're told where those are before a cell's built.
            if (Route is not null)
                Look.Art.World.Walls = (Run?.YardLength ?? 600, Route.Plan?.Terminus.GateM ?? line.Length - (Run?.Tuning.TerminusZone ?? 400) - 200);
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
            Look.Art.World.Branch(mesh, branch, eye, DrawDistance, at, main.Sample(branch.Toe).Tangent, Diverging?.Invoke(branch.Index) ?? false);
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

    /// <summary>Walls both sides, gun towers with lamps, and a gatehouse over the line.</summary>
    void Fortress(MeshBuilder mesh, RailLine line, Double3 eye, double from, double to, double start, double end, double gateAt, bool lit = true)
    {
        if (Look is not null)
        {
            Look.Art.World.Fortress(mesh, line, eye, from, to, start, end, gateAt, platform: start == 0, lit);
            // Its people (T100): a few about at night, in their own drab, idling on their own beat. A dark town has none.
            if (lit)
                foreach (var (feet, facing, variant) in Art.WorldArt.FortFolk(line, eye, Math.Max(start, from - 20), Math.Min(end, to + 20), gateAt, start == 0))
                {
                    var back = -ToF(facing);
                    var right = Vector3.Cross(Vector3.UnitY, back);
                    var m = Art.CreatureArt.Basis(V(feet, eye), right, Vector3.UnitY, back);
                    float drab = 0.45f + variant % 3 * 0.05f;
                    Look.Art.Creatures.Draw(mesh, "crew", "idle", Time + variant * 0.73, true, m, variant, seed: variant * 13,
                        adjust: (_, l) => l with { Colour = l.Colour * new Vector3(drab, drab * 0.95f, drab * 0.9f) });
                }
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
    static void SetPieces(MeshBuilder mesh, Sim.Run.Site site, IReadOnlyList<CarFrame> frames, Double3 eye, double time)
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
        if (site.Has(Sim.Run.ModuleKind.Spout))
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
        if (site.Has(Sim.Run.ModuleKind.Ramp))
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
        if (site.Has(Sim.Run.ModuleKind.Hose))
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
    void Facility(MeshBuilder mesh, RailLine line, Double3 eye, RouteFeature f) =>
        FacilityBuildings(mesh, line, eye, f.Facility, (f.Start + f.End) / 2, f.Side, push: 0);

    /// <summary>A facility's buildings beside a track (the main line, or its spur), centred along it at <paramref name="mid"/>.</summary>
    void FacilityBuildings(MeshBuilder mesh, RailLine line, Double3 eye, FacilityKind? kind, double mid, double side, double push)
    {
        if (Look is not null)
        {
            Look.Art.World.Facility(mesh, line, eye, kind, mid, side, push);
            return;
        }
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
        bool utility = Utility?.Invoke(frame.Index) == true || vehicle is { Kind: VehicleKind.Utility };
        // A lived-in car's stove smoking through its pipe: a utility car's, and the guard van's (TrainKit).
        if (Look is not null && frame.Shape.Interior is { } inside && frame.Shape.Cab is null && (utility || frame.Shape.Gun is not null))
        {
            var pipe = utility
                ? Art.TrainKit.StoveAt(frame.Shape) with { Y = (float)frame.Shape.RoofHeight + 0.9f }
                : new Vector3((float)frame.Shape.HalfWidth - 0.35f, (float)frame.Shape.RoofHeight + 0.9f, -(float)frame.Shape.HalfLength + 2.6f);
            Look.Art.Effects.StoveSmoke(mesh, o + right * pipe.X + up * pipe.Y + back * pipe.Z, up, back, (float)_speed, Time, frame.Index);
        }
        if (Look is not null && Look.Art.Car(mesh, frame, eye, vehicle, Emergency, Tick, CutEnds(frame.Index), burnt?.Char ?? 0, utility, openLockers, Handrails))
        {
            CarWorkings(mesh, frame, eye, Draw);
            if (engine)
            {
                // The dials: pressure from the boiler, heat from the fire, the water glass (no water model yet: steady),
                // and speed against the line's 80 km/h top.
                float speed = (float)frame.Velocity.Length / 22.2f;
                Look.Art.Gauges(mesh, frame, eye, [Pressure, FireGlow, 0.72f, speed]);
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
            // The headlamp: dark under emergency lighting.
            mesh.Emissive = 1;
            Draw(Box.FromCentre(new Double3(0, 2.8, -half - 0.05), new Double3(0.35, 0.35, 0.1)), Emergency ? Palette.LampAmber * 0.08f : Palette.LampAmber);
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
            double scale = Math.Min((W - 0.06) / Math.Max(1, maxU - minU), (H - 0.06) / Math.Max(1, maxV - minV));
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
        foreach (var (dx, dz) in _mapDots)
            draw(Box.FromCentre(On(dx, dz, 0.01), new Double3(0.005, 0.005, 0.002)), MapInk);
        if (Route is { } route)
            foreach (var f in route.Features.Where(f => f.Kind is FeatureKind.Facility or FeatureKind.Village))
            {
                var p = line.Sample(RailLine.MainPath, Math.Clamp(f.Start, 0, length)).Position;
                draw(Box.FromCentre(On(p.X, p.Z, 0.012), new Double3(0.009, 0.009, 0.003)), f.Kind == FeatureKind.Facility ? Palette.LampAmber : Palette.BoardEnamel);
            }
        // T121 playtest ("on bends on the map put a number there that shows the top speed the bend can be taken"): each
        // posted stretch inked over in red, and its board's figure in km/h beside it, on the outside of the bend. Where two
        // would print over each other the slower one wins: it's the one that derails you.
        var labelled = new List<(double X, double Y)>();
        foreach (var (s0, s1, kmh) in PostedBends(line, length).OrderBy(b => b.Kmh))
        {
            int steps = Math.Max(2, (int)((s1 - s0) / (length / 180)));
            for (int i = 0; i <= steps; i++)
            {
                var q = line.Sample(RailLine.MainPath, s0 + (s1 - s0) * i / steps).Position;
                draw(Box.FromCentre(On(q.X, q.Z, 0.011), new Double3(0.006, 0.006, 0.002)), MapLimit);
            }
            var a = line.Sample(RailLine.MainPath, s0).Position;
            var b = line.Sample(RailLine.MainPath, s1).Position;
            var m = line.Sample(RailLine.MainPath, (s0 + s1) / 2).Position;
            var mid = On(m.X, m.Z, 0);
            // Out from the bend: from its chord's middle through the arc's; a straight stretch (a bridge) puts it above.
            var chord = (On(a.X, a.Z, 0) + On(b.X, b.Z, 0)) * 0.5;
            var outward = new Double3(mid.X - chord.X, mid.Y - chord.Y, 0);
            outward = outward.Length > 0.004 ? outward.Normalized : new Double3(0, 1, 0);
            string figure = kmh.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var font = BitmapFont.Default;
            const double px = 0.0045;
            double w = font.Measure(figure) * px, h = font.Height * px;
            double cx = mid.X + outward.X * (0.012 + w / 2), cy = mid.Y + outward.Y * (0.012 + h / 2);
            cx = Math.Clamp(cx, x0 + w / 2 + 0.005, x0 + W - w / 2 - 0.005);
            cy = Math.Clamp(cy, y0 + h / 2 + 0.005, y0 + H - h / 2 - 0.005);
            if (labelled.Any(l => Math.Abs(l.X - cx) < w + 0.01 && Math.Abs(l.Y - cy) < h + 0.008))
                continue;
            labelled.Add((cx, cy));
            MapFigure(draw, figure, cx - w / 2, cy + h / 2, z + 0.013, px);
        }
        mesh.Emissive = 1;
        var at = line.Sample(RailLine.MainPath, Math.Clamp(_hint, 0, length)).Position;
        float pulse = 0.7f + 0.3f * MathF.Sin((float)Time * 4);
        draw(Box.FromCentre(On(at.X, at.Z, 0.014), new Double3(0.013, 0.013, 0.003)), Palette.SignalRed * pulse);
        mesh.Emissive = 0;
        mesh.Style = style;
    }

    LinePlan? _bendsFor;
    Route? _bendsForRoute;
    List<(double S0, double S1, int Kmh)> _bends = [];

    /// <summary>
    /// The main line's posted stretches, for the run map: a generated line's speed boards (LineBuilder.Signage: every bend
    /// that would derail the engine at full steam, and each demand's), its figure and the bend it stands before; a
    /// prototype route's own boards (Lineside) where there's no plan.
    /// </summary>
    List<(double S0, double S1, int Kmh)> PostedBends(RailLine line, double length)
    {
        if (ReferenceEquals(_bendsForRoute, Route) && ReferenceEquals(_bendsFor, Route?.Plan))
            return _bends;
        _bendsForRoute = Route;
        _bendsFor = Route?.Plan;
        _bends = [];
        if (Route?.Plan is { } plan)
        {
            foreach (var b in plan.Signage.Where(b => b is { Type: "speedBoard", Edge: "main", Required: true, Value: > 0 }))
            {
                // The bend it stands before: the sharpest curve in the next 600 m, and as far either side as it's nearly as sharp.
                double kMax = 0, sMax = b.S;
                for (double s = b.S; s <= Math.Min(length, b.S + 600); s += 5)
                {
                    double k = Math.Abs(line.Sample(RailLine.MainPath, s).Curvature);
                    if (k > kMax)
                        (kMax, sMax) = (k, s);
                }
                if (kMax < 1e-6)
                    continue;
                double s0 = sMax, s1 = sMax;
                while (s0 > b.S && Math.Abs(line.Sample(RailLine.MainPath, s0 - 5).Curvature) > kMax * 0.6)
                    s0 -= 5;
                while (s1 < length && s1 < b.S + 900 && Math.Abs(line.Sample(RailLine.MainPath, s1 + 5).Curvature) > kMax * 0.6)
                    s1 += 5;
                // The board's own figure (rounded down to the 5 the boards are painted in), so the map and the board agree.
                int kmh = int.TryParse(b.Text, System.Globalization.CultureInfo.InvariantCulture, out int painted) ? painted : (int)(b.Value!.Value * 3.6);
                _bends.Add((s0, Math.Max(s1, s0 + 10), kmh));
            }
        }
        else
            foreach (var sign in BoardList().Where(b => b.Kind == SignKind.SpeedLimit && b.Limit > 0))
                if (Math.Clamp(sign.End, 0, length) > Math.Clamp(sign.Start, 0, length))
                    _bends.Add((Math.Clamp(sign.Start, 0, length), Math.Clamp(sign.End, 0, length), sign.LimitKmh));
        return _bends;
    }

    /// <summary>A figure inked on the run map in the 5×7 font, from its top left; each row's runs of pixels one box.</summary>
    static void MapFigure(Action<Box, Vector3> draw, string text, double left, double top, double z, double px)
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
                    draw(new Box(new Double3(x + gx * px, y - px, z), new Double3(x + (gx + run) * px, y, z + 0.002)), MapLimit);
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
            foreach (var i in shape.Interactables.Where(i => i.Kind == InteractableKind.Firebox && FireGlow > 0))
            {
                draw(Box.FromCentre(i.Position + new Double3(0, 0.7, -0.29), new Double3(0.32, 0.22, 0.02)), FireColour(0.03f + 0.18f * FireGlow) * 0.35f);
                draw(Box.FromCentre(i.Position + new Double3(0, 0.55, -0.27), new Double3(0.32, 0.07, 0.02)), FireColour(0.1f + 0.5f * FireGlow) * 0.7f);
            }
            mesh.Emissive = 0;
            // The door open, the art pass's fire: flames off the bed, cinders out of the hole, its light into the cab.
            if (FireDoorOpen && Look?.Art.Effects is { HasFlames: true } fx)
                foreach (var i in shape.Interactables.Where(i => i.Kind == InteractableKind.Firebox))
                {
                    var bed = frame.ToWorld(i.Position + new Double3(0, 0.6, -0.17)).RelativeTo(eye);
                    fx.Furnace(mesh, bed, frame.Right.RelativeTo(default), frame.Up.RelativeTo(default), frame.Back.RelativeTo(default), FireGlow, FireColour(1), Time);
                }
            // The vent valve and the driver's levers: modelled by the art pass where it has them (SceneArt.CabControls).
            bool modelled = Look?.Art.CabControls(mesh, frame, eye, Controls, WrenchRacked, CordPulled) == true;
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
        }
        if (shape.Interior is not null && Look is not null)
        {
            // Lanterns hanging where the car's lights are (the art pass's), and the car's fittings: its extinguisher's
            // board and cradle, a gun car's powder and shot.
            var fitted = Vehicles is { } lampsOf && frame.Index < lampsOf.Count ? lampsOf[frame.Index] : null;
            var bitten = Art.Bite.For(Look.Tuning.Bite, frame.Shape, fitted, frame.Index);
            Look.Art.CarLamps(mesh, frame, eye, Emergency, bitten);
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
