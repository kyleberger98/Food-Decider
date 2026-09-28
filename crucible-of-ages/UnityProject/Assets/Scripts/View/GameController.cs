using System;
using System.Collections.Generic;
using System.Linq;
using Crucible.Core.Combat;
using Crucible.Core.Content;
using Crucible.Core.Economy;
using Crucible.Core.Empire;
using Crucible.Core.Game;
using Crucible.Core.Hex;
using Crucible.Core.Units;
using Crucible.Core.World;
using UnityEngine;

namespace Crucible.View
{
    public enum HudPanel
    {
        None,
        City,
        CityState,
        Siege,
        Diplomacy,
        Policies,
        Tech,
    }

    public enum NoticeKind
    {
        Info,
        Good,
        Bad,
        War,
    }

    public sealed class Notice
    {
        public string Text;
        public NoticeKind Kind;
        public int Turn;
        public float Time;
    }

    /// <summary>
    /// Input and game actions. Every action is a public method so the HUD's buttons and the keyboard
    /// shortcuts share one code path; the HUD (<see cref="GameHud"/>) only reads state and calls these.
    /// World: click your army, then a hex to march, an adjacent enemy to attack, or a battlefield hex to
    /// reinforce. Battle: click a unit, then a green hex to move or a red enemy to attack.
    /// Keys: Enter end turn · Space confirm deployment / end battle turn · R retreat · X auto-resolve ·
    /// B batter walls · G besiege · F found city · T research · P policies · L diplomacy · I/U improve /
    /// automate · V use great person · Esc close panel / deselect · F5/F9 quick save/load.
    /// (WASD/QE belong to the camera.)
    /// </summary>
    public sealed class GameController : MonoBehaviour
    {
        HexMapRenderer _map;
        CameraRig _rig;
        MarkerLayer _markers;
        MeshCollider _mapCollider;
        int _fogVersion = -1;
        int _mapVersion = -1;
        HexCoord? _routeFor;

        public GameState Game { get; private set; }
        public TurnManager Turns { get; private set; }
        public HexMapRenderer MapRenderer => _map;
        public Camera Camera => _rig != null ? _rig.Camera : null;

        public Army SelectedArmy { get; private set; }
        public City SelectedCity { get; private set; }
        public Unit SelectedUnit { get; private set; }
        public Player SelectedCityState { get; private set; }
        public HudPanel OpenPanel { get; private set; }
        public HexCoord? HoverHex { get; private set; }
        public ArmyPath HoverRoute { get; private set; }

        /// <summary>Newest last. The HUD shows the most recent few.</summary>
        public List<Notice> Notices { get; } = new List<Notice>();

        /// <summary>Set by the HUD: true while the pointer is over a UI element (clicks then don't reach the map).</summary>
        public Func<bool> IsPointerOverUi = () => false;

        /// <summary>Raised when a game is loaded, so views can rebuild.</summary>
        public event Action GameReplaced;

        public Player Human => Turns.ActivePlayer;

        /// <summary>Fog is drawn from the (single) human player's point of view.</summary>
        public PlayerVisibility Viewer => Game.Visibility(Game.Players.FirstOrDefault(p => !p.IsAI)?.Id ?? 0);

        /// <summary>A battle waiting on a human decision, if any.</summary>
        public Battle HumanBattle => Game.Battles.FirstOrDefault(b => b.AwaitingAction && !b.Active.Player.IsAI);

        public void Init(GameState game, TurnManager turns, HexMapRenderer map, CameraRig rig)
        {
            Game = game;
            Turns = turns;
            _map = map;
            _rig = rig;
            _mapCollider = map.GetComponent<MeshCollider>();
            _markers = new GameObject("Markers").AddComponent<MarkerLayer>();
            _markers.Init(map);
            rig.BlockZoom = () => IsPointerOverUi();
            HookEvents();
            Post("Select your army and click a hex to march. End the turn with the button or Enter.");
        }

