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
| Cities | Click your city for its panel (growth, borders, production); pick what to build from the list, or **Buy** it with gold. **F** founds a city with a selected settler. **T** cycles research. **P** opens social policies. |
| Great people & city-states | With a great person selected, **V** uses their gift (Great Generals lead armies instead: merge them in). Click a city-state to see influence and gift gold. |
| Workers | With a worker selected: **I** builds the best improvement on its hex (moving cancels), **U** toggles automation. |
| Deployment | Click a unit, then a blue hex of your zone (swaps with friends). **Space** confirms. |
| Battle | Click a unit, then a green hex to move or a red enemy to attack. Hover an enemy to see the combat breakdown. **Space** ends the battle turn, **R** retreats, **X** auto-resolves the round, **B** batters the walls with the selected unit. |
| Reinforce | With an army next to an ongoing battle selected, click a battlefield hex to join it. |
| Siege | Next to an enemy city, **G** declares a siege (militia rise, the city stops growing). The siege panel spends siege progress on rams, siege towers and catapults. Click the city to assault it. |
| Camera | WASD/arrows pan, Q/E rotate, mouse wheel zooms |
| Saving | **F5** quick-saves, **F9** quick-loads (`quicksave.crucible` in Unity's persistent data folder) |

## Status

Milestone **M0 (scaffold)** is done. **M1–M8** are mostly done: pathfinding, move orders, fog of war, deployment, reinforcements, auto-resolve, and the city economy (growth, production, buildings, gold, happiness, borders, research, settlers) sieges (walls, engines, militia, sorties), resources, workers and social policies, fleets, embarking, coastal battles, air power and buying with gold across all eight eras, and great people, religion, city-states, the World Congress, tourism and the spaceship. All five victory conditions can now be won. See GDD §8 for the milestone plan. The AI opponent now
expands, builds armies, besieges and assaults your cities (`StrategicAI`). The tests include
AI-vs-AI skirmishes that end in domination.
