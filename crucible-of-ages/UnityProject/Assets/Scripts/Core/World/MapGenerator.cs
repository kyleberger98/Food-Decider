using System;
using System.Collections.Generic;
using System.Linq;
using Crucible.Core.Hex;
using Crucible.Core.Random;

namespace Crucible.Core.World
{
    public enum MapScript
    {
        /// <summary>Two large continents with a few offshore islands.</summary>
        Continents,

        /// <summary>One supercontinent.</summary>
        Pangaea,

        /// <summary>Many medium islands.</summary>
        Archipelago,
    }

    public sealed class MapGeneratorSettings
    {
        public int Width = 48;
        public int Height = 32;
        public ulong Seed = 1;
        public MapScript Script = MapScript.Continents;

        /// <summary>Share of the map that is land; 0 picks the script's default.</summary>
        public double LandFraction;

        /// <summary>One river source per this many land tiles.</summary>
        public int LandTilesPerRiver = 40;

        /// <summary>One lake per this many land tiles.</summary>
        public int LandTilesPerLake = 80;

        public int NaturalWonders = 3;
    }

    /// <summary>
    /// Deterministic world generator (GDD §2). Land comes from continent "blobs" whose coastlines are
    /// warped by noise, so shapes read as continents and islands rather than a single smudge. Ridge
    /// noise raises mountain ranges; latitude and moisture set climate (ice caps, tundra and taiga, temperate
    /// grass and forest, deserts with oases, equatorial jungle and marsh); lakes fill inland hollows;
    /// rivers run downhill; a few natural wonders crown the map.
    /// </summary>
    public static class MapGenerator
    {
        struct Blob
        {
            public double X, Y, Rx, Ry;
        }

        public static WorldMap Generate(MapGeneratorSettings s)
        {
            var rng = new DeterministicRng(s.Seed);
            var map = new WorldMap(s.Width, s.Height);

            var shapeNoise = new ValueNoise(rng, 5);
            var detailNoise = new ValueNoise(rng, 13);
            var ridgeNoise = new ValueNoise(rng, 8);
            var moistNoise = new ValueNoise(rng, 7);
            var warpX = new ValueNoise(rng, 4);
            var warpY = new ValueNoise(rng, 4);
            var blobs = MakeBlobs(s.Script, rng);

            // Pass 1: raw height. Sea level is then chosen so the land share hits its target on any seed.
            var heights = new double[map.Tiles.Count];
            int index = 0;
            foreach (var tile in map.Tiles)
            {
                var (col, row) = tile.Coord.ToOffset();
                double u = (col + 0.5) / s.Width, v = (row + 0.5) / s.Height;

                // Warp the sample point so coastlines wander instead of tracing ellipses.
                double uw = u + (warpX.Sample(u, v) - 0.5) * 0.16;
                double vw = v + (warpY.Sample(u, v) - 0.5) * 0.16;
                double m1 = 0, m2 = 0; // strongest and second-strongest blob
                foreach (var b in blobs)
                {
                    double dx = (uw - b.X) / b.Rx, dy = (vw - b.Y) / b.Ry;
                    double m = Smooth(Clamp01(1.0 - Math.Sqrt(dx * dx + dy * dy)));
                    if (m > m1) { m2 = m1; m1 = m; }
                    else if (m > m2) m2 = m;
                }
                // Islands keep a channel between them where their blobs meet.
                double mask = s.Script == MapScript.Archipelago ? m1 - 0.9 * m2 : m1;
                double h = mask * 0.68 + shapeNoise.Sample(u, v) * 0.2 + detailNoise.Sample(u, v) * 0.12;
                double edge = Math.Min(Math.Min(u, 1 - u), Math.Min(v, 1 - v)); // 0 at border, 0.5 in centre
                h *= Smooth(Math.Min(1.0, edge / 0.1));
                heights[index++] = h;
            }

            double landFraction = s.LandFraction > 0 ? s.LandFraction : DefaultLandFraction(s.Script);
            var sorted = (double[])heights.Clone();
            Array.Sort(sorted);
            double seaLevel = sorted[Math.Min(sorted.Length - 1, (int)((1.0 - landFraction) * sorted.Length))];
            double peak = Math.Max(seaLevel + 1e-6, sorted[sorted.Length - 1]);

            // Pass 2: terrain, climate and features.
            index = 0;
            foreach (var tile in map.Tiles)
            {
                var (col, row) = tile.Coord.ToOffset();
                double u = (col + 0.5) / s.Width, v = (row + 0.5) / s.Height;
                double h = heights[index++];
                double latitude = Math.Abs(v - 0.5) * 2.0; // 0 equator, 1 pole
                if (h < seaLevel)
                {
                    tile.Terrain = TerrainType.Ocean;
                    tile.Elevation = -2;
                    // Pack ice: solid at the poles, ragged towards the temperate seas.
                    if (latitude > 0.93 || (latitude > 0.8 && moistNoise.Sample(u, v) + (latitude - 0.8) * 3 > 0.95))
                        tile.Feature = FeatureType.Ice;
                    continue;
                }

                // Height above sea level sets lowland → highland; ridge noise lifts mountain ranges.
                double t = (h - seaLevel) / (peak - seaLevel);
                int elevation = t < 0.2 ? 0 : t < 0.48 ? 1 : t < 0.78 ? 2 : 3;
                double ridge = 1.0 - Math.Abs(2.0 * ridgeNoise.Sample(u, v) - 1.0); // 1 along ridge lines
                if ((t > 0.15 && ridge > 0.93) || t > 0.95) elevation = Tile.MountainElevation;
                else if (t > 0.08 && ridge > 0.82) elevation = Math.Min(3, elevation + 1);
                tile.Elevation = elevation;

                double moisture = moistNoise.Sample(u, v);
                tile.Terrain = PickTerrain(latitude, moisture, tile.Elevation);
                tile.Feature = PickFeature(rng, latitude, moisture, tile);
            }

            MarkCoasts(map);
            FillLakes(map, rng, s.LandTilesPerLake);
            CarveRivers(map, rng, s.LandTilesPerRiver);
            PlaceWonders(map, rng, s.NaturalWonders);
            // Separate stream so resource tuning never reshapes the terrain of a given seed.
            PlaceResources(map, new DeterministicRng(s.Seed ^ 0xA5A5_5A5A_1234_4321UL));
            return map;
        }