        void HookEvents()
        {
            Game.BattleStarted += b => Post($"Battle: {b.Attacker.Player.Name} vs {b.Defender.Player.Name}.", NoticeKind.War);
            Game.BattleEnded += b => Post($"Battle over — {Describe(b)}.", b.Winner.HasValue && Side(b, b.Winner.Value).Player == HumanPlayer ? NoticeKind.Good : NoticeKind.Bad);
            Game.ArmySunk += a => Post($"An embarked army ({a.Count} units) was sunk at sea!", NoticeKind.Bad);
            Game.ExplorationFinished += a =>
            {
                if (a.OwnerId == Human.Id) Post($"{(a.IsNaval ? "A fleet" : "An explorer")} has nothing left to explore nearby and awaits orders.");
            };
            Game.GreatPersonBorn += (p, u) => { if (!p.IsAI) Post($"A {u.Def.Name} is born! Select them to use their gift.", NoticeKind.Good); };
            Game.ReligionFounded += r => Post($"{r.Name} has been founded by {Game.Player(r.FounderId).Name}.");
            Game.WarDeclared += (a, b) => Post($"War: {a.Name} against {b.Name}!", NoticeKind.War);
            Game.PeaceMade += (a, b) => Post($"Peace between {a.Name} and {b.Name}.", NoticeKind.Good);
        }

        Player HumanPlayer => Game.Players.FirstOrDefault(p => !p.IsAI);
        static BattleSide Side(Battle b, BattleSideId id) => id == BattleSideId.Attacker ? b.Attacker : b.Defender;

        static string Describe(Battle b)
        {
            switch (b.Status)
            {
                // Phrased so they read right for the human player too ("You").
                case BattleStatus.AttackerWon: return $"victory for {b.Attacker.Player.Name}" + (b.Objective.HasValue ? ", the city falls" : "");
                case BattleStatus.DefenderWon: return $"the line held for {b.Defender.Player.Name}";
                case BattleStatus.AttackerRetreated: return $"withdrawal by {b.Attacker.Player.Name}";
                case BattleStatus.DefenderRetreated: return $"withdrawal by {b.Defender.Player.Name}";
                default: return b.Status.ToString();
            }
        }

        public void Post(string text, NoticeKind kind = NoticeKind.Info)
        {
            Notices.Add(new Notice { Text = text, Kind = kind, Turn = Game?.Turn ?? 0, Time = UnityEngine.Time.time });
            if (Notices.Count > 50) Notices.RemoveAt(0);
        }

        // ------------------------------------------------------------------ frame

        void Update()
        {
            if (Game == null) return;
            var battle = HumanBattle;
            HoverHex = PickHex(out var h) ? h : (HexCoord?)null;
            HandleKeys(battle);
            if (Game == null) return; // a load may have replaced the game this frame

            if (!Turns.IsGameOver && Input.GetMouseButtonDown(0) && HoverHex.HasValue && !IsPointerOverUi())
            {
                if (battle != null) BattleClick(battle, HoverHex.Value);
                else WorldClick(HoverHex.Value);
            }

            if (SelectedArmy != null && Game.Army(SelectedArmy.Id) == null) SelectedArmy = null;
            if (SelectedCity != null && SelectedCity.OwnerId != Human.Id) { SelectedCity = null; if (OpenPanel == HudPanel.City) OpenPanel = HudPanel.None; }
            if (SelectedUnit != null && !SelectedUnit.IsAlive) SelectedUnit = null;
            if (OpenPanel == HudPanel.Siege && BesiegedBySelection() == null) OpenPanel = HudPanel.None;

            var viewer = Viewer;
            if (viewer.Version != _fogVersion || Game.MapVersion != _mapVersion)
            {
                _map.ApplyFog(viewer, MarkerLayer.ColorOf);
                _markers.SyncTerrainMarkers(Game, viewer);
                _fogVersion = viewer.Version;
                _mapVersion = Game.MapVersion;
            }

            // Route preview for the selected army (cached per hovered hex).
            if (battle == null && SelectedArmy != null && HoverHex.HasValue && !IsPointerOverUi())
            {
                if (_routeFor != HoverHex)
                {
                    HoverRoute = Pathfinder.Find(Game, SelectedArmy, HoverHex.Value);
                    _routeFor = HoverHex;
                }
            }
            else ClearRoute();

            var shown = battle ?? Game.Battles.FirstOrDefault(b => b.Attacker.Player == Human || b.Defender.Player == Human);
            _markers.Sync(Game, viewer, shown, SelectedArmy, SelectedUnit);
            _markers.ShowHighlights(battle != null ? BattleHighlights(battle) : RouteHighlights());
        }

