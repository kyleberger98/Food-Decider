using System.Collections.Generic;
using System.Linq;
using Crucible.Core.Combat;
using Crucible.Core.Game;
using Crucible.Core.Units;
using Crucible.View.Art;
using UnityEngine;

namespace Crucible.View
{
    /// <summary>
    /// World-space visuals: low-poly settlements (<see cref="CityModels"/>), army and battle-unit
    /// miniatures (<see cref="UnitModels"/>: squads, riders, engines, ships, aircraft), resource and
    /// improvement markers, and translucent battlefield / highlight tiles. Unit flags and health bars
    /// are drawn by the HUD on top.
    /// </summary>
    public sealed class MarkerLayer : MonoBehaviour
    {
        static readonly Color[] PlayerColors =
        {
            new Color(0.20f, 0.45f, 0.95f),
            new Color(0.90f, 0.25f, 0.20f),
            new Color(0.95f, 0.80f, 0.20f),
            new Color(0.55f, 0.30f, 0.80f),
        };

        HexMapRenderer _map;
        Material _overlayMaterial;
        readonly Dictionary<int, GameObject> _armies = new Dictionary<int, GameObject>();
        readonly Dictionary<int, GameObject> _cities = new Dictionary<int, GameObject>();
        readonly Dictionary<int, GameObject> _units = new Dictionary<int, GameObject>();
        readonly Dictionary<int, string> _armyLooks = new Dictionary<int, string>();
        readonly Dictionary<int, string> _cityLooks = new Dictionary<int, string>();
        readonly Dictionary<string, Mesh> _meshCache = new Dictionary<string, Mesh>();
        readonly List<GameObject> _battleTiles = new List<GameObject>();
        readonly List<GameObject> _highlightPool = new List<GameObject>();
        readonly List<GameObject> _terrainMarkers = new List<GameObject>();
        int _shownBattleId = -1;

        public void Init(HexMapRenderer map)
        {
            _map = map;
            _overlayMaterial = new Material(Shader.Find("Sprites/Default"));
        }

        public static Color ColorOf(int playerId) => PlayerColors[playerId % PlayerColors.Length];

        /// <summary>Brings markers in line with the game state. Cheap enough to call every frame at this scale.</summary>
        /// <param name="viewer">Fog of war to respect (the human player's), or null to show everything.</param>
        public void Sync(GameState game, PlayerVisibility viewer, Battle focusBattle, Army selectedArmy, Unit selectedUnit)
        {
            SyncCities(game, viewer);
            SyncArmies(game, viewer, focusBattle, selectedArmy);
            SyncBattle(focusBattle, selectedUnit);
        }

        void SyncCities(GameState game, PlayerVisibility viewer)
        {
            foreach (var city in game.Cities)
            {
                if (!_cities.TryGetValue(city.Id, out var go))
                {
                    go = Model($"City {city.Name}");
                    go.transform.position = _map.HexToWorld(city.Position);
                    _cities[city.Id] = go;
                    _map.ClearDecorations(city.Position); // no forest growing through the houses
                }
                go.SetActive(viewer == null || viewer.IsExplored(city.Position));
                int walls = game.Map.Get(city.Position).WallTier;
                bool capital = Crucible.Core.Economy.EconomyRules.IsCapital(game, city);
                int size = Mathf.Min(8, city.Population);
                // Cities rebuild in the style of their owner's age (mud brick, stone, brick, concrete, glass).
                var era = game.Player(city.OwnerId).Tech.CurrentEra;
                string look = $"{city.OwnerId}|{size}|{walls}|{capital}|{era}";
                if (!_cityLooks.TryGetValue(city.Id, out var old) || old != look)
                {
                    _cityLooks[city.Id] = look;
                    go.GetComponent<MeshFilter>().sharedMesh = Cached("city" + city.Id + "|" + look,
                        () => CityModels.Build(size, walls, capital, ToRgb(ColorOf(city.OwnerId)), city.Id, era));
                }
            }
        }

