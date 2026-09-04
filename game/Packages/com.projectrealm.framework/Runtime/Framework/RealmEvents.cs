namespace ProjectRealm.Framework
{
    public sealed class RealmApplicationStateChangedEvent
    {
        public RealmApplicationStateChangedEvent(RealmApplicationState previous, RealmApplicationState current, string reason)
        {
            Previous = previous;
            Current = current;
            Reason = reason ?? string.Empty;
        }

        public RealmApplicationState Previous { get; }
        public RealmApplicationState Current { get; }
        public string Reason { get; }
    }

    public sealed class RealmWorldOpenedEvent
    {
        public RealmWorldOpenedEvent(WorldSessionSnapshot world, bool loadedFromSave)
        {
            World = world;
            LoadedFromSave = loadedFromSave;
        }

        public WorldSessionSnapshot World { get; }
        public bool LoadedFromSave { get; }
    }

    public sealed class RealmWorldClosedEvent
    {
        public RealmWorldClosedEvent(string saveId)
        {
            SaveId = saveId ?? string.Empty;
        }

        public string SaveId { get; }
    }

    public sealed class RealmSimulationAdvancedEvent
    {
        public RealmSimulationAdvancedEvent(SimulationStepSnapshot step)
        {
            Step = step;
        }

        public SimulationStepSnapshot Step { get; }
    }

    public sealed class RealmSaveCompletedEvent
    {
        public RealmSaveCompletedEvent(string saveId, string stateHash)
        {
            SaveId = saveId ?? string.Empty;
            StateHash = stateHash ?? string.Empty;
        }

        public string SaveId { get; }
        public string StateHash { get; }
    }

    public sealed class RealmFaultedEvent
    {
        public RealmFaultedEvent(string code, string message)
        {
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public string Code { get; }
        public string Message { get; }
    }
}
