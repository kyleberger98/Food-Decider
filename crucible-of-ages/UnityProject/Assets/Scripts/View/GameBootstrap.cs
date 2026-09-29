using System.Linq;
using Crucible.Core.Game;
using UnityEngine;

namespace Crucible.View
{
    /// <summary>
    /// Entry point: generates a skirmish, builds the map mesh, and wires up the camera, input and HUD.
    /// Pressing Play in any scene starts a game (see <see cref="AutoStart"/>); add this to a
    /// GameObject yourself to pick the seed, map size and map script in the inspector.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        public int seed = 2024;
        public int mapWidth = 48;
        public int mapHeight = 32;
        public Crucible.Core.World.MapScript mapScript = Crucible.Core.World.MapScript.Continents;

        /// <summary>
        /// Starts a game when Play is pressed in a scene that doesn't have a GameBootstrap yet (such as
        /// the empty scene of a freshly opened project), so no scene setup is needed.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            if (FindFirstObjectByType<GameBootstrap>() != null) return;
            new GameObject("Crucible of Ages").AddComponent<GameBootstrap>();
        }

        void Start()
        {
            LoadArtLibrary();
            var game = GameSetup.NewSkirmish((ulong)seed, mapWidth, mapHeight, script: mapScript);

            var mapGo = new GameObject("WorldMap");
            var mapRenderer = mapGo.AddComponent<HexMapRenderer>();
            mapRenderer.Build(game.Map);

            if (FindFirstObjectByType<Light>() == null)
            {
                var sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.intensity = 1.1f;
                sun.shadows = LightShadows.Soft;
                sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }

            var home = game.Armies.First(a => !game.Player(a.OwnerId).IsAI);
            var rig = CameraRig.Create(mapRenderer.HexToWorld(home.Position));

            var turns = new TurnManager(game, new Crucible.Core.AI.StrategicAI());
            var controller = gameObject.AddComponent<GameController>();
            controller.Init(game, turns, mapRenderer, rig);
            UI.GameHud.Create(controller);
            turns.Start();
        }

        /// <summary>
        /// Blender models exported by Tools/blender/export_assets.py live in Resources/Art as .bytes
        /// files. Anything missing falls back to the procedural art.
        /// </summary>
        static void LoadArtLibrary()
        {
            var library = new Art.ArtLibrary();
            foreach (var asset in Resources.LoadAll<TextAsset>("Art"))
            {
                try { library.Add(asset.name, asset.bytes); }
                catch (System.IO.InvalidDataException e) { Debug.LogWarning($"Skipping art '{asset.name}': {e.Message}"); }
            }
            Art.ArtLibrary.Current = library;
            Debug.Log($"Art library: {library.Count} Blender models.");
        }
    }
}
