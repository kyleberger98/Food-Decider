using System.Collections.Generic;
using Crucible.Core.Economy;

namespace Crucible.Core.Content
{
    /// <summary>
    /// Starter content: a skeleton tech spine through all eight eras (only the military techs
    /// so far), the land units along it, and the two v1 factions. Numbers follow Civ V
    /// where an analogue exists.
    /// </summary>
    public static class DefaultContent
    {
        public const string Aurel = "aurel_dominion";
        public const string Khaganate = "steppe_khaganate";
        public const string MilitiaUnit = "militia";
        public const string SettlerUnit = "settler";
        public const string WorkerUnit = "worker";
        public const string CityStateFaction = "city_state";

        public static ContentDatabase Create()
        {
            var db = new ContentDatabase();
            AddFactions(db);
            AddTechs(db);
            AddUnits(db);
            AddBuildings(db);
            AddPolicies(db);
            AddGreatPeopleAndProjects(db);
            db.Validate();
            return db;
        }

        static void AddFactions(ContentDatabase db)
        {
            db.Add(new FactionDef
            {
                Id = Aurel,
                Name = "Aurel Dominion",
                AbilityName = "Iron Discipline",
                AbilityText = "Army cap +1. Units heal 10 HP when a battle round ends.",
                ArmyCapBonus = 1,
                HealOnRoundEnd = 10,
            });
            db.Add(new FactionDef
            {
                Id = Khaganate,
                Name = "Khaganate of the Steppe",
                AbilityName = "Endless Horizon",
                AbilityText = "Mounted units +1 movement on the world map. Pillaging is free and yields double.",
                MountedWorldMovementBonus = 1,
            });
            db.Add(new FactionDef
            {
                Id = CityStateFaction,
                Name = "City-State",
                AbilityName = "Independence",
                AbilityText = "Neutral minor power. Befriend it with gold for bonuses and World Congress votes.",
            });
        }

