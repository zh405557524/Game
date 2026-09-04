using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ProjectRealm.Domain
{
    /// <summary>命令从草拟、校验、预留、执行到结算的完整状态机。</summary>
    public enum CommandStatus
    {
        Drafted,
        Submitted,
        Validating,
        Accepted,
        Rejected,
        Reserving,
        Reserved,
        ReservationFailed,
        Dispatched,
        Executing,
        Completed,
        PartiallyCompleted,
        Failed,
        Settled,
        Cancelled,
        Expired,
        Superseded,
        Suspended
    }

    /// <summary>跨层传递的不可变命令信封，包含权限作用域和幂等键。</summary>
    public sealed class CommandEnvelope
    {
        public CommandEnvelope(
            StableId commandInstanceId,
            StableId commandDefinitionId,
            StableId actorId,
            StableId targetId,
            StableId authorityScopeId,
            string idempotencyKey,
            byte[] payload,
            TickId submittedTick)
        {
            SimulationNode.RequireId(commandInstanceId, nameof(commandInstanceId));
            SimulationNode.RequireId(commandDefinitionId, nameof(commandDefinitionId));
            SimulationNode.RequireId(actorId, nameof(actorId));
            SimulationNode.RequireId(targetId, nameof(targetId));
            SimulationNode.RequireId(authorityScopeId, nameof(authorityScopeId));
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                throw new ArgumentException("A command idempotency key is required.", nameof(idempotencyKey));
            }

            CommandInstanceId = commandInstanceId;
            CommandDefinitionId = commandDefinitionId;
            ActorId = actorId;
            TargetId = targetId;
            AuthorityScopeId = authorityScopeId;
            IdempotencyKey = idempotencyKey;
            Payload = (byte[])(payload ?? Array.Empty<byte>()).Clone();
            SubmittedTick = submittedTick;
        }

        public StableId CommandInstanceId { get; }
        public StableId CommandDefinitionId { get; }
        public StableId ActorId { get; }
        public StableId TargetId { get; }
        public StableId AuthorityScopeId { get; }
        public string IdempotencyKey { get; }
        public byte[] Payload { get; }
        public TickId SubmittedTick { get; }
    }

    /// <summary>一次命令状态迁移的审计事件。</summary>
    public sealed class CommandStatusEvent
    {
        public CommandStatusEvent(
            StableId eventId,
            StableId commandInstanceId,
            CommandStatus previousStatus,
            CommandStatus currentStatus,
            TickId tickId,
            string reasonCode)
        {
            SimulationNode.RequireId(eventId, nameof(eventId));
            SimulationNode.RequireId(commandInstanceId, nameof(commandInstanceId));
            EventId = eventId;
            CommandInstanceId = commandInstanceId;
            PreviousStatus = previousStatus;
            CurrentStatus = currentStatus;
            TickId = tickId;
            ReasonCode = reasonCode ?? string.Empty;
        }

        public StableId EventId { get; }
        public StableId CommandInstanceId { get; }
        public CommandStatus PreviousStatus { get; }
        public CommandStatus CurrentStatus { get; }
        public TickId TickId { get; }
        public string ReasonCode { get; }
    }

    /// <summary>命令在指定权限作用域中的资源预留记录。</summary>
    public sealed class ResourceReservation
    {
        public ResourceReservation(
            StableId reservationId,
            StableId commandInstanceId,
            StableId authorityScopeId,
            string resourceKey,
            long amount,
            bool committed)
        {
            SimulationNode.RequireId(reservationId, nameof(reservationId));
            SimulationNode.RequireId(commandInstanceId, nameof(commandInstanceId));
            SimulationNode.RequireId(authorityScopeId, nameof(authorityScopeId));
            if (string.IsNullOrWhiteSpace(resourceKey))
            {
                throw new ArgumentException("A reservation resource key is required.", nameof(resourceKey));
            }

            if (amount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            ReservationId = reservationId;
            CommandInstanceId = commandInstanceId;
            AuthorityScopeId = authorityScopeId;
            ResourceKey = resourceKey;
            Amount = amount;
            Committed = committed;
        }

        public StableId ReservationId { get; }
        public StableId CommandInstanceId { get; }
        public StableId AuthorityScopeId { get; }
        public string ResourceKey { get; }
        public long Amount { get; }
        public bool Committed { get; }
    }

    /// <summary>命令执行端返回的稳定结果。</summary>
    public sealed class CommandExecutionResult
    {
        public CommandExecutionResult(StableId commandInstanceId, CommandStatus status, string reasonCode)
        {
            SimulationNode.RequireId(commandInstanceId, nameof(commandInstanceId));
            CommandInstanceId = commandInstanceId;
            Status = status;
            ReasonCode = reasonCode ?? string.Empty;
        }

        public StableId CommandInstanceId { get; }
        public CommandStatus Status { get; }
        public string ReasonCode { get; }
    }

    /// <summary>事实提交后才能发布的领域事件信封。</summary>
    public sealed class EventEnvelope
    {
        public EventEnvelope(
            StableId eventId,
            StableId eventDefinitionId,
            StableId sourceNodeId,
            TickId committedTick,
            byte[] payload)
        {
            SimulationNode.RequireId(eventId, nameof(eventId));
            SimulationNode.RequireId(eventDefinitionId, nameof(eventDefinitionId));
            SimulationNode.RequireId(sourceNodeId, nameof(sourceNodeId));
            EventId = eventId;
            EventDefinitionId = eventDefinitionId;
            SourceNodeId = sourceNodeId;
            CommittedTick = committedTick;
            Payload = (byte[])(payload ?? Array.Empty<byte>()).Clone();
        }

        public StableId EventId { get; }
        public StableId EventDefinitionId { get; }
        public StableId SourceNodeId { get; }
        public TickId CommittedTick { get; }
        public byte[] Payload { get; }
    }

    /// <summary>玩家意图 DTO；意图必须先转成命令，不能直接修改权威状态。</summary>
    public sealed class PlayerIntent
    {
        public PlayerIntent(StableId actorId, StableId intentDefinitionId, StableId targetId, byte[] payload)
        {
            SimulationNode.RequireId(actorId, nameof(actorId));
            SimulationNode.RequireId(intentDefinitionId, nameof(intentDefinitionId));
            SimulationNode.RequireId(targetId, nameof(targetId));
            ActorId = actorId;
            IntentDefinitionId = intentDefinitionId;
            TargetId = targetId;
            Payload = (byte[])(payload ?? Array.Empty<byte>()).Clone();
        }

        public StableId ActorId { get; }
        public StableId IntentDefinitionId { get; }
        public StableId TargetId { get; }
        public byte[] Payload { get; }
    }

    /// <summary>命令当前状态及全部状态迁移历史。</summary>
    public sealed class CommandRecord
    {
        private readonly List<CommandStatusEvent> _statusEvents;

        public CommandRecord(CommandEnvelope envelope, CommandStatus status = CommandStatus.Drafted, IEnumerable<CommandStatusEvent> statusEvents = null)
        {
            Envelope = envelope ?? throw new ArgumentNullException(nameof(envelope));
            Status = status;
            _statusEvents = (statusEvents ?? Array.Empty<CommandStatusEvent>()).ToList();
        }

        public CommandEnvelope Envelope { get; }
        public CommandStatus Status { get; private set; }
        public IReadOnlyList<CommandStatusEvent> StatusEvents => new ReadOnlyCollection<CommandStatusEvent>(_statusEvents.ToList());

        public CommandRecord Clone()
        {
            return new CommandRecord(Envelope, Status, _statusEvents);
        }

        /// <summary>执行合法状态迁移并附加确定性事件 ID。</summary>
        public CommandStatusEvent TransitionTo(CommandStatus next, TickId tickId, string reasonCode = null)
        {
            if (!IsAllowed(Status, next))
            {
                throw new InvalidOperationException($"Command '{Envelope.CommandInstanceId}' cannot transition from {Status} to {next}.");
            }

            var previous = Status;
            Status = next;
            var eventId = new StableId($"command-status.{Envelope.CommandInstanceId.Value}.{_statusEvents.Count + 1:D4}");
            var statusEvent = new CommandStatusEvent(eventId, Envelope.CommandInstanceId, previous, next, tickId, reasonCode);
            _statusEvents.Add(statusEvent);
            return statusEvent;
        }

        private static bool IsAllowed(CommandStatus current, CommandStatus next)
        {
            if (next == CommandStatus.Cancelled || next == CommandStatus.Expired || next == CommandStatus.Superseded || next == CommandStatus.Suspended)
            {
                return current != CommandStatus.Settled && current != CommandStatus.Rejected && current != CommandStatus.Cancelled;
            }

            switch (current)
            {
                case CommandStatus.Drafted: return next == CommandStatus.Submitted;
                case CommandStatus.Submitted: return next == CommandStatus.Validating;
                case CommandStatus.Validating: return next == CommandStatus.Accepted || next == CommandStatus.Rejected;
                case CommandStatus.Accepted: return next == CommandStatus.Reserving;
                case CommandStatus.Reserving: return next == CommandStatus.Reserved || next == CommandStatus.ReservationFailed;
                case CommandStatus.Reserved: return next == CommandStatus.Dispatched;
                case CommandStatus.Dispatched: return next == CommandStatus.Executing;
                case CommandStatus.Executing:
                    return next == CommandStatus.Completed || next == CommandStatus.PartiallyCompleted || next == CommandStatus.Failed;
                case CommandStatus.Completed:
                case CommandStatus.PartiallyCompleted:
                case CommandStatus.Failed:
                case CommandStatus.ReservationFailed:
                    return next == CommandStatus.Settled;
                case CommandStatus.Suspended: return next == CommandStatus.Submitted || next == CommandStatus.Cancelled;
                default: return false;
            }
        }
    }
}
