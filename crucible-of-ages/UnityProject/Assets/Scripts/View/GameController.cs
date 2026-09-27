using System.Linq;
using System.Text;
using Crucible.Core.Combat;
using Crucible.Core.Economy;
using Crucible.Core.Empire;
using Crucible.Core.Game;
using Crucible.Core.Hex;
using Crucible.Core.Units;
using UnityEngine;

namespace Crucible.View
{
    /// <summary>
    /// Input + HUD. World mode: click your army, then click a hex to march there, an adjacent enemy to
    /// attack, or a hex of an ongoing battle to reinforce it. Deployment: click a unit, then a hex of
    /// your zone. Battle: click a unit, then a green hex to move or a red enemy to attack; hover an
    /// enemy for the combat breakdown.
    /// Keys: Enter = end world turn, Space = confirm deployment / end battle turn, R = retreat,
    /// A = auto-resolve the current round.
    /// </summary>
    public sealed class GameController : MonoBehaviour
    {
        GameState _game;
        TurnManager _turns;
        HexMapRenderer _map;
        CameraRig _rig;
        MarkerLayer _markers;
        MeshCollider _mapCollider;

        Army _selectedArmy;
        City _selectedCity;
        Unit _selectedUnit;
        int _mapVersion = -1;
        Vector2 _cityScroll;

        /// <summary>Screen areas drawn by OnGUI last frame; clicks there don't reach the map.</summary>
        readonly System.Collections.Generic.List<Rect> _uiRects = new System.Collections.Generic.List<Rect>();

        bool MouseOverUI()
        {
            var p = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            return _uiRects.Any(r => r.Contains(p));
        }
        HexCoord? _hover;
        ArmyPath _hoverPath;
        HexCoord? _hoverPathFor;
        int _fogVersion = -1;
        string _message = "Select your army (blue) and click a hex to move. Enter ends the turn.";

        public void Init(GameState game, TurnManager turns, HexMapRenderer map, CameraRig rig)
        {
            _game = game;
            _turns = turns;
            _map = map;
            _rig = rig;
            _mapCollider = map.GetComponent<MeshCollider>();
            _markers = new GameObject("Markers").AddComponent<MarkerLayer>();
            _markers.Init(map);
            _game.BattleStarted += b => _message = $"Battle! Round {b.Round}. Your units deploy in the tinted zone.";
            _game.BattleEnded += b => _message = $"Battle over: {b.Status}.";
        }

        Player Human => _turns.ActivePlayer;

        /// <summary>Fog is drawn from the (single) human player's point of view.</summary>
        PlayerVisibility Viewer => _game.Visibility(_game.Players.FirstOrDefault(p => !p.IsAI)?.Id ?? 0);

        /// <summary>A battle waiting on a human decision, if any.</summary>
        Battle HumanBattle => _game.Battles.FirstOrDefault(b => b.AwaitingAction && !b.Active.Player.IsAI);

        void Update()
        {
            if (_game == null) return;
            var battle = HumanBattle;
            _hover = PickHex(out var h) ? h : (HexCoord?)null;

            if (!_turns.IsGameOver)
            {
                if (Input.GetMouseButtonDown(0) && _hover.HasValue && !MouseOverUI())
                {
                    ClearRoutePreview();
                    if (battle != null) BattleClick(battle, _hover.Value);
                    else WorldClick(_hover.Value);
                }

                if (battle != null)
                {
                    bool deploying = battle.Status == BattleStatus.Deploying;
                    if (Input.GetKeyDown(KeyCode.Space))
                    {
                        if (deploying) battle.ConfirmDeployment();
                        else battle.EndTurn();
                        _selectedUnit = null;
                        AfterBattleAction(battle);
                    }
                    else if (Input.GetKeyDown(KeyCode.R) && !deploying) { battle.Retreat(); AfterBattleAction(battle); }
                    else if (Input.GetKeyDown(KeyCode.A))
                    {
                        _game.AutoResolveRound(battle);
                        _selectedUnit = null;
                        _message = battle.IsFinished ? $"Auto-resolved: {battle.Status}." : "Round auto-resolved.";
                    }
                }
                else if (Input.GetKeyDown(KeyCode.F) && _selectedArmy != null)
                {
                    var city = _game.FoundCityWithSettler(_selectedArmy);
                    _message = city != null
                        ? $"Founded {city.Name}!"
                        : "Can't settle here: need a settler with moves, land, 4+ hexes from other cities, not in foreign borders.";
                    if (city != null) _selectedCity = city;
                }
                else if (Input.GetKeyDown(KeyCode.T))
                {
                    CycleResearch();
                }
                else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                {
                    _selectedArmy = null;
                    ClearRoutePreview();
                    _turns.EndTurn();
                    _message = _turns.IsGameOver ? $"Game over — {_game.Victory}" : $"Turn {_game.Turn}.";
                }
            }

            if (_selectedArmy != null && _game.Army(_selectedArmy.Id) == null) _selectedArmy = null;
            if (_selectedCity != null && _selectedCity.OwnerId != Human.Id) _selectedCity = null;
            if (_selectedUnit != null && !_selectedUnit.IsAlive) _selectedUnit = null;

            var viewer = Viewer;
            if (viewer.Version != _fogVersion || _game.MapVersion != _mapVersion)
            {
                _map.ApplyFog(viewer, MarkerLayer.ColorOf);
                _fogVersion = viewer.Version;
                _mapVersion = _game.MapVersion;
            }

            // Path preview for the selected army (A* is cheap at this map size; cache per hovered hex).
            if (battle == null && _selectedArmy != null && _hover.HasValue)
            {
                if (_hoverPathFor != _hover)
                {
                    _hoverPath = Pathfinder.Find(_game, _selectedArmy, _hover.Value);
                    _hoverPathFor = _hover;
                }
            }
            else ClearRoutePreview();

            var shown = battle ?? _game.Battles.FirstOrDefault(b => b.Attacker.Player == Human || b.Defender.Player == Human);
            _markers.Sync(_game, viewer, shown, _selectedArmy, _selectedUnit);
            _markers.ShowHighlights(battle != null ? BattleHighlights(battle) : Enumerable.Empty<(HexCoord, Color)>());
        }

