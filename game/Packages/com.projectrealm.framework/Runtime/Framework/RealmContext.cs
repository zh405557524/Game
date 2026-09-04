using System;

namespace ProjectRealm.Framework
{
    internal sealed class RealmContext : IRealmContext
    {
        public RealmContext(
            WorldManager world,
            SimulationManager simulation,
            SaveManager saves,
            NavigationManager navigation,
            DiagnosticsManager diagnostics,
            IRealmEventStream events)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            Simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
            Saves = saves ?? throw new ArgumentNullException(nameof(saves));
            Navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            Events = events ?? throw new ArgumentNullException(nameof(events));
        }

        public WorldManager World { get; }
        public SimulationManager Simulation { get; }
        public SaveManager Saves { get; }
        public NavigationManager Navigation { get; }
        public DiagnosticsManager Diagnostics { get; }
        public IRealmEventStream Events { get; }
    }
}
