using System;
using ProjectRealm.Foundation;

namespace ProjectRealm.Framework
{
    /// <summary>
    /// 玩家层用于创建、查看和关闭世界的 Framework 代理。它只转发请求并返回不可变摘要，
    /// 不持有 <c>WorldRuntime</c>，也不允许调用方绕过 System Server 修改拓扑或模块状态。
    /// </summary>
    public sealed class WorldManager
    {
        private readonly IWorldManagerGateway _gateway;

        internal WorldManager(IWorldManagerGateway gateway)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        }

        public bool HasActiveWorld => _gateway.HasActiveWorld;
        public RealmResult<WorldSessionSnapshot> GetCurrent() => _gateway.GetCurrent();
        /// <summary>从 Definition 创建空状态世界和初始检查点；不会隐式推进 Tick。</summary>
        public RealmResult<WorldSessionSnapshot> Create(NewRealmWorldRequest request) => _gateway.Create(request);
        public RealmResult Close() => _gateway.Close();
    }
}
