using System.Linq;
using Crucible.Core.Game;
using UnityEngine;

namespace Crucible.View
{
    /// <summary>
    /// Entry point. Drop this on an empty GameObject in an empty scene and press Play:
    /// it generates a skirmish, builds the map mesh, and wires up the camera, input and HUD.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        public int seed = 2024;
        public int mapWidth = 48;
        public int mapHeight = 32;
        public Crucible.Core.World.MapScript mapScript = Crucible.Core.World.MapScript.Continents;

        void Start()
        {
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
    }
}
