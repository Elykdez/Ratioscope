using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Localization.Components;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace Hypocycloid.UI
{
    [RequireComponent(typeof(RectTransform))]
    public class TipsTrigger
        : LocalizeStringEvent,
            IPointerEnterHandler,
            IPointerExitHandler,
            IPointerDownHandler,
            IPointerUpHandler
    {
        const string TIP_ANCHOR_NAME = "{{TooltipAnchor}}";

        // Matches Android's own long-press timeout so held taps feel native.
        const float LONG_PRESS_SECONDS = 0.5f;

        [Serializable]
        public sealed class TipRequestEvent : UnityEvent<TipsTrigger> { }

        [SerializeField, TextArea]
        [Tooltip(
            "Fallback tooltip text. LocalizeStringEvent updates it when a string reference is assigned."
        )]
        string tipString;

        // Optional live content source. When assigned (e.g. by the timeline to show
        // the current time), it overrides the static/localized tipString each time the
        // tooltip text is read. Returning null falls back to tipString.
        public Func<string> ContentProvider { get; set; }
        public string Content => ContentProvider?.Invoke() ?? tipString;

        // Whether the tooltip is currently shown, so live content can be re-pushed.
        public bool TipVisible { get; private set; }

        [SerializeField]
        RectTransform tipAnchorTrans;
        public RectTransform TipAnchorTrans => tipAnchorTrans;
        public RectTransform Anchor =>
            tipAnchorTrans != null ? tipAnchorTrans : GetComponent<RectTransform>();

        [SerializeField]
        [Tooltip("Tooltip offset from the anchor, in the tooltip canvas local space.")]
        Vector2 localOffset = Vector2.zero;
        public Vector2 LocalOffset => localOffset;

        [SerializeField]
        [Tooltip("Tooltip pivot position.")]
        TipPivotPreset tipPivotPreset = TipPivotPreset.MiddleCenter;
        public Vector2 TipPivot => GetPivotFromPreset(tipPivotPreset);

        [SerializeField]
        float hoverDelay = 0.5f;
        public float HoverDelay => hoverDelay;

        [SerializeField]
        [Tooltip(
            "Touch behaviour. LongPress reveals the tip after a hold and cancels if the finger "
                + "moves, so scrolling stays clean. PressAndHold shows it immediately and keeps "
                + "it through the drag, for controls whose tip tracks the drag (e.g. sliders)."
        )]
        TouchTipMode touchMode = TouchTipMode.LongPress;

        [SerializeField]
        TextAlignmentOptions alignment = TextAlignmentOptions.Center;
        public TextAlignmentOptions Alignment => alignment;

        [Header("Show once")]
        [SerializeField]
        [Tooltip(
            "When set, this tooltip auto-shows only until the user has seen it once; "
                + "the seen state is persisted across sessions."
        )]
        bool showOnce;
        public bool ShowOnce => showOnce;

        [SerializeField]
        [Tooltip(
            "Stable id used to persist the show-once seen state. Falls back to the "
                + "localization key, then the object name."
        )]
        string tipId;

        // Resolved id for show-once persistence. The explicit field wins; otherwise
        // the localization key is used (stable across renames), then the object name.
        public string TipId
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(tipId))
                    return tipId;

                string key = StringReference?.TableEntryReference.Key;
                return !string.IsNullOrEmpty(key) ? key : name;
            }
        }

        [SerializeField]
        Graphic targetGraphic;

        [SerializeField]
        Color highlightColor = Color.white;
        Color originalColor;

        [Header("Events")]
        [SerializeField]
        TipRequestEvent onTipRequested = new();
        public TipRequestEvent OnTipRequested => onTipRequested;

        [SerializeField]
        UnityEvent onTipHidden = new();
        public UnityEvent OnTipHidden => onTipHidden;

#if UNITY_EDITOR
        [Header("Editor use")]
        [SerializeField]
        bool drawGizmo;
