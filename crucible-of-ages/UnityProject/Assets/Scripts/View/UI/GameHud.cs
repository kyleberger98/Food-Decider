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
using UnityEngine.UIElements;

namespace Crucible.View.UI
{
    /// <summary>
    /// The game HUD, built entirely in code with UI Toolkit (no USS/UXML assets):
    /// top resource bar, notification feed, right-hand context panel (city, tech, policies, diplomacy,
    /// city-state, siege), selection card, end-turn button, battle banner and action bar, combat
    /// preview, hover tooltip, and world-anchored city banners / army badges / battle health bars.
    /// Regions rebuild only when a cheap signature of what they show changes, so clicks never land
    /// on a half-rebuilt element.
    /// </summary>
    public sealed partial class GameHud : MonoBehaviour
    {
        GameController _c;
        UIDocument _doc;
        VisualElement _root;

        // Regions
        VisualElement _topStats, _topRight;
        Label _turnLabel;
        VisualElement _notices;
        VisualElement _panel;
        ScrollView _panelScroll;
        Label _panelTitle;
        VisualElement _selection;
        VisualElement _endTurn;
        VisualElement _battleBanner, _battleActions;
        VisualElement _preview;
        VisualElement _tooltip;
        VisualElement _overlay;
        VisualElement _gameOver;

        readonly Dictionary<string, string> _signatures = new Dictionary<string, string>();
        readonly Dictionary<int, VisualElement> _cityBanners = new Dictionary<int, VisualElement>();
        readonly Dictionary<int, VisualElement> _armyBadges = new Dictionary<int, VisualElement>();
        readonly Dictionary<int, VisualElement> _unitBars = new Dictionary<int, VisualElement>();

        GameState G => _c.Game;
        Player Me => _c.Human;

        public static GameHud Create(GameController controller)
        {
            var go = new GameObject("HUD");
            go.SetActive(false); // configure the document before it enables
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = settings;
            var hud = go.AddComponent<GameHud>();
            hud._c = controller;
            hud._doc = doc;
            go.SetActive(true);
            hud.Build();
            controller.IsPointerOverUi = hud.PointerOverUi;
            controller.GameReplaced += hud.OnGameReplaced;
            return hud;
        }

        // ------------------------------------------------------------------ skeleton

        void Build()
        {
            _root = _doc.rootVisualElement;
            _root.Clear();
            _root.pickingMode = PickingMode.Ignore;
            _root.style.flexGrow = 1;
            ApplyFont(_root);

            _overlay = Layer();
            _root.Add(_overlay);

            // Top bar
            var top = Ui.Box(Theme.Chrome);
            Ui.Absolute(top, 0, 0, 0);
            top.style.height = 50;
            top.style.flexDirection = FlexDirection.Row;
            top.style.alignItems = Align.Center;
            top.style.paddingLeft = 16;
            top.style.paddingRight = 12;
            top.style.borderBottomWidth = 1;
            top.style.borderBottomColor = Theme.Line;
            _turnLabel = Ui.Text("", 14, Theme.Accent, bold: true);
            _turnLabel.style.marginRight = 22;
            top.Add(_turnLabel);
            _topStats = Ui.Row();
            top.Add(_topStats);
            top.Add(Ui.Spacer());
            _topRight = Ui.Row(8);
            top.Add(_topRight);
            _root.Add(top);

            _notices = Ui.Col(6);
            Ui.Absolute(_notices, 16, 62);
            _notices.style.width = 400;
            _notices.pickingMode = PickingMode.Ignore;
            _root.Add(_notices);

            // Right-hand context panel
            _panel = Ui.Box(Theme.Panel, 0, 10);
            Ui.Absolute(_panel, null, 62, 16);
            _panel.style.width = 430;
            _panel.style.maxHeight = Length.Percent(80);
            Ui.Border(_panel, Theme.Line, 1);
            var header = Ui.Row();
            header.style.paddingLeft = 14;
            header.style.paddingRight = 8;
            header.style.paddingTop = header.style.paddingBottom = 10;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = Theme.Line;
            _panelTitle = Ui.Heading("");
            header.Add(_panelTitle);
            header.Add(Ui.Spacer());
            header.Add(Ui.Btn("×  Esc", () => _c.ClosePanel(), ButtonStyle.Ghost, size: 11));
            _panel.Add(header);
            _panelScroll = new ScrollView(ScrollViewMode.Vertical);
            _panelScroll.style.flexShrink = 1;
            Ui.Pad(_panelScroll.contentContainer, 12);
            _panel.Add(_panelScroll);
            _root.Add(_panel);

            _selection = Ui.Box(Theme.Panel, 12, 10);
            Ui.Absolute(_selection, 16, null, null, 16);
            _selection.style.width = 440;
            Ui.Border(_selection, Theme.Line, 1);
            _root.Add(_selection);

            _endTurn = Ui.Col(6);
            Ui.Absolute(_endTurn, null, null, 16, 16);
            _endTurn.style.alignItems = Align.FlexEnd;
            _endTurn.pickingMode = PickingMode.Ignore;
            _root.Add(_endTurn);

            _battleBanner = Ui.Box(Theme.Panel, 12, 10);
            Ui.Absolute(_battleBanner, null, 62);
            _battleBanner.style.left = Length.Percent(50);
            _battleBanner.style.width = 560;
            _battleBanner.style.marginLeft = -280;
            Ui.Border(_battleBanner, new Color(Theme.War.r, Theme.War.g, Theme.War.b, 0.6f), 1);
            _root.Add(_battleBanner);

            _battleActions = Ui.Row(8);
            Ui.Absolute(_battleActions, null, null, null, 18);
            _battleActions.style.left = Length.Percent(50);
            _battleActions.style.width = 620;
            _battleActions.style.marginLeft = -310;
            _battleActions.style.justifyContent = Justify.Center;
            _battleActions.pickingMode = PickingMode.Ignore; // only the buttons themselves block map clicks
            _root.Add(_battleActions);

            _preview = Ui.Box(Theme.Panel, 12, 10);
            Ui.Absolute(_preview, null, 180, 16);
            _preview.style.width = 360;
            Ui.Border(_preview, Theme.Line, 1);
            _preview.pickingMode = PickingMode.Ignore;
            _root.Add(_preview);

            _tooltip = Ui.Box(new Color(0.04f, 0.05f, 0.06f, 0.94f), 8, 6);
            Ui.Absolute(_tooltip, 0, 0);
            _tooltip.style.maxWidth = 300;
            _tooltip.pickingMode = PickingMode.Ignore;
            Ui.Border(_tooltip, Theme.Line, 1);
            _root.Add(_tooltip);

            _gameOver = Ui.Box(new Color(0, 0, 0, 0.6f));
            Ui.Absolute(_gameOver, 0, 0, 0, 0);
            _gameOver.style.alignItems = Align.Center;
            _gameOver.style.justifyContent = Justify.Center;
            _root.Add(_gameOver);

            _signatures.Clear();
            Ui.Show(_panel, false);
            Ui.Show(_battleBanner, false);
            Ui.Show(_preview, false);
            Ui.Show(_tooltip, false);
            Ui.Show(_gameOver, false);
        }

