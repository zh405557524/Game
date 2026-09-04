using System;
using ProjectRealm.Foundation;

namespace ProjectRealm.Framework
{
    /// <summary>
    /// 面向开发工具的只读诊断代理。查询由 DiagnosticsService 从已提交状态组装不可变投影，
    /// 不得推进 Tick、消费随机流或改变状态散列。
    /// </summary>
    public sealed class DiagnosticsManager
    {
        private readonly IDiagnosticsManagerGateway _gateway;

        internal DiagnosticsManager(IDiagnosticsManagerGateway gateway)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        }

        public RealmResult<RealmDiagnosticsSnapshot> Query(string search = null, int page = 0, int pageSize = 50) =>
            _gateway.Query(search, page, pageSize);
    }
}
