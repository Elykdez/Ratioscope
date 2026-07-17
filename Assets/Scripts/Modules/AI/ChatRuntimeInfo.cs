using Unity.InferenceEngine;

namespace Hypocycloid.Ratioscope
{
    public sealed class ChatRuntimeInfo
    {
        public string ModelSource { get; internal set; }
        public BackendType Backend { get; internal set; }
        public int ContextLength { get; internal set; }
        public int TransformerBlockCount { get; internal set; }
        public int VocabularySize { get; internal set; }
        public double LoadSeconds { get; internal set; }
    }
}
