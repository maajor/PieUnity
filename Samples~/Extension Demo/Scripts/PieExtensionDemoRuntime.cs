using UnityEngine;
using System;

namespace Pie.Samples
{
    /// <summary>
    /// Optional runtime-side host bridge registration for Play Mode / Player tests.
    /// </summary>
    public class PieExtensionDemoRuntime : MonoBehaviour
    {
        [SerializeField] private string runtimeFlagValue = "runtime-demo-ready";
        private Func<string, string> _previousReadRuntimeFlag;
        private Func<string, string> _previousEchoRuntimePayload;
        private bool _ownsReadRuntimeFlag;
        private bool _ownsEchoRuntimePayload;

        private void Awake()
        {
            PieHostBridge.TryGetHandler("read_runtime_flag", out _previousReadRuntimeFlag);
            PieHostBridge.TryGetHandler("echo_runtime_payload", out _previousEchoRuntimePayload);
            PieHostBridge.Register("read_runtime_flag", HandleReadRuntimeFlag);
            PieHostBridge.Register("echo_runtime_payload", HandleEchoRuntimePayload);
            _ownsReadRuntimeFlag = true;
            _ownsEchoRuntimePayload = true;
        }

        private void OnDestroy()
        {
            if (_ownsReadRuntimeFlag)
            {
                if (_previousReadRuntimeFlag != null)
                    PieHostBridge.Register("read_runtime_flag", _previousReadRuntimeFlag);
                else
                    PieHostBridge.Unregister("read_runtime_flag");

                _ownsReadRuntimeFlag = false;
            }

            if (_ownsEchoRuntimePayload)
            {
                if (_previousEchoRuntimePayload != null)
                    PieHostBridge.Register("echo_runtime_payload", _previousEchoRuntimePayload);
                else
                    PieHostBridge.Unregister("echo_runtime_payload");

                _ownsEchoRuntimePayload = false;
            }
        }

        private string HandleReadRuntimeFlag(string argsJson)
        {
            return "{\"value\":\"" + EscapeJson(runtimeFlagValue) + "\",\"args\":" + (string.IsNullOrEmpty(argsJson) ? "{}" : argsJson) + "}";
        }

        private string HandleEchoRuntimePayload(string argsJson)
        {
            return "{\"echo\":" + (string.IsNullOrEmpty(argsJson) ? "{}" : argsJson) + "}";
        }

        private static string EscapeJson(string value)
        {
            return (value ?? "")
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }
    }
}
