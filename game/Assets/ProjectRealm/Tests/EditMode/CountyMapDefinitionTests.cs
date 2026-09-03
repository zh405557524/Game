using System.Collections.Generic;
using NUnit.Framework;
using ProjectRealm.Presentation.Map;
using UnityEngine;

namespace ProjectRealm.Tests.Integration
{
    public sealed class CountyMapDefinitionTests
    {
        private CountyMapDefinition definition;

        [SetUp]
        public void SetUp()
        {
            definition = ScriptableObject.CreateInstance<CountyMapDefinition>();
            definition.ResetToPrototype();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(definition);
        }

        [Test]
        public void PrototypeContainsTheRequiredFirstSliceLayers()
        {
            Assert.That(definition.HasRequiredPrototypeLayers(out var reason), Is.True, reason);
        }

        [Test]
        public void PrototypeUsesExactlyTenUniquelyIdentifiedSettlements()
        {
            var stableIds = new HashSet<string>();

            foreach (var settlement in definition.Settlements)
            {
                Assert.That(settlement.StableId, Is.Not.Null.And.Not.Empty);
                Assert.That(stableIds.Add(settlement.StableId), Is.True, $"Duplicate StableId: {settlement.StableId}");
            }

            Assert.That(stableIds.Count, Is.EqualTo(10));
        }

        [Test]
        public void PrototypeIncludesVillageTownAndCountySeatSemanticLevels()
        {
            var kinds = new HashSet<SettlementKind>();
            foreach (var settlement in definition.Settlements)
            {
                kinds.Add(settlement.Kind);
            }

            Assert.That(kinds, Is.EquivalentTo(new[]
            {
                SettlementKind.Village,
                SettlementKind.Town,
                SettlementKind.CountySeat
            }));
        }
    }
}
