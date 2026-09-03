using System;
using System.Collections.Generic;
using ProjectRealm.Domain;
using ProjectRealm.Ports;

namespace ProjectRealm.Infrastructure
{
    public sealed class InMemoryWorldDefinitionReader : IWorldDefinitionReader
    {
        private readonly HashSet<StableId> _worldIds;

        public InMemoryWorldDefinitionReader(IEnumerable<StableId> worldIds)
        {
            if (worldIds == null)
            {
                throw new ArgumentNullException(nameof(worldIds));
            }

            _worldIds = new HashSet<StableId>(worldIds);
        }

        public bool ContainsWorld(StableId worldId)
        {
            return _worldIds.Contains(worldId);
        }
    }
}
