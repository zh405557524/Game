using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ProjectRealm.EditorTools
{
    public sealed class MapDebugWorkbench : EditorWindow
    {
        public const string CatalogPath = ProjectRealmWorkspaceLayout.Development + "/Catalog/MapDebugCatalog.asset";
        private MapDebugCatalog catalog;
        private int layer;
        private Vector2 scroll;
        private static readonly string[] States = { "待调试", "调试中", "待人工确认", "已人工确认" };

        [MenuItem("Project Realm/Debug/Map Debug Workbench")]
        public static void Open()
        {
            var window = GetWindow<MapDebugWorkbench>("地图调试管理");
            window.minSize = new Vector2(820, 520);
            window.Show();
        }

        private void OnEnable() => catalog = LoadCatalog();

        public static MapDebugCatalog LoadCatalog()
        {
            var data = AssetDatabase.LoadAssetAtPath<MapDebugCatalog>(CatalogPath);
            if (data != null)
            {
                if (WaterDebugCases.Ensure(data)) AssetDatabase.SaveAssets();
                return data;
            }
            ProjectRealmWorkspaceLayout.EnsureFolder(ProjectRealmWorkspaceLayout.Development + "/Catalog");
            data = CreateInstance<MapDebugCatalog>();
            string[] names = { "平原", "丘陵", "山地", "高原", "盆地" };
            for (int i = 0; i < names.Length; i++)
            {
                string key = ProjectRealmWorkspaceLayout.TerrainFolders[i];
                data.cases.Add(new MapDebugCase
                {
                    id = "terrain/" + key, displayName = names[i], layer = 0,
                    scenePath = $"{ProjectRealmWorkspaceLayout.DebugScenes}/01_Terrain/{key}/{key.Substring(3)}Debug.unity",
                    testDataPath = $"{ProjectRealmWorkspaceLayout.TestData}/Map/01_Terrain/{key}",
                    generatedPath = $"{ProjectRealmWorkspaceLayout.Generated}/Map/01_Terrain/{key}",
                    state = DebugReviewState.NotStarted,
                    findings = "尚未完成单项调试；自动测试通过不等于视觉通过。"
                });
            }
            for (int i = 1; i < ProjectRealmWorkspaceLayout.LayerFolders.Length; i++)
            {
                string folder = ProjectRealmWorkspaceLayout.LayerFolders[i];
                data.cases.Add(new MapDebugCase { id = folder, displayName = ProjectRealmWorkspaceLayout.LayerNames[i], layer = i,
                    testDataPath = $"{ProjectRealmWorkspaceLayout.TestData}/Map/{folder}", generatedPath = $"{ProjectRealmWorkspaceLayout.Generated}/Map/{folder}",
                    findings = "分类已预留；未创建样板，后续按具体对象增加独立案例。" });
            }
            WaterDebugCases.Ensure(data);
            AssetDatabase.CreateAsset(data, CatalogPath);
            AssetDatabase.SaveAssets();
            return data;
        }

        private void OnGUI()
        {
            if (catalog == null) catalog = LoadCatalog();
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("单项调试 → 人工确认 → 组合调试", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Scenes 只放场景。输入数据、生成网格/材质、自动测试夹具及历史备份分别管理；占位分类不表示已实现。", MessageType.Info);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical(GUILayout.Width(175));
            for (int i = 0; i < ProjectRealmWorkspaceLayout.LayerNames.Length; i++)
                if (GUILayout.Toggle(layer == i, $"{i + 1:D2}  {ProjectRealmWorkspaceLayout.LayerNames[i]}", "Button") && layer != i)
                { layer = i; scroll = Vector2.zero; }
            EditorGUILayout.Space(14);
            if (GUILayout.Button("打开练习场景")) OpenScene(ProjectRealmWorkspaceLayout.LearningScene);
            if (GUILayout.Button("打开组合对照")) OpenScene(ProjectRealmWorkspaceLayout.IntegrationScene);
            if (GUILayout.Button("打开材质验收")) OpenScene(ProjectRealmWorkspaceLayout.MaterialsScene);
            if (GUILayout.Button("选中目录说明")) Ping("Assets/Scenes/README.md");
            EditorGUILayout.EndVertical();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            float contentWidth = Mathf.Max(350, position.width - 210);
            foreach (var item in catalog.cases)
            {
                if (item.layer != layer) continue;
                EditorGUILayout.BeginVertical("box", GUILayout.Width(contentWidth));
                EditorGUILayout.LabelField(item.displayName, EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                var state = (DebugReviewState)EditorGUILayout.Popup("人工验收状态", (int)item.state, States);
                string findings = EditorGUILayout.TextArea(item.findings ?? "", new GUIStyle(EditorStyles.textArea) { wordWrap = true }, GUILayout.MinHeight(40), GUILayout.Width(contentWidth - 12));
                string evidence = EditorGUILayout.TextField("验收证据路径", item.evidencePath ?? "");
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(catalog, "Update map debug review");
                    item.state = state; item.findings = findings; item.evidencePath = evidence;
                    EditorUtility.SetDirty(catalog);
                }
                DrawPath("场景：" + (string.IsNullOrEmpty(item.scenePath) ? "尚未建立" : item.scenePath), contentWidth - 12);
                DrawPath("测试输入：" + item.testDataPath, contentWidth - 12);
                DrawPath("生成输出：" + item.generatedPath, contentWidth - 12);
                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(!File.Exists(item.scenePath)))
                    if (GUILayout.Button("打开单项场景")) OpenScene(item.scenePath);
                if (GUILayout.Button("查看测试数据")) Ping(item.testDataPath);
                if (GUILayout.Button("查看生成资源")) Ping(item.generatedPath);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }
            if (GUILayout.Button("保存调试记录")) AssetDatabase.SaveAssets();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawPath(string text, float width)
        {
            var style = EditorStyles.wordWrappedLabel;
            float height = Mathf.Max(18, style.CalcHeight(new GUIContent(text), width));
            EditorGUILayout.SelectableLabel(text, style, GUILayout.Width(width), GUILayout.Height(height));
        }

        private static void Ping(string path)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset != null) { Selection.activeObject = asset; EditorGUIUtility.PingObject(asset); }
            else Debug.Log($"Path reserved; assets not created yet: {path}");
        }
        private static void OpenScene(string path)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("先退出 Play Mode。"); return; }
            if (!File.Exists(path)) { Debug.LogWarning($"Scene not created: {path}"); return; }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(path);
        }

        [MenuItem("Project Realm/Debug/Open Learning Map")]
        public static void OpenLearning() => OpenScene(ProjectRealmWorkspaceLayout.LearningScene);

        [MenuItem("Project Realm/Debug/Open Combined Terrain Reference")]
        public static void OpenCombined() => OpenScene(ProjectRealmWorkspaceLayout.IntegrationScene);
    }
}
