using System.Linq;
using Crucible.Core.AI;
using Crucible.Core.Combat;
using Crucible.Core.Content;
using Crucible.Core.Hex;
using Xunit;

namespace Crucible.Core.Tests
{
    public class BattleTests
    {
        static readonly HexCoord A = TestWorld.H(4, 5);
        static readonly HexCoord D = TestWorld.H(6, 5);

        [Fact]
        public void Battlefield_contains_both_armies_and_zones_do_not_overlap()
        {
            var b = TestWorld.Battle(TestWorld.FlatMap(), A, new[] { "warrior" }, D, new[] { "warrior" });
            Assert.True(b.Contains(A));
            Assert.True(b.Contains(D));
            Assert.Empty(b.Attacker.DeploymentZone.Intersect(b.Defender.DeploymentZone));
            Assert.All(b.Attacker.DeploymentZone, h => Assert.True(h.DistanceTo(A) < h.DistanceTo(D)));
        }

        [Fact]
        public void Units_beyond_the_frontline_cap_wait_in_reserve_then_deploy()
        {
            var units = Enumerable.Repeat("warrior", 6).ToArray(); // cap is 4
            var b = TestWorld.Battle(TestWorld.FlatMap(), A, units, D, new[] { "warrior" });
            Assert.Equal(4, b.DeployedUnits(BattleSideId.Attacker).Count());
            Assert.Equal(2, b.Attacker.Reserve.Count);

            // Kill one deployed attacker; a reserve unit takes its place next attacker turn.
            var victim = b.DeployedUnits(BattleSideId.Attacker).First();
            victim.Hp = 0;
            b.EndTurn(); // attacker → defender
            b.EndTurn(); // defender → attacker (turn 2)
            Assert.Equal(1, b.Attacker.Reserve.Count(u => u.IsAlive));
        }

        [Fact]
        public void Ranged_units_deploy_behind_melee()
        {
            var b = TestWorld.Battle(TestWorld.FlatMap(), A, new[] { "archer", "warrior" }, D, new[] { "warrior" });
            var archer = b.DeployedUnits(BattleSideId.Attacker).Single(u => u.Def.Id == "archer");
            var warrior = b.DeployedUnits(BattleSideId.Attacker).Single(u => u.Def.Id == "warrior");
            Assert.True(b.PositionOf(archer).Value.DistanceTo(D) >= b.PositionOf(warrior).Value.DistanceTo(D));
        }

        [Fact]
        public void A_round_is_three_alternating_turns_and_the_battle_lasts_three_rounds()
        {
            var b = TestWorld.Battle(TestWorld.FlatMap(), A, new[] { "warrior" }, D, new[] { "warrior" });
            for (int round = 1; round <= Battle.MaxRounds; round++)
            {
                Assert.Equal(round, b.Round);
                for (int turn = 1; turn <= Battle.TurnsPerRound; turn++)
                {
                    Assert.Equal(BattleSideId.Attacker, b.ActiveSide);
                    Assert.Equal(turn, b.TurnInRound);
                    b.EndTurn();
                    Assert.Equal(BattleSideId.Defender, b.ActiveSide);
                    b.EndTurn();
                }
                if (round < Battle.MaxRounds)
                {
                    Assert.Equal(BattleStatus.AwaitingNextRound, b.Status);
                    b.BeginNextRound();
                }
            }
            Assert.Equal(BattleStatus.DefenderWon, b.Status); // attacker failed to win in 3 rounds
            Assert.Equal(BattleSideId.Defender, b.Winner);
        }

        [Fact]
        public void Idle_units_fortify()
        {
            var b = TestWorld.Battle(TestWorld.FlatMap(), A, new[] { "warrior" }, D, new[] { "warrior" });
            var defender = b.DeployedUnits(BattleSideId.Defender).Single();
            Assert.False(defender.Fortified);
            b.EndTurn();
            b.EndTurn(); // defender idled
            Assert.True(defender.Fortified);
        }

        [Fact]
        public void Only_the_active_side_can_act()
        {
            var b = TestWorld.Battle(TestWorld.FlatMap(), A, new[] { "warrior" }, D, new[] { "warrior" });
            var defender = b.DeployedUnits(BattleSideId.Defender).Single();
            var anyHex = b.ReachableHexes(defender).Keys.FirstOrDefault();
            Assert.False(b.TryMove(defender, anyHex));
        }

        [Fact]
        public void Entering_enemy_zone_of_control_stops_movement_but_allows_attack()
        {
            var map = TestWorld.FlatMap();
            var b = TestWorld.Battle(map, TestWorld.H(3, 5), new[] { "horseman" }, TestWorld.H(7, 5), new[] { "warrior" });
            var horse = b.DeployedUnits(BattleSideId.Attacker).Single();
            var enemy = b.DeployedUnits(BattleSideId.Defender).Single();
            var enemyPos = b.PositionOf(enemy).Value;

            var reach = b.ReachableHexes(horse);
            var zocHex = reach.Keys.Where(h => h.DistanceTo(enemyPos) == 1).OrderByDescending(h => reach[h]).First();
            Assert.True(b.TryMove(horse, zocHex));
            Assert.True(b.IsMoveLocked(horse));
            Assert.Empty(b.ReachableHexes(horse));
            Assert.True(horse.BattleMovesLeft > 0);
            Assert.NotNull(b.TryAttack(horse, enemyPos));
        }