        void SyncArmies(GameState game, PlayerVisibility viewer, Battle focusBattle, Army selected)
        {
            var alive = new HashSet<int>();
            foreach (var army in game.Armies)
            {
                alive.Add(army.Id);
                if (!_armies.TryGetValue(army.Id, out var go))
                {
                    go = Model($"Army {army.Id}");
                    _armies[army.Id] = go;
                }
                // Armies unfold into individual units while their battle is on screen.
                bool seen = viewer == null || viewer.IsVisible(army.Position);
                bool unfolded = focusBattle != null && focusBattle.Attacker.Armies.Concat(focusBattle.Defender.Armies).Contains(army);
                go.SetActive(seen && !unfolded);
                if (!go.activeSelf) continue;

                // The army is shown by its lead unit (strongest military unit, else its first civilian).
                var lead = LeadUnit(army);
                if (lead == null) continue;
                string look = $"{lead.Def.Id}|{army.OwnerId}";
                if (!_armyLooks.TryGetValue(army.Id, out var old) || old != look)
                {
                    _armyLooks[army.Id] = look;
                    go.GetComponent<MeshFilter>().sharedMesh = UnitMesh(lead.Def, army.OwnerId);
                    go.transform.localScale = Vector3.one * UnitScale(lead.Def);
                }
                go.transform.position = _map.HexToWorld(army.Position);
                go.transform.rotation = Quaternion.Euler(0f, FacingYaw, 0f);
                bool embarked = !army.IsNaval && game.IsEmbarked(army);
                // Selected: brightened. Embarked troops ride in pale boats (pale tint).
                Tint(go, army == selected ? new Color(1.35f, 1.35f, 1.35f) : embarked ? new Color(0.8f, 0.9f, 1.1f) : Color.white);
            }
            foreach (var id in _armies.Keys.Where(id => !alive.Contains(id)).ToList())
            {
                Destroy(_armies[id]);
                _armies.Remove(id);
                _armyLooks.Remove(id);
            }
        }

        /// <summary>
        /// Blender formations (Civ V style: no base, life-like proportions) are drawn larger so they
        /// read at strategy zoom; the procedural stand-ins already carry a base and their own scale.
        /// </summary>
        static float UnitScale(Crucible.Core.Content.UnitDef def) => UnitModels.AuthoredName(def) != null ? 1.6f : 1f;

        /// <summary>Models face the default camera, turned a little for a three-quarter view.</summary>
        const float FacingYaw = 200f;

        public static Unit LeadUnit(Army army) =>
            army.Units.Where(u => u.Def.IsMilitary && u.Def.GreatPerson == Crucible.Core.Content.GreatPersonType.None)
                .OrderByDescending(u => Mathf.Max(u.Def.CombatStrength, u.Def.RangedStrength)).FirstOrDefault()
            ?? army.Units.FirstOrDefault();

        /// <summary>A unit's model in its owner's colour: its own era model (unit_knight) or its symbol's.</summary>
        Mesh UnitMesh(Crucible.Core.Content.UnitDef def, int ownerId) =>
            Cached($"unit|{def.Id}|{ownerId}", () => UnitModels.Build(def, ToRgb(ColorOf(ownerId))));

        Mesh Cached(string key, System.Func<MeshData> build)
        {
            if (!_meshCache.TryGetValue(key, out var mesh))
                _meshCache[key] = mesh = HexMapRenderer.ToMesh(build(), key);
            return mesh;
        }

        void OnDestroy()
        {
            foreach (var mesh in _meshCache.Values) Destroy(mesh);
            _meshCache.Clear();
        }

        static Rgb ToRgb(Color c) => new Rgb(c.r, c.g, c.b);

        GameObject Model(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>().sharedMaterial = HexMapRenderer.LowPolyMaterial;
            return go;
        }

