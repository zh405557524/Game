using ProjectRealm.Domain;

namespace ProjectRealm.Ports
{
    public interface IWorldDefinitionReader
    {
        bool ContainsWorld(StableId worldId);
    }
}
