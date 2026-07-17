using System;

namespace Hypocycloid.Ratioscope
{
    public sealed class ChatMessage
    {
        public string Role { get; }
        public string Content { get; }

        public ChatMessage(string role, string content)
        {
            if (role != "system" && role != "user" && role != "assistant")
                throw new ArgumentException(
                    "LLM chat role must be system, user, or assistant.",
                    nameof(role)
                );

            Role = role;
            Content = content ?? throw new ArgumentNullException(nameof(content));
        }

        public static ChatMessage System(string content) => new("system", content);

        public static ChatMessage User(string content) => new("user", content);

        public static ChatMessage Assistant(string content) => new("assistant", content);
    }
}
