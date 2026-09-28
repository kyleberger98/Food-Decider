# Crucible of Ages — Game Design Document

> Working title. A turn-based 4X in the spirit of *Civilization V*, with *Humankind*-style
> tactical combat: stacked armies, battles fought on the world map, multi-round engagements,
> reinforcements, and decisive elevation.

| Decision | Choice |
|---|---|
| Engine | Unity (C#), 3D low-poly hexes |
| Strategic layer | Classic Civ V: one faction all game, tech tree, social policies, city tiles, great people, wonders, religion, city-states |
| Combat layer | Humankind: army stacking, tactical battlefield, multi-round battles, elevation & terrain |
| Army cap | Tech-based (4 → 8+) |
| Battle turns | Alternating (attacker then defender) |
| Cities | Humankind-style sieges and assaults |
| Opponents (v1) | AI |
| Content span (v1) | Full history: Ancient → Information era |
| Victories (v1) | Domination, Science, Culture, Diplomatic, Score/turn limit |
| Setting | Fictional factions on a historical tech arc |

---

## 1. Pillars

1. **Every war is a place.** Battles happen *on the world map* — rivers, hills, forests and
   cities you built are the battlefield. Where you fight matters as much as what you bring.
2. **Armies, not carpets.** No Civ V "1 unit per tile" gridlock. Units travel as armies and
   unfold into formations when battle starts.
3. **Build tall or wide, fight on your terms.** The full Civ V economy stays intact, so the
   military game is fed by real economic decisions.
4. **Deterministic, testable simulation.** All rules live in a pure C# core with seeded
   randomness, so AI, autoplay, save/load and (later) multiplayer lockstep come for free.

---

## 2. World

### 2.1 Hex grid
- Pointy-top hexes, axial coordinates `(q, r)`. Map sizes: Duel 40×24 → Huge 128×80.
- Horizontal wrap (cylindrical) optional per map script.

### 2.2 Elevation (combat-critical)
Each land tile has an **elevation level 0–4**; water is below 0.

| Level | Typical terrain | Notes |
|---|---|---|
| −2 / −1 | Ocean / Coast | Naval only |
| 0 | Lowlands, marsh, floodplain | |
| 1 | Plains, grassland | |
| 2 | Hills | |
| 3 | Highlands | |
| 4 | Mountains | Impassable to land units until Information era (paratroopers/air) |

- **Climbing:** moving up 1 level costs +1 MP. Moving up ≥2 levels in one step is a **cliff**
  and is impassable (except along roads/ramps built by workers).
- **High ground:** +3 CS per level above the opponent (max +6), ranged units gain +1 range
  when firing from at least 1 level higher.
- **Line of sight:** a hex between shooter and target blocks LOS if its *sight height* is above
  the higher of the two endpoints. Forests and jungles add +1 sight height. Indirect-fire units
  (siege, artillery) ignore LOS.

### 2.3 Terrain, features, rivers
- Base terrains: Grassland, Plains, Desert, Tundra, Snow, Coast, Ocean, Lake.
- Features: Forest, Jungle, Marsh, Floodplain, Oasis, Reef, Ice, natural wonders.
- **Map scripts:** *Continents* (two large landmasses plus islands, 38% land), *Pangaea* (one
  supercontinent, 42%), *Archipelago* (a scatter of islands, 30%). Land comes from warped
  continent blobs; sea level is picked per map so the land share is hit on every seed. Ridge
  noise raises mountain ranges. Climate bands give polar pack ice, tundra and taiga, temperate
  grass and woods, a subtropical desert belt with oases, and equatorial jungle and marsh.
- **Lakes** fill inland hollows: shallow water (2 food, 1 gold) that boats and embarked
  armies can use. **Oases** give 3 food and 1 gold. **Pack ice** blocks ships and embarked armies.
- **Natural wonders** (three per map, at least 8 hexes apart): *Emberfall Geyser* (+2 science,
  +2 faith), *Glass Dunes* (+3 gold, +2 culture), *Worldspine* (a mountain: +2 culture,
  +3 faith). Each gives its owner +1 happiness.
- **Rivers run along hex edges.** Attacking across a river: −4 CS. Crossing ends movement
  unless a bridge/road exists (Civ V rule).
- Resources: Bonus / Luxury / Strategic (Iron, Horses, Niter, Coal, Oil, Aluminum, Uranium) —
  Civ V quantities model; strategic resources gate units.

---

## 3. Strategic layer (Civ V)

Kept intentionally close to Civ V (Brave New World ruleset) so players know it:

- **Yields:** Food, Production, Gold, Science, Culture, Faith, Tourism.
- **Cities:** citizens work tiles within 3 rings; borders grow with culture or by buying tiles;
  buildings, specialists, great-work slots.
- **Happiness:** global. Base 9, −3 per city, −1 per citizen, plus luxuries, buildings and
  policies. Below 0, growth drops to ¼; at −10, growth stops and units fight at −3 CS.
  War weariness applies in the Industrial era and later.
- **Growth:** each citizen eats 2 food. Growing from population n takes
  15 + 6(n−1) + (n−1)^1.8 food. A city whose food store drops below 0 loses a citizen.
- **Upkeep:** buildings cost gold maintenance. Units are free up to 2 + 2 per city, then
  cost 1 gold each. A bankrupt empire disbands one unit per turn.
- **City governor:** it keeps at least one defender per city and never builds into
  negative income. Otherwise it follows its build priority list (monument, granary, market, library, walls, …).
- **Tech tree:** 8 eras (Ancient, Classical, Medieval, Renaissance, Industrial, Modern, Atomic,
  Information), about 80 techs.
- **Social policies:** 9 trees plus 3 ideologies (Freedom / Order / Autocracy analogues, renamed).
  Culture fills a policy pool. The next policy costs 25 + (3n)^2.01, +10% per extra city.
  Trees are linear. **Honor** carries the combat policies: +2 CS, +1 CS, and army cap +1
  (*Professional Army*).
- **Resources & workers:** bonus resources add yields on their own. Strategic and luxury
  resources count once a worker connects them with the right improvement: a farm, mine,
  pasture, plantation, camp or well, each taking 5–7 turns and needing a tech. Each connected
  strategic source supports 4 units that need it. Each distinct luxury gives +4 happiness.
- **Great People:** Scientist, Engineer, Merchant, Writer, Artist, Musician, Prophet,
  **General**, **Admiral**.
- **Religion:** pantheon → founding → enhancing → reformation beliefs.
- **City-states:** Maritime / Cultured / Militaristic / Mercantile / Religious; quests; influence.
- **Diplomacy:** new games start at **peace**. Declaring war needs no active peace treaty; the
  victim's defensive-pact partners join in, and the declaration is remembered (−30 opinion for the
  victim and −10 for everyone else, for 50 turns). Peace can be proposed after 10 turns of war. It
  calls off battles and sieges, expels each side's armies from the other's land, and holds for
  10 turns. At peace, armies can't enter another major's territory without **open borders**;
  city-state land is always open. **Defensive pacts** drag partners into wars. AI **opinion**
  weighs declarations, lost cities, treaties and shared enemies. The AI accepts friendship when it
  likes you and has no designs on you, and accepts peace when it's losing or the war drags on.
  AI proposals to a human wait for an answer. Research agreements, embassies and denouncements
  are ⏳. The **World Congress** is the **United Nations** analogue.
