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
        public Button InspectButton { get; private set; }

        [field: SerializeField]
        public CortexChatController CortexChat { get; private set; }

        void OnEnable()
        {
            BindButtons();
            if (CortexChat != null)
                CortexChat.InspectAvailabilityChanged += OnInspectAvailabilityChanged;
            // Inspection is only possible while a stream runs, so the button starts disabled.
            OnInspectAvailabilityChanged(CortexChat != null && CortexChat.CanInspect);
        }

        void OnDisable()
        {
            UnbindButtons();
            if (CortexChat != null)
                CortexChat.InspectAvailabilityChanged -= OnInspectAvailabilityChanged;
        }

        void OnInspectAvailabilityChanged(bool available)
        {
            if (InspectButton != null)
                InspectButton.interactable = available;
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

            if (InspectButton != null)
            {
                InspectButton.onClick.RemoveListener(OnInspectButtonClicked);
                InspectButton.onClick.AddListener(OnInspectButtonClicked);
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

            if (InspectButton != null)
                InspectButton.onClick.RemoveListener(OnInspectButtonClicked);
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

        void OnInspectButtonClicked()
        {
            CortexChat?.ToggleInspection();
        }
    }
}
