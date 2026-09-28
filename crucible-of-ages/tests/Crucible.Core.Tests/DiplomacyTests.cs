using System.Linq;
using Crucible.Core.AI;
using Crucible.Core.Content;
using Crucible.Core.Empire;
using Crucible.Core.Game;
using Xunit;

namespace Crucible.Core.Tests
{
    public class DiplomacyTests
    {
        /// <summary>Three majors at peace: P0 human (west), P1 AI (east), P2 AI (north-east).</summary>
        static GameState Peaceful()
        {
            var g = TestWorld.Game(TestWorld.FlatMap(30, 16), attackerAI: false);
            g.Diplomacy.MajorsStartAtWar = false;
            g.AddPlayer("Third", DefaultContent.Aurel, true);
            g.FoundCity(0, TestWorld.H(4, 8), "West", true);
            g.FoundCity(1, TestWorld.H(20, 8), "East", true);
            g.FoundCity(2, TestWorld.H(26, 2), "North", true);
            return g;
        }

        [Fact]
        public void Skirmishes_start_at_peace_and_the_ai_declares_war_when_ready()
        {
            var g = GameSetup.NewSkirmish(2024, 36, 24, allAI: true);
            Assert.False(g.AtWar(0, 1));
            Player declarer = null;
            g.WarDeclared += (a, _) => declarer ??= a;
            var turns = new TurnManager(g, new StrategicAI());
            turns.Start();
            for (int i = 0; i < 150 && declarer == null; i++) turns.EndTurn();
            Assert.NotNull(declarer);
            Assert.True(g.Turn >= new StrategicAI().EarliestOffensiveTurn);
        }

        [Fact]
        public void Closed_borders_keep_armies_out_until_open_borders_or_war()
        {
            var g = Peaceful();
            var army = g.CreateArmy(0, TestWorld.H(18, 8), "warrior");
            var inside = TestWorld.H(19, 8); // ring 1 of East, adjacent to the army
            Assert.Equal(1, army.Position.DistanceTo(inside));
            Assert.Equal(1, g.Map.Get(inside).OwnerPlayerId);
            Assert.False(g.MoveArmy(army, inside));
            Assert.Null(Pathfinder.Find(g, army, inside));

            Assert.True(g.Propose(g.Player(0), g.Player(1), Treaty.OpenBorders)); // the AI likes us well enough
            Assert.True(g.MoveArmy(army, inside));
        }

        [Fact]
        public void War_declarations_respect_treaties_and_trigger_defensive_pacts()
        {
            var g = Peaceful();
            var human = g.Player(0);
            var east = g.Player(1);
            var north = g.Player(2);

            g.Diplomacy.Get(1, 2).DefensivePact = true;
            Assert.True(g.DeclareWar(human, east));
            Assert.True(g.AtWar(0, 1));
            Assert.True(g.AtWar(0, 2)); // pact partner joins
            Assert.Equal(1, g.Diplomacy.Declarations.Count(d => d.declarer == 0));

            Assert.True(g.Opinion(east, human) <= Diplomacy.DeclaredOnPenalty);
            Assert.False(g.DeclareWar(human, east)); // already at war
        }

        [Fact]
        public void Peace_needs_ten_turns_of_war_then_expels_armies_and_locks_a_treaty()
        {
            var g = Peaceful();
            var human = g.Player(0);
            var east = g.Player(1);
            g.DeclareWar(human, east);
            var raiders = g.CreateArmy(0, TestWorld.H(19, 7), "swordsman");
            Assert.False(g.CanPropose(human, east, Treaty.Peace));

            for (int i = 0; i < Diplomacy.MinWarTurnsBeforePeace; i++) g.Turn++;
            // The AI accepts: it has no army at all, so it is losing.
            Assert.True(g.Propose(human, east, Treaty.Peace));
            Assert.False(g.AtWar(0, 1));
            Assert.NotEqual(1, g.Map.Get(raiders.Position).OwnerPlayerId); // expelled
            Assert.NotNull(g.CannotDeclareWar(human, east));             // treaty holds
            g.Turn += Diplomacy.PeaceTreatyTurns;
            Assert.Null(g.CannotDeclareWar(human, east));
        }

        [Fact]
        public void Ai_proposals_to_a_human_wait_for_an_answer()
        {
            var g = Peaceful();
            var human = g.Player(0);
            var east = g.Player(1);
            Assert.False(g.Propose(east, human, Treaty.OpenBorders));
            var proposal = Assert.Single(g.Diplomacy.Pending);
            Assert.Equal(east.Id, proposal.FromId);
            Assert.True(g.Answer(proposal, accept: true));
            Assert.True(g.Diplomacy.Get(0, 1).OpenBorders);
            Assert.Empty(g.Diplomacy.Pending);
        }

        [Fact]
        public void Ai_refuses_friendship_after_being_attacked()
        {
            var g = Peaceful();
            var human = g.Player(0);
            var north = g.Player(2);
            g.DeclareWar(human, north);
            for (int i = 0; i < Diplomacy.MinWarTurnsBeforePeace; i++) g.Turn++;
            g.CreateArmy(2, TestWorld.H(26, 5), "tank", "tank"); // north is strong: it won't sue for peace
            g.CreateArmy(0, TestWorld.H(2, 2), "warrior");
            north.AITargetCityId = g.CityAt(TestWorld.H(4, 8)).Id; // and has designs on us
            Assert.False(g.Propose(human, north, Treaty.Peace));
            Assert.False(g.Propose(human, g.Player(1), Treaty.DefensivePact)); // East doesn't like us enough (yet)
        }

        [Fact]
        public void Diplomacy_is_saved()
        {
            var g = Peaceful();
            g.DeclareWar(g.Player(0), g.Player(1));
            g.Diplomacy.Get(1, 2).OpenBorders = true;
            g.Propose(g.Player(2), g.Player(0), Treaty.OpenBorders);
            var (loaded, _) = SaveGame.Load(SaveGame.Save(g, null));
            Assert.True(loaded.AtWar(0, 1));
            Assert.False(loaded.AtWar(1, 2));
            Assert.True(loaded.Diplomacy.Get(1, 2).OpenBorders);
            Assert.Single(loaded.Diplomacy.Pending);
            Assert.Equal(g.Opinion(g.Player(1), g.Player(0)), loaded.Opinion(loaded.Player(1), loaded.Player(0)));
        }
    }
}
