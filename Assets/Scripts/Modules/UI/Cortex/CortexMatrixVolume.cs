using System;
using System.Collections.Generic;
using System.Globalization;
using Hypocycloid.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TextCore;
using UnityEngine.UI;

namespace Hypocycloid.Ratioscope
{
    /// <summary>
    /// Renders the cortex cell mesh through a dedicated prefab-authored camera into the existing RawImage.
    /// The same mesh carries its flat UV layout and folded column shape.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CortexMatrixVolume : MonoBehaviour
    {
        /// <summary>
        /// Symbols the layer rows scramble through. They are meaningless on purpose: a fixed
        /// label per layer reads as static text, a drifting field of symbols reads as work.
        /// Must not exceed CORTEX_GLYPH_CAPACITY in CortexUtils.cginc.
        /// </summary>
        public const string LayerGlyphSymbols = "+=!@#$%^&*";

        const int TokenLabelCharacterLimit = 3;
        const int CompactTokenLabelCharacterLimit = 1;

        /// <summary>
        /// Ceiling on symbol slots per layer cell. Past this the characters are too small to
        /// tell apart at any realistic panel size, however wide the cells get.
        /// </summary>
        const int MaxLayerGlyphSlots = 4;

        static readonly Vector2[] CellCorners =
        {
            new(0f, 0f),
            new(1f, 0f),
            new(1f, 1f),
            new(0f, 1f),
        };

        static readonly int MainTextureId = Shader.PropertyToID("_MainTex");
        static readonly int HeatTextureId = Shader.PropertyToID("_HeatTex");
        static readonly int ColumnsId = Shader.PropertyToID("_Cols");
        static readonly int RowsId = Shader.PropertyToID("_Rows");
        static readonly int TokenRowsId = Shader.PropertyToID("_TokenRows");
        static readonly int EntropyMixId = Shader.PropertyToID("_EntropyMix");
        static readonly int FoldId = Shader.PropertyToID("_Fold");
        static readonly int FoldStaggerId = Shader.PropertyToID("_FoldStagger");
        static readonly int YawId = Shader.PropertyToID("_Yaw");
        static readonly int PitchId = Shader.PropertyToID("_Pitch");
        static readonly int GlowIntensityId = Shader.PropertyToID("_GlowIntensity");
        static readonly int FlatYSignId = Shader.PropertyToID("_FlatYSign");
        static readonly int SurfaceOffsetId = Shader.PropertyToID("_SurfaceOffset");
        static readonly int LayerGlyphSlotsId = Shader.PropertyToID("_LayerGlyphSlots");
        static readonly int LayerGlyphAtlasId = Shader.PropertyToID("_LayerGlyphAtlas");
        static readonly int LayerGlyphRectsId = Shader.PropertyToID("_LayerGlyphRects");
        static readonly int LayerGlyphQuadsId = Shader.PropertyToID("_LayerGlyphQuads");
        static readonly int LayerGlyphCountId = Shader.PropertyToID("_LayerGlyphCount");
        static readonly int LayerGlyphGradientScaleId = Shader.PropertyToID(
            "_LayerGlyphGradientScale"
        );
        static readonly int LayerFlatCellAspectId = Shader.PropertyToID("_LayerFlatCellAspect");
        static readonly int LayerFoldedCellAspectId = Shader.PropertyToID("_LayerFoldedCellAspect");

        [SerializeField]
        Camera volumeCamera;

        /// <summary>
        /// Authored template for the cell material. Everything an artist can settle up front -
        /// palette, glow, fold stagger, glyph fill - lives on this asset where it can be tuned
        /// and previewed; the component clones it per instance and drives only the values that
        /// change with the data or the layout.
        /// </summary>
        [SerializeField]
        Material volumeMaterial;

        [SerializeField]
        TMP_Text tokenTextSource;

        /// <summary>Authored template for the token label material. See volumeMaterial.</summary>
        [SerializeField]
        Material tokenLabelMaterial;

        [field: SerializeField]
        public float CameraYOffset { get; private set; } = 1.05f;

        [field: SerializeField]
        public float CameraZOffset { get; private set; } = -4.75f;

        [field: SerializeField]
        public Color BackgroundColor { get; private set; } = new(0.02f, 0.035f, 0.03f, 1f);

        [field: SerializeField]
        public float ColumnHeight { get; private set; } = 2f;

        [field: SerializeField]
        public float FoldStagger { get; private set; } = 0.45f;

        [field: SerializeField]
        public float DragSensitivity { get; private set; } = 0.25f;

        [field: SerializeField, Range(-90f, 90f)]
        public float MaximumPitch { get; private set; } = 45f;

        [field: SerializeField]
        public float FoldEpsilon { get; private set; } = 0.001f;

        [field: SerializeField, Min(0.1f)]
        public float TokenLabelMaxCellWidth { get; private set; } = 0.84f;

        [field: SerializeField, Range(0.1f, 1f)]
        public float TokenLabelCellHeight { get; private set; } = 0.72f;

        [field: SerializeField, Min(0f)]
        public float TokenLabelSurfaceOffset { get; private set; } = 0.002f;

        RawImage outputImage;
        CortexVisualizationSettings settings;
        RenderTexture heatTexture;
        RenderTexture outputTexture;
        Material volumeInstance;
        Material labelInstance;
        Mesh cellMesh;
        Mesh tokenLabelMesh;
        CommandBuffer drawCommands;
        readonly Dictionary<string, TokenGlyphTemplate> tokenGlyphCache = new();
        readonly List<Vector3> tokenVertices = new();
        readonly List<Vector2> tokenSheetUvs = new();
        readonly List<Vector2> tokenHeatUvs = new();
        readonly List<Vector2> tokenAtlasUvs = new();
        readonly List<int> tokenIndices = new();
        int meshColumns;
        int meshRows;
        int meshStructureRows;
        CortexHeatGrid tokenLabelGrid;
        float foldAmount;
        float foldTarget;
        float automaticYaw;
        float userYaw;
        float userPitch;
        bool dragging;
        bool compactGlyphLayout;
        bool initialized;
        bool renderingSuppressed;

        sealed class TokenGlyphTemplate
        {
            public readonly Vector2[] Positions;
            public readonly Vector2[] AtlasUvs;
            public readonly float Width;

            public TokenGlyphTemplate(Vector2[] positions, Vector2[] atlasUvs, float width)
            {
                Positions = positions;
                AtlasUvs = atlasUvs;
                Width = width;
            }
        }

        static readonly TokenGlyphTemplate EmptyGlyphTemplate =
            new(Array.Empty<Vector2>(), Array.Empty<Vector2>(), 0f);

        public float FoldAmount => foldAmount;
        public bool IsFolded => foldTarget > FoldEpsilon || foldAmount > FoldEpsilon;
        public bool WantsPointerInput => foldTarget > 0.5f;
        public bool IsDragging => dragging;

        #region Unity Lifecycle

        void LateUpdate()
        {
            if (
                !initialized
                || renderingSuppressed
                || settings == null
                || heatTexture == null
                || cellMesh == null
                || outputImage == null
                || !outputImage.isActiveAndEnabled
            )
                return;

            EnsureOutputTexture();
            float step = Time.unscaledDeltaTime / settings.FoldDuration;
            foldAmount = Mathf.MoveTowards(foldAmount, foldTarget, step);
            if (foldAmount > FoldEpsilon && !dragging)
            {
                automaticYaw = Mathf.Repeat(
                    automaticYaw + settings.RotationSpeed * Time.unscaledDeltaTime,
                    360f
                );
            }

            float yaw = (automaticYaw + userYaw) * Mathf.Deg2Rad;
            float pitch = userPitch * Mathf.Deg2Rad;
            volumeInstance.SetFloat(FoldId, foldAmount);
            volumeInstance.SetFloat(YawId, yaw);
            volumeInstance.SetFloat(PitchId, pitch);
            labelInstance.SetFloat(FoldId, foldAmount);
            labelInstance.SetFloat(YawId, yaw);
            labelInstance.SetFloat(PitchId, pitch);

            // Rise above center as the fold completes: the higher vantage looks down onto
            // the token disk under the cylinder instead of viewing it edge-on.
            Vector3 cameraPosition = new(0f, CameraYOffset * foldAmount, CameraZOffset);
            volumeCamera.transform.SetPositionAndRotation(
                cameraPosition,
                Quaternion.LookRotation(-cameraPosition, Vector3.up)
            );
            volumeCamera.aspect = (float)outputTexture.width / outputTexture.height;
            volumeCamera.targetTexture = outputTexture;
            RenderVolume();
        }

        void OnDestroy()
        {
            ReleaseOutputTexture();
            drawCommands?.Release();
            if (volumeInstance != null)
                Destroy(volumeInstance);
            if (labelInstance != null)
                Destroy(labelInstance);
            if (cellMesh != null)
                Destroy(cellMesh);
            if (tokenLabelMesh != null)
                Destroy(tokenLabelMesh);
        }

        #endregion

        #region Setup

        public bool Initialize(RawImage image)
        {
            if (initialized)
                return true;
            if (
                image == null
                || volumeCamera == null
                || volumeMaterial == null
                || tokenTextSource == null
                || tokenTextSource.font == null
                || tokenTextSource.font.atlasTexture == null
                || tokenLabelMaterial == null
            )
            {
                LogHelper.LogError(
                    "CortexMatrixVolume requires its authored RawImage, camera, materials, and token text source references."
                );
                return false;
            }

            outputImage = image;
            outputImage.material = null;
            volumeCamera.enabled = false;
            volumeCamera.cullingMask = 0;
            volumeCamera.clearFlags = CameraClearFlags.SolidColor;
            volumeCamera.backgroundColor = BackgroundColor;
            volumeCamera.orthographic = false;
            volumeCamera.fieldOfView = 38f;
            volumeCamera.nearClipPlane = 0.1f;
            volumeCamera.farClipPlane = 20f;
            volumeCamera.allowHDR = true;

            // Clone rather than use the assets directly: the component writes per-instance state
            // into these every frame, and writing it into the shared asset would leak back into
            // the project files in the editor and bleed between instances at runtime.
            volumeInstance = new Material(volumeMaterial)
            {
                name = "Cortex Matrix Volume (Runtime)",
            };
            volumeInstance.SetFloat(FoldStaggerId, FoldStagger);
            UploadLayerGlyphs();
            labelInstance = new Material(tokenLabelMaterial)
            {
                name = "Cortex Token Labels (Runtime)",
            };
            labelInstance.SetTexture(MainTextureId, tokenTextSource.font.atlasTexture);
            labelInstance.SetFloat(FoldStaggerId, FoldStagger);
            labelInstance.SetFloat(SurfaceOffsetId, TokenLabelSurfaceOffset);
            tokenLabelMesh = new Mesh
            {
                name = "Cortex Token Labels",
                indexFormat = IndexFormat.UInt32,
            };
            tokenLabelMesh.MarkDynamic();
            drawCommands = new CommandBuffer { name = "Cortex Matrix Volume" };
            initialized = true;
            return true;
        }

        public void Configure(CortexVisualizationSettings visualizationSettings)
        {
            if (visualizationSettings == null)
                throw new ArgumentNullException(nameof(visualizationSettings));
            visualizationSettings.Validate();
            settings = visualizationSettings;
            volumeInstance?.SetFloat(GlowIntensityId, settings.GlowIntensity);
            labelInstance?.SetFloat(GlowIntensityId, settings.GlowIntensity);
        }

        public void Rebuild(RenderTexture sourceHeat, int columns, int structureRows, int tokenRows)
        {
            if (!initialized || settings == null)
                throw new InvalidOperationException(
                    "CortexMatrixVolume must be initialized and configured before rebuilding."
                );

            heatTexture = sourceHeat;
            meshColumns = columns;
            meshStructureRows = structureRows;
            meshRows = structureRows + tokenRows;
            if (cellMesh != null)
                Destroy(cellMesh);
            cellMesh = BuildCellMesh(
                columns,
                structureRows,
                tokenRows,
                settings.ColumnRadius,
                settings.HaloRadius,
                settings.HaloOffset
            );
            volumeInstance.SetTexture(MainTextureId, heatTexture);
            volumeInstance.SetFloat(ColumnsId, columns);
            volumeInstance.SetFloat(RowsId, meshRows);
            volumeInstance.SetFloat(TokenRowsId, tokenRows);
            volumeInstance.SetFloat(GlowIntensityId, settings.GlowIntensity);
            labelInstance.SetTexture(HeatTextureId, heatTexture);
            labelInstance.SetFloat(GlowIntensityId, settings.GlowIntensity);
            tokenLabelGrid = null;
            tokenLabelMesh.Clear();
            EnsureOutputTexture();
            UpdateLayerCellAspects();
            outputImage.enabled = true;
        }

        #endregion

        #region User Input

        public void SetFold(float value)
        {
            foldAmount = Mathf.Clamp01(value);
            foldTarget = foldAmount;
            if (foldTarget <= 0.5f)
                dragging = false;
        }

        public void FoldTo(bool is3D)
        {
            foldTarget = is3D ? 1f : 0f;
            if (!is3D)
                dragging = false;
        }

        public void BeginDrag()
        {
            if (WantsPointerInput)
                dragging = true;
        }

        public void Drag(Vector2 pointerDelta)
        {
            if (!dragging)
                return;
            userYaw = Mathf.Repeat(userYaw + pointerDelta.x * DragSensitivity, 360f);
            userPitch = Mathf.Clamp(
                userPitch - pointerDelta.y * DragSensitivity,
                -MaximumPitch,
                MaximumPitch
            );
        }

        public void EndDrag()
        {
            dragging = false;
        }

        #endregion

        #region Rendering

        void EnsureOutputTexture()
        {
            RectTransform rect = outputImage.rectTransform;
            float scale = outputImage.canvas != null ? outputImage.canvas.scaleFactor : 1f;
            int width = Mathf.Max(1, Mathf.CeilToInt(rect.rect.width * scale));
            int height = Mathf.Max(1, Mathf.CeilToInt(rect.rect.height * scale));
            if (
                outputTexture != null
                && outputTexture.width == width
                && outputTexture.height == height
            )
                return;

            ReleaseOutputTexture();
            outputTexture = TextureManager.Ins.GetPersistentRenderTexture(
                "Cortex Matrix Volume",
                width,
                height,
                RenderTextureFormat.ARGBHalf,
                24,
                FilterMode.Bilinear,
                QualitySettings.activeColorSpace == ColorSpace.Linear
            );
            volumeCamera.targetTexture = outputTexture;
            outputImage.texture = outputTexture;
            UpdateLayerCellAspects();
        }

        void RenderVolume()
        {
            drawCommands.Clear();
            Matrix4x4 cameraProjection = volumeCamera.projectionMatrix;
            // SetViewProjectionMatrices applies GL.GetGPUProjectionMatrix internally, so the
            // command buffer must receive the raw projection; pre-converting double-flips Y and
            // double-remaps Z (folded view upside down with broken depth). The GPU projection is
            // still derived here only to give the flat path its render-texture Y sign, since that
            // path emits clip coordinates directly and bypasses the projection matrix.
            Matrix4x4 gpuProjection = GL.GetGPUProjectionMatrix(cameraProjection, true);
            float flatYSign = Mathf.Sign(gpuProjection.m11 / cameraProjection.m11);
            volumeInstance.SetFloat(FlatYSignId, flatYSign);
            labelInstance.SetFloat(FlatYSignId, flatYSign);
            drawCommands.SetRenderTarget(outputTexture);
            drawCommands.SetViewport(new Rect(0f, 0f, outputTexture.width, outputTexture.height));
            drawCommands.ClearRenderTarget(true, true, BackgroundColor);
            drawCommands.SetViewProjectionMatrices(
                volumeCamera.worldToCameraMatrix,
                cameraProjection
            );
            drawCommands.DrawMesh(cellMesh, Matrix4x4.identity, volumeInstance);
            if (tokenLabelMesh.vertexCount > 0)
                drawCommands.DrawMesh(tokenLabelMesh, Matrix4x4.identity, labelInstance);
            Graphics.ExecuteCommandBuffer(drawCommands);
        }

        void ReleaseOutputTexture()
        {
            if (volumeCamera != null && volumeCamera.targetTexture == outputTexture)
                volumeCamera.targetTexture = null;
            if (outputImage != null && outputImage.texture == outputTexture)
                outputImage.texture = null;
            TextureManager.ReleaseManaged(ref outputTexture);
        }

        #endregion

        void UpdateLayerCellAspects()
        {
            if (
                volumeInstance == null
                || outputTexture == null
                || meshColumns < 1
                || meshRows < 1
                || meshStructureRows < 1
            )
                return;

            float flatAspect =
                (outputTexture.width / (float)meshColumns)
                / (outputTexture.height / (float)meshRows);
            float foldedAspect =
                (Mathf.PI * 2f * settings.ColumnRadius / meshColumns)
                / (ColumnHeight / meshStructureRows);
            volumeInstance.SetFloat(LayerFlatCellAspectId, flatAspect);
            volumeInstance.SetFloat(LayerFoldedCellAspectId, foldedAspect);

            // Slots per cell is whatever brings a slot's width closest to a row's height. A
            // fixed count leaves the slot pitch and the row pitch mismatched, and the symbols
            // read as per-cell clusters with a seam between them instead of one even grid of
            // characters. Rounding to the nearest square keeps the horizontal and vertical gaps
            // within half a slot of each other at any panel aspect.
            volumeInstance.SetFloat(
                LayerGlyphSlotsId,
                Mathf.Clamp(Mathf.RoundToInt(flatAspect), 1, MaxLayerGlyphSlots)
            );
            UpdateTokenLabelLayoutForAspect();
        }

        void UpdateTokenLabelLayoutForAspect()
        {
            bool compact = IsVerticalLayout(outputTexture.width, outputTexture.height);
            if (compactGlyphLayout == compact)
                return;

            compactGlyphLayout = compact;
            tokenGlyphCache.Clear();
            if (tokenLabelGrid != null)
                UpdateTokenLabels(tokenLabelGrid);
        }

        static bool IsVerticalLayout(int width, int height) => height > width;

        #region Layer Glyphs

        /// <summary>
        /// Resolves the layer symbols out of the token label font so both halves of the sheet
        /// draw the same typeface, and hands the shader everything it needs to sample them:
        /// the SDF atlas, one atlas UV rect and one placement quad per symbol, and the count.
        ///
        /// The quads carry each symbol's true metrics, normalised so the set's shared ink box
        /// spans 1 on its longer axis. That keeps their relative sizes and baseline positions
        /// intact - stretching each glyph to fill its own slot would make a "=" as tall as a
        /// "#" - while letting the shader place the box with a single aspect-correcting fit.
        ///
        /// Symbols the font cannot supply are skipped rather than fatal: the field reads the
        /// same with five symbols as with six.
        /// </summary>
        void UploadLayerGlyphs()
        {
            TMP_FontAsset font = tokenTextSource.font;
            // A static atlas already holds everything it will ever hold, and asking it to grow
            // only produces a warning.
            if (font.atlasPopulationMode == AtlasPopulationMode.Dynamic)
                font.TryAddCharacters(LayerGlyphSymbols);

            float em = font.faceInfo.pointSize;
            float padding = font.atlasPadding;
            Texture2D atlas = font.atlasTexture;
            Rect[] ink = new Rect[LayerGlyphSymbols.Length];
            Vector4[] rects = new Vector4[LayerGlyphSymbols.Length];
            Vector4[] quads = new Vector4[LayerGlyphSymbols.Length];
            int found = 0;
            for (int i = 0; i < LayerGlyphSymbols.Length; i++)
            {
                char symbol = LayerGlyphSymbols[i];
                if (
                    !font.characterLookupTable.TryGetValue(symbol, out TMP_Character character)
                    || character.glyph == null
                    // The token labels only ever draw material 0, so a symbol that landed on a
                    // spill-over atlas page would sample the wrong texture here.
                    || character.glyph.atlasIndex != 0
                )
                    continue;

                GlyphMetrics metrics = character.glyph.metrics;
                GlyphRect rect = character.glyph.glyphRect;
                if (metrics.width <= 0f || metrics.height <= 0f)
                    continue;

                int slot = found++;
                ink[slot] = new Rect(
                    metrics.horizontalBearingX / em,
                    (metrics.horizontalBearingY - metrics.height) / em,
                    metrics.width / em,
                    metrics.height / em
                );

                // Half a texel in from each padded edge, so bilinear taps never reach across
                // into whichever glyph TMP packed next door.
                float inset = 0.5f;
                rects[slot] = new Vector4(
                    (rect.x - padding + inset) / atlas.width,
                    (rect.y - padding + inset) / atlas.height,
                    (rect.width + padding * 2f - inset * 2f) / atlas.width,
                    (rect.height + padding * 2f - inset * 2f) / atlas.height
                );
            }

            if (found == 0)
            {
                LogHelper.LogWarning(
                    $"The token label font carries none of the layer symbols \"{LayerGlyphSymbols}\"; "
                        + "the layer rows will fall back to plain cell blocks."
                );
                volumeInstance.SetFloat(LayerGlyphCountId, 0f);
                return;
            }

            Rect union = ink[0];
            for (int i = 1; i < found; i++)
            {
                union.xMin = Mathf.Min(union.xMin, ink[i].xMin);
                union.yMin = Mathf.Min(union.yMin, ink[i].yMin);
                union.xMax = Mathf.Max(union.xMax, ink[i].xMax);
                union.yMax = Mathf.Max(union.yMax, ink[i].yMax);
            }

            float scale = 1f / Mathf.Max(union.width, union.height);
            Vector2 origin = union.center;
            float padded = padding / em;
            for (int i = 0; i < found; i++)
            {
                Rect box = ink[i];
                quads[i] = new Vector4(
                    (box.center.x - origin.x) * scale,
                    (box.center.y - origin.y) * scale,
                    (box.width * 0.5f + padded) * scale,
                    (box.height * 0.5f + padded) * scale
                );
            }

            volumeInstance.SetTexture(LayerGlyphAtlasId, atlas);
            volumeInstance.SetVectorArray(LayerGlyphRectsId, rects);
            volumeInstance.SetVectorArray(LayerGlyphQuadsId, quads);
            volumeInstance.SetFloat(LayerGlyphCountId, found);
            // TMP's own convention for the width of the distance ramp, in atlas texels.
            volumeInstance.SetFloat(LayerGlyphGradientScaleId, padding + 1f);
        }

        #endregion

        #region Token Labels

        /// <summary>
        /// Rebuilds the single batched glyph mesh for occupied token cells. TMP remains the
        /// source of glyph layout and atlas UVs; the resulting mesh follows the Cortex morph.
        /// </summary>
        public void UpdateTokenLabels(CortexHeatGrid grid)
        {
            if (!initialized || grid == null)
                return;

            tokenLabelGrid = grid;
            tokenVertices.Clear();
            tokenSheetUvs.Clear();
            tokenHeatUvs.Clear();
            tokenAtlasUvs.Clear();
            tokenIndices.Clear();

            int rows = grid.Height;
            for (int row = 0; row < grid.TokenRows; row++)
            {
                for (int column = 0; column < grid.Width; column++)
                {
                    CortexCellInfo cell = grid.GetCell(column, row);
                    if (string.IsNullOrEmpty(cell.TokenText))
                        continue;

                    TokenGlyphTemplate glyphs = GetTokenGlyphs(cell.TokenText);
                    if (glyphs.Positions.Length == 0)
                        continue;

                    float scale = Mathf.Min(
                        TokenLabelCellHeight,
                        TokenLabelMaxCellWidth / glyphs.Width
                    );
                    Vector2 heatUv = new((column + 0.5f) / grid.Width, (row + 0.5f) / rows);
                    int firstVertex = tokenVertices.Count;
                    for (int i = 0; i < glyphs.Positions.Length; i++)
                    {
                        Vector2 offset = glyphs.Positions[i] * scale;
                        float sheetU = (column + 0.5f + offset.x) / grid.Width;
                        float sheetV = (row + 0.5f + offset.y) / rows;
                        float tokenVertical = (row + 0.5f + offset.y) / grid.TokenRows;
                        Vector3 folded = CalculateFoldedPosition(
                            sheetU,
                            tokenVertical,
                            true,
                            settings.ColumnRadius,
                            settings.HaloRadius,
                            settings.HaloOffset
                        );
                        tokenVertices.Add(folded);
                        tokenSheetUvs.Add(new Vector2(sheetU, sheetV));
                        tokenHeatUvs.Add(heatUv);
                        tokenAtlasUvs.Add(glyphs.AtlasUvs[i]);
                    }

                    int glyphCount = glyphs.Positions.Length / 4;
                    for (int glyph = 0; glyph < glyphCount; glyph++)
                    {
                        int vertex = firstVertex + glyph * 4;
                        tokenIndices.Add(vertex);
                        tokenIndices.Add(vertex + 1);
                        tokenIndices.Add(vertex + 2);
                        tokenIndices.Add(vertex + 2);
                        tokenIndices.Add(vertex + 3);
                        tokenIndices.Add(vertex);
                    }
                }
            }

            tokenLabelMesh.Clear();
            tokenLabelMesh.SetVertices(tokenVertices);
            tokenLabelMesh.SetUVs(0, tokenSheetUvs);
            tokenLabelMesh.SetUVs(1, tokenHeatUvs);
            tokenLabelMesh.SetUVs(2, tokenAtlasUvs);
            tokenLabelMesh.SetTriangles(tokenIndices, 0, true);
        }

        TokenGlyphTemplate GetTokenGlyphs(string text)
        {
            if (tokenGlyphCache.TryGetValue(text, out TokenGlyphTemplate cached))
                return cached;

            string displayText = GetTokenDisplayText(
                text,
                compactGlyphLayout ? CompactTokenLabelCharacterLimit : TokenLabelCharacterLimit
            );
            if (displayText.Length == 0)
            {
                tokenGlyphCache.Add(text, EmptyGlyphTemplate);
                return EmptyGlyphTemplate;
            }

            TMP_TextInfo textInfo = tokenTextSource.GetTextInfo(displayText);
            int visibleCount = 0;
            Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new(float.NegativeInfinity, float.NegativeInfinity);
            for (int i = 0; i < textInfo.characterCount; i++)
            {
                TMP_CharacterInfo character = textInfo.characterInfo[i];
                if (!character.isVisible || character.materialReferenceIndex != 0)
                    continue;

                TMP_MeshInfo meshInfo = textInfo.meshInfo[0];
                for (int corner = 0; corner < 4; corner++)
                {
                    Vector3 vertex = meshInfo.vertices[character.vertexIndex + corner];
                    min = Vector2.Min(min, vertex);
                    max = Vector2.Max(max, vertex);
                }
                visibleCount++;
            }

            float height = max.y - min.y;
            if (visibleCount == 0 || height <= Mathf.Epsilon)
            {
                tokenGlyphCache.Add(text, EmptyGlyphTemplate);
                return EmptyGlyphTemplate;
            }

            Vector2 center = (min + max) * 0.5f;
            Vector2[] positions = new Vector2[visibleCount * 4];
            Vector2[] atlasUvs = new Vector2[positions.Length];
            int destination = 0;
            for (int i = 0; i < textInfo.characterCount; i++)
            {
                TMP_CharacterInfo character = textInfo.characterInfo[i];
                if (!character.isVisible || character.materialReferenceIndex != 0)
                    continue;

                TMP_MeshInfo meshInfo = textInfo.meshInfo[0];
                for (int corner = 0; corner < 4; corner++)
                {
                    int source = character.vertexIndex + corner;
                    positions[destination] = ((Vector2)meshInfo.vertices[source] - center) / height;
                    atlasUvs[destination] = meshInfo.uvs0[source];
                    destination++;
                }
            }

            TokenGlyphTemplate result = new(positions, atlasUvs, (max.x - min.x) / height);
            tokenGlyphCache.Add(text, result);
            return result;
        }

        static string GetTokenDisplayText(string text, int characterLimit)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            if (string.IsNullOrWhiteSpace(text))
                return "\u00B7";

            string trimmed = text.Trim();

            int[] characterStarts = StringInfo.ParseCombiningCharacters(trimmed);
            return characterStarts.Length <= characterLimit
                ? trimmed
                : trimmed.Substring(0, characterStarts[characterLimit]);
        }