        static VisualElement Layer()
        {
            var e = new VisualElement();
            Ui.Absolute(e, 0, 0, 0, 0);
            e.pickingMode = PickingMode.Ignore;
            return e;
        }

        /// <summary>Without a theme stylesheet, text needs an explicit font: use Unity's built-in one.</summary>
        static void ApplyFont(VisualElement root)
        {
            Font font = null;
            foreach (var name in new[] { "LegacyRuntime.ttf", "Arial.ttf" })
            {
                try { font = Resources.GetBuiltinResource<Font>(name); }
                catch (System.ArgumentException) { font = null; }
                if (font != null) break;
            }
            if (font != null) root.style.unityFontDefinition = FontDefinition.FromFont(font);
        }

        void OnGameReplaced()
        {
            foreach (var d in new[] { _cityBanners, _armyBadges, _unitBars }) d.Clear();
            Build();
        }

        public bool PointerOverUi()
        {
            var panel = _root?.panel;
            if (panel == null) return false;
            var pos = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
            var picked = panel.Pick(pos);
            return picked != null;
        }

        /// <summary>Rebuilds a region only when its signature changes.</summary>
        bool Changed(string region, string signature)
        {
            if (_signatures.TryGetValue(region, out var old) && old == signature) return false;
            _signatures[region] = signature;
            return true;
        }

        // ------------------------------------------------------------------ frame

        void LateUpdate()
        {
            if (_c == null || G == null || _root == null) return;
            var battle = _c.HumanBattle;
            int n = _c.Notices.Count;
            string stamp = $"{G.Turn}|{_c.Turns.ActivePlayerIndex}|{n}|{Me.Gold}";

            UpdateTopBar(stamp);
            UpdateNotices(n);
            UpdatePanel(stamp, battle);
            UpdateSelection(stamp, battle);
            UpdateEndTurn(stamp, battle);
            UpdateBattle(stamp, battle);
            UpdatePreview(battle);
            UpdateTooltip(battle);
            UpdateOverlays(battle);
            UpdateGameOver();
        }

        // ------------------------------------------------------------------ top bar

