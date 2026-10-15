using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

public sealed partial class Effects
{
    /// <summary>
    /// The back wall's kettle singing (note 716, <see cref="Sim.Train.CrewCar.KettleSong"/>): a thin jet off its spout, more
    /// and quicker as the boiler nears its redline (<paramref name="song"/> 0..1), curling up and away in the cab's still air.
    /// <paramref name="spout"/> the spout's tip, <paramref name="up"/> the cab's up, <paramref name="out_"/> the way it points.
    /// </summary>
    public void KettleSteam(MeshBuilder mesh, Vector3 spout, Vector3 up, Vector3 out_, float song, double t)
    {
        if (_steam < 0 || song <= 0)
            return;
        int puffs = 3 + (int)(song * 9);
        for (int k = 0; k < puffs; k++)
        {
            float h = Hash(k * 3.17f + 0.5f), period = 0.9f + h * 0.5f - song * 0.3f;
            float age = (float)((t * (1 + song) + h * 7) % period), s = age / period;
            var p = spout + out_ * (age * (0.25f + 0.35f * song)) + up * (age * age * 0.9f)
                + new Vector3(MathF.Sin(age * 3.1f + h * 6), 0, MathF.Cos(age * 2.3f + h * 4)) * (0.04f * s);
            float a = (0.25f + 0.35f * song) * (1 - s);
            mesh.Billboard(p, 0.03f + s * 0.22f, h * 6.28f, new Vector4(0.7f, 0.71f, 0.74f, a), _steam, FxBlend.Alpha, (int)(s * 15.99f), 4);
        }
    }
}
