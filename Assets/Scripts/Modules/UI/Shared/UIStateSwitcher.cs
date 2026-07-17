using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace Hypocycloid.UI
{
    /// <summary>
    /// A component that allows switching between two states on a UI component.
    /// </summary>
    public class UIStateSwitcher : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField]
        PointerEventData.InputButton trigger = PointerEventData.InputButton.Right;

        [SerializeField]
        bool isAlternate = false;

        [SerializeField]
        UnityEvent<bool> onToggle;
        public UnityEvent<bool> OnToggle
        {
            get
            {
                onToggle ??= new();
                return onToggle;
            }
        }

        void Start()
        {
            ToggleState(isAlternate);
        }

        public void ToggleState(bool isOn)
        {
            isAlternate = isOn;
            OnToggle.Invoke(isAlternate);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            // Check if right mouse button was clicked
            if (eventData.button == trigger)
            {
                ToggleState(!isAlternate);
            }
        }
    }
}
