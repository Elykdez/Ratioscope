using System;
using Hypocycloid.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Hypocycloid.UI
{
    // 按钮（短按）防抖
    // 注意：该方法会影响到按钮的interactable状态
    [RequireComponent(typeof(Button))]
    public class UIButtonDebouncer : MonoBehaviour
    {
        // Time interval for debouncing in seconds
        public float debounceInterval = 0.5f;

        Button button; // Reference to the UI Button
        CoroutineHelper.Debounce debounce; // Instance of Debounce class
        bool debouncing;

        void Awake()
        {
            button = GetComponent<Button>();
            debounce = new CoroutineHelper.Debounce();
            button.onClick.AddListener(OnButtonClick);
        }

        void OnEnable()
        {
            if (debouncing)
            {
                button.interactable = true;
                debouncing = false;
            }
            debounce?.ResetTime(this);
        }

        void OnButtonClick()
        {
            button.interactable = false;
            debouncing = true;
            debounce.Run(
                () =>
                {
                    debouncing = false;
                    button.interactable = true;
                },
                debounceInterval,
                this
            );
        }
    }
}
