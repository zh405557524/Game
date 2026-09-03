using System;
using System.Collections.Generic;
using System.Linq;
using ProjectRealm.Domain;
using ProjectRealm.Ports;

namespace ProjectRealm.Infrastructure
{
    public sealed class InMemoryWorldDefinitionStore : IWorldDefinitionStore
    {
        private readonly Dictionary<StableId, WorldDefinition> _definitions;

        public InMemoryWorldDefinitionStore(IEnumerable<WorldDefinition> definitions)
        {
            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            _definitions = definitions.ToDictionary(definition => definition.WorldId);
        }

        public bool ContainsWorld(StableId worldId) => _definitions.ContainsKey(worldId);

        public WorldDefinition LoadWorld(StableId worldId)
        {
            if (!_definitions.TryGetValue(worldId, out var definition))
            {
                throw new KeyNotFoundException($"World definition '{worldId}' is unavailable.");
            }

            return definition;
        }
    }

    public sealed class InMemorySaveGameStore : ISaveGameStore
    {
        private readonly Dictionary<StableId, WorldSaveData> _saves = new Dictionary<StableId, WorldSaveData>();

        public bool Exists(StableId saveId) => _saves.ContainsKey(saveId);

        public WorldSaveData Load(StableId saveId)
        {
            if (!_saves.TryGetValue(saveId, out var save))
            {
                throw new KeyNotFoundException($"Save '{saveId}' does not exist.");
            }

            return save;
        }

        public void Save(WorldSaveData saveData)
        {
            if (saveData == null)
            {
                throw new ArgumentNullException(nameof(saveData));
            }

            _saves[saveData.Manifest.SaveId] = saveData;
        }

        public void BackupBeforeMigration(StableId saveId, string migrationId)
        {
            if (!Exists(saveId))
            {
                throw new KeyNotFoundException($"Save '{saveId}' does not exist.");
            }

            if (string.IsNullOrWhiteSpace(migrationId))
            {
                throw new ArgumentException("A migration ID is required.", nameof(migrationId));
            }
        }
    }

    public sealed class EmptyStateCodec : IModuleStateCodec
    {
        public string CodecId => "empty-state-v1";

        public byte[] Encode(object state)
        {
            if (state != null)
            {
                throw new ArgumentException("The empty-state codec only accepts null state.", nameof(state));
            }

            return Array.Empty<byte>();
        }

        public object Decode(byte[] payload)
        {
            if (payload == null || payload.Length != 0)
            {
                throw new ArgumentException("The empty-state codec requires an empty payload.", nameof(payload));
            }

            return null;
        }
    }
}
