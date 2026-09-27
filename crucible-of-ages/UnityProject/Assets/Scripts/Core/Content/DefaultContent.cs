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

        public static ContentDatabase Create()
        {
            var db = new ContentDatabase();
            AddFactions(db);
            AddTechs(db);
            AddUnits(db);
            AddBuildings(db);
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
        }

        static void AddTechs(ContentDatabase db)
        {
            void T(string id, string name, Era era, int cost, int capBonus, params string[] pre) =>
                db.Add(new TechDef { Id = id, Name = name, Era = era, ScienceCost = cost, ArmyCapBonus = capBonus, Prerequisites = new List<string>(pre) });

            T("agriculture", "Agriculture", Era.Ancient, 20, 0);
            T("pottery", "Pottery", Era.Ancient, 35, 0, "agriculture");
            T("writing", "Writing", Era.Ancient, 55, 0, "pottery");
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
            T("military_tactics", "Military Tactics", Era.Classical, 175, 1, "iron_working", "horseback_riding");

            T("steel", "Steel", Era.Medieval, 485, 0, "iron_working");
            T("machinery", "Machinery", Era.Medieval, 485, 0, "mathematics", "construction");
            T("chivalry", "Chivalry", Era.Medieval, 485, 1, "military_tactics");

            T("gunpowder", "Gunpowder", Era.Renaissance, 1150, 0, "steel", "machinery");
            T("metallurgy", "Metallurgy", Era.Renaissance, 1600, 0, "gunpowder");

            T("military_science", "Military Science", Era.Industrial, 2350, 1, "metallurgy", "chivalry");
            T("rifling", "Rifling", Era.Industrial, 2350, 0, "gunpowder");

            T("replaceable_parts", "Replaceable Parts", Era.Modern, 3100, 0, "rifling", "military_science");
            T("combustion", "Combustion", Era.Modern, 3100, 0, "replaceable_parts");

            T("combined_arms", "Combined Arms", Era.Atomic, 4600, 1, "combustion");

            T("robotics", "Robotics", Era.Information, 6200, 0, "combined_arms");
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
            // Information
            U("mech_infantry", "Mechanized Infantry", UnitClass.Gunpowder, Era.Information, 90, 375, "robotics", battleMp: 3, worldMp: 3);

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
        }
    }
}