        #endregion

        #region Mesh Construction

        Mesh BuildCellMesh(
            int columns,
            int structureRows,
            int tokenRows,
            float columnRadius,
            float haloRadius,
            float haloOffset
        )
        {
            if (columns < 1 || structureRows < 1 || tokenRows < 1)
                throw new ArgumentOutOfRangeException(
                    nameof(columns),
                    "Cortex mesh dimensions must be positive."
                );

            int rows = structureRows + tokenRows;
            int cellCount = checked(columns * rows);
            Vector3[] vertices = new Vector3[cellCount * 4];
            Vector2[] sheetUvs = new Vector2[vertices.Length];
            Vector2[] heatUvs = new Vector2[vertices.Length];
            Vector2[] cellUvs = new Vector2[vertices.Length];
            int[] indices = new int[cellCount * 6];

            int vertex = 0;
            int index = 0;
            for (int row = 0; row < rows; row++)
            {
                bool tokenCell = row < tokenRows;
                float verticalBase = tokenCell ? row : row - tokenRows;
                int verticalCount = tokenCell ? tokenRows : structureRows;
                for (int column = 0; column < columns; column++)
                {
                    Vector2 heatUv = new((column + 0.5f) / columns, (row + 0.5f) / rows);
                    int firstVertex = vertex;
                    for (int cornerIndex = 0; cornerIndex < CellCorners.Length; cornerIndex++)
                    {
                        Vector2 corner = CellCorners[cornerIndex];
                        Vector2 sheetUv =
                            new((column + corner.x) / columns, (row + corner.y) / rows);
                        float vertical = (verticalBase + corner.y) / verticalCount;

                        // Returns the 4 corner positions for each cell
                        // In this case, adjacent cells do not share vertices
                        vertices[vertex] = CalculateFoldedPosition(
                            sheetUv.x,
                            vertical,
                            tokenCell,
                            columnRadius,
                            haloRadius,
                            haloOffset
                        );

                        sheetUvs[vertex] = sheetUv;
                        heatUvs[vertex] = heatUv;
                        cellUvs[vertex] = corner;
                        vertex++;
                    }

                    indices[index++] = firstVertex;
                    indices[index++] = firstVertex + 1;
                    indices[index++] = firstVertex + 2;
                    indices[index++] = firstVertex;
                    indices[index++] = firstVertex + 2;
                    indices[index++] = firstVertex + 3;
                }
            }

            Mesh mesh =
                new()
                {
                    name = "Cortex Matrix Cells",
                    indexFormat = IndexFormat.UInt32,
                    vertices = vertices,
                    uv = sheetUvs,
                    uv2 = heatUvs,
                    uv3 = cellUvs,
                    triangles = indices,
                };
            mesh.RecalculateBounds();
            return mesh;
        }