        void HandleKeys(Battle battle)
        {
            if (Input.GetKeyDown(KeyCode.F5)) QuickSave();
            if (Input.GetKeyDown(KeyCode.F9)) { QuickLoad(); return; }
            if (Input.GetKeyDown(KeyCode.Escape)) { if (OpenPanel != HudPanel.None) ClosePanel(); else Deselect(); }
            if (Turns.IsGameOver) return;

            if (battle != null)
            {
                if (Input.GetKeyDown(KeyCode.Space)) BattleEndTurn();
                else if (Input.GetKeyDown(KeyCode.R)) BattleRetreat();
                else if (Input.GetKeyDown(KeyCode.B)) BattleBatterWalls();
                else if (Input.GetKeyDown(KeyCode.X)) BattleAutoResolve();
                return;
            }
            if (Input.GetKeyDown(KeyCode.F)) FoundCity();
            else if (Input.GetKeyDown(KeyCode.T)) TogglePanel(HudPanel.Tech);
            else if (Input.GetKeyDown(KeyCode.P)) TogglePanel(HudPanel.Policies);
            else if (Input.GetKeyDown(KeyCode.L)) TogglePanel(HudPanel.Diplomacy);
            else if (Input.GetKeyDown(KeyCode.I)) Improve();
            else if (Input.GetKeyDown(KeyCode.U)) ToggleAutomate();
            else if (Input.GetKeyDown(KeyCode.O)) ToggleExplore();
            else if (Input.GetKeyDown(KeyCode.G)) Besiege();
            else if (Input.GetKeyDown(KeyCode.V)) UseGreatPerson();
            else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) EndTurn();
        }

        bool PickHex(out HexCoord hex)
        {
            hex = default;
            if (Camera == null) return false;
            var ray = Camera.ScreenPointToRay(Input.mousePosition);
            if (!_mapCollider.Raycast(ray, out var hit, 2000f)) return false;
            hex = _map.WorldToHex(hit.point);
            return Game.Map.InBounds(hex);
        }

        void ClearRoute()
        {
            HoverRoute = null;
            _routeFor = null;
        }

        IEnumerable<(HexCoord, Color)> RouteHighlights()
        {
            if (HoverRoute == null) yield break;
            foreach (var (step, turn) in HoverRoute.Steps.Zip(HoverRoute.TurnOfStep, (s, t) => (s, t)))
                yield return (step, turn == 0 ? new Color(0.95f, 0.95f, 0.95f, 0.35f) : new Color(0.6f, 0.75f, 1f, 0.28f));
        }

        /// <summary>Deployment zone while deploying; reachable hexes (green) and targets (red) for the selected unit.</summary>
        IEnumerable<(HexCoord, Color)> BattleHighlights(Battle battle)
        {
            if (battle.Status == BattleStatus.Deploying)
            {
                foreach (var h in battle.Active.DeploymentZone) yield return (h, new Color(0.3f, 0.6f, 1f, 0.35f));
                yield break;
            }
            if (battle.Objective.HasValue)
                yield return (battle.Objective.Value, battle.WallsIntact ? new Color(1f, 0.85f, 0.2f, 0.45f) : new Color(1f, 0.85f, 0.2f, 0.2f));
            if (SelectedUnit == null || battle.PositionOf(SelectedUnit) == null) yield break;

            var from = battle.PositionOf(SelectedUnit).Value;
            foreach (var h in battle.ReachableHexes(SelectedUnit).Keys) yield return (h, new Color(0.3f, 1f, 0.4f, 0.35f));
            foreach (var enemy in battle.DeployedUnits(battle.Opponent(battle.ActiveSide).Id))
            {
                var pos = battle.PositionOf(enemy).Value;
                bool ready = SelectedUnit.Def.IsRanged || SelectedUnit.BattleMovesLeft > 0;
                if (ready && battle.CanAttackFrom(SelectedUnit, from, pos)) yield return (pos, new Color(1f, 0.25f, 0.2f, 0.5f));
            }
        }