        void UpdateTopBar(string stamp)
        {
            var income = EconomyRules.EmpireIncome(G, Me);
            int happy = EconomyRules.Happiness(G, Me);
            int policyCost = EconomyRules.PolicyCost(G, Me);
            int pending = G.Diplomacy.Pending.Count(p => p.ToId == Me.Id);
            var research = Me.Tech.CurrentResearch != null ? G.Content.Tech(Me.Tech.CurrentResearch) : null;
            string sig = $"{stamp}|{income}|{happy}|{Me.Tech.Progress}|{research?.Id}|{Me.PolicyCulture}|{pending}|{Me.Faith}|{Me.Tourism}|{_c.OpenPanel}";
            if (!Changed("top", sig)) return;

            var era = Me.Tech.Researched.Select(id => G.Content.Tech(id).Era).DefaultIfEmpty(Era.Ancient).Max();
            _turnLabel.text = $"TURN {G.Turn}  ·  {era.ToString().ToUpperInvariant()} ERA";

            _topStats.Clear();
            _topStats.Add(Ui.Stat("gold", $"{Me.Gold} ({Ui.Signed(income.Gold)})", Theme.Gold, "Treasury and net gold per turn after upkeep"));
            _topStats.Add(Ui.Stat("science", Ui.Signed(income.Science), Theme.Science));
            _topStats.Add(Ui.Stat("culture", Ui.Signed(income.Culture), Theme.Culture));
            _topStats.Add(Ui.Stat("faith", $"{Me.Faith} ({Ui.Signed(income.Faith)})", Theme.Faith, $"Next Great Prophet at {GameState.ProphetThreshold(Me)}"));
            _topStats.Add(Ui.Stat("happiness", Ui.Signed(happy), happy >= 0 ? Theme.Happiness : Theme.Bad,
                happy < 0 ? "Unhappy: growth slowed" + (happy <= -10 ? ", no growth, −3 combat strength" : "") : "Content"));
            _topStats.Add(Ui.Stat("tourism", Ui.Signed(Me.Tourism), Theme.Tourism));
            string Strat(ResourceType r) => $"{Improvements.StrategicAvailable(G, Me.Id, r) - Improvements.StrategicUsed(G, Me.Id, r)}";
            _topStats.Add(Ui.Stat("horses · iron · oil", $"{Strat(ResourceType.Horses)} · {Strat(ResourceType.Iron)} · {Strat(ResourceType.Oil)}", Theme.Production,
                "Free strategic resources (connected supply minus units using them)"));

            _topRight.Clear();
            // Research button with progress
            var researchBtn = Ui.Box(Theme.Card, 0, 6);
            researchBtn.style.paddingLeft = researchBtn.style.paddingRight = 10;
            researchBtn.style.paddingTop = researchBtn.style.paddingBottom = 4;
            researchBtn.style.width = 250;
            var rCol = Ui.Col(3);
            rCol.Put(Ui.Text(research != null ? $"Researching {research.Name}" : "Choose research", 11, research != null ? Theme.Science : Theme.Accent, bold: true));
            int turnsLeft = research != null && income.Science > 0 ? Mathf.CeilToInt((research.ScienceCost - Me.Tech.Progress) / (float)income.Science) : 0;
            var rRow = rCol.Put(Ui.Row(6));
            rRow.Put(Ui.Bar(research != null ? Me.Tech.Progress / (float)research.ScienceCost : 0, Theme.Science));
            rRow.Put(Ui.Text(research != null ? $"{turnsLeft} t" : "", 10, Theme.Muted));
            researchBtn.Add(rCol);
            researchBtn.RegisterCallback<ClickEvent>(_ => _c.TogglePanel(HudPanel.Tech));
            researchBtn.tooltip = "Tech tree (T)";
            _topRight.Put(researchBtn);

            bool policyReady = Me.PolicyCulture >= policyCost;
            _topRight.Put(Ui.Btn(policyReady ? "Policies  •" : "Policies", () => _c.TogglePanel(HudPanel.Policies),
                policyReady ? ButtonStyle.Primary : ButtonStyle.Normal));
            _topRight.Put(Ui.Btn(pending > 0 ? $"Diplomacy  ({pending})" : "Diplomacy", () => _c.TogglePanel(HudPanel.Diplomacy),
                pending > 0 ? ButtonStyle.Primary : ButtonStyle.Normal));
            _topRight.Put(Ui.Btn("Save", _c.QuickSave, ButtonStyle.Ghost));
            _topRight.Put(Ui.Btn("Load", _c.QuickLoad, ButtonStyle.Ghost, _c.HasQuickSave));
        }

        // ------------------------------------------------------------------ notifications

        const float NoticeLifetime = 14f;

        void UpdateNotices(int count)
        {
            var recent = _c.Notices.Where(x => Time.time - x.Time < NoticeLifetime).Reverse().Take(5).ToList();
            if (!Changed("notices", $"{count}|{recent.Count}")) return;
            _notices.Clear();
            foreach (var note in recent)
            {
                var card = Ui.Box(Theme.Chrome, 0, 6);
                card.style.paddingLeft = 10;
                card.style.paddingRight = card.style.paddingTop = card.style.paddingBottom = 7;
                card.style.borderLeftWidth = 3;
                card.style.borderLeftColor = note.Kind == NoticeKind.Good ? Theme.Good
                    : note.Kind == NoticeKind.Bad ? Theme.Bad
                    : note.Kind == NoticeKind.War ? Theme.War : Theme.Info;
                card.pickingMode = PickingMode.Ignore;
                var text = Ui.Text(note.Text, 12, Theme.Text, wrap: true);
                text.pickingMode = PickingMode.Ignore;
                card.Add(text);
                _notices.Put(card);
            }
        }

        // ------------------------------------------------------------------ selection card