        Vector3 CalculateFoldedPosition(
            float u,
            float vertical,
            bool tokenCell,
            float columnRadius,
            float haloRadius,
            float haloOffset
        )
        {
            float angle = u * Mathf.PI * 2f;

            //* INFO: Where the mesh vertices calculation happens.
            // Token rows flatten into a disk below the cylinder;
            // the sheet's bottom edge (vertical = 0) becomes the outer rim,
            // the row nearest the structure stays just outside the cylinder wall.
            float radius = tokenCell
                ? Mathf.Lerp(haloRadius, columnRadius * 1.04f, vertical)
                : columnRadius;
            float height = tokenCell
                ? -ColumnHeight * 0.5f - haloOffset
                : Mathf.Lerp(-ColumnHeight * 0.5f, ColumnHeight * 0.5f, vertical);
            return new Vector3(Mathf.Sin(angle) * radius, height, Mathf.Cos(angle) * radius);
        }

        #endregion

        #region State Updates

        public void SetEntropy(float entropyMix)
        {
            volumeInstance?.SetFloat(EntropyMixId, entropyMix);
            labelInstance?.SetFloat(EntropyMixId, entropyMix);
        }

        /// <summary>Stops the off-screen camera render while the matrix is not on screen.</summary>
        public void SetRenderingSuppressed(bool value)
        {
            renderingSuppressed = value;
        }

        public void ClearHeatTexture()
        {
            heatTexture = null;
            volumeInstance?.SetTexture(MainTextureId, null);
            labelInstance?.SetTexture(HeatTextureId, null);
        }

        #endregion
    }
}