        static void AddTechs(ContentDatabase db)
        {
            void T(string id, string name, Era era, int cost, int capBonus, params string[] pre) =>
                db.Add(new TechDef { Id = id, Name = name, Era = era, ScienceCost = cost, ArmyCapBonus = capBonus, Prerequisites = new List<string>(pre) });

            T("agriculture", "Agriculture", Era.Ancient, 20, 0);
            T("pottery", "Pottery", Era.Ancient, 35, 0, "agriculture");
            T("writing", "Writing", Era.Ancient, 55, 0, "pottery");
            T("calendar", "Calendar", Era.Ancient, 70, 0, "pottery");
            T("sailing", "Sailing", Era.Ancient, 55, 0, "pottery");
            T("animal_husbandry", "Animal Husbandry", Era.Ancient, 35, 0, "agriculture");
            T("archery", "Archery", Era.Ancient, 35, 0, "agriculture");
            T("mining", "Mining", Era.Ancient, 35, 0, "agriculture");
            T("bronze_working", "Bronze Working", Era.Ancient, 55, 0, "mining");
            T("horseback_riding", "Horseback Riding", Era.Ancient, 55, 0, "animal_husbandry");
            T("the_wheel", "The Wheel", Era.Ancient, 55, 0, "animal_husbandry");
            T("masonry", "Masonry", Era.Ancient, 55, 0, "mining");

            T("iron_working", "Iron Working", Era.Classical, 105, 0, "bronze_working");
            T("mathematics", "Mathematics", Era.Classical, 105, 0, "the_wheel", "archery");
            T("construction", "Construction", Era.Classical, 105, 0, "masonry", "the_wheel");
            T("currency", "Currency", Era.Classical, 105, 0, "bronze_working");
            T("optics", "Optics", Era.Classical, 105, 0, "sailing");
            T("philosophy", "Philosophy", Era.Classical, 175, 0, "writing", "calendar");
            T("drama", "Drama and Poetry", Era.Classical, 175, 0, "philosophy");
            T("military_tactics", "Military Tactics", Era.Classical, 175, 1, "iron_working", "horseback_riding");

            T("steel", "Steel", Era.Medieval, 485, 0, "iron_working");
            T("machinery", "Machinery", Era.Medieval, 485, 0, "mathematics", "construction");
            T("chivalry", "Chivalry", Era.Medieval, 485, 1, "military_tactics");
            T("education", "Education", Era.Medieval, 485, 0, "philosophy");
            T("astronomy", "Astronomy", Era.Medieval, 485, 0, "optics", "education");
            T("theology", "Theology", Era.Medieval, 485, 0, "philosophy", "calendar");
            T("physics", "Physics", Era.Medieval, 485, 0, "machinery");
            T("guilds", "Guilds", Era.Medieval, 485, 0, "currency");

            T("gunpowder", "Gunpowder", Era.Renaissance, 1150, 0, "steel", "machinery");
            T("metallurgy", "Metallurgy", Era.Renaissance, 1600, 0, "gunpowder");
            T("navigation", "Navigation", Era.Renaissance, 1150, 0, "astronomy");
            T("banking", "Banking", Era.Renaissance, 1150, 0, "currency", "education");
            T("printing_press", "Printing Press", Era.Renaissance, 1150, 0, "machinery", "education");
            T("economics", "Economics", Era.Renaissance, 1600, 0, "banking", "printing_press");

            T("military_science", "Military Science", Era.Industrial, 2350, 1, "metallurgy", "chivalry");
            T("rifling", "Rifling", Era.Industrial, 2350, 0, "gunpowder");
            T("steam_power", "Steam Power", Era.Industrial, 2350, 0, "navigation", "metallurgy");
            T("industrialization", "Industrialization", Era.Industrial, 2350, 0, "steam_power", "banking");
            T("electricity", "Electricity", Era.Industrial, 2600, 0, "industrialization");
            T("scientific_theory", "Scientific Theory", Era.Industrial, 2350, 0, "printing_press", "astronomy");

            T("replaceable_parts", "Replaceable Parts", Era.Modern, 3100, 0, "rifling", "military_science");
            T("combustion", "Combustion", Era.Modern, 3100, 0, "replaceable_parts");
            T("radio", "Radio", Era.Modern, 3100, 0, "electricity");
            T("flight", "Flight", Era.Modern, 3400, 0, "combustion", "radio");
            T("refrigeration", "Refrigeration", Era.Modern, 3100, 0, "electricity", "scientific_theory");

            T("combined_arms", "Combined Arms", Era.Atomic, 4600, 1, "combustion");
            T("radar", "Radar", Era.Atomic, 4600, 0, "flight");
            T("rocketry", "Rocketry", Era.Atomic, 4600, 0, "radar");
            T("nuclear_fission", "Nuclear Fission", Era.Atomic, 5200, 0, "rocketry");
            T("mass_media", "Mass Media", Era.Atomic, 4600, 0, "radio");

            T("robotics", "Robotics", Era.Information, 6200, 0, "combined_arms");
            T("computers", "Computers", Era.Information, 6200, 0, "radar", "combined_arms");
            T("satellites", "Satellites", Era.Information, 7000, 0, "computers", "rocketry");
            T("the_internet", "The Internet", Era.Information, 7000, 0, "computers", "mass_media");
            T("nanotechnology", "Nanotechnology", Era.Information, 7500, 0, "robotics", "satellites");
            T("globalization", "Globalization", Era.Information, 7500, 0, "the_internet");
            T("space_flight", "Space Flight", Era.Information, 8000, 0, "satellites", "nanotechnology");
        }

