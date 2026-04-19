#if PIE_UNITY_SPLIT_SOURCES
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pie
{
    [Serializable]
    public class PieDevRpcRequest
    {
        public string method;
        public string argsJson;
    }

    [Serializable]
    public class PieDevRpcResponse
    {
        public bool ok;
        public string method;
        public string resultJson;
        public string error;

        public static string Success(string method, string resultJson)
        {
            return JsonUtility.ToJson(new PieDevRpcResponse
            {
                ok = true,
                method = method ?? "",
                resultJson = string.IsNullOrEmpty(resultJson) ? "null" : resultJson,
                error = "",
            });
        }

        public static string Failure(string method, string error)
        {
            return JsonUtility.ToJson(new PieDevRpcResponse
            {
                ok = false,
                method = method ?? "",
                resultJson = "null",
                error = string.IsNullOrEmpty(error) ? "Unknown error" : error,
            });
        }
    }

    public static class PieDevRpc
    {
        private static readonly Dictionary<string, Func<string, string>> Methods = new Dictionary<string, Func<string, string>>(StringComparer.Ordinal);

        [Serializable]
        private class MethodListPayload
        {
            public string[] methods;
        }

        public static void Register(string method, Func<string, string> handler)
        {
            if (string.IsNullOrWhiteSpace(method))
                throw new ArgumentException("RPC method is required.", nameof(method));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            Methods[method] = handler;
        }

        public static bool Unregister(string method)
        {
            if (string.IsNullOrWhiteSpace(method))
                return false;
            return Methods.Remove(method);
        }

        public static void Clear()
        {
            Methods.Clear();
        }

        public static string InvokeHostCall(string argsJson)
        {
            var request = JsonUtility.FromJson<PieDevRpcRequest>(argsJson ?? "{}") ?? new PieDevRpcRequest();
            return InvokeMethod(request.method, request.argsJson ?? "{}");
        }

        public static string InvokeMethod(string method, string argsJson = "{}")
        {
            method = method ?? "";

            if (string.IsNullOrWhiteSpace(method))
                return PieDevRpcResponse.Failure(method, "RPC method is required.");

            var toolFound = PieUnityCapabilityRegistry.HasTool(method);
            if (toolFound)
            {
                if (PieUnityCapabilityRegistry.TryInvokeTool(method, argsJson, out var capabilityResultJson, out var capabilityToolError))
                    return PieDevRpcResponse.Success(method, capabilityResultJson);
                return PieDevRpcResponse.Failure(method, capabilityToolError);
            }

            var rpcFound = PieUnityCapabilityRegistry.HasRpc(method);
            if (rpcFound)
            {
                if (PieUnityCapabilityRegistry.TryInvokeRpc(method, argsJson, out var capabilityRpcResultJson, out var capabilityRpcError))
                    return PieDevRpcResponse.Success(method, capabilityRpcResultJson);
                return PieDevRpcResponse.Failure(method, capabilityRpcError);
            }

            if (string.Equals(method, "rpc.list", StringComparison.Ordinal))
            {
                var methods = new List<string>(Methods.Keys);
                methods.Sort(StringComparer.Ordinal);
                return PieDevRpcResponse.Success(method, JsonUtility.ToJson(new MethodListPayload
                {
                    methods = methods.ToArray(),
                }));
            }

            if (!Methods.TryGetValue(method, out var handler))
            {
                PieDiagnostics.Warning($"[PieDevRpc] RPC method not found: {method}");
                return PieDevRpcResponse.Failure(method, $"RPC method not found: {method}");
            }

            try
            {
                var resultJson = handler(argsJson ?? "{}");
                return PieDevRpcResponse.Success(method, resultJson);
            }
            catch (Exception ex)
            {
                PieDiagnostics.Warning($"[PieDevRpc] {method} failed: {ex.Message}");
                return PieDevRpcResponse.Failure(method, ex.Message);
            }
        }
    }
}

#endif
