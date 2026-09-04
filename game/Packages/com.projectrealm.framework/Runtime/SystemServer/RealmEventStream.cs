using System;
using System.Collections.Generic;
using ProjectRealm.Framework;

namespace ProjectRealm.SystemServer
{
    /// <summary>按订阅顺序同步发布已提交事件的进程内事件流。</summary>
    internal sealed class RealmEventStream : IRealmEventStream
    {
        private readonly Dictionary<Type, List<Delegate>> _listeners = new Dictionary<Type, List<Delegate>>();

        public IDisposable Subscribe<TEvent>(Action<TEvent> listener)
        {
            if (listener == null)
            {
                throw new ArgumentNullException(nameof(listener));
            }

            var type = typeof(TEvent);
            if (!_listeners.TryGetValue(type, out var listeners))
            {
                listeners = new List<Delegate>();
                _listeners.Add(type, listeners);
            }

            listeners.Add(listener);
            return new Subscription(() => listeners.Remove(listener));
        }

        public void Publish<TEvent>(TEvent item)
        {
            if (!_listeners.TryGetValue(typeof(TEvent), out var listeners))
            {
                return;
            }

            foreach (var listener in listeners.ToArray())
            {
                ((Action<TEvent>)listener)(item);
            }
        }

        private sealed class Subscription : IDisposable
        {
            private Action _dispose;

            public Subscription(Action dispose)
            {
                _dispose = dispose;
            }

            public void Dispose()
            {
                var dispose = _dispose;
                _dispose = null;
                dispose?.Invoke();
            }
        }
    }
}
