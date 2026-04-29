using System;
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

    internal static class PieHostBridgeProtocol
    {
        public static PieHostBridgeRequest ParseRequest(string argsJson)
        {
            return JsonUtility.FromJson<PieHostBridgeRequest>(argsJson ?? "{}") ?? new PieHostBridgeRequest();
        }

        public static PieUnityCapabilityDescriptor ToDescriptor(string hostNamespace, PieHostBridgeCapabilityPayload payload)
        {
            payload = payload ?? new PieHostBridgeCapabilityPayload();
            var mode = string.IsNullOrWhiteSpace(payload.mode) ? "runtime" : payload.mode;
            return new PieUnityCapabilityDescriptor
            {
                kind = string.IsNullOrWhiteSpace(payload.kind) ? "tool" : payload.kind,
                name = payload.name ?? "",
                ns = string.IsNullOrWhiteSpace(payload.@namespace) ? hostNamespace : payload.@namespace,
                description = payload.description ?? "",
                mode = mode,
                availableIn = mode,
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
            };
        }

        public static string SuccessJson()
        {
            return "{\"ok\":true}";
        }
    }
}
