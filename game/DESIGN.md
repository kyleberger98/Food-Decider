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
| **Field Aid Station** | Rest site | Heal 30% Resolve **or** promote (upgrade) a card |
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
- Each card tracks **casualties** across the run. On its **3rd** death the card is **lost** from your deck after the combat.

### 3.4 Card types

| Type | Role | StS analogue |
|---|---|---|
| **Unit** | Deploy into an empty slot. Has HP, Firepower, attack type. Fires every end of turn. | (new) |
| **Order** | One-shot effect. | Attack / Skill |
| **Doctrine** | Permanent effect for this combat. | Power |
| **Status** | Junk cards added by enemies (e.g. *Shell Shock*: unplayable, exhausts). | Status |

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

---

## 4. Deck Crafting

### 4.1 Promotions (upgrades)
Same as StS: each card has one upgraded version (`Rifle Squad` → `Rifle Squad+`). Done at Field Aid Stations.

### 4.2 Attachments (small tweaks)
- Found as rewards, bought from the Quartermaster, or from events.
- Applied at a **Workshop** (or free from certain events). **One attachment per card.**
- An attachment stays on the card permanently, including through merges.

| Attachment | Fits | Effect |
|---|---|---|
| **Scope** | Unit / Order with Direct | Becomes Precision |
| **Bayonet** | Unit | +3 Firepower while in front row |
| **Extended Mag** | Unit | Fires twice, −1 Firepower per shot |
| **Sandbags** | Unit | Deploys with 6 Cover |
| **Field Radio** | Any | When played, draw 1 |
| **Ration Pack** | Unit | +4 max HP |

### 4.3 Merging (big upgrades)
At a **Workshop**, pick two cards → they're removed and replaced by one merged card.

- **Recipes** produce named, hand-designed cards:

| A | + B | = Result |
|---|---|---|
| Rifle Squad | Bazooka | **Bazooka Team** — Unit, 7 HP, 8 Direct dmg, +50% vs armor |
| Rifle Squad | Medic | **Combat Medic Squad** — Unit, heals adjacent unit 2/turn |
| Grenade | Grenade | **Satchel Charge** — 14 Indirect dmg to one slot, Expend |
| Smoke Screen | Advance | **Covered Advance** — move a unit + Smoke on its row |
| BAR Gunner | Dig In | **MG Nest** — Unit, Entrenched, 3×3 Direct, back row immune |
| Mortar Team | Spotter | **Fire Mission** — Doctrine: each turn, 6 Indirect to the highest-HP enemy |

- **Generic merge** (no recipe): only two cards of the **same type** can merge. Result: name of A, cost = max(costs), effects of both, attachments of both (if two, keep the first). This keeps merging always useful while recipes are the jackpot.
- Merging thins the deck (2 → 1), which is a real StS-style power lever, so a Workshop visit only allows **one** action.

---

## 5. Prototype Content

### 5.1 Commander: Captain (US Army, 29th Infantry Division)
- **Resolve:** 60 · **CP:** 3/turn · **Starting Supply (gold):** 99
- **Starting Commendation:** *Combat Infantryman Badge* — first Unit deployed each combat gains 4 Cover.

**Starter deck (10):**

| Card | Type | Cost | Effect |
|---|---|---|---|
| Rifle Squad ×3 | Unit | 1 | 6 HP, 3 Direct |
| BAR Gunner ×1 | Unit | 2 | 5 HP, 2×2 Direct |
| Open Fire! ×3 | Order | 1 | A unit fires immediately with +3 damage |
| Dig In ×3 | Order | 1 | Give a unit 5 Cover |

### 5.2 Reward card pool (~20)

