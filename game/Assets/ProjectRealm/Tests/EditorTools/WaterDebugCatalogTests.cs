using System.Linq;
using NUnit.Framework;
using ProjectRealm.EditorTools;
using UnityEngine;

namespace ProjectRealm.Tests.EditorTools
{
    public sealed class WaterDebugCatalogTests
    {
        [Test] public void SixWaterCasesHaveSeparateInputAndOutputFolders()
        {
            var catalog = MapDebugWorkbench.LoadCatalog();
            // Six water kinds can have additional versioned art-comparison cases in the same layer.
            var cases = catalog.cases.Where(x => x.layer == 1 && x.id.StartsWith("water/", System.StringComparison.Ordinal)).ToArray();
            Assert.That(cases.Length, Is.EqualTo(6));
            CollectionAssert.AreEquivalent(WaterDebugCases.Names, cases.Select(x => x.displayName));
            Assert.That(cases.Select(x => x.testDataPath).Distinct().Count(), Is.EqualTo(6));
            Assert.That(cases.Select(x => x.generatedPath).Distinct().Count(), Is.EqualTo(6));
            Assert.That(cases.Single(x => x.id == "water/01_River").scenePath, Is.EqualTo(WaterDebugCases.RiverScene));
        }

        [Test] public void CatalogUpgradeIsIdempotentAndKeepsExistingReviewNotes()
        {
            var catalog = ScriptableObject.CreateInstance<MapDebugCatalog>();
            catalog.cases.Add(new MapDebugCase { id = "02_Water", layer = 1, findings = "User review note", state = DebugReviewState.InProgress });
            try
            {
                Assert.That(WaterDebugCases.Ensure(catalog), Is.True);
                Assert.That(WaterDebugCases.Ensure(catalog), Is.False);
                Assert.That(catalog.cases.Count, Is.EqualTo(6));
                var river = catalog.cases.Single(x => x.id == "water/01_River");
                Assert.That(river.findings, Is.EqualTo("User review note"));
                Assert.That(river.state, Is.EqualTo(DebugReviewState.InProgress));
            }
            finally { Object.DestroyImmediate(catalog); }
        }
    }
}
