using System;
using ProjectRealm.Foundation;
using ProjectRealm.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectRealm.UnityAdapter
{
    /// <summary>Unity 场景系统的薄适配器；场景只负责展示，不拥有活动 WorldRuntime。</summary>
    public sealed class UnityRealmSceneNavigator : IRealmSceneNavigator
    {
        public const string BootstrapScene = "Assets/Scenes/Runtime/00_Bootstrap.unity";
        public const string MainMenuScene = "Assets/Scenes/Runtime/10_MainMenu.unity";
        public const string GameplayScene = "Assets/Scenes/Runtime/20_Gameplay.unity";
        public const string CountyMapVisualScene = "Assets/Scenes/Debug/Map/90_Integration/CountyMapPrototype/CountyMapPrototype.unity";

        public RealmResult ShowMainMenu()
        {
            return LoadSingle(MainMenuScene);
        }

        public RealmResult ShowGameplay()
        {
            var result = LoadSingle(GameplayScene);
            if (!result.Succeeded)
            {
                return result;
            }

            if (Application.CanStreamedLevelBeLoaded(CountyMapVisualScene))
            {
                SceneManager.LoadScene(CountyMapVisualScene, LoadSceneMode.Additive);
            }

            return RealmResult.Success();
        }

        public RealmResult ShowFault(string message)
        {
            Debug.LogError("Project Realm entered Faulted state: " + (message ?? string.Empty));
            return SceneManager.GetActiveScene().path == MainMenuScene ? RealmResult.Success() : LoadSingle(MainMenuScene);
        }

        public RealmResult ExitApplication()
        {
            Application.Quit();
            return RealmResult.Success();
        }

        private static RealmResult LoadSingle(string scenePath)
        {
            if (!Application.CanStreamedLevelBeLoaded(scenePath))
            {
                return RealmResult.Failure(
                    "scene_unavailable",
                    $"Required scene '{scenePath}' is not enabled in Build Settings.",
                    RealmErrorKind.Unavailable);
            }

            try
            {
                SceneManager.LoadScene(scenePath, LoadSceneMode.Single);
                return RealmResult.Success();
            }
            catch (Exception exception)
            {
                return RealmResult.Failure("scene_load_failed", exception.Message, RealmErrorKind.Fatal);
            }
        }
    }
}
