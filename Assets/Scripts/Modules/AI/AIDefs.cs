using System;
using System.ComponentModel;
using System.IO;
using Unity.InferenceEngine;
using UnityEngine;

namespace Hypocycloid.Ratioscope
{
    public enum ChatStreamState
    {
        Thinking,
        Completed,
        Cancelled,
        Faulted,
    }

    /// <summary>Why generation stopped. Only StopToken is a model-signalled completion.</summary>
    public enum ChatFinishReason
    {
        StopToken,
        TokenLimit,
        ContextLimit,
        Cancelled,
    }

    public enum StreamPurpose
    {
        Reply,
        Compaction,
    }

    /// <summary>
    /// Shipping model/backend presets. Contexts are fixed at export time, so each option
    /// maps to one decode artifact in Assets/StreamingAssets/Sentis.
    /// </summary>
    public enum LlmSystemOption
    {
        [Description("Test-only tiny random-weight model")]
        Tiny = -1,

        [Description("Qwen3-1.7B decode graph, 2048 window, GPUCompute")]
        Gpu1_7B_2048 = 0,

        [Description("Qwen3-1.7B decode graph, 2048 window, CPU")]
        Cpu1_7B_2048 = 1,

        [Description("Qwen3-4B decode graph, 4096 window, GPUCompute")]
        Gpu4B_4096 = 2,

        [Description("Qwen3-4B decode graph, 4096 window, CPU")]
        Cpu4B_4096 = 3,
    }

    [Serializable]
    public sealed class LlmModelConfiguration
    {
        [SerializeField]
        string modelFileName;

        [SerializeField]
        string decodeModelFileName;

        [SerializeField]
        BackendType backend;

        [SerializeField]
        bool supportsThinking;

        [SerializeField, Min(1)]
        int transformerBlockCount;

        [SerializeField, Min(1)]
        [Tooltip("Vocabulary the artifact was exported with; guards against tokenizer mismatch.")]
        int vocabularySize;

        public string ModelFileName => modelFileName;
        public string DecodeModelFileName => decodeModelFileName;
        public BackendType Backend => backend;
        public bool SupportsThinking => supportsThinking;
        public int TransformerBlockCount => transformerBlockCount;
        public int VocabularySize => vocabularySize;

        internal ChatServiceOptions CreateOptions(string modelDirectory, string tokenizerPath)
        {
            if (transformerBlockCount <= 0)
                throw new InvalidDataException("Transformer block count must be configured.");
            if (vocabularySize <= 0)
                throw new InvalidDataException("Model vocabulary size must be configured.");
            if (string.IsNullOrEmpty(modelFileName) && string.IsNullOrEmpty(decodeModelFileName))
                throw new InvalidDataException("At least one model artifact must be configured.");

            return new ChatServiceOptions
            {
                ModelPath = LlmModelCatalog.ResolvePath(modelDirectory, modelFileName),
                DecodeModelPath = LlmModelCatalog.ResolvePath(modelDirectory, decodeModelFileName),
                TokenizerPath = tokenizerPath,
                Backend = backend,
                SupportsThinking = supportsThinking,
                TransformerBlockCount = transformerBlockCount,
                VocabularySize = vocabularySize,
            };
        }
    }

    [Serializable]
    public sealed class LlmSystemProfile
    {
        [SerializeField]
        LlmSystemOption option;

        [SerializeField]
        LlmModelConfiguration model;

        public LlmSystemOption Option => option;
        public LlmModelConfiguration Model => model;
    }

    [Serializable]
    public sealed class CortexVisualizationSettings
    {
        [SerializeField, Min(CortexHeatGrid.StageCount)]
        int stageRows;

        [SerializeField, Min(1)]
        int tokenRows;

        [SerializeField, Range(0f, 1f)]
        float forwardPulseHeat;

        [SerializeField, Min(0f)]
        float heatDecayRate;

        [SerializeField, Range(0f, 1f)]
        float candidateBaseHeat;

        [SerializeField, Min(0f)]
        float paletteEntropyScale;

        [SerializeField, Range(0f, 1f)]
        float entropySmoothing;

        [Header("3D Morph")]
        [SerializeField, Min(0.01f)]
        float foldDuration;

        [SerializeField, Min(0.01f)]
        float columnRadius;

        [SerializeField, Min(0.01f)]
        float haloRadius;

        [SerializeField, Min(0f)]
        float haloOffset;

        [SerializeField, Min(0f)]
        float rotationSpeed;

        [SerializeField, Min(0f)]
        float glowIntensity;

        public int StageRows => stageRows;
        public int TokenRows => tokenRows;
        public float ForwardPulseHeat => forwardPulseHeat;
        public float HeatDecayRate => heatDecayRate;
        public float CandidateBaseHeat => candidateBaseHeat;
        public float PaletteEntropyScale => paletteEntropyScale;
        public float EntropySmoothing => entropySmoothing;
        public float FoldDuration => foldDuration;
        public float ColumnRadius => columnRadius;
        public float HaloRadius => haloRadius;
        public float HaloOffset => haloOffset;
        public float RotationSpeed => rotationSpeed;
        public float GlowIntensity => glowIntensity;

        internal void Validate()
        {
            if (stageRows < CortexHeatGrid.StageCount)
                throw new InvalidDataException(
                    $"Cortex stage rows must be at least {CortexHeatGrid.StageCount}."
                );
            if (tokenRows < 1)
                throw new InvalidDataException("Cortex token rows must be positive.");
            if (forwardPulseHeat < 0f || forwardPulseHeat > 1f)
                throw new InvalidDataException("Cortex forward pulse heat must be in [0, 1].");
            if (heatDecayRate < 0f)
                throw new InvalidDataException("Cortex heat decay rate cannot be negative.");
            if (candidateBaseHeat < 0f || candidateBaseHeat > 1f)
                throw new InvalidDataException("Cortex candidate base heat must be in [0, 1].");
            if (paletteEntropyScale < 0f)
                throw new InvalidDataException("Cortex palette entropy scale cannot be negative.");
            if (entropySmoothing < 0f || entropySmoothing > 1f)
                throw new InvalidDataException("Cortex entropy smoothing must be in [0, 1].");
            if (foldDuration <= 0f)
                throw new InvalidDataException("Cortex fold duration must be positive.");
            if (columnRadius <= 0f)
                throw new InvalidDataException("Cortex column radius must be positive.");
            if (haloRadius < columnRadius)
                throw new InvalidDataException(
                    "Cortex halo radius must be at least the column radius."
                );
            if (haloOffset < 0f)
                throw new InvalidDataException("Cortex halo offset cannot be negative.");
            if (rotationSpeed < 0f)
                throw new InvalidDataException("Cortex rotation speed cannot be negative.");
            if (glowIntensity < 0f)
                throw new InvalidDataException("Cortex glow intensity cannot be negative.");
        }
    }
}