- **Workers** build improvements; **roads** matter more than in Civ V, because armies are the
  main movers.
- **Fog of war:** unexplored / fogged (terrain and cities remembered) / visible. Sight is 2 hexes,
  +1 from hills or higher and +1 for an army that contains recon. Ridges and forests block sight
  using the same LOS rule as ranged combat.

### 3.1 Fictional factions
Each faction has a **unique ability**, **2 unique units/buildings/improvements**, a leader
personality (for AI), and a historical "inspiration" used only for art direction.
Launch roster target: 12 factions. Two are specced for v1 content:

| Faction | Ability | Uniques |
|---|---|---|
| **Aurel Dominion** (disciplined legions) | *Iron Discipline*: army cap +1; units heal 10 HP when a battle round ends | Aurel Legionary (Swordsman); Castra (camp improvement: +4 CS defense, heals) |
| **Khaganate of the Steppe** (horse lords) | *Endless Horizon*: mounted units +1 MP on the world map; pillaging is free and yields double | Sky Rider (Horseman); Yurt Camp (movable city building) |

---

## 4. Military layer (Humankind-inspired)

### 4.1 Units
Every unit has:

| Stat | Meaning |
|---|---|
| **Combat Strength (CS)** | melee attack and defense |
| **Ranged Strength (RS)** | ranged attack (ranged/siege classes only) |
| **Range** | in hexes |
| **Battle MP** | movement per battle turn |
| **World MP** | movement per world turn |
| **HP** | 100 for all units |
| **Class** | Melee, Ranged, Mounted, AntiCavalry, Siege, Gunpowder, Armor, Recon, Naval‑Melee, Naval‑Ranged, Carrier, Fighter, Bomber, Missile, Civilian |
| **Promotions** | Civ V promotion trees per class, earned with XP |

