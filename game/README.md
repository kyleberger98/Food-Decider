# Hedgerow

A WWII deck-building roguelike in the style of Slay the Spire, built in Godot 4.3. See [DESIGN.md](DESIGN.md).

**Status:** M2 done. You can play fights back to back: your deck, Resolve and casualties carry over, and you pick 1 of 3 cards after each win. Graphics are placeholders.

![Combat screen](docs/combat.png)

## Play

Open `game/project.godot` in Godot 4.3+ and press **F5** (or run `godot --path game`).

- **Click** a card, then click a highlighted target, **or drag** the card onto the target.
- Cards with no target (Smoke Screen, Suppressing Fire…): click them twice, or drop them on the battlefield.
- **Right-click / Esc:** cancel. **Space / Enter / End Turn:** end your turn.
- Each enemy shows its next move ("Intent") above its name.

## Run the tests

```sh
GODOT=/path/to/godot game/tests/run.sh
```

## UI smoke test and screenshots

These need a display. On a headless Linux box, run them under `xvfb-run`.

```sh
xvfb-run godot --path game -s res://tools/ui_smoke.gd          # clicks and drags through the real UI
xvfb-run godot --path game -s res://tools/screenshot.gd -- out.png [seed] [turns] [victory]
```

## Balance simulator

A greedy bot plays the starter deck against every encounter:

```sh
godot --headless --path game -s res://tools/sim.gd -- 200
```

## Layout

- `data/`: cards and enemies as JSON. To add content, edit these files; no code needed.
- `core/`: game logic. Plain GDScript with no scenes, so it can be tested.
- `ui/`: the combat screen, built in code (`main.tscn` is the entry scene).
- `tests/`: headless test suite.
- `tools/`: dev scripts.
