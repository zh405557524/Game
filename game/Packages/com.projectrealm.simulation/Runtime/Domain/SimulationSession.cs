using System;

namespace ProjectRealm.Domain
{
    /// <summary>
    /// 保留旧公开 API 的兼容外壳。新世界由 WorldRuntime 驱动，旧调用仍可使用 AdvanceDay。
    /// </summary>
    public sealed class SimulationSession
    {
        private readonly ISimulationSessionRuntime _runtime;

        public SimulationSession(StableId worldId, WorldSeed worldSeed)
            : this(worldId, worldSeed, new LegacySessionRuntime())
        {
        }

        public SimulationSession(StableId worldId, WorldSeed worldSeed, ISimulationSessionRuntime runtime)
        {
            if (string.IsNullOrEmpty(worldId.Value))
            {
                throw new ArgumentException("A simulation session requires a world ID.", nameof(worldId));
            }

            WorldId = worldId;
            WorldSeed = worldSeed;
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public StableId WorldId { get; }

        public WorldSeed WorldSeed { get; }

        public long ElapsedDays => _runtime.ElapsedDays;

        public void AdvanceDay()
        {
            _runtime.AdvanceOneDay();
        }

        // 仅为没有 Application 层依赖的旧单元测试保留；不代表完整世界模拟。
        private sealed class LegacySessionRuntime : ISimulationSessionRuntime
        {
            public long ElapsedDays { get; private set; }

            public void AdvanceOneDay()
            {
                checked
                {
                    ElapsedDays++;
                }
            }
        }
    }
}
