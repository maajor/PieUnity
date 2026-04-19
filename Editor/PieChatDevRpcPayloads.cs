using System;
using UnityEngine;

namespace Pie.Editor
{
    internal static class PieChatDevRpcPayloads
    {
        [Serializable]
        private sealed class DevChatStatePayload
        {
            public bool isOpen;
            public bool isInitialized;
            public bool isStreaming;
            public bool isBusy;
            public string statusText;
            public string inputText;
            public int messageCount;
            public int todoCount;
            public string activeSessionId;
            public int configAppliedVersion;
        }

        [Serializable]
        private sealed class DevChatMessagesPayload
        {
            public DevChatMessagePayload[] messages;
        }

        [Serializable]
        internal sealed class DevChatMessagePayload
        {
            public string role;
            public string title;
            public string content;
            public string summary;
            public bool isRunning;
            public bool isError;
        }

        public static string BuildStateJson(
            bool isOpen,
            bool isInitialized,
            bool isStreaming,
            bool isBusy,
            string statusText,
            string inputText,
            int messageCount,
            int todoCount,
            string activeSessionId,
            int configAppliedVersion)
        {
            return JsonUtility.ToJson(new DevChatStatePayload
            {
                isOpen = isOpen,
                isInitialized = isInitialized,
                isStreaming = isStreaming,
                isBusy = isBusy,
                statusText = statusText ?? "",
                inputText = inputText ?? "",
                messageCount = Math.Max(0, messageCount),
                todoCount = Math.Max(0, todoCount),
                activeSessionId = activeSessionId ?? "",
                configAppliedVersion = Math.Max(0, configAppliedVersion),
            });
        }

        public static string BuildMessagesJson(DevChatMessagePayload[] messages)
        {
            return JsonUtility.ToJson(new DevChatMessagesPayload
            {
                messages = messages ?? new DevChatMessagePayload[0],
            });
        }
    }
}
