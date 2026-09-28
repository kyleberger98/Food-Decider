using System;
using System.Collections.Generic;

namespace Crucible.View.Art
{
    /// <summary>Engine-free vector (y up), so the art code can be previewed and tested outside Unity.</summary>
    public readonly struct V3
    {
        public readonly float X, Y, Z;
        public V3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static V3 operator +(V3 a, V3 b) => new V3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static V3 operator -(V3 a, V3 b) => new V3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static V3 operator *(V3 a, float s) => new V3(a.X * s, a.Y * s, a.Z * s);
        public static V3 Lerp(V3 a, V3 b, float t) => a + (b - a) * t;
        public float Length => (float)Math.Sqrt(X * X + Y * Y + Z * Z);
        public V3 Normalized => this * (1f / Math.Max(1e-6f, Length));
        public static V3 Cross(V3 a, V3 b) => new V3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        public static float Dot(V3 a, V3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static readonly V3 Up = new V3(0, 1, 0);
    }

    /// <summary>Linear RGB colour.</summary>
    public readonly struct Rgb
    {
        public readonly float R, G, B;
        public Rgb(float r, float g, float b) { R = r; G = g; B = b; }
        public static Rgb operator *(Rgb c, float s) => new Rgb(c.R * s, c.G * s, c.B * s);
        public static Rgb Lerp(Rgb a, Rgb b, float t) => new Rgb(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);
        public static Rgb Hex(int rgb) => new Rgb(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
    }

    /// <summary>A local coordinate frame: origin plus rotated, scaled axes.</summary>
    public readonly struct Frame
    {
        public readonly V3 O, X, Y, Z;
        public Frame(V3 o, V3 x, V3 y, V3 z) { O = o; X = x; Y = y; Z = z; }
        public static Frame At(float x, float y, float z) => new Frame(new V3(x, y, z), new V3(1, 0, 0), V3.Up, new V3(0, 0, 1));
        public static Frame At(V3 p) => At(p.X, p.Y, p.Z);
        public V3 P(float x, float y, float z) => O + X * x + Y * y + Z * z;
        public V3 Dir(float x, float y, float z) => X * x + Y * y + Z * z;
        public Frame Move(float x, float y, float z) => new Frame(P(x, y, z), X, Y, Z);
        public Frame Scale(float s) => new Frame(O, X * s, Y * s, Z * s);

        /// <summary>Rotates about the local Y (yaw), then X (pitch), then Z (roll) axes; degrees.</summary>
        public Frame Rotate(float yaw, float pitch = 0, float roll = 0)
        {
            var f = this;
            if (yaw != 0) f = Spin(f, yaw, 1);
            if (pitch != 0) f = Spin(f, pitch, 0);
            if (roll != 0) f = Spin(f, roll, 2);
            return f;
        }

        static Frame Spin(Frame f, float deg, int axis)
        {
            float r = deg * (float)Math.PI / 180f, c = (float)Math.Cos(r), s = (float)Math.Sin(r);
            switch (axis)
            {
                case 1: return new Frame(f.O, f.X * c - f.Z * s, f.Y, f.X * s + f.Z * c);
                case 0: return new Frame(f.O, f.X, f.Y * c + f.Z * s, f.Z * c - f.Y * s);
                default: return new Frame(f.O, f.X * c + f.Y * s, f.Y * c - f.X * s, f.Z);
            }
        }
    }

    /// <summary>
    /// Flat-shaded, vertex-coloured triangle soup. Every triangle owns its three vertices so the
    /// facets read crisply (the low-poly look) once the engine computes per-vertex normals.
    /// </summary>
    public sealed class MeshData
    {
        public readonly List<V3> Positions = new List<V3>();
        public readonly List<Rgb> Colors = new List<Rgb>();

        public int VertexCount => Positions.Count;

        public void Tri(V3 a, V3 b, V3 c, Rgb ca, Rgb cb, Rgb cc)
        {
            Positions.Add(a); Positions.Add(b); Positions.Add(c);
            Colors.Add(ca); Colors.Add(cb); Colors.Add(cc);
        }

        public void Tri(V3 a, V3 b, V3 c, Rgb color) => Tri(a, b, c, color, color, color);

        public void Quad(V3 a, V3 b, V3 c, V3 d, Rgb color)
        {
            Tri(a, b, c, color);
            Tri(a, c, d, color);
        }

        public void Quad(V3 a, V3 b, V3 c, V3 d, Rgb ca, Rgb cb, Rgb cc, Rgb cd)
        {
            Tri(a, b, c, ca, cb, cc);
            Tri(a, c, d, ca, cc, cd);
        }

        /// <summary>Box of the given half-extents centred on the frame's origin.</summary>
        public void Box(Frame f, float hx, float hy, float hz, Rgb color, Rgb? top = null)
        {
            V3 P(float x, float y, float z) => f.P(x * hx, y * hy, z * hz);
            var t = top ?? color;
            Quad(P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), t);   // top
            Quad(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), color * 0.6f); // bottom
            Quad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), color);
            Quad(P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), color);
            Quad(P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), color);
            Quad(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), color);
        }

        /// <summary>
        /// n-sided frustum along the frame's Y axis from y0 (radius r0) to y1 (radius r1). r1 = 0 makes a cone.
        /// </summary>
        public void Frustum(Frame f, int sides, float y0, float r0, float y1, float r1, Rgb color, Rgb? topColor = null, bool caps = true, float phase = 0)
        {
            var top = topColor ?? color;
            for (int i = 0; i < sides; i++)
            {
                float a0 = phase + i * 2f * (float)Math.PI / sides, a1 = phase + (i + 1) * 2f * (float)Math.PI / sides;
                float c0 = (float)Math.Cos(a0), s0 = (float)Math.Sin(a0), c1 = (float)Math.Cos(a1), s1 = (float)Math.Sin(a1);
                var b0 = f.P(c0 * r0, y0, s0 * r0);
                var b1 = f.P(c1 * r0, y0, s1 * r0);
                if (r1 <= 0)
                {
                    Tri(b1, b0, f.P(0, y1, 0), color, color, top);
                }
                else
                {
                    var t0 = f.P(c0 * r1, y1, s0 * r1);
                    var t1 = f.P(c1 * r1, y1, s1 * r1);
                    Quad(b1, b0, t0, t1, color, color, top, top);
                    if (caps) Tri(t1, t0, f.P(0, y1, 0), top);
                }
                if (caps) Tri(b0, b1, f.P(0, y0, 0), color * 0.6f);
            }
        }

        /// <summary>Squashed octahedron: a cheap faceted "blob" for tree crowns, heads and rocks.</summary>
        public void Gem(Frame f, float rx, float ry, float rz, Rgb color, Rgb? topColor = null)
        {
            var top = topColor ?? color;
            var up = f.P(0, ry, 0);
            var down = f.P(0, -ry, 0);
            var ring = new[] { f.P(rx, 0, 0), f.P(0, 0, rz), f.P(-rx, 0, 0), f.P(0, 0, -rz) };
            for (int i = 0; i < 4; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % 4];
                Tri(a, up, b, color, top, color);
                Tri(b, down, a, color * 0.75f);
            }
        }

        /// <summary>Hexagon lying flat at the frame origin (pointy-top, like the map).</summary>
        public void Hexagon(Frame f, float radius, Rgb color, Rgb? rim = null)
        {
            var edge = rim ?? color;
            for (int i = 0; i < 6; i++)
            {
                float a0 = (60f * i - 30f) * (float)Math.PI / 180f, a1 = (60f * (i + 1) - 30f) * (float)Math.PI / 180f;
                Tri(f.P(0, 0, 0), f.P((float)Math.Cos(a1) * radius, 0, (float)Math.Sin(a1) * radius),
                    f.P((float)Math.Cos(a0) * radius, 0, (float)Math.Sin(a0) * radius), color, edge, edge);
            }
        }

        public void Append(MeshData other)
        {
            Positions.AddRange(other.Positions);
            Colors.AddRange(other.Colors);
        }
    }

    /// <summary>Small deterministic hash RNG for decoration placement (never touches game state).</summary>
    public sealed class ArtRng
    {
        uint _s;
        public ArtRng(int a, int b, int salt = 0) { _s = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(salt * 83492791) ^ 0x9E3779B9u; Next(); }
        public uint Next()
        {
            _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5;
            return _s;
        }
        public float Value => (Next() & 0xFFFFFF) / (float)0x1000000;
        public float Range(float a, float b) => a + (b - a) * Value;
    }
}
