using System;
using ProjectRealm.Foundation;

namespace ProjectRealm.Framework
{
    /// <summary>
    /// 显式推进时间和提交命令的 Framework 代理。所有请求进入 SimulationService，最终由
    /// WorldRuntime 执行完整闭合 Tick；Unity <c>Update()</c> 不能成为权威时间来源。
    /// </summary>
    public sealed class SimulationManager
    {
        private readonly ISimulationManagerGateway _gateway;

        internal SimulationManager(ISimulationManagerGateway gateway)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        }

        /// <summary>推进日、月、季或年；月以上单位仍由连续日 Tick 到达闭合边界。</summary>
        public RealmResult<SimulationStepSnapshot> Advance(RealmAdvanceUnit unit) => _gateway.Advance(unit);
        public RealmResult<CommandTicketSnapshot> Submit(RealmCommandRequest request) => _gateway.Submit(request);
        public RealmResult Pause() => _gateway.Pause();
        public RealmResult Resume() => _gateway.Resume();
    }
}
