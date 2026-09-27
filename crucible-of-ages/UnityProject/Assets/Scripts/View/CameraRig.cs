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

        void Update()
        {
            var input = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            if (input.sqrMagnitude > 0.01f)
            {
                var yaw = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
                float speed = panSpeed * Mathf.Lerp(0.5f, 2f, Mathf.InverseLerp(minDistance, maxDistance, _distance));
                transform.position += yaw * input.normalized * speed * Time.deltaTime;
            }

            float rotate = (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f);
            if (rotate != 0f) transform.Rotate(0f, rotate * rotateSpeed * Time.deltaTime, 0f, Space.World);

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f)
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