#endif

        Coroutine hoverRoutine;
        bool hasTargetGraphic;
        UnityAction<string> onTipUpdated;
        string fallbackI18nKey;

        void Awake()
        {
            if (StringReference != null && OnUpdateString != null)
            {
                onTipUpdated = value =>
                {
                    var operation = StringReference.CurrentLoadingOperationHandle;
                    bool found =
                        operation.IsValid() && operation.IsDone && operation.Result.Entry != null;
                    tipString = found ? value : fallbackI18nKey ?? value;
                    RefreshIfVisible();
                };
                OnUpdateString.AddListener(onTipUpdated);
            }

            if (targetGraphic != null)
            {
                hasTargetGraphic = true;
                originalColor = targetGraphic.color;
            }

            TryFindAnchor();
        }

        public void SetLocalizationKey(string table, string i18nKey)
        {
            if (string.IsNullOrWhiteSpace(i18nKey))
                return;

            fallbackI18nKey = i18nKey;
            tipString = i18nKey;
            StringReference.SetReference(table, i18nKey);
            RefreshString();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            TipSystem.RegisterActiveTrigger(this);
        }

        protected override void OnDisable()
        {
            CancelPendingHover();
            ResetHighlight();
            TipVisible = false;
            onTipHidden.Invoke();
            base.OnDisable();
            TipSystem.UnregisterActiveTrigger(this);
        }

        void OnDestroy()
        {
            TipSystem.UnregisterActiveTrigger(this);

            if (onTipUpdated != null && OnUpdateString != null)
                OnUpdateString.RemoveListener(onTipUpdated);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            // Touch also raises enter on press; those go through OnPointerDown so a tap does
            // not behave like a hover.
            if (IsTouch(eventData))
                return;

            CancelPendingHover();

            if (hoverDelay <= 0f)
            {
                ShowTip();
                return;
            }

            hoverRoutine = StartCoroutine(ShowTipAfterDelay());
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            HideTip();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!IsTouch(eventData))
                return;

            CancelPendingHover();

            if (touchMode == TouchTipMode.PressAndHold)
            {
                ShowTip();
                return;
            }

            hoverRoutine = StartCoroutine(ShowTipAfterLongPress(eventData));
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (IsTouch(eventData))
                HideTip();
        }

        static bool IsTouch(PointerEventData eventData)
        {
            // InputSystemUIInputModule uses non-negative device ids for every pointer,
            // including the mouse. Its extended event data provides the actual device type.
            if (eventData is ExtendedPointerEventData extendedEventData)
                return extendedEventData.pointerType == UIPointerType.Touch;

            // Legacy StandaloneInputModule reserves negative ids for mouse buttons.
            return eventData.pointerId >= 0;
        }

        public void ShowTip()
        {
            if (!isActiveAndEnabled)
                return;

            if (hasTargetGraphic)
                targetGraphic.color = highlightColor;

            if (tipAnchorTrans == null)
                TryFindAnchor();

            TipVisible = true;
            onTipRequested.Invoke(this);
        }

        public void HideTip()
        {
            CancelPendingHover();
            ResetHighlight();
            TipVisible = false;
            onTipHidden.Invoke();
        }

        // Re-pushes content to the tip system while the tooltip is on screen, so a
        // live ContentProvider (e.g. the current playback time) keeps updating.
        public void RefreshIfVisible()
        {
            if (TipVisible && isActiveAndEnabled)
                onTipRequested.Invoke(this);
        }

        IEnumerator ShowTipAfterDelay()
        {
            yield return new WaitForSecondsRealtime(hoverDelay);
            hoverRoutine = null;
            ShowTip();
        }

        IEnumerator ShowTipAfterLongPress(PointerEventData eventData)
        {
            Vector2 origin = eventData.position;
            // Reuse the threshold the EventSystem uses to promote a press into a drag, so the
            // tip yields to scrolling instead of competing with it.
            float threshold =
                EventSystem.current != null ? EventSystem.current.pixelDragThreshold : 10f;
            float elapsed = 0f;

            while (elapsed < LONG_PRESS_SECONDS)
            {
                // Polling the live event data keeps this off IDragHandler, which would take the
                // drag away from an enclosing ScrollRect.
                if ((eventData.position - origin).sqrMagnitude > threshold * threshold)
                {
                    hoverRoutine = null;
                    yield break;
                }

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            hoverRoutine = null;
            ShowTip();
        }

        void CancelPendingHover()
        {
            if (hoverRoutine == null)
                return;

            StopCoroutine(hoverRoutine);
            hoverRoutine = null;
        }

        void ResetHighlight()
        {
            if (hasTargetGraphic)
                targetGraphic.color = originalColor;
        }

        void TryFindAnchor()
        {
            var found = transform.Find(TIP_ANCHOR_NAME);
            if (found != null)
                tipAnchorTrans = found.GetComponent<RectTransform>();
        }

        static Vector2 GetPivotFromPreset(TipPivotPreset preset)
        {
            return preset switch
            {
                TipPivotPreset.TopLeft => new Vector2(0f, 1f),
                TipPivotPreset.TopCenter => new Vector2(0.5f, 1f),
                TipPivotPreset.TopRight => new Vector2(1f, 1f),
                TipPivotPreset.MiddleLeft => new Vector2(0f, 0.5f),
                TipPivotPreset.MiddleCenter => new Vector2(0.5f, 0.5f),
                TipPivotPreset.MiddleRight => new Vector2(1f, 0.5f),
                TipPivotPreset.BottomLeft => new Vector2(0f, 0f),
                TipPivotPreset.BottomCenter => new Vector2(0.5f, 0f),
                TipPivotPreset.BottomRight => new Vector2(1f, 0f),
                _ => new Vector2(0.5f, 0.5f),
            };
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (tipAnchorTrans == null)
                TryFindAnchor();
        }

        void OnDrawGizmos()
        {
            if (!drawGizmo || tipAnchorTrans == null)
                return;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(tipAnchorTrans.position, tipAnchorTrans.rect.size * 0.0025f);
            Gizmos.DrawLine(transform.position, tipAnchorTrans.position);
            DrawPivotDirectionVisual();
        }

        [ContextMenu("Reload Tips Anchor")]
        void TryCreateTooltipAnchor()
        {
            if (transform.Find(TIP_ANCHOR_NAME) != null)
                return;

            var anchorGo = new GameObject(TIP_ANCHOR_NAME, typeof(RectTransform));
            anchorGo.AddComponent<LayoutElement>().ignoreLayout = true;
            var rect = anchorGo.GetComponent<RectTransform>();
            rect.SetParent(transform, false);

            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, 30f);

            tipAnchorTrans = rect;

            EditorUtility.SetDirty(this);
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }

        void DrawPivotDirectionVisual()
        {
            if (tipAnchorTrans == null)
                return;

            const float radius = 0.05f;
            var pivot = TipPivot;
            var center = tipAnchorTrans.position;
            var size = tipAnchorTrans.rect.size * 0.0025f;

            var bottomLeft = center + new Vector3(-size.x * 0.5f, -size.y * 0.5f, 0);
            var topLeft = center + new Vector3(-size.x * 0.5f, size.y * 0.5f, 0);
            var topRight = center + new Vector3(size.x * 0.5f, size.y * 0.5f, 0);
            var bottomRight = center + new Vector3(size.x * 0.5f, -size.y * 0.5f, 0);

            var leftLerp = Vector3.Lerp(bottomLeft, topLeft, pivot.y);
            var rightLerp = Vector3.Lerp(bottomRight, topRight, pivot.y);
            var pivotPos = Vector3.Lerp(leftLerp, rightLerp, pivot.x);

            Handles.color = Color.yellow;
            Handles.DrawWireDisc(pivotPos, Vector3.forward, radius);
        }
#endif
    }

    public enum TouchTipMode
    {
        LongPress,
        PressAndHold,
    }

    public enum TipPivotPreset
    {
        TopLeft,
        TopCenter,
        TopRight,
        MiddleLeft,
        MiddleCenter,
        MiddleRight,
        BottomLeft,
        BottomCenter,
        BottomRight,
    }
}
