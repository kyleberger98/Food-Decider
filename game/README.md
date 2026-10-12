# Hedgerow

A WWII deck-building roguelike in the style of Slay the Spire, built in Godot 4.3. See [DESIGN.md](DESIGN.md).

**Status:** M1 done. The combat rules run headless and have tests. There is no UI yet (that's M2).

## Run the tests

```sh
GODOT=/path/to/godot game/tests/run.sh
```

## Balance simulator

A greedy bot plays the starter deck against every encounter:

```sh
godot --headless --path game -s res://tools/sim.gd -- 200
```

## Layout

- `data/`: cards and enemies as JSON. To add content, edit these files; no code needed.
- `core/`: game logic. Plain GDScript with no scenes, so it can be tested.
- `tests/`: headless test suite.
- `tools/`: dev scripts.
