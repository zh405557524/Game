using System;
using System.IO;
using System.Linq;
using ProjectRealm.Application;
using ProjectRealm.Domain;
using ProjectRealm.UnityFramework;
using SQLite;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ProjectRealm.Framework.Editor
{
    /// <summary>
    /// 世界模拟只读调试窗口。绘制和切换页签不推进时间；只有工具栏中的显式动作会调用 Host。
    /// </summary>
    public sealed class FrameworkInspectorWindow : EditorWindow
    {
        private static readonly string[] Tabs =
        {
            "World / Graphs",
            "Module Graph",
            "Tick Timeline",
            "Commands & Events",
            "Snapshots / Ledgers",
            "Persistence / Audit"
        };

        private int _selectedTab;
        private Vector2 _scroll;
        private string _search = string.Empty;
        private int _page;
        private const int PageSize = 50;

        [MenuItem("Project Realm/Simulation/Framework Inspector")]
        public static void Open()
        {
            GetWindow<FrameworkInspectorWindow>("Realm Framework");
        }

        private void OnGUI()
        {
            var host = FindHost();
            DrawToolbar(host);
            EditorGUILayout.Space();
            _selectedTab = GUILayout.Toolbar(_selectedTab, Tabs);
            EditorGUILayout.Space();

            if (host == null || host.Runtime == null)
            {
                EditorGUILayout.HelpBox(
                    host == null
                        ? "No UnitySimulationHost exists in the open scene. Creating a host is an explicit scene action."
                        : "The host has no world. Create or load a development world.",
                    MessageType.Info);
                if (host == null && GUILayout.Button("Create UnitySimulationHost in Scene"))
                {
                    var gameObject = new GameObject("Project Realm Simulation Host");
                    gameObject.AddComponent<UnitySimulationHost>();
                    Undo.RegisterCreatedObjectUndo(gameObject, "Create Project Realm Simulation Host");
                    Selection.activeGameObject = gameObject;
                }

                return;
            }

            if (!string.IsNullOrEmpty(host.LastError))
            {
                EditorGUILayout.HelpBox(host.LastError, MessageType.Error);
            }

            var runtime = host.Runtime;
            // 每次重绘重新创建只读投影，不缓存或修改权威运行时对象。
            var diagnostics = new SimulationDiagnosticsQuery().Query(runtime, _search, _page, PageSize);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            switch (_selectedTab)
            {
                case 0: DrawWorld(diagnostics); break;
                case 1: DrawModules(runtime, diagnostics); break;
                case 2: DrawTickTimeline(diagnostics); break;
                case 3: DrawCommandsAndEvents(runtime); break;
                case 4: DrawSnapshots(runtime); break;
                case 5: DrawPersistence(runtime, host); break;
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar(UnitySimulationHost host)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            using (new EditorGUI.DisabledScope(host == null))
            {
                if (GUILayout.Button("New", EditorStyles.toolbarButton)) Invoke(host, host.CreateDevelopmentWorld);
                if (GUILayout.Button("Load", EditorStyles.toolbarButton)) Invoke(host, host.LoadDevelopmentWorld);
            }

            using (new EditorGUI.DisabledScope(host == null || host.Runtime == null))
            {
                if (GUILayout.Button("+Day", EditorStyles.toolbarButton)) Invoke(host, () => host.Step(AdvanceUnit.Day));
                if (GUILayout.Button("+Month", EditorStyles.toolbarButton)) Invoke(host, () => host.Step(AdvanceUnit.Month));
                if (GUILayout.Button("+Season", EditorStyles.toolbarButton)) Invoke(host, () => host.Step(AdvanceUnit.Season));
                if (GUILayout.Button("+Year", EditorStyles.toolbarButton)) Invoke(host, () => host.Step(AdvanceUnit.Year));
                if (GUILayout.Button("Save", EditorStyles.toolbarButton)) Invoke(host, host.SaveDevelopmentWorld);
                if (GUILayout.Button("Reload", EditorStyles.toolbarButton)) Invoke(host, host.LoadDevelopmentWorld);
                if (GUILayout.Button("Export", EditorStyles.toolbarButton)) Export(host);
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label("Read-only unless an action button is pressed", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawWorld(SimulationDiagnosticsSnapshot diagnostics)
        {
            DrawClockAndHash(diagnostics);
            EditorGUILayout.LabelField("Topology", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Geographic nodes", diagnostics.GeographicNodeCount.ToString());
            EditorGUILayout.LabelField("Factions", diagnostics.FactionCount.ToString());
            EditorGUILayout.LabelField("Jurisdictions", diagnostics.JurisdictionCount.ToString());
            DrawSearchAndPaging();
            foreach (var node in diagnostics.Nodes)
            {
                EditorGUILayout.LabelField(
                    node.NodeId.Value,
                    $"{node.Kind} | {node.DisplayName} | parent={node.GeographicParentId?.Value ?? "-"}");
            }
        }

        private void DrawModules(WorldRuntime runtime, SimulationDiagnosticsSnapshot diagnostics)
        {
            EditorGUILayout.LabelField("Module graph", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Instances", diagnostics.ModuleInstanceCount.ToString());
            EditorGUILayout.LabelField("Scaffold / Unavailable", diagnostics.ScaffoldModuleCount.ToString());
            EditorGUILayout.HelpBox(
                "Scaffold modules close framework ticks but do not claim real domain facts or numeric zeroes.",
                MessageType.Warning);
            DrawSearchAndPaging();
            foreach (var instance in diagnostics.Modules)
            {
                var definition = runtime.ModuleCatalog.GetRequired(instance.DefinitionId);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(instance.NodeId.Value, GUILayout.Width(210));
                EditorGUILayout.LabelField(definition.SourceName, GUILayout.Width(230));
                GUILayout.Label(instance.LifecycleState.ToString(), GUILayout.Width(80));
                GUILayout.Label("SCAFFOLD / UNAVAILABLE", EditorStyles.miniBoldLabel);
                EditorGUILayout.EndHorizontal();
            }
        }

        private static void DrawTickTimeline(SimulationDiagnosticsSnapshot diagnostics)
        {
            DrawClockAndHash(diagnostics);
            var tick = diagnostics.LatestTick;
            if (tick == null)
            {
                EditorGUILayout.HelpBox("No tick has run yet.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Tick " + tick.TickId.Value, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Committed", tick.Committed.ToString());
            EditorGUILayout.LabelField("Period closes", tick.PeriodCloseFlags.ToString());
            foreach (var stage in tick.Stages)
            {
                EditorGUILayout.LabelField(
                    $"{(int)stage.Stage:D3} {stage.Stage}",
                    $"executions={stage.ModuleExecutionCount}, success={stage.Succeeded}, reason={stage.ReasonCode}");
            }
        }

        private static void DrawCommandsAndEvents(WorldRuntime runtime)
        {
            EditorGUILayout.LabelField("Commands", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Count", runtime.Commands.Count.ToString());
            foreach (var command in runtime.Commands.Take(200))
            {
                EditorGUILayout.LabelField(
                    command.Envelope.CommandInstanceId.Value,
                    $"{command.Status} | {command.Envelope.CommandDefinitionId.Value} | transitions={command.StatusEvents.Count}");
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Committed events", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Count", runtime.Events.Count.ToString());
            foreach (var item in runtime.Events.Take(200))
            {
                EditorGUILayout.LabelField(item.EventId.Value, $"tick={item.CommittedTick.Value}");
            }
        }

        private static void DrawSnapshots(WorldRuntime runtime)
        {
            EditorGUILayout.LabelField("Latest node snapshots", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Count", runtime.LatestNodeSnapshots.Count.ToString());
            foreach (var snapshot in runtime.LatestNodeSnapshots.Take(200))
            {
                EditorGUILayout.LabelField(
                    snapshot.NodeId.Value,
                    $"tick={snapshot.TickId.Value} | quality={snapshot.DataQuality} | {snapshot.StateHash.Sha256.Substring(0, 12)}…");
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Latest empty period ledgers", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Count", runtime.LatestNodeResults.Count.ToString());
            foreach (var result in runtime.LatestNodeResults.Take(100))
            {
                EditorGUILayout.LabelField(
                    result.NodeId.Value,
                    $"{result.DataQuality} | modules={result.ModuleResults.Count} | residuals={result.ResidualLedger.ResidualKeys.Count}");
            }
        }

        private static void DrawPersistence(WorldRuntime runtime, UnitySimulationHost host)
        {
            EditorGUILayout.LabelField("Persistence and audit", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Save ID", runtime.SaveId.Value);
            EditorGUILayout.LabelField("Closed checkpoints", runtime.Checkpoints.Count.ToString());
            EditorGUILayout.LabelField("Current state SHA-256", runtime.CurrentStateHash.Sha256);
            EditorGUILayout.LabelField("Definition SHA-256", runtime.Ruleset.DefinitionContentHash);
            EditorGUILayout.LabelField("Ruleset", runtime.Ruleset.RulesetVersion);
            EditorGUILayout.LabelField("Module catalog", runtime.Ruleset.ModuleCatalogVersion);
            EditorGUILayout.LabelField("State schema", runtime.Ruleset.StateSchemaVersion);
            EditorGUILayout.LabelField("Commercial release ready", runtime.Ruleset.CommercialReleaseReady.ToString());
            EditorGUILayout.HelpBox(
                "This Definition is development-only. A non-development build is blocked while it is present.",
                MessageType.Warning);
            EditorGUILayout.TextArea(host.ExportDiagnostics(), GUILayout.MinHeight(220));
        }

        private void DrawSearchAndPaging()
        {
            EditorGUILayout.BeginHorizontal();
            var nextSearch = EditorGUILayout.TextField("Search", _search);
            if (!string.Equals(nextSearch, _search, StringComparison.Ordinal))
            {
                _search = nextSearch;
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

        private static void DrawClockAndHash(SimulationDiagnosticsSnapshot diagnostics)
        {
            EditorGUILayout.LabelField("World clock", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Economic date",
                $"Y{diagnostics.Clock.EconomicYear:D4} M{diagnostics.Clock.Month:D2} D{diagnostics.Clock.Day:D2}");
            EditorGUILayout.LabelField("Tick", diagnostics.Clock.TickSequence.ToString());
            EditorGUILayout.LabelField("State SHA-256", diagnostics.StateHash.Sha256);
        }

        private static UnitySimulationHost FindHost()
        {
            return UnitySimulationHost.Active != null
                ? UnitySimulationHost.Active
                : FindAnyObjectByType<UnitySimulationHost>(FindObjectsInactive.Include);
        }

        private static void Invoke(UnitySimulationHost host, Action action)
        {
            try
            {
                action();
                EditorUtility.SetDirty(host);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static void Export(UnitySimulationHost host)
        {
            var path = EditorUtility.SaveFilePanel("Export Project Realm diagnostics", "", "project-realm-framework.txt", "txt");
            if (!string.IsNullOrEmpty(path))
            {
                File.WriteAllText(path, host.ExportDiagnostics());
            }
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
            {
                throw new BuildFailedException(
                    "Development Definition DB is missing. Run: python3 tools/framework/build_runtime_definition.py");
            }

            if ((report.summary.options & BuildOptions.Development) == 0)
            {
                throw new BuildFailedException(
                    "Commercial/Release build blocked: the active Definition is development-only and all authoritative capabilities are Scaffold/Unavailable.");
            }
        }
    }
}