        // ------------------------------------------------------------------ selection & panels

        public void TogglePanel(HudPanel panel) => OpenPanel = OpenPanel == panel ? HudPanel.None : panel;

        public void ClosePanel()
        {
            if (OpenPanel == HudPanel.City) SelectedCity = null;
            if (OpenPanel == HudPanel.CityState) SelectedCityState = null;
            OpenPanel = HudPanel.None;
        }

        public void Deselect()
        {
            SelectedArmy = null;
            SelectedUnit = null;
            ClearRoute();
        }

        public void SelectArmy(Army army)
        {
            SelectedArmy = army;
            if (BesiegedBySelection() != null) OpenPanel = HudPanel.Siege;
        }

        void OpenCity(City city)
        {
            SelectedCity = city;
            OpenPanel = HudPanel.City;
        }

        // ------------------------------------------------------------------ world actions

        public void EndTurn()
        {
            if (HumanBattle != null) { Post("Finish your battle first.", NoticeKind.Bad); return; }
            Deselect();
            Turns.EndTurn();
            if (Turns.IsGameOver) Post($"Game over — {Game.Victory}", NoticeKind.War);
        }

        public void FoundCity()
        {
            if (SelectedArmy == null) return;
            var city = Game.FoundCityWithSettler(SelectedArmy);
            if (city == null)
            {
                Post("Can't settle here: needs a settler with moves, open land 4+ hexes from other cities, outside foreign borders.", NoticeKind.Bad);
                return;
            }
            Post($"Founded {city.Name}!", NoticeKind.Good);
            OpenCity(city);
        }

        public void Improve()
        {
            if (SelectedArmy == null || !WorkerAutomation.HasWorker(SelectedArmy)) return;
            var tile = Game.Map.Get(SelectedArmy.Position);
            var imp = Improvements.Best(Human, tile);
            if (imp != ImprovementType.None && Game.StartImprovement(SelectedArmy, imp))
                Post($"Building a {imp} ({Improvements.BuildTurns(imp) - tile.ImprovementProgress} turns). Moving cancels.");
            else
                Post("Nothing to build here (needs your territory, the right tech and terrain).", NoticeKind.Bad);
        }

        /// <summary>Auto-improve: the worker picks, walks to and builds the best improvement each turn.</summary>
        public void ToggleAutomate()
        {
            if (SelectedArmy == null || !WorkerAutomation.HasWorker(SelectedArmy)) return;
            SelectedArmy.AutomatedWorkers = !SelectedArmy.AutomatedWorkers;
            if (SelectedArmy.AutomatedWorkers)
            {
                SelectedArmy.AutoExplore = false;
                WorkerAutomation.Run(Game, Human.Id); // start working this turn, not next
                Post(SelectedArmy.BuildOrder != ImprovementType.None
                    ? $"Auto-improve on: building a {SelectedArmy.BuildOrder}."
                    : "Auto-improve on: the worker picks its own jobs each turn.");
            }
            else Post("Auto-improve off: the worker awaits orders.");
        }

