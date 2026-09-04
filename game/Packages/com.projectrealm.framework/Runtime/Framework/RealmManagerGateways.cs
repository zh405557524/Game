using System.Collections.Generic;
using ProjectRealm.Foundation;

namespace ProjectRealm.Framework
{
    internal interface IWorldManagerGateway
    {
        bool HasActiveWorld { get; }
        RealmResult<WorldSessionSnapshot> GetCurrent();
        RealmResult<WorldSessionSnapshot> Create(NewRealmWorldRequest request);
        RealmResult Close();
    }

    internal interface ISimulationManagerGateway
    {
        RealmResult<SimulationStepSnapshot> Advance(RealmAdvanceUnit unit);
        RealmResult<CommandTicketSnapshot> Submit(RealmCommandRequest request);
        RealmResult Pause();
        RealmResult Resume();
    }

    internal interface ISaveManagerGateway
    {
        RealmResult<IReadOnlyList<SaveSlotSnapshot>> ListSlots();
        RealmResult<WorldSessionSnapshot> Load(string saveId);
        RealmResult Save();
    }

    internal interface INavigationManagerGateway
    {
        RealmResult ShowMainMenu();
        RealmResult ShowGameplay();
        RealmResult ShowFault(string message);
        RealmResult ExitApplication();
    }

    internal interface IDiagnosticsManagerGateway
    {
        RealmResult<RealmDiagnosticsSnapshot> Query(string search, int page, int pageSize);
    }
}
