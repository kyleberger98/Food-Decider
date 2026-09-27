# Crucible of Ages

A Civilization V–style 4X with Humankind-style tactical combat, built in Unity (working title).
The full design is in **[docs/GDD.md](docs/GDD.md)**.

## Layout

| Path | What |
|---|---|
| `docs/GDD.md` | Game design document: systems, combat formulas, AI, architecture, milestones |
| `UnityProject/Assets/Scripts/Core` | Engine-agnostic, deterministic simulation (`Crucible.Core`): hex math, map gen, units, armies, battles, sieges, AI, victory |
| `UnityProject/Assets/Scripts/View` | Unity presentation (`Crucible.View`): hex mesh, camera, input, HUD, placeholder markers |
| `UnityProject/Assets/Shaders` | Vertex-colour lit shader for the low-poly map |
| `tests/Crucible.Core.Tests` | xUnit tests that compile the Core sources directly, so no Unity is needed |

## Run the tests

```bash
cd tests/Crucible.Core.Tests
dotnet test
```

These cover the hex math, map generation, cliffs, LOS, the combat formula and modifiers,
battle flow (rounds, turns, reserves, ZOC, retreat, sieges), tactical AI determinism,
army caps, and the victory conditions.

## Open in Unity

1. Unity **2022.3 LTS or newer**, **Built-in Render Pipeline** (the default 3D template).
2. In Unity Hub, choose **Add → Add project from disk** and pick `UnityProject/`. Unity generates
   `ProjectSettings/`, `Packages/` and the `.meta` files on first open. Commit them afterwards.
3. Create an empty scene, add an empty GameObject, attach **`GameBootstrap`**, and press **Play**.
4. If you get Input errors, set *Project Settings → Player → Active Input Handling* to **Both**
   (the scaffold uses the legacy `Input` API).
5. For player builds, add `Crucible/VertexColorLit` to *Graphics → Always Included Shaders*.

### Controls

| Mode | Input |
|---|---|
| World | Click your army (blue), hover a hex to see the route and turns, and click to march there (multi-turn orders continue automatically). Click an adjacent enemy or city to attack. **Enter** ends the turn. |
| Battle | Click a unit, then a hex to move or an enemy to attack. Hover an enemy to see the combat breakdown. **Space** ends the battle turn, **R** retreats. |
| Camera | WASD/arrows pan, Q/E rotate, mouse wheel zooms |

## Status

Milestone **M0 (scaffold)** is done. **M1** is mostly done: pathfinding, move orders and fog of war. See GDD §8 for the milestone plan. The AI player is passive
for now: it defends with the tactical AI but does not start wars until M6.
