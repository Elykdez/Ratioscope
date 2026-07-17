using Unity.InferenceEngine;

namespace Hypocycloid.Ratioscope
{
    public sealed class ChatServiceOptions
    {
        /// <summary>Serialized .sentis model file. Ignored when ModelAsset is set.</summary>
        public string ModelPath { get; set; }

        /// <summary>Imported model asset, preferred for player builds.</summary>
        public ModelAsset ModelAsset { get; set; }

        /// <summary>
        /// Optional one-token model with fixed-shape past/present K/V inputs. When present,
        /// ChatService retains the cache across turns instead of re-running the full window.
        /// </summary>
        public string DecodeModelPath { get; set; }

        public ModelAsset DecodeModelAsset { get; set; }

        /// <summary>
        /// Tokenizer binary written by Tools/LlmChat/export_llm_tokenizer.py. The default is
        /// StreamingAssets/Llm/llm_tokenizer.bin; ChatService loads it during Start/StartAsync.
        /// </summary>
        public string TokenizerPath { get; set; }

        /// <summary>
        /// GPUCompute by default for the 1.7B/2048 production artifact. The uint8 4B artifact
        /// saturates an 8 GiB card even at small contexts (weights plus per-schedule
        /// dequantize transients); use the CPU backend for the 4B options on such hardware.
        /// </summary>
        public BackendType Backend { get; set; } = BackendType.GPUCompute;

        /// <summary>Whether this checkpoint supports a visible thinking phase.</summary>
        public bool SupportsThinking { get; set; }

        /// <summary>
        /// Transformer block count for graphs that do not expose a per-block KV cache.
        /// Cached decode graphs are detected from their K/V input pairs instead.
        /// </summary>
        public int TransformerBlockCount { get; set; }

        /// <summary>
        /// Vocabulary the graph was exported with. Sentis does not expose output shapes, so
        /// this comes from the profile. ChatService rejects prompt ids outside it; leaving it
        /// unset (0) disables that check and lets a mismatched tokenizer crash the process.
        /// </summary>
        public int VocabularySize { get; set; }
    }
}
