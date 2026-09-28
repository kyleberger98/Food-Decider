using System.Linq;
using Crucible.Core.Content;
using Crucible.Core.Hex;
using Crucible.Core.World;

namespace Crucible.Core.Game
{
    /// <summary>Creates new games. Today: a two-faction skirmish on a generated continent.</summary>
    public static class GameSetup
    {
        /// <param name="allAI">True for AI-vs-AI games (autoplay, balance testing).</param>
        public static GameState NewSkirmish(ulong seed, int width = 48, int height = 32, bool allAI = false)
        {
            var content = DefaultContent.Create();
            var map = MapGenerator.Generate(new MapGeneratorSettings { Seed = seed, Width = width, Height = height });
            var game = new GameState(content, map, seed ^ 0x5EED5EEDUL);
            game.Diplomacy.MajorsStartAtWar = false; // wars must be declared

            var human = game.AddPlayer(allAI ? "The Consul" : "You", DefaultContent.Aurel, isAI: allAI);
            var ai = game.AddPlayer("The Khagan", DefaultContent.Khaganate, isAI: true);

            var starts = PickStarts(map);
            PlaceStart(game, human.Id, starts.a, "Aurelia", "warrior", "warrior", "archer", "spearman", DefaultContent.SettlerUnit);
            PlaceStart(game, ai.Id, starts.b, "Ordu-Baliq", "warrior", "archer", "sky_rider", "horseman");
            PlaceCityStates(game);
            return game;
        }

        static void PlaceStart(GameState game, int playerId, HexCoord capital, string cityName, params string[] units)
        {
            game.FoundCity(playerId, capital, cityName, isCapital: true);
            var armyHex = capital.Neighbors()
                .Where(n => game.Map.Get(n) is Tile t && t.IsPassableForLand && game.ArmyAt(n) == null)
                .OrderBy(n => n.Q).ThenBy(n => n.R)
                .First();
            game.CreateArmy(playerId, armyHex, units);
        }

        /// <summary>Two neutral city-states on good land at least 6 hexes from every city.</summary>
        static void PlaceCityStates(GameState game)
        {
            var specs = new[] { ("Kessra", Empire.CityStateType.Maritime), ("Vallum", Empire.CityStateType.Mercantile) };
            foreach (var (name, type) in specs)
            {
                var site = game.Map.Tiles
                    .Where(t => t.IsPassableForLand && t.Elevation <= 2 && t.OwnerPlayerId < 0)
                    .Where(t => game.Cities.All(c => c.Position.DistanceTo(t.Coord) >= 6))
                    .Where(t => game.ArmyAt(t.Coord) == null)
                    .OrderByDescending(t => game.Cities.Min(c => c.Position.DistanceTo(t.Coord)))
                    .ThenBy(t => t.Coord.Q).ThenBy(t => t.Coord.R)
                    .Select(t => (HexCoord?)t.Coord)
                    .FirstOrDefault();
                if (!site.HasValue) return;
                var cs = game.AddCityState(name, type, site.Value);
                game.CreateArmy(cs.Id, site.Value, "warrior", "archer");
            }
        }

        /// <summary>Two good start tiles (low, dry land with room around it) as far apart as possible.</summary>
        static (HexCoord a, HexCoord b) PickStarts(WorldMap map)
        {
            bool Good(Tile t) =>
                t.IsPassableForLand && t.Elevation <= 2 && t.Terrain != TerrainType.Snow &&
                map.NeighborsOf(t.Coord).Count(n => n.IsPassableForLand) >= 5;

            var candidates = map.Tiles.Where(Good).Select(t => t.Coord).ToList();
            if (candidates.Count < 2) candidates = map.Tiles.Where(t => t.IsPassableForLand).Select(t => t.Coord).ToList();

            var first = candidates.OrderBy(c => c.ToOffset().col).ThenBy(c => c.R).First();
            var second = candidates.OrderByDescending(c => c.DistanceTo(first)).ThenBy(c => c.Q).ThenBy(c => c.R).First();
            return (first, second);
        }
    }
}
