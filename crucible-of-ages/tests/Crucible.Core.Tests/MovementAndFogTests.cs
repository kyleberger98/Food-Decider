using System.Linq;
using Crucible.Core.Game;
using Crucible.Core.Hex;
using Crucible.Core.World;
using Xunit;

namespace Crucible.Core.Tests
{
    public class MovementAndFogTests
    {
        [Fact]
        public void Path_on_open_ground_is_the_straight_distance()
        {
            var g = TestWorld.Game(TestWorld.FlatMap(20, 12));
            var army = g.CreateArmy(0, TestWorld.H(2, 5), "warrior"); // 2 MP
            var dest = TestWorld.H(9, 5);
            var path = Pathfinder.Find(g, army, dest);

            Assert.NotNull(path);
            Assert.Equal(army.Position.DistanceTo(dest), path.Steps.Count);
            Assert.Equal(dest, path.Destination);
            Assert.Equal(4, path.Turns); // 7 hexes at 2 MP/turn
            for (int i = 1; i < path.Steps.Count; i++) Assert.Equal(1, path.Steps[i - 1].DistanceTo(path.Steps[i]));
        }

        [Fact]
        public void Path_detours_around_a_cliff_wall()
        {
            var map = TestWorld.FlatMap(20, 14, elevation: 0);
            // A plateau two levels up in column 6, rows 2–11: impassable from the lowlands.
            for (int row = 2; row <= 11; row++) map.Get(6, row).Elevation = 2;
            var g = TestWorld.Game(map);
            var army = g.CreateArmy(0, TestWorld.H(3, 6), "warrior");
            var path = Pathfinder.Find(g, army, TestWorld.H(9, 6));

            Assert.NotNull(path);
            Assert.DoesNotContain(path.Steps, h => map.Get(h).Elevation == 2);
            Assert.True(path.Steps.Count > army.Position.DistanceTo(TestWorld.H(9, 6)));
        }

        [Fact]
        public void Unreachable_destinations_return_null()
        {
            var map = TestWorld.FlatMap(12, 10);
            map.Get(6, 5).Terrain = TerrainType.Ocean;
            map.Get(6, 5).Elevation = -2;
            var g = TestWorld.Game(map);
            var army = g.CreateArmy(0, TestWorld.H(2, 5), "warrior");
            Assert.Null(Pathfinder.Find(g, army, TestWorld.H(6, 5)));
        }

        [Fact]
        public void Crossing_a_river_costs_the_rest_of_the_turn()
        {
            var map = TestWorld.FlatMap(12, 10);
            var g = TestWorld.Game(map);
            var start = TestWorld.H(3, 5);
            var army = g.CreateArmy(0, start, "horseman"); // 4 MP
            var dest = start + HexCoord.Direction(0) * 3;
            Assert.Equal(1, Pathfinder.Find(g, army, dest).Turns);

            // A river along every edge between columns 3 and 4 of all rows.
            for (int row = 0; row < 10; row++)
            {
                var a = TestWorld.H(3, row);
                map.AddRiver(a, a.Neighbor(0));
                if (row + 1 < 10) map.AddRiver(a, a.Neighbor(5));
                if (row > 0) map.AddRiver(a, a.Neighbor(1));
            }
            Assert.Equal(2, Pathfinder.Find(g, army, dest).Turns);
        }

        [Fact]
        public void Move_orders_continue_over_several_turns()
        {
            var g = TestWorld.Game(TestWorld.FlatMap(24, 12), attackerAI: false);
            g.FoundCity(0, TestWorld.H(1, 1), "A", true);
            g.FoundCity(1, TestWorld.H(22, 10), "B", true);
            var army = g.CreateArmy(0, TestWorld.H(2, 5), "warrior");
            var dest = TestWorld.H(10, 5);

            var turns = new TurnManager(g);
            turns.Start();
            var path = g.OrderMove(army, dest);
            Assert.Equal(4, path.Turns);
            Assert.Equal(dest, army.Destination);
            Assert.Equal(2, army.Position.DistanceTo(TestWorld.H(2, 5)));

            for (int i = 0; i < 3; i++) turns.EndTurn();
            Assert.Equal(dest, army.Position);
            Assert.Null(army.Destination);
        }

        [Fact]
        public void Fog_reveals_around_armies_and_remembers_explored_hexes()
        {
            var g = TestWorld.Game(TestWorld.FlatMap(24, 12));
            var army = g.CreateArmy(0, TestWorld.H(3, 5), "warrior");
            var vis = g.Visibility(0);

            Assert.Equal(VisibilityState.Visible, vis.Get(army.Position));
            Assert.Equal(VisibilityState.Visible, vis.Get(TestWorld.H(5, 5)));
            Assert.Equal(VisibilityState.Unexplored, vis.Get(TestWorld.H(12, 5)));

            int version = vis.Version;
            var start = army.Position;
            Assert.True(g.MoveArmy(army, start.Neighbor(0)));
            Assert.True(g.MoveArmy(army, start.Neighbor(0).Neighbor(0)));
            Assert.True(vis.Version > version);
            Assert.Equal(VisibilityState.Fogged, vis.Get(start.Neighbor(3).Neighbor(3))); // left behind
        }

        [Fact]
        public void Ridges_and_forests_block_sight_and_hills_extend_it()
        {
            var map = TestWorld.FlatMap(24, 12, elevation: 1);
            var here = TestWorld.H(5, 5);
            var behind = here + HexCoord.Direction(0) * 2;
            map.Get(here.Neighbor(0)).Elevation = 2;
            var g = TestWorld.Game(map);
            var army = g.CreateArmy(0, here, "warrior");
            Assert.False(g.Visibility(0).IsVisible(behind)); // ridge in the way
            Assert.True(g.Visibility(0).IsVisible(here.Neighbor(0))); // the ridge itself is seen

            map.Get(here).Elevation = 2; // now on the hill: level with the ridge, sight 3
            g.RefreshVisibility(0);
            Assert.True(g.Visibility(0).IsVisible(behind));
            Assert.True(g.Visibility(0).IsVisible(here + HexCoord.Direction(3) * 3));
        }
    }
}
