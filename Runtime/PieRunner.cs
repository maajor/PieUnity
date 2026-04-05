using System;
using UnityEngine;
using System.Collections.Generic;

namespace Pie
{
    /// <summary>
    /// Runtime-first entrypoint for Pie Agent.
    /// Attach this component in scene and drive the agent from game code/UI.
    /// </summary>
    public class PieRunner : MonoBehaviour
    {
        private PieBridge _bridge;
        private string _lastAssistantText = "";
        [SerializeField] private string _projectRootOverride = "";
        [SerializeField] private PieSettings _settingsOverride;

        public PieBridge Bridge => _bridge;
        public bool IsReady => _bridge?.IsInitialized == true;
        public string ProjectRootOverride => _projectRootOverride;
        public PieSettings SettingsOverride => _settingsOverride;

        public event Action<string> OnAssistantMessage;
        public event Action<string> OnAssistantDelta;
        public event Action<string> OnToolStart;
        public event Action<string, string> OnToolEnd;
        public event Action<string> OnTurnEndDetailed;
        public event Action OnTurnEnd;
        public event Action<string> OnError;

        [Serializable]
        private class RunnerStatePayload
        {
            public bool isReady;
            public string projectRootOverride;
            public bool hasSettingsOverride;
            public string lastAssistantText;
        }

        private void Awake()
        {
            PieDevRpcDispatcher.InitializeMainThread();
            PieDevRpcServer.Start();
            PieHostBridge.Register("pie.interaction", HandleInteractionHostCall);
            PieHostBridge.Register("pie.dev_rpc", PieDevRpc.InvokeHostCall);
            RegisterDevRpcMethods();
            InitializeBridge();
        }

        private bool InitializeBridge()
        {
            _bridge = new PieBridge();
            _bridge.OnJsEvent += HandleJsEvent;

            var projectRoot = string.IsNullOrWhiteSpace(_projectRootOverride)
                ? null
                : _projectRootOverride;

            if (!_bridge.Initialize(projectRoot, _settingsOverride))
            {
                var err = _bridge.LastError ?? "Bridge init failed";
                PieDiagnostics.Error($"[PieRunner] {err}");
                OnError?.Invoke(err);
                return false;
            }

            return true;
        }

        private void Update()
        {
            PieDevRpcDispatcher.Tick();
            _bridge?.Tick();
        }

        private void OnDestroy()
        {
            PieDevRpcServer.Stop();
            PieHostBridge.Unregister("pie.interaction");
            UnregisterDevRpcMethods();
            PieHostBridge.Unregister("pie.dev_rpc");
            if (_bridge == null) return;
            _bridge.OnJsEvent -= HandleJsEvent;
            _bridge.Dispose();
            _bridge = null;
        }

        private void RegisterDevRpcMethods()
        {
            PieDevRpc.Register("runner.get_state", _ => JsonUtility.ToJson(new RunnerStatePayload
            {
                isReady = IsReady,
                projectRootOverride = _projectRootOverride ?? "",
                hasSettingsOverride = _settingsOverride != null,
                lastAssistantText = _lastAssistantText ?? "",
            }));
        }

        private void UnregisterDevRpcMethods()
        {
            PieDevRpc.Unregister("runner.get_state");
        }

        private string HandleInteractionHostCall(string argsJson)
        {
            var request = JsonUtility.FromJson<PieInteractionRequest>(argsJson ?? "{}") ?? new PieInteractionRequest();
            if (string.Equals(request.type, "notify", StringComparison.Ordinal))
            {
                var message = string.IsNullOrWhiteSpace(request.message) ? "(empty notification)" : request.message;
                switch ((request.level ?? "info").ToLowerInvariant())
                {
                    case "error":
                        Debug.LogError($"[Pie Interaction] {message}");
                        break;
                    case "warning":
                        Debug.LogWarning($"[Pie Interaction] {message}");
                        break;
                    default:
                        Debug.Log($"[Pie Interaction] {message}");
                        break;
                }

                return PieInteractionResponse.ToJson(new PieInteractionResponse
                {
                    type = "notify",
                    acknowledged = true,
                });
            }

            return PieInteractionResponse.Unavailable(
                request.type,
                request.id,
                $"Runtime host does not support interaction request: {request.type}");
        }

