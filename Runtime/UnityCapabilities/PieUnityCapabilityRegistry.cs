using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Pie
{
    public static class PieUnityCapabilityRegistry
    {
        private sealed class CapabilityRegistration
        {
            public PieUnityCapabilityDescriptor Descriptor;
            public Func<string, string> Handler;
        }

        private static readonly Dictionary<string, CapabilityRegistration> RpcMethods = new Dictionary<string, CapabilityRegistration>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, CapabilityRegistration> ToolMethods = new Dictionary<string, CapabilityRegistration>(StringComparer.OrdinalIgnoreCase);
        private static readonly object SyncRoot = new object();
        private static string _instanceId = "";
        private static string _projectPath = "";
        private static string _mode = "";

        public static void ConfigureContext(string instanceId, string projectPath, string mode)
        {
            lock (SyncRoot)
            {
                _instanceId = instanceId ?? "";
                _projectPath = projectPath ?? "";
                _mode = mode ?? "";
            }
        }

        public static string InstanceId
        {
            get { lock (SyncRoot) return _instanceId; }
        }

        public static string ProjectPath
        {
            get { lock (SyncRoot) return _projectPath; }
        }

        public static string Mode
        {
            get { lock (SyncRoot) return _mode; }
        }

        public static void RegisterRpc(
            string name,
            string ns,
            string description,
            string mode,
            bool readOnly,
            bool deprecated,
            string[] aliases,
            PieUnityParameterDescriptor[] parameters,
            Func<string, string> handler,
            string capabilityKind = "host",
            bool convenience = false,
            bool requiresMainThread = true)
        {
            RegisterInternal(RpcMethods, "rpc", name, ns, description, mode, readOnly, deprecated, aliases, parameters, handler, capabilityKind, convenience, requiresMainThread);
        }

        public static void RegisterTool(
            string name,
            string ns,
            string description,
            string mode,
            bool readOnly,
            bool deprecated,
            string[] aliases,
            PieUnityParameterDescriptor[] parameters,
            Func<string, string> handler,
            string capabilityKind = "host",
            bool convenience = false,
            bool requiresMainThread = true)
        {
            RegisterInternal(ToolMethods, "tool", name, ns, description, mode, readOnly, deprecated, aliases, parameters, handler, capabilityKind, convenience, requiresMainThread);
        }

        public static bool TryInvokeRpc(string name, string argsJson, out string resultJson, out string error)
        {
            return TryInvoke("rpc", RpcMethods, name, argsJson, out resultJson, out error);
        }

        public static bool TryInvokeTool(string name, string argsJson, out string resultJson, out string error)
        {
            return TryInvoke("tool", ToolMethods, name, argsJson, out resultJson, out error);
        }

        public static bool HasTool(string name)
        {
            lock (SyncRoot)
                return ToolMethods.ContainsKey(name ?? "");
        }

        public static bool HasRpc(string name)
        {
            lock (SyncRoot)
                return RpcMethods.ContainsKey(name ?? "");
        }

        public static PieUnityCapabilityDescriptor[] GetManifestEntries(string filterNamespace, string filterName)
        {
            lock (SyncRoot)
            {
                var all = RpcMethods.Values
                    .Concat(ToolMethods.Values)
                    .Select((entry) => entry.Descriptor)
                    .GroupBy((entry) => $"{entry.kind}:{entry.name}", StringComparer.OrdinalIgnoreCase)
                    .Select((group) => group.First())
                    .OrderBy((entry) => entry.kind, StringComparer.Ordinal)
                    .ThenBy((entry) => entry.name, StringComparer.Ordinal)
                    .ToArray();

                if (!string.IsNullOrWhiteSpace(filterName))
                {
                    return all.Where((entry) => string.Equals(entry.name, filterName, StringComparison.OrdinalIgnoreCase)).ToArray();
                }

                if (!string.IsNullOrWhiteSpace(filterNamespace))
                {
                    return all.Where((entry) => string.Equals(entry.ns, filterNamespace, StringComparison.OrdinalIgnoreCase)).ToArray();
                }

                return all;
            }
        }

        public static string BuildManifestJson(string filterNamespace, string filterName)
        {
            var instanceId = InstanceId;
            var projectPath = ProjectPath;
            var mode = Mode;
            if (!string.IsNullOrWhiteSpace(filterNamespace) || !string.IsNullOrWhiteSpace(filterName))
            {
                var detail = new PieUnityManifestDetail
                {
                    instanceId = instanceId,
                    projectPath = projectPath,
                    mode = mode,
                    filterNamespace = filterNamespace ?? "",
                    filterName = filterName ?? "",
                    capabilities = GetManifestEntries(filterNamespace, filterName),
                };
                return JsonUtility.ToJson(detail, true);
            }

            var entries = GetManifestEntries(null, null);
            var namespaces = entries
                .Select((entry) => entry.ns)
                .Where((entry) => !string.IsNullOrWhiteSpace(entry))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy((entry) => entry, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var summary = new PieUnityManifestSummary
            {
                instanceId = instanceId,
                projectPath = projectPath,
                mode = mode,
                namespaces = namespaces,
                stableTools = entries.Count((entry) => string.Equals(entry.kind, "tool", StringComparison.OrdinalIgnoreCase) && !entry.deprecated),
                deprecatedAliases = entries.Count((entry) => entry.deprecated),
            };
            return JsonUtility.ToJson(summary, true);
        }

        private static bool TryInvoke(
            string kind,
            Dictionary<string, CapabilityRegistration> source,
            string name,
            string argsJson,
            out string resultJson,
            out string error)
        {
            lock (SyncRoot)
            {
                if (!source.TryGetValue(name ?? "", out var registration))
                {
                    resultJson = "null";
                    error = $"Capability not found: {name}";
                    PieDiagnostics.Warning($"[PieUnityCapabilityRegistry] {kind} {name} failed: {error}");
                    return false;
                }

                try
                {
                    resultJson = registration.Handler(argsJson ?? "{}") ?? "null";
                    error = "";
                    return true;
                }
                catch (Exception ex)
                {
                    resultJson = "null";
                    error = ex.Message;
                    PieDiagnostics.Warning($"[PieUnityCapabilityRegistry] {kind} {name} failed: {ex.Message}");
                    return false;
                }
            }
        }

        private static void RegisterInternal(
            Dictionary<string, CapabilityRegistration> source,
            string kind,
            string name,
            string ns,
            string description,
            string mode,
            bool readOnly,
            bool deprecated,
            string[] aliases,
            PieUnityParameterDescriptor[] parameters,
            Func<string, string> handler,
            string capabilityKind,
            bool convenience,
            bool requiresMainThread)
        {
            lock (SyncRoot)
            {
                var descriptor = new PieUnityCapabilityDescriptor
                {
                    kind = kind,
                    name = name,
                    ns = ns,
                    description = description,
                    mode = mode,
                    availableIn = mode,
                    capabilityKind = capabilityKind ?? "host",
                    readOnly = readOnly,
                    deprecated = deprecated,
                    convenience = convenience,
                    requiresMainThread = requiresMainThread,
                    aliases = aliases ?? new string[0],
                    parameters = parameters ?? new PieUnityParameterDescriptor[0],
                };

                var registration = new CapabilityRegistration
                {
                    Descriptor = descriptor,
                    Handler = handler,
                };

                source[name] = registration;
                if (aliases == null)
                    return;

                for (var i = 0; i < aliases.Length; i++)
                {
                    var alias = aliases[i];
                    if (string.IsNullOrWhiteSpace(alias))
                        continue;
                    source[alias] = registration;
                }
            }
        }
    }
}
