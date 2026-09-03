using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectRealm.Presentation.Map.Water
{
    [RequireComponent(typeof(Camera))]
    public sealed class WaterBodyStudyView : MonoBehaviour
    {
        public WaterBodyStudyDefinition definition;
        public MeshRenderer surface;
        public GameObject referenceDetails;
        private Camera view;
        private MaterialPropertyBlock block, original;
        private Vector3 focus;
        private float zoom, pitch = 55, phase;
        private int mode;
        private bool paused, details = true;
        public float Phase => phase;
        public float Zoom => zoom;
        public float Pitch => pitch;

        private void Start()
        {
            if (definition == null || surface == null) { enabled = false; return; }
            view = GetComponent<Camera>(); block = new MaterialPropertyBlock(); original = new MaterialPropertyBlock(); surface.GetPropertyBlock(original);
            details = definition.referenceDetails; ResetView(); Apply();
        }
        private void OnDestroy() { if (surface != null && original != null) surface.SetPropertyBlock(original); }
        private void ResetView() { focus = Vector3.zero; zoom = definition.ViewSize; pitch = 55; }
        private void Update()
        {
            if (view == null) return;
            var k = Keyboard.current; var m = Mouse.current;
            if (k != null)
            {
                if (k.digit1Key.wasPressedThisFrame) mode = 0;
                if (k.digit2Key.wasPressedThisFrame) mode = 1;
                if (k.digit3Key.wasPressedThisFrame) mode = 2;
                if (k.fKey.wasPressedThisFrame) ResetView();
                if (k.spaceKey.wasPressedThisFrame) paused = !paused;
                if (k.vKey.wasPressedThisFrame) details = !details;
                var movement = Vector2.ClampMagnitude(new Vector2((k.dKey.isPressed ? 1 : 0) - (k.aKey.isPressed ? 1 : 0), (k.wKey.isPressed ? 1 : 0) - (k.sKey.isPressed ? 1 : 0)), 1);
                focus += new Vector3(movement.x, 0, movement.y) * (zoom * 0.65f * Time.unscaledDeltaTime);
            }
            if (m != null && m.position.ReadValue().y > Screen.height * 0.19f && m.position.ReadValue().y < Screen.height * 0.87f)
            {
                float scroll = m.scroll.ReadValue().y;
                if (k != null && k.shiftKey.isPressed) pitch = Mathf.Clamp(pitch + scroll * 0.03f, 35, 85);
                else zoom = Mathf.Clamp(zoom * Mathf.Exp(-scroll * 0.0015f), definition.ViewSize * 0.3f, definition.ViewSize * 1.3f);
            }
            focus.x = Mathf.Clamp(focus.x, -definition.size.x * 0.4f, definition.size.x * 0.4f);
            focus.z = Mathf.Clamp(focus.z, -definition.size.y * 0.4f, definition.size.y * 0.4f);
            if (!paused) phase += Time.deltaTime * definition.animationSpeed;
            Apply();
        }
        private void Apply()
        {
            block.SetFloat("_Phase", phase); block.SetFloat("_PreviewMode", mode == 1 ? 1 : 0); surface.SetPropertyBlock(block);
            surface.enabled = mode != 2; if (referenceDetails != null) referenceDetails.SetActive(details);
            view.orthographicSize = zoom; var rotation = Quaternion.Euler(pitch, 0, 0);
            transform.SetPositionAndRotation(focus - rotation * Vector3.forward * 250, rotation);
        }
        private void OnGUI()
        {
            if (view == null) return;
            var matrix = GUI.matrix; var oldColor = GUI.color; var oldContent = GUI.contentColor; var oldBackground = GUI.backgroundColor;
            float scale = Mathf.Min(Screen.width / 1400f, Screen.height / 900f), width = Screen.width / scale, height = Screen.height / scale;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale); GUI.color = Color.white; GUI.contentColor = Color.white;
            Panel(new Rect(18, 16, width - 36, 87)); Panel(new Rect(18, height - 138, width - 36, 120));
            var title = new GUIStyle(GUI.skin.label) { fontSize = 28 }; title.normal.textColor = Color.black;
            var label = new GUIStyle(title) { fontSize = 18 }; var button = new GUIStyle(GUI.skin.button) { fontSize = 18 };
            button.normal.textColor = button.hover.textColor = button.active.textColor = button.focused.textColor = Color.black;
            GUI.Label(new Rect(36, 26, width - 72, 38), "水系 " + ((int)definition.kind + 1).ToString("D2") + " / " + definition.DisplayName + " · 独立样板", title);
            string[] notes = { "窄浅河道 · 向右下坡 · 顺流纹理", "不规则闭合岸线 · 湖心小岛 · 静水波纹", "小尺度水面 · 浅水塘底 · 缓岸", "多片浅水 · 湿润地表 · 可隐藏芦苇参照", "陆海分界 · 沙滩与近岸浅水 · 向岸浪纹" };
            GUI.Label(new Rect(36, 67, width - 72, 28), notes[(int)definition.kind - 1] + "  /  首轮样板，待视觉确认", label);
            string[] modes = { "1 正常显示", "2 浅深分区", "3 隐藏水面" };
            for (int i = 0; i < 3; i++)
            {
                GUI.backgroundColor = mode == i ? new Color(0.53f, 0.76f, 0.74f) : Color.white;
                if (GUI.Button(new Rect(36 + 168 * i, height - 124, 158, 34), modes[i], button)) mode = i;
            }
            GUI.backgroundColor = Color.white;
            if (GUI.Button(new Rect(540, height - 124, 162, 34), paused ? "Space 继续" : "Space 暂停", button)) paused = !paused;
            if (GUI.Button(new Rect(712, height - 124, 88, 34), "近景", button)) { focus = Vector3.zero; zoom = definition.ViewSize * 0.5f; }
            if (GUI.Button(new Rect(810, height - 124, 88, 34), "全景", button)) ResetView();
            if (GUI.Button(new Rect(908, height - 124, 88, 34), "俯视", button)) pitch = 85;
            if (GUI.Button(new Rect(1006, height - 124, 164, 34), details ? "V 隐藏参照" : "V 显示参照", button)) details = !details;
            GUI.Label(new Rect(36, height - 80, width - 72, 27), "WASD 平移 · 滚轮缩放 · Shift+滚轮调角度 · F 复位 · 2 模式：浅黄 → 深蓝表示几何深度", label);
            GUI.Label(new Rect(36, height - 49, width - 72, 27), "测试输入独立保存；不含真实水文、灌溉、通航或洪水结算。参照物不属于正式植被数据。", label);
            GUI.matrix = matrix; GUI.color = oldColor; GUI.contentColor = oldContent; GUI.backgroundColor = oldBackground;
        }
        private static void Panel(Rect r) { GUI.color = new Color(0.96f, 0.95f, 0.89f, 0.98f); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white; }
    }
}
