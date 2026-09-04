using System;
using ProjectRealm.Foundation;

namespace ProjectRealm.Framework
{
    /// <summary>System Server 内部服务的可审计生命周期。</summary>
    public enum RealmServiceState
    {
        Created,
        Starting,
        Running,
        Paused,
        Stopping,
        Stopped,
        Faulted
    }

    /// <summary>由 RealmSystemServer 按依赖顺序启动、逆序停止的服务。</summary>
    public interface IRealmService
    {
        string ServiceName { get; }
        RealmServiceState State { get; }
        void Start();
        void Pause();
        void Resume();
        void Stop();
    }

    /// <summary>Unity 场景系统在 Framework 中的端口；实现不得保存权威世界状态。</summary>
    public interface IRealmSceneNavigator
    {
        RealmResult ShowMainMenu();
        RealmResult ShowGameplay();
        RealmResult ShowFault(string message);
        RealmResult ExitApplication();
    }

    /// <summary>已经提交的 Framework 事件只读流。</summary>
    public interface IRealmEventStream
    {
        IDisposable Subscribe<TEvent>(Action<TEvent> listener);
    }
}
