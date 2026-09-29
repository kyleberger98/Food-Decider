using System.Linq;
using Crucible.Core.Combat;
using Crucible.Core.Economy;
using Crucible.Core.Empire;
using Crucible.Core.Game;
using Crucible.Core.Units;
using Xunit;

namespace Crucible.Core.Tests
{
    /// <summary>Production queues, research targets, healing, sleep orders and battle forecasts.</summary>
    public class QualityOfLifeTests
    {
        [Fact]
        public void Queued_items_are_built_one_after_another_without_a_wasted_turn()
        {
            var g = TestWorld.Game(attackerAI: false);
            var city = g.FoundCity(0, TestWorld.H(4, 4), "Rome", true);
            Assert.True(g.SetProduction(city, ProductionItem.Building("monument")));
            Assert.True(g.EnqueueProduction(city, ProductionItem.Unit("warrior")));
            Assert.True(g.EnqueueProduction(city, ProductionItem.Unit("warrior")));   // units may repeat
            Assert.False(g.EnqueueProduction(city, ProductionItem.Building("monument"))); // buildings may not
            Assert.Equal(2, city.Queue.Count);

            city.ProductionStored = 1000;
            var turns = new TurnManager(g);
            turns.Start();
            turns.EndTurn();
            Assert.True(city.Has("monument"));
            Assert.Equal(ProductionItem.Unit("warrior"), city.CurrentProduction);
            Assert.Single(city.Queue);

            g.RemoveQueued(city, 0);
            Assert.Empty(city.Queue);
        }

        [Fact]
        public void A_research_target_walks_its_prerequisites_and_switching_keeps_progress()
        {
            var g = TestWorld.Game(attackerAI: false);
            var tech = g.Player(0).Tech;
            tech.Grant("stone_tools");                 // the Neolithic behind us
            tech.Grant("hunting");
            tech.SetResearch("agriculture");
            tech.AddScience(10);
            tech.SetResearch("agriculture"); // no-op
            Assert.Equal(10, tech.Progress);

            tech.SetTarget("horseback_riding");
            Assert.Equal("agriculture", tech.CurrentResearch); // already on the path
            Assert.Equal(new[] { "agriculture", "animal_husbandry", "horseback_riding" }, tech.PathTo("horseback_riding"));

            tech.AddScience(10);                       // agriculture done
            EconomyProcessor.AutoPickResearch(g.Player(0));
            Assert.Equal("animal_husbandry", tech.CurrentResearch);

            tech.AddScience(12);
            tech.SetResearch("archery");               // switch away…
            Assert.Equal(0, tech.Progress);
            tech.SetResearch("animal_husbandry");      // …and back: nothing lost
            Assert.Equal(12, tech.Progress);

            tech.AddScience(100);
            EconomyProcessor.AutoPickResearch(g.Player(0));
            Assert.Equal("horseback_riding", tech.CurrentResearch);
            tech.AddScience(1000);
            Assert.Null(tech.NextTowardTarget());
            Assert.Null(tech.Target);
        }

        [Fact]
        public void Resting_armies_heal_by_where_they_stand_and_moving_armies_do_not()
        {
            var g = TestWorld.Game(TestWorld.FlatMap(20, 12), attackerAI: false);
            var city = g.FoundCity(0, TestWorld.H(4, 4), "Rome", true);
            var inCity = g.CreateArmy(0, TestWorld.H(4, 4), "warrior");
            var inLand = g.CreateArmy(0, TestWorld.H(5, 4), "warrior");
            var outside = g.CreateArmy(0, TestWorld.H(14, 8), "warrior");
            var mover = g.CreateArmy(0, TestWorld.H(10, 2), "warrior");
            foreach (var a in new[] { inCity, inLand, outside, mover }) { a.Units[0].Hp = 40; a.WorldMovesLeft = g.WorldMovementOf(a); }
            mover.WorldMovesLeft = 0; // spent its moves

            var turns = new TurnManager(g);
            turns.Start();
            Assert.Equal(40 + GameState.HealInCity, inCity.Units[0].Hp);
            Assert.Equal(40 + GameState.HealInOwnTerritory, inLand.Units[0].Hp);
            Assert.Equal(40 + GameState.HealNeutral, outside.Units[0].Hp);
            Assert.Equal(40, mover.Units[0].Hp);
        }

