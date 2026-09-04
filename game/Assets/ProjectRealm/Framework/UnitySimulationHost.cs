using System;
using System.Text;
using ProjectRealm.Application;
using ProjectRealm.Domain;
using ProjectRealm.Infrastructure.Sqlite;
using SQLite;
using UnityEngine;

namespace ProjectRealm.UnityFramework
{
    /// <summary>
    /// Unity 生命周期适配器。它只响应显式按钮或方法调用，不在 Update 中推进权威世界。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UnitySimulationHost : MonoBehaviour
    {
        private const string DefaultDefinitionResource = "realm_definition_ming1628_dev_v1";

        [SerializeField] private SQLiteAsset definitionAsset;
        [SerializeField] private string saveId = "development-framework";
        [SerializeField] private string worldId = "MING1628";
        [SerializeField] private long worldSeed = 1628;
        [SerializeField] private bool initializeOnAwake;

        public static UnitySimulationHost Active { get; private set; }

        public event Action RuntimeChanged;

        public WorldRuntime Runtime { get; private set; }

        public string LastError { get; private set; } = string.Empty;

        public StableId SaveId => new StableId(saveId);

        private void Awake()
        {
            if (Active != null && Active != this)
            {
                throw new InvalidOperationException("Only one UnitySimulationHost may be active.");
            }

            Active = this;
            if (initializeOnAwake)
            {
                CreateDevelopmentWorld();
            }
        }

        private void OnDestroy()
        {
            if (Active == this)
            {
                Active = null;
            }
        }

        /// <summary>从开发 Definition 创建一个内存中的新世界。</summary>
        public void CreateDevelopmentWorld()
        {
            Execute(() =>
            {
                var bootstrapper = CreateBootstrapper(out _);
                Runtime = bootstrapper.StartNewWorld(new WorldBootstrapRequest(
                    SaveId,
                    new StableId(worldId),
                    new WorldSeed(worldSeed)));
            });
        }

        /// <summary>从 persistentDataPath 读取当前配置的开发存档。</summary>
        public void LoadDevelopmentWorld()
        {
            Execute(() =>
            {
                var bootstrapper = CreateBootstrapper(out _);
                Runtime = bootstrapper.LoadWorld(new LoadWorldRequest(SaveId));
            });
        }

        /// <summary>保存最近闭合 Tick；不会隐式推进时间。</summary>
        public void SaveDevelopmentWorld()
        {
            Execute(() => RequireRuntime().Save());
        }

        /// <summary>显式推进日、月、季或年。</summary>
        public WorldTickResult Step(AdvanceUnit unit)
        {
            WorldTickResult result = null;
            Execute(() => result = RequireRuntime().Advance(new AdvanceRequest(unit)));
            return result;
        }

        /// <summary>导出只读文本诊断，不改变状态散列。</summary>
        public string ExportDiagnostics()
        {
            var runtime = RequireRuntime();
            var snapshot = new SimulationDiagnosticsQuery().Query(runtime, pageSize: 1);
            var builder = new StringBuilder();
            builder.AppendLine("Project Realm Framework Diagnostics");
            builder.AppendLine("world=" + runtime.WorldId.Value);
            builder.AppendLine("save=" + runtime.SaveId.Value);
            builder.AppendLine("tick=" + snapshot.Clock.TickSequence);
            builder.AppendLine($"calendar={snapshot.Clock.EconomicYear:D4}-{snapshot.Clock.Month:D2}-{snapshot.Clock.Day:D2}");
            builder.AppendLine("state_sha256=" + snapshot.StateHash.Sha256);
            builder.AppendLine("geographic_nodes=" + snapshot.GeographicNodeCount);
            builder.AppendLine("factions=" + snapshot.FactionCount);
            builder.AppendLine("jurisdictions=" + snapshot.JurisdictionCount);
            builder.AppendLine("module_instances=" + snapshot.ModuleInstanceCount);
            builder.AppendLine("scaffold_modules=" + snapshot.ScaffoldModuleCount);
            builder.AppendLine("definition_sha256=" + runtime.Ruleset.DefinitionContentHash);
            builder.AppendLine("commercial_release_ready=" + runtime.Ruleset.CommercialReleaseReady);
            return builder.ToString();
        }

        private WorldBootstrapper CreateBootstrapper(out SqliteSaveGameStore saveStore)
        {
            var asset = definitionAsset != null
                ? definitionAsset
                : Resources.Load<SQLiteAsset>(DefaultDefinitionResource);
            if (asset == null)
            {
                throw new InvalidOperationException(
                    "The development Definition database is missing. Run: python3 tools/framework/build_runtime_definition.py");
            }

            var definitions = new SqliteWorldDefinitionStore(asset);
            saveStore = new SqliteSaveGameStore(UnityEngine.Application.persistentDataPath);
            return new WorldBootstrapper(definitions, saveStore);
        }

        private WorldRuntime RequireRuntime()
        {
            return Runtime ?? throw new InvalidOperationException("Create or load a framework world first.");
        }

        private void Execute(Action action)
        {
            try
            {
                LastError = string.Empty;
                action();
                RuntimeChanged?.Invoke();
            }
            catch (Exception exception)
            {
                LastError = exception.Message;
                Debug.LogException(exception, this);
                throw;
            }
        }
    }
}
