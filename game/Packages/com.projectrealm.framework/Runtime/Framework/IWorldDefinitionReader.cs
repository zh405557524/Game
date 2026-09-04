using ProjectRealm.Foundation;
using ProjectRealm.World;

namespace ProjectRealm.Framework
{
    public interface IWorldDefinitionReader
    {
        bool ContainsWorld(StableId worldId);
    }
}
