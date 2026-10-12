# HEDGEROW — Game Design Doc (Prototype)

*Working title. A deck-building roguelike in the Slay the Spire mold, set on the WWII Western Front. You are a US Army company commander fighting through the Normandy bocage.*

- **Engine:** Godot 4 (GDScript), desktop
- **Tone:** Grounded historical
- **Prototype scope:** 1 commander, ~24 cards, 6 enemies + 1 elite + 1 boss, 1 act, branching map, rewards, workshop crafting

---

## 1. Pillars

1. **The StS loop, intact.** Energy, draw 5, telegraphed enemy intents, branching map, pick-1-of-3 rewards, relics. Players who know StS should be playing in 30 seconds.
2. **You command, you don't fight.** Your cards deploy squads onto the field and give them orders. Your units do the shooting and the dying.
3. **Position matters.** Front and back rows on both sides decide who can be hit and by what.
4. **Your deck is your armory.** Attachments tweak cards; merges fuse two cards into something new.

---

## 2. Core Loop

```
Map node → Encounter / Event / Shop / Rest / Workshop → Reward → Map … → Boss
```

A run is one act (prototype) of ~15 floors on a branching map, ending in a boss fight.

### Map nodes

| Node | StS equivalent | What happens |
|---|---|---|
| **Skirmish** | Monster | Standard fight |
| **Strongpoint** | Elite | Hard fight, guaranteed Commendation (relic) |
| **Field Aid Station** | Rest site | Heal 30% Resolve **or** promote (upgrade) a card **or** rotate troops (clear a card's casualties) |
| **Workshop** | — (new) | Apply an attachment **or** merge two cards |
| **Quartermaster** | Shop | Buy cards, attachments, commendations; remove a card |
| **Intel** | Event (?) | Text event with choices |
| **Boss** | Boss | Act boss |

---

## 3. Combat

### 3.1 The battlefield

```
   YOUR SIDE                          ENEMY SIDE
 [Back 1][Back 2]  [Front 1][Front 2][Front 3]   |   [Front 1][Front 2][Front 3]  [Back 1][Back 2]
      HQ (you)
```

- Each side has a **Front row (3 slots)** and a **Back row (2 slots)**.
- **You** are the HQ behind your lines. Your HP is **Resolve** (starts 60). Resolve 0 = run over.
- Units persist on the field between turns until killed.

### 3.2 Turn structure

1. **Start of turn:** gain 3 **Command Points (CP)**, draw 5. Your units' Cover resets to 0 (unless Entrenched).
2. **Your phase:** play cards in any order.
3. **End of turn:** discard hand. **Your units fire** left-to-right (front row, then back row) at their default targets. Medics heal.
4. **Enemy phase:** each enemy executes its shown intent (attack, barrage, dig in, suppress…), then shows a new one.

Your units fire automatically; enemies act only through their telegraphed intents, exactly like StS.

**Default targets:** Direct fire hits the unit in the same lane (slot), else the leftmost legal target. Indirect hits the toughest (highest HP) target. Precision hits the weakest.

### 3.3 Targeting rules (the positional core)

| Attack type | Can target | Examples |
|---|---|---|
| **Direct** | Enemy front row. Back row only if front row is empty. | Rifles, MGs, tank guns |
| **Indirect** | Any enemy slot | Mortars, grenades, artillery |
| **Precision** | Any enemy slot | Snipers, scoped weapons |

- If **your** front row is empty, enemy direct fire hits your **back row**; if that's empty too, it hits **Resolve** directly.
- So front-line units are your "Block." Losing your line is how you lose.
- **Row restrictions:** some units only deploy to one row (Mortar Team: back only; Rifle Squad: either; Sherman: front only).
- **Moving:** Orders like *Advance* / *Fall Back* move a unit between rows. Front row gives your Direct-fire units a damage bonus (+2); back row protects.

### 3.3b Casualties (decided)
- When a unit dies, its card goes to the **discard pile** and can be redeployed.
- Each card tracks **casualties** across the run. On its **3rd** death (4th with *Dog Tags*) the card is **lost** from your deck after the combat.
- Each death also costs **2 Resolve** (morale). *Medevac* pulls a unit out before it dies; the rest stop's *Rotate troops* and the *Field Hospital* event clear casualties.

### 3.4 Card types

| Type | Role | StS analogue |
|---|---|---|
| **Unit** | Deploy into an empty slot. Has HP, Firepower, attack type. Fires every end of turn. | (new) |
| **Order** | One-shot effect. | Attack / Skill |
| **Doctrine** | Permanent effect for this combat. | Power |
| **Status** | Junk cards added by enemies (e.g. *Shell Shock*: unplayable, exhausts). Not used yet. | Status |

### 3.5 Keywords & status effects

| Keyword | Effect | StS analogue |
|---|---|---|
| **Cover X** | Absorbs X damage this round. | Block |
| **Entrenched** | Cover doesn't reset. | Barricade |
| **Suppressed X** | Deals 25% less damage for X turns. | Weak |
| **Exposed X** | Takes 50% more damage for X turns. | Vulnerable |
| **Pinned** | Skips its next attack. | Stun |
| **Bleeding X** | Lose X HP per turn, decreases by 1. | Poison |
| **Smoke** | Direct fire against this row misses this round. | (new) |
| **Expend** | Removed from the deck for this combat after use. | Exhaust |
| **Strength X** | Deals +X damage (enemy officers hand it out). | Strength |

---

## 4. Deck Crafting

### 4.1 Promotions (upgrades)
Same as StS: each card has one upgraded version (`Rifle Squad` → `Rifle Squad+`). Done at Field Aid Stations.

### 4.2 Attachments (small tweaks)
- Found after fights (30% chance; elites always), bought from the Quartermaster (40 Supply), or from events (Farmhouse, Supply Dump). They go into your **kit** (shown in the top bar).
- Fitted at a **Workshop**. **One attachment per card**; fitting a new one returns the old one to your kit.
- An attachment stays on the card permanently, including through merges (if the merged card can't take it, or both inputs had one, the extra goes back to your kit).

| Attachment | Fits | Effect |
|---|---|---|
| **Scope** | Unit with Direct fire | Fires as Precision |
| **Bayonet** | Unit | +3 firepower while in front row |
| **Extended Mag** | Unit with 2+ firepower | Fires one extra time, −1 firepower per shot |
| **Sandbags** | Unit | Deploys with 6 Cover |
| **Field Radio** | Any | When played, draw 1 |
| **Ration Pack** | Unit | +4 max HP |

### 4.3 Merging (big upgrades)
At a **Workshop**, pick two cards → they're removed and replaced by one merged card. A Workshop allows **one** job (attachment or merge); the *Spare Parts Crate* Commendation allows two. The screen lists the recipes your deck can make right now, and previews the result before you confirm.

- **Recipes** produce named cards (order doesn't matter; an upgraded input gives an upgraded result):

| A | + B | = Result |
|---|---|---|
| Rifle Squad | Rifle Squad | **Reinforced Squad**: 1 CP, 12 HP, 4 Direct |
| Rifle Squad | Bazooka | **Bazooka Team**: 2 CP, 9 HP, 8 Direct, anti-armor |
| Rifle Squad | Medic | **Combat Medic Squad**: 1 CP, 10 HP, 3 Direct, heals adjacent 2 |
| BAR Gunner | Dig In | **MG Nest**: 2 CP, 10 HP, 3 Direct ×3, deploys with 4 Cover, Entrenched |
| Grenade | Grenade | **Satchel Charge**: 14 Indirect to one enemy, Expend |
| Smoke Screen | Advance | **Covered Advance**: smoke front row, move a unit up, it fires |
| Dig In | Dig In | **Foxholes**: 8 Cover and Entrenched |
| Open Fire! | Open Fire! | **Volley Fire**: 2 CP, all your units fire |
| Mortar Team | Grenade | **Fire Mission**: Doctrine, 6 Indirect to the toughest enemy each turn |

- **Generic merge** (no recipe): only cards of the **same type**. Cost = the higher cost. Units: A's stats plus half of B's HP and firepower, rows both can use, best traits. Orders/doctrines: both cards' effects and triggers (not allowed if they need different kinds of target). Name: "A & B". Upgrading a generic merge upgrades both halves.
- Merge results can't be merged again. Casualties carry over (the higher of the two).

---|---|---|
| **Scope** | Unit / Order with Direct | Becomes Precision |
| **Bayonet** | Unit | +3 Firepower while in front row |
| **Extended Mag** | Unit | Fires twice, −1 Firepower per shot |
| **Sandbags** | Unit | Deploys with 6 Cover |
| **Field Radio** | Any | When played, draw 1 |
| **Ration Pack** | Unit | +4 max HP |

## 5. Prototype Content

*Tables below are generated from `data/`; the JSON is the source of truth.*

### 5.1 Commander: Captain (US Army, 29th Infantry Division)
- **Resolve:** 60 · **CP:** 3/turn · **Starting Supply:** 99
- **Starting Commendation:** *Combat Infantryman Badge*: the first unit you deploy each combat gains 4 Cover.
- **Morale:** every time one of your units dies you lose 2 Resolve. Casualties cost you twice: Resolve now, and the card itself on its third death.

**Starter deck (10):** 3× Rifle Squad, 1× BAR Gunner, 3× Open Fire!, 3× Dig In.

### 5.2 Cards

| Card | Type | Rarity | CP | Text |
|---|---|---|---|---|
| Rifle Squad | Unit | starter | 1 | Deploy. 8 HP, fires 3 Direct. |
| BAR Gunner | Unit | starter | 2 | Deploy. 7 HP, fires 2 Direct twice. |
| Open Fire! | Order | starter | 1 | A unit fires immediately with +3 damage. |
| Dig In | Order | starter | 1 | Give a unit 5 Cover. |
| Medic | Unit | common | 1 | Deploy. 6 HP. End of turn: heal adjacent units 2. |
| Mortar Team | Unit | common | 2 | Deploy to back row. 6 HP, fires 4 Indirect at the toughest enemy. |
| Sniper | Unit | uncommon | 2 | Deploy to back row. 5 HP, fires 6 Precision at the weakest enemy. |
| Bazooka | Unit | common | 2 | Deploy. 6 HP, fires 6 Direct. Ignores Armor and deals +50% to armored targets. |
| Sherman | Unit | rare | 3 | Deploy to front row. 18 HP, Armor 2, fires 6 Direct. |
| Grenade | Order | common | 1 | Deal 6 Indirect damage to any enemy. |
| Smoke Screen | Order | common | 1 | Smoke your front row: enemy Direct fire against it misses this round. |
| Advance | Order | common | 0 | Move a back-row unit to the front row. It fires. |
| Fall Back | Order | common | 0 | Move a front-row unit to the back row. Heal it 3. |
| Suppressing Fire | Order | common | 1 | Apply 2 Suppressed to every enemy in the front row. |
| Requisition | Order | common | 0 | Draw 2. Expend. |
| Hold the Line | Doctrine | uncommon | 2 | Doctrine. Start of turn: your front-row units gain 2 Cover. |
| Flank | Order | common | 1 | A unit fires immediately as Precision (can hit any enemy). |
| Reinforcements | Order | common | 1 | Heal all your units 4. |
| Rally | Order | uncommon | 0 | Remove Suppressed, Pinned, Exposed and Bleeding from your units. Gain 1 Command. |
| Field Promotion | Order | uncommon | 1 | A unit gains +2 firepower for this combat. |
| Artillery Barrage | Order | rare | 3 | Deal 8 Indirect damage to every enemy. Expend. |
| Combined Arms | Doctrine | uncommon | 1 | Doctrine. Whenever you deploy a unit, draw 1. |
| Air Superiority | Doctrine | rare | 3 | Doctrine. Start of turn: deal 5 Indirect damage to a random enemy. |
| Spotter | Unit | uncommon | 1 | Deploy to back row. 5 HP. Your Indirect attacks deal +2 damage while it's on the field. |
| Combat Engineers | Unit | common | 1 | Deploy. 6 HP, fires 2 Direct. End of turn: adjacent units gain 3 Cover. |
| Lieutenant | Unit | rare | 2 | Deploy to back row. 6 HP, fires 2 Direct. Start of turn: +1 Command while on the field. |
| Flamethrower | Unit | uncommon | 2 | Deploy to front row. 7 HP, fires 4 Direct that ignores Cover and applies 2 Bleeding. |
| Covering Fire | Order | common | 1 | All your units gain 3 Cover. |
| Medevac | Order | uncommon | 1 | Pull a unit off the field: its card returns to your hand. No casualty. |
| Pin Down | Order | common | 1 | Pin any enemy: it skips its next action. |
| Recon Patrol | Order | common | 0 | All enemies are Exposed for 1 turn (take 50% more damage). Draw 1. |
| Bangalore Torpedo | Order | uncommon | 2 | Destroy all Cover on the enemy front row, then deal 6 Indirect damage to each enemy there. |
| Dig Deep | Doctrine | rare | 3 | Doctrine. Your units are Entrenched: their Cover no longer resets. |

Merge-only cards are listed in 4.3.

### 5.3 Enemies (Act 1: Normandy)

Enemy HP grows 4% per floor (the boss doesn't scale). The first three floors draw from the easy fights.

| Enemy | HP | Intent cycle | Notes |
|---|---|---|---|
| Grenadier | 20 | 6 Direct |  |
| MG42 Team | 28 | 4×2 Direct → 4×2 Direct → Suppressed 1 (row) + 4 Direct |  |
| Mortar Crew | 16 | 7 Indirect → +4 Cover |  |
| Sniper | 12 | 9 Precision → +3 Cover |  |
| Panzerfaust Team | 12 | +4 Cover → 8 Direct | anti armor |
| Feldwebel | 16 | Strength 1 (all allies) → 4 Precision → calls 1 Grenadier |  |
| Fortified Bunker | 40 | 4×3 Direct → +6 Cover | Entrenched, starts with 8 Cover, immune precision |
| Tiger I | 85 | 18 Direct → 4×3 Direct → +15 Cover + Pinned 1 (foe toughest) | Armor 3, at half HP: calls 2 Grenadier |
| StuG III | 38 | 10 Direct → +8 Cover → 10 Direct | Armor 2 |
| Fallschirmjäger | 16 | 4×2 Direct → Exposed 1 (row) + 3 Direct |  |
| Flak 88 | 22 | +6 Cover → 14 Indirect |  |

| Fight | Tier | Front row | Back row |
|---|---|---|---|
| Patrol | easy | Grenadier, Grenadier |  |
| Scouts | easy | Grenadier | Sniper |
| Machine-Gun Nest | normal | Grenadier, MG42 Team |  |
| Mortar Line | normal | Grenadier, Grenadier | Mortar Crew |
| Overwatch | normal | Grenadier | Sniper, Mortar Crew |
| Ambush | normal | Panzerfaust Team, Grenadier, Panzerfaust Team |  |
| Command Post | normal | Grenadier, Grenadier | Feldwebel |
| Fortified Bunker | elite | Grenadier, Fortified Bunker, Grenadier |  |
| Sniper Nest | elite | MG42 Team | Sniper, Sniper |
| Assault Gun | normal | Grenadier, StuG III |  |
| Fallschirmjäger Drop | normal | Fallschirmjäger, Fallschirmjäger |  |
| Flak Position | normal | Grenadier, Grenadier | Flak 88 |
| Panzergrenadier Company | elite | Fallschirmjäger, Grenadier, Fallschirmjäger | Feldwebel |
| Tiger I | boss | Tiger I |  |

### 5.4 Commendations (relics)

- *Combat Infantryman Badge* (starter): The first unit you deploy each combat gains 4 Cover.
- *Purple Heart* (common): Heal 6 Resolve after each combat.
- *Bronze Star* (common): +1 Command on the first turn of each combat.
- *Field Manual* (common): Draw 1 extra card on the first turn of each combat.
- *Lucky Zippo* (uncommon): The first unit that would die each combat survives at 1 HP.
- *Signal Flare* (uncommon): At the start of combat, all enemies are Exposed for 1 turn.
- *Spare Parts Crate* (uncommon): Workshops let you do two jobs instead of one.
- *Dog Tags* (uncommon): Cards are lost after 4 casualties instead of 3.
- *Mess Kit* (common): Resting at a Field Aid Station heals 15 more Resolve.
- *Spare Ammo Crate* (common): Your units deal +1 damage.
- *Engineer's Manual* (common): Your units deploy with 2 extra Cover.

### 5.5 Intel events

- **Abandoned Farmhouse**: Search it (gain an attachment; 50% chance: lose 8 Resolve) / Move on
- **French Resistance Contact**: Pay 50 Supply (gain a random rare card) / Share rations (heal 10 Resolve) / Leave
- **Stragglers**: Take them in (add a Rifle Squad) / Send them to scout ahead (lose 5 Resolve, gain a Commendation)
- **Field Hospital**: Pay 30 Supply (clear all casualties in your deck) / Leave
- **Abandoned Supply Dump**: Salvage gear (gain 2 attachments) / Sell the fuel to the motor pool (+60 Supply)
- **Mail Call**: Read the letters (heal 15 Resolve) / Study the manuals (upgrade a random card)
- **Liberated Village**: Celebrate with them (+6 max Resolve, heal 6) / Take the volunteers (add a Combat Engineers card)
- **Captured Officer**: Send him to the rear with the map (gain a Commendation) / Trade the watch with the Quartermaster (+50 Supply)

---

## 6. Technical Architecture (Godot 4)

**Rule:** game logic is plain GDScript classes with no scene/Node dependencies, so it runs and is unit-tested headless. Scenes are a thin view layer.

```
game/
  project.godot
  data/              # JSON content: cards, enemies, recipes, attachments, relics, events
  core/              # Pure logic (RefCounted classes)
    card_db.gd       # loads data/, builds cards, units, enemies
    card_instance.gd # one card in the deck (upgrade, attachment, casualties)
    unit.gd          # anything on the battlefield
    battlefield.gd   # rows, slots, targeting rules
    combat.gd        # seeded turn state machine + effect interpreter, emits events for the UI
    crafting.gd      # attachments + merge recipes
    map_gen.gd       # StS-style branching map
    run_state.gd     # deck, Resolve, supply, relics, floor
  ui/                # Scenes + scripts that observe core/ via signals
  tests/             # Headless tests (tests/run.sh)
  tools/sim.gd       # Greedy-bot balance simulator
```

- Cards are **data-driven**: effects defined in JSON as lists like `[{"op":"damage","amount":6,"type":"indirect","target":"choose_enemy"}]`, interpreted by `effects.gd`. New cards = new JSON, no code.
- Combat emits an event log (`unit_deployed`, `damage_dealt`, …); the UI animates from that log. This also makes combat replayable and testable.
- Art: placeholder colored panels + text for the prototype.

---

## 7. Milestones

1. **M1 — Combat core (headless):** battlefield, targeting, units, starter deck, 4 enemies, tests. ✅
2. **M2 — Combat UI:** playable single fight with drag-to-deploy, intents, rows. ✅ (plus back-to-back fights with card rewards as a stopgap until M3)
3. **M3 — Run loop:** map, rewards, Field Aid Station, Quartermaster, events, boss. ✅
4. **M4 — Crafting:** Workshop, attachments, merges. ✅
5. **M5 — Content & balance:** full card pool, elites, Tiger boss, commendations, morale rule, floor scaling. ✅

---

## 8. Decisions & Open Questions

**Decided**
- Dead units' cards return to the discard pile. ✅
- Cards are lost after 3 casualties in a run. ✅
- Title: *Hedgerow*. ✅

**Balance snapshot (greedy-bot simulator, 300 runs):** wins ~56%, ~2.4 cards lost to casualties per run, ~0.9 merges per run. Most run-ending fights: Tiger I, Panzergrenadier Company, Sniper Nest, Fortified Bunker. The bot is simple (no smart targeting or Medevac), so a human should do better; we may want to make it harder after playtesting.

**Open**
1. **Playtest feel:** the numbers above come from a bot. Real play will show whether units feel too fragile or too safe, and whether the Tiger fight drags.
2. **Field cap:** is 5 unit slots enough, or should some cards *reinforce* an existing unit instead of taking a slot?
3. **More acts:** Act 2 (e.g. Operation Cobra / Falaise Pocket) with new enemies and a new boss.