| Card | Type | Cost | Effect |
|---|---|---|---|
| Medic | Unit | 1 | 4 HP, no attack; heals adjacent units 2 at end of turn |
| Mortar Team | Unit | 2 | Back row only, 4 HP, 4 Indirect |
| Sniper | Unit | 2 | Back row only, 3 HP, 6 Precision |
| Bazooka | Unit | 2 | 4 HP, 6 Direct, +50% vs Armor |
| Sherman | Unit | 3 | Front only, 18 HP, 6 Direct, Armor |
| Spotter | Unit | 1 | Back row, 3 HP; your Indirect attacks deal +2 |
| Grenade | Order | 1 | 6 Indirect to one slot |
| Smoke Screen | Order | 1 | Smoke on your front row |
| Advance | Order | 0 | Move a unit back→front; it fires |
| Fall Back | Order | 0 | Move a unit front→back; heal 3 |
| Suppressing Fire | Order | 1 | Suppress 2 all enemy front row |
| Flank | Order | 1 | A unit fires as Precision this turn |
| Reinforcements | Order | 1 | Heal all units 4 |
| Requisition | Order | 0 | Draw 2, Expend |
| Artillery Barrage | Order | 3 | 8 Indirect to every enemy, Expend |
| Hold the Line | Doctrine | 2 | Front-row units gain 2 Cover each turn |
| Combined Arms | Doctrine | 1 | When you deploy a Unit, draw 1 |
| Air Superiority | Doctrine | 3 | Start of turn: 4 Indirect to a random enemy |
| Field Promotion | Order | 1 | A unit gets +2 Firepower permanently this combat |
| Rally | Order | 1 | Remove Suppressed/Pinned from all units, +1 CP |

### 5.3 Enemies (Act 1: Normandy bocage)

| Enemy | HP | Pattern |
|---|---|---|
| Grenadier | 18 | Front; 6 Direct |
| MG42 Team | 26 | Front; 2×4 Direct; every 3rd turn *Suppress* your front row + 4 Direct |
| Mortar Crew | 16 | Back; alternates 7 Indirect at your highest-HP unit / 4 Cover |
| Sniper | 12 | Back; alternates 9 Precision at your back row (or Resolve if empty) / 3 Cover |
| Panzerfaust Team | 6 | Front; 10 Direct to your Armor unit if any, else 4 |
| Feldwebel (Officer) | 12 | Back; buffs allies +2 dmg, calls Reinforcement (Grenadier) every 3 turns |
| **Elite: Fortified Bunker** | 40 | Front, Entrenched 8 Cover; 3×4 Direct; immune to Precision |
| **Boss: Tiger I** | 120 | Front, Armor (Direct −3). Cycle: *Main Gun* 18 to one slot → *Coaxial MG* 4×3 → *Reposition* (gains 15 Cover, Pins a unit). At 50% HP calls infantry support. |

### 5.4 Commendations (relics, ~6)
- *Purple Heart* — heal 6 Resolve after each combat.
- *Bronze Star* — +1 CP on the first turn of each combat.
- *Lucky Zippo* — the first unit killed each combat survives at 1 HP.
- *Field Manual* — draw 1 extra card on turn 1.
- *Spare Parts Crate* — Workshops allow two actions.
- *Signal Flare* — Start of combat: apply Exposed 1 to all enemies.

### 5.5 Intel events (~3)
- **Abandoned Farmhouse** — Search (gain an attachment, 50%: take 8 Resolve dmg) / Move on.
- **French Resistance Contact** — Pay 50 Supply for a rare card / Share rations: heal 10 / Leave.
- **Stragglers** — Add a free Rifle Squad / Lose 5 Resolve, gain a Commendation.

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
3. **M3 — Run loop:** map, rewards, Field Aid Station, Quartermaster, events, boss.
4. **M4 — Crafting:** Workshop, attachments, merges.
5. **M5 — Content & balance:** full card pool, elite, Tiger boss, commendations.

---

## 8. Decisions & Open Questions

**Decided**
- Dead units' cards return to the discard pile. ✅
- Cards are lost after 3 casualties in a run. ✅
- Title: *Hedgerow*. ✅

**Open**
1. **Attrition balance:** the greedy-bot sim loses ~2 units per fight, so Resolve barely drops and the real cost is casualties. With the 3-casualty rule, that may drain decks too fast over 15 floors. Revisit in M5 (e.g. a Field Aid Station option to clear casualties, or fewer deaths per fight).
2. **Field cap:** is 5 unit slots enough, or should some cards *reinforce* an existing unit instead of taking a slot?
