using System;
using ProjectRealm.Framework;

namespace ProjectRealm.SystemServer
{
    /// <summary>集中校验应用状态转换，避免场景和按钮各自维护一份生命周期。</summary>
    internal sealed class RealmApplicationStateMachine
    {
        private readonly RealmEventStream _events;

        public RealmApplicationStateMachine(RealmEventStream events)
        {
            _events = events ?? throw new ArgumentNullException(nameof(events));
            State = RealmApplicationState.Cold;
        }

        public RealmApplicationState State { get; private set; }

        public void Transition(RealmApplicationState next, string reason = null)
        {
            if (!IsAllowed(State, next))
            {
                throw new InvalidOperationException($"Realm application cannot transition from {State} to {next}.");
            }

            var previous = State;
            State = next;
            _events.Publish(new RealmApplicationStateChangedEvent(previous, next, reason));
        }

        public void Fault(string code, string message)
        {
            if (State != RealmApplicationState.Faulted)
            {
                var previous = State;
                State = RealmApplicationState.Faulted;
                _events.Publish(new RealmApplicationStateChangedEvent(previous, State, code));
            }

            _events.Publish(new RealmFaultedEvent(code, message));
        }

        private static bool IsAllowed(RealmApplicationState current, RealmApplicationState next)
        {
            switch (current)
            {
                case RealmApplicationState.Cold:
                    return next == RealmApplicationState.Booting;
                case RealmApplicationState.Booting:
                    return next == RealmApplicationState.MainMenu || next == RealmApplicationState.ShuttingDown;
                case RealmApplicationState.MainMenu:
                    return next == RealmApplicationState.LoadingWorld || next == RealmApplicationState.ShuttingDown;
                case RealmApplicationState.LoadingWorld:
                    return next == RealmApplicationState.Running || next == RealmApplicationState.MainMenu;
                case RealmApplicationState.Running:
                    return next == RealmApplicationState.Paused || next == RealmApplicationState.Saving ||
                           next == RealmApplicationState.UnloadingWorld || next == RealmApplicationState.ShuttingDown;
                case RealmApplicationState.Paused:
                    return next == RealmApplicationState.Running || next == RealmApplicationState.Saving ||
                           next == RealmApplicationState.UnloadingWorld || next == RealmApplicationState.ShuttingDown;
                case RealmApplicationState.Saving:
                    return next == RealmApplicationState.Running || next == RealmApplicationState.Paused;
                case RealmApplicationState.UnloadingWorld:
                    return next == RealmApplicationState.MainMenu;
                case RealmApplicationState.Faulted:
                    return next == RealmApplicationState.ShuttingDown;
                case RealmApplicationState.ShuttingDown:
                    return false;
                default:
                    return false;
            }
        }
    }
}