        static double DefaultLandFraction(MapScript script) => script switch
        {
            MapScript.Pangaea => 0.42,
            MapScript.Archipelago => 0.3,
            _ => 0.38,
        };

        static List<Blob> MakeBlobs(MapScript script, DeterministicRng rng)
        {
            var blobs = new List<Blob>();
            void Add(double x, double y, double rx, double ry) => blobs.Add(new Blob { X = x, Y = y, Rx = rx, Ry = ry });
            double J(double spread) => (rng.NextDouble() - 0.5) * 2 * spread;

            switch (script)
            {
                case MapScript.Pangaea:
                    Add(0.5 + J(0.04), 0.5 + J(0.05), 0.40, 0.38);
                    Add(0.3 + J(0.05), 0.35 + J(0.05), 0.18, 0.2);
                    Add(0.7 + J(0.05), 0.65 + J(0.05), 0.18, 0.2);
                    break;
                case MapScript.Archipelago:
                    // A jittered 4×3 grid of islands, a few left out as open sea.
                    for (int gx = 0; gx < 4; gx++)
                        for (int gy = 0; gy < 3; gy++)
                        {
                            if (rng.NextDouble() < 0.2) continue;
                            Add(0.16 + gx * 0.225 + J(0.05), 0.24 + gy * 0.26 + J(0.05), 0.08 + rng.NextDouble() * 0.05, 0.09 + rng.NextDouble() * 0.06);
                        }
                    break;
                default:
                    Add(0.27 + J(0.04), 0.5 + J(0.08), 0.22 + J(0.03), 0.36 + J(0.04));
                    Add(0.73 + J(0.04), 0.5 + J(0.08), 0.22 + J(0.03), 0.36 + J(0.04));
                    for (int i = 0; i < 3; i++) Add(0.1 + rng.NextDouble() * 0.8, 0.12 + rng.NextDouble() * 0.76, 0.05 + rng.NextDouble() * 0.04, 0.06 + rng.NextDouble() * 0.05);
                    break;
            }
            return blobs;
        }

        static TerrainType PickTerrain(double latitude, double moisture, int elevation)
        {
            if (latitude > 0.88 || (elevation >= 4 && latitude > 0.6)) return TerrainType.Snow;
            if (latitude > 0.72) return TerrainType.Tundra;
            // Subtropical desert belt, plus dry interiors further from the equator.
            if ((latitude > 0.12 && latitude < 0.5 && moisture < 0.38) || moisture < 0.22) return TerrainType.Desert;
            return moisture > 0.52 ? TerrainType.Grassland : TerrainType.Plains;
        }

