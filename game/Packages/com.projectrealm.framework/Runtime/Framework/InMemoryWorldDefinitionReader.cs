using ProjectRealm.Foundation;
using System;
using System.Collections.Generic;
using ProjectRealm.World;
using ProjectRealm.Framework;

namespace ProjectRealm.Framework.Testing
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