        public bool SetConfig(string apiKey, string provider = null, string model = null, string baseUrl = null)
        {
            if (_bridge?.IsInitialized != true) return false;

            var safeApiKey = EscapeJsonString(apiKey ?? "");
            var safeProvider = EscapeJsonString(provider ?? "");
            var safeModel = EscapeJsonString(model ?? "");
            var safeBaseUrl = EscapeJsonString(baseUrl ?? "");
            var json = $"{{\"apiKey\":\"{safeApiKey}\",\"provider\":\"{safeProvider}\",\"model\":\"{safeModel}\",\"baseUrl\":\"{safeBaseUrl}\"}}";
            return _bridge.SendToJs("set_config", json);
        }

        public bool SendChatMessage(string content)
        {
            if (_bridge?.IsInitialized != true) return false;
            var safeContent = EscapeJsonString(content ?? "");
            var json = $"{{\"content\":\"{safeContent}\"}}";
            return _bridge.SendToJs("send_message", json);
        }

        public bool NewSession()
        {
            if (_bridge?.IsInitialized != true) return false;
            return _bridge.SendToJs("new_session", "{}");
        }

        public bool Abort()
        {
            if (_bridge?.IsInitialized != true) return false;
            return _bridge.SendToJs("abort", "{}");
        }

        public bool Reinitialize(string projectRoot = null)
        {
            _projectRootOverride = projectRoot ?? "";

            if (_bridge != null)
            {
                _bridge.OnJsEvent -= HandleJsEvent;
                _bridge.Dispose();
                _bridge = null;
            }

            _lastAssistantText = "";
            return InitializeBridge();
        }

        private void HandleJsEvent(string eventName, string json)
        {
            switch (eventName)
            {
                case "message_update":
                {
                    var text = ExtractJsonString(json, "text");
                    if (text != null)
                    {
                        OnAssistantMessage?.Invoke(text);
                        if (text.StartsWith(_lastAssistantText, StringComparison.Ordinal))
                        {
                            var delta = text.Substring(_lastAssistantText.Length);
                            if (!string.IsNullOrEmpty(delta))
                                OnAssistantDelta?.Invoke(delta);
                        }
                        else
                        {
                            OnAssistantDelta?.Invoke(text);
                        }
                        _lastAssistantText = text;
                    }
                    break;
                }
                case "tool_start":
                {
                    var name = ExtractJsonString(json, "name") ?? "tool";
                    OnToolStart?.Invoke(name);
                    break;
                }
                case "tool_end":
                {
                    var name = ExtractJsonString(json, "name") ?? "tool";
                    var error = ExtractJsonString(json, "error");
                    var result = ExtractJsonString(json, "result");
                    OnToolEnd?.Invoke(name, error ?? result ?? "");
                    break;
                }
                case "turn_end":
                {
                    var stopReason = ExtractJsonString(json, "stopReason") ?? "";
                    OnTurnEndDetailed?.Invoke(stopReason);
                    if (stopReason == "toolUse")
                        break;

                    _lastAssistantText = "";
                    OnTurnEnd?.Invoke();
                    break;
                }
                case "error":
                {
                    var message = ExtractJsonString(json, "message") ?? json;
                    OnError?.Invoke(message);
                    break;
                }
            }
        }

        private static string EscapeJsonString(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }

        private static string ExtractJsonString(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return null;

            var pattern = $"\"{key}\":\"";
            var start = json.IndexOf(pattern, StringComparison.Ordinal);
            if (start < 0) return null;
            start += pattern.Length;

            var sb = new System.Text.StringBuilder();
            var i = start;
            while (i < json.Length)
            {
                var c = json[i];
                if (c == '\\' && i + 1 < json.Length)
                {
                    switch (json[i + 1])
                    {
                        case 'n':  sb.Append('\n'); i += 2; continue;
                        case 'r':  sb.Append('\r'); i += 2; continue;
                        case 't':  sb.Append('\t'); i += 2; continue;
                        case '"':  sb.Append('"');  i += 2; continue;
                        case '\\': sb.Append('\\'); i += 2; continue;
                        default:   sb.Append(json[i + 1]); i += 2; continue;
                    }
                }
                if (c == '"') break;
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }
    }
}
