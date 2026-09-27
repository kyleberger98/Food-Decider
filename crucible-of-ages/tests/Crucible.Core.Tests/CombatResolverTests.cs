using System.Linq;
using Crucible.Core.Combat;
using Crucible.Core.Random;
using Crucible.Core.World;
using Xunit;

namespace Crucible.Core.Tests
{
    public class CombatResolverTests
    {
        static CombatSituation Duel(string attacker, string defender, int atkElev = 1, int defElev = 1)
        {
            return new CombatSituation
            {
                Attacker = TestWorld.Unit(attacker, 0),
                Defender = TestWorld.Unit(defender, 1),
                AttackerTile = new Tile(new Hex.HexCoord(0, 0)) { Terrain = TerrainType.Grassland, Elevation = atkElev },
                DefenderTile = new Tile(new Hex.HexCoord(1, 0)) { Terrain = TerrainType.Grassland, Elevation = defElev },
            };
        }

        static int Mod(StrengthBreakdown b, string label) => b.Modifiers.Where(m => m.Label == label).Sum(m => m.Value);

        [Fact]
        public void Equal_strength_deals_thirty_expected_damage_both_ways()
        {
            var p = CombatResolver.Preview(Duel("warrior", "warrior"));
            Assert.Equal(0, p.Delta);
            Assert.Equal(30, p.ExpectedDamageToDefender, 3);
            Assert.Equal(30, p.ExpectedDamageToAttacker, 3);
        }

        [Fact]
        public void Damage_scales_exponentially_with_strength_difference()
        {
            Assert.Equal(30 * System.Math.E, CombatResolver.ExpectedDamage(25), 3);
            Assert.True(CombatResolver.ExpectedDamage(10) > CombatResolver.ExpectedDamage(0));
            Assert.True(CombatResolver.ExpectedDamage(-10) < CombatResolver.ExpectedDamage(0));
        }

        [Theory]
        [InlineData(2, 1, 3)]
        [InlineData(3, 1, 6)]
        [InlineData(4, 0, 6)] // capped
        public void High_ground_bonus_per_level_is_capped(int atkElev, int defElev, int expected)
        {
            var p = CombatResolver.Preview(Duel("warrior", "warrior", atkElev, defElev));
            Assert.Equal(expected, Mod(p.Attacker, "High ground"));
            Assert.Equal(0, Mod(p.Defender, "High ground"));
        }

        [Fact]
        public void Defender_on_high_ground_gets_the_bonus()
        {
            var p = CombatResolver.Preview(Duel("warrior", "warrior", 1, 2));
            Assert.Equal(3, Mod(p.Defender, "High ground"));
        }

        [Fact]
        public void Spears_counter_horses_in_both_directions()
        {
            var atk = CombatResolver.Preview(Duel("spearman", "horseman"));
            Assert.Equal(8, Mod(atk.Attacker, "Class bonus"));
            var def = CombatResolver.Preview(Duel("horseman", "spearman"));
            Assert.Equal(8, Mod(def.Defender, "Class bonus"));
        }

        [Fact]
        public void Situational_modifiers_apply()
        {
            var s = Duel("warrior", "warrior");
            s.CrossesRiver = true;
            s.Flankers = 5;
            s.Defender.Fortified = true;
            s.DefenderTile.Feature = FeatureType.Forest;
            s.Attacker.Hp = 65;
            var p = CombatResolver.Preview(s);
            Assert.Equal(CombatResolver.RiverCrossingPenalty, Mod(p.Attacker, "Across river"));
            Assert.Equal(CombatResolver.FlankingMax, Mod(p.Attacker, "Flanking"));
            Assert.Equal(-3, Mod(p.Attacker, "Wounded"));
            Assert.Equal(CombatResolver.FortifiedBonus, Mod(p.Defender, "Fortified"));
            Assert.Equal(CombatResolver.RoughTerrainDefense, Mod(p.Defender, "Rough terrain"));
        }

        [Fact]
        public void Ranged_attacks_use_ranged_strength_and_take_no_retaliation()
        {
            var s = Duel("archer", "warrior");
            s.IsRanged = true;
            var r = CombatResolver.Resolve(s, new DeterministicRng(1));
            Assert.Equal(7, r.Preview.Attacker.Base);
            Assert.Equal(0, r.DamageToAttacker);
            Assert.Equal(100, s.Attacker.Hp);
            Assert.InRange(r.DamageToDefender, 1, 100);
            Assert.Equal(100 - r.DamageToDefender, s.Defender.Hp);
        }

        [Fact]
        public void Rolled_damage_stays_within_the_random_band()
        {
            var rng = new DeterministicRng(3);
            for (int i = 0; i < 500; i++)
                Assert.InRange(CombatResolver.RollDamage(0, rng), 24, 36);
        }
    }
}
