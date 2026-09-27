using System;
using Crucible.Core.Empire;

namespace Crucible.Core.Game
{
    /// <summary>Strategic AI hook for a whole player turn (GDD §6.1–6.3). Lands in M6.</summary>
    public interface IPlayerAI
    {
        void TakeTurn(GameState game, Player player);
    }

    /// <summary>Placeholder AI: keeps its armies home. Its battles are still fought by the tactical AI.</summary>
    public sealed class PassivePlayerAI : IPlayerAI
    {
        public void TakeTurn(GameState game, Player player) { }
    }

    /// <summary>Sequential turns: each player in order, then the world turn advances.</summary>
    public sealed class TurnManager
    {
        readonly GameState _game;
        readonly IPlayerAI _ai;

        public int ActivePlayerIndex { get; private set; }
        public Player ActivePlayer => _game.Players[ActivePlayerIndex];

        public event Action<Player> TurnStarted;

        public TurnManager(GameState game, IPlayerAI ai = null)
        {
            _game = game ?? throw new ArgumentNullException(nameof(game));
            _ai = ai ?? new PassivePlayerAI();
        }

        public bool IsGameOver => _game.Victory != null;

        /// <summary>Begins the first player's turn and runs AI players until a human must act.</summary>
        public void Start()
        {
            ActivePlayerIndex = 0;
            BeginTurn();
            RunAIPlayers();
        }

        /// <summary>The active (human) player ends their turn.</summary>
        public void EndTurn()
        {
            Advance();
            RunAIPlayers();
        }

        void RunAIPlayers()
        {
            int guard = 0;
            while (!IsGameOver && ActivePlayer.IsAI && guard++ < _game.Players.Count * 4)
            {
                _ai.TakeTurn(_game, ActivePlayer);
                Advance();
            }
        }

        void Advance()
        {
            do
            {
                ActivePlayerIndex++;
                if (ActivePlayerIndex >= _game.Players.Count)
                {
                    ActivePlayerIndex = 0;
                    _game.Turn++;
                    _game.Victory = _game.Victory ?? VictoryChecker.Check(_game);
                }
            } while (ActivePlayer.IsEliminated && !IsGameOver);

            if (!IsGameOver) BeginTurn();
        }

        void BeginTurn()
        {
            _game.BeginPlayerTurn(ActivePlayer);
            TurnStarted?.Invoke(ActivePlayer);
        }
    }
}
