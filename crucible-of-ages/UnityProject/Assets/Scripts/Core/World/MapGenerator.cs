using System;
using System.Collections.Generic;
using System.Linq;
using Crucible.Core.Hex;
using Crucible.Core.Random;

namespace Crucible.Core.World
{
    public sealed class MapGeneratorSettings
    {
        public int Width = 48;
        public int Height = 32;
        public ulong Seed = 1;

        /// <summary>Roughly the fraction of tiles that end up as water.</summary>
        public double SeaLevel = 0.42;

        /// <summary>One river source per this many land tiles.</summary>
        public int LandTilesPerRiver = 45;
    }

    /// <summary>
    /// Simple deterministic continent generator: layered value noise for height and moisture,
    /// an edge falloff so the map is ringed by ocean, latitude-based climate, and downhill rivers.
    /// Placeholder for the real map scripts (Continents, Pangaea, Archipelago…).
    /// </summary>
    public static class MapGenerator
    {
        public static WorldMap Generate(MapGeneratorSettings s)
        {
            var rng = new DeterministicRng(s.Seed);
            var map = new WorldMap(s.Width, s.Height);

            var heightNoise = new ValueNoise(rng, 6);
            var detailNoise = new ValueNoise(rng, 14);
            var moistNoise = new ValueNoise(rng, 8);

            foreach (var tile in map.Tiles)
            {
                var (col, row) = tile.Coord.ToOffset();
                double u = (double)col / s.Width, v = (double)row / s.Height;

                double h = heightNoise.Sample(u, v) * 0.75 + detailNoise.Sample(u, v) * 0.25;
                double edge = Math.Min(Math.Min(u, 1 - u), Math.Min(v, 1 - v)); // 0 at border, 0.5 in centre
                h *= Smooth(Math.Min(1.0, edge / 0.12));

                double landRange = 1.0 - s.SeaLevel;
                if (h < s.SeaLevel)
                {
                    tile.Terrain = TerrainType.Ocean;
                    tile.Elevation = -2;
                    continue;
                }

                double t = (h - s.SeaLevel) / landRange; // 0..1 above sea level
                tile.Elevation = t < 0.22 ? 0 : t < 0.48 ? 1 : t < 0.68 ? 2 : t < 0.84 ? 3 : 4;

                double latitude = Math.Abs(v - 0.5) * 2.0; // 0 equator, 1 pole
                double moisture = moistNoise.Sample(u, v);
                tile.Terrain = PickTerrain(latitude, moisture, tile.Elevation);
                tile.Feature = PickFeature(rng, latitude, moisture, tile);
            }

            MarkCoasts(map);
            CarveRivers(map, rng, s.LandTilesPerRiver);
            // Separate stream so resource tuning never reshapes the terrain of a given seed.
            PlaceResources(map, new DeterministicRng(s.Seed ^ 0xA5A5_5A5A_1234_4321UL));
            return map;
        }

        /// <summary>Scatters resources where they make sense (GDD §2.3). Roughly 1 tile in 9 gets one.</summary>
        static void PlaceResources(WorldMap map, DeterministicRng rng)
        {
            foreach (var t in map.Tiles)
            {
                if (t.IsMountain || t.Terrain == TerrainType.Snow || t.Terrain == TerrainType.Ocean) continue;
                double roll = rng.NextDouble();
                bool hills = !t.IsWater && t.Elevation >= 2;

                if (t.Terrain == TerrainType.Coast) { if (roll < 0.08) t.Resource = ResourceType.Fish; continue; }
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

        static TerrainType PickTerrain(double latitude, double moisture, int elevation)
        {
            if (latitude > 0.88 || (elevation >= 4 && latitude > 0.6)) return TerrainType.Snow;
            if (latitude > 0.72) return TerrainType.Tundra;
            if (latitude < 0.45 && moisture < 0.32) return TerrainType.Desert;
            return moisture > 0.5 ? TerrainType.Grassland : TerrainType.Plains;
        }

        static FeatureType PickFeature(DeterministicRng rng, double latitude, double moisture, Tile tile)
        {
            if (tile.IsMountain || tile.Terrain == TerrainType.Snow || tile.Terrain == TerrainType.Desert)
                return FeatureType.None;
            double roll = rng.NextDouble();
            if (latitude < 0.3 && moisture > 0.62 && roll < 0.6) return FeatureType.Jungle;
            if (moisture > 0.55 && roll < 0.45) return FeatureType.Forest;
            if (tile.Elevation == 0 && moisture > 0.6 && roll < 0.2) return FeatureType.Marsh;
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
                    // TODO(M1): proper edge-following rivers; for now a river marks the edge
                    // crossed by each downhill step, which is what matters for combat.
                    var next = map.NeighborsOf(current.Coord)
                        .Where(n => !visited.Contains(n.Coord) && n.Elevation <= current.Elevation)
                        .OrderBy(n => n.Elevation)
                        .ThenBy(n => rng.NextInt(1000))
                        .FirstOrDefault();
                    if (next == null) break;
                    map.AddRiver(current.Coord, next.Coord);
                    if (!next.IsWater && next.Terrain == TerrainType.Desert) next.Feature = FeatureType.Floodplain;
                    visited.Add(next.Coord);
                    current = next;
                }
            }
        }

        static double Smooth(double x) => x * x * (3 - 2 * x);

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
            static double Clamp01(double x) => x < 0 ? 0 : x > 1 ? 1 : x;
        }
    }
}