        static void AddUnits(ContentDatabase db)
        {
            UnitDef U(string id, string name, UnitClass cls, Era era, int cs, int cost, string tech,
                int rs = 0, int range = 0, int battleMp = 2, int worldMp = 2, bool indirect = false, string resource = null)
            {
                var def = new UnitDef
                {
                    Id = id, Name = name, Class = cls, Era = era, CombatStrength = cs, RangedStrength = rs,
                    Range = range, BattleMovement = battleMp, WorldMovement = worldMp, ProductionCost = cost,
                    RequiredTech = tech, IndirectFire = indirect, RequiredResource = resource,
                    WallDamageMultiplier = cls == UnitClass.Siege ? 2.0 : 1.0,
                };
                db.Add(def);
                return def;
            }

            // Civilian
            U(SettlerUnit, "Settler", UnitClass.Civilian, Era.Ancient, 0, 106, null);
            U(WorkerUnit, "Worker", UnitClass.Civilian, Era.Ancient, 0, 70, null);

            // Ancient
            U("scout", "Scout", UnitClass.Recon, Era.Ancient, 5, 25, null, battleMp: 3, worldMp: 3);
            U("warrior", "Warrior", UnitClass.Melee, Era.Ancient, 8, 40, null);
            U("archer", "Archer", UnitClass.Ranged, Era.Ancient, 5, 40, "archery", rs: 7, range: 2);
            U("spearman", "Spearman", UnitClass.AntiCavalry, Era.Ancient, 11, 56, "bronze_working");
            U("horseman", "Horseman", UnitClass.Mounted, Era.Ancient, 12, 75, "horseback_riding", battleMp: 4, worldMp: 4, resource: "horses");
            // Classical
            U("catapult", "Catapult", UnitClass.Siege, Era.Classical, 7, 75, "mathematics", rs: 14, range: 2, indirect: true, resource: "iron");
            U("swordsman", "Swordsman", UnitClass.Melee, Era.Classical, 14, 75, "iron_working", resource: "iron");
            U("composite_bowman", "Composite Bowman", UnitClass.Ranged, Era.Classical, 7, 75, "construction", rs: 11, range: 2);
            // Medieval
            U("pikeman", "Pikeman", UnitClass.AntiCavalry, Era.Medieval, 16, 90, "steel");
            U("knight", "Knight", UnitClass.Mounted, Era.Medieval, 20, 120, "chivalry", battleMp: 4, worldMp: 4, resource: "horses");
            U("crossbowman", "Crossbowman", UnitClass.Ranged, Era.Medieval, 13, 120, "machinery", rs: 18, range: 2);
            U("trebuchet", "Trebuchet", UnitClass.Siege, Era.Medieval, 12, 120, "machinery", rs: 20, range: 2, indirect: true);
            // Renaissance
            U("musketman", "Musketman", UnitClass.Gunpowder, Era.Renaissance, 24, 150, "gunpowder");
            U("cannon", "Cannon", UnitClass.Siege, Era.Renaissance, 14, 185, "metallurgy", rs: 26, range: 2, indirect: true);
            // Industrial
            U("rifleman", "Rifleman", UnitClass.Gunpowder, Era.Industrial, 34, 225, "rifling");
            U("artillery", "Artillery", UnitClass.Siege, Era.Industrial, 21, 250, "military_science", rs: 32, range: 3, indirect: true);
            // Modern
            U("infantry", "Infantry", UnitClass.Gunpowder, Era.Modern, 50, 320, "replaceable_parts");
            U("landship", "Landship", UnitClass.Armor, Era.Modern, 60, 350, "combustion", battleMp: 4, worldMp: 4, resource: "oil");
            // Atomic
            U("tank", "Tank", UnitClass.Armor, Era.Atomic, 70, 375, "combined_arms", battleMp: 5, worldMp: 5, resource: "oil");
            U("rocket_artillery", "Rocket Artillery", UnitClass.Siege, Era.Atomic, 45, 425, "rocketry", rs: 60, range: 3, indirect: true);
            // Information
            U("mech_infantry", "Mechanized Infantry", UnitClass.Gunpowder, Era.Information, 90, 375, "robotics", battleMp: 3, worldMp: 3);

            // Navy (GDD §4.7)
            var trireme = U("trireme", "Trireme", UnitClass.NavalMelee, Era.Ancient, 10, 40, "sailing", battleMp: 3, worldMp: 3);
            trireme.CoastOnly = true;
            U("frigate", "Frigate", UnitClass.NavalRanged, Era.Renaissance, 25, 185, "navigation", rs: 28, range: 2, battleMp: 4, worldMp: 5);
            U("ironclad", "Ironclad", UnitClass.NavalMelee, Era.Industrial, 45, 250, "steam_power", battleMp: 4, worldMp: 4);
            U("destroyer", "Destroyer", UnitClass.NavalMelee, Era.Modern, 55, 320, "combustion", battleMp: 5, worldMp: 6, resource: "oil");
            U("battleship", "Battleship", UnitClass.NavalRanged, Era.Modern, 55, 375, "electricity", rs: 65, range: 3, battleMp: 4, worldMp: 5, resource: "oil");

            // Air (based in cities; Range = operational radius, RangedStrength = strike strength)
            U("fighter", "Fighter", UnitClass.Fighter, Era.Modern, 20, 300, "flight", rs: 45, range: 8, resource: "oil");
            U("bomber", "Bomber", UnitClass.Bomber, Era.Atomic, 25, 320, "radar", rs: 65, range: 10, resource: "oil");
            U("jet_fighter", "Jet Fighter", UnitClass.Fighter, Era.Information, 30, 375, "computers", rs: 70, range: 10);

            // Not buildable: spawned by besieged cities (GDD §4.6).
            U(MilitiaUnit, "Militia", UnitClass.Melee, Era.Ancient, 6, 0, null);

            // Siege engines, built from siege progress while besieging.
            var ram = U("battering_ram", "Battering Ram", UnitClass.Siege, Era.Ancient, 10, 0, null);
            ram.SiegeOnly = true;
            ram.SiegeProgressCost = 30;
            ram.AttacksWallsOnly = true;
            ram.WallDamageMultiplier = 3.0;

            var tower = U("siege_tower", "Siege Tower", UnitClass.Siege, Era.Ancient, 6, 0, null);
            tower.SiegeOnly = true;
            tower.SiegeProgressCost = 40;
            tower.CanAttack = false;
            tower.CarriesOverWalls = true;

            db.Unit("catapult").SiegeProgressCost = 45;
            db.Unit("trebuchet").SiegeProgressCost = 60;

            // Faction uniques
            var legionary = U("aurel_legionary", "Aurel Legionary", UnitClass.Melee, Era.Classical, 17, 75, "iron_working", resource: "iron");
            legionary.FactionId = Aurel;
            legionary.Replaces = "swordsman";

            var skyRider = U("sky_rider", "Sky Rider", UnitClass.Mounted, Era.Ancient, 14, 75, "horseback_riding", battleMp: 4, worldMp: 5, resource: "horses");
            skyRider.FactionId = Khaganate;
            skyRider.Replaces = "horseman";
        }

