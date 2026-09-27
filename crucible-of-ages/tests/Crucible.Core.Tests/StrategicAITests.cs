using System.Linq;
using Crucible.Core.AI;
using Crucible.Core.Content;
using Crucible.Core.Game;
using Xunit;

namespace Crucible.Core.Tests
{
    /// <summary>M6 exit criterion: the AI expands, fights and can win a domination game on its own.</summary>
    public class StrategicAITests
    {
        [Theory]
        [InlineData(7UL)]
        [InlineData(2024UL)]
        public void Ai_vs_ai_skirmish_ends_in_domination(ulong seed)
        {
            var g = GameSetup.NewSkirmish(seed, 36, 24, allAI: true);
            int battles = 0, maxCities = 0;
            g.BattleStarted += _ => battles++;
            var turns = new TurnManager(g, new StrategicAI());
            turns.Start();
            for (int i = 0; i < 300 && !turns.IsGameOver; i++)
            {
                turns.EndTurn();
                maxCities = System.Math.Max(maxCities, g.Players.Max(p => g.Cities.Count(c => c.OwnerId == p.Id)));
            }

            Assert.NotNull(g.Victory);
            Assert.Equal(VictoryType.Domination, g.Victory.Type);
            Assert.True(battles >= 2, $"battles {battles}");
            Assert.True(maxCities >= 2, "the AI should expand");
        }

        [Fact]
        public void Ai_settles_new_cities()
        {
            var g = GameSetup.NewSkirmish(2024, 36, 24, allAI: true);
            var turns = new TurnManager(g, new StrategicAI());
            turns.Start();
            for (int i = 0; i < 120; i++) turns.EndTurn();
            Assert.All(g.Players, p => Assert.True(g.Cities.Count(c => c.FounderId == p.Id) >= 2, $"{p.Name} founded too few cities"));
            Assert.All(g.Cities.Where(c => !c.IsOriginalCapital), c =>
                Assert.All(g.Cities.Where(o => o != c), o => Assert.True(o.Position.DistanceTo(c.Position) >= 4)));
        }

        [Fact]
        public void Ai_besieges_and_takes_a_weak_neighbour()
        {
            var g = TestWorld.Game(TestWorld.FlatMap(24, 14), attackerAI: true, defenderAI: true);
            g.FoundCity(0, TestWorld.H(3, 6), "Rome", true);
            var target = g.FoundCity(1, TestWorld.H(12, 6), "Veii", true);
            g.CreateArmy(0, TestWorld.H(5, 6), "swordsman", "swordsman", "archer");

            var turns = new TurnManager(g, new StrategicAI());
            turns.Start();
            bool sawSiege = false;
            for (int i = 0; i < 40 && !turns.IsGameOver; i++)
            {
                turns.EndTurn();
                sawSiege |= target.IsBesieged;
            }
            Assert.True(sawSiege || target.OwnerId == 0);
            Assert.Equal(0, target.OwnerId);
            Assert.Equal(VictoryType.Domination, g.Victory?.Type);
        }

        [Fact]
        public void Garrisons_keep_one_defender_and_send_the_rest_out()
        {
            var g = TestWorld.Game(TestWorld.FlatMap(24, 14));
            var city = g.FoundCity(0, TestWorld.H(4, 6), "Home", true);
            g.FoundCity(1, TestWorld.H(20, 8), "Far", true);
            g.CreateArmy(0, city.Position, "warrior", "spearman", "archer", DefaultContent.WorkerUnit);

            new StrategicAI().TakeTurn(g, g.Player(0));

            var garrison = g.ArmyAt(city.Position);
            Assert.Single(garrison.Units);
            Assert.Equal("spearman", garrison.Units[0].Def.Id);
            Assert.Contains(g.Armies, a => a.Units.Any(u => u.Def.Id == DefaultContent.WorkerUnit) && a.Units.All(u => !u.Def.IsMilitary));
        }
    }
}
