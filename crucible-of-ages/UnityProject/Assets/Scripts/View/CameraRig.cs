using UnityEngine;

namespace Crucible.View
{
    /// <summary>
    /// Strategy camera with Humankind's controls: left-drag grabs and pans the map, middle-drag (or
    /// Q/E) rotates, the mouse wheel zooms smoothly and tilts the view toward the horizon up close,
    /// WASD/arrows pan. Screen-edge panning is on in builds and off in the editor, where the Game
    /// view's edges are too easy to brush against.
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public float panSpeed = 14f;
        public float rotateSpeed = 90f;
        public float dragRotateSpeed = 0.3f;   // degrees per pixel of middle-drag
        public float zoomStep = 0.12f;          // share of the distance per wheel notch
        public float minDistance = 5f;
        public float maxDistance = 45f;
        public float closePitch = 38f;          // looking toward the horizon when zoomed in
        public float farPitch = 62f;            // looking down on the map when zoomed out

        /// <summary>Pixels the mouse must travel with the button held before a click becomes a drag.</summary>
        public const float DragThreshold = 6f;

        Camera _camera;
        float _distance = 18f, _targetDistance = 18f;
        float _yaw, _targetYaw;

        /// <summary>True while the pointer is over the HUD: the wheel scrolls panels and drags don't start.</summary>
        public System.Func<bool> BlockZoom = () => false;

        public Camera Camera => _camera;

        /// <summary>The current (or just released) left press moved far enough to count as a pan, not a click.</summary>
        public bool WasDragged { get; private set; }

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
            rig.ApplyView();
            return rig;
        }

        /// <summary>Pan when the mouse touches the screen edge. Off in the editor by default.</summary>
        public bool edgeScroll = !Application.isEditor;
        public float edgeMargin = 10f;

        Vector3? _glideTo;
        bool _leftHeld, _dragging;
        Vector2 _pressAt;
        Vector3 _grabWorld;
        Vector3 _lastMiddle;

        /// <summary>Glides the camera to look at <paramref name="world"/> (jump to unit, city or notice).</summary>
        public void FocusOn(Vector3 world) => _glideTo = new Vector3(world.x, transform.position.y, world.z);

        void Update()
        {
            if (_glideTo.HasValue)
            {
                transform.position = Vector3.Lerp(transform.position, _glideTo.Value, 1f - Mathf.Exp(-10f * Time.deltaTime));
                if ((transform.position - _glideTo.Value).sqrMagnitude < 0.0004f) _glideTo = null;
            }

            DragPan();
            KeyboardAndEdgePan();

            // Rotate: middle-drag or Q/E, eased.
            if (Input.GetMouseButtonDown(2)) _lastMiddle = Input.mousePosition;
            if (Input.GetMouseButton(2))
            {
                _targetYaw += (Input.mousePosition.x - _lastMiddle.x) * dragRotateSpeed;
                _lastMiddle = Input.mousePosition;
            }
            float rotate = (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f);
            _targetYaw += rotate * rotateSpeed * Time.deltaTime;

            // Zoom: each notch moves a share of the distance, so it feels the same near and far.
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f && !BlockZoom())
                _targetDistance = Mathf.Clamp(_targetDistance * Mathf.Pow(1f - zoomStep, scroll), minDistance, maxDistance);

            float ease = 1f - Mathf.Exp(-12f * Time.deltaTime);
            _distance = Mathf.Lerp(_distance, _targetDistance, ease);
            _yaw = Mathf.LerpAngle(_yaw, _targetYaw, ease);
            ApplyView();
        }

        /// <summary>Left-drag grabs the ground under the cursor and keeps it there (Humankind / map apps).</summary>
        void DragPan()
        {
            if (Input.GetMouseButtonDown(0))
            {
                _leftHeld = !BlockZoom();
                _dragging = false;
                WasDragged = false;
                _pressAt = Input.mousePosition;
            }
            if (!Input.GetMouseButton(0)) { _leftHeld = false; _dragging = false; return; }
            if (!_leftHeld) return;

            if (!_dragging && ((Vector2)Input.mousePosition - _pressAt).magnitude > DragThreshold && GroundUnder(_pressAt, out _grabWorld))
            {
                _dragging = true;
                WasDragged = true;
                _glideTo = null;
            }
            if (_dragging && GroundUnder(Input.mousePosition, out var now))
            {
                var delta = _grabWorld - now;
                transform.position += new Vector3(delta.x, 0f, delta.z);
            }
        }

        void KeyboardAndEdgePan()
        {
            var input = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            if (edgeScroll && Application.isFocused && !_dragging && !BlockZoom())
            {
                var m = Input.mousePosition;
                bool inside = m.x >= 0 && m.y >= 0 && m.x <= Screen.width && m.y <= Screen.height;
                if (inside)
                {
                    if (m.x < edgeMargin) input.x -= 1f; else if (m.x > Screen.width - edgeMargin) input.x += 1f;
                    if (m.y < edgeMargin) input.z -= 1f; else if (m.y > Screen.height - edgeMargin) input.z += 1f;
                }
            }
            if (input.sqrMagnitude < 0.01f) return;
            _glideTo = null;
            var yaw = Quaternion.Euler(0f, _yaw, 0f);
            float speed = panSpeed * Mathf.Lerp(0.5f, 2f, Mathf.InverseLerp(minDistance, maxDistance, _distance));
            transform.position += yaw * input.normalized * speed * Time.deltaTime;
        }

        bool GroundUnder(Vector2 screen, out Vector3 world)
        {
            world = default;
            if (_camera == null) return false;
            var ray = _camera.ScreenPointToRay(screen);
            var ground = new Plane(Vector3.up, new Vector3(0f, transform.position.y, 0f));
            if (!ground.Raycast(ray, out float t)) return false;
            world = ray.GetPoint(t);
            return true;
        }

        void ApplyView()
        {
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            if (_camera == null) return;
            float pitch = Mathf.Lerp(closePitch, farPitch, Mathf.InverseLerp(minDistance, maxDistance * 0.6f, _distance));
            var offset = Quaternion.Euler(pitch, 0f, 0f) * new Vector3(0f, 0f, -_distance);
            _camera.transform.localPosition = offset;
            _camera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }
    }
}
