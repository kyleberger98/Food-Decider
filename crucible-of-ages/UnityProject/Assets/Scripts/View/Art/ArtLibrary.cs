using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Crucible.View.Art
{
    /// <summary>
    /// Models authored in Blender (Tools/blender) and exported as "CRM1" meshes to
    /// Assets/Resources/Art. Engine-free: Unity hands in the bytes, the art code asks for meshes by
    /// name. Parts painted magenta in Blender take the owner's colour. When a model is missing, the
    /// procedural art (UnitModels, TerrainArt, CityModels) draws its own stand-in.
    /// </summary>
    public sealed class ArtLibrary
    {
        /// <summary>The library the art code uses; null (or empty) means procedural art only.</summary>
        public static ArtLibrary Current;

        readonly Dictionary<string, MeshData> _models = new Dictionary<string, MeshData>();
        readonly Dictionary<(string, Rgb), MeshData> _tinted = new Dictionary<(string, Rgb), MeshData>();

        public int Count => _models.Count;
        public IEnumerable<string> Names => _models.Keys;

        public bool Has(string name) => _models.ContainsKey(name);

        public void Add(string name, byte[] data)
        {
            _models[name] = Parse(data);
            _tinted.Clear();
        }

        /// <summary>Loads every *.bytes file in a folder (tools and tests; the game loads via Resources).</summary>
        public static ArtLibrary FromDirectory(string dir)
        {
            var lib = new ArtLibrary();
            if (Directory.Exists(dir))
                foreach (var file in Directory.GetFiles(dir, "*.bytes"))
                    lib.Add(Path.GetFileNameWithoutExtension(file), File.ReadAllBytes(file));
            return lib;
        }

        /// <summary>The model with its team parts in <paramref name="team"/> (cached), or null if absent.</summary>
        public MeshData Get(string name, Rgb team)
        {
            if (!_models.TryGetValue(name, out var raw)) return null;
            if (_tinted.TryGetValue((name, team), out var done)) return done;
            var m = new MeshData();
            m.Positions.AddRange(raw.Positions);
            foreach (var c in raw.Colors) m.Colors.Add(Recolor(c, team));
            _tinted[(name, team)] = m;
            return m;
        }

        /// <summary>Appends a model placed by <paramref name="frame"/> (position, rotation, scale) to <paramref name="target"/>.</summary>
        public bool AppendTo(MeshData target, string name, Frame frame, Rgb team, float shade = 1f)
        {
            var m = Get(name, team);
            if (m == null) return false;
            for (int i = 0; i < m.Positions.Count; i++)
            {
                var p = m.Positions[i];
                target.Positions.Add(frame.P(p.X, p.Y, p.Z));
                target.Colors.Add(shade == 1f ? m.Colors[i] : m.Colors[i] * shade);
            }
            return true;
        }

        /// <summary>Magenta marks the owner's colour: pure magenta is the colour itself, darker magenta a shade of it.</summary>
        public static bool IsTeamMarker(Rgb c) => c.G < 0.02f && Math.Abs(c.R - c.B) < 0.02f && c.R > 0.1f;

        static Rgb Recolor(Rgb c, Rgb team) => IsTeamMarker(c) ? team * c.R : c;

        /// <summary>
        /// "CRM1", int32 triangle count, then per vertex float32 x, y, z and uint8 r, g, b — already in
        /// Unity axes and winding (see export_assets.py).
        /// </summary>
        public static MeshData Parse(byte[] data)
        {
            if (data == null || data.Length < 8 || Encoding.ASCII.GetString(data, 0, 4) != "CRM1")
                throw new InvalidDataException("Not a CRM1 mesh.");
            int tris = BitConverter.ToInt32(data, 4);
            const int stride = 15;
            if (tris < 0 || data.Length != 8 + tris * 3 * stride) throw new InvalidDataException("Truncated CRM1 mesh.");
            var m = new MeshData();
            int o = 8;
            for (int i = 0; i < tris * 3; i++, o += stride)
            {
                m.Positions.Add(new V3(BitConverter.ToSingle(data, o), BitConverter.ToSingle(data, o + 4), BitConverter.ToSingle(data, o + 8)));
                m.Colors.Add(new Rgb(data[o + 12] / 255f, data[o + 13] / 255f, data[o + 14] / 255f));
            }
            return m;
        }
    }
}