        /// <summary>Deployment zone while deploying; reachable hexes (green) and targets (red) for the selected unit.</summary>
        System.Collections.Generic.IEnumerable<(HexCoord, Color)> BattleHighlights(Battle battle)
        {
            if (battle.Status == BattleStatus.Deploying)
            {
                foreach (var h in battle.Active.DeploymentZone) yield return (h, new Color(0.3f, 0.6f, 1f, 0.35f));
                yield break;
            }
            if (_selectedUnit == null || battle.PositionOf(_selectedUnit) == null) yield break;

            var from = battle.PositionOf(_selectedUnit).Value;
            foreach (var h in battle.ReachableHexes(_selectedUnit).Keys) yield return (h, new Color(0.3f, 1f, 0.4f, 0.35f));
            foreach (var enemy in battle.DeployedUnits(battle.Opponent(battle.ActiveSide).Id))
            {
                var pos = battle.PositionOf(enemy).Value;
                bool meleeReady = _selectedUnit.Def.IsRanged || _selectedUnit.BattleMovesLeft > 0;
                if (meleeReady && battle.CanAttackFrom(_selectedUnit, from, pos)) yield return (pos, new Color(1f, 0.25f, 0.2f, 0.5f));
            }
        }

        void ClearRoutePreview()
        {
            _hoverPath = null;
            _hoverPathFor = null;
        }

        bool PickHex(out HexCoord hex)
        {
            hex = default;
            if (_rig == null || _rig.Camera == null) return false;
            var ray = _rig.Camera.ScreenPointToRay(Input.mousePosition);
            if (!_mapCollider.Raycast(ray, out var hit, 2000f)) return false;
            hex = _map.WorldToHex(hit.point);
            return _game.Map.InBounds(hex);
        }

        // ------------------------------------------------------------------ world map

        void CycleResearch()
        {
            var options = Human.Tech.Available().OrderBy(t => t.ScienceCost).ThenBy(t => t.Id).ToList();
            if (options.Count == 0) return;
            int i = options.FindIndex(t => t.Id == Human.Tech.CurrentResearch);
            var next = options[(i + 1) % options.Count];
            Human.Tech.SetResearch(next.Id);
            _message = $"Researching {next.Name} ({next.ScienceCost} science).";
        }

        void WorldClick(HexCoord hex)
        {
            var clickedCity = _game.CityAt(hex);
            if (clickedCity != null && clickedCity.OwnerId == Human.Id) _selectedCity = clickedCity;
            else if (_selectedArmy == null) _selectedCity = null;

            var army = _game.ArmyAt(hex);
            if (army != null && army.OwnerId == Human.Id)
            {
                _selectedArmy = army;
                _message = $"{army.Count}/{Human.ArmyCap} units, {army.WorldMovesLeft} moves left.";
                return;
            }
            if (_selectedArmy == null) return;

            var ongoing = _game.BattleCovering(hex);
            if (ongoing != null)
            {
                _message = _game.JoinBattle(_selectedArmy, ongoing)
                    ? "Reinforcing! Your units march in from this side at the start of your next battle turn."
                    : "Can't join: stand next to the battlefield with moves left.";
                return;
            }

            var city = _game.CityAt(hex);
            bool enemyThere = (army != null && _game.AtWar(army.OwnerId, Human.Id)) ||
                              (city != null && _game.AtWar(city.OwnerId, Human.Id));
            if (enemyThere)
            {
                if (_selectedArmy.Position.DistanceTo(hex) != 1) { _message = "Move next to the enemy first."; return; }
                var battle = _game.Attack(_selectedArmy, hex);
                if (battle == null) _message = "Can't attack (no moves left, or already fighting).";
                return;
            }

            var path = _game.OrderMove(_selectedArmy, hex);
            if (path == null) _message = "No route there (water, mountains, cliffs or blocked).";
            else if (_selectedArmy.Position == hex) _message = "Arrived.";
            else _message = $"Marching: arrives in {path.Turns} turn(s). The order continues automatically.";
        }

