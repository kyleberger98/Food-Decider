using System;
using System.Collections.Generic;
using System.Linq;
using Crucible.Core.Content;
using Crucible.Core.Random;
using Crucible.Core.Units;
using Crucible.Core.World;

namespace Crucible.Core.Combat
{
    [Serializable]
    public readonly struct CombatModifier
    {
        public readonly string Label;
        public readonly int Value;

        public CombatModifier(string label, int value)
        {
            Label = label;
            Value = value;
        }

        public override string ToString() => $"{Label} {(Value >= 0 ? "+" : "")}{Value}";
    }

    /// <summary>Base strength plus every flat modifier — exactly what the combat preview tooltip shows.</summary>
    public sealed class StrengthBreakdown
    {
        public int Base;
        public readonly List<CombatModifier> Modifiers = new List<CombatModifier>();
        public int Total => Base + Modifiers.Sum(m => m.Value);

        internal void Add(string label, int value)
        {
            if (value != 0) Modifiers.Add(new CombatModifier(label, value));
        }

        public override string ToString() => $"{Total} (base {Base}{string.Concat(Modifiers.Select(m => ", " + m))})";
    }

    /// <summary>Everything the formula needs to know about one attack. Built by <see cref="Battle"/>.</summary>
    public sealed class CombatSituation
    {
        public Unit Attacker;
        public Unit Defender;
        public Tile AttackerTile;
        public Tile DefenderTile;
        public bool IsRanged;
        public bool CrossesRiver;

        /// <summary>Other attacking-side units adjacent to the defender (melee only).</summary>
        public int Flankers;

        /// <summary>The walls on the defender's hex have been breached: no wall bonus.</summary>
        public bool WallsBreached;

        public readonly List<CombatModifier> AttackerExtras = new List<CombatModifier>();
        public readonly List<CombatModifier> DefenderExtras = new List<CombatModifier>();
    }

    public sealed class CombatPreview
    {
        public StrengthBreakdown Attacker;
        public StrengthBreakdown Defender;
        public int Delta => Attacker.Total - Defender.Total;
        public double ExpectedDamageToDefender;
        public double ExpectedDamageToAttacker;
    }

    public sealed class CombatResult
    {
        public CombatPreview Preview;
        public int DamageToDefender;
        public int DamageToAttacker;
        public bool DefenderKilled;
        public bool AttackerKilled;
    }

    /// <summary>
    /// Humankind-style flat-modifier combat with a Civ V-style exponential damage curve (GDD §4.5):
    /// <c>dmg = round(30 · e^(Δ/25) · U(0.8, 1.2))</c>. Equal strength deals ~30; +25 CS ≈ 2.7×.
    /// </summary>
    public static class CombatResolver
    {
        public const double BaseDamage = 30.0;
        public const double StrengthScale = 25.0;
        public const double RandomMin = 0.8;
        public const double RandomMax = 1.2;

        public const int HighGroundPerLevel = 3;
        public const int HighGroundMax = 6;
        public const int RoughTerrainDefense = 3;
        public const int RiverCrossingPenalty = -4;
        public const int FlankingPerUnit = 2;
        public const int FlankingMax = 6;
        public const int FortifiedBonus = 3;
        public const int WallBonusPerTier = 5;
        public const int HpPerWoundPoint = 10;

