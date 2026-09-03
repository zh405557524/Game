using ProjectRealm.Domain;

namespace ProjectRealm.Ports
{
    public interface IWorldDefinitionStore : IWorldDefinitionReader
    {
        WorldDefinition LoadWorld(StableId worldId);
    }

    public interface ISaveGameStore
    {
        bool Exists(StableId saveId);
        WorldSaveData Load(StableId saveId);
        void Save(WorldSaveData saveData);
        void BackupBeforeMigration(StableId saveId, string migrationId);
    }

    public interface IModuleStateCodec
    {
        string CodecId { get; }
        byte[] Encode(object state);
        object Decode(byte[] payload);
    }

    public interface IModuleExecutorFactory
    {
        IModuleExecutor Create(ModuleDefinition definition);
    }

    public interface ISimulationDiagnosticsSink
    {
        void RecordStage(TickId tickId, StageExecutionRecord stage);
        void RecordModuleResult(ModuleResult result);
        void RecordCommandStatus(CommandStatusEvent statusEvent);
    }
}
