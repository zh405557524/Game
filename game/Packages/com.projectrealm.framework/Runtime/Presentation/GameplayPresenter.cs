using System;
using ProjectRealm.Framework;

namespace ProjectRealm.Presentation
{
    /// <summary>Gameplay Shell 的用例协调器；所有世界变化仍通过 Manager 请求。</summary>
    public sealed class GameplayPresenter : IDisposable
    {
        private readonly IRealmContext _context;
        private readonly IGameplayView _view;
        private readonly IDisposable _advanceSubscription;
        private readonly IDisposable _saveSubscription;

        public GameplayPresenter(IRealmContext context, IGameplayView view)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _advanceSubscription = context.Events.Subscribe<RealmSimulationAdvancedEvent>(_ => Refresh());
            _saveSubscription = context.Events.Subscribe<RealmSaveCompletedEvent>(item =>
                _view.ShowStatus($"Saved {item.SaveId} @ {ShortHash(item.StateHash)}", false));
        }

        public void Enter()
        {
            Refresh();
        }

        public void Advance(RealmAdvanceUnit unit)
        {
            var result = _context.Simulation.Advance(unit);
            if (!result.Succeeded)
            {
                _view.ShowStatus($"{result.Error.Code}: {result.Error.Message}", true);
                return;
            }

            _view.ShowStatus(result.Value.Committed
                ? $"Advanced {unit}; tick={result.Value.Tick}; hash={ShortHash(result.Value.StateHash)}"
                : $"Tick rolled back: {result.Value.FailureReason}", !result.Value.Committed);
            Refresh();
        }

        public void Save()
        {
            var result = _context.Saves.Save();
            if (!result.Succeeded)
            {
                _view.ShowStatus($"{result.Error.Code}: {result.Error.Message}", true);
            }
        }

        public void ReturnToMainMenu()
        {
            var close = _context.World.Close();
            if (!close.Succeeded)
            {
                _view.ShowStatus($"{close.Error.Code}: {close.Error.Message}", true);
                return;
            }

            var navigation = _context.Navigation.ShowMainMenu();
            if (!navigation.Succeeded)
            {
                _view.ShowStatus($"{navigation.Error.Code}: {navigation.Error.Message}", true);
            }
        }

        public void Dispose()
        {
            _advanceSubscription.Dispose();
            _saveSubscription.Dispose();
        }

        private void Refresh()
        {
            var world = _context.World.GetCurrent();
            if (!world.Succeeded)
            {
                _view.ShowStatus($"{world.Error.Code}: {world.Error.Message}", true);
                return;
            }

            _view.ShowWorld(world.Value);
            var diagnostics = _context.Diagnostics.Query(pageSize: 10);
            if (diagnostics.Succeeded)
            {
                _view.ShowDiagnostics(diagnostics.Value);
            }
        }

        private static string ShortHash(string hash)
        {
            return string.IsNullOrEmpty(hash) || hash.Length <= 12 ? hash : hash.Substring(0, 12);
        }
    }
}
