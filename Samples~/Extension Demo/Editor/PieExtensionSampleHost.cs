using UnityEditor;
using UnityEngine;

namespace Pie.Samples.Editor
{
    [InitializeOnLoad]
    public static class PieExtensionSampleHost
    {
        private static bool s_registered;

        static PieExtensionSampleHost()
        {
            EnsureRegistered();
        }

        [InitializeOnLoadMethod]
        private static void RegisterOnLoad()
        {
            EnsureRegistered();
        }

        public static void EnsureRegistered()
        {
            PieHostBridge.Register("read_runtime_flag", ReadRuntimeFlag);
            PieHostBridge.Register("echo_runtime_payload", EchoRuntimePayload);

            if (s_registered)
                return;

            s_registered = true;
            Debug.Log("[Pie Sample] Registered extension demo host handlers.");
        }

        private static string ReadRuntimeFlag(string argsJson)
        {
            return "{\"value\":\"editor-demo-ready\",\"args\":" + (string.IsNullOrEmpty(argsJson) ? "{}" : argsJson) + "}";
        }

        private static string EchoRuntimePayload(string argsJson)
        {
            return "{\"echo\":" + (string.IsNullOrEmpty(argsJson) ? "{}" : argsJson) + "}";
        }
    }
}
