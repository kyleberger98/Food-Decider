using System.Linq;
using Crucible.Core.Combat;
using Crucible.Core.Content;
using Crucible.Core.Economy;
using Crucible.Core.Empire;
using Crucible.Core.Game;
using Crucible.Core.Units;
using Xunit;

namespace Crucible.Core.Tests
{
    /// <summary>M8: great people, religion, city-states, World Congress, tourism, spaceship — and the victories they unlock.</summary>
    public class VictorySystemsTests
    {
        static (GameState g, City home, City rival) Setup()
        {
            var g = TestWorld.Game(TestWorld.FlatMap(30, 16), attackerAI: false);
            var home = g.FoundCity(0, TestWorld.H(4, 7), "Home", true);
            var rival = g.FoundCity(1, TestWorld.H(26, 8), "Rival", true);
            return (g, home, rival);
        }

        static void GrantAll(Player p, GameState g)
        {
            foreach (var t in g.Content.Techs) p.Tech.Grant(t.Id);
        }

        // ------------------------------------------------------------------ great people

        [Fact]
        public void Libraries_produce_a_great_scientist_who_discovers_science()
        {
            var (g, home, _) = Setup();
            var p = g.Player(0);
            home.Buildings.Add("library");
            Unit born = null;
            g.GreatPersonBorn += (_, u) => born = u;
            for (int i = 0; i < 60 && born == null; i++) EconomyProcessor.ProcessTurn(g, p);

            Assert.NotNull(born);
            Assert.Equal(GreatPersonType.Scientist, born.Def.GreatPerson);
            Assert.Equal(200, GameState.GreatPersonThreshold(p, GreatPersonType.Scientist)); // next one costs more

            var army = g.Armies.Single(a => a.Units.Contains(born));
            int researched = p.Tech.Researched.Count;
            var msg = g.UseGreatPerson(army, born);
            Assert.Contains("science", msg);
            Assert.True(p.Tech.Researched.Count > researched || p.Tech.Progress > 0);
            Assert.DoesNotContain(g.Armies, a => a.Units.Contains(born)); // consumed
        }

        [Fact]
        public void Merchants_engineers_and_artists_pay_off()
        {
            var (g, home, _) = Setup();
            var p = g.Player(0);
            Army With(string id) => g.CreateArmy(0, home.Position.Neighbor(0), id);

            var m = With("great_merchant");
            int gold = p.Gold;
            g.UseGreatPerson(m, m.Units[0]);
            Assert.Equal(gold + 350, p.Gold);

            var e = With("great_engineer");
            int stored = home.ProductionStored;
            g.UseGreatPerson(e, e.Units[0]);
            Assert.Equal(stored + 400, home.ProductionStored);

            var a = With("great_artist");
            g.UseGreatPerson(a, a.Units[0]);
            Assert.Equal(1, home.GreatWorks);
            Assert.True(g.TourismPerTurn(p) >= 3);
        }

        [Fact]
        public void Great_generals_come_from_battle_and_lead_without_taking_a_slot()
        {
            var (g, _, _) = Setup();
            var p = g.Player(0);
            var army = g.CreateArmy(0, TestWorld.H(8, 7), "warrior", "warrior", "warrior", "warrior"); // cap 4
            Assert.True(army.TryAdd(g.CreateUnit("great_general", 0), p.ArmyCap));
            Assert.False(army.TryAdd(g.CreateUnit("great_general", 0), p.ArmyCap)); // one per army
            Assert.False(army.TryAdd(g.CreateUnit("warrior", 0), p.ArmyCap));

            var enemy = g.CreateArmy(1, TestWorld.H(9, 7), "warrior");
            Battle battle = null;
            g.BattleStarted += b => battle = b;
            g.Attack(army, enemy.Position);
            Assert.True(battle.HasGeneral(BattleSideId.Attacker));
            var mine = battle.DeployedUnits(BattleSideId.Attacker).First();
            var theirs = battle.DeployedUnits(BattleSideId.Defender).FirstOrDefault();
            if (theirs != null)
                Assert.Contains(battle.PreviewAttack(mine, battle.PositionOf(mine).Value, theirs).Attacker.Modifiers,
                    m => m.Label == "Great General" && m.Value == Battle.GeneralBonus);
            battle.ConfirmDeployment(); // the human attacker accepts the auto-deployment
            battle.Retreat();
            g.ResolveBattle(battle);

            // Enough battle experience produces a new general in the capital.
            p.GeneralPoints = GameState.GreatPersonThreshold(p, GreatPersonType.General);
            EconomyProcessor.ProcessTurn(g, p);
            Assert.Equal(2, g.Armies.SelectMany(x => x.Units).Count(u => u.OwnerId == 0 && u.Def.GreatPerson == GreatPersonType.General));
            Assert.Equal(0, p.GeneralPoints);
        }