        /// <summary>Auto-explore for scouts, military armies and fleets.</summary>
        public void ToggleExplore()
        {
            if (SelectedArmy == null) return;
            if (!Exploration.CanExplore(SelectedArmy))
            {
                Post("Only scouts, military armies and fleets can explore.", NoticeKind.Bad);
                return;
            }
            SelectedArmy.AutoExplore = !SelectedArmy.AutoExplore;
            if (SelectedArmy.AutoExplore)
            {
                SelectedArmy.Destination = null;
                SelectedArmy.AutomatedWorkers = false;
                if (Exploration.Step(Game, SelectedArmy)) Post("Exploring: the army scouts on its own each turn (O to stop).");
            }
            else
            {
                SelectedArmy.Destination = null;
                Post("Stopped exploring.");
            }
        }

        public void Besiege()
        {
            if (SelectedArmy == null) return;
            var target = AdjacentEnemyCity(SelectedArmy);
            if (target != null && Game.DeclareSiege(SelectedArmy, target))
            {
                Post($"{target.Name} is under siege. Siege progress builds engines each turn.", NoticeKind.War);
                OpenPanel = HudPanel.Siege;
            }
            else Post("Stand next to an enemy city (not already besieged) to declare a siege.", NoticeKind.Bad);
        }

        public Unit GreatPersonInSelection() =>
            SelectedArmy?.Units.FirstOrDefault(u => u.Def.GreatPerson != GreatPersonType.None && u.Def.GreatPerson != GreatPersonType.General);

        public void UseGreatPerson()
        {
            var person = GreatPersonInSelection();
            if (person == null) return;
            var result = Game.UseGreatPerson(SelectedArmy, person);
            Post(result ?? "They can't do that right now.", result != null ? NoticeKind.Good : NoticeKind.Bad);
        }

        public void BuildSiegeEngine(UnitDef engine)
        {
            var city = BesiegedBySelection();
            if (city == null) return;
            Post(Game.BuildSiegeEngine(city, SelectedArmy, engine.Id) != null ? $"{engine.Name} joins the army." : "No room in this army (army cap).",
                NoticeKind.Info);
        }

        public void SetResearch(string techId)
        {
            if (!Human.Tech.CanResearch(techId)) return;
            Human.Tech.SetResearch(techId);
            Post($"Researching {Game.Content.Tech(techId).Name}.");
        }

        public void SetProduction(City city, ProductionItem item)
        {
            if (Game.SetProduction(city, item)) Post($"{city.Name} will build {EconomyRules.NameOf(Game, item)}.");
        }

        public void Buy(City city, ProductionItem item)
        {
            bool ok = Game.Purchase(city, item);
            Post(ok ? $"Bought {EconomyRules.NameOf(Game, item)} in {city.Name}." : "Couldn't buy that (not enough gold, or no room to place it).",
                ok ? NoticeKind.Good : NoticeKind.Bad);
        }

        public void AdoptPolicy(PolicyDef policy)
        {
            if (Game.AdoptPolicy(Human, policy.Id)) Post($"Adopted {policy.Name}.", NoticeKind.Good);
        }

        public void DeclareWar(Player target)
        {
            if (!Game.DeclareWar(Human, target)) Post(Game.CannotDeclareWar(Human, target) ?? "Can't declare war.", NoticeKind.Bad);
        }

        public void Propose(Player target, Treaty kind)
        {
            bool ok = Game.Propose(Human, target, kind);
            Post(ok ? $"{target.Name} agrees: {kind}." : $"{target.Name} refuses {kind}.", ok ? NoticeKind.Good : NoticeKind.Bad);
        }

        public void Answer(Proposal proposal, bool accept)
        {
            if (Game.Answer(proposal, accept)) Post($"{proposal.Kind} with {Game.Player(proposal.FromId).Name} agreed.", NoticeKind.Good);
        }

        public void Gift(Player cityState, int gold)
        {
            if (Game.GiftGold(Human, cityState, gold))
                Post($"{cityState.Name}: influence {Game.InfluenceOf(cityState, Human.Id)} ({Game.StatusWith(cityState, Human.Id)}).", NoticeKind.Good);
        }