        // ------------------------------------------------------------------ tactical battle

        void BattleClick(Battle battle, HexCoord hex)
        {
            var unit = battle.UnitAt(hex);
            if (battle.Status == BattleStatus.Deploying)
            {
                if (_selectedUnit != null && battle.Redeploy(_selectedUnit, hex)) _message = "Redeployed.";
                else if (unit != null && battle.SideOf(unit) == battle.ActiveSide) _selectedUnit = unit;
                return;
            }
            if (unit != null && battle.SideOf(unit) == battle.ActiveSide)
            {
                _selectedUnit = unit;
                _message = $"{unit.Def.Name}: {unit.Hp} HP, {unit.BattleMovesLeft} MP{(unit.HasAttacked ? ", attacked" : "")}.";
                return;
            }
            if (_selectedUnit == null) return;

            if (unit != null)
            {
                var result = battle.TryAttack(_selectedUnit, hex);
                _message = result == null
                    ? "Out of range, no line of sight, or no moves left."
                    : $"Hit for {result.DamageToDefender}" + (result.DamageToAttacker > 0 ? $", took {result.DamageToAttacker}." : ".");
            }
            else if (!battle.TryMove(_selectedUnit, hex))
            {
                _message = "Can't move there.";
            }
            AfterBattleAction(battle);
        }

        void AfterBattleAction(Battle battle)
        {
            // Hands control to the AI side if it is now active, and applies the result when the battle ends.
            _game.AdvanceAIBattleTurns(battle);
            if (battle.IsFinished) _game.ResolveBattle(battle);
            if (battle.Status == BattleStatus.AwaitingNextRound)
                _message = $"Round {battle.Round} over. The next round starts on the attacker's next turn.";
        }

        // ------------------------------------------------------------------ HUD

        void OnGUI()
        {
            if (_game == null) return;
            if (Event.current.type == EventType.Layout) _uiRects.Clear();
            var sb = new StringBuilder();
            sb.AppendLine($"Turn {_game.Turn}  —  {Human.Name} ({Human.Faction.Name})  —  army cap {Human.ArmyCap}");
            if (_turns.IsGameOver) sb.AppendLine($"GAME OVER: {_game.Victory}");

            var income = EconomyRules.EmpireIncome(_game, Human);
            var research = Human.Tech.CurrentResearch != null ? _game.Content.Tech(Human.Tech.CurrentResearch) : null;
            sb.AppendLine($"Gold {Human.Gold} ({income.Gold:+#;-#;0})   Science +{income.Science}   Culture +{income.Culture}   Happiness {EconomyRules.Happiness(_game, Human)}");
            sb.AppendLine(research != null
                ? $"Research: {research.Name} {Human.Tech.Progress}/{research.ScienceCost}  (T to change)"
                : "Research: none (T to choose)");

            var battle = HumanBattle;
            if (battle != null)
            {
                if (battle.Status == BattleStatus.Deploying)
                {
                    sb.AppendLine($"DEPLOYMENT ({battle.ActiveSide}): click a unit, then a blue hex. Space confirms.");
                }
                else
                {
                    sb.AppendLine($"BATTLE  round {battle.Round}/{Battle.MaxRounds}, turn {battle.TurnInRound}/{Battle.TurnsPerRound}, {battle.ActiveSide} to act");
                    sb.AppendLine("Space: end battle turn   R: retreat   A: auto-resolve round");
                }
                sb.AppendLine($"Reserves: you {battle.Active.Reserve.Count} / enemy {battle.Opponent(battle.ActiveSide).Reserve.Count}");
                AppendPreview(sb, battle);
            }
            else
            {
                sb.AppendLine("Enter: end turn   WASD: pan   Q/E: rotate   Wheel: zoom");
                if (_selectedArmy != null)
                {
                    sb.AppendLine($"Army: {_selectedArmy.Count}/{Human.ArmyCap} units, {_selectedArmy.WorldMovesLeft} moves" +
                                  (_selectedArmy.Destination.HasValue ? $", marching to {_selectedArmy.Destination.Value}" : ""));
                    if (_hoverPath != null) sb.AppendLine($"Route: {_hoverPath.Steps.Count} hexes, {_hoverPath.Turns} turn(s)");
                    if (_selectedArmy.Units.Any(u => u.Def.Id == Crucible.Core.Content.DefaultContent.SettlerUnit))
                        sb.AppendLine("F: found a city here");
                }
            }

            if (_hover.HasValue && _game.Map.Get(_hover.Value) is Crucible.Core.World.Tile t)
                sb.AppendLine($"Hex {t}");
            sb.AppendLine(_message);

            var hud = new Rect(10, 10, 520, 24 + 18 * sb.ToString().Split('\n').Length);
            if (Event.current.type == EventType.Layout) _uiRects.Add(hud);
            GUI.Box(hud, GUIContent.none);
            GUI.Label(new Rect(20, 16, 500, 800), sb.ToString());

            if (_selectedCity != null && battle == null) DrawCityPanel(_selectedCity);
        }