        // ------------------------------------------------------------------ religion

        [Fact]
        public void Faith_raises_a_prophet_who_founds_a_religion_that_spreads()
        {
            var (g, home, rival) = Setup();
            var p = g.Player(0);
            var second = g.FoundCity(0, TestWorld.H(10, 7), "Second", false);
            var neighbour = g.FoundCity(1, TestWorld.H(14, 7), "Border", false);

            p.Faith = GameState.ProphetThreshold(p);
            EconomyProcessor.ProcessTurn(g, p);
            var prophet = g.Armies.SelectMany(a => a.Units).Single(u => u.Def.GreatPerson == GreatPersonType.Prophet);
            var army = g.Armies.Single(a => a.Units.Contains(prophet));

            Religion founded = null;
            g.ReligionFounded += r => founded = r;
            Assert.Contains("founded", g.UseGreatPerson(army, prophet));
            Assert.NotNull(founded);
            Assert.Equal(founded.Id, p.FoundedReligionId);
            Assert.Equal(founded.Id, g.City(founded.HolyCityId).ReligionId);

            int happyBefore = EconomyRules.Happiness(g, p);
            var turns = new TurnManager(g);
            turns.Start();
            for (int i = 0; i < 20; i++) turns.EndTurn();
            Assert.Equal(founded.Id, second.ReligionId);   // 6 hexes from the holy city
            Assert.Equal(founded.Id, neighbour.ReligionId); // spreads across borders too
            Assert.Equal(-1, rival.ReligionId);             // too far
            Assert.True(EconomyRules.Happiness(g, p) > happyBefore - 5);
            Assert.True(g.FollowerCities(p) >= 3);
        }

        // ------------------------------------------------------------------ city-states

        [Fact]
        public void City_states_are_neutral_and_reward_friends_and_allies()
        {
            var (g, home, _) = Setup();
            var p = g.Player(0);
            var cs = g.AddCityState("Kessra", CityStateType.Maritime, TestWorld.H(12, 12));
            Assert.False(g.AtWar(p.Id, cs.Id));
            var army = g.CreateArmy(0, TestWorld.H(11, 12), "warrior");
            Assert.Null(g.Attack(army, TestWorld.H(12, 12)));
            Assert.False(g.MoveArmy(army, TestWorld.H(12, 12)));

            var food = EconomyRules.CityYields(g, home).Food;
            p.Gold = 1000;
            Assert.True(g.GiftGold(p, cs, 150));
            Assert.Equal(CityStateStatus.Friend, g.StatusWith(cs, p.Id));
            Assert.Equal(food + 1, EconomyRules.CityYields(g, home).Food);
            Assert.True(g.GiftGold(p, cs, 150));
            Assert.Equal(CityStateStatus.Ally, g.StatusWith(cs, p.Id));
            Assert.Equal(food + 3, EconomyRules.CityYields(g, home).Food);

            var merc = g.AddCityState("Vallum", CityStateType.Mercantile, TestWorld.H(18, 3));
            int happy = EconomyRules.Happiness(g, p);
            g.GiftGold(p, merc, 300);
            Assert.Equal(happy + 4, EconomyRules.Happiness(g, p));

            var turns = new TurnManager(g);
            turns.Start();
            turns.EndTurn();
            Assert.True(g.InfluenceOf(cs, p.Id) < 60); // influence fades
        }

        // ------------------------------------------------------------------ victories