        void UpdateSelection(string stamp, Battle battle)
        {
            var army = _c.SelectedArmy;
            var unit = _c.SelectedUnit;
            var route = _c.HoverRoute;
            string sig = battle != null
                ? $"b|{stamp}|{unit?.Id}|{unit?.Hp}|{unit?.BattleMovesLeft}|{unit?.HasAttacked}|{battle.Status}|{battle.TurnInRound}"
                : $"w|{stamp}|{army?.Id}|{army?.Count}|{army?.WorldMovesLeft}|{army?.Destination}|{army?.BuildOrder}|{army?.AutomatedWorkers}|" +
                  $"{string.Join(",", army?.Units.Select(u => u.Hp) ?? Enumerable.Empty<int>())}|{route?.Steps.Count}|{route?.Turns}";
            if (!Changed("selection", sig)) return;
            _selection.Clear();

            if (battle != null)
            {
                Ui.Show(_selection, unit != null);
                if (unit != null) BuildUnitCard(unit, battle);
                return;
            }
            Ui.Show(_selection, army != null);
            if (army == null) return;

            bool embarked = G.IsEmbarked(army);
            string kind = army.IsNaval ? "Fleet" : army.Units.All(u => !u.Def.IsMilitary) ? "Civilians" : embarked ? "Embarked army" : "Army";
            var header = _selection.Put(Ui.Row(8));
            var stripe = Ui.Box(MarkerLayer.ColorOf(army.OwnerId), 0, 2);
            stripe.style.width = 4;
            stripe.style.height = 30;
            header.Put(stripe);
            var titleCol = header.Put(Ui.Col(2));
            titleCol.Put(Ui.Heading(kind));
            titleCol.Put(Ui.Text($"{army.Units.Count(u => u.Def.GreatPerson != GreatPersonType.General)}/{Me.ArmyCap} units  ·  {army.WorldMovesLeft}/{G.WorldMovementOf(army)} moves" +
                                 (army.Destination.HasValue ? "  ·  marching" : "") +
                                 (army.BuildOrder != ImprovementType.None ? $"  ·  building {army.BuildOrder}" : ""), 11, Theme.Muted));
            _selection.Put(Ui.Divider());

            var list = _selection.Put(Ui.Col(5));
            foreach (var u in army.Units)
            {
                var row = list.Put(Ui.Row(8));
                var name = Ui.Text(u.Def.Name, 12, Theme.Text, bold: true);
                name.style.width = 150;
                row.Put(name);
                string stats = u.Def.GreatPerson != GreatPersonType.None ? u.Def.GreatPerson.ToString()
                    : !u.Def.IsMilitary ? "civilian"
                    : u.Def.IsRanged ? $"{u.Def.RangedStrength} rng · {u.Def.CombatStrength} def" : $"{u.Def.CombatStrength} str";
                var statLabel = Ui.Text(stats, 11, Theme.Muted);
                statLabel.style.width = 110;
                row.Put(statLabel);
                if (u.Def.IsMilitary) row.Put(Ui.Bar(u.Hp / (float)Unit.MaxHp, u.Hp > 60 ? Theme.Good : u.Hp > 30 ? Theme.Gold : Theme.Bad));
            }

            if (route != null)
            {
                _selection.Put(Ui.Divider());
                _selection.Put(Ui.Text($"Route: {route.Steps.Count} hexes  ·  {route.Turns} turn{(route.Turns == 1 ? "" : "s")}  —  click to march", 11, Theme.Info));
            }

            var actions = Ui.Row(6);
            actions.style.flexWrap = Wrap.Wrap;
            actions.style.marginTop = 10;
            if (army.Units.Any(u => u.Def.Id == DefaultContent.SettlerUnit))
                actions.Put(Ui.Btn("Found city  (F)", _c.FoundCity, ButtonStyle.Primary, G.CanFoundCityAt(army.Position, Me.Id) && army.WorldMovesLeft > 0));
            if (WorkerAutomation.HasWorker(army))
            {
                var best = Improvements.Best(Me, G.Map.Get(army.Position));
                actions.Put(Ui.Btn(best != ImprovementType.None ? $"Build {best}  (I)" : "Nothing to build", _c.Improve, ButtonStyle.Normal, best != ImprovementType.None));
                actions.Put(Ui.Btn(army.AutomatedWorkers ? "Stop automating  (U)" : "Automate  (U)", _c.ToggleAutomate));
            }
            var gp = _c.GreatPersonInSelection();
            if (gp != null) actions.Put(Ui.Btn($"Use {gp.Def.Name}  (V)", _c.UseGreatPerson, ButtonStyle.Primary));
            var enemyCity = _c.AdjacentEnemyCityOfSelection();
            if (enemyCity != null && !enemyCity.IsBesieged) actions.Put(Ui.Btn($"Besiege {enemyCity.Name}  (G)", _c.Besiege, ButtonStyle.Danger));
            if (_c.BesiegedBySelection() != null) actions.Put(Ui.Btn("Siege camp", () => _c.TogglePanel(HudPanel.Siege)));
            actions.Put(Ui.Btn("Deselect  (Esc)", _c.Deselect, ButtonStyle.Ghost));
            _selection.Add(actions);
        }

        void BuildUnitCard(Unit unit, Battle battle)
        {
            var header = _selection.Put(Ui.Row(8));
            var stripe = Ui.Box(MarkerLayer.ColorOf(unit.OwnerId), 0, 2);
            stripe.style.width = 4;
            stripe.style.height = 30;
            header.Put(stripe);
            var col = header.Put(Ui.Col(2));
            col.Put(Ui.Heading(unit.Def.Name));
            col.Put(Ui.Text($"{unit.Def.Class}  ·  {unit.BattleMovesLeft}/{unit.Def.BattleMovement} MP" + (unit.HasAttacked ? "  ·  attacked" : "") +
                            (unit.Fortified ? "  ·  fortified" : "") + (battle.IsMoveLocked(unit) ? "  ·  engaged" : ""), 11, Theme.Muted));
            _selection.Put(Ui.Divider());
            var hpRow = _selection.Put(Ui.Row(8));
            hpRow.Put(Ui.Text($"{unit.Hp} HP", 12, Theme.Text, bold: true));
            hpRow.Put(Ui.Bar(unit.Hp / (float)Unit.MaxHp, unit.Hp > 60 ? Theme.Good : unit.Hp > 30 ? Theme.Gold : Theme.Bad, 8));
            _selection.Put(Ui.Text(unit.Def.IsRanged
                ? $"Ranged {unit.Def.RangedStrength}  ·  range {unit.Def.Range}{(unit.Def.IndirectFire ? " (indirect)" : "")}  ·  defence {unit.Def.CombatStrength}"
                : $"Strength {unit.Def.CombatStrength}", 12, Theme.Text));
            _selection.Put(Ui.Text("Green hexes: move  ·  red: attack  ·  hover an enemy for the odds", 11, Theme.Muted, wrap: true));
        }

