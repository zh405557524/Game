using UnityEngine;

namespace ProjectRealm.UnityPresentation.Map
{
    [RequireComponent(typeof(FiveTerrainCamera))]
    public sealed class FiveTerrainHud : MonoBehaviour
    {
        private FiveTerrainCamera controller;
        private Font font;
        private GUIStyle title, small, body, button, number, label;
        private readonly Color ink = new Color(0.20f, 0.25f, 0.22f);
        private readonly Color paper = new Color(0.94f, 0.925f, 0.865f);
        private readonly Color accent = new Color(0.53f, 0.24f, 0.17f);

        private void OnEnable() => controller = GetComponent<FiveTerrainCamera>();
        private void OnDestroy() { if (font != null) Destroy(font); }

        private void Initialize()
        {
            if (title != null) return;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Songti SC", "STSong", "PingFang SC", "Arial Unicode MS", "Arial" }, 28);
            title = Style(30, FontStyle.Normal); small = Style(12, FontStyle.Normal);
            body = Style(15, FontStyle.Normal); number = Style(13, FontStyle.Bold);
            label = Style(17, FontStyle.Normal); label.alignment = TextAnchor.MiddleCenter;
            button = Style(16, FontStyle.Normal); button.alignment = TextAnchor.MiddleCenter;
        }

        private GUIStyle Style(int size, FontStyle weight)
        {
            var style = new GUIStyle(GUI.skin.label) { font = font, fontSize = size, fontStyle = weight, wordWrap = false };
            style.normal.textColor = ink;
            return style;
        }

        private void Fill(Rect rect, Color color)
        {
            var previous = GUI.color; GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = previous;
        }

        private bool Button(Rect rect, string text, bool selected = false)
        {
            bool hover = rect.Contains(Event.current.mousePosition);
            Fill(rect, selected ? ink : hover ? new Color(0.85f, 0.86f, 0.79f, 0.98f) : new Color(paper.r, paper.g, paper.b, 0.94f));
            button.normal.textColor = selected ? paper : ink;
            if (selected) Fill(new Rect(rect.x, rect.yMax - 3, rect.width, 3), accent);
            return GUI.Button(rect, text, button);
        }

        private void OnGUI()
        {
            if (controller == null || controller.Definition == null) return;
            Initialize();
            float scale = Mathf.Max(0.5f, Screen.height / 900f);
            var oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            float width = Screen.width / scale;
            Fill(new Rect(0, 0, width, 109), new Color(paper.r, paper.g, paper.b, 0.97f));
            Fill(new Rect(28, 24, 4, 58), accent);
            GUI.Label(new Rect(47, 16, 290, 45), "山河 · 地形初稿", title);
            GUI.Label(new Rect(49, 65, 390, 23), "PROJECT REALM  /  五种基础地貌 · 可运行样板", small);
            float start = Mathf.Max(380, width - 620);
            for (int i = 0; i < 5; i++)
                if (Button(new Rect(start + i * 103, 34, 95, 42), $"{i + 1}  {FiveTerrainDefinition.Names[i]}", controller.Selected == i)) controller.FocusTerrain(i);
            if (Button(new Rect(width - 90, 34, 65, 42), "全览")) controller.Home();
            Fill(new Rect(28, 108, width - 56, 1), new Color(0.36f, 0.39f, 0.31f, 0.3f));

            if (controller.ShowLabels)
            {
                for (int i = 0; i < 5; i++)
                {
                    Vector2 p = controller.Definition.Focus((LandformKind)i);
                    Vector3 screen = GetComponent<Camera>().WorldToScreenPoint(new Vector3(p.x, controller.Definition.Height(p.x, p.y) + 4f, p.y));
                    if (screen.z < 0) continue;
                    float sx = screen.x / scale, sy = (Screen.height - screen.y) / scale;
                    if (sx < 55 || sx > width - 55 || sy < 140 || sy > 750) continue;
                    Fill(new Rect(sx - 1, sy, 1, 22), new Color(ink.r, ink.g, ink.b, 0.5f));
                    Fill(new Rect(sx - 38, sy - 33, 76, 30), new Color(paper.r, paper.g, paper.b, 0.94f));
                    GUI.Label(new Rect(sx - 38, sy - 33, 76, 30), FiveTerrainDefinition.Names[i], label);
                }
            }
            if (controller.Selected >= 0)
            {
                Fill(new Rect(28, 690, 260, 108), new Color(paper.r, paper.g, paper.b, 0.96f));
                GUI.Label(new Rect(44, 698, 220, 26), FiveTerrainDefinition.Names[controller.Selected], label);
                GUI.Label(new Rect(44, 736, 240, 52), FiveTerrainDefinition.Descriptions[controller.Selected], body);
            }
            Fill(new Rect(0, 818, width, 82), new Color(paper.r, paper.g, paper.b, 0.96f));
            GUI.Label(new Rect(28, 835, width - 360, 26), "WASD 平移   ·   右键拖动   ·   滚轮调角度   ·   Shift + 滚轮缩放   ·   F 全览   ·   H 标签", body);
            GUI.Label(new Rect(28, 866, 570, 21), "本轮仅地形层  /  水系、植被、村落后续逐层加入  /  非真实县域地理", small);
            if (Button(new Rect(width - 306, 837, 45, 34), "−")) controller.AdjustZoom(1.18f);
            if (Button(new Rect(width - 253, 837, 45, 34), "+")) controller.AdjustZoom(1f / 1.18f);
            if (Button(new Rect(width - 192, 837, 75, 34), "低角度")) controller.AdjustPitch(-5);
            if (Button(new Rect(width - 109, 837, 75, 34), "俯视")) controller.AdjustPitch(5);
            GUI.matrix = oldMatrix;
        }
    }
}
