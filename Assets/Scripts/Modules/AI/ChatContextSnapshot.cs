using System;
using System.Collections.Generic;

namespace Hypocycloid.Ratioscope
{
    /// <summary>The exact prompt retained for the next model turn.</summary>
    public sealed class ChatContextSnapshot
    {
        readonly ChatMessage[] messages;
        readonly ChatMessage[] removedMessages;
        readonly int[] promptIds;

        public IReadOnlyList<ChatMessage> Messages => messages;
        public IReadOnlyList<ChatMessage> RemovedMessages => removedMessages;
        public IReadOnlyList<int> PromptIds => promptIds;
        public string PromptText { get; }
        public int PromptTokens => promptIds.Length;
        public int WindowCapacity { get; }
        public int RequestedReplyTokens { get; }
        public int AvailableReplyTokens => Math.Max(0, WindowCapacity - PromptTokens);
        public bool WasTrimmed => removedMessages.Length > 0;

        internal ChatContextSnapshot(
            IReadOnlyList<ChatMessage> messages,
            IReadOnlyList<ChatMessage> removedMessages,
            string promptText,
            IReadOnlyList<int> promptIds,
            int windowCapacity,
            int requestedReplyTokens
        )
        {
            this.messages = Copy(messages);
            this.removedMessages = Copy(removedMessages);
            this.promptIds = new int[promptIds.Count];
            for (int i = 0; i < promptIds.Count; i++)
                this.promptIds[i] = promptIds[i];

            PromptText = promptText ?? "";
            WindowCapacity = windowCapacity;
            RequestedReplyTokens = requestedReplyTokens;
        }

        static ChatMessage[] Copy(IReadOnlyList<ChatMessage> source)
        {
            ChatMessage[] copy = new ChatMessage[source.Count];
            for (int i = 0; i < source.Count; i++)
                copy[i] = source[i];
            return copy;
        }
    }
}
