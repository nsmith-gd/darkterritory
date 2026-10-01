using Ballast;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// X1 (note 145): a crewmate's act read off their replicated state picks their clip, and every act has one: the crew's
/// actions are crew_clips.glb's, merged into crew.glb on load.
/// </summary>
public class CrewActsTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning T = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    static readonly PlayerTuning P = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));
    static readonly CombatTuning C = DataFile.Load<CombatTuning>(Path.Combine(Content, CombatTuning.File));

    static World World()
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        return new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 1)), line, 5_000), C);
    }

    [Fact]
    public void EveryActHasACrewClip()
    {
        var crew = new CreatureArt(Look.Load(Content), Content).Get("crew");
        Assert.NotNull(crew);
        foreach (var pose in Enum.GetValues<CrewPose>())
        {
            string clip = pose switch
            {
                CrewPose.Crouch => "crouch_idle",
                CrewPose.CarryWalk => "carry_walk",
                CrewPose.LanternWalk => "lantern_walk",
                _ => pose.ToString().ToLowerInvariant(),
            };
            Assert.True(crew!.Clip(clip) is not null, $"{pose}: the crew has no '{clip}' clip");
        }
    }

    [Fact]
    public void TheActIsReadOffTheirState()
    {
        var w = World();
        var s = PlayerMotor.SpawnOnRoof(w.Train, 2, 0, P);
        Assert.Null(CrewActs.Of(s, 1, w));
        Assert.Equal(CrewPose.Held, CrewActs.Of(s with { Flags = PlayerFlags.Held }, 1, w));
        Assert.Equal(CrewPose.Climb, CrewActs.Of(s with { Surface = Surface.Ladder }, 1, w));
        Assert.Equal(CrewPose.Push, CrewActs.Of(s with { Flags = PlayerFlags.Pushing }, 1, w));
        Assert.Equal(CrewPose.Lever, CrewActs.Of(s with { Flags = PlayerFlags.Operating }, 1, w));
        // In the air: at the top of a jump, nothing; coming down hard, falling.
        Assert.Null(CrewActs.Of(s with { Surface = Surface.Air, Velocity = new Double3(0, -1, 0) }, 1, w));
        Assert.Equal(CrewPose.Fall, CrewActs.Of(s with { Surface = Surface.Air, Velocity = new Double3(0, -6, 0) }, 1, w));
        // Dead, they're drawn as their body.
        Assert.Null(CrewActs.Of(s with { Death = DeathCause.Struck, Flags = PlayerFlags.Held }, 1, w));
    }

    [Fact]
    public void TheLampTheExtinguisherTheGapAndAFriendHeld()
    {
        var w = World();
        var s = PlayerMotor.SpawnOnRoof(w.Train, 2, 0, P);
        // What's in their hands: the hand lamp held out, the extinguisher on the hip (L0a).
        var lamp = w.Bodies.SpawnCrate(w.Train, 2, new Double3(0, w.Train.Frames[2].Shape.RoofHeight, 0), Sim.Physics.BodyKind.Lamp);
        lamp.Carrier = 1;
        Assert.Equal(CrewPose.Lantern, CrewActs.Of(s, 1, w));
        lamp.Carrier = -1;
        var ext = w.Bodies.SpawnCrate(w.Train, 2, new Double3(0, w.Train.Frames[2].Shape.RoofHeight, 0), Sim.Physics.BodyKind.Extinguisher);
        ext.Carrier = 1;
        Assert.Equal(CrewPose.Extinguish, CrewActs.Of(s, 1, w));
        ext.Carrier = -1;
        // On the coupling plate, balancing over the gap; beside a friend something's holding, hauling at them.
        Assert.Equal(CrewPose.Gap, CrewActs.Of(s with { Surface = Surface.Coupler }, 1, w));
        var friend = s with { Position = s.Position + new Double3(0.8, 0, 0), Flags = PlayerFlags.Held };
        Assert.Equal(CrewPose.Haul, CrewActs.Of(s, 1, w, [s, friend]));
        Assert.Null(CrewActs.Of(s, 1, w, [s, friend with { Position = s.Position + new Double3(4, 0, 0) }]));
    }

    [Fact]
    public void AtTheGunTheyreSatOnItsSeat()
    {
        var w = World();
        int gun = Enumerable.Range(0, w.Train.Vehicles.Count).Last(i => w.Train.Vehicles[i].HasGun);
        var mount = Guns.Mount(w.Train, gun)!.Value;
        var s = PlayerMotor.SpawnOnRoof(w.Train, gun, mount.Position.Z + 0.5, P);
        Assert.Equal(CrewPose.Gunner, CrewActs.Of(s, 1, w));
        var c = CrewActs.Crewmate(1, s, w, w.Train.Frames);
        Assert.Equal(CrewPose.Gunner, c.Act);
        // Feet on the roof under the seat, behind the pivot (the barrel's way the other way), facing the gun's way.
        var seat = CrewActs.Seated(s, mount);
        Assert.Equal(mount.Position.Y - 0.9, seat.Position.Y, 3);
        Assert.Equal(mount.Position.Z - mount.Facing.Z * TrainKit.CannonSeat.Z, seat.Position.Z, 3);
        Assert.Equal(PlayerMotor.WorldPosition(seat, w.Train).X, c.Feet.X, 3);
        Assert.Equal(mount.Facing.Z < 0 ? 0 : Math.PI, Math.Abs(seat.Yaw), 3);
        // The pan, the clip's sat height over those feet: the cannon's seat 0.42 under the pivot.
        Assert.Equal(mount.Position.Y + TrainKit.CannonSeat.Y, seat.Position.Y + CrewActs.GunnerPan, 3);
    }

    [Fact]
    public void YourOwnArmsAreTheCrewsForearmsAndGlovesInView()
    {
        var look = Look.Load(Content);
        var art = new CreatureArt(look, Content);
        var bar = PropArt.Of(look).Get("tool_crowbar");
        Assert.NotNull(bar);
        // Empty hands at your sides: nothing of you is in view.
        var mesh = new Ballast.Render.MeshBuilder();
        Assert.True(art.OwnArms(mesh, 0, 0, null, false, -1, 0, 1));
        Assert.Empty(mesh.Instances);
        // A tool in hand: the arms (a cut of the crew mesh, only forearms and gloves) and the tool, in front of the eye.
        Assert.True(art.OwnArms(mesh, 0, 0, null, false, -1, 0, 1, bar));
        var arms = mesh.Instances.Single(i => i.Asset.Skin is not null);
        int whole = art.Get("crew")!.Parts.Sum(p => p.Indices.Length / 3);
        Assert.InRange(arms.Asset.Triangles, 100, whole / 3);
        // The tool's appended to the mesh: in front of the eye (the mesh's origin), up from the right fist and leaning in
        // across the view, so its middle is about the middle.
        var at = Middle(mesh);
        Assert.True(at.Z < -0.2f, $"the tool at {at}");
        Assert.InRange(at.Y, -0.45f, 0.2f);
        Assert.InRange(at.X, -0.1f, 0.4f);
        // Turned to look behind you, it turns with you.
        mesh.Clear();
        art.OwnArms(mesh, MathF.PI, 0, null, false, -1, 0, 1, bar);
        Assert.True(Middle(mesh).Z > 0.2f);
    }

    static System.Numerics.Vector3 Middle(Ballast.Render.MeshBuilder mesh)
    {
        Assert.True(mesh.Count > 0);
        var sum = System.Numerics.Vector3.Zero;
        foreach (var v in mesh.Vertices)
            sum += v.Position;
        return sum / mesh.Count;
    }
}
