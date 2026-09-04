using System;
using ProjectRealm.Framework;
using ProjectRealm.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectRealm.UnityPresentation.Screens
{
    /// <summary>显式推进、保存和诊断的开发 Gameplay Shell；地图场景仅作为背后视觉层。</summary>
    [DisallowMultipleComponent]
    public sealed class GameplayScreenView : MonoBehaviour, IRealmScreen, IGameplayView
    {
        private GameplayPresenter _presenter;
        private Label _world;
        private Label _diagnostics;
        private Label _status;

        private void Awake()
        {
            BuildDocument();
        }

        public void Bind(IRealmContext context)
        {
            _presenter = new GameplayPresenter(context, this);
        }

        public void Enter()
        {
            _presenter?.Enter();
        }

        public void Exit()
        {
            _presenter?.Dispose();
            _presenter = null;
        }

        public void ShowWorld(WorldSessionSnapshot world)
        {
            _world.text =
                $"World {world.WorldId}  ·  {world.Year:D4}-{world.Month:D2}-{world.Day:D2}  ·  Tick {world.Tick}\n" +
                $"Hash {ShortHash(world.StateHash)}  ·  Nodes {world.GeographicNodeCount:N0}  ·  Modules {world.ModuleInstanceCount:N0}";
        }

        public void ShowDiagnostics(RealmDiagnosticsSnapshot diagnostics)
        {
            _diagnostics.text =
                $"Data Quality: {diagnostics.World.DataQuality}\n" +
                $"Scaffold / Unavailable: {diagnostics.World.ScaffoldModuleCount:N0}\n" +
                $"Commands {diagnostics.CommandCount}  Reservations {diagnostics.ReservationCount}  Events {diagnostics.EventCount}\n" +
                $"Checkpoints {diagnostics.CheckpointCount}  Last stages {diagnostics.Stages.Count}";
        }

        public void ShowStatus(string message, bool isError)
        {
            _status.text = message ?? string.Empty;
            _status.style.color = isError ? new Color(0.92f, 0.32f, 0.28f) : new Color(0.72f, 0.82f, 0.76f);
        }

        private void OnDestroy()
        {
            Exit();
        }

        private void BuildDocument()
        {
            var panelSettings = LoadPanelSettings();
            var document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.sortingOrder = 100;

            var root = document.rootVisualElement;
            root.style.flexGrow = 1;
            root.style.flexDirection = FlexDirection.Row;
            root.style.justifyContent = Justify.SpaceBetween;
            root.style.paddingLeft = 18;
            root.style.paddingRight = 18;
            root.style.paddingTop = 18;
            root.style.paddingBottom = 18;

            var panel = new VisualElement();
            panel.style.width = 420;
            panel.style.paddingLeft = 18;
            panel.style.paddingRight = 18;
            panel.style.paddingTop = 16;
            panel.style.paddingBottom = 16;
            panel.style.backgroundColor = new Color(0.055f, 0.065f, 0.06f, 0.94f);
            root.Add(panel);

            _world = Label("Loading world...", 16);
            panel.Add(_world);

            var controls = new VisualElement();
            controls.style.flexDirection = FlexDirection.Row;
            controls.style.flexWrap = Wrap.Wrap;
            controls.style.marginTop = 14;
            panel.Add(controls);
            controls.Add(Button("+ Day", () => _presenter?.Advance(RealmAdvanceUnit.Day)));
            controls.Add(Button("+ Month", () => _presenter?.Advance(RealmAdvanceUnit.Month)));
            controls.Add(Button("+ Season", () => _presenter?.Advance(RealmAdvanceUnit.Season)));
            controls.Add(Button("+ Year", () => _presenter?.Advance(RealmAdvanceUnit.Year)));
            controls.Add(Button("Save", () => _presenter?.Save()));
            controls.Add(Button("Main Menu", () => _presenter?.ReturnToMainMenu()));

            _diagnostics = Label("Diagnostics unavailable", 13);
            _diagnostics.style.marginTop = 16;
            panel.Add(_diagnostics);

            _status = Label(string.Empty, 12);
            _status.style.marginTop = 16;
            panel.Add(_status);
        }

        private static Label Label(string text, int size)
        {
            var label = new Label(text);
            label.style.fontSize = size;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.color = new Color(0.85f, 0.85f, 0.78f);
            return label;
        }

        private static Button Button(string text, Action clicked)
        {
            var button = new Button(clicked) { text = text };
            button.style.width = 116;
            button.style.height = 34;
            button.style.marginRight = 6;
            button.style.marginBottom = 6;
            return button;
        }

        private static string ShortHash(string hash)
        {
            return string.IsNullOrEmpty(hash) || hash.Length <= 16 ? hash : hash.Substring(0, 16);
        }

        private static PanelSettings LoadPanelSettings()
        {
            var settings = Resources.Load<PanelSettings>("ProjectRealmRuntimePanelSettings");
            if (settings == null)
            {
                throw new InvalidOperationException("ProjectRealmRuntimePanelSettings.asset is missing from Resources.");
            }

            return settings;
        }
    }
}
