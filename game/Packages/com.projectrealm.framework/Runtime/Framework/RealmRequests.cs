using System;

namespace ProjectRealm.Framework
{
    /// <summary>玩家可显式请求的闭合模拟周期。</summary>
    public enum RealmAdvanceUnit
    {
        Day,
        Month,
        Season,
        Year
    }

    /// <summary>从 Definition 创建空状态世界的请求。</summary>
    public sealed class NewRealmWorldRequest
    {
        public NewRealmWorldRequest(string saveId, string worldId, long worldSeed)
        {
            SaveId = RequireText(saveId, nameof(saveId));
            WorldId = RequireText(worldId, nameof(worldId));
            WorldSeed = worldSeed;
        }

        public string SaveId { get; }
        public string WorldId { get; }
        public long WorldSeed { get; }

        private static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("A stable identifier is required.", parameterName);
            }

            return value;
        }
    }

    /// <summary>UI 提交给模拟命令队列的无权威状态请求。</summary>
    public sealed class RealmCommandRequest
    {
        public RealmCommandRequest(
            string commandInstanceId,
            string commandDefinitionId,
            string actorId,
            string targetId,
            string authorityScopeId,
            string idempotencyKey,
            byte[] payload = null)
        {
            CommandInstanceId = RequireText(commandInstanceId, nameof(commandInstanceId));
            CommandDefinitionId = RequireText(commandDefinitionId, nameof(commandDefinitionId));
            ActorId = RequireText(actorId, nameof(actorId));
            TargetId = RequireText(targetId, nameof(targetId));
            AuthorityScopeId = RequireText(authorityScopeId, nameof(authorityScopeId));
            IdempotencyKey = RequireText(idempotencyKey, nameof(idempotencyKey));
            Payload = (byte[])(payload ?? Array.Empty<byte>()).Clone();
        }

        public string CommandInstanceId { get; }
        public string CommandDefinitionId { get; }
        public string ActorId { get; }
        public string TargetId { get; }
        public string AuthorityScopeId { get; }
        public string IdempotencyKey { get; }
        public byte[] Payload { get; }

        private static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("A command identifier is required.", parameterName);
            }

            return value;
        }
    }
}
