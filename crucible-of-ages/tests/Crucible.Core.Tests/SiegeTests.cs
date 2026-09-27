using System.Linq;
using Crucible.Core.Combat;
using Crucible.Core.Content;
using Crucible.Core.Economy;
using Crucible.Core.Empire;
using Crucible.Core.Game;
using Crucible.Core.Hex;
using Crucible.Core.Units;
using Xunit;

namespace Crucible.Core.Tests
{
    /// <summary>M4: walls, siege engines, militia and the siege lifecycle (GDD §4.6).</summary>
    public class SiegeTests
    {
        static readonly HexCoord A = TestWorld.H(4, 5);
        static readonly HexCoord D = TestWorld.H(6, 5);

        static Battle WalledAssault(params string[] attackers)
        {
            var map = TestWorld.FlatMap();
            map.Get(D).WallTier = 1;
            return TestWorld.Battle(map, A, attackers, D, new[] { "warrior" }, objective: D);
        }

        static HexCoord MoveNextToCentre(Battle b, Unit unit, System.Func<HexCoord, bool> extra = null)
        {
            var reach = b.ReachableHexes(unit);
            var hex = reach.Keys.Where(h => h.DistanceTo(D) == 1 && (extra == null || extra(h)))
                .OrderByDescending(h => reach[h]).ThenBy(h => h.Q).ThenBy(h => h.R).First();
            Assert.True(b.TryMove(unit, hex));
            return hex;
        }

        [Fact]
        public void Intact_walls_stop_melee_but_not_archers()
        {
            var b = WalledAssault("swordsman", "archer");
            Assert.True(b.HasWalls);
            Assert.Equal(Battle.WallHpPerTier, b.WallHp);

            var sword = b.DeployedUnits(BattleSideId.Attacker).Single(u => u.Def.Id == "swordsman");
            var archer = b.DeployedUnits(BattleSideId.Attacker).Single(u => u.Def.Id == "archer");
            var from = MoveNextToCentre(b, sword);

            Assert.True(b.BlockedByWalls(sword, from, D));
            Assert.False(b.CanAttackFrom(sword, from, D));
            Assert.DoesNotContain(D, b.ReachableHexes(sword).Keys);
            Assert.True(b.CanAttackFrom(archer, b.PositionOf(archer).Value, D) ||
                        b.PositionOf(archer).Value.DistanceTo(D) > archer.Def.Range);
        }

        [Fact]
        public void Battering_the_walls_breaches_them_and_removes_the_wall_bonus()
        {
            var b = WalledAssault("swordsman");
            var sword = b.DeployedUnits(BattleSideId.Attacker).Single();
            var defender = b.DeployedUnits(BattleSideId.Defender).Single();
            var from = MoveNextToCentre(b, sword);

            Assert.Contains(b.PreviewAttack(sword, from, defender).Defender.Modifiers, m => m.Label == "Walls");
            int dmg = b.TryAttackWalls(sword);
            Assert.True(dmg > 0);
            Assert.Equal(Battle.WallHpPerTier - dmg, b.WallHp);
            Assert.Equal(-1, b.TryAttackWalls(sword)); // one attack per turn

            // Knock the rest down over the next turns.
            for (int i = 0; i < 20 && b.WallsIntact && b.Status == BattleStatus.InProgress; i++)
            {
                b.EndTurn(); b.EndTurn();
                if (b.Status == BattleStatus.AwaitingNextRound) b.BeginNextRound();
                if (b.Status != BattleStatus.InProgress) break;
                if (b.PositionOf(sword)?.DistanceTo(D) != 1) MoveNextToCentre(b, sword);
                b.TryAttackWalls(sword);
            }
            Assert.False(b.WallsIntact);
            var pos = b.PositionOf(sword).Value;
            Assert.DoesNotContain(b.PreviewAttack(sword, pos, defender).Defender.Modifiers, m => m.Label == "Walls");
        }

        [Fact]
        public void Siege_towers_let_adjacent_infantry_over_the_walls()
        {
            var b = WalledAssault("swordsman", "siege_tower");
            var sword = b.DeployedUnits(BattleSideId.Attacker).Single(u => u.Def.Id == "swordsman");
            var tower = b.DeployedUnits(BattleSideId.Attacker).Single(u => u.Def.Id == "siege_tower");

            Assert.False(b.CanAttackFrom(tower, b.PositionOf(tower).Value, D)); // towers never attack
            var towerHex = MoveNextToCentre(b, tower);
            var swordHex = MoveNextToCentre(b, sword, h => h.DistanceTo(towerHex) == 1);

            Assert.False(b.BlockedByWalls(sword, swordHex, D));
            Assert.True(b.CanAttackFrom(sword, swordHex, D));
        }

        [Fact]
        public void Rams_hit_walls_hardest_but_cannot_attack_units()
        {
            var b = WalledAssault("battering_ram", "swordsman");
            var ram = b.DeployedUnits(BattleSideId.Attacker).Single(u => u.Def.Id == "battering_ram");
            var sword = b.DeployedUnits(BattleSideId.Attacker).Single(u => u.Def.Id == "swordsman");
            Assert.True(b.ExpectedWallDamage(ram) > 1.5 * b.ExpectedWallDamage(sword));

            var from = MoveNextToCentre(b, ram);
            Assert.False(b.CanAttackFrom(ram, from, D));
            Assert.True(b.CanAttackWallsFrom(ram, from));
        }