        void SyncBattle(Battle battle, Unit selected)
        {
            int battleId = battle?.Id ?? -1;
            if (battleId != _shownBattleId)
            {
                foreach (var t in _battleTiles) Destroy(t);
                _battleTiles.Clear();
                _shownBattleId = battleId;
                if (battle != null)
                {
                    foreach (var hex in battle.Tiles)
                    {
                        var tile = GameObject.CreatePrimitive(PrimitiveType.Quad);
                        Destroy(tile.GetComponent<Collider>());
                        tile.name = "BattleTile";
                        tile.transform.SetParent(transform, false);
                        tile.transform.position = _map.HexToWorld(hex) + Vector3.up * 0.03f;
                        tile.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                        tile.transform.localScale = Vector3.one * _map.hexSize * 1.4f;
                        var r = tile.GetComponent<Renderer>();
                        r.sharedMaterial = _overlayMaterial;
                        bool zoneA = battle.Attacker.DeploymentZone.Contains(hex);
                        bool zoneD = battle.Defender.DeploymentZone.Contains(hex);
                        var c = zoneA ? ColorOf(battle.Attacker.Player.Id) : zoneD ? ColorOf(battle.Defender.Player.Id) : Color.white;
                        c.a = 0.18f;
                        var block = new MaterialPropertyBlock();
                        block.SetColor("_Color", c);
                        r.SetPropertyBlock(block);
                        _battleTiles.Add(tile);
                    }
                }
            }

            var deployed = new HashSet<int>();
            if (battle != null)
            {
                foreach (var unit in battle.AllDeployedUnits)
                {
                    deployed.Add(unit.Id);
                    if (!_units.TryGetValue(unit.Id, out var go))
                    {
                        go = Model(unit.Def.Name);
                        go.GetComponent<MeshFilter>().sharedMesh = UnitMesh(unit.Def, unit.OwnerId);
                        go.transform.localScale = Vector3.one * 0.9f * UnitScale(unit.Def);
                        _units[unit.Id] = go;
                    }
                    go.transform.position = _map.HexToWorld(battle.PositionOf(unit).Value);
                    // Face the enemy: attackers look toward the defenders' zone and vice versa.
                    bool attacker = battle.Attacker.Player.Id == unit.OwnerId;
                    var foe = battle.DeployedUnits(attacker ? BattleSideId.Defender : BattleSideId.Attacker).FirstOrDefault();
                    if (foe != null)
                    {
                        var to = _map.HexToWorld(battle.PositionOf(foe).Value) - go.transform.position;
                        to.y = 0;
                        if (to.sqrMagnitude > 0.01f) go.transform.rotation = Quaternion.LookRotation(to);
                    }
                    // Selected: bright. Wounded units darken as they lose health.
                    float hp = unit.Hp / (float)Unit.MaxHp;
                    Tint(go, unit == selected ? new Color(1.4f, 1.4f, 1.4f) : Color.Lerp(new Color(0.35f, 0.3f, 0.3f), Color.white, 0.3f + 0.7f * hp));
                }
            }
            foreach (var id in _units.Keys.Where(id => !deployed.Contains(id)).ToList())
            {
                Destroy(_units[id]);
                _units.Remove(id);
            }
        }

