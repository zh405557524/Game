using UnityEditor;
using UnityEngine;

namespace ProjectRealm.EditorTools
{
    [InitializeOnLoad]
    public static class ProjectRealmVersionGuard
    {
        public const string ExpectedUnityVersion = "6000.5.10f1";

        static ProjectRealmVersionGuard()
        {
            if (Application.unityVersion != ExpectedUnityVersion)
            {
                Debug.LogWarning(
                    $"Project Realm is locked to Unity {ExpectedUnityVersion}; current Editor is {Application.unityVersion}.");
            }
        }
    }
}
