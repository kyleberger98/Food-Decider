using System.Linq;
using Crucible.Core.AI;
using Crucible.Core.Combat;
using Crucible.Core.Hex;
using Crucible.Core.Random;
using Crucible.Core.Units;
using Xunit;

namespace Crucible.Core.Tests
{
    /// <summary>M2: deployment phase, reinforcements, joining battles, auto-resolve.</summary>
    public class TacticalBattleTests
    {
        static readonly HexCoord A = TestWorld.H(4, 5);
        static readonly HexCoord D = TestWorld.H(6, 5);

        static Battle Deploying(string[] attacker, string[] defender, Crucible.Core.World.WorldMap map = null)
        {
            map ??= TestWorld.FlatMap();
            var aArmy = new Army(1, 0, A);
            foreach (var id in attacker) aArmy.TryAdd(TestWorld.Unit(id, 0), 99);
            var dArmy = new Army(2, 1, D);
            foreach (var id in defender) dArmy.TryAdd(TestWorld.Unit(id, 1), 99);
            var battle = new Battle(1, map, Battlefield.Generate(map, A, D, Battlefield.FieldRadius),
                new BattleSide(BattleSideId.Attacker, TestWorld.Player(0), A),
                new BattleSide(BattleSideId.Defender, TestWorld.Player(1), D),
                new DeterministicRng(3));
            battle.AddArmy(BattleSideId.Attacker, aArmy);
            battle.AddArmy(BattleSideId.Defender, dArmy);
            battle.Start(deploymentPhase: true);
            return battle;
        }

        [Fact]
        public void Deployment_phase_defender_then_attacker_then_round_one()
        {
            var b = Deploying(new[] { "warrior" }, new[] { "warrior" });
            Assert.Equal(BattleStatus.Deploying, b.Status);
            Assert.True(b.AwaitingAction);
            Assert.False(b.IsFinished);
            Assert.Equal(BattleSideId.Defender, b.ActiveSide);

            b.ConfirmDeployment();
            Assert.Equal(BattleSideId.Attacker, b.ActiveSide);
            Assert.Equal(BattleStatus.Deploying, b.Status);

            b.ConfirmDeployment();
            Assert.Equal(BattleStatus.InProgress, b.Status);
            Assert.Equal(BattleSideId.Attacker, b.ActiveSide);
            Assert.Equal(1, b.Round);
        }

        [Fact]
        public void Redeploy_only_inside_own_zone_and_swaps_with_friends()
        {
            var b = Deploying(new[] { "warrior", "archer" }, new[] { "warrior" });
            b.ConfirmDeployment(); // defender done → attacker arranges

            var warrior = b.DeployedUnits(BattleSideId.Attacker).Single(u => u.Def.Id == "warrior");
            var archer = b.DeployedUnits(BattleSideId.Attacker).Single(u => u.Def.Id == "archer");
            var wPos = b.PositionOf(warrior).Value;
            var aPos = b.PositionOf(archer).Value;

            Assert.True(b.Redeploy(warrior, aPos));
            Assert.Equal(aPos, b.PositionOf(warrior));
            Assert.Equal(wPos, b.PositionOf(archer));

            var outside = b.Tiles.First(h => !b.Attacker.DeploymentZone.Contains(h) && b.UnitAt(h) == null);
            Assert.False(b.Redeploy(warrior, outside));

            var enemy = b.DeployedUnits(BattleSideId.Defender).Single();
            Assert.False(b.Redeploy(enemy, wPos)); // not the defender's turn to deploy
        }

        [Fact]
        public void Commands_are_refused_during_deployment()
        {
            var b = Deploying(new[] { "warrior" }, new[] { "warrior" });
            var defender = b.DeployedUnits(BattleSideId.Defender).Single();
            var free = b.PositionOf(defender).Value.Neighbors().First(h => b.Contains(h) && b.UnitAt(h) == null);
            Assert.False(b.TryMove(defender, free));
            Assert.Throws<System.InvalidOperationException>(() => b.EndTurn());
        }

