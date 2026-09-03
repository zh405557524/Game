using System;
using System.Linq;
using NUnit.Framework;
using ProjectRealm.Application;
using ProjectRealm.Domain;
using ProjectRealm.Ports;

namespace ProjectRealm.Tests.Unit
{
    public sealed class SimulationCoreTests
    {
        [Test]
        public void StartNewWorldUsesStableIdentityAndSeed()
        {
            var worldId = new StableId("MING1628");
            var bootstrapper = new WorldBootstrapper(new SingleWorldDefinitionReader(worldId));

            var session = bootstrapper.StartNewWorld(worldId, new WorldSeed(1628));

            Assert.That(session.WorldId, Is.EqualTo(worldId));
            Assert.That(session.WorldSeed, Is.EqualTo(new WorldSeed(1628)));
            Assert.That(session.ElapsedDays, Is.Zero);
        }

        [Test]
        public void StableIdRejectsWhitespace()
        {
            Assert.Throws<ArgumentException>(() => new StableId("MING 1628"));
        }

        [TestCase(typeof(StableId))]
        [TestCase(typeof(IWorldDefinitionReader))]
        [TestCase(typeof(WorldBootstrapper))]
        public void SimulationCoreAssemblyDoesNotReferenceUnityEngine(Type markerType)
        {
            var referencesUnityEngine = markerType.Assembly
                .GetReferencedAssemblies()
                .Any(reference => reference.Name.StartsWith("UnityEngine", StringComparison.Ordinal));

            Assert.That(referencesUnityEngine, Is.False, $"{markerType.Assembly.GetName().Name} must remain independent of UnityEngine.");
        }

        private sealed class SingleWorldDefinitionReader : IWorldDefinitionReader
        {
            private readonly StableId _worldId;

            public SingleWorldDefinitionReader(StableId worldId)
            {
                _worldId = worldId;
            }

            public bool ContainsWorld(StableId worldId)
            {
                return _worldId.Equals(worldId);
            }
        }
    }
}
