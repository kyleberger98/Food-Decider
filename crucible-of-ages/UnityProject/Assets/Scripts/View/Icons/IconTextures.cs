using System.Collections.Generic;
using Crucible.Core.Content;
using UnityEngine;

namespace Crucible.View.Icons
{
    /// <summary>
    /// Turns <see cref="IconArt"/> masks into white-on-transparent textures, built on first use and
    /// cached. Colour comes from tinting (UI Toolkit's background tint, or a material colour), so one
    /// texture serves every player.
    /// </summary>
    public static class IconTextures
    {
        public const int GlyphSize = 64;
        public const int ShieldSize = 96;

        static readonly Dictionary<UnitIcon, Texture2D> Glyphs = new Dictionary<UnitIcon, Texture2D>();
        static readonly Dictionary<(UnitIcon, Color32), Texture2D> Flags = new Dictionary<(UnitIcon, Color32), Texture2D>();
        static Texture2D _shieldRim, _shieldField;

        public static Texture2D Glyph(UnitIcon icon)
        {
            if (!Glyphs.TryGetValue(icon, out var tex))
                Glyphs[icon] = tex = FromMask(IconArt.Glyph(icon, GlyphSize), GlyphSize, $"Glyph {icon}");
            return tex;
        }

        public static Texture2D Glyph(UnitDef def) => Glyph(IconArt.ForUnit(def));

        /// <summary>Full shield silhouette (drawn dark, as the rim).</summary>
        public static Texture2D ShieldRim => _shieldRim ??= FromMask(IconArt.Shield(ShieldSize), ShieldSize, "Shield rim");

        /// <summary>Shield inset by the rim width (drawn in the owner's colour).</summary>
        public static Texture2D ShieldField => _shieldField ??= FromMask(IconArt.Shield(ShieldSize, 8), ShieldSize, "Shield field");

        /// <summary>A finished flag (rim, coloured field, cream symbol) for world-space billboards.</summary>
        public static Texture2D Flag(UnitIcon icon, Color color)
        {
            var key = (icon, (Color32)color);
            if (Flags.TryGetValue(key, out var tex)) return tex;

            int s = ShieldSize;
            var rim = IconArt.Shield(s);
            var field = IconArt.Shield(s, 8);
            int g = (int)(s * 0.62f), gx0 = (s - g) / 2, gy0 = (int)(s * 0.14f);
            var glyph = IconArt.Glyph(icon, g);
            var dark = new Color(0.08f, 0.08f, 0.1f);
            var cream = new Color(0.98f, 0.96f, 0.9f);
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    int i = y * s + x;
                    float a = rim[i];
                    var c = Color.Lerp(dark, color, field[i]);
                    int gx = x - gx0, gy = y - gy0;
                    if (gx >= 0 && gy >= 0 && gx < g && gy < g) c = Color.Lerp(c, cream, glyph[gy * g + gx]);
                    c.a = a;
                    px[(s - 1 - y) * s + x] = c; // masks are top-down; textures are bottom-up
                }
            tex = NewTexture(s, $"Flag {icon}");
            tex.SetPixels32(px);
            tex.Apply(true, true);
            Flags[key] = tex;
            return tex;
        }

        static Texture2D FromMask(float[] mask, int size, string name)
        {
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[(size - 1 - y) * size + x] = new Color32(255, 255, 255, (byte)(mask[y * size + x] * 255f + 0.5f));
            var tex = NewTexture(size, name);
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        static Texture2D NewTexture(int size, string name) =>
            new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                hideFlags = HideFlags.DontSave,
            };
    }
}
