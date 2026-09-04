using ProjectRealm.Foundation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ProjectRealm.World;
using ProjectRealm.Framework;

namespace ProjectRealm.SystemServer
{
    /// <summary>
    /// 管理命令状态机、作用域内幂等键和资源预留。实例会在 Tick 开始时克隆，
    /// 因此命令转换与世界状态共享同一个提交/回滚边界。
    /// </summary>
    internal sealed class CommandProcessor
    {
        private readonly Dictionary<StableId, CommandRecord> _commands;
        private readonly Dictionary<string, StableId> _idempotencyKeys;
        private readonly List<ResourceReservation> _reservations;
        private readonly ISimulationDiagnosticsSink _diagnostics;
        private readonly bool _scaffoldOnly;

        public CommandProcessor(
            bool scaffoldOnly,
            ISimulationDiagnosticsSink diagnostics = null,
            IEnumerable<CommandRecord> commands = null,
            IEnumerable<ResourceReservation> reservations = null)
        {
            _scaffoldOnly = scaffoldOnly;
            _diagnostics = diagnostics ?? new NullSimulationDiagnosticsSink();
            _commands = new Dictionary<StableId, CommandRecord>();
            _idempotencyKeys = new Dictionary<string, StableId>(StringComparer.Ordinal);
            _reservations = (reservations ?? Array.Empty<ResourceReservation>()).ToList();

            foreach (var command in commands ?? Array.Empty<CommandRecord>())
            {
                RegisterExisting(command);
            }
        }

        public IReadOnlyList<CommandRecord> Commands => new ReadOnlyCollection<CommandRecord>(_commands.Values
            .OrderBy(command => command.Envelope.CommandInstanceId.Value, StringComparer.Ordinal).ToList());

        public IReadOnlyList<ResourceReservation> Reservations => new ReadOnlyCollection<ResourceReservation>(_reservations
            .OrderBy(reservation => reservation.ReservationId.Value, StringComparer.Ordinal).ToList());

        /// <summary>提交新命令；同一权限作用域和幂等键的重复请求稳定返回原命令。</summary>
        public CommandRecord Submit(CommandEnvelope envelope, TickId tickId)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            // 幂等键必须带权限作用域，避免两个互不相关的辖区互相吞掉命令。
            var key = ScopeIdempotencyKey(envelope.AuthorityScopeId, envelope.IdempotencyKey);
            if (_idempotencyKeys.TryGetValue(key, out var existingId))
            {
                return _commands[existingId];
            }

            if (_commands.ContainsKey(envelope.CommandInstanceId))
            {
                throw new InvalidOperationException($"Command ID '{envelope.CommandInstanceId}' already exists.");
            }

            var command = new CommandRecord(envelope);
            _commands.Add(envelope.CommandInstanceId, command);
            _idempotencyKeys.Add(key, envelope.CommandInstanceId);
            Transition(command, CommandStatus.Submitted, tickId);
            return command;
        }

        /// <summary>执行 S80 校验；当前脚手架能力统一以 implementation_unavailable 拒绝。</summary>
        public int ValidatePending(TickId tickId)
        {
            var count = 0;
            foreach (var command in OrderedWithStatus(CommandStatus.Submitted))
            {
                Transition(command, CommandStatus.Validating, tickId);
                if (_scaffoldOnly)
                {
                    Transition(command, CommandStatus.Rejected, tickId, ScaffoldModuleExecutor.UnavailableReason);
                    count++;
                    continue;
                }

                Transition(command, CommandStatus.Accepted, tickId);
                count++;
            }

            return count;
        }

        /// <summary>执行 S90 资源预留，并记录可持久化的预留凭据。</summary>
        public int ReserveAccepted(TickId tickId)
        {
            var count = 0;
            foreach (var command in OrderedWithStatus(CommandStatus.Accepted))
            {
                Transition(command, CommandStatus.Reserving, tickId);
                // resource.none/0 只用于验证状态机协议，不代表真实资源或库存为零。
                var reservation = new ResourceReservation(
                    new StableId("reservation." + command.Envelope.CommandInstanceId.Value),
                    command.Envelope.CommandInstanceId,
                    command.Envelope.AuthorityScopeId,
                    "resource.none",
                    0,
                    true);
                _reservations.Add(reservation);
                Transition(command, CommandStatus.Reserved, tickId);
                count++;
            }

            return count;
        }

        /// <summary>为新 Tick 创建事务副本，保留既有命令、状态事件和预留。</summary>
        public CommandProcessor Clone()
        {
            return new CommandProcessor(
                _scaffoldOnly,
                _diagnostics,
                Commands.Select(command => command.Clone()),
                Reservations);
        }

        /// <summary>执行 S100，把已预留命令送入执行态。</summary>
        public int DispatchReserved(TickId tickId)
        {
            var commands = OrderedWithStatus(CommandStatus.Reserved);
            foreach (var command in commands)
            {
                Transition(command, CommandStatus.Dispatched, tickId);
            }

            return commands.Count;
        }

        /// <summary>执行 S110 的同步立即命令路径；长期行动将在后续版本扩展。</summary>
        public int ExecuteDispatched(TickId tickId)
        {
            var commands = OrderedWithStatus(CommandStatus.Dispatched);
            foreach (var command in commands)
            {
                Transition(command, CommandStatus.Executing, tickId);
                Transition(command, CommandStatus.Completed, tickId);
                Transition(command, CommandStatus.Settled, tickId);
            }

            return commands.Count;
        }

        private List<CommandRecord> OrderedWithStatus(CommandStatus status)
        {
            return _commands.Values.Where(command => command.Status == status)
                .OrderBy(command => command.Envelope.CommandInstanceId.Value, StringComparer.Ordinal)
                .ToList();
        }

        private void RegisterExisting(CommandRecord command)
        {
            if (command == null || _commands.ContainsKey(command.Envelope.CommandInstanceId))
            {
                throw new InvalidOperationException("Persisted commands must be non-null and unique.");
            }

            var key = ScopeIdempotencyKey(command.Envelope.AuthorityScopeId, command.Envelope.IdempotencyKey);
            if (_idempotencyKeys.ContainsKey(key))
            {
                throw new InvalidOperationException($"Duplicate persisted idempotency key '{key}'.");
            }

            _commands.Add(command.Envelope.CommandInstanceId, command);
            _idempotencyKeys.Add(key, command.Envelope.CommandInstanceId);
        }

        private void Transition(CommandRecord command, CommandStatus next, TickId tickId, string reasonCode = null)
        {
            _diagnostics.RecordCommandStatus(command.TransitionTo(next, tickId, reasonCode));
        }

        private static string ScopeIdempotencyKey(StableId scopeId, string idempotencyKey)
        {
            return scopeId.Value + "\u001f" + idempotencyKey;
        }
    }
}