        [Fact]
        public void Healing_and_sentry_armies_wake_when_healed_or_threatened()
        {
            var g = TestWorld.Game(TestWorld.FlatMap(20, 12), attackerAI: false);
            g.FoundCity(0, TestWorld.H(4, 4), "Rome", true);
            var healer = g.CreateArmy(0, TestWorld.H(4, 4), "warrior");
            var sentry = g.CreateArmy(0, TestWorld.H(12, 6), "warrior");
            Assert.False(g.SetStance(healer, ArmyStance.Heal)); // already whole
            healer.Units[0].Hp = 80;
            Assert.True(g.SetStance(healer, ArmyStance.Heal));
            Assert.True(g.SetStance(sentry, ArmyStance.Sentry));
            int woke = 0, threats = 0;
            g.ArmyWoke += (a, threat) => { woke++; if (threat) threats++; };

            var turns = new TurnManager(g);
            turns.Start();
            Assert.Equal(ArmyStance.Awake, healer.Stance);   // healed to full in the city
            Assert.Equal(ArmyStance.Sentry, sentry.Stance);   // nothing around yet

            g.CreateArmy(1, TestWorld.H(14, 6), "swordsman"); // at war (test default)
            turns.EndTurn();
            Assert.Equal(ArmyStance.Awake, sentry.Stance);
            Assert.Equal(2, woke);
            Assert.Equal(1, threats);

            // Moving also wakes an army.
            g.SetStance(sentry, ArmyStance.Sentry);
            sentry.WorldMovesLeft = 2;
            Assert.True(g.MoveArmy(sentry, TestWorld.H(11, 6)));
            Assert.Equal(ArmyStance.Awake, sentry.Stance);
        }

        [Fact]
        public void Forecast_favours_the_bigger_army_and_respects_walls_and_high_ground()
        {
            var g = TestWorld.Game(TestWorld.FlatMap(20, 12), attackerAI: false);
            var big = g.CreateArmy(0, TestWorld.H(5, 5), "swordsman", "swordsman", "swordsman");
            var small = g.CreateArmy(1, TestWorld.H(6, 5), "warrior");
            var f = BattleForecast.Estimate(g, big, small.Position);
            Assert.True(f.Verdict >= ForecastVerdict.LikelyVictory, f.Label);
            var back = BattleForecast.Estimate(g, small, big.Position);
            Assert.True(back.Verdict <= ForecastVerdict.LikelyDefeat, back.Label);
            Assert.Null(BattleForecast.Estimate(g, big, TestWorld.H(10, 10))); // nothing there

            // Same fight against a hill: worse odds.
            g.Map.Get(small.Position).Elevation = 2;
            var uphill = BattleForecast.Estimate(g, big, small.Position);
            Assert.True(uphill.Ratio < f.Ratio);
            Assert.True(uphill.DefenderHoldsHighGround);

            // A walled city with militia is tougher than the lone army.
            var city = g.FoundCity(1, TestWorld.H(12, 6), "Veii", true);
            g.Map.Get(city.Position).WallTier = 2;
            var attackers = g.CreateArmy(0, TestWorld.H(11, 6), "swordsman", "swordsman", "swordsman");
            var siege = BattleForecast.Estimate(g, attackers, city.Position);
            Assert.True(siege.Militia > 0);
            Assert.True(siege.Ratio < f.Ratio);
        }

        [Fact]
        public void Queues_research_targets_and_orders_survive_save_and_load()
        {
            var g = TestWorld.Game(attackerAI: false);
            var city = g.FoundCity(0, TestWorld.H(4, 4), "Rome", true);
            g.FoundCity(1, TestWorld.H(13, 9), "Veii", true);
            g.SetProduction(city, ProductionItem.Building("monument"));
            g.EnqueueProduction(city, ProductionItem.Unit("warrior"));
            foreach (var id in new[] { "stone_tools", "hunting", "agriculture" }) g.Player(0).Tech.Grant(id);
            g.Player(0).Tech.SetTarget("horseback_riding");
            g.Player(0).Tech.AddScience(5);
            g.Player(0).Tech.SetResearch("archery");
            var army = g.CreateArmy(0, TestWorld.H(6, 6), "warrior");
            g.SetStance(army, ArmyStance.Sentry);

            var turns = new TurnManager(g);
            var (loaded, _) = SaveGame.Load(SaveGame.Save(g, turns));
            var lc = loaded.Cities.First(c => c.Name == "Rome");
            Assert.Equal(new[] { ProductionItem.Unit("warrior") }, lc.Queue);
            Assert.Equal("horseback_riding", loaded.Player(0).Tech.Target);
            Assert.Equal(5, loaded.Player(0).Tech.ProgressOn("animal_husbandry"));
            Assert.Equal(ArmyStance.Sentry, loaded.Army(army.Id).Stance);
        }
    }
}
