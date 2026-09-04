using System;
using System.Collections.Generic;
using System.Linq;
using ProjectRealm.Framework;

namespace ProjectRealm.SystemServer
{
    /// <summary>只供 System Server 使用的生命周期注册表，不承担运行时 Service Locator 职责。</summary>
    internal sealed class RealmServiceRegistry
    {
        private readonly IReadOnlyList<IRealmService> _services;

        public RealmServiceRegistry(IEnumerable<IRealmService> services)
        {
            var list = (services ?? throw new ArgumentNullException(nameof(services))).ToList();
            if (list.Any(service => service == null) || list.Select(service => service.ServiceName).Distinct(StringComparer.Ordinal).Count() != list.Count)
            {
                throw new InvalidOperationException("Realm services must be non-null and have unique names.");
            }

            _services = list;
        }

        public void StartAll()
        {
            var started = new List<IRealmService>();
            try
            {
                foreach (var service in _services)
                {
                    service.Start();
                    started.Add(service);
                }
            }
            catch
            {
                for (var index = started.Count - 1; index >= 0; index--)
                {
                    try { started[index].Stop(); }
                    catch { /* Preserve the original startup exception. */ }
                }

                throw;
            }
        }

        public void StopAll()
        {
            Exception first = null;
            for (var index = _services.Count - 1; index >= 0; index--)
            {
                try { _services[index].Stop(); }
                catch (Exception exception) { first = first ?? exception; }
            }

            if (first != null)
            {
                throw first;
            }
        }
    }
}
