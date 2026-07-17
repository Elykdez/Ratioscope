using System.Collections.Generic;
using System.Text;

namespace Hypocycloid.Ratioscope
{
    /// <summary>
    /// Builds the ChatML prompt string for system/user/assistant conversations, mirroring
    /// the chat_template in tokenizer_config.json of the Qwen3-family checkpoints
    /// (tool calling is not supported). Message content is used verbatim, exactly like
    /// the reference template. Tokenize the result with
    /// LlmTokenizer.EncodeWithSpecialTokens.
    /// </summary>
    public static class ChatTemplate
    {
        public static string BuildPromptText(
            IReadOnlyList<ChatMessage> messages,
            bool appendEmptyThinkBlock = false
        )
        {
            StringBuilder prompt = new(256);
            for (int i = 0; i < messages.Count; i++)
            {
                ChatMessage message = messages[i];
                // The reference template only emits system, user, and assistant roles.
                if (
                    message.Role != "system"
                    && message.Role != "user"
                    && message.Role != "assistant"
                )
                    continue;
                prompt.Append("<|im_start|>");
                prompt.Append(message.Role);
                prompt.Append('\n');
                prompt.Append(message.Content);
                prompt.Append("<|im_end|>\n");
            }

            prompt.Append("<|im_start|>assistant\n");
            // Hybrid thinking checkpoints (Qwen3-1.7B) open a <think> block unless the
            // template pre-closes it; this mirrors apply_chat_template(enable_thinking=False).
            if (appendEmptyThinkBlock)
                prompt.Append("<think>\n\n</think>\n\n");
            return prompt.ToString();
        }
    }
}
