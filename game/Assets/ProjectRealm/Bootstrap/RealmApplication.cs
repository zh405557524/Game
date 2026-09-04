using System;
using System.Linq;
using ProjectRealm.Framework;
using ProjectRealm.Persistence.Sqlite;
using ProjectRealm.Presentation;
using ProjectRealm.SystemServer;
using ProjectRealm.UnityAdapter;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectRealm.Bootstrap
{
    /// <summary>
    /// Unity 中相当于 Java main()/Android Application 的唯一入口。
    /// Awake 只构造 System Server、注入 Context 并进入主菜单；不会自动创建或推进世界。
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public sealed class RealmApplication : MonoBehaviour
    {
        private const string DefaultDefinitionResource = "realm_definition_ming1628_dev_v1";

        [SerializeField] private string definitionResource = DefaultDefinitionResource;

        private RealmSystemServer _systemServer;
        private IDisposable _faultSubscription;
        private bool _stopped;
        private RealmApplicationState _bootstrapState = RealmApplicationState.Cold;

        public IRealmContext Context => _systemServer?.Context;
        public RealmApplicationState State => _systemServer?.State ?? _bootstrapState;
        public string LastError { get; private set; } = string.Empty;

        private void Awake()
        {
            var applications = FindObjectsByType<RealmApplication>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (applications.Any(item => item != this))
            {
                Debug.LogError("Only one RealmApplication may exist. Destroying duplicate Bootstrap root.", this);
                Destroy(gameObject);
                return;
            }

            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            _bootstrapState = RealmApplicationState.Booting;

            try
            {
                var definitionAsset = new UnityDefinitionAssetProvider(definitionResource).LoadRequired();
                var definitions = new SqliteWorldDefinitionStore(definitionAsset);
                var smokeRoot = ReadArgument("-projectRealmFrameworkSmokeRoot");
                var saves = new SqliteSaveGameStore(string.IsNullOrWhiteSpace(smokeRoot)
                    ? UnityEngine.Application.persistentDataPath
                    : smokeRoot);
                _systemServer = new RealmSystemServer(definitions, saves, new UnityRealmSceneNavigator());
                _faultSubscription = _systemServer.Context.Events.Subscribe<RealmFaultedEvent>(OnFaulted);
                _systemServer.Start();

                var navigation = _systemServer.Context.Navigation.ShowMainMenu();
                if (!navigation.Succeeded)
                {
                    throw new InvalidOperationException(navigation.Error.Message);
                }
            }
            catch (Exception exception)
            {
                _bootstrapState = RealmApplicationState.Faulted;
                LastError = exception.Message;
                Debug.LogException(exception, this);
            }
        }

        private void OnApplicationQuit()
        {
            StopServer();
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            _faultSubscription?.Dispose();
            StopServer();
        }

        private void OnGUI()
        {
            if (string.IsNullOrEmpty(LastError))
            {
                return;
            }

            GUI.color = new Color(0.95f, 0.35f, 0.3f);
            GUI.Box(new Rect(24, 24, Math.Min(Screen.width - 48, 860), 120),
                "Project Realm Framework Fault\n\n" + LastError +
                "\n\nSee the Unity Console for details.");
            GUI.color = Color.white;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (Context == null)
            {
                return;
            }

            foreach (var screen in FindScreens(scene))
            {
                screen.Bind(Context);
                screen.Enter();
            }
        }

        private void OnSceneUnloaded(Scene scene)
        {
            foreach (var screen in FindScreens(scene))
            {
                screen.Exit();
            }
        }

        private void OnFaulted(RealmFaultedEvent item)
        {
            LastError = item.Code + ": " + item.Message;
            Context?.Navigation.ShowFault(LastError);
        }

        private void StopServer()
        {
            if (_stopped)
            {
                return;
            }

            _stopped = true;
            try
            {
                _systemServer?.Stop();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        private static IRealmScreen[] FindScreens(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return Array.Empty<IRealmScreen>();
            }

            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true))
                .OfType<IRealmScreen>()
                .ToArray();
        }

        private static string ReadArgument(string name)
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index < arguments.Length - 1; index++)
            {
                if (string.Equals(arguments[index], name, StringComparison.Ordinal)) return arguments[index + 1];
            }
            return null;
        }
    }
}
