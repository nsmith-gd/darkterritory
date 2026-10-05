using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The effects pass (note 147): each effect draws what it's for, when it's for, from the time alone (no state), with the
/// flipbooks tools/art makes (fx_flame, fx_flash beside fx_smoke, fx_steam, fx_spark).
/// </summary>
public class EffectsTests
{
    static readonly Look Look = Look.Load(DataFile.FindContentRoot());
    static readonly Effects Fx = new(Look);
    static readonly Vector3 O = new(0, -1.6f, -3);

    static MeshBuilder Mesh() => new();

    [Fact]
    public void TheArtHasItsFlipbooks()
    {
        Assert.True(Fx.HasFlames);
        Assert.True(Look.Layer("fx_flame") >= 0 && Look.Layer("fx_flash") >= 0);
    }

    [Fact]
    public void ACarFireSmouldersThenBurns()
    {
        var smoulder = Mesh();
        Fx.CarFire(smoulder, O, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, alight: false, 0.2, 5);
        Assert.NotEmpty(smoulder.AlphaFx);
        // The TELEGRAPH has to read in a lamp-lit car (the audit's playthrough found it didn't): smoke pale enough to
        // show against lit planking, and a low ember glow in the load with one small light, short of flames.
        Assert.True(smoulder.AlphaFx.ToArray().Max(v => v.Colour.X) > 0.9f, "the smoke's as dark as the walls");
        Assert.NotEmpty(smoulder.AdditiveFx);
        var ember = Assert.Single(smoulder.PointLights);
        var burning = Mesh();
        Fx.CarFire(burning, O, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, alight: true, 0.8, 5);
        Assert.True(burning.AdditiveFx.Count > smoulder.AdditiveFx.Count * 3, "flames, not embers");
        Assert.True(burning.PointLights.Max(l => l.Colour.Length()) > ember.Colour.Length() * 2);
        Assert.Equal(2, burning.PointLights.Count);
        // Bigger the further it's gone: more flame.
        var small = Mesh();
        Fx.CarFire(small, O, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, alight: true, 0.1, 5);
        Assert.True(burning.AdditiveFx.Count > small.AdditiveFx.Count);
    }

    [Fact]
    public void AFireAboutToJumpTheCouplingCreepsTowardTheCarsEnds()
    {
        // App. A.5 "grows, jumps couplings": the nearer the blaze is to spreading, the further its flames reach along the car.
        static float Reach(MeshBuilder m) => m.AdditiveFx.Max(v => MathF.Abs(v.Position.Z - O.Z));
        var held = Mesh();
        Fx.CarFire(held, O, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, alight: true, 0.8, 5);
        var going = Mesh();
        Fx.CarFire(going, O, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, alight: true, 0.8, 5, spread: 0.9f);
        Assert.True(going.AdditiveFx.Count > held.AdditiveFx.Count, "the creep's flames");
        Assert.True(Reach(going) > Reach(held) + 1.5f, $"reaches {Reach(going)} m along the car, {Reach(held)} held");
    }

    [Fact]
    public void AKilledCreatureCrumblesToAshThenIsGone()
    {
        var falling = Mesh();
        Fx.Crumble(falling, O, 0.3f, 7);
        Assert.Empty(falling.AlphaFx);
        var crumbling = Mesh();
        Fx.Crumble(crumbling, O, (float)Effects.DeathSeconds * 0.65f, 7);
        Assert.NotEmpty(crumbling.AlphaFx);
        Assert.NotEmpty(crumbling.AdditiveFx);
        var gone = Mesh();
        Fx.Crumble(gone, O, (float)Effects.DeathSeconds + 1.3f, 7);
        Assert.Empty(gone.AlphaFx);
    }

    [Fact]
    public void TheFurnaceBurnsAndLightsTheCab()
    {
        var mesh = Mesh();
        Fx.Furnace(mesh, O, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, 0.9f, Palette.FurnaceOrange, 3);
        Assert.NotEmpty(mesh.AdditiveFx);
        Assert.Single(mesh.PointLights);
    }

    [Fact]
    public void TheBedIsAHeapOfCoalsNotABand()
    {
        // The firebox through its hole (Effects.Coals): lumps of coal across the grate, mounded and banked, not a flat bright
        // band along its foot.
        var mesh = Mesh();
        Fx.Furnace(mesh, O, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, 0.7f, Palette.FurnaceOrange, 3);
        var v = mesh.Flattened();
        Assert.True(v.Length / 3 >= 60 * 12, $"{v.Length / 3} triangles of coal");
        Assert.True(v.Max(p => p.Position.X) - v.Min(p => p.Position.X) > 0.5f, "across the grate");
        Assert.True(v.Max(p => p.Position.Y) - v.Min(p => p.Position.Y) > 0.05f, "mounded, not flat");
        Assert.True(v.Max(p => p.Position.Z) - v.Min(p => p.Position.Z) > 0.08f, "back into the box");
    }

