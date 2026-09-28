using System;
using System.Collections.Generic;
using System.Linq;
using Crucible.Core.Content;
using Crucible.Core.Game;
using Crucible.Core.Hex;
using Crucible.Core.Units;

namespace Crucible.Core.Combat
{
    public enum ForecastVerdict
    {
        CrushingDefeat,
        LikelyDefeat,
        EvenFight,
        LikelyVictory,
        DecisiveVictory,
    }

    /// <summary>A quick estimate of how a world-map attack would go, shown before committing (Humankind-style).</summary>
    public sealed class BattleForecast
    {
        public ForecastVerdict Verdict;

        /// <summary>Attacker's staying power over the defender's; above 1 favours the attacker.</summary>
        public double Ratio;

        public int AttackerUnits, DefenderUnits;

        /// <summary>Militia that would rise to defend a city (counted among the defenders).</summary>
        public int Militia;

        public int WallTier;
        public bool DefenderHoldsHighGround;

        public string Label => Verdict switch
        {
            ForecastVerdict.DecisiveVictory => "Decisive victory",
            ForecastVerdict.LikelyVictory => "Likely victory",
            ForecastVerdict.EvenFight => "Even fight",
            ForecastVerdict.LikelyDefeat => "Likely defeat",
            _ => "Crushing defeat",
        };

        /// <summary>
        /// Estimates an attack from <paramref name="attacker"/> on the army or city at
        /// <paramref name="target"/>. Every unit is paired against the average enemy using the real
        /// damage formula (strength, wounds, class counters, high ground, walls, rough terrain), and
        /// each side's damage output times its total health is compared (Lanchester's square law).
        /// Returns null if there is nothing there to fight.
        /// </summary>
        public static BattleForecast Estimate(GameState game, Army attacker, HexCoord target)
        {
            var defArmy = game.ArmyAt(target);
            var city = game.CityAt(target);
            if (defArmy != null && defArmy.OwnerId == attacker.OwnerId) return null;
            if (defArmy == null && city == null) return null;

            var defenders = new List<(UnitDef def, int hp)>();
            if (defArmy != null) defenders.AddRange(defArmy.Units.Where(u => u.Def.IsMilitary).Select(u => (def: u.Def, hp: u.Hp)));
            int militia = 0;
            if (city != null && city.OwnerId != attacker.OwnerId && !city.IsBesieged)
            {
                militia = city.MilitiaCount;
                var m = game.Content.Unit(DefaultContent.MilitiaUnit);
                for (int i = 0; i < militia; i++) defenders.Add((m, Unit.MaxHp));
            }
            var attackers = attacker.Units.Where(u => u.Def.IsMilitary).Select(u => (def: u.Def, hp: u.Hp)).ToList();

            var atkTile = game.Map.Get(attacker.Position);
            var defTile = game.Map.Get(target);
            int climb = atkTile.Elevation - defTile.Elevation;
            int walls = city != null ? defTile.WallTier : 0;
            bool river = game.Map.HasRiverBetween(attacker.Position, target);
            int attackGeneral = attacker.Units.Any(u => u.Def.GreatPerson == GreatPersonType.General) ? 3 : 0;
            int defendGeneral = defArmy != null && defArmy.Units.Any(u => u.Def.GreatPerson == GreatPersonType.General) ? 3 : 0;

            var forecast = new BattleForecast
            {
                AttackerUnits = attackers.Count,
                DefenderUnits = defenders.Count,
                Militia = militia,
                WallTier = walls,
                DefenderHoldsHighGround = climb < 0,
            };
            if (attackers.Count == 0) { forecast.Verdict = ForecastVerdict.CrushingDefeat; return forecast; }
            if (defenders.Count == 0) { forecast.Ratio = double.PositiveInfinity; forecast.Verdict = ForecastVerdict.DecisiveVictory; return forecast; }

            int Wound(int hp) => (Unit.MaxHp - hp) / CombatResolver.HpPerWoundPoint;
            int Strength(UnitDef d) => Math.Max(d.CombatStrength, d.RangedStrength);

            double AttackOutput(List<(UnitDef def, int hp)> side, List<(UnitDef def, int hp)> foes, bool attacking)
            {
                double total = 0;
                foreach (var (def, hp) in side)
                {
                    double sum = 0;
                    foreach (var (foe, foeHp) in foes)
                    {
                        int self = Strength(def) - Wound(hp) + CombatResolver.ClassBonus(def.Class, foe.Class, attacking, attacking && city != null);
                        int other = Strength(foe) - Wound(foeHp) + CombatResolver.ClassBonus(foe.Class, def.Class, !attacking, false);
                        int highGround = Math.Min(CombatResolver.HighGroundMax, Math.Abs(climb) * CombatResolver.HighGroundPerLevel);
                        if (attacking)
                        {
                            self += (climb > 0 ? highGround : 0) + attackGeneral + (river && !def.IsRanged ? CombatResolver.RiverCrossingPenalty : 0);
                            other += (climb < 0 ? highGround : 0) + walls * CombatResolver.WallBonusPerTier + defendGeneral +
                                     (defTile.IsRoughTerrain ? CombatResolver.RoughTerrainDefense : 0);
                        }
                        else
                        {
                            self += (climb < 0 ? highGround : 0) + walls * CombatResolver.WallBonusPerTier + defendGeneral +
                                    (defTile.IsRoughTerrain ? CombatResolver.RoughTerrainDefense : 0);
                            other += (climb > 0 ? highGround : 0) + attackGeneral;
                        }
                        sum += CombatResolver.ExpectedDamage(self - other);
                    }
                    total += sum / foes.Count * hp / Unit.MaxHp;
                }
                return total;
            }

            double aOut = AttackOutput(attackers, defenders, attacking: true);
            double dOut = AttackOutput(defenders, attackers, attacking: false);
            double aHp = attackers.Sum(x => x.hp), dHp = defenders.Sum(x => x.hp);
            forecast.Ratio = aOut * aHp / Math.Max(1e-6, dOut * dHp);
            forecast.Verdict = forecast.Ratio >= 2.2 ? ForecastVerdict.DecisiveVictory
                : forecast.Ratio >= 1.3 ? ForecastVerdict.LikelyVictory
                : forecast.Ratio >= 0.77 ? ForecastVerdict.EvenFight
                : forecast.Ratio >= 0.45 ? ForecastVerdict.LikelyDefeat
                : ForecastVerdict.CrushingDefeat;
            return forecast;
        }
    }
}