        // ------------------------------------------------------------------ end turn

        void UpdateEndTurn(string stamp, Battle battle)
        {
            int idle = G.Armies.Count(a => a.OwnerId == Me.Id && !a.InBattle && a.WorldMovesLeft > 0 && !a.Destination.HasValue &&
                                           a.BuildOrder == ImprovementType.None && !a.AutomatedWorkers && G.CityAt(a.Position) == null);
            int unset = G.Cities.Count(c => c.OwnerId == Me.Id && !c.CurrentProduction.HasValue);
            if (!Changed("endturn", $"{stamp}|{battle != null}|{idle}|{unset}|{G.Victory != null}")) return;
            _endTurn.Clear();
            if (unset > 0) _endTurn.Put(Ui.Pill($"{unset} city idle — pick production", Theme.Gold));
            if (idle > 0) _endTurn.Put(Ui.Pill($"{idle} arm{(idle == 1 ? "y" : "ies")} can still move", Theme.Info));
            var btn = Ui.Btn(battle != null ? "BATTLE IN PROGRESS" : "END TURN", _c.EndTurn, ButtonStyle.Primary, battle == null && G.Victory == null, size: 18);
            btn.style.paddingLeft = btn.style.paddingRight = 34;
            btn.style.paddingTop = btn.style.paddingBottom = 14;
            btn.tooltip = "Enter";
            _endTurn.Put(btn);
        }

        // ------------------------------------------------------------------ battle HUD

        void UpdateBattle(string stamp, Battle battle)
        {
            Ui.Show(_battleBanner, battle != null);
            Ui.Show(_battleActions, battle != null);
            if (battle == null) { _signatures.Remove("battle"); return; }
            string sig = $"{stamp}|{battle.Id}|{battle.Status}|{battle.Round}|{battle.TurnInRound}|{battle.ActiveSide}|{battle.WallHp}|" +
                         $"{battle.Active.Reserve.Count}|{battle.Opponent(battle.ActiveSide).Reserve.Count}|{_c.SelectedUnit?.Id}|{_c.SelectedUnit?.HasAttacked}";
            if (!Changed("battle", sig)) return;

            _battleBanner.Clear();
            var mine = battle.Active;
            var theirs = battle.Opponent(battle.ActiveSide);
            var title = _battleBanner.Put(Ui.Row(8));
            title.Put(Ui.Pill(battle.Objective.HasValue ? "SIEGE ASSAULT" : "FIELD BATTLE", Theme.War));
            title.Put(Ui.Heading($"{battle.Attacker.Player.Name}  vs  {battle.Defender.Player.Name}"));
            title.Put(Ui.Spacer());
            title.Put(Ui.Text(battle.ActiveSide == BattleSideId.Attacker ? "you attack" : "you defend", 11, Theme.Muted));

            if (battle.Status == BattleStatus.Deploying)
            {
                _battleBanner.Put(Ui.Text("DEPLOYMENT — click a unit, then a blue hex of your zone to move or swap it. Confirm when ready.", 12, Theme.Info, wrap: true))
                    .style.marginTop = 8;
            }
            else
            {
                var track = Ui.Row(10);
                track.style.marginTop = 8;
                for (int r = 1; r <= Battle.MaxRounds; r++)
                {
                    var roundBox = Ui.Col(3);
                    roundBox.Put(Ui.Caption($"Round {r}"));
                    var pips = roundBox.Put(Ui.Row(3));
                    for (int t = 1; t <= Battle.TurnsPerRound; t++)
                    {
                        bool done = r < battle.Round || (r == battle.Round && t < battle.TurnInRound);
                        bool now = r == battle.Round && t == battle.TurnInRound;
                        var pip = Ui.Box(now ? Theme.Accent : done ? Theme.Muted : Theme.Track, 0, 3);
                        pip.style.width = 26;
                        pip.style.height = 6;
                        pips.Put(pip);
                    }
                    track.Put(roundBox);
                }
                track.Put(Ui.Spacer());
                track.Put(Ui.Text($"Reserves  you {mine.Reserve.Count}  ·  them {theirs.Reserve.Count}", 11, Theme.Muted));
                _battleBanner.Add(track);
                if (battle.AirSupport(battle.ActiveSide).Count > 0 || battle.AirSupport(theirs.Id).Count > 0)
                    _battleBanner.Put(Ui.Text($"Air support  you {battle.AirSupport(battle.ActiveSide).Count}  ·  them {battle.AirSupport(theirs.Id).Count}", 11, Theme.Info))
                        .style.marginTop = 4;
            }

            if (battle.HasWalls)
            {
                var walls = Ui.Row(8);
                walls.style.marginTop = 8;
                walls.Put(Ui.Text(battle.WallsIntact ? $"Walls {battle.WallHp}/{battle.MaxWallHp}" : "Walls breached", 11, battle.WallsIntact ? Theme.Accent : Theme.Good, bold: true));
                walls.Put(Ui.Bar(battle.MaxWallHp > 0 ? battle.WallHp / (float)battle.MaxWallHp : 0, Theme.Accent));
                _battleBanner.Add(walls);
                if (battle.WallsIntact)
                    _battleBanner.Put(Ui.Text("Melee can't enter the centre (yellow) without a siege tower alongside. Batter the walls to breach them.", 11, Theme.Muted, wrap: true));
            }

            _battleActions.Clear();
            bool deploying = battle.Status == BattleStatus.Deploying;
            _battleActions.Put(Ui.Btn(deploying ? "CONFIRM DEPLOYMENT  (Space)" : "END BATTLE TURN  (Space)", _c.BattleEndTurn, ButtonStyle.Primary, size: 14));
            if (!deploying && battle.HasWalls && battle.WallsIntact)
            {
                var u = _c.SelectedUnit;
                bool can = u != null && battle.PositionOf(u) is HexCoord from && battle.CanAttackWallsFrom(u, from);
                _battleActions.Put(Ui.Btn("Batter walls  (B)", _c.BattleBatterWalls, ButtonStyle.Normal, can));
            }
            _battleActions.Put(Ui.Btn("Auto-resolve round  (X)", _c.BattleAutoResolve));
            if (!deploying) _battleActions.Put(Ui.Btn("Retreat  (R)", _c.BattleRetreat, ButtonStyle.Danger));
        }

