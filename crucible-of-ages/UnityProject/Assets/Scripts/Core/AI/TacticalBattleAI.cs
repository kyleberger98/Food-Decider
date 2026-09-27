using System;
using System.Collections.Generic;
using System.Linq;
using Crucible.Core.Combat;
using Crucible.Core.Hex;
using Crucible.Core.Units;

namespace Crucible.Core.AI
{
    /// <summary>
    /// Greedy one-ply tactical AI (GDD §6.4). For each unit — ranged first, so melee can finish
    /// softened targets — it scores every reachable hex and every attack available from it,
    /// then executes the best. Also drives auto-resolve, so both give the same results.
    /// </summary>
    public sealed class TacticalBattleAI
    {
        public double DamageTakenWeight = 0.7;
        public double KillBonus = 40;
        public double ElevationWeight = 2;
        public double ApproachWeight = 3;

        /// <summary>Retreat when our remaining strength is below this fraction of the enemy's.</summary>
        public double RetreatThreshold = 0.25;

        /// <summary>Plays the active side's whole battle turn, ending it (or retreating).</summary>
        public void PlayTurn(Battle battle)
        {
            if (battle.Status != BattleStatus.InProgress) return;
            var side = battle.ActiveSide;

            if (ShouldRetreat(battle, side))
            {
                battle.Retreat();
                return;
            }

            var units = battle.DeployedUnits(side)
                .OrderBy(u => u.Def.IsRanged ? 0 : 1)
                .ThenBy(u => u.Id)
                .ToList();

            foreach (var unit in units)
            {
                if (battle.Status != BattleStatus.InProgress) return;
                if (!unit.IsAlive || battle.PositionOf(unit) == null) continue;
                PlayUnit(battle, unit);
            }

            if (battle.Status == BattleStatus.InProgress) battle.EndTurn();
        }

        /// <summary>Plays AI turns until the round ends or the battle finishes.</summary>
        public void PlayUntilPause(Battle battle)
        {
            int guard = 0;
            while (battle.Status == BattleStatus.InProgress && guard++ < 1000) PlayTurn(battle);
        }

        /// <summary>Plays every remaining round immediately (auto-resolve of a whole battle).</summary>
        public void ResolveFully(Battle battle)
        {
            PlayUntilPause(battle);
            while (battle.Status == BattleStatus.AwaitingNextRound)
            {
                battle.BeginNextRound();
                PlayUntilPause(battle);
            }
        }

        void PlayUnit(Battle battle, Unit unit)
        {
            var start = battle.PositionOf(unit).Value;
            var enemies = battle.DeployedUnits(Opp(battle.SideOf(unit))).ToList();
            if (enemies.Count == 0) return;

            var options = new List<(HexCoord hex, int mpLeft)> { (start, unit.BattleMovesLeft) };
            options.AddRange(battle.ReachableHexes(unit).Select(kv => (kv.Key, kv.Value)));

            double bestScore = double.NegativeInfinity;
            HexCoord bestHex = start;
            HexCoord? bestTarget = null;

            foreach (var (hex, mpLeft) in options)
            {
                double position = PositionScore(battle, unit, hex, enemies);
                if (position > bestScore)
                {
                    bestScore = position;
                    bestHex = hex;
                    bestTarget = null;
                }

                if (!unit.Def.IsRanged && mpLeft <= 0) continue;
                foreach (var enemy in enemies)
                {
                    var target = battle.PositionOf(enemy).Value;
                    if (!battle.CanAttackFrom(unit, hex, target)) continue;
                    var p = battle.PreviewAttack(unit, hex, enemy);
                    double score = 100 // any sensible attack beats shuffling around
                                   + p.ExpectedDamageToDefender
                                   - DamageTakenWeight * p.ExpectedDamageToAttacker
                                   + (p.ExpectedDamageToDefender >= enemy.Hp ? KillBonus : 0)
                                   - (p.ExpectedDamageToAttacker >= unit.Hp ? KillBonus : 0)
                                   + position * 0.1;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestHex = hex;
                        bestTarget = target;
                    }
                }
            }

            if (bestHex != start && !battle.TryMove(unit, bestHex)) return;
            if (bestTarget.HasValue) battle.TryAttack(unit, bestTarget.Value);
        }

        double PositionScore(Battle battle, Unit unit, HexCoord hex, List<Unit> enemies)
        {
            int nearest = enemies.Min(e => hex.DistanceTo(battle.PositionOf(e).Value));
            var tile = battle.Map.Get(hex);
            double score = ElevationWeight * tile.Elevation + (tile.IsRoughTerrain ? 1.5 : 0);

            if (unit.Def.IsRanged)
            {
                score -= ApproachWeight * Math.Abs(nearest - unit.Def.Range);
                if (nearest <= 1) score -= 10; // don't stand in the front line
            }
            else
            {
                score -= ApproachWeight * nearest;
            }

            if (battle.Objective.HasValue && battle.SideOf(unit) == BattleSideId.Attacker)
                score -= hex.DistanceTo(battle.Objective.Value);
            return score;
        }

        bool ShouldRetreat(Battle battle, BattleSideId side)
        {
            // Never flee a siege you are defending: the city falls anyway.
            if (side == BattleSideId.Defender && battle.Objective.HasValue) return false;
            double ours = Strength(battle, side);
            double theirs = Strength(battle, Opp(side));
            return theirs > 0 && ours / theirs < RetreatThreshold;
        }

        static double Strength(Battle battle, BattleSideId side) =>
            battle.DeployedUnits(side).Concat(battle.Side(side).Reserve)
                .Where(u => u.IsAlive)
                .Sum(u => Math.Max(u.Def.CombatStrength, u.Def.RangedStrength) * u.Hp / (double)Unit.MaxHp);

        static BattleSideId Opp(BattleSideId s) => s == BattleSideId.Attacker ? BattleSideId.Defender : BattleSideId.Attacker;
    }
}
