using System;
using System.Collections.Generic;
using System.Linq;
using ProjectRealm.Foundation;
using ProjectRealm.Framework;

namespace ProjectRealm.SystemServer
{
    /// <summary>协调存档列表、读取和安全闭合 Tick 写入。</summary>
    internal sealed class SaveService : RealmServiceBase, ISaveManagerGateway
    {
        private readonly WorldService _world;
        private readonly RealmApplicationStateMachine _applicationState;
        private readonly RealmEventStream _events;

        public SaveService(WorldService world, RealmApplicationStateMachine applicationState, RealmEventStream events)
            : base("SaveService")
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _applicationState = applicationState ?? throw new ArgumentNullException(nameof(applicationState));
            _events = events ?? throw new ArgumentNullException(nameof(events));
        }

        public RealmResult<IReadOnlyList<SaveSlotSnapshot>> ListSlots()
        {
            try
            {
                IReadOnlyList<SaveSlotSnapshot> slots = _world.SaveStore.ListSaveIds()
                    .Select(id => new SaveSlotSnapshot(id.Value))
                    .ToList();
                return RealmResult<IReadOnlyList<SaveSlotSnapshot>>.Success(slots);
            }
            catch (Exception exception)
            {
                var error = RealmErrorMapper.FromException(exception);
                return RealmResult<IReadOnlyList<SaveSlotSnapshot>>.Failure(error.Code, error.Message, error.Kind);
            }
        }

        public RealmResult<WorldSessionSnapshot> Load(string saveId)
        {
            return _world.Load(saveId);
        }

        public RealmResult Save()
        {
            if (!_world.HasActiveWorld)
            {
                return RealmResult.Failure("no_active_world", "No world is currently open.", RealmErrorKind.Conflict);
            }

            var previous = _applicationState.State;
            if (previous != RealmApplicationState.Running && previous != RealmApplicationState.Paused)
            {
                return RealmResult.Failure("invalid_application_state", "The world cannot be saved in the current state.", RealmErrorKind.Conflict);
            }

            _applicationState.Transition(RealmApplicationState.Saving, "save_world");
            try
            {
                var runtime = _world.RequireRuntime();
                runtime.Save();
                _applicationState.Transition(previous, "save_completed");
                _events.Publish(new RealmSaveCompletedEvent(runtime.SaveId.Value, runtime.CurrentStateHash.Sha256));
                return RealmResult.Success();
            }
            catch (Exception exception)
            {
                var error = RealmErrorMapper.FromException(exception);
                _applicationState.Transition(previous, error.Code);
                return RealmResult.Failure(error.Code, error.Message, error.Kind);
            }
        }
    }
}