        static void AddPolicies(ContentDatabase db)
        {
            void P(string tree, string id, string name, string requires, string text,
                Yields capital = default, Yields city = default, int happiness = 0, int armyCap = 0, int cs = 0) =>
                db.Add(new PolicyDef
                {
                    Id = id, Name = name, Tree = tree, Requires = requires, Description = text,
                    CapitalYields = capital, CityYields = city, Happiness = happiness,
                    ArmyCapBonus = armyCap, CombatStrengthBonus = cs,
                });

            P("Tradition", "tradition", "Tradition", null, "+3 culture and +2 food in the capital.", capital: new Yields(food: 2, culture: 3));
            P("Tradition", "aristocracy", "Aristocracy", "tradition", "+3 production in the capital.", capital: new Yields(production: 3));
            P("Tradition", "monarchy", "Monarchy", "aristocracy", "+3 gold in the capital, +2 happiness.", capital: new Yields(gold: 3), happiness: 2);

            P("Liberty", "liberty", "Liberty", null, "+1 culture in every city.", city: new Yields(culture: 1));
            P("Liberty", "collective_rule", "Collective Rule", "liberty", "+1 food in every city.", city: new Yields(food: 1));
            P("Liberty", "republic", "Republic", "collective_rule", "+1 production in every city.", city: new Yields(production: 1));

            P("Honor", "honor", "Honor", null, "+2 combat strength for all units.", cs: 2);
            P("Honor", "discipline", "Discipline", "honor", "+1 more combat strength, +1 happiness.", cs: 1, happiness: 1);
            P("Honor", "professional_army", "Professional Army", "discipline", "Army cap +1.", armyCap: 1);

            P("Commerce", "commerce", "Commerce", null, "+3 gold in the capital.", capital: new Yields(gold: 3));
            P("Commerce", "mercantilism", "Mercantilism", "commerce", "+1 gold in every city.", city: new Yields(gold: 1));
            P("Commerce", "protectionism", "Protectionism", "mercantilism", "+3 happiness.", happiness: 3);
        }

