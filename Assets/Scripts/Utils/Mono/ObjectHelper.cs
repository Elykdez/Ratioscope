using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Hypocycloid.Utils
{
    public class ObjectHelper
    {
        public struct ToggleState
        {
            Behaviour component;
            bool enabled;

            public static ToggleState Capture(Behaviour component)
            {
                return new ToggleState
                {
                    component = component,
                    enabled = component != null && component.enabled,
                };
            }

            public readonly void Restore()
            {
                SetEnabled(enabled);
            }

            public readonly void SetEnabled(bool isEnabled)
            {
                if (component != null)
                    component.enabled = isEnabled;
            }
        }

        public struct ActiveState
        {
            GameObject gameObject;
            bool activeSelf;

            public static ActiveState Capture(Component component)
            {
                return new ActiveState
                {
                    gameObject = component != null ? component.gameObject : null,
                    activeSelf = component != null && component.gameObject.activeSelf,
                };
            }

            public readonly void Restore()
            {
                SetActive(activeSelf);
            }

            public readonly void SetActive(bool isActive)
            {
                if (gameObject != null)
                    gameObject.SetActive(isActive);
            }
        }

        public struct LayoutState
        {
            public bool IsValid;
            public Vector2 AnchorMin;
            public Vector2 AnchorMax;
            public Vector2 Pivot;
            public Vector2 AnchoredPosition;
            public Vector2 SizeDelta;

            public static LayoutState Capture(RectTransform trans)
            {
                if (trans == null)
                    return default;

                return new LayoutState
                {
                    IsValid = true,
                    AnchorMin = trans.anchorMin,
                    AnchorMax = trans.anchorMax,
                    Pivot = trans.pivot,
                    AnchoredPosition = trans.anchoredPosition,
                    SizeDelta = trans.sizeDelta,
                };
            }
        }

        public static GameObject InstantiatePrefab(GameObject prefab, Transform parent)
        {
            if (prefab == null)
                return null;

            Object instance;
            try
            {
                instance = Object.Instantiate((Object)prefab, parent, false);
            }
            catch (InvalidCastException)
            {
                return null;
            }

            if (instance is GameObject gameObject)
                return gameObject;

            if (instance is Component component)
                return component.gameObject;

            if (instance != null)
                Object.Destroy(instance);
            return null;
        }

        public static void DestroyObject(GameObject obj)
        {
            if (obj == null)
                return;
            if (Application.isPlaying)
                Object.Destroy(obj);
            else
                Object.DestroyImmediate(obj);
        }
    }
}
