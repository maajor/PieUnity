using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pie
{
    [Serializable]
    internal sealed class PieHostBridgeCapabilityPayload
    {
        public string kind;
        public string name;
        public string @namespace;
        public string description;
        public string mode;
        public bool readOnly;
        public bool requiresMainThread = true;
        public string owner;
        public string writeScope;
        public string returns;
        public string recommendedWorkflow;
        public string[] examples;
        public string[] errorCodes;
        public bool destructive;
        public bool canTriggerDomainReload;
        public PieUnityParameterDescriptor[] parameters;
    }

    [Serializable]
    internal sealed class PieHostBridgeRequest
    {
        public string action;
        public string hostNamespace;
        public string hostDisplayName;
        public PieHostBridgeCapabilityPayload[] capabilities;
    }

    /// <summary>
    /// Structured host bridge surface for JS-side runtime hosts and legacy C# handlers.
    /// JS uses pieBridge.callHost("pie.host_bridge", { action, ... }) for registration.
    /// Registered runtime host capabilities are surfaced through PieUnityCapabilityRegistry.
    /// </summary>
    public static class PieHostBridge
    {
        private static readonly Dictionary<string, Func<string, string>> LegacyHandlers = new Dictionary<string, Func<string, string>>(StringComparer.Ordinal);

        public static void Register(string name, Func<string, string> handler)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Host bridge name is required.", nameof(name));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            LegacyHandlers[name] = handler;
        }

        public static bool Unregister(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;
            return LegacyHandlers.Remove(name);
        }

        public static void Clear()
        {
            LegacyHandlers.Clear();
            PieUnityCapabilityRegistry.ResetRuntimeHosts();
        }

        public static bool Has(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && LegacyHandlers.ContainsKey(name);
        }

        public static bool TryGetHandler(string name, out Func<string, string> handler)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                handler = null;
                return false;
            }

            return LegacyHandlers.TryGetValue(name, out handler);
        }

        public static string Invoke(string name, string argsJson)
        {
            if (string.Equals(name, "pie.host_bridge", StringComparison.Ordinal))
                return InvokeHostBridge(argsJson);

            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Host bridge name is required.", nameof(name));

            if (!LegacyHandlers.TryGetValue(name, out var handler))
                throw new InvalidOperationException($"Host bridge handler not found: {name}");

            return handler(argsJson ?? "{}") ?? "null";
        }

        public static string InvokeHostBridge(string argsJson)
        {
            var request = JsonUtility.FromJson<PieHostBridgeRequest>(argsJson ?? "{}") ?? new PieHostBridgeRequest();
            var action = (request.action ?? "").Trim().ToLowerInvariant();
            var hostNamespace = (request.hostNamespace ?? "").Trim();

            switch (action)
            {
                case "register_host":
                    return RegisterRuntimeHost(hostNamespace, request.hostDisplayName, request.capabilities);
                case "unregister_host":
                    PieUnityCapabilityRegistry.UnregisterRuntimeHost(hostNamespace);
                    return "{\"ok\":true}";
                default:
                    throw new InvalidOperationException("Unsupported host bridge action: " + action);
            }
        }

        private static string RegisterRuntimeHost(string hostNamespace, string hostDisplayName, PieHostBridgeCapabilityPayload[] payloads)
        {
            var descriptors = new List<PieUnityCapabilityDescriptor>();
            var items = payloads ?? new PieHostBridgeCapabilityPayload[0];
            for (var i = 0; i < items.Length; i++)
            {
                var payload = items[i] ?? new PieHostBridgeCapabilityPayload();
                descriptors.Add(new PieUnityCapabilityDescriptor
                {
                    kind = payload.kind ?? "tool",
                    name = payload.name ?? "",
                    ns = string.IsNullOrWhiteSpace(payload.@namespace) ? hostNamespace : payload.@namespace,
                    description = payload.description ?? "",
                    mode = string.IsNullOrWhiteSpace(payload.mode) ? "runtime" : payload.mode,
                    availableIn = string.IsNullOrWhiteSpace(payload.mode) ? "runtime" : payload.mode,
                    capabilityKind = "host",
                    owner = payload.owner ?? "",
                    writeScope = payload.writeScope ?? "",
                    returns = payload.returns ?? "",
                    recommendedWorkflow = payload.recommendedWorkflow ?? "",
                    examples = payload.examples ?? new string[0],
                    errorCodes = payload.errorCodes ?? new string[0],
                    readOnly = payload.readOnly,
                    deprecated = false,
                    convenience = false,
                    requiresMainThread = payload.requiresMainThread,
                    destructive = payload.destructive,
                    editorOnly = false,
                    runtimeOnly = true,
                    canTriggerDomainReload = payload.canTriggerDomainReload,
                    aliases = new string[0],
                    parameters = payload.parameters ?? new PieUnityParameterDescriptor[0],
                });
            }

            if (!PieUnityCapabilityRegistry.TryReplaceRuntimeHost(
                hostNamespace,
                hostDisplayName,
                descriptors.ToArray(),
                descriptor => args => InvokeRuntimeHostCapability(descriptor.name, args),
                out var error))
            {
                throw new InvalidOperationException(error);
            }

            return "{\"ok\":true}";
        }

        private static string InvokeRuntimeHostCapability(string capabilityName, string argsJson)
        {
            var bridge = PieBridge.Instance;
            if (bridge == null || !bridge.IsInitialized)
                throw new InvalidOperationException("PieBridge is not initialized.");
            if (!bridge.IsRuntimeHostBridgeReady)
                throw new InvalidOperationException("Runtime host bridge is not ready.");

            return bridge.InvokeRuntimeHostCapability(capabilityName, argsJson ?? "{}");
        }
    }
}
