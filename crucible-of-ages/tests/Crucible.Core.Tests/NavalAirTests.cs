using System.Linq;
using Crucible.Core.AI;
using Crucible.Core.Combat;
using Crucible.Core.Content;
using Crucible.Core.Economy;
using Crucible.Core.Empire;
using Crucible.Core.Game;
using Crucible.Core.Hex;
using Crucible.Core.Units;
using Crucible.Core.World;
using Xunit;

namespace Crucible.Core.Tests
{
    /// <summary>M7: full-history content, fleets, embarking, coastal battles, air power, buying with gold.</summary>
    public class NavalAirTests
    {
        /// <summary>Land in columns 0–9, coast in 10–12, ocean from 13.</summary>
        static WorldMap CoastMap()
        {
            var map = TestWorld.FlatMap(20, 12);
            foreach (var t in map.Tiles)
            {
                int col = t.Coord.ToOffset().col;
                if (col >= 13) { t.Terrain = TerrainType.Ocean; t.Elevation = -2; }
                else if (col >= 10) { t.Terrain = TerrainType.Coast; t.Elevation = -1; }
            }
            return map;
        }

        static GameState CoastGame() => TestWorld.Game(CoastMap(), attackerAI: false);

        [Fact]
        public void Every_era_has_land_units_and_the_navy_and_air_force_exist()
        {
            var c = TestWorld.Content;
            foreach (Era era in System.Enum.GetValues(typeof(Era)))
                Assert.Contains(c.Units, u => u.Era == era && u.IsMilitary && u.Domain == UnitDomain.Land && u.ProductionCost > 0);
            Assert.True(c.Units.Count(u => u.Domain == UnitDomain.Naval) >= 4);
            Assert.True(c.Units.Count(u => u.Domain == UnitDomain.Air) >= 3);
            Assert.True(c.Techs.Count() >= 50, $"{c.Techs.Count()} techs");
        }

        [Fact]
        public void Fleets_sail_water_and_triremes_hug_the_coast()
        {
            var g = CoastGame();
            var fleet = g.CreateArmy(0, TestWorld.H(10, 5), "trireme");
            Assert.True(fleet.IsNaval);
            var land = fleet.Position.Neighbors().First(n => !g.Map.Get(n).IsWater);
            Assert.False(g.MoveArmy(fleet, land));
            Assert.Null(Pathfinder.Find(g, fleet, TestWorld.H(15, 5))); // ocean: trireme can't
            Assert.NotNull(Pathfinder.Find(g, fleet, TestWorld.H(12, 1)));

            g.Player(0).Tech.Grant("navigation");
            var frigates = g.CreateArmy(0, TestWorld.H(12, 8), "frigate");
            Assert.NotNull(Pathfinder.Find(g, frigates, TestWorld.H(16, 8)));
            Assert.False(frigates.TryAdd(g.CreateUnit("warrior", 0), 9)); // fleets and troops don't mix
        }

        [Fact]
        public void Land_armies_embark_with_optics_and_cross_ocean_with_astronomy()
        {
            var g = CoastGame();
            var army = g.CreateArmy(0, TestWorld.H(9, 5), "warrior");
            var coast = army.Position + HexCoord.Direction(0);
            Assert.False(g.MoveArmy(army, coast));

            g.Player(0).Tech.Grant("optics");
            Assert.True(g.MoveArmy(army, coast));
            Assert.True(g.IsEmbarked(army));
            Assert.Null(Pathfinder.Find(g, army, TestWorld.H(15, 5)));
            g.Player(0).Tech.Grant("astronomy");
            Assert.NotNull(Pathfinder.Find(g, army, TestWorld.H(15, 5)));
        }

        [Fact]
        public void Warships_sink_embarked_armies_and_embarked_armies_cannot_attack()
        {
            var g = CoastGame();
            g.Player(0).Tech.Grant("optics");
            var troops = g.CreateArmy(0, TestWorld.H(9, 5), "swordsman", "swordsman");
            Assert.True(g.MoveArmy(troops, TestWorld.H(10, 5)));
            var ship = g.CreateArmy(1, TestWorld.H(11, 5), "trireme");
            troops.WorldMovesLeft = 2;
            Assert.Null(g.Attack(troops, ship.Position)); // can't fight from the boats

            Army sunk = null;
            g.ArmySunk += a => sunk = a;
            ship.WorldMovesLeft = 3;
            Assert.Null(g.Attack(ship, troops.Position));
            Assert.Same(troops, sunk);
            Assert.Null(g.Army(troops.Id));
        }

