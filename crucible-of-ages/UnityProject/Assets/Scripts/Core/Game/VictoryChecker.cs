using System.Linq;
using Crucible.Core.Empire;

namespace Crucible.Core.Game
{
    public enum VictoryType
    {
        Domination,
        Science,
        Culture,
        Diplomatic,
        Score,
    }

    public sealed class VictoryResult
    {
        public VictoryType Type;
        public int WinnerId;

        public override string ToString() => $"{Type} victory for P{WinnerId}";
    }

    /// <summary>The five victory conditions of GDD §5.</summary>
    public static class VictoryChecker
    {
        public const int SpaceshipParts = 6;

        public static VictoryResult Check(GameState g)
        {
            var alive = g.Players.Where(p => !p.IsEliminated).ToList();

            // Domination: one player owns every original capital.
            var capitals = g.Cities.Where(c => c.IsOriginalCapital).ToList();
            if (capitals.Count > 1 && capitals.Select(c => c.OwnerId).Distinct().Count() == 1)
                return new VictoryResult { Type = VictoryType.Domination, WinnerId = capitals[0].OwnerId };

            foreach (var p in alive)
            {
                if (p.SpaceshipPartsLanded >= SpaceshipParts)
                    return new VictoryResult { Type = VictoryType.Science, WinnerId = p.Id };
                if (p.WonWorldLeaderVote)
                    return new VictoryResult { Type = VictoryType.Diplomatic, WinnerId = p.Id };
                if (IsCulturallyDominant(p, alive))
                    return new VictoryResult { Type = VictoryType.Culture, WinnerId = p.Id };
            }

            if (g.Turn >= g.TurnLimit)
            {
                var best = alive.OrderByDescending(p => Score(g, p)).ThenBy(p => p.Id).First();
                return new VictoryResult { Type = VictoryType.Score, WinnerId = best.Id };
            }
            return null;
        }

        /// <summary>Tourism against every other surviving civ exceeds that civ's lifetime culture.</summary>
        static bool IsCulturallyDominant(Player p, System.Collections.Generic.List<Player> alive)
        {
            var others = alive.Where(o => o.Id != p.Id).ToList();
            return others.Count > 0 && others.All(o =>
                p.TourismAgainst.TryGetValue(o.Id, out var t) && t > 0 && t > o.LifetimeCulture);
        }

        /// <summary>Civ V-style score: cities, population, techs, wonders (TODO), and future techs.</summary>
        public static int Score(GameState g, Player p)
        {
            var cities = g.Cities.Where(c => c.OwnerId == p.Id).ToList();
            return cities.Count * 8 + cities.Sum(c => c.Population) * 4 + p.Tech.Researched.Count * 4;
        }
    }
}
