using System;
using Hypocycloid.Utils;
using UnityEngine;
using UnityEngine.Rendering;
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
        static readonly Vector2[] CellCorners =
        {
            new(0f, 0f),
            new(1f, 0f),
            new(1f, 1f),
            new(0f, 1f),
        };

        static readonly int MainTextureId = Shader.PropertyToID("_MainTex");
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

        [SerializeField]
        Camera volumeCamera;

        [SerializeField]
        Shader volumeShader;

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

        RawImage outputImage;
        CortexVisualizationSettings settings;
        RenderTexture heatTexture;
        RenderTexture outputTexture;
        Material material;
        Mesh cellMesh;
        CommandBuffer drawCommands;
        float foldAmount;
        float foldTarget;
        float automaticYaw;
        float userYaw;
        float userPitch;
        bool dragging;
        bool initialized;
        bool renderingSuppressed;

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

            material.SetFloat(FoldId, foldAmount);
            material.SetFloat(YawId, (automaticYaw + userYaw) * Mathf.Deg2Rad);
            material.SetFloat(PitchId, userPitch * Mathf.Deg2Rad);

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
            if (material != null)
                Destroy(material);
            if (cellMesh != null)
                Destroy(cellMesh);
        }

        #endregion

        #region Setup

        public bool Initialize(RawImage image)
        {
            if (initialized)
                return true;
            if (image == null || volumeCamera == null || volumeShader == null)
            {
                LogHelper.LogError(
                    "CortexMatrixVolume requires its authored RawImage, camera, and shader references."
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

            material = new Material(volumeShader) { name = "Cortex Matrix Volume (Runtime)" };
            material.SetFloat(FoldStaggerId, FoldStagger);
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
            material?.SetFloat(GlowIntensityId, settings.GlowIntensity);
        }

        public void Rebuild(RenderTexture sourceHeat, int columns, int structureRows, int tokenRows)
        {
            if (!initialized || settings == null)
                throw new InvalidOperationException(
                    "CortexMatrixVolume must be initialized and configured before rebuilding."
                );

            heatTexture = sourceHeat;
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
            material.SetTexture(MainTextureId, heatTexture);
            material.SetFloat(ColumnsId, columns);
            material.SetFloat(RowsId, structureRows + tokenRows);
            material.SetFloat(TokenRowsId, tokenRows);
            material.SetFloat(GlowIntensityId, settings.GlowIntensity);
            EnsureOutputTexture();
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
            material.SetFloat(FlatYSignId, Mathf.Sign(gpuProjection.m11 / cameraProjection.m11));
            drawCommands.SetRenderTarget(outputTexture);
            drawCommands.SetViewport(new Rect(0f, 0f, outputTexture.width, outputTexture.height));
            drawCommands.ClearRenderTarget(true, true, BackgroundColor);
            drawCommands.SetViewProjectionMatrices(
                volumeCamera.worldToCameraMatrix,
                cameraProjection
            );
            drawCommands.DrawMesh(cellMesh, Matrix4x4.identity, material);
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
            material?.SetFloat(EntropyMixId, entropyMix);
        }

        /// <summary>Stops the off-screen camera render while the matrix is not on screen.</summary>
        public void SetRenderingSuppressed(bool value)
        {
            renderingSuppressed = value;
        }

        public void ClearHeatTexture()
        {
            heatTexture = null;
            material?.SetTexture(MainTextureId, null);
        }

        #endregion
    }
}
