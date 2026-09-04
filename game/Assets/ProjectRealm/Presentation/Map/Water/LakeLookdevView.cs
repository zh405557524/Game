using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectRealm.UnityPresentation.Map.Water
{
    [RequireComponent(typeof(Camera))]
    public sealed class LakeLookdevView : MonoBehaviour
    {
        public LakeLookdevProfile profile;
        public MeshRenderer water, banks;
        public Material[] waterStages, bankStages;
        public GameObject lakeRoot, tileRoot;
        [Range(0, 3)] public int stage = 3;
        public bool tiles, bedOnly, paused = true;
        public float phase;
        public float zoom = 60.5f, pitch = 55;
        public Vector3 focus;
        private MaterialPropertyBlock properties;
        private static readonly string[] StageNames = { "1 旧版基线", "2 只换水纹", "3 再调水材质", "4 新岸边 · 完整候选" };

        private void Start()
        {
            if (profile == null || !profile.Validate(out _) || water == null || banks == null ||
                waterStages == null || waterStages.Length != 4 || bankStages == null || bankStages.Length != 4)
            { enabled = false; return; }
            Apply();
        }

        public void Frame(float scale = 1, float angle = 55)
        { focus = Vector3.zero; zoom = profile.baseline.ViewSize * scale; pitch = angle; }

        public void Apply()
        {
            if (water == null || banks == null || waterStages == null || bankStages == null) return;
            stage = Mathf.Clamp(stage, 0, 3);
            water.sharedMaterial = waterStages[stage]; banks.sharedMaterial = bankStages[stage];
            properties ??= new MaterialPropertyBlock(); properties.Clear(); properties.SetFloat("_Phase", phase);
            if (stage >= 2) properties.SetFloat("_TileSize", profile.waterTileSize);
            water.SetPropertyBlock(properties); water.enabled = !bedOnly;
            properties.Clear();
            if (stage == 3)
            {
                properties.SetFloat("_TileSize", profile.shoreTileSize); properties.SetFloat("_ShoreWidth", profile.shoreWidth);
                properties.SetVector("_LandColor", (Vector4)profile.landColor.linear);
            }
            banks.SetPropertyBlock(properties);
            lakeRoot.SetActive(!tiles); tileRoot.SetActive(tiles);
            var camera = GetComponent<Camera>(); camera.orthographicSize = tiles ? 50 : zoom;
            var rotation = Quaternion.Euler(tiles ? 90 : pitch, 0, 0);
            camera.transform.SetPositionAndRotation((tiles ? Vector3.zero : focus) - rotation * Vector3.forward * 250, rotation);
        }

        private void Update()
        {
            var k = Keyboard.current; var m = Mouse.current;
            if (k != null)
            {
                if (k.digit1Key.wasPressedThisFrame) { stage = 0; tiles = false; }
                if (k.digit2Key.wasPressedThisFrame) { stage = 1; tiles = false; }
                if (k.digit3Key.wasPressedThisFrame) { stage = 2; tiles = false; }
                if (k.digit4Key.wasPressedThisFrame) { stage = 3; tiles = false; }
                if (k.digit5Key.wasPressedThisFrame) tiles = !tiles;
                if (k.bKey.wasPressedThisFrame) bedOnly = !bedOnly;
                if (k.spaceKey.wasPressedThisFrame) paused = !paused;
                if (k.fKey.wasPressedThisFrame) { tiles = false; bedOnly = false; Frame(); }
                if (!tiles)
                {
                    var movement = Vector2.ClampMagnitude(new Vector2((k.dKey.isPressed ? 1 : 0) - (k.aKey.isPressed ? 1 : 0), (k.wKey.isPressed ? 1 : 0) - (k.sKey.isPressed ? 1 : 0)), 1);
                    focus += new Vector3(movement.x, 0, movement.y) * (zoom * 0.65f * Time.unscaledDeltaTime);
                }
            }
            if (!tiles && m != null && m.position.ReadValue().y > Screen.height * 0.20f && m.position.ReadValue().y < Screen.height * 0.86f)
            {
                float scroll = m.scroll.ReadValue().y;
                if (k != null && k.shiftKey.isPressed) pitch = Mathf.Clamp(pitch + scroll * 0.03f, 35, 85);
                else zoom = Mathf.Clamp(zoom * Mathf.Exp(-scroll * 0.0015f), profile.baseline.ViewSize * 0.23f, profile.baseline.ViewSize * 1.35f);
            }
            focus.x = Mathf.Clamp(focus.x, -profile.baseline.size.x * 0.4f, profile.baseline.size.x * 0.4f);
            focus.z = Mathf.Clamp(focus.z, -profile.baseline.size.y * 0.4f, profile.baseline.size.y * 0.4f);
            if (!paused) phase += Time.deltaTime * profile.baseline.animationSpeed;
            Apply();
        }

        private void OnGUI()
        {
            if (!enabled || profile == null) return;
            var oldMatrix = GUI.matrix; var oldColor = GUI.color; var oldContent = GUI.contentColor; var oldBackground = GUI.backgroundColor;
            try
            {
                float scale = Mathf.Min(Screen.width / 1400f, Screen.height / 900f), width = Screen.width / scale, height = Screen.height / scale;
                GUI.matrix = Matrix4x4.Scale(Vector3.one * scale); GUI.color = GUI.contentColor = Color.white;
                Panel(new Rect(18, 16, width - 36, 90)); Panel(new Rect(18, height - 142, width - 36, 126));
                var title = new GUIStyle(GUI.skin.label) { fontSize = 28 }; title.normal.textColor = Color.black;
                var label = new GUIStyle(title) { fontSize = 18 }; var button = new GUIStyle(GUI.skin.button) { fontSize = 18 };
                button.normal.textColor = button.hover.textColor = button.active.textColor = button.focused.textColor = Color.black;
                GUI.Label(new Rect(36, 26, width - 72, 38), "湖泊 V3 / 品质基准 · 待风格确认", title);
                GUI.Label(new Rect(36, 69, width - 72, 28), tiles ? "原图 3×3 平铺：左为水纹，右为岸边；无接缝修补、无材质调色。" : "同一网格 · 同一机位 · 分阶段定位差异；场景默认暂停，1—4 切换不改变相机。", label);
                for (int i = 0; i < 4; i++)
                {
                    GUI.backgroundColor = stage == i && !tiles ? new Color(0.51f, 0.73f, 0.68f) : Color.white;
                    if (GUI.Button(new Rect(36 + i * 224, height - 128, 212, 34), StageNames[i], button)) { stage = i; tiles = false; }
                }
                GUI.backgroundColor = Color.white;
                if (GUI.Button(new Rect(936, height - 128, 175, 34), "5 平铺检查", button)) tiles = !tiles;
                if (GUI.Button(new Rect(1123, height - 128, 196, 34), paused ? "Space 播放波纹" : "Space 暂停", button)) paused = !paused;
                if (GUI.Button(new Rect(36, height - 86, 105, 31), "近景", button)) { tiles = false; Frame(0.45f); }
                if (GUI.Button(new Rect(151, height - 86, 105, 31), "全景", button)) { tiles = false; Frame(); }
                if (GUI.Button(new Rect(266, height - 86, 105, 31), "远景", button)) { tiles = false; Frame(1.3f); }
                if (GUI.Button(new Rect(381, height - 86, 165, 31), bedOnly ? "B 显示水面" : "B 隐藏水面", button)) bedOnly = !bedOnly;
                GUI.Label(new Rect(565, height - 85, width - 600, 28), "WASD 平移 · 滚轮缩放 · Shift+滚轮调角度 · F 复位", label);
                GUI.Label(new Rect(36, height - 47, width - 72, 27), "当前仅湖泊候选；其他水体及原场景未替换。技术通过 ≠ 美术通过。", label);
            }
            finally { GUI.matrix = oldMatrix; GUI.color = oldColor; GUI.contentColor = oldContent; GUI.backgroundColor = oldBackground; }
        }
        private static void Panel(Rect rect) { GUI.color = new Color(0.96f, 0.95f, 0.89f, 0.98f); GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = Color.white; }
    }
}