        void UpdatePreview(Battle battle)
        {
            var unit = _c.SelectedUnit;
            var hover = _c.HoverHex;
            Unit target = battle != null && hover.HasValue ? battle.UnitAt(hover.Value) : null;
            bool show = battle != null && unit != null && target != null && battle.PositionOf(unit) != null &&
                        battle.SideOf(target) != battle.SideOf(unit) && battle.Status == BattleStatus.InProgress;
            Ui.Show(_preview, show);
            if (!show) { _signatures.Remove("preview"); return; }
            if (!Changed("preview", $"{unit.Id}|{unit.Hp}|{target.Id}|{target.Hp}|{battle.PositionOf(unit)}|{unit.HasAttacked}")) return;

            _preview.Clear();
            var from = battle.PositionOf(unit).Value;
            _preview.Put(Ui.Caption("Combat preview"));
            if (!battle.CanAttackFrom(unit, from, hover.Value))
            {
                _preview.Put(Ui.Text($"{target.Def.Name} can't be attacked from here (range, line of sight, walls or domain).", 12, Theme.Muted, wrap: true));
                return;
            }
            var p = battle.PreviewAttack(unit, from, target);
            var cols = Ui.Row(12, center: false);
            cols.style.marginTop = 6;
            cols.Put(StrengthColumn(unit.Def.Name, p.Attacker, Theme.Good));
            cols.Put(StrengthColumn(target.Def.Name, p.Defender, Theme.Bad));
            _preview.Add(cols);
            _preview.Put(Ui.Divider());
            DamageRow("Damage to them", p.ExpectedDamageToDefender, target.Hp, Theme.Good);
            if (!unit.Def.IsRanged) DamageRow("Damage to you", p.ExpectedDamageToAttacker, unit.Hp, Theme.Bad);
            else _preview.Put(Ui.Text("Ranged attack: no retaliation", 11, Theme.Muted));
        }

        VisualElement StrengthColumn(string name, StrengthBreakdown s, Color accent)
        {
            var col = Ui.Col(2);
            col.style.flexGrow = 1;
            col.style.flexBasis = 0;
            col.Put(Ui.Text(name, 12, Theme.Text, bold: true));
            col.Put(Ui.Text(s.Total.ToString(), 26, accent, bold: true));
            col.Put(Ui.Text($"base {s.Base}", 11, Theme.Muted));
            foreach (var m in s.Modifiers)
                col.Put(Ui.Text($"{m.Label} {Ui.Signed(m.Value)}", 11, m.Value >= 0 ? Theme.Text : Theme.Bad));
            return col;
        }

        void DamageRow(string label, double expected, int hp, Color color)
        {
            var row = _preview.Put(Ui.Row(8));
            var l = Ui.Text(label, 11, Theme.Muted);
            l.style.width = 110;
            row.Put(l);
            row.Put(Ui.Bar((float)(expected / Unit.MaxHp), color, 8));
            row.Put(Ui.Text(expected >= hp ? $"~{expected:0}  KILL" : $"~{expected:0}", 11, expected >= hp ? color : Theme.Text, bold: expected >= hp));
        }

        // ------------------------------------------------------------------ tooltip

