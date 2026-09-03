using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectRealm.Presentation.Map
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class CountyMapCameraController : MonoBehaviour
    {
        [SerializeField] private Vector2 mapSize = new Vector2(80f, 54f);
        [SerializeField, Min(1f)] private float minimumZoom = 8f;
        [SerializeField, Min(1f)] private float maximumZoom = 38f;
        [SerializeField, Min(0.01f)] private float keyboardPanSpeed = 22f;
        [SerializeField, Min(0.0001f)] private float wheelZoomSensitivity = 0.0015f;

        private Camera mapCamera;

        public void Configure(Vector2 boundsSize)
        {
            mapSize = boundsSize;
            EnsureCamera();
            ClampPosition();
        }

        private void Awake()
        {
            EnsureCamera();
        }

        private void Update()
        {
            EnsureCamera();
            ApplyMouseZoom();
            ApplyMousePan();
            ApplyKeyboardPan();
            ClampPosition();
        }

        private void EnsureCamera()
        {
            if (mapCamera == null)
            {
                mapCamera = GetComponent<Camera>();
            }

            mapCamera.orthographic = true;
            mapCamera.orthographicSize = Mathf.Clamp(mapCamera.orthographicSize, minimumZoom, maximumZoom);
        }

        private void ApplyMouseZoom()
        {
            if (Mouse.current == null)
            {
                return;
            }

            var scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Approximately(scroll, 0f))
            {
                return;
            }

            mapCamera.orthographicSize *= Mathf.Exp(-scroll * wheelZoomSensitivity);
            mapCamera.orthographicSize = Mathf.Clamp(mapCamera.orthographicSize, minimumZoom, maximumZoom);
        }

        private void ApplyMousePan()
        {
            if (Mouse.current == null ||
                (!Mouse.current.middleButton.isPressed && !Mouse.current.rightButton.isPressed))
            {
                return;
            }

            var delta = Mouse.current.delta.ReadValue();
            var worldUnitsPerPixel = mapCamera.orthographicSize * 2f / Mathf.Max(1f, Screen.height);
            var screenRight = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
            var screenUp = Vector3.ProjectOnPlane(transform.up, Vector3.up).normalized;
            transform.position -= screenRight * (delta.x * worldUnitsPerPixel);
            transform.position -= screenUp * (delta.y * worldUnitsPerPixel);
        }

        private void ApplyKeyboardPan()
        {
            if (Keyboard.current == null)
            {
                return;
            }

            var horizontal = 0f;
            var vertical = 0f;

            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed)
            {
                horizontal -= 1f;
            }

            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed)
            {
                horizontal += 1f;
            }

            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed)
            {
                vertical -= 1f;
            }

            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed)
            {
                vertical += 1f;
            }

            var input = Vector2.ClampMagnitude(new Vector2(horizontal, vertical), 1f);
            if (input == Vector2.zero)
            {
                return;
            }

            var screenRight = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
            var screenUp = Vector3.ProjectOnPlane(transform.up, Vector3.up).normalized;
            var zoomScale = mapCamera.orthographicSize / maximumZoom;
            transform.position += (screenRight * input.x + screenUp * input.y) *
                                  (keyboardPanSpeed * Mathf.Lerp(0.45f, 1f, zoomScale) * Time.unscaledDeltaTime);
        }

        private void ClampPosition()
        {
            var position = transform.position;
            position.x = Mathf.Clamp(position.x, -mapSize.x * 0.5f, mapSize.x * 0.5f);
            position.z = Mathf.Clamp(position.z, -mapSize.y * 0.75f, mapSize.y * 0.75f);
            transform.position = position;
        }
    }
}