        public static CombatPreview Preview(CombatSituation s)
        {
            Validate(s);
            var atk = new StrengthBreakdown
            {
                Base = s.IsRanged ? s.Attacker.Def.RangedStrength : s.Attacker.Def.CombatStrength,
            };
            var def = new StrengthBreakdown { Base = s.Defender.Def.CombatStrength };

            // Wounds
            atk.Add("Wounded", -WoundPenalty(s.Attacker));
            def.Add("Wounded", -WoundPenalty(s.Defender));

            // Elevation: whoever stands higher gets the bonus.
            int climb = s.AttackerTile.Elevation - s.DefenderTile.Elevation;
            if (climb > 0) atk.Add("High ground", Math.Min(HighGroundMax, climb * HighGroundPerLevel));
            if (climb < 0) def.Add("High ground", Math.Min(HighGroundMax, -climb * HighGroundPerLevel));

            // Terrain & position
            if (s.DefenderTile.IsRoughTerrain) def.Add("Rough terrain", RoughTerrainDefense);
            if (!s.IsRanged && s.CrossesRiver) atk.Add("Across river", RiverCrossingPenalty);
            if (!s.IsRanged && s.Flankers > 0) atk.Add("Flanking", Math.Min(FlankingMax, s.Flankers * FlankingPerUnit));
            if (s.Defender.Fortified) def.Add("Fortified", FortifiedBonus);
            if (s.DefenderTile.WallTier > 0 && !s.WallsBreached) def.Add("Walls", s.DefenderTile.WallTier * WallBonusPerTier);

            // Class counters
            atk.Add("Class bonus", ClassBonus(s.Attacker.Def.Class, s.Defender.Def.Class, attacking: true, targetOnCity: s.DefenderTile.HasCity));
            def.Add("Class bonus", ClassBonus(s.Defender.Def.Class, s.Attacker.Def.Class, attacking: false, targetOnCity: false));

            foreach (var m in s.AttackerExtras) atk.Add(m.Label, m.Value);
            foreach (var m in s.DefenderExtras) def.Add(m.Label, m.Value);

            int delta = atk.Total - def.Total;
            return new CombatPreview
            {
                Attacker = atk,
                Defender = def,
                ExpectedDamageToDefender = Math.Min(Unit.MaxHp, ExpectedDamage(delta)),
                ExpectedDamageToAttacker = s.IsRanged ? 0 : Math.Min(Unit.MaxHp, ExpectedDamage(-delta)),
            };
        }

        /// <summary>Rolls damage and applies it to both units. Ranged attacks take no retaliation.</summary>
        public static CombatResult Resolve(CombatSituation s, DeterministicRng rng)
        {
            var preview = Preview(s);
            int toDefender = RollDamage(preview.Delta, rng);
            int toAttacker = s.IsRanged ? 0 : RollDamage(-preview.Delta, rng);

            s.Defender.TakeDamage(toDefender);
            s.Attacker.TakeDamage(toAttacker);

            return new CombatResult
            {
                Preview = preview,
                DamageToDefender = toDefender,
                DamageToAttacker = toAttacker,
                DefenderKilled = !s.Defender.IsAlive,
                AttackerKilled = !s.Attacker.IsAlive,
            };
        }

        public static double ExpectedDamage(int delta) => BaseDamage * Math.Exp(delta / StrengthScale);

        public static int RollDamage(int delta, DeterministicRng rng)
        {
            double dmg = ExpectedDamage(delta) * rng.Range(RandomMin, RandomMax);
            return Math.Max(1, Math.Min(Unit.MaxHp, (int)Math.Round(dmg)));
        }

        /// <summary>−1 CS per 10 HP missing.</summary>
        public static int WoundPenalty(Unit u) => (Unit.MaxHp - u.Hp) / HpPerWoundPoint;

        /// <summary>Counter bonuses from GDD §4.1, from the point of view of <paramref name="self"/>.</summary>
        public static int ClassBonus(UnitClass self, UnitClass opponent, bool attacking, bool targetOnCity)
        {
            int bonus = 0;
            if (self == UnitClass.AntiCavalry && (opponent == UnitClass.Mounted || opponent == UnitClass.Armor)) bonus += 8; // pikes vs horse, anti-tank vs armour
            if (self == UnitClass.Armor && opponent == UnitClass.Gunpowder) bonus += 5;
            if (attacking && self == UnitClass.Mounted && (opponent == UnitClass.Ranged || opponent == UnitClass.Siege)) bonus += 5;
            if (attacking && self == UnitClass.Siege) bonus += targetOnCity ? 10 : -10;
            return bonus;
        }

        static void Validate(CombatSituation s)
        {
            if (s.Attacker == null || s.Defender == null) throw new ArgumentException("Attacker and defender are required.");
            if (s.AttackerTile == null || s.DefenderTile == null) throw new ArgumentException("Tiles are required.");
            if (s.IsRanged && !s.Attacker.Def.IsRanged) throw new ArgumentException($"{s.Attacker} has no ranged attack.");
        }
    }
}
