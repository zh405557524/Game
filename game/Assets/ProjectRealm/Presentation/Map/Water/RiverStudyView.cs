using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectRealm.UnityPresentation.Map.Water
{
    [RequireComponent(typeof(Camera))]
    public sealed class RiverStudyView : MonoBehaviour
    {
        public RiverStudyDefinition definition;
        public MeshRenderer water;
        public GameObject flowArrows;
        private Camera view;
        private MaterialPropertyBlock block, originalBlock;
        private Vector3 focus;
        private float zoom = 66, pitch = 55, flowDistance;
        private bool paused;
        private int mode;

        private void Start()
        {
            if (definition == null || water == null || flowArrows == null) { enabled = false; return; }
            view = GetComponent<Camera>(); block = new MaterialPropertyBlock(); originalBlock = new MaterialPropertyBlock();
            water.GetPropertyBlock(originalBlock); ResetView(); Apply();
        }

        private void OnDestroy() { if (water != null && originalBlock != null) water.SetPropertyBlock(originalBlock); }
        private void ResetView() { focus = Vector3.zero; zoom = 66; pitch = 55; }

        private void Update()
        {
            if (view == null) return;
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.digit1Key.wasPressedThisFrame) mode = 0;
                if (keyboard.digit2Key.wasPressedThisFrame) mode = 1;
                if (keyboard.digit3Key.wasPressedThisFrame) mode = 2;
                if (keyboard.spaceKey.wasPressedThisFrame) paused = !paused;
                if (keyboard.fKey.wasPressedThisFrame) ResetView();
                var move = new Vector2((keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                    (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
                move = Vector2.ClampMagnitude(move, 1) * (zoom * 0.55f * Time.unscaledDeltaTime);
                focus += new Vector3(move.x, 0, move.y);
            }
            var mouse = Mouse.current;
            if (mouse != null && mouse.position.ReadValue().y > Screen.height * 0.18f && mouse.position.ReadValue().y < Screen.height * 0.88f)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (keyboard != null && keyboard.shiftKey.isPressed) pitch = Mathf.Clamp(pitch + scroll * 0.025f, 35, 80);
                else zoom = Mathf.Clamp(zoom * Mathf.Exp(-scroll * 0.0015f), 20, 85);
            }
            focus.x = Mathf.Clamp(focus.x, -70, 70); focus.z = Mathf.Clamp(focus.z, -45, 45);
            if (!paused) flowDistance += Time.deltaTime * definition.flowSpeed;
            Apply();
        }

        private void Apply()
        {
            block.SetFloat("_FlowDistance", flowDistance); block.SetFloat("_TextureLength", definition.textureLength); water.SetPropertyBlock(block);
            water.enabled = mode != 2; flowArrows.SetActive(mode == 1);
            view.orthographicSize = zoom;
            var rotation = Quaternion.Euler(pitch, 0, 0);
            transform.SetPositionAndRotation(focus - rotation * Vector3.forward * 240, rotation);
        }

        private void OnGUI()
        {
            if (view == null) return;
            var matrix = GUI.matrix; var color = GUI.color; var content = GUI.contentColor;
            float scale = Mathf.Min(Screen.width / 1400f, Screen.height / 900f);
            float width = Screen.width / scale, height = Screen.height / scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1)); GUI.color = Color.white; GUI.contentColor = Color.white;
            Panel(new Rect(18, 16, width - 36, 82)); Panel(new Rect(18, height - 132, width - 36, 114));
            var title = new GUIStyle(GUI.skin.label) { fontSize = 28 }; title.normal.textColor = Color.black;
            var text = new GUIStyle(title) { fontSize = 19 };
            var button = new GUIStyle(GUI.skin.button) { fontSize = 19 };
            button.normal.textColor = button.hover.textColor = button.active.textColor = button.focused.textColor = Color.black;
            GUI.Label(new Rect(36, 26, 700, 38), "水系 01 / 河流 · 独立制作样板", title);
            GUI.Label(new Rect(36, 64, width - 72, 28), "可编辑河道 · 宽度渐变 · 河床与岸线 · 顺流纹理   /   等待视觉确认", text);
            string[] names = { "1 水面与河岸", "2 查看下游方向", "3 检查河床" };
            for (int i = 0; i < names.Length; i++)
            {
                GUI.backgroundColor = mode == i ? new Color(0.57f, 0.79f, 0.78f) : Color.white;
                if (GUI.Button(new Rect(36 + i * 184, height - 119, 174, 34), names[i], button)) mode = i;
            }
            GUI.backgroundColor = Color.white;
            if (GUI.Button(new Rect(596, height - 119, 180, 34), paused ? "Space 继续流动" : "Space 暂停流动", button)) paused = !paused;
            if (GUI.Button(new Rect(786, height - 119, 105, 34), "近景", button)) { focus = new Vector3(4, 0, 10); zoom = 32; }
            if (GUI.Button(new Rect(901, height - 119, 105, 34), "全景", button)) ResetView();
            GUI.Label(new Rect(36, height - 77, width - 72, 26), "WASD 平移 · 滚轮缩放 · Shift+滚轮调俯角 · F 复位 · Play 调整不改测试输入", text);
            var source = water.sharedMaterial.GetTexture("_BaseMap");
            GUI.Label(new Rect(36, height - 48, width - 72, 24), "来源：" + (source != null ? source.name : "missing") + "   |   只验证表现，不含水文模拟、通航和洪水结算", text);
            GUI.matrix = matrix; GUI.color = color; GUI.contentColor = content;
        }

        private static void Panel(Rect rect)
        {
            GUI.color = new Color(0.95f, 0.94f, 0.88f, 0.96f); GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = Color.white;
        }
    }
}
