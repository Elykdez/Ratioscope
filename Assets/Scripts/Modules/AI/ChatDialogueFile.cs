using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hypocycloid.Ratioscope
{
    public static class ChatDialogueFile
    {
        public const int CurrentVersion = 1;

        [Serializable]
        sealed class DialogueDocument
        {
            public int version;
            public DialogueMessage[] messages;
        }

        [Serializable]
        sealed class DialogueMessage
        {
            public string role;
            public string content;
        }

        public static string Serialize(IReadOnlyList<ChatMessage> messages)
        {
            if (messages == null)
                throw new ArgumentNullException(nameof(messages));

            DialogueDocument document =
                new()
                {
                    version = CurrentVersion,
                    messages = new DialogueMessage[messages.Count],
                };
            for (int i = 0; i < messages.Count; i++)
            {
                ChatMessage message = messages[i]
                    ?? throw new ArgumentException("Dialogue messages cannot contain null.");
                if (message.Role != "user" && message.Role != "assistant")
                    throw new ArgumentException(
                        "Dialogue files can only contain user and assistant messages."
                    );
                document.messages[i] =
                    new DialogueMessage { role = message.Role, content = message.Content };
            }

            return JsonUtility.ToJson(document, true);
        }

        public static bool TryDeserialize(
            string text,
            out List<ChatMessage> messages,
            out string error
        )
        {
            messages = null;
            error = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "Dialogue file is empty.";
                return false;
            }

            DialogueDocument document;
            try
            {
                document = JsonUtility.FromJson<DialogueDocument>(text);
            }
            catch (ArgumentException exception)
            {
                error = "Dialogue file is not valid JSON: " + exception.Message;
                return false;
            }

            if (document == null || document.version != CurrentVersion)
            {
                error = $"Unsupported dialogue version. Expected {CurrentVersion}.";
                return false;
            }
            if (document.messages == null)
            {
                error = "Dialogue file has no messages array.";
                return false;
            }

            messages = new List<ChatMessage>(document.messages.Length);
            for (int i = 0; i < document.messages.Length; i++)
            {
                DialogueMessage message = document.messages[i];
                if (message == null || (message.role != "user" && message.role != "assistant"))
                {
                    messages = null;
                    error = $"Dialogue message {i + 1} has an unsupported role.";
                    return false;
                }
                if (message.content == null)
                {
                    messages = null;
                    error = $"Dialogue message {i + 1} has no content.";
                    return false;
                }
                messages.Add(new ChatMessage(message.role, message.content));
            }
            return true;
        }
    }
}
