using System.Collections.Generic;
using ProjectRealm.Foundation;
using ProjectRealm.World;

namespace ProjectRealm.Framework
{
    /// <summary>只读 Definition 数据源；实现可以是 SQLite、内存或测试替身。</summary>
    public interface IWorldDefinitionStore : IWorldDefinitionReader
    {
        WorldDefinition LoadWorld(StableId worldId);
    }

    /// <summary>Save 数据源；Domain/Application 不感知具体数据库或文件路径。</summary>
    public interface ISaveGameStore
    {
        bool Exists(StableId saveId);
        IReadOnlyList<StableId> ListSaveIds();
        WorldSaveData Load(StableId saveId);
        void Save(WorldSaveData saveData);
        void BackupBeforeMigration(StableId saveId, string migrationId);
    }

    /// <summary>模块私有状态的版本化二进制编解码器。</summary>
    public interface IModuleStateCodec
    {
        string CodecId { get; }
        byte[] Encode(object state);
        object Decode(byte[] payload);
    }

    /// <summary>按模块定义解析执行器的注册接口。</summary>
    public interface IModuleExecutorFactory
    {
        IModuleExecutor Create(ModuleDefinition definition);
    }

    /// <summary>旁路诊断输出；实现不得改变权威状态或消费随机流。</summary>
    public interface ISimulationDiagnosticsSink
    {
        void RecordStage(TickId tickId, StageExecutionRecord stage);
        void RecordModuleResult(ModuleResult result);
        void RecordCommandStatus(CommandStatusEvent statusEvent);
    }
}
