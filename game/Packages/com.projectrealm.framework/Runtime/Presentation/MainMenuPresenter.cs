using System;
using ProjectRealm.Framework;

namespace ProjectRealm.Presentation
{
    /// <summary>主菜单只使用 Manager，不引用 System Server、SQLite 或 WorldRuntime。</summary>
    public sealed class MainMenuPresenter
    {
        private readonly IRealmContext _context;
        private readonly IMainMenuView _view;

        public MainMenuPresenter(IRealmContext context, IMainMenuView view)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _view = view ?? throw new ArgumentNullException(nameof(view));
        }

        public void Enter()
        {
            var slots = _context.Saves.ListSlots();
            if (!slots.Succeeded)
            {
                _view.ShowStatus(slots.Error.Message, true);
                return;
            }

            _view.ShowSaveSlots(slots.Value);
            _view.ShowStatus("Framework ready. Choose New World or Load.", false);
        }

        public void CreateWorld(string saveId, string worldId, long seed)
        {
            var result = _context.World.Create(new NewRealmWorldRequest(saveId, worldId, seed));
            if (!result.Succeeded)
            {
                _view.ShowStatus($"{result.Error.Code}: {result.Error.Message}", true);
                return;
            }

            var navigation = _context.Navigation.ShowGameplay();
            if (!navigation.Succeeded)
            {
                _view.ShowStatus($"{navigation.Error.Code}: {navigation.Error.Message}", true);
            }
        }

        public void LoadWorld(string saveId)
        {
            var result = _context.Saves.Load(saveId);
            if (!result.Succeeded)
            {
                _view.ShowStatus($"{result.Error.Code}: {result.Error.Message}", true);
                return;
            }

            var navigation = _context.Navigation.ShowGameplay();
            if (!navigation.Succeeded)
            {
                _view.ShowStatus($"{navigation.Error.Code}: {navigation.Error.Message}", true);
            }
        }

        public void ExitApplication()
        {
            var result = _context.Navigation.ExitApplication();
            if (!result.Succeeded)
            {
                _view.ShowStatus($"{result.Error.Code}: {result.Error.Message}", true);
            }
        }
    }
}