        /// <summary>
        /// Resources (small spheres: green bonus, red strategic, violet luxury, glowing yellow-green
        /// uranium) and improvements (flat tan slabs) on explored hexes, and a sickly haze over fallout.
        /// Resources the viewing player hasn't discovered (uranium before Atomic Theory) stay hidden.
        /// Rebuilt only when the map, fog or the viewer's research changes.
        /// </summary>
        public void SyncTerrainMarkers(GameState game, PlayerVisibility viewer, Crucible.Core.Empire.Player knower = null)
        {
            foreach (var m in _terrainMarkers) Destroy(m);
            _terrainMarkers.Clear();
            foreach (var t in game.Map.Tiles)
            {
                if (viewer != null && !viewer.IsExplored(t.Coord)) continue;
                var center = _map.HexToWorld(t.Coord);
                if (t.Improvement != Crucible.Core.World.ImprovementType.None)
                {
                    var slab = Primitive(PrimitiveType.Cube, t.Improvement.ToString());
                    slab.transform.localScale = new Vector3(0.45f, 0.05f, 0.45f);
                    slab.transform.position = center + new Vector3(-0.3f, 0.03f, -0.3f);
                    Tint(slab, ImprovementColor(t.Improvement));
                    _terrainMarkers.Add(slab);
                }
                if (t.Fallout > 0)
                {
                    var haze = Primitive(PrimitiveType.Quad, "Fallout");
                    haze.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                    haze.transform.localScale = Vector3.one * _map.hexSize * 1.5f;
                    haze.transform.position = center + Vector3.up * 0.06f;
                    haze.GetComponent<Renderer>().sharedMaterial = _overlayMaterial;
                    Tint(haze, new Color(0.55f, 0.8f, 0.1f, 0.12f + 0.25f * t.Fallout / Crucible.Core.Game.GameState.FalloutTurns));
                    _terrainMarkers.Add(haze);
                }
                bool known = knower == null || Crucible.Core.Economy.Improvements.Knows(knower, t.Resource);
                if (t.Resource != Crucible.Core.World.ResourceType.None && known)
                {
                    var orb = Primitive(PrimitiveType.Sphere, t.Resource.ToString());
                    orb.transform.localScale = Vector3.one * 0.2f;
                    orb.transform.position = center + new Vector3(0.35f, 0.12f, 0.25f);
                    var kind = Crucible.Core.Economy.Improvements.KindOf(t.Resource);
                    Tint(orb, t.Resource == Crucible.Core.World.ResourceType.Uranium ? new Color(0.75f, 1.2f, 0.2f)
                        : kind == Crucible.Core.Economy.ResourceKind.Strategic ? new Color(0.85f, 0.25f, 0.2f)
                        : kind == Crucible.Core.Economy.ResourceKind.Luxury ? new Color(0.7f, 0.35f, 0.9f)
                        : new Color(0.4f, 0.85f, 0.35f));
                    _terrainMarkers.Add(orb);
                }
            }
        }

        /// <summary>Plays a nuclear strike: flash, shock ring and mushroom cloud sized to the blast.</summary>
        public void Detonate(Crucible.Core.Hex.HexCoord at, int radius)
        {
            var mesh = Cached("fx|mushroom", EffectModels.MushroomCloud);
            NuclearBlast.Spawn(transform, _map.HexToWorld(at), _map.hexSize * (radius + 0.6f), mesh, HexMapRenderer.LowPolyMaterial, _overlayMaterial);
        }

        static Color ImprovementColor(Crucible.Core.World.ImprovementType i)
        {
            switch (i)
            {
                case Crucible.Core.World.ImprovementType.Farm: return new Color(0.86f, 0.78f, 0.35f);
                case Crucible.Core.World.ImprovementType.Mine: return new Color(0.35f, 0.33f, 0.36f);
                case Crucible.Core.World.ImprovementType.Pasture: return new Color(0.55f, 0.75f, 0.35f);
                case Crucible.Core.World.ImprovementType.Plantation: return new Color(0.3f, 0.55f, 0.25f);
                case Crucible.Core.World.ImprovementType.Camp: return new Color(0.6f, 0.42f, 0.25f);
                default: return new Color(0.75f, 0.62f, 0.4f);
            }
        }

        /// <summary>Shows translucent hex highlights (move range, targets, deployment zone); pooled.</summary>
        public void ShowHighlights(IEnumerable<(Crucible.Core.Hex.HexCoord hex, Color color)> highlights)
        {
            int used = 0;
            foreach (var (hex, color) in highlights)
            {
                if (used == _highlightPool.Count)
                {
                    var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    Destroy(q.GetComponent<Collider>());
                    q.name = "Highlight";
                    q.transform.SetParent(transform, false);
                    q.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                    q.transform.localScale = Vector3.one * _map.hexSize * 1.1f;
                    q.GetComponent<Renderer>().sharedMaterial = _overlayMaterial;
                    _highlightPool.Add(q);
                }
                var go = _highlightPool[used++];
                go.SetActive(true);
                go.transform.position = _map.HexToWorld(hex) + Vector3.up * 0.05f;
                Tint(go, color);
            }
            for (int i = used; i < _highlightPool.Count; i++) _highlightPool[i].SetActive(false);
        }

        GameObject Primitive(PrimitiveType type, string name)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>()); // picking raycasts only against the map
            go.name = name;
            go.transform.SetParent(transform, false);
            return go;
        }

        static void Tint(GameObject go, Color c)
        {
            var r = go.GetComponent<Renderer>();
            var block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetColor("_Color", c);
            r.SetPropertyBlock(block);
        }
    }
}
