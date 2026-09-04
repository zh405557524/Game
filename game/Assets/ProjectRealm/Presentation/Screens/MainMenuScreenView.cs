using System;
using System.Collections.Generic;
using System.Linq;
using ProjectRealm.Framework;
using ProjectRealm.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectRealm.UnityPresentation.Screens
{
    /// <summary>无美术依赖的开发主菜单；RealmApplication 在场景加载后注入 Context。</summary>
    [DisallowMultipleComponent]
    public sealed class MainMenuScreenView : MonoBehaviour, IRealmScreen, IMainMenuView
    {
        [SerializeField] private string saveId = "development-framework";
        [SerializeField] private string worldId = "MING1628";
        [SerializeField] private long worldSeed = 1628;

        private MainMenuPresenter _presenter;
        private Label _status;
        private DropdownField _saveSlots;

        private void Awake()
        {
            BuildDocument();
        }

        public void Bind(IRealmContext context)
        {
            _presenter = new MainMenuPresenter(context, this);
        }

        public void Enter()
        {
            _presenter?.Enter();
        }

        public void Exit()
        {
        }

        public void ShowStatus(string message, bool isError)
        {
            _status.text = message ?? string.Empty;
            _status.style.color = isError ? new Color(0.92f, 0.32f, 0.28f) : new Color(0.72f, 0.82f, 0.76f);
        }

        public void ShowSaveSlots(IReadOnlyList<SaveSlotSnapshot> slots)
        {
            var choices = (slots ?? Array.Empty<SaveSlotSnapshot>()).Select(slot => slot.SaveId).ToList();
            _saveSlots.choices = choices;
            _saveSlots.value = choices.Count > 0 ? choices[0] : saveId;
        }

        private void BuildDocument()
        {
            var panelSettings = LoadPanelSettings();
            var document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.sortingOrder = 100;

            var root = document.rootVisualElement;
            root.style.flexGrow = 1;
            root.style.alignItems = Align.Center;
            root.style.justifyContent = Justify.Center;
            root.style.backgroundColor = new Color(0.055f, 0.065f, 0.06f, 0.97f);

            var card = new VisualElement();
            card.style.width = 560;
            card.style.paddingLeft = 36;
            card.style.paddingRight = 36;
            card.style.paddingTop = 30;
            card.style.paddingBottom = 30;
            card.style.backgroundColor = new Color(0.10f, 0.12f, 0.11f, 1f);
            root.Add(card);

            var title = new Label("PROJECT REALM");
            title.style.fontSize = 32;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new Color(0.86f, 0.82f, 0.69f);
            title.style.marginBottom = 6;
            card.Add(title);

            var subtitle = new Label("Android-style World Framework · Development Shell");
            subtitle.style.color = new Color(0.58f, 0.66f, 0.61f);
            subtitle.style.marginBottom = 24;
            card.Add(subtitle);

            var newButton = Button("New Development World", () => _presenter?.CreateWorld(saveId, worldId, worldSeed));
            card.Add(newButton);

            _saveSlots = new DropdownField("Save")
            {
                choices = new List<string>(),
                value = saveId
            };
            _saveSlots.style.marginTop = 12;
            _saveSlots.style.marginBottom = 8;
            card.Add(_saveSlots);

            card.Add(Button("Load Selected Save", () => _presenter?.LoadWorld(_saveSlots.value)));
            card.Add(Button("Exit", () => _presenter?.ExitApplication()));

            _status = new Label("Booting Framework...");
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.marginTop = 20;
            _status.style.color = new Color(0.72f, 0.82f, 0.76f);
            card.Add(_status);
        }

        private static Button Button(string text, Action clicked)
        {
            var button = new Button(clicked) { text = text };
            button.style.height = 40;
            button.style.marginBottom = 8;
            return button;
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
