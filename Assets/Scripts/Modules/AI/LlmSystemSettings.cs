using System.IO;
using Hypocycloid.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hypocycloid.Ratioscope
{
    [CreateAssetMenu(fileName = "LlmSystemSettings", menuName = "Hypocycloid/LLM System Settings")]
    public sealed class LlmSystemSettings : ScriptableObject
    {
        const float MegabytesPerGiB = 1024f;

        [SerializeField]
        string tokenizerRelativePath;

        [SerializeField]
        string sentisRelativeDirectory;

        [SerializeField]
        string modelManifestRelativePath = "config/models.json";

        [SerializeField]
        LlmSystemProfile[] profiles;

        [SerializeField]
        CortexVisualizationSettings cortexVisualization;

        [SerializeField, Min(0f)]
        [Tooltip(
            "Reserved before choosing a model, since Unity reports total rather than free VRAM."
        )]
        float vramReserveGiB;

        [SerializeField, Min(0f)]
        [Tooltip("Below this usable VRAM, inference switches to the CPU.")]
        float minimumGpuVramGiB;

        [SerializeField, Min(0f)]
        [Tooltip("Below this usable VRAM, the smaller 1.7B model is selected.")]
        float minimum4BVramGiB;

        [SerializeField]
        LlmSystemOption cpuFallbackOption;

        [SerializeField]
        LlmSystemOption midVramOption;

        [SerializeField]
        LlmSystemOption highVramOption;

        public float VramReserveGiB => vramReserveGiB;
        public float MinimumGpuVramGiB => minimumGpuVramGiB;
        public float Minimum4BVramGiB => minimum4BVramGiB;
        public CortexVisualizationSettings CortexVisualization => cortexVisualization;

        public string ModelManifestPath =>
            Path.Combine(RuntimeDataPaths.StreamingRoot, modelManifestRelativePath);

        public LlmModelManifest LoadManifest()
        {
            if (string.IsNullOrWhiteSpace(modelManifestRelativePath))
                throw new InvalidDataException("The LLM model manifest path is not configured.");
            if (!File.Exists(ModelManifestPath))
                throw new FileNotFoundException(
                    "The LLM model manifest was not found.",
                    ModelManifestPath
                );

            LlmModelManifest manifest = JsonUtility.FromJson<LlmModelManifest>(
                File.ReadAllText(ModelManifestPath)
            );
            if (manifest == null)
                throw new InvalidDataException("The LLM model manifest could not be parsed.");
            return manifest;
        }

        public bool IsModelAvailable(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return false;
            return File.Exists(LlmModelCatalog.LocalPath(fileName))
                || File.Exists(
                    Path.Combine(RuntimeDataPaths.StreamingRoot, sentisRelativeDirectory, fileName)
                );
        }

        /// <summary>
        /// True when an artifact the runtime can start from exists on disk. Mirrors
        /// ChatService.StartAsync, which prefers the decode graph and falls back to the
        /// text graph, so either file makes the option loadable.
        /// </summary>
        public bool IsOptionAvailable(LlmSystemOption option)
        {
            LlmModelConfiguration model = FindModel(option);
            return model != null
                && (
                    IsModelAvailable(model.DecodeModelFileName)
                    || IsModelAvailable(model.ModelFileName)
                );
        }

        public string GetPreferredArtifactFileName(LlmSystemOption option)
        {
            LlmModelConfiguration model = FindModel(option);
            if (model == null)
                throw new InvalidDataException($"No LLM profile is configured for {option}.");
            return !string.IsNullOrEmpty(model.DecodeModelFileName)
                ? model.DecodeModelFileName
                : model.ModelFileName;
        }

        public ChatServiceOptions CreateServiceOptions(LlmSystemOption option)
        {
            LlmModelConfiguration model = FindModel(option);
            if (model == null)
                throw new InvalidDataException($"No LLM profile is configured for {option}.");
            return CreateOptions(model);
        }

        LlmModelConfiguration FindModel(LlmSystemOption option)
        {
            if (profiles == null)
                return null;
            foreach (LlmSystemProfile profile in profiles)
            {
                if (profile != null && profile.Option == option)
                    return profile.Model;
            }
            return null;
        }

        ChatServiceOptions CreateOptions(LlmModelConfiguration model)
        {
            if (model == null)
                throw new InvalidDataException("The LLM model profile is missing.");
            if (string.IsNullOrEmpty(tokenizerRelativePath))
                throw new InvalidDataException("The tokenizer relative path is not configured.");
            if (string.IsNullOrEmpty(sentisRelativeDirectory))
                throw new InvalidDataException("The Sentis model directory is not configured.");
            return model.CreateOptions(
                Path.Combine(RuntimeDataPaths.StreamingRoot, sentisRelativeDirectory),
                Path.Combine(RuntimeDataPaths.StreamingRoot, tokenizerRelativePath)
            );
        }

        public LlmSystemOption SelectForCurrentSystem()
        {
#if !UNITY_STANDALONE && !UNITY_EDITOR
            // Android uses the configured CPU fallback and downloads its graph after install.
            return cpuFallbackOption;
#else
            bool hasGpu = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            float usableVramGiB = Mathf.Max(
                0f,
                SystemInfo.graphicsMemorySize / MegabytesPerGiB - vramReserveGiB
            );
            return SelectForUsableVram(usableVramGiB, hasGpu);
#endif
        }

        public LlmSystemOption SelectForUsableVram(float usableVramGiB, bool hasGpu = true)
        {
            if (!hasGpu || usableVramGiB < minimumGpuVramGiB)
                return cpuFallbackOption;
            if (usableVramGiB < minimum4BVramGiB)
                return midVramOption;
            return highVramOption;
        }
    }
}
