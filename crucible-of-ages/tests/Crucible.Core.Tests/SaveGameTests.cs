using System.IO;
using System.Linq;
using Crucible.Core.AI;
using Crucible.Core.Combat;
using Crucible.Core.Game;
using Xunit;

namespace Crucible.Core.Tests
{
    public class SaveGameTests
    {
        [Theory]
        [InlineData(7UL, 40)]
        [InlineData(2024UL, 55)]
        public void Save_then_load_plays_on_identically(ulong seed, int turnsBeforeSave)
        {
            var g = GameSetup.NewSkirmish(seed, 36, 24, allAI: true);
            var turns = new TurnManager(g, new StrategicAI());
            turns.Start();
            for (int i = 0; i < turnsBeforeSave && !turns.IsGameOver; i++) turns.EndTurn();

            var bytes = SaveGame.Save(g, turns);
            var (loaded, loadedTurns) = SaveGame.Load(bytes, new StrategicAI());
            Assert.Equal(bytes, SaveGame.Save(loaded, loadedTurns)); // round-trips exactly

            for (int i = 0; i < 60; i++)
            {
                if (!turns.IsGameOver) turns.EndTurn();
                if (!loadedTurns.IsGameOver) loadedTurns.EndTurn();
            }
            Assert.Equal(SaveGame.Save(g, turns), SaveGame.Save(loaded, loadedTurns)); // same future
        }

        [Fact]
        public void Battles_in_progress_survive_a_save()
        {
            var g = TestWorld.Game(TestWorld.FlatMap(20, 12), attackerAI: false, defenderAI: false);
            g.FoundCity(0, TestWorld.H(2, 2), "A", true);
            g.FoundCity(1, TestWorld.H(17, 9), "B", true);
            var a = g.CreateArmy(0, TestWorld.H(6, 5), "swordsman", "archer");
            var d = g.CreateArmy(1, TestWorld.H(7, 5), "warrior", "spearman");
            var battle = g.Attack(a, d.Position);
            battle.ConfirmDeployment();
            battle.ConfirmDeployment();
            var sword = battle.DeployedUnits(BattleSideId.Attacker).First();
            battle.TryMove(sword, battle.ReachableHexes(sword).Keys.OrderBy(h => h.DistanceTo(d.Position)).First());

            var (loaded, _) = SaveGame.Load(SaveGame.Save(g, null));
            var lb = loaded.Battles.Single();
            Assert.Equal(battle.Status, lb.Status);
            Assert.Equal(battle.ActiveSide, lb.ActiveSide);
            Assert.Equal(battle.Round, lb.Round);
            Assert.Equal(battle.Log, lb.Log);
            foreach (var u in battle.AllDeployedUnits)
            {
                var twin = lb.AllDeployedUnits.Single(x => x.Id == u.Id);
                Assert.Equal(battle.PositionOf(u), lb.PositionOf(twin));
                Assert.Equal(u.Hp, twin.Hp);
                Assert.Equal(u.BattleMovesLeft, twin.BattleMovesLeft);
            }
            // The loaded battle's units are the same objects as the loaded armies' units.
            Assert.All(lb.AllDeployedUnits, u => Assert.Contains(loaded.Armies, ar => ar.Units.Contains(u)));
        }

        [Fact]
        public void Rejects_foreign_or_future_files()
        {
            Assert.Throws<InvalidDataException>(() => SaveGame.Load(System.Text.Encoding.UTF8.GetBytes("not a save at all, sorry")));
        }
    }
}
