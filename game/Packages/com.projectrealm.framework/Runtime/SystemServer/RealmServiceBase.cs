using System;
using ProjectRealm.Framework;

namespace ProjectRealm.SystemServer
{
    internal abstract class RealmServiceBase : IRealmService
    {
        protected RealmServiceBase(string serviceName)
        {
            ServiceName = serviceName ?? throw new ArgumentNullException(nameof(serviceName));
            State = RealmServiceState.Created;
        }

        public string ServiceName { get; }
        public RealmServiceState State { get; private set; }

        public void Start()
        {
            RequireState(RealmServiceState.Created, RealmServiceState.Stopped);
            State = RealmServiceState.Starting;
            try
            {
                OnStart();
                State = RealmServiceState.Running;
            }
            catch
            {
                State = RealmServiceState.Faulted;
                throw;
            }
        }

        public void Pause()
        {
            RequireState(RealmServiceState.Running);
            OnPause();
            State = RealmServiceState.Paused;
        }

        public void Resume()
        {
            RequireState(RealmServiceState.Paused);
            OnResume();
            State = RealmServiceState.Running;
        }

        public void Stop()
        {
            if (State == RealmServiceState.Stopped || State == RealmServiceState.Created)
            {
                State = RealmServiceState.Stopped;
                return;
            }

            State = RealmServiceState.Stopping;
            try
            {
                OnStop();
                State = RealmServiceState.Stopped;
            }
            catch
            {
                State = RealmServiceState.Faulted;
                throw;
            }
        }

        protected virtual void OnStart() { }
        protected virtual void OnPause() { }
        protected virtual void OnResume() { }
        protected virtual void OnStop() { }

        protected void RequireRunning()
        {
            RequireState(RealmServiceState.Running, RealmServiceState.Paused);
        }

        private void RequireState(params RealmServiceState[] allowed)
        {
            foreach (var item in allowed)
            {
                if (State == item)
                {
                    return;
                }
            }

            throw new InvalidOperationException($"Service '{ServiceName}' cannot operate while in state '{State}'.");
        }
    }
}
