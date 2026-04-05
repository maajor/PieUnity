using System;
using UnityEngine;

namespace Pie
{
    [Serializable]
    public class PieInteractionRequest
    {
        public string type;
        public string id;
        public string message;
        public string level;
        public string prompt;
        public string detail;
        public string placeholder;
        public string prefill;
        public string[] options;
        public int timeoutMs;
    }

    [Serializable]
    public class PieInteractionResponse
    {
        public string type;
        public bool acknowledged;
        public string id;
        public string selection;
        public bool confirmed;
        public string value;
        public bool unavailable;
        public string message;
        public bool timedOut;
        public bool skipped;

        public static string ToJson(PieInteractionResponse response)
        {
            return JsonUtility.ToJson(response ?? new PieInteractionResponse());
        }

        public static string Unavailable(string type, string id, string message)
        {
            return ToJson(new PieInteractionResponse
            {
                type = type,
                id = id,
                unavailable = true,
                message = message,
            });
        }
    }
}
