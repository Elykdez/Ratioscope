namespace Hypocycloid.Ratioscope
{
    public sealed class ChatResult
    {
        public string Content { get; }
        public string Thinking { get; }

        /// <summary>Generated text before channel parsing, special tokens included.</summary>
        public string RawText { get; }
        public int PromptTokens { get; }
        public int GeneratedTokens { get; }
        public double ElapsedSeconds { get; }
        public ChatFinishReason FinishReason { get; }

        /// <summary>True only when the model emitted an end-of-turn/end-of-text token.</summary>
        public bool IsComplete => FinishReason == ChatFinishReason.StopToken;

        internal ChatResult(
            string content,
            string thinking,
            string rawText,
            int promptTokens,
            int generatedTokens,
            double elapsedSeconds,
            ChatFinishReason finishReason
        )
        {
            Content = content ?? "";
            Thinking = thinking ?? "";
            RawText = rawText ?? "";
            PromptTokens = promptTokens;
            GeneratedTokens = generatedTokens;
            ElapsedSeconds = elapsedSeconds;
            FinishReason = finishReason;
        }
    }
}