    [Fact]
    public void AShovelfulFlaresTheFireThenItSettles()
    {
        // §31 "furnace flare": just after the coal lands the fire roars up, throws sparks out into the cab and its light jumps;
        // by FlareSeconds it's as it was.
        var steady = Mesh();
        Fx.Furnace(steady, O, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, 0.6f, Palette.FurnaceOrange, 3);
        var flaring = Mesh();
        Fx.Furnace(flaring, O, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, 0.6f, Palette.FurnaceOrange, 3, sinceCoal: 0.1);
        Assert.True(flaring.AdditiveFx.Count > steady.AdditiveFx.Count * 2, "the gout and the shower");
        Assert.True(flaring.PointLights[0].Colour.Length() > steady.PointLights[0].Colour.Length() * 1.8f);
        var settled = Mesh();
        Fx.Furnace(settled, O, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, 0.6f, Palette.FurnaceOrange, 3, sinceCoal: Effects.FlareSeconds + 0.01);
        Assert.Equal(steady.AdditiveFx.Count, settled.AdditiveFx.Count);
        Assert.Equal(steady.PointLights[0].Colour, settled.PointLights[0].Colour);
    }

    [Fact]
    public void AShotFlashesThenLeavesItsSmokeBehindTheMovingCar()
    {
        var flash = Mesh();
        Fx.CannonShot(flash, O, -Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitY, 10, 0.03, 100);
        Assert.Single(flash.PointLights);
        var later = Mesh();
        Fx.CannonShot(later, O, -Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitY, 10, 2, 100);
        Assert.Empty(later.PointLights);
        Assert.NotEmpty(later.AlphaFx);
        // The air's still and the car isn't: two seconds on at 10 m/s, the cloud is well behind the muzzle.
        float z = later.AlphaFx.Average(v => v.Position.Z);
        Assert.True(z > O.Z + 8, $"the smoke's at z {z}");
        var gone = Mesh();
        Fx.CannonShot(gone, O, -Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitY, 10, Effects.ShotSmokeSeconds + 0.1, 100);
        Assert.Empty(gone.AlphaFx);
    }

    [Fact]
    public void ACannonballBurstsWithALightThenLeavesSmokeAndAScorch()
    {
        // T121: "all cannonballs should have an impact explosion and VFX to show where impact was".
        var at = new Vector3(0, -1.6f, -60);
        var burst = Mesh();
        Fx.CannonImpact(burst, at, Vector3.Normalize(new Vector3(0, -0.1f, -1)), Sim.Combat.ImpactSurface.Ground, 0, 0.05, 7);
        Assert.NotEmpty(burst.AdditiveFx);
        var light = Assert.Single(burst.PointLights);
        Assert.True(light.Range >= 40, "the burst lights the ground round it, seen from the gun");
        // Seconds on: smoke over it, the scorch on the ground, no light.
        var later = Mesh();
        Fx.CannonImpact(later, at, -Vector3.UnitZ, Sim.Combat.ImpactSurface.Ground, 0, 3, 7);
        Assert.Empty(later.PointLights);
        Assert.True(later.AlphaFx.Max(v => v.Position.Y) > at.Y + 3, "the smoke's risen");
        Assert.Contains(later.AlphaFx, v => MathF.Abs(v.Position.Y - at.Y) < 0.1f && v.Colour.X < 0.05f);
        var gone = Mesh();
        Fx.CannonImpact(gone, at, -Vector3.UnitZ, Sim.Combat.ImpactSurface.Ground, 0, Effects.ImpactSeconds + 0.1, 7);
        Assert.Empty(gone.AlphaFx);
        Assert.Empty(gone.AdditiveFx);
    }

    [Fact]
    public void EverySurfaceABallLandsOnShowsIt()
    {
        foreach (var surface in Enum.GetValues<Sim.Combat.ImpactSurface>())
        {
            var mesh = Mesh();
            Fx.CannonImpact(mesh, new Vector3(0, -1.6f, -40), -Vector3.UnitZ, surface, 0, 0.1, 3);
            Assert.True(mesh.AdditiveFx.Count + mesh.AlphaFx.Count > 0, $"{surface} shows nothing");
            Assert.NotEmpty(mesh.PointLights);
        }
        // Into water, a splash: spray thrown up, and no fire.
        var splash = Mesh();
        Fx.CannonImpact(splash, new Vector3(0, -1.6f, -40), -Vector3.UnitZ, Sim.Combat.ImpactSurface.Water, 0, 0.6, 3);
        Assert.True(splash.AlphaFx.Max(v => v.Position.Y) > -1.6f + 2);
        Assert.Empty(splash.AdditiveFx);
    }

    [Fact]
    public void TheDollGoesUpInPorcelain()
    {
        var earth = Mesh();
        Fx.CannonImpact(earth, new Vector3(0, -1, -40), -Vector3.UnitZ, Sim.Combat.ImpactSurface.Creature, 0, 0.5, 3);
        var doll = Mesh();
        Fx.CannonImpact(doll, new Vector3(0, -1, -40), -Vector3.UnitZ, Sim.Combat.ImpactSurface.Creature, Sim.Enemies.EnemyKind.TrackDoll, 0.5, 3);
        Assert.Contains(doll.AlphaFx, v => v.Colour.X > 0.6f && v.Colour.Y > 0.6f);
        Assert.DoesNotContain(earth.AlphaFx, v => v.Colour.X > 0.6f && v.Colour.Y > 0.6f && v.Colour.Z > 0.6f && v.Colour.W > 0.9f);
    }