        static void AddGreatPeopleAndProjects(ContentDatabase db)
        {
            void G(string id, string name, GreatPersonType type) =>
                db.Add(new UnitDef { Id = id, Name = name, Class = UnitClass.Civilian, Era = Era.Ancient, GreatPerson = type, WorldMovement = 2 });
            G("great_scientist", "Great Scientist", GreatPersonType.Scientist);
            G("great_engineer", "Great Engineer", GreatPersonType.Engineer);
            G("great_merchant", "Great Merchant", GreatPersonType.Merchant);
            G("great_artist", "Great Artist", GreatPersonType.Artist);
            G("great_prophet", "Great Prophet", GreatPersonType.Prophet);
            G("great_general", "Great General", GreatPersonType.General);

            // Buildings that attract great people (Civ V specialist slots, simplified to flat points).
            void Gp(string building, GreatPersonType type, int points)
            {
                var b = db.Building(building);
                b.GreatPersonType = type;
                b.GreatPersonPoints = points;
            }
            Gp("library", GreatPersonType.Scientist, 2);
            Gp("university", GreatPersonType.Scientist, 3);
            Gp("research_lab", GreatPersonType.Scientist, 3);
            Gp("workshop", GreatPersonType.Engineer, 2);
            Gp("factory", GreatPersonType.Engineer, 3);
            Gp("market", GreatPersonType.Merchant, 2);
            Gp("bank", GreatPersonType.Merchant, 3);
            Gp("stock_exchange", GreatPersonType.Merchant, 3);
            Gp("amphitheater", GreatPersonType.Artist, 2);
            Gp("broadcast_tower", GreatPersonType.Artist, 3);
            Gp("monument", GreatPersonType.Artist, 1);

            void P(string id, string name, int cost, string tech, string requires = null, int max = 1, bool part = false, string building = null) =>
                db.Add(new ProjectDef
                {
                    Id = id, Name = name, ProductionCost = cost, RequiredTech = tech, RequiresProject = requires,
                    MaxCount = max, SpaceshipPart = part, RequiredBuilding = building,
                });
            P("apollo_program", "Apollo Program", 1500, "space_flight");
            P("ss_booster", "SS Booster", 1000, "space_flight", "apollo_program", max: 3, part: true, building: "factory");
            P("ss_cockpit", "SS Cockpit", 1000, "space_flight", "apollo_program", part: true, building: "factory");
            P("ss_stasis_chamber", "SS Stasis Chamber", 1000, "space_flight", "apollo_program", part: true, building: "factory");
            P("ss_engine", "SS Engine", 1000, "space_flight", "apollo_program", part: true, building: "factory");
        }

        static void AddBuildings(ContentDatabase db)
        {
            void B(string id, string name, Era era, int cost, string tech, int upkeep, Yields yields, int happiness = 0, int walls = 0) =>
                db.Add(new BuildingDef
                {
                    Id = id, Name = name, Era = era, ProductionCost = cost, RequiredTech = tech,
                    Maintenance = upkeep, Yields = yields, Happiness = happiness, WallTiers = walls,
                });

            B("monument", "Monument", Era.Ancient, 40, null, 1, new Yields(culture: 2));
            B("granary", "Granary", Era.Ancient, 60, "pottery", 1, new Yields(food: 2));
            B("library", "Library", Era.Ancient, 75, "writing", 1, new Yields(science: 3));
            B("walls", "Walls", Era.Ancient, 60, "masonry", 1, new Yields(), walls: 1);
            B("market", "Market", Era.Classical, 100, "currency", 0, new Yields(gold: 3));
            B("colosseum", "Colosseum", Era.Classical, 100, "construction", 1, new Yields(), happiness: 3);
            B("workshop", "Workshop", Era.Medieval, 120, "machinery", 2, new Yields(production: 3));
            B("castle", "Castle", Era.Medieval, 160, "chivalry", 2, new Yields(), walls: 1);
            B("harbor", "Harbor", Era.Ancient, 80, "sailing", 1, new Yields(food: 2, gold: 1));
            db.Building("harbor").RequiresCoast = true;
            B("university", "University", Era.Medieval, 160, "education", 2, new Yields(science: 5));
            B("bank", "Bank", Era.Renaissance, 200, "banking", 0, new Yields(gold: 4));
            B("factory", "Factory", Era.Industrial, 300, "industrialization", 3, new Yields(production: 6));
            B("stadium", "Stadium", Era.Modern, 350, "radio", 3, new Yields(), happiness: 4);
            B("research_lab", "Research Lab", Era.Information, 400, "computers", 3, new Yields(science: 8));
            B("amphitheater", "Amphitheater", Era.Classical, 100, "drama", 1, new Yields(culture: 3));
            B("temple", "Temple", Era.Medieval, 100, "theology", 1, new Yields(culture: 1, faith: 3), happiness: 2);
            B("shrine", "Shrine", Era.Ancient, 40, "pottery", 1, new Yields(faith: 1));
            B("stock_exchange", "Stock Exchange", Era.Renaissance, 250, "economics", 0, new Yields(gold: 5));
            B("public_school", "Public School", Era.Industrial, 300, "scientific_theory", 3, new Yields(science: 5));
            B("broadcast_tower", "Broadcast Tower", Era.Atomic, 350, "mass_media", 3, new Yields(culture: 5));
            B("data_center", "Data Center", Era.Information, 400, "the_internet", 3, new Yields(science: 5, gold: 3));
        }
    }
}