        [Fact]
        public void Coastal_cities_launch_ships_and_build_harbors()
        {
            var g = CoastGame();
            var port = g.FoundCity(0, TestWorld.H(9, 6), "Port", true);
            var inland = g.FoundCity(0, TestWorld.H(3, 6), "Inland", false);
            g.FoundCity(1, TestWorld.H(1, 1), "Enemy", true);
            g.Player(0).Tech.Grant("pottery");
            g.Player(0).Tech.Grant("sailing");

            Assert.True(EconomyRules.CanBuild(g, port, ProductionItem.Unit("trireme")));
            Assert.False(EconomyRules.CanBuild(g, inland, ProductionItem.Unit("trireme")));
            Assert.True(EconomyRules.CanBuild(g, port, ProductionItem.Building("harbor")));
            Assert.False(EconomyRules.CanBuild(g, inland, ProductionItem.Building("harbor")));

            var ship = g.SpawnUnit(port, "trireme");
            var fleet = g.Armies.Single(a => a.Units.Contains(ship));
            Assert.True(g.Map.Get(fleet.Position).IsWater);
            Assert.Equal(1, fleet.Position.DistanceTo(port.Position));
            var second = g.SpawnUnit(port, "trireme");
            Assert.Contains(second, fleet.Units); // joins the fleet in the harbour
        }

        [Fact]
        public void Coastal_battles_keep_ships_on_water_and_troops_on_land()
        {
            var map = CoastMap();
            var fleetPos = TestWorld.H(10, 5);
            var shorePos = TestWorld.H(9, 5);
            var b = TestWorld.Battle(map, fleetPos, new[] { "trireme", "frigate" }, shorePos, new[] { "warrior", "archer" });

            foreach (var u in b.AllDeployedUnits)
            {
                bool onWater = map.Get(b.PositionOf(u).Value).IsWater;
                Assert.Equal(u.Def.Domain == UnitDomain.Naval, onWater);
            }
            var trireme = b.DeployedUnits(BattleSideId.Attacker).Single(u => u.Def.Id == "trireme");
            Assert.All(b.ReachableHexes(trireme).Keys, h => Assert.True(map.Get(h).IsWater));
            var warrior = b.DeployedUnits(BattleSideId.Defender).Single(u => u.Def.Id == "warrior");
            var warriorPos = b.PositionOf(warrior).Value;
            // A trireme next to the warrior still can't strike it: melee doesn't cross domains.
            var nextToWarrior = warriorPos.Neighbors().FirstOrDefault(h => b.Contains(h) && map.Get(h).IsWater);
            if (b.Contains(nextToWarrior)) Assert.False(b.CanAttackFrom(trireme, nextToWarrior, warriorPos));
        }

        [Fact]
        public void Bombers_strike_at_round_start_and_fighters_intercept()
        {
            var map = TestWorld.FlatMap(24, 12);
            var attacker = TestWorld.Player(0);
            var defender = TestWorld.Player(1);
            var bomber = TestWorld.Unit("bomber", 0);
            var fighter = TestWorld.Unit("fighter", 1);

            var b = new Battle(1, map, Battlefield.Generate(map, TestWorld.H(4, 5), TestWorld.H(6, 5), 3),
                new BattleSide(BattleSideId.Attacker, attacker, TestWorld.H(4, 5)),
                new BattleSide(BattleSideId.Defender, defender, TestWorld.H(6, 5)),
                new Random.DeterministicRng(9));
            var a = new Army(1, 0, TestWorld.H(4, 5)); a.TryAdd(TestWorld.Unit("mech_infantry", 0), 9);
            var d = new Army(2, 1, TestWorld.H(6, 5)); d.TryAdd(TestWorld.Unit("mech_infantry", 1), 9); // survives a bombing
            b.AddArmy(BattleSideId.Attacker, a);
            b.AddArmy(BattleSideId.Defender, d);
            b.SetAirSupport(BattleSideId.Attacker, new[] { bomber });
            b.SetAirSupport(BattleSideId.Defender, new[] { fighter });
            b.Start();

            Assert.Contains(b.Log, l => l.Contains("intercepts"));
            Assert.True(bomber.Hp < Unit.MaxHp);
            if (bomber.IsAlive)
            {
                Assert.Contains(b.Log, l => l.Contains("strikes"));
                Assert.True(d.Units[0].Hp < Unit.MaxHp);
            }

            // Once per round: ending turns within the round doesn't strike again.
            int strikes = b.Log.Count(l => l.Contains("strikes"));
            b.EndTurn();
            b.EndTurn();
            Assert.Equal(strikes, b.Log.Count(l => l.Contains("strikes") && !l.Contains(fighter.Def.Name)));
        }

