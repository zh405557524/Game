using System;
using System.Collections.Generic;
using NUnit.Framework;
using ProjectRealm.Framework;
using ProjectRealm.SystemServer;

namespace ProjectRealm.Tests.Unit
{
    public sealed class RealmSystemServerLifecycleTests
    {
        [Test]
        public void ApplicationStateMachineRejectsSkippedTransitions()
        {
            var machine = new RealmApplicationStateMachine(new RealmEventStream());

            Assert.Throws<InvalidOperationException>(() =>
                machine.Transition(RealmApplicationState.Running, "skip_boot"));

            machine.Transition(RealmApplicationState.Booting);
            machine.Transition(RealmApplicationState.MainMenu);
            Assert.Throws<InvalidOperationException>(() =>
                machine.Transition(RealmApplicationState.Running, "skip_load"));
        }

        [Test]
        public void StartupFailureStopsAlreadyStartedServicesInReverseOrder()
        {
            var calls = new List<string>();
            var registry = new RealmServiceRegistry(new IRealmService[]
            {
                new RecordingService("world", calls),
                new RecordingService("simulation", calls),
                new RecordingService("save", calls, failOnStart: true)
            });

            Assert.Throws<InvalidOperationException>(() => registry.StartAll());

            Assert.That(calls, Is.EqualTo(new[]
            {
                "start:world",
                "start:simulation",
                "start:save",
                "stop:simulation",
                "stop:world"
            }));
        }

        [Test]
        public void NormalShutdownStopsServicesInReverseDependencyOrder()
        {
            var calls = new List<string>();
            var registry = new RealmServiceRegistry(new IRealmService[]
            {
                new RecordingService("world", calls),
                new RecordingService("simulation", calls),
                new RecordingService("save", calls)
            });

            registry.StartAll();
            calls.Clear();
            registry.StopAll();

            Assert.That(calls, Is.EqualTo(new[] { "stop:save", "stop:simulation", "stop:world" }));
        }

        private sealed class RecordingService : IRealmService
        {
            private readonly IList<string> _calls;
            private readonly bool _failOnStart;

            public RecordingService(string serviceName, IList<string> calls, bool failOnStart = false)
            {
                ServiceName = serviceName;
                _calls = calls;
                _failOnStart = failOnStart;
                State = RealmServiceState.Created;
            }

            public string ServiceName { get; }
            public RealmServiceState State { get; private set; }

            public void Start()
            {
                _calls.Add("start:" + ServiceName);
                if (_failOnStart)
                {
                    State = RealmServiceState.Faulted;
                    throw new InvalidOperationException("forced_start_failure");
                }

                State = RealmServiceState.Running;
            }

            public void Pause() => State = RealmServiceState.Paused;
            public void Resume() => State = RealmServiceState.Running;

            public void Stop()
            {
                _calls.Add("stop:" + ServiceName);
                State = RealmServiceState.Stopped;
            }
        }
    }
}
