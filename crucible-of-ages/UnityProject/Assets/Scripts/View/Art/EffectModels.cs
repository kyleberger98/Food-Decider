namespace Crucible.View.Art
{
    /// <summary>
    /// One-off effect meshes (engine-free). The mushroom cloud of a nuclear strike comes from the
    /// Blender library (prop_mushroom) when exported, else from stacked frustums. About 0.85 tall and
    /// 0.5 across at model scale; the game scales it up to the blast.
    /// </summary>
    public static class EffectModels
    {
        static readonly Rgb Dust = Rgb.Hex(0x8A7A68), DustLight = Rgb.Hex(0xB8A894), Stem = Rgb.Hex(0xC8B8A0);
        static readonly Rgb Fire = Rgb.Hex(0xF08A2A), Cap = Rgb.Hex(0xE8D2B0), CapTop = Rgb.Hex(0xF4EAD8);

        public static MeshData MushroomCloud()
        {
            var authored = ArtLibrary.Current?.Get("prop_mushroom", new Rgb(1, 1, 1));
            if (authored != null) return authored;

            var m = new MeshData();
            var o = Frame.At(0, 0, 0);
            m.Frustum(o, 12, 0f, 0.26f, 0.07f, 0.16f, Dust, DustLight);          // base surge
            m.Frustum(o, 8, 0.05f, 0.08f, 0.52f, 0.06f, Stem, Fire);            // column
            m.Frustum(o, 12, 0.48f, 0.12f, 0.6f, 0.25f, Fire, Cap);             // cap underside
            m.Frustum(o, 12, 0.6f, 0.25f, 0.76f, 0.2f, Cap, CapTop);
            m.Frustum(o, 12, 0.76f, 0.2f, 0.85f, 0f, CapTop);
            m.Frustum(o, 12, 0.3f, 0.1f, 0.34f, 0.14f, DustLight);              // condensation ring
            return m;
        }
    }
}
