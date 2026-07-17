using Hypocycloid.Utils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Hypocycloid.UI
{
    // 按钮（长按）节流
    [RequireComponent(typeof(Button))]
    public class UIButtonThrottler : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public float interactiveTime = 1f;
        public Button.ButtonClickedEvent OnTrigger;

        // Button selfBtn;
        readonly CoroutineHelper.Throttle actThrottle = new();
        public bool IsTriggered { get; private set; }

        void Update()
        {
            if (IsTriggered)
            {
                Trigger();
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            IsTriggered = true;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            IsTriggered = false;
        }

        public void Trigger()
        {
            actThrottle.Run(
                () =>
                {
                    OnTrigger?.Invoke();
                },
                interactiveTime
            );
        }

        // void SetInteractable(bool isInteractable)
        // {
        //     selfBtn.interactable = isInteractable;
        // }
    }
}
