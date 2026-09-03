using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectRealm.Presentation.Map
{
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public sealed class FiveTerrainCamera : MonoBehaviour
    {
        [SerializeField] private FiveTerrainDefinition definition;
        [SerializeField] private Vector3 focus = new Vector3(0, 8, 0);
        [SerializeField, Range(30, 75)] private float pitch = 51;
        [SerializeField] private float zoom = 128;
        private Camera view;
        private Vector3 desiredFocus;
        private float desiredPitch, desiredZoom;
        public int Selected { get; private set; } = -1;
        public bool ShowLabels { get; set; } = true;
        public float Pitch => pitch;
        public float Zoom => zoom;
        public FiveTerrainDefinition Definition => definition;

        public void Configure(FiveTerrainDefinition data)
        {
            definition = data;
            view = GetComponent<Camera>();
            Home(true);
        }

        private void OnEnable()
        {
            view = GetComponent<Camera>();
            desiredFocus = focus; desiredPitch = pitch; desiredZoom = zoom;
        }

        public void Home(bool immediate = false)
        {
            desiredFocus = new Vector3(0, 9, 0);
            desiredPitch = 51; desiredZoom = definition != null ? definition.depth * 0.52f : 125;
            Selected = -1;
            if (immediate) { focus = desiredFocus; pitch = desiredPitch; zoom = desiredZoom; ApplyPose(); }
        }

        public void FocusTerrain(int index, bool immediate = false)
        {
            if (definition == null || index < 0 || index >= 5) return;
            var point = definition.Focus((LandformKind)index);
            desiredFocus = new Vector3(point.x, definition.Height(point.x, point.y) * 0.65f, point.y);
            desiredZoom = index == 2 ? 59f : 55f;
            desiredPitch = index == 3 || index == 4 ? 58f : 48f;
            Selected = index;
            if (immediate) { focus = desiredFocus; pitch = desiredPitch; zoom = desiredZoom; ApplyPose(); }
        }

        public void AdjustZoom(float factor) => desiredZoom = Mathf.Clamp(desiredZoom * factor, 24, definition.depth * 0.7f);
        public void AdjustPitch(float delta) => desiredPitch = Mathf.Clamp(desiredPitch + delta, 30, 75);

        private void Update()
        {
            if (definition == null || !UnityEngine.Application.isFocused) return;
            var keyboard = Keyboard.current;
            Vector2 motion = Vector2.zero;
            if (keyboard != null)
            {
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) motion.y++;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) motion.y--;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) motion.x++;
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) motion.x--;
                if (keyboard.fKey.wasPressedThisFrame || keyboard.homeKey.wasPressedThisFrame) Home();
                if (keyboard.hKey.wasPressedThisFrame) ShowLabels = !ShowLabels;
                if (keyboard.digit1Key.wasPressedThisFrame) FocusTerrain(0);
                if (keyboard.digit2Key.wasPressedThisFrame) FocusTerrain(1);
                if (keyboard.digit3Key.wasPressedThisFrame) FocusTerrain(2);
                if (keyboard.digit4Key.wasPressedThisFrame) FocusTerrain(3);
                if (keyboard.digit5Key.wasPressedThisFrame) FocusTerrain(4);
                if (keyboard.equalsKey.isPressed) AdjustZoom(Mathf.Exp(-Time.unscaledDeltaTime));
                if (keyboard.minusKey.isPressed) AdjustZoom(Mathf.Exp(Time.unscaledDeltaTime));
            }
            motion = Vector2.ClampMagnitude(motion, 1);
            float speed = zoom * 0.8f * Time.unscaledDeltaTime;
            desiredFocus += new Vector3(motion.x, 0, motion.y) * speed;
            if (motion != Vector2.zero) Selected = -1;

            var mouse = Mouse.current;
            if (mouse != null && !OverHud(mouse.position.ReadValue()))
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (keyboard != null && keyboard.shiftKey.isPressed) AdjustZoom(Mathf.Exp(-scroll * 0.0015f));
                else AdjustPitch(scroll * 0.025f);
                if (mouse.middleButton.isPressed || mouse.rightButton.isPressed)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    float unit = zoom * 2f / Mathf.Max(1, Screen.height);
                    desiredFocus -= new Vector3(delta.x, 0, delta.y / Mathf.Max(0.5f, Mathf.Sin(pitch * Mathf.Deg2Rad))) * unit;
                    if (delta.sqrMagnitude > 0) Selected = -1;
                }
            }
            desiredFocus.x = Mathf.Clamp(desiredFocus.x, -definition.width * 0.46f, definition.width * 0.46f);
            desiredFocus.z = Mathf.Clamp(desiredFocus.z, -definition.depth * 0.46f, definition.depth * 0.46f);
            float blend = 1f - Mathf.Exp(-12f * Mathf.Min(Time.unscaledDeltaTime, 0.1f));
            focus = Vector3.Lerp(focus, desiredFocus, blend);
            pitch = Mathf.Lerp(pitch, desiredPitch, blend);
            zoom = Mathf.Lerp(zoom, desiredZoom, blend);
            ApplyPose();
        }

        public static bool OverHud(Vector2 position)
        {
            float scale = Mathf.Max(0.5f, Screen.height / 900f);
            float top = (Screen.height - position.y) / scale;
            return top < 116f || top > 818f || position.x / scale < 270f && top > 680f;
        }

        private void ApplyPose()
        {
            if (view == null) view = GetComponent<Camera>();
            view.orthographic = true;
            view.orthographicSize = zoom;
            Quaternion rotation = Quaternion.Euler(pitch, 0, 0);
            transform.SetPositionAndRotation(focus - rotation * Vector3.forward * 450f, rotation);
        }
    }
}
