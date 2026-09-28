using UnityEngine;

namespace Crucible.View
{
    /// <summary>Strategy camera: WASD/arrows pan, mouse wheel zooms, Q/E rotate.</summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public float panSpeed = 14f;
        public float rotateSpeed = 90f;
        public float zoomSpeed = 8f;
        public float minDistance = 6f;
        public float maxDistance = 45f;
        public float pitch = 55f;

        Camera _camera;
        float _distance = 18f;

        /// <summary>When true (pointer over the HUD), the mouse wheel scrolls the UI instead of zooming.</summary>
        public System.Func<bool> BlockZoom = () => false;

        public Camera Camera => _camera;

        public static CameraRig Create(Vector3 focus)
        {
            var rig = new GameObject("CameraRig").AddComponent<CameraRig>();
            rig.transform.position = focus;

            var cam = Camera.main;
            if (cam == null) cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.transform.SetParent(rig.transform, false);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.08f, 0.14f);
            rig._camera = cam;
            rig.ApplyZoom();
            return rig;
        }

        /// <summary>Pan when the mouse touches the screen edge (Civ style). Only while the game window has focus.</summary>
        public bool edgeScroll = true;
        public float edgeMargin = 12f;

        Vector3? _glideTo;
        Vector3 _dragAnchor;

        /// <summary>Glides the camera to look at <paramref name="world"/> (jump to unit, city or notice).</summary>
        public void FocusOn(Vector3 world) => _glideTo = new Vector3(world.x, transform.position.y, world.z);

        void Update()
        {
            if (_glideTo.HasValue)
            {
                transform.position = Vector3.Lerp(transform.position, _glideTo.Value, 1f - Mathf.Exp(-10f * Time.deltaTime));
                if ((transform.position - _glideTo.Value).sqrMagnitude < 0.0004f) _glideTo = null;
            }

            var input = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            if (edgeScroll && Application.isFocused && !BlockZoom())
            {
                var m = Input.mousePosition;
                bool inside = m.x >= 0 && m.y >= 0 && m.x <= Screen.width && m.y <= Screen.height;
                if (inside)
                {
                    if (m.x < edgeMargin) input.x -= 1f; else if (m.x > Screen.width - edgeMargin) input.x += 1f;
                    if (m.y < edgeMargin) input.z -= 1f; else if (m.y > Screen.height - edgeMargin) input.z += 1f;
                }
            }
            if (input.sqrMagnitude > 0.01f) _glideTo = null;

            // Middle-mouse drag pans the map.
            if (Input.GetMouseButtonDown(2)) _dragAnchor = Input.mousePosition;
            if (Input.GetMouseButton(2))
            {
                var delta = Input.mousePosition - _dragAnchor;
                _dragAnchor = Input.mousePosition;
                var yawRot = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
                float perPixel = _distance / Mathf.Max(1, Screen.height) * 1.6f;
                transform.position -= yawRot * new Vector3(delta.x, 0f, delta.y) * perPixel;
                _glideTo = null;
            }

            if (input.sqrMagnitude > 0.01f)
            {
                var yaw = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
                float speed = panSpeed * Mathf.Lerp(0.5f, 2f, Mathf.InverseLerp(minDistance, maxDistance, _distance));
                transform.position += yaw * input.normalized * speed * Time.deltaTime;
            }

            float rotate = (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f);
            if (rotate != 0f) transform.Rotate(0f, rotate * rotateSpeed * Time.deltaTime, 0f, Space.World);

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f && !BlockZoom())
            {
                _distance = Mathf.Clamp(_distance - scroll * zoomSpeed * 0.5f, minDistance, maxDistance);
                ApplyZoom();
            }
        }

        void ApplyZoom()
        {
            if (_camera == null) return;
            var offset = Quaternion.Euler(pitch, 0f, 0f) * new Vector3(0f, 0f, -_distance);
            _camera.transform.localPosition = offset;
            _camera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }
    }
}