        [Fact]
        public void World_Congress_vote_gives_a_diplomatic_victory_with_city_state_allies()
        {
            var (g, _, _) = Setup();
            var p = g.Player(0);
            var a = g.AddCityState("Kessra", CityStateType.Maritime, TestWorld.H(12, 12));
            g.AddCityState("Vallum", CityStateType.Mercantile, TestWorld.H(18, 3));
            p.Gold = 2000;
            g.GiftGold(p, a, 500);
            GrantAll(p, g);

            var turns = new TurnManager(g);
            turns.Start();
            turns.EndTurn();
            Assert.True(g.WorldCongressFounded);
            Assert.Equal(p.Id, g.WorldCongressHostId);
            for (int i = 0; i < GameState.WorldLeaderVoteInterval + 1 && !turns.IsGameOver; i++) turns.EndTurn();

            Assert.Equal(3, g.LastVote[p.Id]); // own + host + allied city-state
            Assert.True(g.LastVote[p.Id] >= g.VotesNeeded);
            Assert.Equal(VictoryType.Diplomatic, g.Victory?.Type);
            Assert.Equal(p.Id, g.Victory.WinnerId);
        }

        [Fact]
        public void Spaceship_needs_apollo_a_factory_and_six_parts_then_lands()
        {
            var (g, home, _) = Setup();
            var p = g.Player(0);
            Assert.False(EconomyRules.CanBuild(g, home, ProductionItem.Project("apollo_program")));
            GrantAll(p, g);
            Assert.True(EconomyRules.CanBuild(g, home, ProductionItem.Project("apollo_program")));
            Assert.False(EconomyRules.CanBuild(g, home, ProductionItem.Project("ss_booster"))); // Apollo first

            g.CompleteProject(home, "apollo_program");
            Assert.False(EconomyRules.CanBuild(g, home, ProductionItem.Project("apollo_program")));
            Assert.False(EconomyRules.CanBuild(g, home, ProductionItem.Project("ss_booster"))); // needs a factory
            home.Buildings.Add("factory");
            Assert.True(EconomyRules.CanBuild(g, home, ProductionItem.Project("ss_booster")));

            foreach (var part in new[] { "ss_booster", "ss_booster", "ss_booster", "ss_cockpit", "ss_stasis_chamber", "ss_engine" })
                g.CompleteProject(home, part);
            Assert.False(EconomyRules.CanBuild(g, home, ProductionItem.Project("ss_booster"))); // 3 max
            Assert.Equal(g.Turn + GameState.SpaceshipFlightTurns, p.SpaceshipArrivalTurn);

            var turns = new TurnManager(g);
            turns.Start();
            for (int i = 0; i < GameState.SpaceshipFlightTurns + 1 && !turns.IsGameOver; i++) turns.EndTurn();
            Assert.Equal(VictoryType.Science, g.Victory?.Type);
        }

        [Fact]
        public void Losing_the_capital_scraps_the_spaceship()
        {
            var (g, home, _) = Setup();
            var p = g.Player(0);
            GrantAll(p, g);
            home.Buildings.Add("factory");
            g.CompleteProject(home, "apollo_program");
            g.CompleteProject(home, "ss_booster");
            Assert.Equal(1, p.SpaceshipPartsBuilt);

            var raiders = g.CreateArmy(1, home.Position.Neighbor(0), "tank", "tank", "tank", "tank");
            g.Player(1).Tech.Grant("combined_arms");
            var battle = g.Attack(raiders, home.Position);
            new AI.TacticalBattleAI().ResolveFully(battle);
            g.ResolveBattle(battle);
            Assert.Equal(1, home.OwnerId);
            Assert.Equal(0, p.SpaceshipPartsBuilt);
            Assert.False(p.CompletedProjects.ContainsKey("ss_booster"));
        }

        [Fact]
        public void Tourism_overtaking_rival_culture_wins_a_culture_victory()
        {
            var (g, home, _) = Setup();
            var p = g.Player(0);
            p.Tech.Grant("radio"); // culture starts feeding tourism
            home.GreatWorks = 30;
            g.Player(1).LifetimeCulture = 200;

            var turns = new TurnManager(g);
            turns.Start();
            for (int i = 0; i < 20 && !turns.IsGameOver; i++) turns.EndTurn();
            Assert.Equal(VictoryType.Culture, g.Victory?.Type);
            Assert.Equal(p.Id, g.Victory.WinnerId);
        }
    }
}