        [Fact]
        public void Ai_deploys_ranged_units_on_high_ground()
        {
            var map = TestWorld.FlatMap(elevation: 0);
            var b0 = Deploying(new[] { "warrior", "archer" }, new[] { "warrior" }, map);
            var zone = b0.Attacker.DeploymentZone;
            var hill = zone.Where(h => b0.UnitAt(h) == null).OrderBy(h => h.Q).First();
            map.Get(hill).Elevation = 1;

            var b = Deploying(new[] { "warrior", "archer" }, new[] { "warrior" }, map);
            var ai = new TacticalBattleAI();
            ai.PlayTurn(b); // defender deploys
            ai.PlayTurn(b); // attacker deploys
            var archer = b.DeployedUnits(BattleSideId.Attacker).Single(u => u.Def.Id == "archer");
            Assert.Equal(1, map.Get(b.PositionOf(archer).Value).Elevation);
            Assert.Equal(BattleStatus.InProgress, b.Status);
        }

        [Fact]
        public void Reinforcements_enter_from_their_own_edge()
        {
            var b = TestWorld.Battle(TestWorld.FlatMap(20, 14), A, new[] { "warrior" }, D, new[] { "warrior" });
            var entry = TestWorld.H(5, 9); // south of the field
            var reinforcement = new Army(3, 0, entry);
            reinforcement.TryAdd(TestWorld.Unit("spearman", 0), 99);
            b.AddArmy(BattleSideId.Attacker, reinforcement);

            b.EndTurn();
            b.EndTurn(); // attacker's next turn: reinforcements deploy
            var spear = reinforcement.Units.Single();
            var pos = b.PositionOf(spear);
            Assert.NotNull(pos);
            var nearestFieldHex = b.Tiles.Min(h => h.DistanceTo(entry));
            Assert.True(pos.Value.DistanceTo(entry) <= nearestFieldHex + 1);
            Assert.DoesNotContain(pos.Value, b.Attacker.DeploymentZone.Where(h => h.DistanceTo(entry) > nearestFieldHex + 1));
        }

        [Fact]
        public void Human_attack_waits_for_human_deployment_then_joining_and_auto_resolve_work()
        {
            var g = TestWorld.Game(attackerAI: false, defenderAI: true);
            var attacker = g.CreateArmy(0, TestWorld.H(4, 5), "swordsman", "swordsman");
            var defender = g.CreateArmy(1, TestWorld.H(5, 5), "warrior");
            var battle = g.Attack(attacker, defender.Position);

            // AI defender already deployed; the human attacker is now arranging.
            Assert.Equal(BattleStatus.Deploying, battle.Status);
            Assert.Equal(BattleSideId.Attacker, battle.ActiveSide);

            // A second human army marches into the battle from outside the field.
            var edge = battle.Tiles.SelectMany(t => t.Neighbors()).First(n => !battle.Contains(n) && g.ArmyAt(n) == null
                && g.Map.Get(n) != null && g.Map.Get(n).IsPassableForLand);
            var late = g.CreateArmy(0, edge, "archer");
            Assert.True(g.JoinBattle(late, battle));
            Assert.Contains(late, battle.Attacker.Armies);
            Assert.False(g.JoinBattle(late, battle)); // already in

            g.AutoResolveRound(battle);
            Assert.True(battle.IsFinished || battle.Status == BattleStatus.AwaitingNextRound);
        }

        [Fact]
        public void Attacking_an_army_already_in_battle_joins_that_battle()
        {
            var g = TestWorld.Game(attackerAI: false, defenderAI: false); // humans: battle pauses
            var a1 = g.CreateArmy(0, TestWorld.H(4, 5), "warrior");
            var d = g.CreateArmy(1, TestWorld.H(5, 5), "warrior");
            var battle = g.Attack(a1, d.Position);
            Assert.NotNull(battle);

            var outside = d.Position.Range(6).First(h => !battle.Contains(h) && h.Neighbors().Contains(d.Position) == false
                && h.Neighbors().Any(battle.Contains) && g.ArmyAt(h) == null && g.Map.Get(h) != null);
            var a2 = g.CreateArmy(0, outside, "spearman");
            // Not adjacent to the defender army itself, so a direct Attack is refused …
            Assert.Null(g.Attack(a2, d.Position));
            // … but joining the battle from its edge works.
            Assert.True(g.JoinBattle(a2, battle));
        }
    }
}
