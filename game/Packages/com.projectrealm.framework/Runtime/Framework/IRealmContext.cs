namespace ProjectRealm.Framework
{
    /// <summary>
    /// 类似 Android Context 的受控 Framework 入口。它只暴露 Manager 代理，不暴露 Service Registry、
    /// WorldRuntime 或任何静态 Current/Get&lt;T&gt;。
    /// </summary>
    public interface IRealmContext
    {
        WorldManager World { get; }
        SimulationManager Simulation { get; }
        SaveManager Saves { get; }
        NavigationManager Navigation { get; }
        DiagnosticsManager Diagnostics { get; }
        IRealmEventStream Events { get; }
    }
}
