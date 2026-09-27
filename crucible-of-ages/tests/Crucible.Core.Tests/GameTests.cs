using System.Linq;
using Crucible.Core.Combat;
using Crucible.Core.Content;
using Crucible.Core.Empire;
using Crucible.Core.Game;
using Xunit;

namespace Crucible.Core.Tests
{
    public class GameTests
    {
        [Fact]
        public void Content_validates_and_army_cap_grows_with_tech()
        {
            var p = new Player(0, "x", TestWorld.Content.Faction(DefaultContent.Khaganate), false, TestWorld.Content);
            Assert.Equal(4, p.ArmyCap);
            p.Tech.Grant("military_tactics");
            p.Tech.Grant("chivalry");
            Assert.Equal(6, p.ArmyCap);

            var aurel = new Player(1, "y", TestWorld.Content.Faction(DefaultContent.Aurel), false, TestWorld.Content);
            Assert.Equal(5, aurel.ArmyCap);
        }

        [Fact]
        public void Tech_research_respects_prerequisites_and_carries_overflow()
        {
            var tree = new TechTree(TestWorld.Content);
            Assert.False(tree.CanResearch("bronze_working"));
            tree.SetResearch("agriculture");
            Assert.Equal("agriculture", tree.AddScience(25).Id);
            Assert.Equal(5, tree.Progress);
            Assert.True(tree.CanResearch("mining"));
        }

        [Fact]
        public void Armies_respect_cap_and_move_at_slowest_speed()
        {
            var g = TestWorld.Game();
            var army = g.CreateArmy(0, TestWorld.H(3, 3), "warrior", "horseman");
            Assert.Equal(2, army.MaxWorldMovement());
            Assert.Throws<System.InvalidOperationException>(() =>
                g.CreateArmy(0, TestWorld.H(5, 5), "warrior", "warrior", "warrior", "warrior", "warrior"));
        }

        [Fact]
        public void Khaganate_mounted_bonus_applies_to_all_mounted_army()
        {
            var g = TestWorld.Game();
            var army = g.CreateArmy(0, TestWorld.H(3, 3), "horseman", "sky_rider");
            Assert.Equal(5, g.WorldMovementOf(army)); // min(4+1, 5+1)
        }

        [Fact]
        public void Moving_next_to_an_enemy_ends_movement()
        {
            var g = TestWorld.Game();
            var mover = g.CreateArmy(0, TestWorld.H(3, 5), "horseman");
            g.CreateArmy(1, TestWorld.H(6, 5), "warrior");
            Assert.True(g.MoveArmy(mover, TestWorld.H(4, 5)));
            Assert.True(mover.WorldMovesLeft > 0);
            Assert.True(g.MoveArmy(mover, TestWorld.H(5, 5)));
            Assert.Equal(0, mover.WorldMovesLeft);
        }

        [Fact]
        public void Merge_and_split_armies()
        {
            var g = TestWorld.Game();
            var a = g.CreateArmy(0, TestWorld.H(3, 3), "warrior", "warrior");
            var b = g.CreateArmy(0, TestWorld.H(4, 3), "archer", "archer", "archer");
            Assert.Equal(2, g.MergeArmies(b, a)); // cap 4 → only 2 fit
            Assert.Equal(4, a.Count);
            Assert.Equal(1, b.Count);

            var split = g.SplitArmy(a, a.Units.Take(1), TestWorld.H(2, 3));
            Assert.NotNull(split);
            Assert.Equal(3, a.Count);
            Assert.Equal(1, split.Count);
        }

        [Fact]
        public void Attack_pulls_in_nearby_armies_and_ai_resolves_the_round()
        {
            var g = TestWorld.Game();
            var attacker = g.CreateArmy(0, TestWorld.H(4, 5), "swordsman", "swordsman");
            var helper = g.CreateArmy(0, TestWorld.H(3, 7), "archer");
            var defender = g.CreateArmy(1, TestWorld.H(5, 5), "warrior");

            Battle started = null;
            g.BattleStarted += b => started = b;
            var battle = g.Attack(attacker, defender.Position);

            Assert.NotNull(battle);
            Assert.Same(battle, started);
            Assert.Contains(helper, battle.Attacker.Armies);
            Assert.NotEqual(BattleStatus.InProgress, battle.Status); // both sides are AI
        }

        [Fact]
        public void Siege_assault_raises_militia_and_capture_gives_domination()
        {
            var g = TestWorld.Game();
            g.FoundCity(0, TestWorld.H(2, 2), "Home", isCapital: true);
            var enemyCapital = g.FoundCity(1, TestWorld.H(9, 6), "Target", isCapital: true);
            var army = g.CreateArmy(0, TestWorld.H(8, 6), "knight", "knight", "swordsman", "swordsman");

            int militiaAtStart = 0;
            g.BattleStarted += b => militiaAtStart = b.Defender.Armies.SelectMany(a => a.Units)
                .Count(u => u.Def.Id == DefaultContent.MilitiaUnit);

            var battle = g.Attack(army, enemyCapital.Position);
            Assert.NotNull(battle);
            Assert.True(battle.Objective.HasValue);
            Assert.Equal(enemyCapital.MilitiaCount, militiaAtStart);

            // Keep playing rounds on the attacker's turns until the battle ends.
            var turns = new TurnManager(g);
            turns.Start();
            for (int i = 0; i < 10 && g.Battles.Any(); i++) turns.EndTurn();

            Assert.Empty(g.Battles);
            Assert.Equal(0, enemyCapital.OwnerId);
            Assert.NotNull(g.Victory);
            Assert.Equal(VictoryType.Domination, g.Victory.Type);
            Assert.True(g.Player(1).IsEliminated);
        }

        [Fact]
        public void Score_victory_at_turn_limit()
        {
            var g = TestWorld.Game();
            g.FoundCity(0, TestWorld.H(2, 2), "A", isCapital: true).Population = 5;
            g.FoundCity(1, TestWorld.H(9, 6), "B", isCapital: true);
            g.TurnLimit = 3;
            var turns = new TurnManager(g);
            turns.Start();
            for (int i = 0; i < 10 && !turns.IsGameOver; i++) turns.EndTurn();
            Assert.Equal(VictoryType.Score, g.Victory.Type);
            Assert.Equal(0, g.Victory.WinnerId);
        }

        [Fact]
        public void Skirmish_setup_builds_a_playable_game()
        {
            var g = GameSetup.NewSkirmish(2024);
            Assert.Equal(2, g.Players.Count);
            Assert.Equal(2, g.Cities.Count());
            Assert.Equal(2, g.Armies.Count());
            Assert.All(g.Armies, a => Assert.True(g.Map.Get(a.Position).IsPassableForLand));
        }
    }
}
