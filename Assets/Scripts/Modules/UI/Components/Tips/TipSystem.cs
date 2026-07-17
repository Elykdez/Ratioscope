using System.Collections.Generic;
using Hypocycloid.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Hypocycloid.UI
{
    public class TipSystem : MonoBehaviour
    {
        // Global-scoped key prefix for show-once seen state (see TipsTrigger.ShowOnce).
        const string TIP_SEEN_PREFIX = "tip_seen_";

        // Gap kept between the tip and the canvas edge when it has to be flipped or slid.
        const float EDGE_PADDING = 8f;

        [field: SerializeField]
        public UITip TipsUI { get; private set; }

        static readonly List<TipSystem> activeSystems = new();
        static readonly List<TipsTrigger> activeTriggers = new();

        readonly List<TipsTrigger> registeredTriggers = new();

        void Awake()
        {
            InitializeTipsUI();
        }

        void OnEnable()
        {
            if (!activeSystems.Contains(this))
                activeSystems.Add(this);

            RegisterActiveTriggersInScene();
        }

        void OnDisable()
        {
            activeSystems.Remove(this);
            UnregisterAllTriggers();
            HideTips();
        }

        void Start()
        {
            HideTips();
        }

        public void SetTipsUI(UITip tipsUI)
        {
            TipsUI = tipsUI;
            InitializeTipsUI();
        }

        public void ShowTips(TipsTrigger trigger)
        {
            if (trigger == null)
                return;

            // Show-once tips are suppressed once the user has seen them. Intended for
            // static hints; pairing it with a live ContentProvider is not supported.
            if (trigger.ShowOnce && IsTipSeen(trigger.TipId))
                return;

            ShowTips(
                trigger.Content,
                trigger.Anchor,
                trigger.TipPivot,
                trigger.LocalOffset,
                trigger.Alignment
            );

            if (trigger.ShowOnce)
                MarkTipSeen(trigger.TipId);
        }

        static bool IsTipSeen(string tipId) =>
            !string.IsNullOrEmpty(tipId) && GameData.GetBool(TIP_SEEN_PREFIX + tipId, false, true);

        static void MarkTipSeen(string tipId)
        {
            if (string.IsNullOrEmpty(tipId))
                return;

            GameData.SetBool(TIP_SEEN_PREFIX + tipId, true, true);
            GameData.Save();
        }

        public void ShowTips(
            string content,
            RectTransform uiTrans,
            Vector2 tooltipPivot,
            Vector2 localOffset,
            TextAlignmentOptions alignment
        )
        {
            if (uiTrans == null)
                return;

            if (!EnsureTipsUI())
                return;

            TipsUI.gameObject.SetActive(true);
            TipsUI.TipText.text = content;
            TipsUI.TipText.alignment = alignment;
            TipsUI.TipRect.pivot = tooltipPivot;
            TipsUI.TipRect.anchoredPosition = RePosition(uiTrans, localOffset);
            FitInsideCanvas(tooltipPivot, localOffset);
        }

        public void HideTips()
        {
            if (TipsUI == null)
                return;

            TipsUI.gameObject.SetActive(false);
        }

        public void RegisterTrigger(TipsTrigger trigger)
        {
            if (trigger == null || registeredTriggers.Contains(trigger))
                return;

            trigger.OnTipRequested.AddListener(ShowTips);
            trigger.OnTipHidden.AddListener(HideTips);
            registeredTriggers.Add(trigger);
        }

        public void UnregisterTrigger(TipsTrigger trigger)
        {
            if (trigger == null)
                return;

            trigger.OnTipRequested.RemoveListener(ShowTips);
            trigger.OnTipHidden.RemoveListener(HideTips);
            registeredTriggers.Remove(trigger);
        }

        internal static void RegisterActiveTrigger(TipsTrigger trigger)
        {
            if (trigger == null)
                return;

            if (!activeTriggers.Contains(trigger))
                activeTriggers.Add(trigger);

            for (var i = activeSystems.Count - 1; i >= 0; i--)
            {
                var system = activeSystems[i];
                if (system == null)
                {
                    activeSystems.RemoveAt(i);
                    continue;
                }

                if (system.gameObject.scene == trigger.gameObject.scene)
                    system.RegisterTrigger(trigger);
            }
        }

        internal static void UnregisterActiveTrigger(TipsTrigger trigger)
        {
            if (trigger == null)
                return;

            activeTriggers.Remove(trigger);

            for (var i = activeSystems.Count - 1; i >= 0; i--)
            {
                var system = activeSystems[i];
                if (system == null)
                {
                    activeSystems.RemoveAt(i);
                    continue;
                }

                system.UnregisterTrigger(trigger);
            }
        }

        void InitializeTipsUI()
        {
            if (TipsUI == null)
                TipsUI = GetComponentInChildren<UITip>(true);

            if (TipsUI == null)
                return;

            TipsUI.Init();
            TipsUI.gameObject.SetActive(false);
        }

        bool EnsureTipsUI()
        {
            if (TipsUI != null)
                return true;

            InitializeTipsUI();
            return TipsUI != null;
        }

        void RegisterActiveTriggersInScene()
        {
            for (var i = activeTriggers.Count - 1; i >= 0; i--)
            {
                var trigger = activeTriggers[i];
                if (trigger == null)
                {
                    activeTriggers.RemoveAt(i);
                    continue;
                }

                if (trigger.gameObject.scene == gameObject.scene)
                    RegisterTrigger(trigger);
            }
        }

        void UnregisterAllTriggers()
        {
            for (var i = registeredTriggers.Count - 1; i >= 0; i--)
                UnregisterTrigger(registeredTriggers[i]);
        }

        Vector2 RePosition(RectTransform uiTrans, Vector2 localOffset)
        {
            // Place the tooltip at the CENTER of the {{TooltipAnchor}} rect rather than its
            // pivot. The anchor is authored as the region the tooltip should occupy (its
            // pivot sits on the element edge so the box extends into open space), so its
            // center is the intended spot; using the pivot makes the tip cling to the element.
            Vector3 anchorCenter = uiTrans.TransformPoint(uiTrans.rect.center);

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                TipsUI.CanvasRect,
                RectTransformUtility.WorldToScreenPoint(TipsUI.EventCamera, anchorCenter),
                TipsUI.EventCamera,
                out var anchoredPos
            );

            anchoredPos += localOffset;
            return anchoredPos;
        }

        /// <summary>
        /// Keeps the tip on screen. Tips are authored with a fixed pivot (the cell readout grows
        /// down-right of the cursor), so near a canvas edge they run off it. Flipping to the
        /// opposite side of the anchor is preferred over sliding, which would drag the tip over
        /// the element it describes.
        /// </summary>
        void FitInsideCanvas(Vector2 pivot, Vector2 localOffset)
        {
            RectTransform tip = TipsUI.TipRect;

            // A content size fitter only resolves on the next layout pass, but the tip is
            // positioned in the frame it is shown, so force the size before measuring it.
            LayoutRebuilder.ForceRebuildLayoutImmediate(tip);

            Rect canvas = TipsUI.CanvasRect.rect;
            Vector2 size = tip.rect.size;
            Vector2 pos = tip.anchoredPosition;

            float minX = canvas.xMin + EDGE_PADDING;
            float maxX = canvas.xMax - EDGE_PADDING;
            float minY = canvas.yMin + EDGE_PADDING;
            float maxY = canvas.yMax - EDGE_PADDING;

            TryFlip(ref pos.x, ref pivot.x, localOffset.x, size.x, minX, maxX);
            TryFlip(ref pos.y, ref pivot.y, localOffset.y, size.y, minY, maxY);

            // Anything still overflowing (a tip wider than the gap on either side) slides in.
            if (size.x <= maxX - minX)
                pos.x = Mathf.Clamp(pos.x, minX + pivot.x * size.x, maxX - (1f - pivot.x) * size.x);
            if (size.y <= maxY - minY)
                pos.y = Mathf.Clamp(pos.y, minY + pivot.y * size.y, maxY - (1f - pivot.y) * size.y);

            tip.pivot = pivot;
            tip.anchoredPosition = pos;
        }

        // Mirrors the tip across its anchor on one axis, but only when that actually fits.
        static void TryFlip(
            ref float pos,
            ref float pivot,
            float offset,
            float size,
            float min,
            float max
        )
        {
            if (Fits(pos, pivot, size, min, max))
                return;

            float flippedPos = pos - 2f * offset;
            float flippedPivot = 1f - pivot;
            if (!Fits(flippedPos, flippedPivot, size, min, max))
                return;

            pos = flippedPos;
            pivot = flippedPivot;
        }

        static bool Fits(float pos, float pivot, float size, float min, float max) =>
            pos - pivot * size >= min && pos + (1f - pivot) * size <= max;
    }
}
