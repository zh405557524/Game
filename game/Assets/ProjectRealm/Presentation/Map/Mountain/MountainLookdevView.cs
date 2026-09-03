using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectRealm.Presentation.Map.Mountain
{
    [RequireComponent(typeof(Camera))]
    public sealed class MountainLookdevView : MonoBehaviour
    {
        public MountainLookdevProfile profile;
        public MeshRenderer terrain;
        public GameObject treesRoot, mistRoot;
        [Range(0, 2)] public int surfaceStage = 2;
        public bool trees = true, mist = true, paper = true, showHud = true;
        public Vector3 focus;
        public float zoom = 82, pitch = 48;
        private MaterialPropertyBlock properties;
        private Camera viewCamera;
        public const float MinimumPitch = 35, MaximumPitch = 75;

        private void Start()
        {
            if (profile == null || !profile.Validate(out _) || terrain == null) { enabled = false; return; }
            Apply();
        }

        public void ResetView()
        { focus = profile.defaultFocus; zoom = profile.defaultZoom; pitch = profile.defaultPitch; }

        // Pure camera-state change. No AssetDatabase, mesh builder, texture writes, or shared material mutation here.
        public void Navigate(Vector2 movement, float wheel, bool shift, float deltaTime, bool reset = false)
        {
            if (profile == null) return;
            if (reset) { ResetView(); return; }
            movement = Vector2.ClampMagnitude(movement, 1);
            focus += new Vector3(movement.x, 0, movement.y) * (zoom * 0.7f * Mathf.Clamp(deltaTime, 0, 0.1f));
            focus.x = Mathf.Clamp(focus.x, -profile.size.x * 0.42f, profile.size.x * 0.42f);
            focus.z = Mathf.Clamp(focus.z, -profile.size.y * 0.42f, profile.size.y * 0.42f);
            if (shift) pitch = Mathf.Clamp(pitch + wheel * 0.035f, MinimumPitch, MaximumPitch);
            else zoom = Mathf.Clamp(zoom * Mathf.Exp(-wheel * 0.0015f), profile.minZoom, profile.maxZoom);
        }

        public void Apply()
        {
            if (profile == null || terrain == null) return;
            viewCamera ??= GetComponent<Camera>();
            surfaceStage = Mathf.Clamp(surfaceStage, 0, 2);
            zoom = Mathf.Clamp(zoom, profile.minZoom, profile.maxZoom); pitch = Mathf.Clamp(pitch, MinimumPitch, MaximumPitch);
            properties ??= new MaterialPropertyBlock(); properties.Clear();
            properties.SetFloat("_Stage", surfaceStage);
            properties.SetFloat("_PaperStrength", paper ? profile.paperStrength : 0);
            terrain.SetPropertyBlock(properties);
            if (profile.pine == null) trees = false;
            if (treesRoot != null) treesRoot.SetActive(trees);
            if (mistRoot != null) mistRoot.SetActive(mist);
            viewCamera.orthographic = true; viewCamera.orthographicSize = zoom;
            var rotation = Quaternion.Euler(pitch, 0, 0);
            transform.SetPositionAndRotation(focus - rotation * Vector3.forward * profile.cameraDistance, rotation);
            if (treesRoot != null && trees)
                foreach (Transform item in treesRoot.transform) item.rotation = rotation;
            // Planar mist cards face the same fixed heading. Turning them off never changes terrain geometry.
            if (mistRoot != null && mist)
                foreach (Transform item in mistRoot.transform) item.rotation = rotation;
        }

        private void Update()
        { PollInput(Time.unscaledDeltaTime); }

        public void PollInput(float deltaTime)
        {
            var keyboard = Keyboard.current; var mouse = Mouse.current;
            Vector2 move = Vector2.zero;
            bool reset = false, shift = false;
            if (keyboard != null)
            {
                move = new Vector2((keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0), (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
                shift = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
                reset = keyboard.fKey.wasPressedThisFrame;
                if (keyboard.digit1Key.wasPressedThisFrame) surfaceStage = 0;
                if (keyboard.digit2Key.wasPressedThisFrame) surfaceStage = 1;
                if (keyboard.digit3Key.wasPressedThisFrame) surfaceStage = 2;
                if (keyboard.tKey.wasPressedThisFrame) trees = !trees;
                if (keyboard.gKey.wasPressedThisFrame) mist = !mist;
                if (keyboard.pKey.wasPressedThisFrame) paper = !paper;
                if (keyboard.hKey.wasPressedThisFrame) showHud = !showHud;
            }
            float wheel = mouse == null ? 0 : mouse.scroll.ReadValue().y;
            if (mouse != null && showHud && mouse.position.ReadValue().y < 70) wheel = 0;
            Navigate(move, wheel, shift, deltaTime, reset);
            Apply();
        }

        private void OnGUI()
        {
            if (!showHud || !enabled || profile == null) return;
            var oldMatrix = GUI.matrix; var oldColor = GUI.color;
            try
            {
                float scale = Mathf.Max(0.4f, Mathf.Min(Screen.width / 1400f, Screen.height / 1050f));
                float w = Screen.width / scale, h = Screen.height / scale;
                GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
                GUI.color = new Color(0.95f, 0.94f, 0.90f, 0.94f);
                GUI.DrawTexture(new Rect(16, h - 84, w - 32, 68), Texture2D.whiteTexture);
                GUI.color = Color.white;
                var label = new GUIStyle(GUI.skin.label) { fontSize = 17 };
                label.normal.textColor = new Color(0.15f, 0.19f, 0.17f);
                string stage = surfaceStage == 0 ? "1 白模" : surfaceStage == 1 ? "2 淡彩" : "3 皴擦";
                string treeState = profile.pine == null ? "缺有效透明素材" : trees ? "开" : "关";
                GUI.Label(new Rect(30, h - 80, w - 60, 29), $"山地小样 V1 · 待视觉验收     {stage}   T 树丛:{treeState}   G 薄雾:{(mist ? "开" : "关")}   P 纸感:{(paper ? "开" : "关")}     {pitch:0}° / {zoom:0}", label);
                GUI.Label(new Rect(30, h - 51, w - 60, 27), "WASD 平移   滚轮缩放   Shift+滚轮调俯角   F 复位   1/2/3 分层   H 隐藏说明；没有自由环绕。", label);
            }
            finally { GUI.matrix = oldMatrix; GUI.color = oldColor; }
        }
    }
}