        void UpdateTooltip(Battle battle)
        {
            var hover = _c.HoverHex;
            var panel = _root.panel;
            bool over = hover.HasValue && panel != null && !PointerOverUi() && _c.Viewer.IsExplored(hover.Value);
            Ui.Show(_tooltip, over);
            if (!over) return;

            var mouse = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
            float tw = float.IsNaN(_tooltip.layout.width) ? 0 : _tooltip.layout.width;
            float th = float.IsNaN(_tooltip.layout.height) ? 0 : _tooltip.layout.height;
            // Flip to the other side of the cursor near the right/bottom edges.
            _tooltip.style.left = mouse.x + 18 + tw > _root.layout.width ? mouse.x - tw - 12 : mouse.x + 18;
            _tooltip.style.top = mouse.y + 18 + th > _root.layout.height ? mouse.y - th - 12 : mouse.y + 18;

            var tile = G.Map.Get(hover.Value);
            var unit = battle?.UnitAt(hover.Value);
            var army = _c.Viewer.IsVisible(hover.Value) ? G.ArmyAt(hover.Value) : null;
            if (!Changed("tooltip", $"{hover}|{unit?.Id}|{unit?.Hp}|{army?.Id}|{army?.Count}|{tile.Improvement}|{tile.OwnerPlayerId}")) return;

            _tooltip.Clear();
            string height = tile.IsWater ? (tile.Terrain == TerrainType.Coast ? "coast" : "ocean")
                : tile.IsMountain ? "mountain" : new[] { "lowland", "plains", "hills", "highlands" }[Mathf.Clamp(tile.Elevation, 0, 3)];
            _tooltip.Put(Ui.Text($"{tile.Terrain}{(tile.Feature != FeatureType.None ? " · " + tile.Feature : "")}  ({height})", 12, Theme.Text, bold: true));
            if (!tile.IsWater && !tile.IsMountain)
            {
                var y = EconomyRules.TileYields(tile, G.Map);
                _tooltip.Put(Ui.Text($"{y.Food} food  ·  {y.Production} prod  ·  {y.Gold} gold", 11, Theme.Muted));
            }
            if (tile.Resource != ResourceType.None)
                _tooltip.Put(Ui.Text($"{tile.Resource} ({Improvements.KindOf(tile.Resource)})" +
                                     (Improvements.IsConnected(tile) ? " — connected" : $" — needs a {Improvements.ImprovementFor(tile.Resource)}"), 11, Theme.Accent));
            if (tile.Improvement != ImprovementType.None) _tooltip.Put(Ui.Text($"Improvement: {tile.Improvement}", 11, Theme.Text));
            if (tile.RiverEdges != 0) _tooltip.Put(Ui.Text("River: crossing ends movement, −4 when attacking across", 11, Theme.Info, wrap: true));
            if (tile.OwnerPlayerId >= 0) _tooltip.Put(Ui.Text($"Territory of {G.Player(tile.OwnerPlayerId).Name}", 11, MarkerLayer.ColorOf(tile.OwnerPlayerId)));
            if (unit != null)
                _tooltip.Put(Ui.Text($"{unit.Def.Name} ({G.Player(unit.OwnerId).Name}) — {unit.Hp} HP", 11, Theme.Text, bold: true));
            else if (army != null)
                _tooltip.Put(Ui.Text($"{(army.IsNaval ? "Fleet" : "Army")} of {G.Player(army.OwnerId).Name}: {string.Join(", ", army.Units.Select(u => u.Def.Name))}", 11, Theme.Text, wrap: true));
        }

        // ------------------------------------------------------------------ world overlays

        void UpdateOverlays(Battle battle)
        {
            var cam = _c.Camera;
            var panel = _root.panel;
            if (cam == null || panel == null) return;
            var viewer = _c.Viewer;

            bool Place(VisualElement e, Vector3 world, float lift)
            {
                var vp = cam.WorldToViewportPoint(world);
                bool onScreen = vp.z > 0 && vp.x > -0.1f && vp.x < 1.1f && vp.y > -0.1f && vp.y < 1.1f;
                Ui.Show(e, onScreen);
                if (!onScreen) return false;
                var p = RuntimePanelUtils.CameraTransformWorldToPanel(panel, world + Vector3.up * lift, cam);
                float w = float.IsNaN(e.layout.width) ? 0 : e.layout.width;   // NaN until the first layout pass
                float h = float.IsNaN(e.layout.height) ? 0 : e.layout.height;
                e.style.left = p.x - w / 2;
                e.style.top = p.y - h;
                return true;
            }

            // City banners
            var seen = new HashSet<int>();
            foreach (var city in G.Cities)
            {
                if (!viewer.IsExplored(city.Position)) continue;
                seen.Add(city.Id);
                if (!_cityBanners.TryGetValue(city.Id, out var banner))
                {
                    banner = Ui.Box(null, 0, 5);
                    banner.pickingMode = PickingMode.Ignore;
                    Ui.Absolute(banner, 0, 0);
                    _overlay.Add(banner);
                    _cityBanners[city.Id] = banner;
                }
                string sig = $"{city.OwnerId}|{city.Population}|{city.CurrentProduction}|{city.IsBesieged}|{city.FoodStored}|{city.ProductionStored}";
                if (Changed("city" + city.Id, sig)) BuildCityBanner(banner, city);
                Place(banner, _c.MapRenderer.HexToWorld(city.Position), 0.9f);
            }
            foreach (var id in _cityBanners.Keys.Where(id => !seen.Contains(id)).ToList()) { _cityBanners[id].RemoveFromHierarchy(); _cityBanners.Remove(id); }

            // Army badges (visible armies not unfolded in the battle on screen)
            seen.Clear();
            var unfolded = battle != null ? new HashSet<Army>(battle.Attacker.Armies.Concat(battle.Defender.Armies)) : new HashSet<Army>();
            foreach (var army in G.Armies)
            {
                if (!viewer.IsVisible(army.Position) || unfolded.Contains(army)) continue;
                seen.Add(army.Id);
                if (!_armyBadges.TryGetValue(army.Id, out var badge))
                {
                    badge = Ui.Box(null, 0, 9);
                    badge.pickingMode = PickingMode.Ignore;
                    Ui.Absolute(badge, 0, 0);
                    badge.style.paddingLeft = badge.style.paddingRight = 6;
                    badge.style.paddingTop = badge.style.paddingBottom = 1;
                    var label = Ui.Text("", 11, Color.white, bold: true);
                    label.pickingMode = PickingMode.Ignore;
                    badge.Add(label);
                    _overlay.Add(badge);
                    _armyBadges[army.Id] = badge;
                }
                badge.style.backgroundColor = Color.Lerp(MarkerLayer.ColorOf(army.OwnerId), Color.black, 0.35f);
                Ui.Border(badge, army == _c.SelectedArmy ? Color.white : new Color(0, 0, 0, 0), 1.5f);
                string icon = army.IsNaval ? "~" : army.Units.Any(u => u.Def.GreatPerson == GreatPersonType.General) ? "*" : "";
                ((Label)badge[0]).text = $"{icon}{army.Units.Count(u => u.Def.IsMilitary)}" + (army.Units.Any(u => !u.Def.IsMilitary) ? "+" : "");
                Place(badge, _c.MapRenderer.HexToWorld(army.Position), 0.75f);
            }
            foreach (var id in _armyBadges.Keys.Where(id => !seen.Contains(id)).ToList()) { _armyBadges[id].RemoveFromHierarchy(); _armyBadges.Remove(id); }

            // Battle health bars
            seen.Clear();
            var shown = battle ?? G.Battles.FirstOrDefault(b => b.Attacker.Player == Me || b.Defender.Player == Me);
            if (shown != null)
                foreach (var unit in shown.AllDeployedUnits)
                {
                    seen.Add(unit.Id);
                    if (!_unitBars.TryGetValue(unit.Id, out var bar))
                    {
                        bar = Ui.Box(new Color(0, 0, 0, 0.7f), 1, 2);
                        bar.pickingMode = PickingMode.Ignore;
                        Ui.Absolute(bar, 0, 0);
                        bar.style.width = 34;
                        bar.style.height = 6;
                        var fill = Ui.Box(Theme.Good, 0, 2);
                        fill.style.height = 4;
                        bar.Add(fill);
                        _overlay.Add(bar);
                        _unitBars[unit.Id] = bar;
                    }
                    var f = bar[0];
                    f.style.width = Length.Percent(unit.Hp);
                    f.style.backgroundColor = unit.OwnerId == Me.Id ? Theme.Good : Theme.Bad;
                    Place(bar, _c.MapRenderer.HexToWorld(shown.PositionOf(unit).Value), 0.65f);
                }
            foreach (var id in _unitBars.Keys.Where(id => !seen.Contains(id)).ToList()) { _unitBars[id].RemoveFromHierarchy(); _unitBars.Remove(id); }
        }

