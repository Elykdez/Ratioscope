using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace Hypocycloid.UI
{
    /// <summary>
    /// Feeds a slider's live value into a TipsTrigger so its tooltip shows the current
    /// value while hovering or dragging. Does nothing when no TipsTrigger is wired, so
    /// it is safe to leave on sliders that have no tooltip.
    /// </summary>
    [RequireComponent(typeof(Slider))]
    public class SliderTipValue : MonoBehaviour
    {
        [SerializeField]
        Slider slider;

        [SerializeField]
        [Tooltip("Tip trigger that displays the value (usually on the slider handle). Optional.")]
        TipsTrigger tipsTrigger;

        [SerializeField]
        [Tooltip("Composite format for the value, e.g. \"{0:0.##}\", \"{0:0}%\", \"{0:0.0}x\".")]
        string valueFormat = "{0:0.##}";

        // Optional custom content source. When it returns a non-empty string the tooltip
        // shows that instead of the formatted slider value - lets a driver such as the video
        // timeline display a playback time rather than the raw 0..1 position, while this
        // component stays the single owner of the trigger's content binding.
        public Func<string> ContentOverride { get; set; }

        void Awake()
        {
            if (slider == null)
                slider = GetComponent<Slider>();
        }

        void OnEnable()
        {
            if (slider == null || tipsTrigger == null)
                return;

            tipsTrigger.ContentProvider = FormatValue;
            slider.onValueChanged.AddListener(OnSliderValueChanged);
        }

        void OnDisable()
        {
            if (slider != null)
                slider.onValueChanged.RemoveListener(OnSliderValueChanged);

            // Release the override so the trigger falls back to its localized/static text.
            if (tipsTrigger != null)
                tipsTrigger.ContentProvider = null;
        }

        // Re-push the value while the tooltip is on screen so dragging updates it live.
        void OnSliderValueChanged(float _) => tipsTrigger.RefreshIfVisible();

        string FormatValue()
        {
            string custom = ContentOverride?.Invoke();
            if (!string.IsNullOrEmpty(custom))
                return custom;

            return string.Format(CultureInfo.InvariantCulture, valueFormat, slider.value);
        }

#if UNITY_EDITOR
        void Reset()
        {
            slider = GetComponent<Slider>();
            tipsTrigger = GetComponentInChildren<TipsTrigger>(true);
        }
#endif
    }
}
