using UnityEngine;
using UnityEngine.UI;

namespace Hypocycloid.Ratioscope
{
    [DisallowMultipleComponent]
    public sealed class UIInfo : MonoBehaviour
    {
        [field: Header("Config")]
        [field: SerializeField]
        public UIConfigPanel ConfigPanel { get; private set; }

        [field: SerializeField]
        public Button ConfigButton { get; private set; }

        [field: Header("Help")]
        [field: SerializeField]
        public UIHelpPanel HelpPanel { get; private set; }

        [field: SerializeField]
        public Button HelpButton { get; private set; }

        [field: Header("Cortex")]
        [field: SerializeField]
        public Button FillButton { get; private set; }

        [field: SerializeField]
        public Button ImportButton { get; private set; }

        [field: SerializeField]
        public Button ExportButton { get; private set; }

        [field: SerializeField]
        public CortexChatController CortexChat { get; private set; }

        void OnEnable()
        {
            BindButtons();
        }

        void OnDisable()
        {
            UnbindButtons();
        }

        void BindButtons()
        {
            if (ConfigButton != null)
            {
                ConfigButton.onClick.RemoveListener(OnConfigButtonClicked);
                ConfigButton.onClick.AddListener(OnConfigButtonClicked);
            }

            if (HelpButton != null)
            {
                HelpButton.onClick.RemoveListener(OnHelpButtonClicked);
                HelpButton.onClick.AddListener(OnHelpButtonClicked);
            }

            if (FillButton != null)
            {
                FillButton.onClick.RemoveListener(OnFillButtonClicked);
                FillButton.onClick.AddListener(OnFillButtonClicked);
            }

            if (ImportButton != null)
            {
                ImportButton.onClick.RemoveListener(OnImportButtonClicked);
                ImportButton.onClick.AddListener(OnImportButtonClicked);
            }

            if (ExportButton != null)
            {
                ExportButton.onClick.RemoveListener(OnExportButtonClicked);
                ExportButton.onClick.AddListener(OnExportButtonClicked);
            }
        }

        void UnbindButtons()
        {
            if (ConfigButton != null)
                ConfigButton.onClick.RemoveListener(OnConfigButtonClicked);

            if (HelpButton != null)
                HelpButton.onClick.RemoveListener(OnHelpButtonClicked);

            if (FillButton != null)
                FillButton.onClick.RemoveListener(OnFillButtonClicked);

            if (ImportButton != null)
                ImportButton.onClick.RemoveListener(OnImportButtonClicked);

            if (ExportButton != null)
                ExportButton.onClick.RemoveListener(OnExportButtonClicked);
        }

        void OnConfigButtonClicked()
        {
            ConfigPanel?.ToggleVisible();
        }

        void OnHelpButtonClicked()
        {
            HelpPanel?.ToggleVisible();
        }

        void OnFillButtonClicked()
        {
            CortexChat?.ToggleDimension();
        }

        void OnImportButtonClicked()
        {
            CortexChat?.ImportDialogue();
        }

        void OnExportButtonClicked()
        {
            CortexChat?.ExportDialogue();
        }
    }
}
