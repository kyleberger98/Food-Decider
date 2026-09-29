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
        Log,
        Saves,
        Help,
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

        /// <summary>Where it happened: clicking the notice moves the camera there.</summary>
        public HexCoord? At;
    }

    /// <summary>What still needs the player's attention this turn (Civ's End Turn prompts).</summary>
    public enum TurnBlocker
    {
        None,
        ChooseResearch,
        ChooseProduction,
        UnitNeedsOrders,
    }

    /// <summary>
    /// Input and game actions. Every action is a public method so the HUD's buttons and the keyboard
    /// shortcuts share one code path; the HUD (<see cref="GameHud"/>) only reads state and calls these.
    /// Mouse (Humankind): left-click selects an army, unit or city (left-click on empty ground
    /// deselects); right-click gives the order: march to a hex, attack an adjacent enemy, join a
    /// battle; in battle, move to a green hex or attack a red enemy. Left-drag pans, middle-drag rotates
    /// (see <see cref="CameraRig"/>). Right-click with nothing selected closes the open panel.
    /// Keys: Enter next action / end turn (Shift+Enter: end turn now) · Tab next unit · Space skip unit
    /// (in battle: confirm deployment / end battle turn) · Z sleep · H heal · R retreat · X auto-resolve ·
    /// B batter walls · G besiege · F found city · T research · P policies · L diplomacy · N notification log ·
    /// I/U improve / auto-improve · O explore · V use great person · C centre on selection · Home capital ·
    /// F1 keys · Esc close panel / deselect · F5/F9 quick save/load. (WASD/QE belong to the camera.)
    /// </summary>
    public sealed class GameController : MonoBehaviour
    {
        HexMapRenderer _map;
        CameraRig _rig;
        MarkerLayer _markers;
        MeshCollider _mapCollider;
        int _fogVersion = -1;
        int _mapVersion = -1;
        int _techKnown = -1;
        HexCoord? _routeFor;
        bool _leftPressOverUi;

        public GameState Game { get; private set; }
        public TurnManager Turns { get; private set; }
        public HexMapRenderer MapRenderer => _map;
        public Camera Camera => _rig != null ? _rig.Camera : null;

        /// <summary>A nuclear weapon waiting for its target: the next map click launches it (Esc cancels).</summary>
        public (City city, Unit weapon)? NukeTargeting { get; private set; }

        public Army SelectedArmy { get; private set; }
        public City SelectedCity { get; private set; }
        public Unit SelectedUnit { get; private set; }
        public Player SelectedCityState { get; private set; }
        public HudPanel OpenPanel { get; private set; }
        public HexCoord? HoverHex { get; private set; }
        public ArmyPath HoverRoute { get; private set; }

        /// <summary>Newest last. The HUD shows the most recent few; the log (N) shows them all.</summary>
        public List<Notice> Notices { get; } = new List<Notice>();

        /// <summary>Armies told to skip this turn (Space); cleared when the player's next turn starts.</summary>
        readonly HashSet<int> _skipped = new HashSet<int>();

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
            Post("Left-click your army to select it, right-click a hex to march there. Drag to pan, wheel to zoom, F1 lists every control.");
        }

        void HookEvents()
        {
            Turns.TurnStarted += p =>
            {
                if (p.IsAI) return;
                _skipped.Clear();
                Autosave();
            };
            Game.ProductionCompleted += (city, item) =>
            {
                if (city.OwnerId != HumanPlayer?.Id) return;
                string next = city.CurrentProduction.HasValue ? $" Next: {EconomyRules.NameOf(Game, city.CurrentProduction.Value)}." : "";
                Post($"{city.Name} completed {EconomyRules.NameOf(Game, item)}.{next}", NoticeKind.Good, city.Position);
            };
            Game.ArmyWoke += (a, threat) =>
            {
                if (a.OwnerId != HumanPlayer?.Id) return;
                Post(threat ? "Enemies sighted! A sleeping army woke up." : "An army has fully healed and awaits orders.",
                    threat ? NoticeKind.War : NoticeKind.Info, a.Position);
            };
            Game.BattleStarted += b => Post($"Battle: {b.Attacker.Player.Name} vs {b.Defender.Player.Name}.", NoticeKind.War, b.Defender.Origin);
            Game.BattleEnded += b => Post($"Battle over — {Describe(b)}.", b.Winner.HasValue && Side(b, b.Winner.Value).Player == HumanPlayer ? NoticeKind.Good : NoticeKind.Bad);
            Game.ArmySunk += a => Post($"An embarked army ({a.Count} units) was sunk at sea!", NoticeKind.Bad);
            Game.ExplorationFinished += a =>
            {
                if (a.OwnerId == Human.Id) Post($"{(a.IsNaval ? "A fleet" : "An explorer")} has nothing left to explore nearby and awaits orders.", NoticeKind.Info, a.Position);
            };
            Game.GreatPersonBorn += (p, u) => { if (!p.IsAI) Post($"A {u.Def.Name} is born! Select them to use their gift.", NoticeKind.Good); };
            Game.ReligionFounded += r => Post($"{r.Name} has been founded by {Game.Player(r.FounderId).Name}.");
            Game.WarDeclared += (a, b) => Post($"War: {a.Name} against {b.Name}!", NoticeKind.War);
            Game.PeaceMade += (a, b) => Post($"Peace between {a.Name} and {b.Name}.", NoticeKind.Good);
            Game.NuclearStrikeLaunched += s =>
            {
                var who = Game.Player(s.PlayerId);
                string cities = s.Cities.Count == 0 ? "" : " " + string.Join(", ", s.Cities.Select(c => $"{c.city.Name} lost {c.lost} citizens")) + ".";
                Post($"NUCLEAR STRIKE: {who.Name} used {(s.Weapon.StartsWith("A") ? "an" : "a")} {s.Weapon}. {s.UnitsDestroyed} units destroyed, {s.UnitsDamaged} damaged.{cities} " +
                     $"Fallout lingers for {GameState.FalloutTurns} turns.", NoticeKind.War, s.Target);
                _markers.Detonate(s.Target, s.Radius);
                if (Viewer.IsExplored(s.Target)) FocusOn(s.Target);
            };
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

        public void Post(string text, NoticeKind kind = NoticeKind.Info, HexCoord? at = null)
        {
            Notices.Add(new Notice { Text = text, Kind = kind, Turn = Game?.Turn ?? 0, Time = UnityEngine.Time.time, At = at });
            if (Notices.Count > 200) Notices.RemoveAt(0);
        }

        /// <summary>Moves the camera to a hex (from a notice, the next-unit button or the C/Home keys).</summary>
        public void FocusOn(HexCoord hex) => _rig.FocusOn(_map.HexToWorld(hex));

        public void OpenNotice(Notice notice)
        {
            if (!notice.At.HasValue) return;
            FocusOn(notice.At.Value);
            if (Game.CityAt(notice.At.Value) is City c && c.OwnerId == Human.Id) OpenCity(c);
            else if (Game.ArmyAt(notice.At.Value) is Army a && a.OwnerId == Human.Id) SelectArmy(a);
        }

        // ------------------------------------------------------------------ frame

        void Update()
        {
            if (Game == null) return;
            var battle = HumanBattle;
            HoverHex = PickHex(out var h) ? h : (HexCoord?)null;
            HandleKeys(battle);
            if (Game == null) return; // a load may have replaced the game this frame

            // Left-click (released without dragging the map) selects; right-click orders.
            bool overUi = IsPointerOverUi();
            if (Input.GetMouseButtonDown(0)) _leftPressOverUi = overUi;
            if (!Turns.IsGameOver && Input.GetMouseButtonUp(0) && !_leftPressOverUi && !overUi && !_rig.WasDragged)
            {
                if (!HoverHex.HasValue) Deselect();
                else if (battle != null) BattleSelect(battle, HoverHex.Value);
                else WorldSelect(HoverHex.Value);
            }
            if (!Turns.IsGameOver && Input.GetMouseButtonDown(1) && !overUi)
            {
                if (battle != null) { if (HoverHex.HasValue) BattleOrder(battle, HoverHex.Value); }
                else WorldOrder(HoverHex);
            }

            if (SelectedArmy != null && Game.Army(SelectedArmy.Id) == null) SelectedArmy = null;
            if (SelectedCity != null && SelectedCity.OwnerId != Human.Id) { SelectedCity = null; if (OpenPanel == HudPanel.City) OpenPanel = HudPanel.None; }
            if (SelectedUnit != null && !SelectedUnit.IsAlive) SelectedUnit = null;
            if (OpenPanel == HudPanel.Siege && BesiegedBySelection() == null) OpenPanel = HudPanel.None;

            if (NukeTargeting is { } aim && (aim.city.OwnerId != Human.Id || !aim.city.AirUnits.Contains(aim.weapon)))
                NukeTargeting = null;

            var viewer = Viewer;
            int techKnown = HumanPlayer?.Tech.Researched.Count ?? 0;
            if (viewer.Version != _fogVersion || Game.MapVersion != _mapVersion || techKnown != _techKnown)
            {
                _map.ApplyFog(viewer, MarkerLayer.ColorOf);
                _markers.SyncTerrainMarkers(Game, viewer, HumanPlayer); // uranium shows once Atomic Theory is known
                _fogVersion = viewer.Version;
                _mapVersion = Game.MapVersion;
                _techKnown = techKnown;
            }

            // Route preview for the selected army (cached per hovered hex).
            if (battle == null && NukeTargeting == null && SelectedArmy != null && HoverHex.HasValue && !IsPointerOverUi())
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
            _markers.ShowHighlights(battle != null ? BattleHighlights(battle) : NukeTargeting != null ? NukeHighlights() : RouteHighlights());
        }

        void HandleKeys(Battle battle)
        {
            if (Input.GetKeyDown(KeyCode.F5)) QuickSave();
            if (Input.GetKeyDown(KeyCode.F9)) { QuickLoad(); return; }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (NukeTargeting != null) CancelNukeTargeting();
                else if (OpenPanel != HudPanel.None) ClosePanel();
                else Deselect();
            }
            if (Input.GetKeyDown(KeyCode.F1)) TogglePanel(HudPanel.Help);
            if (Input.GetKeyDown(KeyCode.N)) TogglePanel(HudPanel.Log);
            if (Input.GetKeyDown(KeyCode.Home)) FocusCapital();
            if (Input.GetKeyDown(KeyCode.C)) FocusSelection();
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
            else if (Input.GetKeyDown(KeyCode.Tab)) SelectNextIdle();
            else if (Input.GetKeyDown(KeyCode.Space)) SkipTurn();
            else if (Input.GetKeyDown(KeyCode.Z)) ToggleStance(ArmyStance.Sentry);
            else if (Input.GetKeyDown(KeyCode.H)) ToggleStance(ArmyStance.Heal);
            else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) EndTurn();
                else NextAction();
            }
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

        /// <summary>While aiming a nuke: the edge of its range, and the blast under the cursor (red if allowed, grey if not).</summary>
        IEnumerable<(HexCoord, Color)> NukeHighlights()
        {
            if (!(NukeTargeting is { } aim)) yield break;
            var (city, weapon) = aim;
            foreach (var t in Game.Map.Tiles)
                if (t.Coord.DistanceTo(city.Position) == weapon.Def.Range)
                    yield return (t.Coord, new Color(1f, 0.85f, 0.2f, 0.3f));
            if (!HoverHex.HasValue || IsPointerOverUi()) yield break;
            bool ok = Game.NukeBlocker(city, weapon, HoverHex.Value) == null;
            foreach (var h in Game.BlastArea(HoverHex.Value, weapon.Def.BlastRadius))
                yield return (h, ok ? new Color(1f, 0.2f, 0.1f, h == HoverHex.Value ? 0.6f : 0.4f) : new Color(0.5f, 0.5f, 0.5f, 0.35f));
        }

        /// <summary>Why the hovered hex can't be nuked, or null (the HUD shows it in the tooltip).</summary>
        public string NukeHoverBlocker() =>
            NukeTargeting is { } aim && HoverHex.HasValue ? Game.NukeBlocker(aim.city, aim.weapon, HoverHex.Value) : null;

        public void BeginNukeTargeting(City city, Unit weapon)
        {
            if (city.OwnerId != Human.Id || !weapon.Def.IsNuclear) return;
            NukeTargeting = (city, weapon);
            SelectedArmy = null;
            OpenPanel = HudPanel.None;
            FocusOn(city.Position);
            Post($"{weapon.Def.Name} armed in {city.Name}: left-click a target within {weapon.Def.Range} hexes (right-click or Esc cancels).", NoticeKind.War, city.Position);
        }

        public void CancelNukeTargeting()
        {
            if (NukeTargeting == null) return;
            NukeTargeting = null;
            Post("Launch cancelled.");
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

        // ------------------------------------------------------------------ what needs attention

        /// <summary>
        /// Armies still waiting for orders: they can move, and aren't marching, exploring, working,
        /// sleeping, healing, skipped this turn or sitting as a city garrison.
        /// </summary>
        public List<Army> IdleArmies() =>
            Game.Armies.Where(a => a.OwnerId == Human.Id && !a.InBattle && a.WorldMovesLeft > 0 && !a.Destination.HasValue &&
                                   a.BuildOrder == ImprovementType.None && !a.AutomatedWorkers && !a.AutoExplore &&
                                   a.Stance == ArmyStance.Awake && !_skipped.Contains(a.Id) && Game.CityAt(a.Position) == null)
                .OrderBy(a => a.Id).ToList();

        public City CityWithoutProduction() =>
            Game.Cities.Where(c => c.OwnerId == Human.Id && !c.CurrentProduction.HasValue).OrderBy(c => c.Id).FirstOrDefault();

        public TurnBlocker Blocker()
        {
            if (Human.Tech.CurrentResearch == null && Human.Tech.Available().Any()) return TurnBlocker.ChooseResearch;
            if (CityWithoutProduction() != null) return TurnBlocker.ChooseProduction;
            if (IdleArmies().Count > 0) return TurnBlocker.UnitNeedsOrders;
            return TurnBlocker.None;
        }

        /// <summary>The End Turn button: deal with the next thing needing attention, or end the turn.</summary>
        public void NextAction()
        {
            switch (Blocker())
            {
                case TurnBlocker.ChooseResearch: OpenPanel = HudPanel.Tech; break;
                case TurnBlocker.ChooseProduction:
                    var city = CityWithoutProduction();
                    OpenCity(city);
                    FocusOn(city.Position);
                    break;
                case TurnBlocker.UnitNeedsOrders: SelectNextIdle(); break;
                default: EndTurn(); break;
            }
        }

        /// <summary>Selects (and centres on) the next army waiting for orders, cycling after the current one.</summary>
        public void SelectNextIdle()
        {
            var idle = IdleArmies();
            if (idle.Count == 0) { Post("No units need orders."); return; }
            var next = idle.FirstOrDefault(a => SelectedArmy != null && a.Id > SelectedArmy.Id) ?? idle[0];
            SelectArmy(next);
            FocusOn(next.Position);
        }

        /// <summary>Space: leave this army be for the rest of the turn.</summary>
        public void SkipTurn()
        {
            if (SelectedArmy == null) { SelectNextIdle(); return; }
            _skipped.Add(SelectedArmy.Id);
            SelectNextOrDeselect();
        }

        /// <summary>Z sleeps (wakes when enemies come near), H heals until whole; pressing again wakes.</summary>
        public void ToggleStance(ArmyStance stance)
        {
            if (SelectedArmy == null) return;
            if (SelectedArmy.Stance == stance)
            {
                SelectedArmy.Stance = ArmyStance.Awake;
                Post("The army is awake.");
                return;
            }
            if (!Game.SetStance(SelectedArmy, stance))
            {
                Post(stance == ArmyStance.Heal ? "Already at full health." : "Can't do that during a battle.", NoticeKind.Bad);
                return;
            }
            Post(stance == ArmyStance.Heal
                ? $"Healing: +{Game.HealRate(SelectedArmy)} HP a turn here (25 in your cities, 15 in your land, 10 neutral, 5 foreign)."
                : "Sleeping until enemies come near.");
            SelectNextOrDeselect();
        }

        void SelectNextOrDeselect()
        {
            var idle = IdleArmies();
            if (idle.Count > 0) SelectNextIdle();
            else Deselect();
        }

        public void FocusSelection()
        {
            if (SelectedArmy != null) FocusOn(SelectedArmy.Position);
            else if (SelectedCity != null) FocusOn(SelectedCity.Position);
        }

        public void FocusCapital()
        {
            var capital = Game.Cities.FirstOrDefault(c => c.OwnerId == Human.Id && EconomyRules.IsCapital(Game, c));
            if (capital != null) { FocusOn(capital.Position); OpenCity(capital); }
        }

        /// <summary>Forecast for attacking the hovered hex with the selected army, if it holds an enemy at war.</summary>
        public BattleForecast HoverForecast()
        {
            if (SelectedArmy == null || !HoverHex.HasValue || HumanBattle != null) return null;
            var hex = HoverHex.Value;
            int owner = Game.ArmyAt(hex)?.OwnerId ?? Game.CityAt(hex)?.OwnerId ?? -1;
            if (owner < 0 || owner == Human.Id || !Game.AtWar(owner, Human.Id) || !Viewer.IsVisible(hex)) return null;
            return BattleForecast.Estimate(Game, SelectedArmy, hex);
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
            if (!Human.Tech.CanResearch(techId)) { SetResearchTarget(techId); return; }
            Human.Tech.SetResearch(techId);
            Post($"Researching {Game.Content.Tech(techId).Name}.");
        }

        /// <summary>Heads for a distant tech: its prerequisites are researched first, in order.</summary>
        public void SetResearchTarget(string techId)
        {
            Human.Tech.SetTarget(techId);
            if (Human.Tech.Target == null) return;
            int steps = Human.Tech.PathTo(techId).Count;
            Post($"Research goal: {Game.Content.Tech(techId).Name} ({steps} techs). Now researching {Game.Content.Tech(Human.Tech.CurrentResearch).Name}.");
        }

        public void SetProduction(City city, ProductionItem item)
        {
            if (Game.SetProduction(city, item)) Post($"{city.Name} will build {EconomyRules.NameOf(Game, item)}.");
        }

        /// <summary>Adds to the city's build queue (Shift+click Build, or the + button).</summary>
        public void Enqueue(City city, ProductionItem item)
        {
            if (Game.EnqueueProduction(city, item)) Post($"{EconomyRules.NameOf(Game, item)} queued in {city.Name}.");
            else Post(city.Queue.Count >= City.MaxQueue ? "The queue is full." : "Already queued.", NoticeKind.Bad);
        }

        public void Unqueue(City city, int index) => Game.RemoveQueued(city, index);

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

        /// <summary>Left-click on the map: select (or, while aiming a nuclear weapon, fire it).</summary>
        void WorldSelect(HexCoord hex)
        {
            if (NukeTargeting is { } aim)
            {
                string blocker = Game.NukeBlocker(aim.city, aim.weapon, hex);
                if (blocker != null) { Post(blocker, NoticeKind.Bad); return; }
                NukeTargeting = null;
                Game.LaunchNuke(aim.city, aim.weapon, hex); // reported through NuclearStrikeLaunched
                return;
            }

            var clickedCity = Game.CityAt(hex);
            var army = Game.ArmyAt(hex);
            if (army != null && army.OwnerId == Human.Id)
            {
                SelectArmy(army);
                if (clickedCity != null && clickedCity.OwnerId == Human.Id) OpenCity(clickedCity);
                return;
            }
            if (clickedCity != null && clickedCity.OwnerId == Human.Id) { Deselect(); OpenCity(clickedCity); return; }
            if (clickedCity != null && Game.Player(clickedCity.OwnerId).IsCityState)
            {
                SelectedCityState = Game.Player(clickedCity.OwnerId);
                OpenPanel = HudPanel.CityState;
                return;
            }
            // Empty ground (or someone else's army): let go of the selection, as in Humankind.
            Deselect();
        }

        /// <summary>
        /// Right-click on the map with an army selected: march, attack an adjacent enemy, or join a
        /// battle. Without a selection it closes the open panel; while aiming a nuke it cancels.
        /// </summary>
        void WorldOrder(HexCoord? target)
        {
            if (NukeTargeting != null) { CancelNukeTargeting(); return; }
            if (SelectedArmy == null || !target.HasValue)
            {
                if (OpenPanel != HudPanel.None) ClosePanel();
                return;
            }
            var hex = target.Value;
            var clickedCity = Game.CityAt(hex);
            var army = Game.ArmyAt(hex);

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

        /// <summary>Left-click in battle: pick one of your units (while deploying, a click on your zone also places it).</summary>
        void BattleSelect(Battle battle, HexCoord hex)
        {
            var unit = battle.UnitAt(hex);
            if (unit != null && battle.SideOf(unit) == battle.ActiveSide) { SelectedUnit = unit; return; }
            if (battle.Status == BattleStatus.Deploying && SelectedUnit != null) battle.Redeploy(SelectedUnit, hex);
        }

        /// <summary>Right-click in battle: the selected unit moves to the hex or attacks the enemy on it.</summary>
        void BattleOrder(Battle battle, HexCoord hex)
        {
            var unit = battle.UnitAt(hex);
            if (SelectedUnit == null) return;
            if (battle.Status == BattleStatus.Deploying)
            {
                if (!battle.Redeploy(SelectedUnit, hex)) Post("Place units inside your blue deployment zone.", NoticeKind.Bad);
                return;
            }
            if (unit != null && battle.SideOf(unit) == battle.ActiveSide) { SelectedUnit = unit; return; }

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
        static string AutosaveDir => System.IO.Path.Combine(Application.persistentDataPath, "autosaves");

        /// <summary>Rolling autosaves kept (one per turn, newest first).</summary>
        public const int AutosavesKept = 5;

        public bool HasQuickSave => System.IO.File.Exists(SavePath);

        public void QuickSave()
        {
            System.IO.File.WriteAllBytes(SavePath, SaveGame.Save(Game, Turns));
            Post("Game saved.", NoticeKind.Good);
        }

        /// <summary>Written at the start of each of the player's turns; the oldest beyond <see cref="AutosavesKept"/> are deleted.</summary>
        void Autosave()
        {
            try
            {
                System.IO.Directory.CreateDirectory(AutosaveDir);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(AutosaveDir, $"autosave_T{Game.Turn:D4}.crucible"), SaveGame.Save(Game, Turns));
                foreach (var old in Autosaves().Skip(AutosavesKept)) System.IO.File.Delete(old.path);
            }
            catch (System.IO.IOException e)
            {
                Post($"Autosave failed: {e.Message}", NoticeKind.Bad);
            }
        }

        /// <summary>Autosaves, newest first, with their turn numbers.</summary>
        public List<(string path, int turn)> Autosaves()
        {
            if (!System.IO.Directory.Exists(AutosaveDir)) return new List<(string, int)>();
            return System.IO.Directory.GetFiles(AutosaveDir, "autosave_T*.crucible")
                .Select(p => (p, turn: int.TryParse(System.IO.Path.GetFileNameWithoutExtension(p).Substring("autosave_T".Length), out var t) ? t : 0))
                .OrderByDescending(x => x.turn).ToList();
        }

        public void QuickLoad()
        {
            if (!HasQuickSave) { Post("No quicksave yet (F5 to save).", NoticeKind.Bad); return; }
            LoadFrom(SavePath);
        }

        public void LoadFrom(string path)
        {
            try
            {
                var (game, turns) = SaveGame.Load(System.IO.File.ReadAllBytes(path), new Crucible.Core.AI.StrategicAI());
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
                _skipped.Clear();
                HookEvents();
                Post($"Loaded turn {game.Turn}.", NoticeKind.Good);
                GameReplaced?.Invoke();
            }
            catch (Exception e) when (e is System.IO.InvalidDataException || e is System.IO.IOException)
            {
                Post($"Couldn't load: {e.Message}", NoticeKind.Bad);
            }
        }
    }
}
