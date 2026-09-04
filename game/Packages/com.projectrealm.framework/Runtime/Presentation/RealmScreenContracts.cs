using System.Collections.Generic;
using ProjectRealm.Framework;

namespace ProjectRealm.Presentation
{
    /// <summary>由 RealmApplication 在场景加载后显式注入 Context 的 Screen。</summary>
    public interface IRealmScreen
    {
        void Bind(IRealmContext context);
        void Enter();
        void Exit();
    }

    public interface IMainMenuView
    {
        void ShowStatus(string message, bool isError);
        void ShowSaveSlots(IReadOnlyList<SaveSlotSnapshot> slots);
    }

    public interface IGameplayView
    {
        void ShowWorld(WorldSessionSnapshot world);
        void ShowDiagnostics(RealmDiagnosticsSnapshot diagnostics);
        void ShowStatus(string message, bool isError);
    }
}
