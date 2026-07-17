using UnityEngine;
using UnityEngine.UI;

namespace Hypocycloid.UI
{
    [AddComponentMenu("Layout/Ring Layout Group")]
    [ExecuteAlways]
    public class UIRingLayoutGroup : LayoutGroup
    {
        static readonly Vector2 Center = new(0.5f, 0.5f);

        [Header("Ring Settings")]
        [Tooltip("If true, radius expands so children keep the requested minimum spacing.")]
        [SerializeField]
        protected bool m_AutoExpandRadius;

        [Tooltip("The radius of the ring.")]
        [SerializeField]
        protected float m_Radius = 100f;

        [Tooltip("The angle (in degrees) where the first element is placed.")]
        [SerializeField, Range(-180f, 180f)]
        protected float m_StartAngle = 0f;

        [Tooltip("The angle (in degrees) between each element.")]
        [SerializeField, Range(0f, 180f)]
        protected float m_Spacing = 30f;

        [Tooltip("Minimum gap between neighbouring elements when Auto Expand Radius is enabled.")]
        [SerializeField]
        protected float m_MinChildSpacing = 12f;

        [Tooltip("If true, elements are evenly distributed along 360 degrees, overriding Spacing.")]
        [SerializeField]
        protected bool m_FillCircle = false;

        [Tooltip("If true, elements are placed clockwise.")]
        [SerializeField]
        protected bool m_Clockwise = true;

        // Public properties to allow access via code and ensure layout rebuilds on change
        public float Radius
        {
            get { return m_Radius; }
            set { SetProperty(ref m_Radius, value); }
        }
        public float StartAngle
        {
            get { return m_StartAngle; }
            set { SetProperty(ref m_StartAngle, value); }
        }
        public float Spacing
        {
            get { return m_Spacing; }
            set { SetProperty(ref m_Spacing, value); }
        }
        public bool FillCircle
        {
            get { return m_FillCircle; }
            set { SetProperty(ref m_FillCircle, value); }
        }
        public bool Clockwise
        {
            get { return m_Clockwise; }
            set { SetProperty(ref m_Clockwise, value); }
        }

        /// <summary>
        /// Gets or sets whether the radius expands to preserve spacing between children.
        /// </summary>
        public bool AutoExpandRadius
        {
            get { return m_AutoExpandRadius; }
            set { SetProperty(ref m_AutoExpandRadius, value); }
        }

        /// <summary>
        /// Gets or sets the minimum spacing between neighbouring children.
        /// </summary>
        public float MinChildSpacing
        {
            get { return m_MinChildSpacing; }
            set { SetProperty(ref m_MinChildSpacing, value); }
        }

        /// <summary>
        /// Called by the layout system to calculate the horizontal layout size.
        /// </summary>
        public override void CalculateLayoutInputHorizontal()
        {
            base.CalculateLayoutInputHorizontal();
            CalculateRadialLayout();
        }

        /// <summary>
        /// Called by the layout system to calculate the vertical layout size.
        /// </summary>
        public override void CalculateLayoutInputVertical()
        {
            CalculateRadialLayout();
        }

        /// <summary>
        /// Called by the layout system to set the children's positions.
        /// </summary>
        public override void SetLayoutHorizontal()
        {
            SetChildren();
        }

        public override void SetLayoutVertical()
        {
            SetChildren();
        }

        private void CalculateRadialLayout()
        {
            m_Tracker.Clear();

            if (rectChildren.Count == 0)
            {
                SetLayoutInputForAxis(padding.horizontal, padding.horizontal, -1, 0);
                SetLayoutInputForAxis(padding.vertical, padding.vertical, -1, 1);
                return;
            }

            float boundsRadius = GetEffectiveRadius() + (GetMaxChildDiagonal() * 0.5f);
            float minWidth = padding.horizontal + (boundsRadius * 2f);
            float minHeight = padding.vertical + (boundsRadius * 2f);

            SetLayoutInputForAxis(minWidth, minWidth, -1, 0);
            SetLayoutInputForAxis(minHeight, minHeight, -1, 1);
        }

        private void SetChildren()
        {
            m_Tracker.Clear();

            int count = rectChildren.Count;
            if (count == 0)
                return;

            float currentSpacing = m_Spacing;
            if (m_FillCircle)
                currentSpacing = 360f / count;

            float radius = GetEffectiveRadius();

            for (int i = 0; i < count; i++)
            {
                RectTransform child = rectChildren[i];
                m_Tracker.Add(
                    this,
                    child,
                    DrivenTransformProperties.Anchors
                        | DrivenTransformProperties.AnchoredPosition
                        | DrivenTransformProperties.Pivot
                );

                child.anchorMin = Center;
                child.anchorMax = Center;
                child.pivot = Center;

                float angleDegrees = m_StartAngle + (i * currentSpacing * (m_Clockwise ? -1f : 1f));
                float angleRadians = angleDegrees * Mathf.Deg2Rad;
                child.anchoredPosition = new Vector2(
                    Mathf.Cos(angleRadians) * radius,
                    Mathf.Sin(angleRadians) * radius
                );
            }
        }

        float GetEffectiveRadius()
        {
            if (!m_AutoExpandRadius || rectChildren.Count <= 1)
                return Mathf.Max(0f, m_Radius);

            float maxChildChord = GetMaxChildDiagonal() + Mathf.Max(0f, m_MinChildSpacing);
            float angleRadians = Mathf.PI / rectChildren.Count;
            float minRadius = maxChildChord / (2f * Mathf.Sin(angleRadians));

            return Mathf.Max(Mathf.Max(0f, m_Radius), minRadius);
        }

        float GetMaxChildDiagonal()
        {
            float maxDiagonal = 0f;
            for (int i = 0; i < rectChildren.Count; i++)
            {
                RectTransform child = rectChildren[i];
                float width = Mathf.Max(child.rect.width, LayoutUtility.GetPreferredWidth(child));
                float height = Mathf.Max(
                    child.rect.height,
                    LayoutUtility.GetPreferredHeight(child)
                );
                float diagonal = Mathf.Sqrt((width * width) + (height * height));
                maxDiagonal = Mathf.Max(maxDiagonal, diagonal);
            }

            return maxDiagonal;
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            SetDirty();

            if (Application.isPlaying)
                return;

            UnityEditor.EditorApplication.delayCall -= RebuildEditorPreview;
            UnityEditor.EditorApplication.delayCall += RebuildEditorPreview;
        }

        // Ensure editor static callback is deregistered to avoid keeping non-static callbacks alive
        protected override void OnDestroy()
        {
            UnityEditor.EditorApplication.delayCall -= RebuildEditorPreview;
            base.OnDestroy();
        }

        void RebuildEditorPreview()
        {
            if (this == null || !isActiveAndEnabled)
                return;

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
        }
#endif
    }
}