        City AdjacentEnemyCity(Army army) =>
            army.Position.Neighbors().Select(Game.CityAt)
                .FirstOrDefault(c => c != null && Game.AtWar(c.OwnerId, army.OwnerId));

        public City AdjacentEnemyCityOfSelection() => SelectedArmy == null ? null : AdjacentEnemyCity(SelectedArmy);

        /// <summary>The city the selected army is currently besieging, if any.</summary>
        public City BesiegedBySelection() =>
            SelectedArmy == null ? null :
            SelectedArmy.Position.Neighbors().Select(Game.CityAt)
                .FirstOrDefault(c => c != null && c.IsBesieged && c.BesiegerId == SelectedArmy.OwnerId);

        void WorldClick(HexCoord hex)
        {
            var clickedCity = Game.CityAt(hex);
            var army = Game.ArmyAt(hex);

            if (army != null && army.OwnerId == Human.Id)
            {
                SelectArmy(army);
                if (clickedCity != null && clickedCity.OwnerId == Human.Id) OpenCity(clickedCity);
                return;
            }
            if (SelectedArmy == null)
            {
                if (clickedCity != null && clickedCity.OwnerId == Human.Id) OpenCity(clickedCity);
                else if (clickedCity != null && Game.Player(clickedCity.OwnerId).IsCityState)
                {
                    SelectedCityState = Game.Player(clickedCity.OwnerId);
                    OpenPanel = HudPanel.CityState;
                }
                return;
            }

            var ongoing = Game.BattleCovering(hex);
            if (ongoing != null)
            {
                bool joined = Game.JoinBattle(SelectedArmy, ongoing);
                Post(joined ? "Reinforcing! Your units march in from this side next battle turn." : "Can't join: stand next to the battlefield with moves left.",
                    joined ? NoticeKind.War : NoticeKind.Bad);
                return;
            }

            int foreignOwner = army != null && army.OwnerId != Human.Id ? army.OwnerId
                : clickedCity != null && clickedCity.OwnerId != Human.Id ? clickedCity.OwnerId : -1;
            if (foreignOwner >= 0 && Game.Player(foreignOwner).IsCityState)
            {
                SelectedCityState = Game.Player(foreignOwner);
                OpenPanel = HudPanel.CityState;
                return;
            }
            if (foreignOwner >= 0 && !Game.AtWar(foreignOwner, Human.Id))
            {
                Post($"You are at peace with {Game.Player(foreignOwner).Name}. Declare war from Diplomacy (L) first.", NoticeKind.Bad);
                return;
            }
            if (foreignOwner >= 0)
            {
                if (SelectedArmy.Position.DistanceTo(hex) != 1) { Post("Move next to the enemy first.", NoticeKind.Bad); return; }
                if (Game.Attack(SelectedArmy, hex) == null && Game.Army(SelectedArmy.Id) != null)
                    Post("Can't attack (no moves left, embarked, or already fighting).", NoticeKind.Bad);
                return;
            }

            if (clickedCity != null && clickedCity.OwnerId == Human.Id && SelectedArmy.Position == hex) { OpenCity(clickedCity); return; }
            var path = Game.OrderMove(SelectedArmy, hex);
            if (path == null) Post("No route there (water, mountains, cliffs, borders or blocked).", NoticeKind.Bad);
            else SelectedArmy.AutoExplore = false; // a manual order takes the army off auto-explore
            ClearRoute();
        }

        // ------------------------------------------------------------------ battle actions

