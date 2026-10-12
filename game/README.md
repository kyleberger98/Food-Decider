# Hedgerow

A WWII deck-building roguelike in the style of Slay the Spire, built in Godot 4.3. See [DESIGN.md](DESIGN.md).

**Status:** M3 done. You can play a full run: a branching map of 12 floors with fights, elites, rest stops, a shop and random events, ending in a fight against a Tiger I tank. Graphics are placeholders. Workshop crafting comes next (M4).

![Map](docs/map.png)
![Combat screen](docs/combat.png)
![Reward, shop, rest stop and event screens](docs/screens.png)

## Play

Open `game/project.godot` in Godot 4.3+ and press **F5** (or run `godot --path game`).

- **Click** a card, then click a highlighted target, **or drag** the card onto the target.
- Cards with no target (Smoke Screen, Suppressing Fire…): click them twice, or drop them on the battlefield.
- **Right-click / Esc:** cancel. **Space / Enter / End Turn:** end your turn.
- Each enemy shows its next move ("Intent") above its name.
- **Map:** click a highlighted node to move there. The bar at the top shows Resolve, Supply, your Commendations (hover them for details) and your deck.

## Run the tests

```sh
GODOT=/path/to/godot game/tests/run.sh
```

## UI smoke test and screenshots

These need a display. On a headless Linux box, run them under `xvfb-run`.

```sh
xvfb-run godot --path game -s res://tools/ui_smoke.gd          # clicks and drags through the real UI
xvfb-run godot --path game -s res://tools/screenshot.gd -- out.png [map|combat|boss|reward|rest|shop|event|end] [seed]
```

## Balance simulator

A simple greedy bot plays the game:

```sh
godot --headless --path game -s res://tools/sim.gd -- fights 200   # starter deck vs each encounter
godot --headless --path game -s res://tools/sim.gd -- runs 300     # full runs through the map
```

## Layout

- `data/`: cards, enemies and encounters, Commendations and events as JSON. To add content, edit these files; no code needed.
- `core/`: game logic. Plain GDScript with no scenes, so it can be tested.
- `ui/`: the combat screen, built in code (`main.tscn` is the entry scene).
- `tests/`: headless test suite.
- `tools/`: dev scripts.
