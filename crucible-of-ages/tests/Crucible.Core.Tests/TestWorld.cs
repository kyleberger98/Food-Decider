using Crucible.Core.Combat;
using Crucible.Core.Content;
using Crucible.Core.Empire;
using Crucible.Core.Game;
using Crucible.Core.Hex;
using Crucible.Core.Random;
using Crucible.Core.Units;
using Crucible.Core.World;

namespace Crucible.Core.Tests
{
    /// <summary>Hand-built flat grassland worlds for deterministic rule tests.</summary>
    static class TestWorld
    {
        public static readonly ContentDatabase Content = DefaultContent.Create();

        public static WorldMap FlatMap(int width = 16, int height = 12, int elevation = 1)
        {
            var map = new WorldMap(width, height);
            foreach (var t in map.Tiles)
            {
                t.Terrain = TerrainType.Grassland;
                t.Elevation = elevation;
            }
            return map;
        }

        public static GameState Game(WorldMap map = null, bool attackerAI = true, bool defenderAI = true)
        {
            var g = new GameState(Content, map ?? FlatMap(), 42);
            g.AddPlayer("Attacker", DefaultContent.Khaganate, attackerAI); // cap 4
            g.AddPlayer("Defender", DefaultContent.Khaganate, defenderAI); // cap 4
            return g;
        }

        public static Player Player(int id, string faction = DefaultContent.Khaganate) =>
            new Player(id, "P" + id, Content.Faction(faction), isAI: true, Content);

        static int _nextId = 10_000;

        public static Unit Unit(string defId, int owner) => new Unit(_nextId++, Content.Unit(defId), owner);

        public static HexCoord H(int col, int row) => HexCoord.FromOffset(col, row);

        /// <summary>A battle between two single-army sides on a flat map, already started.</summary>
        public static Battle Battle(WorldMap map, HexCoord aPos, string[] aUnits, HexCoord dPos, string[] dUnits,
            ulong seed = 7, Player attacker = null, Player defender = null, HexCoord? objective = null)
        {
            attacker ??= Player(0);
            defender ??= Player(1);
            var aArmy = new Army(1, attacker.Id, aPos);
            foreach (var id in aUnits) aArmy.TryAdd(Unit(id, attacker.Id), 99);
            var dArmy = new Army(2, defender.Id, dPos);
            foreach (var id in dUnits) dArmy.TryAdd(Unit(id, defender.Id), 99);

            var tiles = Combat.Battlefield.Generate(map, aPos, dPos, Combat.Battlefield.FieldRadius);
            var battle = new Battle(1, map, tiles,
                new BattleSide(BattleSideId.Attacker, attacker, aPos),
                new BattleSide(BattleSideId.Defender, defender, dPos),
                new DeterministicRng(seed), objective);
            battle.AddArmy(BattleSideId.Attacker, aArmy);
            battle.AddArmy(BattleSideId.Defender, dArmy);
            battle.Start();
            return battle;
        }
    }
}
