using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ProjectRealm.Bootstrap;
using ProjectRealm.Framework;
using ProjectRealm.Presentation;
using ProjectRealm.UnityPresentation.Screens;

namespace ProjectRealm.Tests.Integration
{
    public sealed class FrameworkLayerBoundaryTests
    {
        [Test]
        public void RealmApplicationDoesNotExposeAStaticContextOrManagerLocator()
        {
            var staticMembers = typeof(RealmApplication)
                .GetMembers(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(member => member.MemberType == MemberTypes.Field || member.MemberType == MemberTypes.Property)
                .ToArray();

            Assert.That(staticMembers.Any(member =>
                string.Equals(member.Name, "Current", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(member.Name, "Instance", StringComparison.OrdinalIgnoreCase)), Is.False);
            Assert.That(staticMembers.OfType<PropertyInfo>().Any(property =>
                typeof(IRealmContext).IsAssignableFrom(property.PropertyType)), Is.False);
        }

        [Test]
        public void PresentationAssembliesCannotReferenceSystemServerSqliteOrUnityFromPurePresenter()
        {
            AssertForbiddenReferences(typeof(MainMenuPresenter).Assembly,
                "ProjectRealm.SystemServer", "ProjectRealm.Persistence.Sqlite", "UnityEngine");
            AssertForbiddenReferences(typeof(MainMenuScreenView).Assembly,
                "ProjectRealm.SystemServer", "ProjectRealm.Persistence.Sqlite");
        }

        [Test]
        public void FrameworkAssemblyCannotReferenceSystemServerOrUnity()
        {
            AssertForbiddenReferences(typeof(IRealmContext).Assembly, "ProjectRealm.SystemServer", "UnityEngine");
        }

        private static void AssertForbiddenReferences(Assembly assembly, params string[] forbidden)
        {
            var references = assembly.GetReferencedAssemblies().Select(item => item.Name).ToArray();
            foreach (var name in forbidden)
            {
                Assert.That(references, Does.Not.Contain(name), $"{assembly.GetName().Name} must not reference {name}.");
            }
        }
    }
}
