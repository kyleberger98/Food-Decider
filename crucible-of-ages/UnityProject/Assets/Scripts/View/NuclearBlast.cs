using UnityEngine;

namespace Crucible.View
{
    /// <summary>
    /// A nuclear strike on screen: a white flash of light, a shock ring racing out across the blast
    /// radius and a mushroom cloud that rises, spreads and settles away over a few seconds.
    /// </summary>
    public sealed class NuclearBlast : MonoBehaviour
    {
        const float Duration = 6f;
        float _t;
        float _size;
        Transform _cloud;
        Renderer _ring;
        Light _flash;

        public static void Spawn(Transform parent, Vector3 at, float blastWorldRadius, Mesh cloudMesh, Material cloudMaterial, Material ringMaterial)
        {
            var root = new GameObject("NuclearBlast");
            root.transform.SetParent(parent, false);
            root.transform.position = at;
            var blast = root.AddComponent<NuclearBlast>();
            blast._size = blastWorldRadius;

            var cloud = new GameObject("MushroomCloud");
            cloud.transform.SetParent(root.transform, false);
            cloud.AddComponent<MeshFilter>().sharedMesh = cloudMesh;
            cloud.AddComponent<MeshRenderer>().sharedMaterial = cloudMaterial;
            blast._cloud = cloud.transform;

            var ring = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(ring.GetComponent<Collider>());
            ring.name = "ShockRing";
            ring.transform.SetParent(root.transform, false);
            ring.transform.localPosition = Vector3.up * 0.08f;
            ring.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            blast._ring = ring.GetComponent<Renderer>();
            blast._ring.sharedMaterial = ringMaterial;

            var light = new GameObject("Flash").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.transform.localPosition = Vector3.up * blastWorldRadius;
            light.type = LightType.Point;
            light.color = new Color(1f, 0.9f, 0.7f);
            light.range = blastWorldRadius * 6f;
            blast._flash = light;
            blast.Update();
        }

        void Update()
        {
            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / Duration);

            // The cloud billows up fast, then spreads and sinks into the haze.
            float grow = 1f - Mathf.Pow(1f - Mathf.Clamp01(_t / 1.6f), 3f);
            float settle = Mathf.Clamp01((k - 0.7f) / 0.3f);
            float scale = _size * 1.4f * Mathf.Lerp(0.15f, 1f, grow) * (1f + 0.25f * k);
            _cloud.localScale = new Vector3(scale, scale * (1f - 0.9f * settle), scale);
            var block = new MaterialPropertyBlock();
            float heat = Mathf.Clamp01(1f - _t / 1.2f);
            block.SetColor("_Color", Color.Lerp(Color.white, new Color(2.2f, 1.9f, 1.5f), heat));
            _cloud.GetComponent<Renderer>().SetPropertyBlock(block);

            // Shock ring: out to the edge of the blast in half a second, fading.
            float ringK = Mathf.Clamp01(_t / 0.6f);
            _ring.transform.localScale = Vector3.one * (_size * 2.4f * ringK + 0.1f);
            var ringBlock = new MaterialPropertyBlock();
            ringBlock.SetColor("_Color", new Color(1f, 0.85f, 0.55f, 0.55f * (1f - ringK)));
            _ring.SetPropertyBlock(ringBlock);

            _flash.intensity = 12f * Mathf.Clamp01(1f - _t / 0.8f);
            if (_t >= Duration) Destroy(gameObject);
        }
    }
}
