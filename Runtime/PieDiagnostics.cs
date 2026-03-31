// Runtime/PieDiagnostics.cs
// Lightweight diagnostic logger for Pie Agent. Timestamped logs to Unity Console.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pie
{
    public enum PieLogLevel
    {
        Verbose,
        Info,
        Warning,
        Error
    }

    public static class PieDiagnostics
    {
        public static PieLogLevel CurrentLevel = PieLogLevel.Info;
        public static readonly List<string> LogHistory = new List<string>();

        public static void Log(string message, PieLogLevel level = PieLogLevel.Info)
        {
            if (level < CurrentLevel) return;

            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var fullMsg = $"[Pie {timestamp}] {message}";

            LogHistory.Add(fullMsg);

            switch (level)
            {
                case PieLogLevel.Error:
                    Debug.LogError(fullMsg);
                    break;
                case PieLogLevel.Warning:
                    Debug.LogWarning(fullMsg);
                    break;
                default:
                    Debug.Log(fullMsg);
                    break;
            }
        }

        public static void Verbose(string message) => Log(message, PieLogLevel.Verbose);
        public static void Info(string message)    => Log(message, PieLogLevel.Info);
        public static void Warning(string message) => Log(message, PieLogLevel.Warning);
        public static void Error(string message)   => Log(message, PieLogLevel.Error);

        public static string ExportLogs() => string.Join("\n", LogHistory);

        public static void Clear() => LogHistory.Clear();
    }
}