        static FeatureType PickFeature(DeterministicRng rng, double latitude, double moisture, Tile tile)
        {
            double roll = rng.NextDouble();
            if (tile.IsMountain || tile.Terrain == TerrainType.Snow) return FeatureType.None;
            if (tile.Terrain == TerrainType.Desert)
                return roll < 0.05 && tile.Elevation <= 1 ? FeatureType.Oasis : FeatureType.None;
            if (tile.Terrain == TerrainType.Tundra) return moisture > 0.5 && roll < 0.45 ? FeatureType.Forest : FeatureType.None; // taiga
            if (latitude < 0.28 && moisture > 0.6 && roll < 0.65) return FeatureType.Jungle;
            if (moisture > 0.54 && roll < 0.45) return FeatureType.Forest;
            if (tile.Elevation == 0 && moisture > 0.58 && roll < 0.22) return FeatureType.Marsh;
            return FeatureType.None;
        }

        static void MarkCoasts(WorldMap map)
        {
            foreach (var tile in map.Tiles)
            {
                if (tile.Terrain != TerrainType.Ocean) continue;
                if (map.NeighborsOf(tile.Coord).Any(n => !n.IsWater))
                {
                    tile.Terrain = TerrainType.Coast;
                    tile.Elevation = -1;
                }
            }
        }

        /// <summary>Lakes sit in low inland hollows, sometimes two hexes long.</summary>
        static void FillLakes(WorldMap map, DeterministicRng rng, int landTilesPerLake)
        {
            bool Inland(Tile t) => t.IsPassableForLand && t.Elevation <= 1 && t.Terrain != TerrainType.Snow &&
                                   map.NeighborsOf(t.Coord).Count() == 6 && map.NeighborsOf(t.Coord).All(n => !n.IsWater);
            var candidates = map.Tiles.Where(Inland).ToList();
            int count = Math.Min(candidates.Count, map.Tiles.Count(t => !t.IsWater) / Math.Max(1, landTilesPerLake));
            for (int i = 0; i < count && candidates.Count > 0; i++)
            {
                var lake = candidates[rng.NextInt(candidates.Count)];
                MakeLake(lake);
                var extra = map.NeighborsOf(lake.Coord).Where(Inland).ToList();
                if (extra.Count > 0 && rng.NextDouble() < 0.5) MakeLake(extra[rng.NextInt(extra.Count)]);
                candidates.RemoveAll(c => c.Coord.DistanceTo(lake.Coord) <= 3);
            }
        }

        static void MakeLake(Tile t)
        {
            t.Terrain = TerrainType.Lake;
            t.Elevation = -1;
            t.Feature = FeatureType.None;
        }

        static void CarveRivers(WorldMap map, DeterministicRng rng, int landTilesPerRiver)
        {
            var sources = map.Tiles.Where(t => !t.IsWater && t.Elevation >= 2).ToList();
            int landCount = map.Tiles.Count(t => !t.IsWater);
            int riverCount = Math.Min(sources.Count, landCount / Math.Max(1, landTilesPerRiver));

            for (int i = 0; i < riverCount; i++)
            {
                var current = sources[rng.NextInt(sources.Count)];
                var visited = new HashSet<HexCoord> { current.Coord };
                for (int step = 0; step < 30 && !current.IsWater; step++)
                {
                    // TODO: proper edge-following rivers; for now a river marks the edge crossed by
                    // each downhill step, which is what matters for combat and yields.
                    var next = map.NeighborsOf(current.Coord)
                        .Where(n => !visited.Contains(n.Coord) && n.Elevation <= current.Elevation && !n.IsIce)
                        .OrderBy(n => n.Elevation)
                        .ThenBy(n => rng.NextInt(1000))
                        .FirstOrDefault();
                    if (next == null) break;
                    map.AddRiver(current.Coord, next.Coord);
                    if (!next.IsWater && next.Terrain == TerrainType.Desert && next.Feature == FeatureType.None) next.Feature = FeatureType.Floodplain;
                    visited.Add(next.Coord);
                    current = next;
                }
            }
        }

        /// <summary>A few natural wonders, each on fitting ground and well apart from the others.</summary>
        static void PlaceWonders(WorldMap map, DeterministicRng rng, int count)
        {
            var placed = new List<HexCoord>();
            var kinds = new[] { NaturalWonder.Worldspine, NaturalWonder.GlassDunes, NaturalWonder.EmberfallGeyser };
            foreach (var kind in kinds.Take(count))
            {
                Func<Tile, bool> fits;
                switch (kind)
                {
                    case NaturalWonder.Worldspine: fits = t => t.IsMountain && t.Terrain != TerrainType.Snow; break;
                    case NaturalWonder.GlassDunes: fits = t => t.IsPassableForLand && (t.Terrain == TerrainType.Desert || t.Terrain == TerrainType.Plains) && t.Feature == FeatureType.None; break;
                    default: fits = t => t.IsPassableForLand && t.Elevation >= 1 && t.Feature == FeatureType.None && t.Terrain != TerrainType.Snow; break;
                }
                var options = map.Tiles.Where(t => fits(t) && t.Wonder == NaturalWonder.None && placed.All(p => p.DistanceTo(t.Coord) >= 8)).ToList();
                if (options.Count == 0) continue;
                var tile = options[rng.NextInt(options.Count)];
                tile.Wonder = kind;
                placed.Add(tile.Coord);
            }
        }