        [Fact]
        public void Ranged_units_need_line_of_sight_unless_indirect()
        {
            var map = TestWorld.FlatMap();
            var aPos = TestWorld.H(4, 5);
            var dPos = TestWorld.H(6, 5);
            map.Get(TestWorld.H(5, 5)).Elevation = 2; // ridge between them
            var b = TestWorld.Battle(map, aPos, new[] { "archer", "catapult" }, dPos, new[] { "warrior" });
            var archer = b.DeployedUnits(BattleSideId.Attacker).Single(u => u.Def.Id == "archer");
            var catapult = b.DeployedUnits(BattleSideId.Attacker).Single(u => u.Def.Id == "catapult");
            var target = b.PositionOf(b.DeployedUnits(BattleSideId.Defender).Single()).Value;

            Assert.False(b.CanAttackFrom(archer, aPos, target));
            Assert.True(b.CanAttackFrom(catapult, aPos, target));
        }

        [Fact]
        public void High_ground_extends_ranged_reach()
        {
            var map = TestWorld.FlatMap();
            var from = TestWorld.H(3, 5);
            var target = TestWorld.H(6, 5); // distance 3, archer range 2
            var b = TestWorld.Battle(map, from, new[] { "archer" }, target, new[] { "warrior" });
            var archer = b.DeployedUnits(BattleSideId.Attacker).Single();
            var enemyPos = b.PositionOf(b.DeployedUnits(BattleSideId.Defender).Single()).Value;
            var shootFrom = map.Tiles.Select(t => t.Coord).First(h => h.DistanceTo(enemyPos) == 3 && b.Contains(h));

            Assert.False(b.CanAttackFrom(archer, shootFrom, enemyPos));
            map.Get(shootFrom).Elevation = 2;
            Assert.True(b.CanAttackFrom(archer, shootFrom, enemyPos));
        }

        [Fact]
        public void Killing_the_last_defender_wins_and_melee_advances()
        {
            var b = TestWorld.Battle(TestWorld.FlatMap(), A, new[] { "swordsman" }, D, new[] { "warrior" });
            var sword = b.DeployedUnits(BattleSideId.Attacker).Single();
            var enemy = b.DeployedUnits(BattleSideId.Defender).Single();
            enemy.Hp = 1;
            var enemyPos = b.PositionOf(enemy).Value;

            var from = b.ReachableHexes(sword).Keys.First(h => h.DistanceTo(enemyPos) == 1);
            Assert.True(b.TryMove(sword, from));
            var result = b.TryAttack(sword, enemyPos);

            Assert.True(result.DefenderKilled);
            Assert.Equal(enemyPos, b.PositionOf(sword));
            Assert.Equal(BattleStatus.AttackerWon, b.Status);
        }

        [Fact]
        public void Retreat_ends_the_battle_for_the_retreating_side()
        {
            var b = TestWorld.Battle(TestWorld.FlatMap(), A, new[] { "warrior" }, D, new[] { "warrior" });
            b.Retreat();
            Assert.Equal(BattleStatus.AttackerRetreated, b.Status);
            Assert.Equal(BattleSideId.Defender, b.Winner);
        }

        [Fact]
        public void Siege_attacker_wins_by_holding_the_objective()
        {
            var map = TestWorld.FlatMap();
            var objective = TestWorld.H(8, 5);
            var b = TestWorld.Battle(map, A, new[] { "warrior" }, D, new[] { "warrior" }, objective: objective);
            var warrior = b.DeployedUnits(BattleSideId.Attacker).Single();

            // Walk toward the (unoccupied) objective over a few turns.
            for (int i = 0; i < 6 && b.Status == BattleStatus.InProgress; i++)
            {
                if (b.ActiveSide == BattleSideId.Attacker)
                {
                    var reach = b.ReachableHexes(warrior);
                    if (reach.Count > 0)
                        b.TryMove(warrior, reach.Keys.OrderBy(h => h.DistanceTo(objective)).First());
                }
                if (b.Status == BattleStatus.InProgress) b.EndTurn();
            }
            // The defender is idle in this test, so the attacker should reach the objective.
            Assert.True(b.Status == BattleStatus.AttackerWon || b.PositionOf(warrior) == objective);
        }

        [Fact]
        public void Auto_resolve_favors_the_much_stronger_army_and_is_deterministic()
        {
            (BattleStatus, BattleSideId?, string) Run(ulong seed)
            {
                var b = TestWorld.Battle(TestWorld.FlatMap(), A, new[] { "knight", "knight", "pikeman", "crossbowman" },
                    D, new[] { "warrior", "warrior" }, seed);
                new TacticalBattleAI().ResolveFully(b);
                Assert.True(b.IsFinished);
                // Unit ids differ between runs; strip them so only the fight itself is compared.
                return (b.Status, b.Winner, System.Text.RegularExpressions.Regex.Replace(string.Join("\n", b.Log), "#\\d+", "#"));
            }

            var first = Run(11);
            Assert.Equal(BattleSideId.Attacker, first.Item2);
            Assert.Equal(first, Run(11)); // same seed → identical battle log
        }

        [Fact]
        public void Aurel_heal_on_round_end()
        {
            var aurel = TestWorld.Player(0, DefaultContent.Aurel);
            var b = TestWorld.Battle(TestWorld.FlatMap(), A, new[] { "warrior" }, D, new[] { "warrior" }, attacker: aurel);
            var w = b.DeployedUnits(BattleSideId.Attacker).Single();
            w.Hp = 50;
            for (int i = 0; i < Battle.TurnsPerRound * 2; i++) b.EndTurn();
            Assert.Equal(BattleStatus.AwaitingNextRound, b.Status);
            Assert.Equal(60, w.Hp);
        }
    }
}
