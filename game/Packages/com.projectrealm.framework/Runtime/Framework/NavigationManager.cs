using System;
using ProjectRealm.Foundation;

namespace ProjectRealm.Framework
{
    /// <summary>
    /// 应用场景导航代理。它只调用场景端口，不拥有 WorldRuntime；场景切换本身不得推进时间、
    /// 消耗随机流或改变状态散列。
    /// </summary>
    public sealed class NavigationManager
    {
        private readonly INavigationManagerGateway _gateway;

        internal NavigationManager(INavigationManagerGateway gateway)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        }

        public RealmResult ShowMainMenu() => _gateway.ShowMainMenu();
        public RealmResult ShowGameplay() => _gateway.ShowGameplay();
        public RealmResult ShowFault(string message) => _gateway.ShowFault(message);
        public RealmResult ExitApplication() => _gateway.ExitApplication();
    }
}