        void BuildCityBanner(VisualElement banner, City city)
        {
            banner.Clear();
            var owner = G.Player(city.OwnerId);
            banner.style.backgroundColor = Color.Lerp(MarkerLayer.ColorOf(city.OwnerId), Color.black, 0.45f);
            Ui.Border(banner, city.IsBesieged ? Theme.War : new Color(1, 1, 1, 0.25f), 1);
            banner.style.paddingLeft = banner.style.paddingRight = 7;
            banner.style.paddingTop = banner.style.paddingBottom = 3;
            var row = banner.Put(Ui.Row(6));
            var pop = Ui.Box(new Color(0, 0, 0, 0.4f), 0, 7);
            pop.style.paddingLeft = pop.style.paddingRight = 5;
            pop.Add(Ui.Text(city.Population.ToString(), 11, Color.white, bold: true));
            row.Put(pop);
            row.Put(Ui.Text((EconomyRules.IsCapital(G, city) ? "* " : "") + city.Name.ToUpperInvariant(), 11, Color.white, bold: true));
            if (owner.IsCityState) row.Put(Ui.Text(owner.CityStateType.ToString(), 9, new Color(1, 1, 1, 0.7f)));
            if (city.IsBesieged) row.Put(Ui.Text("BESIEGED", 9, Theme.War, bold: true));
            foreach (var child in banner.Query<VisualElement>().ToList()) child.pickingMode = PickingMode.Ignore;

            if (city.OwnerId == Me.Id)
            {
                var growth = Ui.Row(4);
                growth.style.marginTop = 2;
                growth.Put(Ui.Bar(city.FoodStored / (float)EconomyRules.GrowthThreshold(city.Population), Theme.Food, 3));
                if (city.CurrentProduction.HasValue)
                    growth.Put(Ui.Bar(city.ProductionStored / (float)Mathf.Max(1, EconomyRules.Cost(G, city.CurrentProduction.Value)), Theme.Production, 3));
                foreach (var child in growth.Query<VisualElement>().ToList()) child.pickingMode = PickingMode.Ignore;
                growth.pickingMode = PickingMode.Ignore;
                banner.Add(growth);
            }
        }

        // ------------------------------------------------------------------ game over

        void UpdateGameOver()
        {
            var v = G.Victory;
            Ui.Show(_gameOver, v != null && !_dismissedGameOver);
            if (v == null || !Changed("gameover", v.ToString())) return;
            _gameOver.Clear();
            var card = Ui.Box(Theme.Panel, 28, 12);
            card.style.width = 520;
            card.style.alignItems = Align.Center;
            Ui.Border(card, Theme.Accent, 2);
            var winner = G.Player(v.WinnerId);
            bool won = !winner.IsAI;
            card.Put(Ui.Text(won ? "VICTORY" : "DEFEAT", 40, won ? Theme.Accent : Theme.Bad, bold: true));
            card.Put(Ui.Text($"{v.Type} victory for {winner.Name} ({winner.Faction.Name})", 16, Theme.Text)).style.marginTop = 6;
            card.Put(Ui.Text($"Turn {G.Turn}", 12, Theme.Muted)).style.marginTop = 4;
            var buttons = Ui.Row(10);
            buttons.style.marginTop = 18;
            buttons.Put(Ui.Btn("Load quicksave", _c.QuickLoad, ButtonStyle.Normal, _c.HasQuickSave));
            buttons.Put(Ui.Btn("Look around", () => { _dismissedGameOver = true; Ui.Show(_gameOver, false); }, ButtonStyle.Primary));
            card.Add(buttons);
            _gameOver.Add(card);
        }

        bool _dismissedGameOver;
    }
}
