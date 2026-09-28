using System.Linq;
using Crucible.Core.Empire;
using Crucible.Core.Game;

namespace Crucible.Core.AI
{
    /// <summary>How an AI answers proposals (GDD §6.5): opinion for friendship, strength for peace.</summary>
    public static class DiplomacyAI
    {
        public static bool Accepts(GameState game, Player ai, Player from, Treaty kind)
        {
            int opinion = game.Opinion(ai, from);
            switch (kind)
            {
                case Treaty.Peace:
                {
                    var rel = game.Diplomacy.Get(ai.Id, from.Id);
                    int warTurns = game.Turn - rel.WarStartedTurn;
                    double mine = MilitaryStrength(game, ai), theirs = MilitaryStrength(game, from);
                    // Losing, or a long stalemate, or we have no designs on them anyway.
                    return mine < 0.9 * theirs || warTurns >= 40 || !PlansWarOn(game, ai, from);
                }
                case Treaty.OpenBorders:
                    return opinion >= 0 && !PlansWarOn(game, ai, from);
                case Treaty.DefensivePact:
                    return opinion >= 20 && !PlansWarOn(game, ai, from);
                default:
                    return false;
            }
        }

        public static double MilitaryStrength(GameState game, Player p) =>
            game.Armies.Where(a => a.OwnerId == p.Id).Sum(StrategicAI.Strength);

        public static bool PlansWarOn(GameState game, Player ai, Player other) =>
            game.City(ai.AITargetCityId) is City target && target.OwnerId == other.Id;
    }
}
