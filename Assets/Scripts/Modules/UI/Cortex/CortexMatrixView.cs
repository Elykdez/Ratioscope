using Hypocycloid.Utils;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Hypocycloid.Ratioscope
{
    /// <summary>
    /// The Overall view is a multi-stage pipeline:
    ///     - a heat tensor renders to an RFloat RenderTexture (Sentis),
    ///     - a dedicated camera draws a mesh with that heat into an ARGBHalf target via a CommandBuffer
    ///     - a RawImage displays it.
    /// This owns the heat-grid texture and forwards stream telemetry into the off-screen matrix renderer.
    /// Attach to a RawImage; call Attach for each ChatStream.
    /// </summary>
    public sealed class CortexMatrixView
        : MonoBehaviour,
            IBeginDragHandler,
            IDragHandler,
            IEndDragHandler
    {
        static readonly TextureTransform HeatTextureTransform =
            new TextureTransform().SetCoordOrigin(CoordOrigin.BottomLeft);

        public CortexHeatGrid Grid { get; private set; }

        [SerializeField]
        RawImage image;

        [SerializeField]
        CortexMatrixVolume volume;

        public RawImage Image => image;
        public float FoldAmount => volume != null ? volume.FoldAmount : 0f;
        public bool IsFolded => volume != null && volume.IsFolded;

        /// <summary>The canvas render camera to use for screen-point math (null for Overlay).</summary>
        public Camera EventCamera => ResolveEventCamera();

        RenderTexture heatTexture;
        Tensor<float> heatTensor;
        ChatStream attached;
        Canvas canvas;
        CortexVisualizationSettings visualization;
        bool renderingSuppressed;

        // Heat state stays live via stream events and CPU decay so it is truthful when shown
        // again; only the GPU upload and the off-screen render are skipped while off screen.
        bool ShouldRender => image != null && image.isActiveAndEnabled && !renderingSuppressed;

        #region Unity Lifecycle

        void Awake()
        {
            if (image == null)
            {
                LogHelper.LogError("CortexMatrixView requires its authored RawImage reference.");
                enabled = false;
                return;
            }
            image.enabled = false;
            image.raycastTarget = false;
            if (volume == null || !volume.Initialize(image))
            {
                LogHelper.LogError(
                    "CortexMatrixView requires its authored volume renderer reference."
                );
                enabled = false;
                return;
            }
        }

        void Update()
        {
            if (heatTexture == null)
                return;
            Grid.Decay(Time.deltaTime);
            if (!ShouldRender)
                return;
            UploadHeat();
            volume.SetEntropy(
                Mathf.Clamp01(Grid.SmoothedEntropy * visualization.PaletteEntropyScale)
            );
        }

        void OnDestroy()
        {
            Detach();
            ReleaseHeatResources();
        }

        #endregion

        #region User Input

        public void SetFold(float value)
        {
            volume?.SetFold(value);
            if (image != null)
                image.raycastTarget = value > 0.5f;
        }

        public void FoldTo(bool is3D)
        {
            volume?.FoldTo(is3D);
            if (image != null)
                image.raycastTarget = is3D;
        }

        /// <summary>Pauses the GPU heat upload and off-screen render while the matrix is hidden
        /// (e.g. a full-screen overlay is open). Heat state keeps advancing so the chat and
        /// visualization stay in sync when it returns.</summary>
        public void SetRenderingSuppressed(bool value)
        {
            if (renderingSuppressed == value)
                return;
            renderingSuppressed = value;
            volume?.SetRenderingSuppressed(value);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (volume != null && volume.WantsPointerInput)
                volume.BeginDrag();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (volume != null && volume.IsDragging)
                volume.Drag(eventData.delta);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            volume?.EndDrag();
        }

        #endregion

        #region Setup

        public void Configure(CortexVisualizationSettings settings)
        {
            if (settings == null)
                throw new System.ArgumentNullException(nameof(settings));
            settings.Validate();
            visualization = settings;
            volume.Configure(settings);
        }

        public void Prepare(int transformerBlockCount)
        {
            if (visualization == null)
                throw new System.InvalidOperationException(
                    "CortexMatrixView must be configured from LlmSystemSettings before use."
                );
            RebuildGrid(transformerBlockCount);
        }

        #endregion

        #region Stream

        /// <summary>Routes a stream's telemetry into the grid (detaches any previous one).</summary>
        public void Attach(ChatStream stream)
        {
            Detach();
            if (visualization == null)
                throw new System.InvalidOperationException(
                    "CortexMatrixView must be configured from LlmSystemSettings before use."
                );
            attached = stream;
            RebuildGrid(stream.TransformerBlockCount);
            stream.ForwardEvaluated += OnForwardEvaluated;
            stream.TokenSampled += OnTokenSampled;
        }

        public void Detach()
        {
            if (attached == null)
                return;
            attached.ForwardEvaluated -= OnForwardEvaluated;
            attached.TokenSampled -= OnTokenSampled;
            attached = null;
        }

        void OnForwardEvaluated() => Grid.OnForward();

        void OnTokenSampled(TokenMetrics metrics)
        {
            Grid.OnToken(metrics);
        }

        #endregion

        #region Heat Grid

        void RebuildGrid(int transformerBlockCount)
        {
            Grid = new CortexHeatGrid(
                visualization.StageRows,
                transformerBlockCount,
                visualization.TokenRows,
                visualization.ForwardPulseHeat,
                visualization.HeatDecayRate,
                visualization.CandidateBaseHeat,
                visualization.EntropySmoothing
            );
            ReleaseHeatResources();
            RenderTextureDescriptor descriptor =
                new(Grid.Width, Grid.Height, RenderTextureFormat.RFloat, 0)
                {
                    enableRandomWrite = SystemInfo.supportsComputeShaders,
                    msaaSamples = 1,
                    useMipMap = false,
                    autoGenerateMips = false,
                    sRGB = false,
                };
            heatTexture = TextureManager.Ins.GetPersistentRenderTexture(
                "Cortex Heat",
                descriptor,
                FilterMode.Point
            );
            heatTensor = new Tensor<float>(
                new TensorShape(1, 1, Grid.Height, Grid.Width),
                clearOnInit: false
            );
            volume.Rebuild(heatTexture, Grid.Width, Grid.StructureRows, Grid.TokenRows);
            UploadHeat();
            image.enabled = true;

            // Diagnostics: confirm heat RT format support on this device.
            LogHelper.Log(
                $"[CortexHeat] size={Grid.Width}x{Grid.Height} "
                    + $"RFloatRT={SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RFloat)} "
                    + $"compute={SystemInfo.supportsComputeShaders} "
                    + $"created={heatTexture != null && heatTexture.IsCreated()} "
                    + $"fmt={(heatTexture != null ? heatTexture.format.ToString() : "null")}"
            );
        }

        void UploadHeat()
        {
            heatTensor.Upload(Grid.Heat);
            TextureConverter.RenderToTexture(heatTensor, heatTexture, HeatTextureTransform);
        }

        void ReleaseHeatResources()
        {
            volume?.ClearHeatTexture();
            heatTensor?.Dispose();
            heatTensor = null;
            if (heatTexture == null)
                return;
            if (image != null && image.texture == heatTexture)
                image.texture = null;
            TextureManager.ReleaseManaged(ref heatTexture);
        }

        #endregion

        #region Cell Query

        /// <summary>Maps a screen point inside the RawImage to grid cell data for tooltips.
        /// A null eventCamera resolves to the owning canvas's render camera, which the
        /// Screen Space - Camera canvas requires (passing null there maps incorrectly and
        /// the tooltip never appears).</summary>
        public bool TryGetCell(Vector2 screenPoint, Camera eventCamera, out CortexCellInfo info)
        {
            info = default;
            if (Grid == null || IsFolded)
                return false;
            eventCamera ??= ResolveEventCamera();
            RectTransform rect = (RectTransform)transform;
            if (
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rect,
                    screenPoint,
                    eventCamera,
                    out Vector2 local
                )
            )
                return false;

            Rect r = rect.rect;
            float u = (local.x - r.xMin) / r.width;
            float v = (local.y - r.yMin) / r.height;
            if (u < 0f || u > 1f || v < 0f || v > 1f)
                return false;

            info = Grid.GetCell((int)(u * Grid.Width), (int)(v * Grid.Height));
            return true;
        }

        // Overlay canvases map screen points with a null camera; Camera/World-space
        // canvases require their render camera. Resolved lazily since canvas assignment
        // can change after Awake.
        Camera ResolveEventCamera()
        {
            if (canvas == null)
                canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
                return null;
            Canvas root = canvas.rootCanvas;
            return root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        }

        #endregion
    }
}