    [Fact]
    public void AHitFlashesOnTheCreatureForAMoment()
    {
        var mesh = Mesh();
        Fx.HitFlash(mesh, new Vector3(0, 0, -4), new Vector3(0, 0, -4.2f), -Vector3.UnitZ, Sim.Combat.HitSource.Melee, killed: false, 0.03, 5);
        Assert.NotEmpty(mesh.AdditiveFx);
        Assert.Single(mesh.PointLights);
        var gone = Mesh();
        Fx.HitFlash(gone, new Vector3(0, 0, -4), new Vector3(0, 0, -4.2f), -Vector3.UnitZ, Sim.Combat.HitSource.Melee, killed: false, 1, 5);
        Assert.Empty(gone.AdditiveFx);
        Assert.Empty(gone.PointLights);
    }

    [Fact]
    public void AnExtinguisherSpraysOntoTheFire()
    {
        var mesh = Mesh();
        Fx.Spray(mesh, O + new Vector3(-1, 1, 2), O, 1);
        Assert.NotEmpty(mesh.AlphaFx);
    }

    [Fact]
    public void CoalPoursFromTheSpout()
    {
        var mesh = Mesh();
        Fx.CoalPour(mesh, new Vector3(0, 6, -8), Vector3.UnitX, -Vector3.UnitZ, 5.4f, 2);
        // Lumps and dust fall below the spout, the cloud kicked up near the bottom.
        Assert.True(mesh.AlphaFx.Min(v => v.Position.Y) < 6 - 4);
    }

    [Fact]
    public void TheChoirBringTheCold()
    {
        var mesh = Mesh();
        Fx.ChoirCold(mesh, new Vector3(0, 2, -6), 4, swooping: false, 71);
        Assert.NotEmpty(mesh.AlphaFx);
        Assert.NotEmpty(mesh.AdditiveFx);
    }

    [Fact]
    public void CorruptedAirIsTheWorldsNotTheEyes()
    {
        var clean = Mesh();
        Fx.Corruption(clean, new Double3(100, 5, 200), 3, Effects.Air.Clean);
        Assert.Empty(clean.AlphaFx);
        Assert.Empty(clean.AdditiveFx);
        Assert.Equal(Effects.Air.Ash, Effects.AirOf("industrialRuin"));
        Assert.Equal(Effects.Air.Spores, Effects.AirOf("contaminatedMarsh"));
        Assert.Equal(Effects.Air.Clean, Effects.AirOf("farmland"));
        // Anchored to the world: move the eye a whole cell and the motes' world positions are the same set.
        static HashSet<(int, int)> World(Double3 eye)
        {
            var mesh = new MeshBuilder();
            Fx.Corruption(mesh, eye, 3, Effects.Air.Ash);
            var set = new HashSet<(int, int)>();
            for (int i = 0; i + 5 < mesh.AlphaFx.Count; i += 6)
            {
                var c = (mesh.AlphaFx[i].Position + mesh.AlphaFx[i + 2].Position) / 2;
                set.Add(((int)Math.Round((c.X + eye.X) * 100), (int)Math.Round((c.Z + eye.Z) * 100)));
            }
            return set;
        }
        var a = World(new Double3(100, 5, 200));
        var b = World(new Double3(106, 5, 200));
        Assert.True(a.Intersect(b).Count() > a.Count / 2, $"{a.Intersect(b).Count()} of {a.Count} shared");
    }

    [Fact]
    public void OverABrassFieldTheAirCarriesItsDust()
    {
        // GDD §30 (the checklist's corruption particulate): brass dust, glinting, near a brass field and nowhere else.
        var dust = Mesh();
        Fx.Corruption(dust, new Double3(100, 5, 200), 3, Effects.Air.Brass);
        Assert.NotEmpty(dust.AdditiveFx);
        Assert.Empty(dust.AlphaFx);
        var route = Sim.LineGen.Routes.Generate(DataFile.FindContentRoot(), "deadlines:4", 6);
        var line = route.Build();
        var field = route.Plan!.Structures.First(s => s.Type == Sim.LineGen.StructureType.BrassField && s.Edge == "main");
        var over = line.Sample((field.S0 + field.S1) / 2).Position + Double3.Up * 3;
        Assert.True(WorldArt.NearBrass(route, over));
        var away = line.Sample(Math.Min(line.Length - 1, field.S1 + 400)).Position + Double3.Up * 3;
        Assert.DoesNotContain(route.Plan.Structures, s => s.Type == Sim.LineGen.StructureType.BrassField && s.S0 < field.S1 + 500 && s.S1 > field.S1 + 300);
        Assert.False(WorldArt.NearBrass(route, away));
    }
}
