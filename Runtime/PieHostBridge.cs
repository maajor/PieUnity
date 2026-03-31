using System;
using System.Collections.Generic;

namespace Pie
{
    /// <summary>
    /// Thin host capability registry for project-specific runtime integrations.
    /// TypeScript extensions can call these handlers via pieExtension api.callHost(...).
    /// Handlers receive JSON text and return JSON text or plain text.
    /// </summary>
    public static class PieHostBridge
    {
        private static readonly Dictionary<string, Func<string, string>> Handlers = new Dictionary<string, Func<string, string>>(StringComparer.Ordinal);

        public static void Register(string name, Func<string, string> handler)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Host bridge name is required.", nameof(name));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            Handlers[name] = handler;
        }

        public static bool Unregister(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;
            return Handlers.Remove(name);
        }

        public static void Clear()
        {
            Handlers.Clear();
        }

        public static bool Has(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && Handlers.ContainsKey(name);
        }

        public static bool TryGetHandler(string name, out Func<string, string> handler)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                handler = null;
                return false;
            }

            return Handlers.TryGetValue(name, out handler);
        }

        public static string Invoke(string name, string argsJson)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Host bridge name is required.", nameof(name));

            if (!Handlers.TryGetValue(name, out var handler))
                throw new InvalidOperationException($"Host bridge handler not found: {name}");

            return handler(argsJson ?? "{}") ?? "null";
        }
    }
}
