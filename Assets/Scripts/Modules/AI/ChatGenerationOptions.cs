namespace Hypocycloid.Ratioscope
{
    public sealed class ChatGenerationOptions
    {
        public int MaxNewTokens { get; set; } = 64;

        /// <summary>Zero or below selects greedy (argmax) decoding.</summary>
        public float Temperature { get; set; } = 1f;
        public float TopP { get; set; } = 0.95f;
        public int TopK { get; set; } = 64;

        /// <summary>
        /// Enables the thinking phase when the selected model supports it. Disable this for
        /// utility generations such as context compaction where only final content is useful.
        /// </summary>
        public bool EnableThinking { get; set; } = true;
    }
}