        static (GameState g, City city, Army besiegers) Besieged()
        {
            var g = TestWorld.Game(TestWorld.FlatMap(20, 14));
            g.FoundCity(0, TestWorld.H(2, 2), "Home", true);
            var city = g.FoundCity(1, TestWorld.H(10, 6), "Target", true);
            city.Population = 5;
            var army = g.CreateArmy(0, TestWorld.H(9, 6), "swordsman", "swordsman");
            Assert.True(g.DeclareSiege(army, city));
            return (g, city, army);
        }

        [Fact]
        public void Declaring_a_siege_raises_militia()
        {
            var (g, city, _) = Besieged();
            Assert.True(city.IsBesieged);
            Assert.Equal(0, city.BesiegerId);
            Assert.Equal(city.MilitiaCount, g.ArmyAt(city.Position).Units.Count(u => u.BoundToCityId == city.Id));
        }

        [Fact]
        public void Siege_engines_cost_siege_progress_and_are_capped()
        {
            var (g, city, army) = Besieged();
            Assert.Null(g.BuildSiegeEngine(city, army, "battering_ram")); // no progress yet
            Assert.False(EconomyRules.CanBuild(g, g.City(1), ProductionItem.Unit("battering_ram")));

            city.SiegeProgress = 200;
            Assert.NotNull(g.BuildSiegeEngine(city, army, "battering_ram"));
            Assert.Equal(170, city.SiegeProgress);
            Assert.Null(g.BuildSiegeEngine(city, army, "catapult")); // needs Mathematics
            Assert.NotNull(g.BuildSiegeEngine(city, army, "siege_tower"));
            Assert.Null(g.BuildSiegeEngine(city, army, "battering_ram")); // army cap 4 reached

            var second = g.CreateArmy(0, TestWorld.H(11, 6), "warrior");
            Assert.NotNull(g.BuildSiegeEngine(city, second, "battering_ram"));
            Assert.Null(g.BuildSiegeEngine(city, second, "battering_ram")); // 3 per siege
            Assert.Empty(g.AvailableSiegeEngines(city));
        }

        [Fact]
        public void Besiegers_accumulate_progress_and_the_siege_lifts_when_they_leave()
        {
            var (g, city, army) = Besieged();
            var turns = new TurnManager(g);
            turns.Start();
            turns.EndTurn(); // P0 → P1
            turns.EndTurn(); // P1 → P0: besieger's turn begins
            Assert.True(city.SiegeProgress > 0);

            Assert.True(g.MoveArmy(army, army.Position + HexCoord.Direction(3)));
            turns.EndTurn();
            turns.EndTurn();
            Assert.False(city.IsBesieged);
            Assert.Equal(-1, city.BesiegerId);
            Assert.DoesNotContain(g.Armies.SelectMany(a => a.Units), u => u.BoundToCityId == city.Id);
        }

        [Fact]
        public void Militia_stay_home_when_the_garrison_sorties()
        {
            var (g, city, besiegers) = Besieged();
            var garrison = g.ArmyAt(city.Position);
            Assert.True(garrison.TryAdd(g.CreateUnit("spearman", 1), 99));
            garrison.WorldMovesLeft = 2;

            var battle = g.Attack(garrison, besiegers.Position);
            Assert.NotNull(battle);
            Assert.False(battle.Objective.HasValue);
            var militia = garrison.Units.Where(u => u.BoundToCityId == city.Id).ToList();
            Assert.NotEmpty(militia);
            Assert.All(militia, m => Assert.Throws<System.InvalidOperationException>(() => battle.SideOf(m)));
        }

        [Fact]
        public void Militia_are_left_behind_when_the_garrison_marches_out()
        {
            var (g, city, _) = Besieged();
            var garrison = g.ArmyAt(city.Position);
            garrison.TryAdd(g.CreateUnit("spearman", 1), 99);
            var exit = city.Position.Neighbors().First(n => g.ArmyAt(n) == null && !g.InEnemyZoc(n, 1) && g.Map.Get(n) != null);
            garrison.WorldMovesLeft = 2;
            Assert.True(g.MoveArmy(garrison, exit));

            Assert.All(garrison.Units, u => Assert.Equal(-1, u.BoundToCityId));
            Assert.All(g.ArmyAt(city.Position).Units, u => Assert.Equal(city.Id, u.BoundToCityId));
        }

        [Fact]
        public void Ai_assault_with_ram_and_tower_takes_a_walled_city()
        {
            var g = TestWorld.Game(TestWorld.FlatMap(20, 14));
            g.FoundCity(0, TestWorld.H(2, 2), "Home", true);
            var city = g.FoundCity(1, TestWorld.H(10, 6), "Target", true);
            city.Buildings.Add("walls");
            g.Map.Get(city.Position).WallTier = 2;
            var army = g.CreateArmy(0, TestWorld.H(9, 6), "swordsman", "swordsman", "battering_ram", "siege_tower");

            var battle = g.Attack(army, city.Position);
            Assert.NotNull(battle);
            Assert.True(battle.HasWalls);

            var turns = new TurnManager(g);
            turns.Start();
            for (int i = 0; i < 10 && g.Battles.Any(); i++) turns.EndTurn();

            Assert.Empty(g.Battles);
            Assert.Equal(0, city.OwnerId);
            Assert.Contains(battle.Log, l => l.Contains("batters the walls") || l.Contains("holds the city centre"));
        }
    }
}