**Class counters** (bonus CS when attacking *or* defending against):

| Class | Bonus vs |
|---|---|
| AntiCavalry | Mounted +8 |
| Mounted | Ranged / Siege +5 (flank‑charge) |
| Siege | Cities & fortifications +10, −10 vs units in open field |
| Armor | Gunpowder +5 |

### 4.2 Armies (stacking)
- An **army** is 1..N units on one tile. **Max one military army per tile** (plus one civilian).
- **Army cap** comes from tech and policies:

| Source | Cap |
|---|---|
| Start | 4 |
| *Military Tactics* (Classical) | 5 |
| *Chivalry* (Medieval) | 6 |
| *Military Science* (Industrial) | 7 |
| *Combined Arms* (Atomic) | 8 |
| Great General in army / *Professional Army* policy / Aurel ability | +1 each |

- An army moves at the **speed of its slowest unit**; embarking, ZOC and river rules apply to
  the whole army.
- Merge, split, and transfer units freely at the start of a move (costs no MP).
- **Zone of control:** entering a hex adjacent to an enemy army ends world movement.
- **Upkeep:** gold per unit, scaling with era (Civ V curve).

### 4.3 Starting a battle
An army **engages** when it attacks an adjacent enemy army or city, or when it tries to move
through an enemy's ZOC hex that holds an army.

1. **Battlefield generation.** All passable hexes within **radius 3** of the midpoint between
   attacker and defender (radius 4 for sieges), clipped to terrain the combatants can stand on.
   Naval battles use water hexes; coastal battles include both.
2. **Deployment zones.** Each side gets the hexes of the battlefield closest to its own origin
   tile, about ⅓ of the field each. The defender deploys first (it chose the ground).
   Deployment is auto-suggested and can be edited.
3. **Reinforcements & allies.** Any friendly or allied army whose tile is **inside or adjacent
   to the battlefield** is pulled in as reinforcement, with its own deployment slots at
   its edge of the field. Armies that are not pulled in can **march in** between rounds.
4. **Frontline slots.** Each side can have at most `armyCap` units on the field at once.
   Extra units wait in **reserve** and deploy at the start of the next battle turn after a
   friendly unit dies.

### 4.4 Battle flow
- A battle lasts up to **3 rounds**. **One round = 3 battle turns per side.**
- **Round 1** plays immediately when the battle starts. **Rounds 2 and 3** play at the start
  of the attacker's next world turns, so a battle can span 3 world turns. The battlefield
  tiles stay **locked**: armies can't pass through them, and new armies can enter only as
  reinforcements.
- Inside a battle turn, the active side moves and acts with **every** unit (alternating),
  then ends its turn.
- **Unit actions per battle turn:** move (battle MP), then one attack (or attack then stop).
  Ranged units can't move after attacking. Some promotions allow move-after-attack.
- **Zone of control inside battle:** entering an enemy-adjacent hex, or crossing a river, ends
  that unit's movement. A unit with MP left can still attack from there.
- **Retreat:** at the start of any of its battle turns, a side can retreat. Units adjacent to
  an enemy take a free hit ("disengage damage") first. The retreating army moves 1 hex away
  from the battle and loses 50% of remaining world MP next turn.
- **Victory conditions:**
  - All enemy units destroyed or retreated → win.
  - **Siege assault:** attacker holds the city center (the capture flag) at the end of any
    attacker battle turn → city captured.
  - After round 3 with both sides still standing → **defender wins** and the attacker
    is pushed back 1 hex.
- **Auto-resolve** runs the same simulation with AI controlling both sides, so results are
  identical in expectation to playing it out.

