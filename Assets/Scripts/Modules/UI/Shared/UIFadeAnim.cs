using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace Hypocycloid.UI
{
    /// <summary>
    /// Simple UI Anim
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class UIFadeAnim : MonoBehaviour
    {
        public float showDelay;
        public AnimationCurve fadeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        public bool isRewind;
        public bool isPassive;
        public bool playOnStart;
        public UnityEvent<bool> onComplete;

        float lastKeyTime;
        CanvasGroup uiGroup;

        void Awake()
        {
            uiGroup = GetComponent<CanvasGroup>();
            if (!uiGroup.isActiveAndEnabled)
                uiGroup.enabled = true;
        }

        // OnEnable
        void OnEnable()
        {
            lastKeyTime = fadeCurve[fadeCurve.length - 1].time;
            if (!isPassive)
                Play();
        }

        IEnumerator SetVisible(float delay)
        {
            uiGroup.alpha = fadeCurve.Evaluate(isRewind ? lastKeyTime : 0);
            ResetInteraction();

            if (delay > 0)
                yield return new WaitForSeconds(delay);

            float elapsed = 0f;
            while (elapsed < lastKeyTime)
            {
                yield return null;
                elapsed += Time.deltaTime;
                float currentKeyTime = isRewind ? (lastKeyTime - elapsed) : elapsed;
                uiGroup.alpha = fadeCurve.Evaluate(currentKeyTime);
                ResetInteraction();
            }

            uiGroup.alpha = fadeCurve.Evaluate(isRewind ? 0 : lastKeyTime);
            ResetInteraction();
            onComplete?.Invoke(!isRewind);
        }

        /// <summary>
        /// Reset interaction
        /// </summary>
        void ResetInteraction()
        {
            bool state = uiGroup.alpha <= 0;
            uiGroup.interactable = !state;
            uiGroup.blocksRaycasts = !state;
        }

        /// <summary>
        /// Should set on
        /// </summary>
        /// <param name="isOn"></param>
        public void SetOn(bool isOn)
        {
            StopAllCoroutines();
            uiGroup.alpha = isOn ? 1 : 0;
            ResetInteraction();
        }

        /// <summary>
        /// Play anim
        /// </summary>
        /// <param name="delay">delay(s)</param>
        /// <summary>
        public void Play()
        {
            // if (uiGroup == null) uiGroup = GetComponent<CanvasGroup>();
            StopAllCoroutines();
            StartCoroutine(SetVisible(Mathf.Abs(showDelay)));
        }

        /// <summary>
        /// Play anim
        /// </summary>
        /// <param name="delay">delay(s)</param>
        /// <summary>

        public void Play(bool show)
        {
            this.isRewind = !show;
            Play();
        }
    }
}
