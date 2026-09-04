using System;
using System.Collections.Generic;
using ProjectRealm.Foundation;

namespace ProjectRealm.Framework
{
    /// <summary>
    /// 列出、写入和读取闭合 Tick 存档的 Framework 代理。保存动作由 SaveService 协调，
    /// Manager 不接触 SQLite 连接；读取必须通过 Definition 与检查点兼容性校验。
    /// </summary>
    public sealed class SaveManager
    {
        private readonly ISaveManagerGateway _gateway;

        internal SaveManager(ISaveManagerGateway gateway)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        }

        public RealmResult<IReadOnlyList<SaveSlotSnapshot>> ListSlots() => _gateway.ListSlots();
        public RealmResult<WorldSessionSnapshot> Load(string saveId) => _gateway.Load(saveId);
        /// <summary>保存最近一次已闭合的权威状态，不触发额外 Tick。</summary>
        public RealmResult Save() => _gateway.Save();
    }
}
