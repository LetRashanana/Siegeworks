using UnityEngine;
using UnityEngine.InputSystem;

namespace Siegeworks.View
{
    // Gray-box RTS camera: WASD/arrows pan, mouse wheel zoom, Q/E rotate.
    // ON PROBATION (feel): panSpeed, zoomStep, pitch, min/maxHeight are Austin's to judge.
    public class RtsCamera : MonoBehaviour
    {
        public float panSpeed = 1.2f;       // fraction of camera height per second
        public float zoomStep = 0.12f;      // fraction of height per wheel notch
        public float minHeight = 12f;
        public float maxHeight = 200f;
        public float pitch = 55f;           // degrees down from horizontal
        public float rotateSpeed = 90f;     // degrees per second
        public float boundsHalfSize = 128f;

        Vector3 _focus;
        float _height = 40f;
        float _yaw;

        void Start() => Apply();

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            float dt = Time.unscaledDeltaTime;

            Vector2 move = Vector2.zero;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move.y += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move.x += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move.x -= 1f;
            if (kb.qKey.isPressed) _yaw -= rotateSpeed * dt;
            if (kb.eKey.isPressed) _yaw += rotateSpeed * dt;

            var yaw = Quaternion.Euler(0f, _yaw, 0f);
            _focus += (yaw * Vector3.forward * move.y + yaw * Vector3.right * move.x) * (panSpeed * _height * dt);
            _focus.x = Mathf.Clamp(_focus.x, -boundsHalfSize, boundsHalfSize);
            _focus.z = Mathf.Clamp(_focus.z, -boundsHalfSize, boundsHalfSize);

            var mouse = Mouse.current;
            float scroll = mouse != null ? mouse.scroll.ReadValue().y : 0f;
            if (scroll != 0f) _height = Mathf.Clamp(_height * (1f - Mathf.Sign(scroll) * zoomStep), minHeight, maxHeight);

            Apply();
        }

        void Apply()
        {
            var rot = Quaternion.Euler(pitch, _yaw, 0f);
            transform.SetPositionAndRotation(_focus - rot * Vector3.forward * (_height / Mathf.Sin(pitch * Mathf.Deg2Rad)), rot);
        }
    }
}