        /// <summary>Scatters resources where they make sense (GDD §2.3). Roughly 1 tile in 9 gets one.</summary>
        static void PlaceResources(WorldMap map, DeterministicRng rng)
        {
            foreach (var t in map.Tiles)
            {
                double roll = rng.NextDouble();
                if (t.IsMountain || t.IsIce || t.Wonder != NaturalWonder.None || t.Terrain == TerrainType.Snow || t.Terrain == TerrainType.Ocean) continue;
                bool hills = !t.IsWater && t.Elevation >= 2;

                if (t.Terrain == TerrainType.Coast || t.Terrain == TerrainType.Lake) { if (roll < 0.08) t.Resource = ResourceType.Fish; continue; }
                if (hills && t.Feature == FeatureType.None)
                {
                    t.Resource = roll < 0.07 ? ResourceType.Iron : roll < 0.10 ? ResourceType.Gems : t.Resource;
                    continue;
                }
                switch (t.Feature)
                {
                    case FeatureType.Forest:
                        t.Resource = roll < 0.05 ? (t.Terrain == TerrainType.Tundra ? ResourceType.Furs : ResourceType.Silk) : ResourceType.None;
                        continue;
                    case FeatureType.Jungle:
                        t.Resource = roll < 0.05 ? ResourceType.Gems : ResourceType.None;
                        continue;
                    case FeatureType.Marsh:
                        t.Resource = roll < 0.06 ? ResourceType.Oil : ResourceType.None;
                        continue;
                    case FeatureType.Oasis:
                        continue;
                }
                switch (t.Terrain)
                {
                    case TerrainType.Grassland:
                        t.Resource = roll < 0.04 ? ResourceType.Horses : roll < 0.08 ? ResourceType.Cattle : roll < 0.10 ? ResourceType.Wine : ResourceType.None;
                        break;
                    case TerrainType.Plains:
                        t.Resource = roll < 0.04 ? ResourceType.Horses : roll < 0.08 ? ResourceType.Wheat : roll < 0.10 ? ResourceType.Wine : roll < 0.12 ? ResourceType.Iron : ResourceType.None;
                        break;
                    case TerrainType.Desert:
                        t.Resource = t.Feature == FeatureType.Floodplain && roll < 0.2 ? ResourceType.Wheat : roll < 0.05 ? ResourceType.Oil : ResourceType.None;
                        break;
                    case TerrainType.Tundra:
                        t.Resource = roll < 0.05 ? ResourceType.Oil : roll < 0.09 ? ResourceType.Furs : ResourceType.None;
                        break;
                }
            }
        }

        static double Smooth(double x) => x * x * (3 - 2 * x);
        static double Clamp01(double x) => x < 0 ? 0 : x > 1 ? 1 : x;

        /// <summary>Bilinear-interpolated lattice noise in [0,1], sampled with u,v in [0,1].</summary>
        sealed class ValueNoise
        {
            readonly double[,] _lattice;
            readonly int _cells;

            public ValueNoise(DeterministicRng rng, int cells)
            {
                _cells = cells;
                _lattice = new double[cells + 1, cells + 1];
                for (int x = 0; x <= cells; x++)
                    for (int y = 0; y <= cells; y++)
                        _lattice[x, y] = rng.NextDouble();
            }

            public double Sample(double u, double v)
            {
                double x = Clamp01(u) * _cells, y = Clamp01(v) * _cells;
                int x0 = Math.Min((int)x, _cells - 1), y0 = Math.Min((int)y, _cells - 1);
                double fx = Smooth(x - x0), fy = Smooth(y - y0);
                double a = Lerp(_lattice[x0, y0], _lattice[x0 + 1, y0], fx);
                double b = Lerp(_lattice[x0, y0 + 1], _lattice[x0 + 1, y0 + 1], fx);
                return Lerp(a, b, fy);
            }

            static double Lerp(double a, double b, double t) => a + (b - a) * t;
        }
    }
}
