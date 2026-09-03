using NUnit.Framework;
using ProjectRealm.Application;
using ProjectRealm.Domain;
using ProjectRealm.Infrastructure;

namespace ProjectRealm.Tests.Integration
{
    public sealed class WorldBootstrapIntegrationTests
    {
        [Test]
        public void ApplicationCanLoadAWorldThroughAnInfrastructurePort()
        {
            var worldId = new StableId("MING1628");
            var definitions = new InMemoryWorldDefinitionReader(new[] { worldId });
            var bootstrapper = new WorldBootstrapper(definitions);

            var session = bootstrapper.StartNewWorld(worldId, new WorldSeed(1628));

            Assert.That(session.WorldId, Is.EqualTo(worldId));
        }
    }
}
