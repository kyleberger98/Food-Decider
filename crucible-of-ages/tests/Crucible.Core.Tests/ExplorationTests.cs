using System.Linq;
using Crucible.Core.Game;
using Crucible.Core.World;
using Xunit;

namespace Crucible.Core.Tests
{
    /// <summary>Auto-explore for scouts, armies and fleets; auto-improve for workers.</summary>
    public class ExplorationTests
    {
        [Fact]
        public void A_scout_explores_the_whole_continent_then_stops_and_reports()
        {
            var g = TestWorld.Game(TestWorld.FlatMap(24, 16), attackerAI: false);
            g.FoundCity(0, TestWorld.H(2, 2), "Home", true);
            var scout = g.CreateArmy(0, TestWorld.H(3, 3), "scout");
            int finished = 0;
            g.ExplorationFinished += a => finished += a == scout ? 1 : 0;
            scout.AutoExplore = true;

            var vis = g.Visibility(0);
            int before = vis.Explored.Count;
            var turns = new TurnManager(g);
            turns.Start();
            int afterFive = 0;
            for (int i = 0; i < 60 && scout.AutoExplore; i++)
            {
                turns.EndTurn();
                if (i == 4) afterFive = vis.Explored.Count;
            }
            Assert.True(afterFive > before + 40, $"explored {before} → {afterFive}");
            Assert.False(scout.AutoExplore);
            Assert.Equal(1, finished);
            Assert.Equal(g.Map.Tiles.Count, vis.Explored.Count); // nothing left unseen on an open map
        }

        [Fact]
        public void Explorers_split_up_instead_of_following_each_other()
        {
            var g = TestWorld.Game(TestWorld.FlatMap(30, 20), attackerAI: false);
            var a = g.CreateArmy(0, TestWorld.H(15, 10), "scout");
            var b = g.CreateArmy(0, TestWorld.H(16, 10), "warrior");
            a.AutoExplore = b.AutoExplore = true;
            Exploration.Run(g, 0);
            Assert.True(a.Destination.HasValue || a.Position != TestWorld.H(15, 10));
            var ta = a.Destination ?? a.Position;
            var tb = b.Destination ?? b.Position;
            Assert.True(ta.DistanceTo(tb) > 2, $"{ta} vs {tb}");
        }

        [Fact]
        public void Explorers_keep_away_from_enemies_at_war()
        {
            var g = TestWorld.Game(TestWorld.FlatMap(20, 12), attackerAI: false);
            var scout = g.CreateArmy(0, TestWorld.H(10, 6), "scout");
            var enemy = g.CreateArmy(1, TestWorld.H(13, 6), "swordsman");
            Assert.True(g.Diplomacy.IsAtWar(0, 1));
            scout.AutoExplore = true;
            for (int turn = 0; turn < 6; turn++)
            {
                scout.WorldMovesLeft = g.WorldMovementOf(scout);
                Exploration.Step(g, scout);
                Assert.True(scout.Position.DistanceTo(enemy.Position) > 1, $"turn {turn}: {scout.Position}");
                if (scout.Destination.HasValue) Assert.True(scout.Destination.Value.DistanceTo(enemy.Position) > 2);
            }
        }

        [Fact]
        public void Fleets_explore_the_sea_and_civilians_cannot_explore()
        {
            var map = TestWorld.FlatMap(24, 14);
            foreach (var t in map.Tiles.Where(t => t.Coord.ToOffset().col >= 6)) { t.Terrain = TerrainType.Coast; t.Elevation = -1; }
            var g = TestWorld.Game(map, attackerAI: false);
            var fleet = g.CreateArmy(0, TestWorld.H(7, 7), "trireme");
            fleet.AutoExplore = true;
            int before = g.Visibility(0).Explored.Count;
            for (int turn = 0; turn < 4; turn++)
            {
                fleet.WorldMovesLeft = g.WorldMovementOf(fleet);
                Exploration.Step(g, fleet);
            }
            Assert.True(g.Visibility(0).Explored.Count > before + 20);
            Assert.True(g.Map.Get(fleet.Position).IsWater);

            var worker = g.CreateArmy(0, TestWorld.H(2, 2), "worker");
            Assert.False(Exploration.CanExplore(worker));
            worker.AutoExplore = true;
            Assert.False(Exploration.Step(g, worker));
            Assert.False(worker.AutoExplore);
        }

        [Fact]
        public void Auto_explore_survives_save_and_load()
        {
            var g = TestWorld.Game(TestWorld.FlatMap(), attackerAI: false);
            g.FoundCity(0, TestWorld.H(2, 2), "Home", true);
            g.FoundCity(1, TestWorld.H(13, 9), "Away", true);
            var scout = g.CreateArmy(0, TestWorld.H(3, 3), "scout");
            scout.AutoExplore = true;
            var turns = new TurnManager(g);
            turns.Start();
            var (loaded, _) = SaveGame.Load(SaveGame.Save(g, turns));
            Assert.True(loaded.Army(scout.Id).AutoExplore);
        }

        [Fact]
        public void A_players_auto_improving_worker_builds_on_its_own()
        {
            var g = TestWorld.Game(TestWorld.FlatMap(), attackerAI: false);
            g.FoundCity(0, TestWorld.H(4, 4), "Home", true);
            g.FoundCity(1, TestWorld.H(13, 9), "Away", true);
            foreach (var tech in new[] { "agriculture", "mining", "animal_husbandry" })
                if (g.Content.Techs.Any(t => t.Id == tech)) g.Player(0).Tech.Grant(tech);
            var worker = g.CreateArmy(0, TestWorld.H(5, 5), "worker");
            worker.AutomatedWorkers = true;
            var turns = new TurnManager(g);
            turns.Start();
            for (int i = 0; i < 25; i++) turns.EndTurn();
            Assert.True(g.Map.Tiles.Count(t => t.OwnerPlayerId == 0 && t.Improvement != ImprovementType.None) >= 2);
        }
    }
}