        [Fact]
        public void Aircraft_are_based_in_city_hangars_within_range()
        {
            var g = TestWorld.Game(TestWorld.FlatMap(30, 14), attackerAI: false);
            var city = g.FoundCity(0, TestWorld.H(3, 6), "Base", true);
            g.FoundCity(1, TestWorld.H(27, 12), "Far", true);
            for (int i = 0; i < City.AirCapacity; i++) Assert.NotNull(g.SpawnUnit(city, "bomber"));
            Assert.Null(g.SpawnUnit(city, "bomber"));
            Assert.DoesNotContain(g.Armies, a => a.Units.Any(u => u.Def.Domain == UnitDomain.Air));

            var near = g.CreateArmy(0, TestWorld.H(6, 6), "warrior");
            var enemy = g.CreateArmy(1, TestWorld.H(7, 6), "warrior");
            var battle = g.Attack(near, enemy.Position);
            Assert.Equal(City.AirCapacity, battle.AirSupport(BattleSideId.Attacker).Count);

            var farArmy = g.CreateArmy(0, TestWorld.H(22, 3), "warrior");
            var farEnemy = g.CreateArmy(1, TestWorld.H(23, 3), "warrior");
            var farBattle = g.Attack(farArmy, farEnemy.Position);
            Assert.Empty(farBattle.AirSupport(BattleSideId.Attacker)); // beyond the bombers' range
        }

        [Fact]
        public void Gold_buys_items_and_counts_progress_on_the_current_build()
        {
            var g = TestWorld.Game(TestWorld.FlatMap(20, 12), attackerAI: false);
            var city = g.FoundCity(0, TestWorld.H(5, 5), "Home", true);
            g.FoundCity(1, TestWorld.H(16, 9), "Far", true);
            var p = g.Player(0);
            var warrior = ProductionItem.Unit("warrior");

            int full = EconomyRules.PurchaseCost(g, city, warrior);
            Assert.Equal(100, full); // 2×40 + 20
            g.SetProduction(city, warrior);
            city.ProductionStored = 30;
            Assert.True(EconomyRules.PurchaseCost(g, city, warrior) < full);

            p.Gold = 10;
            Assert.False(g.Purchase(city, warrior));
            p.Gold = 500;
            int price = EconomyRules.PurchaseCost(g, city, warrior);
            Assert.True(g.Purchase(city, warrior));
            Assert.Equal(500 - price, p.Gold);
            Assert.Null(city.CurrentProduction);
            Assert.Equal(0, city.ProductionStored);
            Assert.Contains(g.ArmyAt(city.Position).Units, u => u.Def.Id == "warrior");

            Assert.True(g.Purchase(city, ProductionItem.Building("monument")));
            Assert.True(city.Has("monument"));
            city.BesiegedSinceTurn = g.Turn;
            Assert.False(g.Purchase(city, warrior));
        }

        [Fact]
        public void Ai_spends_surplus_gold()
        {
            var g = TestWorld.Game(TestWorld.FlatMap(20, 12));
            g.FoundCity(0, TestWorld.H(5, 5), "Home", true);
            g.FoundCity(1, TestWorld.H(16, 9), "Far", true);
            var p = g.Player(0);
            p.Gold = 1000;
            int before = EconomyRules.MilitaryUnitCount(g, p);
            new StrategicAI().TakeTurn(g, p);
            Assert.True(p.Gold < 1000);
            Assert.True(p.Gold >= new StrategicAI().GoldReserve);
            Assert.True(EconomyRules.MilitaryUnitCount(g, p) > before);
        }
    }
}
