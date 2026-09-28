using System;

namespace Crucible.View.Art
{
    /// <summary>
    /// Low-poly settlements (engine-free): a keep flying the owner's banner, a cluster of houses that
    /// grows with population, and a ring of walls with towers once the city is fortified.
    /// </summary>
    public static class CityModels
    {
        static readonly Rgb Plaster = Rgb.Hex(0xE8DDC4), Timber = Rgb.Hex(0x9A7452), Roof = Rgb.Hex(0xB4553A);
        static readonly Rgb Stone = Rgb.Hex(0xA8A198), StoneDark = Rgb.Hex(0x7E776F), Paving = Rgb.Hex(0xB9AD92);

        public static MeshData Build(int population, int wallTier, bool capital, Rgb team, int seed)
        {
            var lib = ArtLibrary.Current;
            if (lib != null && lib.Has("city_keep") && lib.Has("city_house") && lib.Has("city_wall") && lib.Has("city_tower"))
                return FromParts(lib, population, wallTier, capital, team, seed);

            var m = new MeshData();
            var o = Frame.At(0, 0, 0);
            var rng = new ArtRng(seed, population, 7);
            m.Hexagon(o.Move(0, 0.012f, 0), 0.72f, Paving, Paving * 0.9f);

            // Keep: a squat tower with the owner's banner; the capital's is taller and crowned.
            float keepH = capital ? 0.5f : 0.38f;
            m.Frustum(o, 6, 0, 0.15f, keepH, 0.13f, Stone, StoneDark);
            for (int i = 0; i < 6; i++)
                m.Box(o.Rotate(i * 60).Move(0.12f, keepH + 0.03f, 0), 0.025f, 0.03f, 0.03f, Stone);
            m.Box(o.Move(0, keepH + 0.14f, 0), 0.005f, 0.14f, 0.005f, Timber);
            m.Box(o.Move(0.06f, keepH + 0.22f, 0), 0.055f, 0.045f, 0.004f, team, team);
            if (capital) m.Frustum(o.Move(0, keepH, 0), 6, 0, 0.1f, 0.12f, 0, team * 0.9f, team);

            // Houses in a ring around the keep, more as the city grows.
            int houses = Math.Min(12, 4 + population);
            for (int i = 0; i < houses; i++)
            {
                float a = i * 2f * (float)Math.PI / houses + rng.Range(-0.15f, 0.15f);
                float r = (i % 2 == 0 ? 0.36f : 0.52f) + rng.Range(-0.04f, 0.04f);
                var f = o.Move((float)Math.Cos(a) * r, 0, (float)Math.Sin(a) * r).Rotate(-a * 180f / (float)Math.PI + 90);
                float w = rng.Range(0.06f, 0.085f), d = rng.Range(0.07f, 0.1f), h = rng.Range(0.07f, 0.11f);
                var wall = rng.Value < 0.3f ? Timber : Plaster;
                m.Box(f.Move(0, h, 0), w, h, d, wall);
                // Gabled roof.
                var roof = Roof * rng.Range(0.85f, 1.1f);
                float y = 2 * h, ridge = y + w * 0.9f;
                m.Quad(f.P(-w * 1.15f, y, -d * 1.1f), f.P(-w * 1.15f, y, d * 1.1f), f.P(0, ridge, d * 1.1f), f.P(0, ridge, -d * 1.1f), roof);
                m.Quad(f.P(w * 1.15f, y, d * 1.1f), f.P(w * 1.15f, y, -d * 1.1f), f.P(0, ridge, -d * 1.1f), f.P(0, ridge, d * 1.1f), roof * 0.8f);
                m.Tri(f.P(-w, y, d), f.P(w, y, d), f.P(0, ridge, d), wall);
                m.Tri(f.P(w, y, -d), f.P(-w, y, -d), f.P(0, ridge, -d), wall);
            }

            if (wallTier > 0)
            {
                // Curtain wall along the hex rim with a tower at every corner; higher tiers stand taller.
                float h = 0.1f + 0.04f * wallTier, rad = 0.8f;
                for (int i = 0; i < 6; i++)
                {
                    double a0 = Math.PI / 180 * (60 * i - 30), a1 = Math.PI / 180 * (60 * (i + 1) - 30);
                    var p0 = new V3((float)Math.Cos(a0) * rad, 0, (float)Math.Sin(a0) * rad);
                    var p1 = new V3((float)Math.Cos(a1) * rad, 0, (float)Math.Sin(a1) * rad);
                    var mid = (p0 + p1) * 0.5f;
                    float yaw = -(float)(Math.Atan2(p1.Z - p0.Z, p1.X - p0.X) * 180 / Math.PI);
                    m.Box(Frame.At(mid.X, h / 2, mid.Z).Rotate(yaw), (p1 - p0).Length / 2, h / 2, 0.025f, Stone, StoneDark);
                    m.Frustum(Frame.At(p0), 6, 0, 0.06f, h + 0.06f, 0.055f, Stone, StoneDark);
                    m.Frustum(Frame.At(p0.X, h + 0.06f, p0.Z), 6, 0, 0.065f, 0.06f, 0, team * 0.85f, team);
                }
            }
            return m;
        }

        /// <summary>The same layout assembled from Blender parts (city_keep, city_house, city_wall, city_tower).</summary>
        static MeshData FromParts(ArtLibrary lib, int population, int wallTier, bool capital, Rgb team, int seed)
        {
            var m = new MeshData();
            var o = Frame.At(0, 0, 0);
            var rng = new ArtRng(seed, population, 7);
            m.Hexagon(o.Move(0, 0.012f, 0), 0.72f, Paving, Paving * 0.9f);
            lib.AppendTo(m, "city_keep", o.Rotate(rng.Range(0, 360)).Scale(capital ? 1.25f : 1f), team);

            int houses = Math.Min(12, 4 + population);
            for (int i = 0; i < houses; i++)
            {
                float a = i * 2f * (float)Math.PI / houses + rng.Range(-0.15f, 0.15f);
                float r = (i % 2 == 0 ? 0.38f : 0.54f) + rng.Range(-0.04f, 0.04f);
                // Houses face the keep (their door side, the model's front, points inward).
                var f = o.Move((float)Math.Cos(a) * r, 0, (float)Math.Sin(a) * r).Rotate(-a * 180f / (float)Math.PI - 90);
                lib.AppendTo(m, "city_house", f.Scale(rng.Range(0.8f, 1.1f)), team, rng.Range(0.9f, 1.05f));
            }

            if (wallTier > 0)
            {
                float rad = 0.8f, height = 1f + 0.3f * (wallTier - 1);
                for (int i = 0; i < 6; i++)
                {
                    double a0 = Math.PI / 180 * (60 * i - 30), a1 = Math.PI / 180 * (60 * (i + 1) - 30);
                    var p0 = new V3((float)Math.Cos(a0) * rad, 0, (float)Math.Sin(a0) * rad);
                    var p1 = new V3((float)Math.Cos(a1) * rad, 0, (float)Math.Sin(a1) * rad);
                    var mid = (p0 + p1) * 0.5f;
                    float yaw = -(float)(Math.Atan2(p1.Z - p0.Z, p1.X - p0.X) * 180 / Math.PI);
                    lib.AppendTo(m, "city_wall", Frame.At(mid).Rotate(yaw).Scale((p1 - p0).Length, height, 1f), team);
                    lib.AppendTo(m, "city_tower", Frame.At(p0).Scale(1f, height, 1f), team);
                }
            }
            return m;
        }
    }
}
