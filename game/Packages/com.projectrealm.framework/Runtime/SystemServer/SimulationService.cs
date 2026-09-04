using System;
using System.Linq;
using ProjectRealm.Foundation;
using ProjectRealm.Framework;
using ProjectRealm.World;

namespace ProjectRealm.SystemServer
{
    /// <summary>把 Manager 的显式推进请求映射到 WorldRuntime 的闭合 Tick。</summary>
    internal sealed class SimulationService : RealmServiceBase, ISimulationManagerGateway
    {
        private readonly WorldService _world;
        private readonly RealmApplicationStateMachine _applicationState;
        private readonly RealmEventStream _events;

        public SimulationService(WorldService world, RealmApplicationStateMachine applicationState, RealmEventStream events)
            : base("SimulationService")
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _applicationState = applicationState ?? throw new ArgumentNullException(nameof(applicationState));
            _events = events ?? throw new ArgumentNullException(nameof(events));
        }

        public RealmResult<SimulationStepSnapshot> Advance(RealmAdvanceUnit unit)
        {
            if (_applicationState.State != RealmApplicationState.Running)
            {
                return RealmResult<SimulationStepSnapshot>.Failure(
                    "simulation_not_running",
                    "Simulation can advance only while a world is running and not paused.",
                    RealmErrorKind.Conflict);
            }

            try
            {
                var result = _world.RequireRuntime().Advance(new AdvanceRequest(Map(unit)));
                var quality = result.ModuleResults.Any(item => item.DataQuality == DataQuality.Unavailable)
                    ? DataQuality.Unavailable.ToString()
                    : DataQuality.Unknown.ToString();
                var snapshot = new SimulationStepSnapshot(
                    unit,
                    result.Committed,
                    result.TickId.Value,
                    result.StateHash.Sha256,
                    result.FailureReason,
                    result.Stages.Count,
                    result.ModuleResults.Count,
                    quality);
                if (result.Committed)
                {
                    _events.Publish(new RealmSimulationAdvancedEvent(snapshot));
                }

                return RealmResult<SimulationStepSnapshot>.Success(snapshot);
            }
            catch (Exception exception)
            {
                return Failure<SimulationStepSnapshot>(exception);
            }
        }

        public RealmResult<CommandTicketSnapshot> Submit(RealmCommandRequest request)
        {
            if (request == null)
            {
                return RealmResult<CommandTicketSnapshot>.Failure("invalid_request", "A command request is required.", RealmErrorKind.Validation);
            }

            if (_applicationState.State != RealmApplicationState.Running)
            {
                return RealmResult<CommandTicketSnapshot>.Failure("simulation_not_running", "Commands require a running world.", RealmErrorKind.Conflict);
            }

            try
            {
                var runtime = _world.RequireRuntime();
                var record = runtime.SubmitCommand(new CommandEnvelope(
                    new StableId(request.CommandInstanceId),
                    new StableId(request.CommandDefinitionId),
                    new StableId(request.ActorId),
                    new StableId(request.TargetId),
                    new StableId(request.AuthorityScopeId),
                    request.IdempotencyKey,
                    request.Payload,
                    new TickId(runtime.Clock.TickSequence)));
                return RealmResult<CommandTicketSnapshot>.Success(new CommandTicketSnapshot(
                    record.Envelope.CommandInstanceId.Value,
                    record.Status.ToString(),
                    record.StatusEvents.LastOrDefault()?.ReasonCode));
            }
            catch (Exception exception)
            {
                return Failure<CommandTicketSnapshot>(exception);
            }
        }

        RealmResult ISimulationManagerGateway.Pause()
        {
            if (_applicationState.State != RealmApplicationState.Running)
            {
                return RealmResult.Failure("invalid_application_state", "Only a running world can be paused.", RealmErrorKind.Conflict);
            }

            _applicationState.Transition(RealmApplicationState.Paused, "pause_simulation");
            return RealmResult.Success();
        }

        RealmResult ISimulationManagerGateway.Resume()
        {
            if (_applicationState.State != RealmApplicationState.Paused)
            {
                return RealmResult.Failure("invalid_application_state", "Only a paused world can be resumed.", RealmErrorKind.Conflict);
            }

            _applicationState.Transition(RealmApplicationState.Running, "resume_simulation");
            return RealmResult.Success();
        }

        private RealmResult<T> Failure<T>(Exception exception)
        {
            var error = RealmErrorMapper.FromException(exception);
            if (error.Kind == RealmErrorKind.Fatal)
            {
                _applicationState.Fault(error.Code, error.Message);
            }

            return RealmResult<T>.Failure(error.Code, error.Message, error.Kind);
        }

        private static AdvanceUnit Map(RealmAdvanceUnit unit)
        {
            switch (unit)
            {
                case RealmAdvanceUnit.Day: return AdvanceUnit.Day;
                case RealmAdvanceUnit.Month: return AdvanceUnit.Month;
                case RealmAdvanceUnit.Season: return AdvanceUnit.Season;
                case RealmAdvanceUnit.Year: return AdvanceUnit.Year;
                default: throw new ArgumentOutOfRangeException(nameof(unit));
            }
        }
    }
}
