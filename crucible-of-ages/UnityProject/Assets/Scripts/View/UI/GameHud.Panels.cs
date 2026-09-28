using System.Linq;
using Crucible.Core.Combat;
using Crucible.Core.Content;
using Crucible.Core.Economy;
using Crucible.Core.Empire;
using Crucible.Core.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace Crucible.View.UI
{
    /// <summary>The right-hand context panels: city, tech, policies, diplomacy, city-state, siege camp.</summary>
    public sealed partial class GameHud
    {
        void UpdatePanel(string stamp, Battle battle)
        {
            var kind = battle != null ? HudPanel.None : _c.OpenPanel;
            Ui.Show(_panel, kind != HudPanel.None);
            if (kind == HudPanel.None) { _signatures.Remove("panel"); return; }

            var city = _c.SelectedCity;
            var cs = _c.SelectedCityState;
            var siege = _c.BesiegedBySelection();
            string sig = $"{kind}|{stamp}|{Me.PolicyCulture}|{Me.Policies.Count}|{Me.Tech.CurrentResearch}|{G.Diplomacy.Pending.Count}|" +
                         $"{city?.Id}|{city?.CurrentProduction}|{city?.ProductionStored}|{city?.Population}|{city?.Buildings.Count}|{city?.AirUnits.Count}|" +
                         $"{cs?.Id}|{(cs != null ? G.InfluenceOf(cs, Me.Id) : 0)}|{siege?.SiegeProgress}|{siege?.SiegeEnginesBuilt}|{_c.SelectedArmy?.Count}|" +
                         string.Join(",", G.MajorPlayers.Select(p => G.Diplomacy.Get(Me.Id, p.Id)).Select(r => $"{r.AtWar}{r.OpenBorders}{r.DefensivePact}"));
            if (!Changed("panel", sig)) return;

            var offset = _panelScroll.scrollOffset;
            var body = _panelScroll.contentContainer;
            body.Clear();
            switch (kind)
            {
                case HudPanel.City when city != null: _panelTitle.text = city.Name; CityPanel(body, city); break;
                case HudPanel.Tech: _panelTitle.text = "Research"; TechPanel(body); break;
                case HudPanel.Policies: _panelTitle.text = "Social Policies"; PolicyPanel(body); break;
                case HudPanel.Diplomacy: _panelTitle.text = "Diplomacy"; DiplomacyPanel(body); break;
                case HudPanel.CityState when cs != null: _panelTitle.text = cs.Name; CityStatePanel(body, cs); break;
                case HudPanel.Siege when siege != null: _panelTitle.text = $"Siege of {siege.Name}"; SiegePanel(body, siege); break;
                default: Ui.Show(_panel, false); break;
            }
            _panelScroll.scrollOffset = offset;
        }

        // ------------------------------------------------------------------ city

        void CityPanel(VisualElement body, City city)
        {
            var y = EconomyRules.CityYields(G, city);
            int surplus = EconomyRules.FoodSurplus(G, city, y);
            int threshold = EconomyRules.GrowthThreshold(city.Population);

            var head = body.Put(Ui.Row(8));
            head.Put(Ui.Pill($"POP {city.Population}", Theme.Food));
            if (EconomyRules.IsCapital(G, city)) head.Put(Ui.Pill("CAPITAL", Theme.Accent));
            if (city.IsBesieged) head.Put(Ui.Pill("BESIEGED", Theme.War));
            if (city.ReligionId >= 0) head.Put(Ui.Pill(G.Religions[city.ReligionId].Name, Theme.Faith));

            var yields = Ui.Row();
            yields.style.flexWrap = Wrap.Wrap;
            yields.style.marginTop = 10;
            yields.Add(Ui.Stat("food", Ui.Signed(surplus), Theme.Food, $"{y.Food} produced, {EconomyRules.FoodPerCitizen * city.Population} eaten"));
            yields.Add(Ui.Stat("production", y.Production.ToString(), Theme.Production));
            yields.Add(Ui.Stat("gold", y.Gold.ToString(), Theme.Gold));
            yields.Add(Ui.Stat("science", y.Science.ToString(), Theme.Science));
            yields.Add(Ui.Stat("culture", y.Culture.ToString(), Theme.Culture));
            if (y.Faith > 0) yields.Add(Ui.Stat("faith", y.Faith.ToString(), Theme.Faith));
            body.Add(yields);

            ProgressRow(body, "Growth", city.FoodStored, threshold, Theme.Food,
                surplus > 0 ? $"{Mathf.CeilToInt((threshold - city.FoodStored) / (float)surplus)} turns" : surplus < 0 ? "starving" : "stagnant");
            ProgressRow(body, "Borders", city.CultureStored, EconomyRules.BorderGrowthThreshold(city), Theme.Culture, "next hex");

            body.Put(Ui.Divider());
            body.Put(Ui.Caption("Now building"));
            if (city.CurrentProduction.HasValue)
            {
                var item = city.CurrentProduction.Value;
                int cost = EconomyRules.Cost(G, item);
                int turns = y.Production > 0 ? Mathf.CeilToInt(Mathf.Max(0, cost - city.ProductionStored) / (float)y.Production) : 99;
                ProgressRow(body, EconomyRules.NameOf(G, item), city.ProductionStored, cost, Theme.Production, $"{turns} turns");
            }
            else body.Put(Ui.Text("Nothing — choose below (stored production carries over).", 12, Theme.Gold, wrap: true)).style.marginTop = 4;

            if (city.Buildings.Count > 0 || city.AirUnits.Count > 0 || city.GreatWorks > 0)
            {
                body.Put(Ui.Divider());
                body.Put(Ui.Caption("Buildings"));
                var wrap = body.Put(Ui.Row());
                wrap.style.flexWrap = Wrap.Wrap;
                foreach (var b in city.Buildings.OrderBy(b => b))
                {
                    var pill = Ui.Pill(G.Content.Building(b).Name, Theme.Card, Theme.Text);
                    pill.style.marginRight = pill.style.marginTop = 4;
                    wrap.Add(pill);
                }
                if (city.GreatWorks > 0) body.Put(Ui.Text($"Great works: {city.GreatWorks}", 12, Theme.Tourism)).style.marginTop = 4;
                if (city.AirUnits.Count > 0)
                    body.Put(Ui.Text($"Hangar {city.AirUnits.Count}/{City.AirCapacity}: " + string.Join(", ", city.AirUnits.Select(u => $"{u.Def.Name} ({u.Hp})")), 12, Theme.Info, wrap: true))
                        .style.marginTop = 4;
            }

            body.Put(Ui.Divider());
            var options = G.Content.Units.Select(u => ProductionItem.Unit(u.Id))
                .Concat(G.Content.Buildings.Select(b => ProductionItem.Building(b.Id)))
                .Concat(G.Content.Projects.Select(p => ProductionItem.Project(p.Id)))
                .Where(i => EconomyRules.CanBuild(G, city, i))
                .ToList();
            foreach (var group in options.GroupBy(i => i.Kind))
            {
                body.Put(Ui.Caption(group.Key == ProductionKind.Unit ? "Units" : group.Key == ProductionKind.Building ? "Buildings" : "Projects")).style.marginTop = 6;
                foreach (var item in group.OrderBy(i => EconomyRules.Cost(G, i)))
                    body.Put(ProductionRow(city, item, y.Production));
            }
        }

        VisualElement ProductionRow(City city, ProductionItem item, int production)
        {
            bool current = city.CurrentProduction.HasValue && city.CurrentProduction.Value.Equals(item);
            int cost = EconomyRules.Cost(G, item);
            int turns = production > 0 ? Mathf.CeilToInt(cost / (float)production) : 99;
            int price = EconomyRules.PurchaseCost(G, city, item);

            var row = Ui.Box(current ? new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.15f) : Theme.Card, 0, 6);
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingLeft = 10;
            row.style.paddingRight = 6;
            row.style.paddingTop = row.style.paddingBottom = 5;
            row.style.marginTop = 4;
            var info = Ui.Col(1);
            info.style.flexGrow = 1;
            info.Put(Ui.Text(EconomyRules.NameOf(G, item), 12, Theme.Text, bold: true));
            info.Put(Ui.Text($"{cost} prod  ·  {turns} turns{Describe(item)}", 10, Theme.Muted));
            row.Add(info);
            var build = Ui.Btn(current ? "Building" : "Build", () => _c.SetProduction(city, item), ButtonStyle.Normal, !current, 11);
            build.style.marginRight = 4;
            row.Add(build);
            row.Add(Ui.Btn($"Buy {price}", () => _c.Buy(city, item), ButtonStyle.Ghost, Me.Gold >= price && !city.IsBesieged, 11));
            return row;
        }

        string Describe(ProductionItem item)
        {
            switch (item.Kind)
            {
                case ProductionKind.Unit:
                    var u = G.Content.Unit(item.Id);
                    if (!u.IsMilitary) return "";
                    return u.IsRanged ? $"  ·  {u.RangedStrength} ranged" : u.Domain == UnitDomain.Air ? $"  ·  {u.RangedStrength} strike" : $"  ·  {u.CombatStrength} strength";
                case ProductionKind.Building:
                    var b = G.Content.Building(item.Id);
                    var parts = new System.Collections.Generic.List<string>();
                    if (b.Yields.Food > 0) parts.Add($"+{b.Yields.Food} food");
                    if (b.Yields.Production > 0) parts.Add($"+{b.Yields.Production} prod");
                    if (b.Yields.Gold > 0) parts.Add($"+{b.Yields.Gold} gold");
                    if (b.Yields.Science > 0) parts.Add($"+{b.Yields.Science} sci");
                    if (b.Yields.Culture > 0) parts.Add($"+{b.Yields.Culture} culture");
                    if (b.Yields.Faith > 0) parts.Add($"+{b.Yields.Faith} faith");
                    if (b.Happiness > 0) parts.Add($"+{b.Happiness} happy");
                    if (b.WallTiers > 0) parts.Add("walls");
                    if (b.Maintenance > 0) parts.Add($"−{b.Maintenance} gold/t");
                    return parts.Count > 0 ? "  ·  " + string.Join(", ", parts) : "";
                default:
                    return G.Content.Project(item.Id).SpaceshipPart ? "  ·  spaceship part" : "";
            }
        }

        static void ProgressRow(VisualElement body, string label, int value, int max, Color color, string note)
        {
            var col = body.Put(Ui.Col(3));
            col.style.marginTop = 8;
            var top = col.Put(Ui.Row());
            top.Add(Ui.Text(label, 12, Theme.Text, bold: true));
            top.Add(Ui.Spacer());
            top.Add(Ui.Text($"{value}/{max}  ·  {note}", 11, Theme.Muted));
            col.Put(Ui.Bar(max > 0 ? value / (float)max : 0, color, 7));
        }

        // ------------------------------------------------------------------ research

        void TechPanel(VisualElement body)
        {
            int science = EconomyRules.EmpireIncome(G, Me).Science;
            var current = Me.Tech.CurrentResearch != null ? G.Content.Tech(Me.Tech.CurrentResearch) : null;
            if (current != null)
                ProgressRow(body, $"Researching {current.Name}", Me.Tech.Progress, current.ScienceCost, Theme.Science,
                    science > 0 ? $"{Mathf.CeilToInt((current.ScienceCost - Me.Tech.Progress) / (float)science)} turns" : "no science");
            body.Put(Ui.Text($"{Me.Tech.Researched.Count} of {G.Content.Techs.Count()} technologies known  ·  +{science} science/turn", 11, Theme.Muted))
                .style.marginTop = 6;
            body.Put(Ui.Divider());
            body.Put(Ui.Caption("Available now"));
            foreach (var tech in Me.Tech.Available().OrderBy(t => t.ScienceCost).ThenBy(t => t.Name))
            {
                bool isCurrent = tech == current;
                var card = Ui.Box(isCurrent ? new Color(Theme.Science.r, Theme.Science.g, Theme.Science.b, 0.15f) : Theme.Card, 10, 6);
                card.style.marginTop = 6;
                var top = card.Put(Ui.Row(8));
                var name = Ui.Col(1);
                name.style.flexGrow = 1;
                name.Put(Ui.Text(tech.Name, 13, Theme.Text, bold: true));
                int turns = science > 0 ? Mathf.CeilToInt(tech.ScienceCost / (float)science) : 99;
                name.Put(Ui.Text($"{tech.Era} era  ·  {tech.ScienceCost} science  ·  ~{turns} turns", 10, Theme.Muted));
                top.Put(name);
                top.Put(Ui.Btn(isCurrent ? "Researching" : "Research", () => _c.SetResearch(tech.Id), ButtonStyle.Normal, !isCurrent, 11));
                string unlocks = Unlocks(tech.Id);
                if (unlocks.Length > 0) card.Put(Ui.Text("Unlocks: " + unlocks, 11, Theme.Info, wrap: true)).style.marginTop = 4;
                body.Add(card);
            }
        }

        string Unlocks(string techId)
        {
            var names = G.Content.Units.Where(u => u.RequiredTech == techId && u.FactionId == null && u.ProductionCost > 0).Select(u => u.Name)
                .Concat(G.Content.Buildings.Where(b => b.RequiredTech == techId).Select(b => b.Name))
                .Concat(G.Content.Projects.Where(p => p.RequiredTech == techId && p.RequiresProject == null).Select(p => p.Name))
                .ToList();
            var tech = G.Content.Tech(techId);
            if (tech.ArmyCapBonus > 0) names.Add($"army cap +{tech.ArmyCapBonus}");
            if (techId == "optics") names.Add("embarking");
            if (techId == "astronomy") names.Add("ocean crossing");
            if (techId == "globalization") names.Add("World Congress");
            return string.Join(", ", names);
        }

        // ------------------------------------------------------------------ policies

        void PolicyPanel(VisualElement body)
        {
            int cost = EconomyRules.PolicyCost(G, Me);
            ProgressRow(body, "Culture toward next policy", Me.PolicyCulture, cost, Theme.Culture,
                Me.PolicyCulture >= cost ? "ready!" : $"+{EconomyRules.EmpireIncome(G, Me).Culture}/turn");
            foreach (var tree in G.Content.Policies.GroupBy(p => p.Tree))
            {
                var card = Ui.Card();
                card.style.marginTop = 10;
                card.Put(Ui.Heading(tree.Key));
                foreach (var policy in tree)
                {
                    bool adopted = Me.Policies.Contains(policy.Id);
                    bool available = EconomyRules.CanAdopt(G, Me, policy);
                    var row = card.Put(Ui.Row(8));
                    row.style.marginTop = 6;
                    var dot = Ui.Box(adopted ? Theme.Good : available ? Theme.Accent : Theme.Track, 0, 5);
                    dot.style.width = dot.style.height = 10;
                    row.Put(dot);
                    var text = Ui.Col(1);
                    text.style.flexGrow = 1;
                    text.style.flexShrink = 1;
                    text.Put(Ui.Text(policy.Name, 12, adopted ? Theme.Good : Theme.Text, bold: true));
                    text.Put(Ui.Text(policy.Description, 11, Theme.Muted, wrap: true));
                    row.Put(text);
                    if (!adopted)
                        row.Put(Ui.Btn("Adopt", () => _c.AdoptPolicy(policy), ButtonStyle.Primary, available && Me.PolicyCulture >= cost, 11));
                }
                body.Add(card);
            }
        }

        // ------------------------------------------------------------------ diplomacy

        void DiplomacyPanel(VisualElement body)
        {
            var pending = G.Diplomacy.Pending.Where(p => p.ToId == Me.Id).ToList();
            if (pending.Count > 0)
            {
                body.Put(Ui.Caption("Proposals for you"));
                foreach (var proposal in pending)
                {
                    var card = Ui.Card();
                    card.style.marginTop = 6;
                    Ui.Border(card, Theme.Accent, 1);
                    card.Put(Ui.Text($"{G.Player(proposal.FromId).Name} proposes {Label(proposal.Kind)}", 12, Theme.Text, bold: true, wrap: true));
                    var buttons = card.Put(Ui.Row(6));
                    buttons.style.marginTop = 6;
                    buttons.Put(Ui.Btn("Accept", () => _c.Answer(proposal, true), ButtonStyle.Primary));
                    buttons.Put(Ui.Btn("Decline", () => _c.Answer(proposal, false)));
                    body.Add(card);
                }
                body.Put(Ui.Divider());
            }

            foreach (var other in G.MajorPlayers.Where(p => p != Me && !p.IsEliminated))
            {
                var rel = G.Diplomacy.Get(Me.Id, other.Id);
                var card = Ui.Card();
                var head = card.Put(Ui.Row(8));
                var stripe = Ui.Box(MarkerLayer.ColorOf(other.Id), 0, 2);
                stripe.style.width = 4;
                stripe.style.height = 32;
                head.Put(stripe);
                var names = head.Put(Ui.Col(1));
                names.style.flexGrow = 1;
                names.Put(Ui.Text(other.Name, 14, Theme.Text, bold: true));
                names.Put(Ui.Text(other.Faction.Name, 11, Theme.Muted));
                head.Put(rel.AtWar ? Ui.Pill("AT WAR", Theme.Bad)
                    : G.Turn < rel.PeaceUntilTurn ? Ui.Pill($"TREATY → T{rel.PeaceUntilTurn}", Theme.Info)
                    : Ui.Pill("PEACE", Theme.Good));

                var treaties = card.Put(Ui.Row(6));
                treaties.style.marginTop = 6;
                if (rel.OpenBorders) treaties.Put(Ui.Pill("open borders", Theme.Card, Theme.Text));
                if (rel.DefensivePact) treaties.Put(Ui.Pill("defensive pact", Theme.Card, Theme.Text));

                int opinion = G.Opinion(other, Me);
                var op = card.Put(Ui.Row(8));
                op.style.marginTop = 6;
                var opLabel = Ui.Text($"Opinion of you {Ui.Signed(opinion)}", 11, opinion >= 20 ? Theme.Good : opinion < 0 ? Theme.Bad : Theme.Muted);
                opLabel.style.width = 140;
                op.Put(opLabel);
                op.Put(Ui.Bar((opinion + 100) / 200f, opinion >= 0 ? Theme.Good : Theme.Bad));

                var actions = card.Put(Ui.Row(6));
                actions.style.marginTop = 8;
                actions.style.flexWrap = Wrap.Wrap;
                if (rel.AtWar)
                {
                    bool can = G.CanPropose(Me, other, Treaty.Peace);
                    actions.Put(Ui.Btn(can ? "Propose peace" : $"Peace from turn {rel.WarStartedTurn + Diplomacy.MinWarTurnsBeforePeace}",
                        () => _c.Propose(other, Treaty.Peace), ButtonStyle.Primary, can));
                }
                else
                {
                    actions.Put(Ui.Btn("Open borders", () => _c.Propose(other, Treaty.OpenBorders), ButtonStyle.Normal, G.CanPropose(Me, other, Treaty.OpenBorders)));
                    actions.Put(Ui.Btn("Defensive pact", () => _c.Propose(other, Treaty.DefensivePact), ButtonStyle.Normal, G.CanPropose(Me, other, Treaty.DefensivePact)));
                    actions.Put(Ui.Btn("Declare war", () => _c.DeclareWar(other), ButtonStyle.Danger, G.CannotDeclareWar(Me, other) == null));
                }
                body.Add(card);
            }

            if (G.WorldCongressFounded)
            {
                body.Put(Ui.Divider());
                body.Put(Ui.Caption("World Congress"));
                var delegates = G.Delegates();
                body.Put(Ui.Text($"Next World Leader vote on turn {G.NextWorldLeaderVoteTurn}. {G.VotesNeeded} of {G.TotalDelegates} delegates win.", 12, Theme.Text, wrap: true));
                foreach (var kv in delegates.OrderByDescending(kv => kv.Value))
                    body.Put(Ui.Text($"{G.Player(kv.Key).Name}: {kv.Value}", 12, kv.Key == Me.Id ? Theme.Accent : Theme.Muted));
            }
        }

        static string Label(Treaty t) => t == Treaty.OpenBorders ? "open borders" : t == Treaty.DefensivePact ? "a defensive pact" : "peace";

        // ------------------------------------------------------------------ city-state & siege

        void CityStatePanel(VisualElement body, Player cs)
        {
            int inf = G.InfluenceOf(cs, Me.Id);
            int ally = G.AllyOf(cs);
            var head = body.Put(Ui.Row(8));
            head.Put(Ui.Pill(cs.CityStateType.ToString().ToUpperInvariant() + " CITY-STATE", Theme.Info));
            head.Put(Ui.Pill(G.StatusWith(cs, Me.Id).ToString().ToUpperInvariant(),
                G.StatusWith(cs, Me.Id) == CityStateStatus.Ally ? Theme.Good : G.StatusWith(cs, Me.Id) == CityStateStatus.Friend ? Theme.Accent : Theme.Track));
            ProgressRow(body, "Your influence", inf, 100, Theme.Accent, $"friend {GameState.FriendInfluence} · ally {GameState.AllyInfluence}");
            body.Put(Ui.Text(ally >= 0 ? $"Allied with {G.Player(ally).Name}" : "No ally", 12, Theme.Muted)).style.marginTop = 6;
            body.Put(Ui.Text(cs.CityStateType == CityStateType.Maritime ? "Friends get +1 food in the capital (allies +3)."
                : cs.CityStateType == CityStateType.Cultured ? "Friends get +2 culture (allies +5)."
                : cs.CityStateType == CityStateType.Mercantile ? "Friends get +2 happiness (allies +4)."
                : "Allies receive a unit every 15 turns.", 12, Theme.Text, wrap: true)).style.marginTop = 6;
            body.Put(Ui.Text("Allies also gain its World Congress delegate. Influence fades by 1 each turn.", 11, Theme.Muted, wrap: true));
            var gifts = body.Put(Ui.Row(6));
            gifts.style.marginTop = 10;
            foreach (int gold in new[] { 50, 100, 250 })
                gifts.Put(Ui.Btn($"Gift {gold}g (+{gold / GameState.GoldPerInfluence})", () => _c.Gift(cs, gold), ButtonStyle.Normal, Me.Gold >= gold));
        }

        void SiegePanel(VisualElement body, City city)
        {
            int siegeTurn = G.Turn - city.BesiegedSinceTurn + 1;
            var head = body.Put(Ui.Row(8));
            head.Put(Ui.Pill($"TURN {siegeTurn}", Theme.War));
            head.Put(Ui.Pill($"WALLS TIER {G.Map.Get(city.Position).WallTier}", Theme.Accent));
            head.Put(Ui.Pill($"{city.MilitiaCount} MILITIA", Theme.Card, Theme.Text));
            body.Put(Ui.Text(siegeTurn >= EconomyProcessor.SiegeStarvationDelay
                ? "The city is starving and loses population every few turns."
                : $"Starvation begins on siege turn {EconomyProcessor.SiegeStarvationDelay}. The city can't grow and its production is cut by a quarter.",
                12, Theme.Text, wrap: true)).style.marginTop = 8;
            ProgressRow(body, "Siege progress", city.SiegeProgress, 100, Theme.Production, $"{city.SiegeEnginesBuilt}/{City.MaxSiegeEngines} engines built");
            body.Put(Ui.Divider());
            body.Put(Ui.Caption("Build siege engines"));
            foreach (var engine in G.AvailableSiegeEngines(city))
            {
                var row = Ui.Box(Theme.Card, 8, 6);
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginTop = 6;
                var info = Ui.Col(1);
                info.style.flexGrow = 1;
                info.Put(Ui.Text(engine.Name, 12, Theme.Text, bold: true));
                info.Put(Ui.Text(engine.CarriesOverWalls ? "Lets adjacent infantry climb the walls"
                    : engine.AttacksWallsOnly ? "×3 damage to walls, can't hit units"
                    : "×2 damage to walls, indirect fire", 10, Theme.Muted));
                row.Add(info);
                row.Add(Ui.Btn($"{engine.SiegeProgressCost} progress", () => _c.BuildSiegeEngine(engine), ButtonStyle.Primary,
                    city.SiegeProgress >= engine.SiegeProgressCost, 11));
                body.Add(row);
            }
            body.Put(Ui.Text("Click the city with your army to assault it.", 11, Theme.Muted)).style.marginTop = 8;
        }
    }
}