        void BattleClick(Battle battle, HexCoord hex)
        {
            var unit = battle.UnitAt(hex);
            if (battle.Status == BattleStatus.Deploying)
            {
                if (SelectedUnit != null && battle.Redeploy(SelectedUnit, hex)) return;
                if (unit != null && battle.SideOf(unit) == battle.ActiveSide) SelectedUnit = unit;
                return;
            }
            if (unit != null && battle.SideOf(unit) == battle.ActiveSide)
            {
                SelectedUnit = unit;
                return;
            }
            if (SelectedUnit == null) return;

            if (unit != null)
            {
                var result = battle.TryAttack(SelectedUnit, hex);
                if (result == null) Post("Out of range, no line of sight, or no moves left.", NoticeKind.Bad);
                else Post($"{SelectedUnit.Def.Name} hits {unit.Def.Name} for {result.DamageToDefender}" +
                          (result.DamageToAttacker > 0 ? $", takes {result.DamageToAttacker}" : "") +
                          (result.DefenderKilled ? " — destroyed!" : "."), NoticeKind.War);
            }
            else if (!battle.TryMove(SelectedUnit, hex)) Post("Can't move there.", NoticeKind.Bad);
            AfterBattleAction(battle);
        }

        public void BattleEndTurn()
        {
            var battle = HumanBattle;
            if (battle == null) return;
            if (battle.Status == BattleStatus.Deploying) battle.ConfirmDeployment();
            else battle.EndTurn();
            SelectedUnit = null;
            AfterBattleAction(battle);
        }

        public void BattleRetreat()
        {
            var battle = HumanBattle;
            if (battle == null || battle.Status != BattleStatus.InProgress) return;
            battle.Retreat();
            AfterBattleAction(battle);
        }

        public void BattleBatterWalls()
        {
            var battle = HumanBattle;
            if (battle == null || SelectedUnit == null || battle.Status != BattleStatus.InProgress) return;
            int dmg = battle.TryAttackWalls(SelectedUnit);
            Post(dmg < 0 ? "Can't reach the walls (range, line of sight, or no moves)."
                : battle.WallsIntact ? $"Walls hit for {dmg}." : "The walls are breached!", dmg < 0 ? NoticeKind.Bad : NoticeKind.War);
            AfterBattleAction(battle);
        }

        public void BattleAutoResolve()
        {
            var battle = HumanBattle;
            if (battle == null) return;
            Game.AutoResolveRound(battle);
            SelectedUnit = null;
            if (!battle.IsFinished) Post("Round auto-resolved.");
        }

        void AfterBattleAction(Battle battle)
        {
            // Hands control to the AI side if it is now active, and applies the result when the battle ends.
            Game.AdvanceAIBattleTurns(battle);
            if (battle.IsFinished) Game.ResolveBattle(battle);
            if (battle.Status == BattleStatus.AwaitingNextRound)
                Post($"Round {battle.Round} over. The next round starts on the attacker's next turn.");
        }

        // ------------------------------------------------------------------ save / load

        static string SavePath => System.IO.Path.Combine(Application.persistentDataPath, "quicksave.crucible");

        public bool HasQuickSave => System.IO.File.Exists(SavePath);

        public void QuickSave()
        {
            System.IO.File.WriteAllBytes(SavePath, SaveGame.Save(Game, Turns));
            Post("Game saved.", NoticeKind.Good);
        }

        public void QuickLoad()
        {
            if (!HasQuickSave) { Post("No quicksave yet (F5 to save).", NoticeKind.Bad); return; }
            try
            {
                var (game, turns) = SaveGame.Load(System.IO.File.ReadAllBytes(SavePath), new Crucible.Core.AI.StrategicAI());
                Game = game;
                Turns = turns;
                _map.Build(game.Map);
                Destroy(_markers.gameObject);
                _markers = new GameObject("Markers").AddComponent<MarkerLayer>();
                _markers.Init(_map);
                SelectedArmy = null; SelectedCity = null; SelectedUnit = null; SelectedCityState = null;
                OpenPanel = HudPanel.None;
                _fogVersion = -1; _mapVersion = -1;
                ClearRoute();
                Notices.Clear();
                HookEvents();
                Post($"Loaded turn {game.Turn}.", NoticeKind.Good);
                GameReplaced?.Invoke();
            }
            catch (System.IO.InvalidDataException e)
            {
                Post($"Couldn't load: {e.Message}", NoticeKind.Bad);
            }
        }
    }
}