        /// <summary>City screen: growth, production and a clickable build list.</summary>
        void DrawCityPanel(City city)
        {
            const float width = 300f;
            var area = new Rect(Screen.width - width - 10, 10, width, Mathf.Min(560, Screen.height - 20));
            if (Event.current.type == EventType.Layout) _uiRects.Add(area);
            GUI.Box(area, GUIContent.none);
            GUILayout.BeginArea(new Rect(area.x + 10, area.y + 8, area.width - 20, area.height - 16));

            var y = EconomyRules.CityYields(_game, city);
            int surplus = EconomyRules.FoodSurplus(_game, city, y);
            GUILayout.Label($"<b>{city.Name}</b>  pop {city.Population}" + (city.IsBesieged ? "  (BESIEGED)" : ""), Rich());
            GUILayout.Label($"Yields: {y}");
            GUILayout.Label($"Food {city.FoodStored}/{EconomyRules.GrowthThreshold(city.Population)} ({surplus:+#;-#;0}/turn)");
            GUILayout.Label($"Borders: {city.CultureStored}/{EconomyRules.BorderGrowthThreshold(city)} culture");
            GUILayout.Label("Buildings: " + (city.Buildings.Count == 0 ? "none" :
                string.Join(", ", city.Buildings.Select(b => _game.Content.Building(b).Name))));

            if (city.CurrentProduction.HasValue)
            {
                var item = city.CurrentProduction.Value;
                int cost = EconomyRules.Cost(_game, item);
                int turns = y.Production > 0 ? Mathf.CeilToInt(Mathf.Max(0, cost - city.ProductionStored) / (float)y.Production) : 99;
                GUILayout.Label($"Building: {EconomyRules.NameOf(_game, item)} {city.ProductionStored}/{cost} ({turns} turns)");
            }
            else GUILayout.Label($"Building: nothing ({city.ProductionStored} stored)");

            GUILayout.Label("Choose production:");
            _cityScroll = GUILayout.BeginScrollView(_cityScroll);
            var options = _game.Content.Buildings.Select(b => ProductionItem.Building(b.Id))
                .Concat(_game.Content.Units.Select(u => ProductionItem.Unit(u.Id)))
                .Where(i => EconomyRules.CanBuild(_game, city, i))
                .OrderBy(i => i.Kind).ThenBy(i => EconomyRules.Cost(_game, i));
            foreach (var item in options)
            {
                if (GUILayout.Button($"{EconomyRules.NameOf(_game, item)}  ({EconomyRules.Cost(_game, item)})"))
                    _game.SetProduction(city, item);
            }
            GUILayout.EndScrollView();
            if (GUILayout.Button("Close")) _selectedCity = null;
            GUILayout.EndArea();
        }

        static GUIStyle Rich() => new GUIStyle(GUI.skin.label) { richText = true };

        void AppendPreview(StringBuilder sb, Battle battle)
        {
            if (_selectedUnit == null || !_hover.HasValue) return;
            var target = battle.UnitAt(_hover.Value);
            var from = battle.PositionOf(_selectedUnit);
            if (target == null || from == null || battle.SideOf(target) == battle.SideOf(_selectedUnit)) return;
            if (!battle.CanAttackFrom(_selectedUnit, from.Value, _hover.Value))
            {
                sb.AppendLine("Target not attackable from here.");
                return;
            }
            var p = battle.PreviewAttack(_selectedUnit, from.Value, target);
            sb.AppendLine($"You: {p.Attacker}");
            sb.AppendLine($"Them: {p.Defender}");
            sb.AppendLine($"Expected: deal {p.ExpectedDamageToDefender:0}, take {p.ExpectedDamageToAttacker:0}");
        }
    }
}
