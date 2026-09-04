using System;
using ProjectRealm.Foundation;
using ProjectRealm.Framework;

namespace ProjectRealm.SystemServer
{
    /// <summary>把 Framework 导航请求转发给 Unity 场景适配器。</summary>
    internal sealed class NavigationService : RealmServiceBase, INavigationManagerGateway
    {
        private readonly IRealmSceneNavigator _navigator;

        public NavigationService(IRealmSceneNavigator navigator)
            : base("NavigationService")
        {
            _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        }

        public RealmResult ShowMainMenu() => Invoke(_navigator.ShowMainMenu);
        public RealmResult ShowGameplay() => Invoke(_navigator.ShowGameplay);
        public RealmResult ShowFault(string message) => Invoke(() => _navigator.ShowFault(message));
        public RealmResult ExitApplication() => Invoke(_navigator.ExitApplication);

        private static RealmResult Invoke(Func<RealmResult> action)
        {
            try
            {
                return action();
            }
            catch (Exception exception)
            {
                var error = RealmErrorMapper.FromException(exception);
                return RealmResult.Failure(error.Code, error.Message, error.Kind);
            }
        }
    }
}
