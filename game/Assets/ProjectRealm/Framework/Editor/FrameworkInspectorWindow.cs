using System;
using System.IO;
using System.Text;
using ProjectRealm.Bootstrap;
using ProjectRealm.Foundation;
using ProjectRealm.Framework;
using SQLite;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ProjectRealm.Framework.Editor
{
    /// <summary>只通过 Framework Manager 查询和操作世界的 Editor 调试窗口。</summary>
    public sealed class FrameworkInspectorWindow : EditorWindow
    {
        private static readonly string[] Tabs =
        {
            "World / Graphs", "Module Graph", "Tick Timeline",
            "Commands & Events", "Snapshots / Ledgers", "Persistence / Audit"
        };

        private const int PageSize = 50;
        private int _selectedTab;
        private int _page;
        private string _search = string.Empty;
        private Vector2 _scroll;

        [MenuItem("Project Realm/Simulation/Framework Inspector")]
        public static void Open() => GetWindow<FrameworkInspectorWindow>("Realm Framework");

        private void OnGUI()
        {
            var application = FindApplication();
            DrawToolbar(application);
            EditorGUILayout.Space();
            _selectedTab = GUILayout.Toolbar(_selectedTab, Tabs);

            if (application == null || application.Context == null)
            {
                EditorGUILayout.HelpBox(
                    "No running RealmApplication was found. Open 00_Bootstrap and enter Play Mode. The Inspector no longer creates a second simulation host.",
                    MessageType.Info);
                return;
            }

            if (!string.IsNullOrEmpty(application.LastError))
            {
                EditorGUILayout.HelpBox(application.LastError, MessageType.Error);
            }

            var result = application.Context.Diagnostics.Query(_search, _page, PageSize);
            if (!result.Succeeded)
            {
                EditorGUILayout.HelpBox($"{result.Error.Code}: {result.Error.Message}", MessageType.Error);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawTab(application, result.Value);
            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar(RealmApplication application)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            var context = application?.Context;
            var hasWorld = context?.World.HasActiveWorld == true;
            using (new EditorGUI.DisabledScope(context == null || hasWorld))
            {
                if (GUILayout.Button("New", EditorStyles.toolbarButton))
                    Invoke(() => context.World.Create(new NewRealmWorldRequest("development-framework", "MING1628", 1628)));
                if (GUILayout.Button("Load", EditorStyles.toolbarButton)) Invoke(() => context.Saves.Load("development-framework"));
            }

            using (new EditorGUI.DisabledScope(context == null || !hasWorld))
            {
                if (GUILayout.Button("+Day", EditorStyles.toolbarButton)) Invoke(() => context.Simulation.Advance(RealmAdvanceUnit.Day));
                if (GUILayout.Button("+Month", EditorStyles.toolbarButton)) Invoke(() => context.Simulation.Advance(RealmAdvanceUnit.Month));
                if (GUILayout.Button("+Season", EditorStyles.toolbarButton)) Invoke(() => context.Simulation.Advance(RealmAdvanceUnit.Season));
                if (GUILayout.Button("+Year", EditorStyles.toolbarButton)) Invoke(() => context.Simulation.Advance(RealmAdvanceUnit.Year));
                if (GUILayout.Button("Save", EditorStyles.toolbarButton)) Invoke(context.Saves.Save);
                if (GUILayout.Button("Close", EditorStyles.toolbarButton)) Invoke(context.World.Close);
                if (GUILayout.Button("Export", EditorStyles.toolbarButton)) Export(context.Diagnostics);
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label(application == null ? "No RealmApplication" : application.State.ToString(), EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTab(RealmApplication application, RealmDiagnosticsSnapshot diagnostics)
        {
            switch (_selectedTab)
            {
                case 0: DrawWorld(application, diagnostics); break;
                case 1: DrawModules(diagnostics); break;
                case 2: DrawStages(diagnostics); break;
                case 3: DrawCommandCounts(diagnostics); break;
                case 4: DrawSnapshotCounts(diagnostics); break;
                case 5: DrawPersistence(diagnostics); break;
            }
        }

        private void DrawWorld(RealmApplication application, RealmDiagnosticsSnapshot diagnostics)
        {
            DrawClockAndHash(diagnostics.World);
            EditorGUILayout.LabelField("Application state", application.State.ToString());
            EditorGUILayout.LabelField("Geographic nodes", diagnostics.World.GeographicNodeCount.ToString());
            EditorGUILayout.LabelField("Factions", diagnostics.FactionCount.ToString());
            EditorGUILayout.LabelField("Jurisdictions", diagnostics.JurisdictionCount.ToString());
            DrawSearchAndPaging();
            foreach (var node in diagnostics.Nodes)
                EditorGUILayout.LabelField(node.Id, $"{node.Kind} | {node.DisplayName}");
        }

        private void DrawModules(RealmDiagnosticsSnapshot diagnostics)
        {
            EditorGUILayout.LabelField("Module instances", diagnostics.World.ModuleInstanceCount.ToString());
            EditorGUILayout.LabelField("Scaffold / Unavailable", diagnostics.World.ScaffoldModuleCount.ToString());
            EditorGUILayout.HelpBox("Scaffold means the framework ran, not that population, inventory or economy equals zero.", MessageType.Warning);
            DrawSearchAndPaging();
            foreach (var module in diagnostics.Modules)
                EditorGUILayout.LabelField(module.InstanceId,
                    $"{module.NodeId} | {module.DefinitionId} | {module.Lifecycle} | {module.ImplementationTier}");
        }

        private static void DrawStages(RealmDiagnosticsSnapshot diagnostics)
        {
            DrawClockAndHash(diagnostics.World);
            if (diagnostics.Stages.Count == 0)
            {
                EditorGUILayout.HelpBox("No Tick has run yet.", MessageType.Info);
                return;
            }

            foreach (var stage in diagnostics.Stages)
                EditorGUILayout.LabelField(stage.Stage,
                    $"executions={stage.ExecutionCount}, success={stage.Succeeded}, reason={stage.FailureCode}");
        }

        private static void DrawCommandCounts(RealmDiagnosticsSnapshot diagnostics)
        {
            EditorGUILayout.LabelField("Commands", diagnostics.CommandCount.ToString());
            EditorGUILayout.LabelField("Reservations", diagnostics.ReservationCount.ToString());
            EditorGUILayout.LabelField("Committed events", diagnostics.EventCount.ToString());
        }

        private static void DrawSnapshotCounts(RealmDiagnosticsSnapshot diagnostics)
        {
            EditorGUILayout.LabelField("Current data quality", diagnostics.World.DataQuality);
            EditorGUILayout.LabelField("Closed checkpoints", diagnostics.CheckpointCount.ToString());
            EditorGUILayout.LabelField("Latest failure", diagnostics.LatestFailure);
        }

        private static void DrawPersistence(RealmDiagnosticsSnapshot diagnostics)
        {
            EditorGUILayout.LabelField("Save ID", diagnostics.World.SaveId);
            EditorGUILayout.LabelField("State SHA-256", diagnostics.World.StateHash);
            EditorGUILayout.LabelField("Commercial release ready", diagnostics.World.CommercialReleaseReady.ToString());
            EditorGUILayout.HelpBox("The active Definition is development-only while authoritative capabilities remain Scaffold/Unavailable.", MessageType.Warning);
        }

        private void DrawSearchAndPaging()
        {
            EditorGUILayout.BeginHorizontal();
            var next = EditorGUILayout.TextField("Search", _search);
            if (!string.Equals(next, _search, StringComparison.Ordinal))
            {
                _search = next;
                _page = 0;
            }
            using (new EditorGUI.DisabledScope(_page == 0))
            {
                if (GUILayout.Button("Previous", GUILayout.Width(72))) _page--;
            }
            if (GUILayout.Button("Next", GUILayout.Width(52))) _page++;
            GUILayout.Label("Page " + (_page + 1), GUILayout.Width(60));
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawClockAndHash(WorldSessionSnapshot world)
        {
            if (!world.HasActiveWorld)
            {
                EditorGUILayout.HelpBox("No active world. MainMenu and Inspector queries do not create one.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Economic date", $"Y{world.Year:D4} M{world.Month:D2} D{world.Day:D2}");
            EditorGUILayout.LabelField("Tick", world.Tick.ToString());
            EditorGUILayout.LabelField("State SHA-256", world.StateHash);
        }

        private static RealmApplication FindApplication() =>
            FindAnyObjectByType<RealmApplication>(FindObjectsInactive.Include);

        private static void Invoke(Func<RealmResult> action)
        {
            var result = action();
            if (!result.Succeeded) Debug.LogError($"{result.Error.Code}: {result.Error.Message}");
        }

        private static void Invoke<T>(Func<RealmResult<T>> action)
        {
            var result = action();
            if (!result.Succeeded) Debug.LogError($"{result.Error.Code}: {result.Error.Message}");
        }

        private static void Export(DiagnosticsManager diagnostics)
        {
            var result = diagnostics.Query(pageSize: 500);
            if (!result.Succeeded)
            {
                Debug.LogError($"{result.Error.Code}: {result.Error.Message}");
                return;
            }

            var snapshot = result.Value;
            var text = new StringBuilder()
                .AppendLine("Project Realm Framework Diagnostics")
                .AppendLine("world=" + snapshot.World.WorldId)
                .AppendLine("save=" + snapshot.World.SaveId)
                .AppendLine("tick=" + snapshot.World.Tick)
                .AppendLine("state_sha256=" + snapshot.World.StateHash)
                .AppendLine("geographic_nodes=" + snapshot.World.GeographicNodeCount)
                .AppendLine("module_instances=" + snapshot.World.ModuleInstanceCount)
                .AppendLine("scaffold_modules=" + snapshot.World.ScaffoldModuleCount)
                .ToString();
            var path = EditorUtility.SaveFilePanel("Export Project Realm diagnostics", "", "project-realm-framework.txt", "txt");
            if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, text);
        }
    }

    public sealed class FrameworkDefinitionBuildGuard : IPreprocessBuildWithReport
    {
        private const string DefinitionAssetPath =
            "Assets/ProjectRealm/Content/Definitions/Development/Resources/realm_definition_ming1628_dev_v1.sqlite";

        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            var asset = AssetDatabase.LoadAssetAtPath<SQLiteAsset>(DefinitionAssetPath);
            if (asset == null)
                throw new BuildFailedException(
                    "Development Definition DB is missing. Run: python3 tools/framework/build_runtime_definition.py");
            if ((report.summary.options & BuildOptions.Development) == 0)
                throw new BuildFailedException(
                    "Commercial/Release build blocked: the active Definition is development-only and all authoritative capabilities are Scaffold/Unavailable.");
        }
    }
}
