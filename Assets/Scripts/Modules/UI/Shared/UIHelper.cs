using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Hypocycloid.UI
{
    public static class UIHelper
    {
        // TMP exposes TMP_Settings.warningsDisabled read-only, so the serialized flag behind it
        // is what gets written.
        static readonly FieldInfo TextWarningsField = typeof(TMP_Settings).GetField(
            "m_warningsDisabled",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        /// <summary>
        /// Turns TMP's console warnings - missing glyph, missing font asset - on or off. The
        /// flag lives on the TMP Settings asset loaded from Resources; this only changes the
        /// loaded copy, so the project asset keeps whatever it has serialized. Returns false
        /// when the flag could not be reached.
        /// </summary>
        public static bool SetTextWarningsEnabled(bool enabled)
        {
            TMP_Settings settings = TMP_Settings.instance;
            if (TextWarningsField == null || settings == null)
                return false;

            TextWarningsField.SetValue(settings, !enabled);
            return true;
        }

        public static void AddTriggerEvent(
            EventTrigger trigger,
            EventTriggerType type,
            UnityAction<BaseEventData> callback
        )
        {
            EventTrigger.Entry entry = new() { eventID = type };
            entry.callback.AddListener(callback);
            trigger.triggers.Add(entry);
        }

        public static void RemoveTriggerEvent(
            EventTrigger trigger,
            EventTriggerType type,
            UnityAction<BaseEventData> callback
        )
        {
            if (trigger?.triggers == null || callback == null)
                return;

            EventTrigger.Entry entry = trigger.triggers.Find(e => e.eventID == type);
            entry?.callback.RemoveListener(callback);
        }

        public static void BindDropdown(TMP_Dropdown dropdown, UnityAction<int> action)
        {
            if (dropdown == null)
                return;

            dropdown.onValueChanged.RemoveListener(action);
            dropdown.onValueChanged.AddListener(action);
        }

        public static void BindButton(Button button, UnityAction action)
        {
            if (button == null)
                return;

            button.onClick.RemoveListener(action);
            button.onClick.AddListener(action);
        }

        public static void BindToggle(Toggle toggle, UnityAction<bool> action)
        {
            if (toggle == null)
                return;

            toggle.onValueChanged.RemoveListener(action);
            toggle.onValueChanged.AddListener(action);
        }

        public static void SetDropdownValueWithoutNotify(TMP_Dropdown dropdown, int value)
        {
            if (dropdown == null)
                return;

            dropdown.SetValueWithoutNotify(value);
            dropdown.RefreshShownValue();
        }

        public static void DisableNestedButtonGraphics(RectTransform buttonRect)
        {
            Button button = buttonRect.GetComponent<Button>();
            Graphic targetGraphic = button != null ? button.targetGraphic : null;
            Graphic[] graphics = buttonRect.GetComponentsInChildren<Graphic>(true);
            foreach (Graphic graphic in graphics)
            {
                if (graphic.transform == buttonRect || graphic == targetGraphic)
                    continue;

                graphic.gameObject.SetActive(false);
            }
        }

        public static void DisableLocalization(GameObject target)
        {
            MonoBehaviour[] behaviours = target.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (MonoBehaviour behaviour in behaviours)
            {
                string typeName = behaviour.GetType().Name;
                if (typeName == "LocalizeStringEvent" || typeName == "GameObjectLocalizer")
                    behaviour.enabled = false;
            }
        }

        public static void ConfigureBoxRect(RectTransform box)
        {
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 0.5f);
            box.anchoredPosition = Vector2.zero;
            box.localRotation = Quaternion.identity;
            box.localScale = Vector3.one;
        }

        public static void ConfigureResetRect(RectTransform reset)
        {
            reset.anchorMin = reset.anchorMax = reset.pivot = new Vector2(1f, 1f);
            reset.anchoredPosition = new Vector2(-8f, -8f);
            reset.sizeDelta = new Vector2(64f, 26f);
            reset.localRotation = Quaternion.identity;
            reset.localScale = Vector3.one;
        }

        public static void StretchToParent(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
        }

        public static RectTransform FindChildRect(Transform root, string childName)
        {
            if (root == null)
                return null;

            RectTransform[] children = root.GetComponentsInChildren<RectTransform>(true);
            foreach (RectTransform child in children)
                if (child.name == childName)
                    return child;

            return null;
        }

        public static void SetLayerRecursively(GameObject target, int layer)
        {
            target.layer = layer;
            foreach (Transform child in target.transform)
                SetLayerRecursively(child.gameObject, layer);
        }

        public static Vector2 AspectCorrect(Vector2 delta, float aspect) =>
            new(delta.x * aspect, delta.y);

        public static Vector2 LocalToOutputDelta(Vector2 delta, float degrees, float aspect)
        {
            Vector2 corrected = AspectCorrect(delta, aspect);
            float radians = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            return new Vector2(
                (cos * corrected.x - sin * corrected.y) / aspect,
                sin * corrected.x + cos * corrected.y
            );
        }

        public static Vector2 OutputToLocalDelta(Vector2 delta, float degrees, float aspect) =>
            LocalToOutputDelta(delta, -degrees, aspect);
    }
}
