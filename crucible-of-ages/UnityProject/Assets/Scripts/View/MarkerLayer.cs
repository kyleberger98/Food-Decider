using System.Collections.Generic;
using System.Linq;
using Crucible.Core.Combat;
using Crucible.Core.Game;
using Crucible.Core.Units;
using UnityEngine;

namespace Crucible.View
{
    /// <summary>
    /// Placeholder visuals: cylinders for armies, cubes for cities, capsules for units deployed in a
    /// battle, and translucent tiles over the battlefield. Replaced by real models in M2/M3.
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
        readonly List<GameObject> _battleTiles = new List<GameObject>();
        readonly List<GameObject> _highlightPool = new List<GameObject>();
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
                    go = Primitive(PrimitiveType.Cube, $"City {city.Name}");
                    go.transform.localScale = new Vector3(0.7f, 0.5f, 0.7f);
                    go.transform.position = _map.HexToWorld(city.Position) + Vector3.up * 0.25f;
                    _cities[city.Id] = go;
                }
                go.SetActive(viewer == null || viewer.IsExplored(city.Position));
                Tint(go, ColorOf(city.OwnerId) * 0.8f);
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
                    go = Primitive(PrimitiveType.Cylinder, $"Army {army.Id}");
                    _armies[army.Id] = go;
                }
                // Armies unfold into individual units while their battle is on screen.
                bool seen = viewer == null || viewer.IsVisible(army.Position);
                bool unfolded = focusBattle != null && focusBattle.Attacker.Armies.Concat(focusBattle.Defender.Armies).Contains(army);
                go.SetActive(seen && !unfolded);
                float h = 0.1f + 0.06f * army.Count;
                go.transform.localScale = new Vector3(0.55f, h, 0.55f);
                go.transform.position = _map.HexToWorld(army.Position) + Vector3.up * (h + 0.02f);
                Tint(go, army == selected ? Color.white : ColorOf(army.OwnerId));
            }
            foreach (var id in _armies.Keys.Where(id => !alive.Contains(id)).ToList())
            {
                Destroy(_armies[id]);
                _armies.Remove(id);
            }
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
                        go = Primitive(PrimitiveType.Capsule, unit.Def.Name);
                        go.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);
                        _units[unit.Id] = go;
                    }
                    go.transform.position = _map.HexToWorld(battle.PositionOf(unit).Value) + Vector3.up * 0.3f;
                    var c = ColorOf(unit.OwnerId);
                    Tint(go, unit == selected ? Color.white : Color.Lerp(Color.black, c, 0.35f + 0.65f * unit.Hp / (float)Unit.MaxHp));
                }
            }
            foreach (var id in _units.Keys.Where(id => !deployed.Contains(id)).ToList())
            {
                Destroy(_units[id]);
                _units.Remove(id);
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