### 4.5 Combat formula
All modifiers are **flat CS additions** (Humankind-style), so every modifier is easy to read
in the combat preview tooltip.

```
Δ   = EffectiveCS(attacker) − EffectiveCS(defender)
dmg = round( 30 × e^(Δ / 25) × U(0.8, 1.2) )      // damage to defender
ret = round( 30 × e^(−Δ / 25) × U(0.8, 1.2) )     // damage to attacker (melee only)
```

`EffectiveCS` = base CS (or RS for ranged attacks) plus modifiers:

| Modifier | Value |
|---|---|
| Wounded | −1 per 10 HP missing |
| High ground | +3 per level above the opponent (max +6) |
| Defending in forest/jungle (hills are covered by high ground) | +3 |
| Attacking across river | −4 |
| Flanking | +2 per *other* friendly unit adjacent to the target (max +6) |
| Class counter | see §4.1 |
| Fortified (skipped a battle turn) | +3 |
| Promotions / veterancy | per promotion |
| Great General in battle | +3 to all units within 2 hexes |
| Unhappiness < −10 | −3 |
| Defending on a walled hex (a capital's palace gives tier 1) | +5 per wall tier |

A unit dies at 0 HP. A melee attacker **advances** into the tile when the defender dies.

### 4.6 Sieges (Humankind-style)
1. **Besiege:** a hostile army ends its turn adjacent to an enemy city and declares a siege.
   The city is **besieged** while at least one hostile army is adjacent to it.
2. **Effects on the besieged city:** no food growth, −25% production, fortification does
   not regenerate. After 5 turns it starts losing 1 population every 3 turns (starvation).
3. **Militia:** when the siege begins, the city spawns **militia** units (count = ⌊pop/4⌋ + 1,
   era-scaled), which defend only inside that city.
4. **Siege engines:** each turn the besieging army gains **Siege Progress** (= sum of its
   units' production value). Spend it to build rams, siege towers or catapults, up to 3 per
   siege. Gunpowder-era armies replace these with sappers and artillery.
5. **Assault:** the attacker may start an assault at any time. The battlefield is everything
   within 4 hexes, and the garrison's best melee unit holds the city centre. **Walls have HP**
   (50 per wall tier: a palace gives tier 1, Walls and Castle add one each) and a strength of
   10 + 8 per tier. While they stand, attacking melee units can neither enter nor strike the
   centre. The exception is a unit stepping off a hex next to a friendly **siege tower**.
   Any unit can batter the walls: siege weapons deal ×2 wall damage and **rams** ×3 (rams
   can't attack units, towers can't attack at all). A breach removes the wall defence
   bonus.
6. **Sortie:** the defender may attack the besiegers at any time, which starts a normal field
   battle.
7. **Capture:** hold the city center at the end of an attacker battle turn (see §4.4).

### 4.7 Naval, air, nuclear
- **Naval battles** use the same system on water hexes. Coastal battles let naval ranged
  units support land fights.
- **Fleets** are armies of ships. They sail on water only, and triremes can't leave the coast.
  Fleets and land units never share an army. Land armies **embark** onto coast after Optics and
  cross ocean after Astronomy (embarking or landing costs 2 MP). Embarked armies can't attack,
  and a warship that attacks one **sinks** it.
- **Battlefields include water.** Ships deploy and move only on water hexes, troops only on land.
  Melee can't cross domains; ranged units (frigates, battleships, archers) can.
- **Air units** don't occupy battlefield hexes. They're based in **city hangars** (4 per city) and
  support any battle within their operating range. At each side's first turn of every round, each
  aircraft strikes the enemy ground unit it expects to hurt most. Each enemy fighter can
  **intercept** one striker per round first. Planes that are shot down are lost, and damaged ones
  repair 20 HP per turn in the hangar. AA units and carriers are ⏳.
- **Buying with gold:** 2 gold per missing production point plus 50% of the item's cost,
  rounded to 5. Progress on the current build counts. You can't buy in a besieged city.
- **Missiles and nukes** are world-map strikes. Nukes destroy armies in their radius
  outright and damage cities. Using one triggers diplomatic penalties.

### 4.8 Great Generals & Admirals
Earned from combat XP. They join an army: army cap +1 and +3 CS aura in battle. They can be
expended to build a Citadel.

---

## 5. Victory conditions

| Victory | Condition |
|---|---|
| **Domination** | Own every original capital |
| **Science** | Build the Apollo Program (Space Flight), then 6 parts (3 boosters, cockpit, stasis chamber, engine) in cities with a Factory. The ship launches automatically and lands after 10 turns. Losing your capital scraps the programme. |
| **Culture** | Your accumulated tourism against *every* other major civ exceeds that civ's lifetime culture. Tourism = 3 per great work, + ½ your culture after Radio, doubled after The Internet. |
| **Diplomatic** | The first civ to learn Globalization founds and hosts the World Congress (+1 delegate). Every 10 turns there's a World Leader vote: 1 delegate per major, and each city-state's delegate goes to its ally. A majority of all delegates wins. |
| **Score** | Highest score at the turn limit (default 500, speed-scaled) |

---

## 6. AI

Layered AI; every layer reads the same `GameState` the player sees (no cheating beyond
difficulty yield bonuses):

1. **Grand strategy:** chooses a victory focus from leader personality and game state,
   re-evaluated every 10 turns.
2. **Economic:** city governors (utility scoring for builds and tiles), worker automation,
   tech path chosen by the grand strategy.
3. **Operational:** forms armies to a target composition template, picks war targets by
   threat and opportunity maps, runs sieges.
4. **Tactical battle AI:** scores every legal (move, attack) pair:
   `expected dmg dealt − 0.7 × expected dmg taken + terrain value + kill bonus + objective bonus`.
   It focus-fires, prefers high ground, keeps ranged units behind the line, and retreats when
   the expected outcome falls below a threshold. The same AI runs auto-resolve.
5. **Diplomacy:** opinion model (Civ V-style modifiers), deal evaluator.

---

## 7. Technical architecture

```
crucible-of-ages/
├─ docs/GDD.md                      ← this file
├─ UnityProject/
│  ├─ Packages/manifest.json
│  └─ Assets/Scripts/
│     ├─ Core/   (Crucible.Core.asmdef — NO UnityEngine references)
│     │  ├─ Hex/        HexCoord (axial math, rings, lines, pixel ↔ hex)
│     │  ├─ Random/     DeterministicRng (seeded, serializable)
│     │  ├─ World/      Tile, WorldMap, TerrainRules (movement, cliffs, LOS), MapGenerator
│     │  ├─ Content/    Definitions (UnitDef, TechDef, FactionDef), ContentDatabase, DefaultContent
│     │  ├─ Units/      Unit, Army
│     │  ├─ Empire/     Player, TechTree, City
│     │  ├─ Combat/     CombatResolver, Battlefield, Battle
│     │  ├─ AI/         TacticalBattleAI
│     │  └─ Game/       GameState (commands, sieges), TurnManager, VictoryChecker, GameSetup
│     └─ View/   (Crucible.View.asmdef — Unity MonoBehaviours)
│        ├─ Art/        engine-free low-poly meshes: TerrainArt, UnitModels, CityModels
│        ├─ Icons/      procedural unit symbols (IconArt) and their textures
│        ├─ UI/         UI Toolkit HUD
│        └─ GameBootstrap, HexMapRenderer, CameraRig, GameController, MarkerLayer
└─ tests/Crucible.Core.Tests/   (.NET 8 xUnit, compiles Core sources directly)
```

- **Core is engine-agnostic** and deterministic. The Unity layer reads state and issues
  commands, and never mutates state directly.
- **Commands** (`MoveArmy`, `Attack`, `BattleMove`, `BattleAttack`, `EndBattleTurn`, …) are the
  only way to change state, so a command log works as a replay and as a lockstep MP stream.
- **Content** is defined in C# for now (`DefaultContent`). It moves to JSON or ScriptableObjects
  once the content volume grows, and the core only ever reads `ContentDatabase`.
- **Rendering:** all art is procedural and vertex-coloured, with no imported assets. The map is
  bevelled hex prisms with per-tile colour jitter, beaches, foam and rivers. Decorations
  (woods, palms, dunes, peaks, wonders) live in a second mesh and fold away under fog. Units
  are low-poly miniatures and cities grow with population. Unit symbols are vector shapes
  rasterised at startup into the Civ-style shield flags that the HUD draws over armies. The
  geometry code has no Unity references, so it is unit-tested and can be previewed outside the
  editor. Planned: 16×16 chunking.

---

## 8. Milestones (all in v1, sequenced)

| # | Milestone | Exit criteria |
|---|---|---|
| M0 | **Scaffold** ✅ | Core compiles; hex math, combat formula, battle flow unit-tested; Unity renders a generated map |
| M1 | World map & movement 🟡 | ✅ multi-turn A* pathfinding, standing move orders, army move/merge/split, ZOC, fog of war (LOS-aware sight, hills +1, recon +1), auto-explore for scouts, armies and fleets, auto-improve for workers. ⏳ Edge-following rivers, passing through friendly armies, chunked map mesh |
| M2 | Tactical battles 🟡 | ✅ battlefield gen, editable deployment phase (defender first), 3×3 rounds, reserves, reinforcements entering from their own edge, joining ongoing battles, retreat, auto-resolve, move/target highlights. ⏳ Real battle camera/animations, naval & coastal fields |
| M3 | Economy 🟡 | ✅ tile yields (terrain, hills, features, rivers), citizens & governor, Civ V growth curve, starvation, production queue with overflow, 8 buildings, palace, gold income/maintenance/unit upkeep/bankruptcy, global happiness, culture border growth, auto research, settlers & city founding, civilians captured in battle. ⏳ Workers & improvements, specialists, strategic/luxury resources (moved to M5) |
| M4 | Sieges ✅ | Siege declaration and lifting, militia (stay home, fight only for their city), siege progress → rams/towers/catapults (max 3), walls with HP (50/tier) that block melee from the centre, towers let adjacent infantry climb, rams ×3 / siege ×2 wall damage, breach removes the wall bonus, garrison holds the centre, sorties, AI assaults. ⏳ Gunpowder-era sappers & artillery engines |
| M5 | Tech, policies, resources 🟡 | ✅ tech-gated units/buildings/improvements, army cap progression, 4 policy trees (Tradition, Liberty, Honor, Commerce) with Civ V cost curve, resources on the map (bonus/strategic/luxury), workers & 6 improvements, strategic caps (4 units per connected source), luxuries (+4 happiness each), worker automation, AI policy adoption. ⏳ Remaining Civ V trees & ideologies, roads, pillaging (Khaganate ability) |
| M6 | AI v1 ✅ | Strategic AI: expansion (site scoring, settlers), garrison management, defence, one offensive at a time (rally → march → siege → engines → assault), governor plan (settlers, military target), AI-vs-AI skirmishes end in domination (tested). ⏳ Fog-aware targeting & scouting, diplomacy, spending gold (needs purchasing), multiple fronts |
| M7 | Full-history content 🟡 | ✅ 57 techs across all 8 eras, land units every era, 5 warships, 3 aircraft, rocket artillery, 20 buildings; fleets (triremes coast-only), embarking (Optics / Astronomy), sinking embarked armies, mixed land-sea battlefields, city hangars with air strikes & interception each round, buying with gold (players and AI). ⏳ Wonders, carriers, nukes, unit upgrades, remaining ~25 techs |
| M8 | Remaining systems ✅ | Great people (scientist, engineer, merchant, artist, prophet, general) from building points, faith and battle; religion founding and pressure-based spread with founder/follower beliefs; city-states (maritime, cultured, mercantile, militaristic) with influence, friends/allies, bonuses; World Congress & World Leader votes; tourism from great works and late-era culture; Apollo Program + 6 spaceship parts with flight time. All five victories reachable (tested). ⏳ Missionaries, pantheons & chosen beliefs, Congress resolutions, diplomacy between majors, conquering city-states |
| M9 | Victory & polish 🟡 | ✅ All five victories, save/load (deterministic, tested), diplomacy between majors, UI Toolkit HUD, map scripts with lakes, ice and natural wonders, procedural low-poly art (terrain decorations, unit miniatures, cities) and Civ-style unit flags. ⏳ Balance passes, tutorial, animation, options/menus |

---

## 9. Open questions
- Should battles spanning 3 world turns block the attacker's other armies from moving
  through the battlefield (current rule: yes)?
- Is naval stacking capped separately from land (proposal: same cap)?
- Should Humankind's *War Support* replace Civ V war weariness?
- Mod support: expose `ContentDatabase` loading from JSON at M7?
