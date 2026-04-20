#if PIE_UNITY_SPLIT_SOURCES
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Pie
{
    public static class PieUnityInstanceRegistry
    {
        private static readonly object FileSyncRoot = new object();
        private const int IoRetryCount = 5;
        private const int IoRetryDelayMs = 25;

        [Serializable]
        private sealed class PieUnityInstanceRegistryFile
        {
            public PieUnityInstance[] instances;
        }

        public static void Register(string instanceId, string projectPath, string projectName, string mode, int port, string token, string productName = "", string applicationIdentifier = "")
        {
            try
            {
                lock (FileSyncRoot)
                {
                    var instances = ReadAllInternal();
                    var pid = System.Diagnostics.Process.GetCurrentProcess().Id;
                    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    var next = new List<PieUnityInstance>();
                    for (var i = 0; i < instances.Count; i++)
                    {
                        var item = instances[i];
                        if (item == null)
                            continue;

                        if (ShouldReplaceExisting(item, pid, instanceId, projectPath, mode))
                            continue;

                        if (IsExpired(item, now))
                            continue;

                        next.Add(item);
                    }

                    next.Add(new PieUnityInstance
                    {
                        instanceId = instanceId,
                        projectPath = projectPath,
                        projectName = projectName,
                        displayName = string.IsNullOrWhiteSpace(productName) ? projectName : productName,
                        productName = productName,
                        applicationIdentifier = applicationIdentifier,
                        mode = mode,
                        port = port,
                        token = token,
                        pid = pid,
                        lastSeenUnix = now,
                        version = PieUnityCapabilitiesConstants.Version,
                        packageVersion = PieUnityCapabilitiesConstants.Version,
                    });

                    WriteAllInternal(next);
                }
            }
            catch (Exception ex)
            {
                PieDiagnostics.Warning($"[PieUnityInstanceRegistry] Register failed: {ex.Message}");
            }
        }

        public static void Unregister(string instanceId)
        {
            try
            {
                lock (FileSyncRoot)
                {
                    var instances = ReadAllInternal();
                    var pid = System.Diagnostics.Process.GetCurrentProcess().Id;
                    var next = new List<PieUnityInstance>();
                    for (var i = 0; i < instances.Count; i++)
                    {
                        var item = instances[i];
                        if (item == null)
                            continue;
                        if (item.pid == pid && string.Equals(item.instanceId, instanceId, StringComparison.Ordinal))
                            continue;
                        next.Add(item);
                    }

                    WriteAllInternal(next);
                }
            }
            catch (Exception ex)
            {
                PieDiagnostics.Warning($"[PieUnityInstanceRegistry] Unregister failed: {ex.Message}");
            }
        }

        public static PieUnityInstance[] ReadAll()
        {
            try
            {
                lock (FileSyncRoot)
                {
                    return ReadAllInternal().ToArray();
                }
            }
            catch
            {
                return new PieUnityInstance[0];
            }
        }

        private static List<PieUnityInstance> ReadAllInternal()
        {
            var filePath = PieUnityCapabilitiesConstants.RegistryFilePath;
            if (!File.Exists(filePath))
                return new List<PieUnityInstance>();

            var json = ReadRegistryText(filePath);
            if (string.IsNullOrWhiteSpace(json))
                return new List<PieUnityInstance>();

            var wrapper = JsonUtility.FromJson<PieUnityInstanceRegistryFile>(json);
            var items = wrapper?.instances ?? new PieUnityInstance[0];
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var next = new List<PieUnityInstance>();
            for (var i = 0; i < items.Length; i++)
            {
                var item = items[i];
                if (item == null)
                    continue;
                if (IsExpired(item, now))
                    continue;
                next.Add(item);
            }

            return next;
        }

        private static void WriteAllInternal(List<PieUnityInstance> instances)
        {
            Directory.CreateDirectory(PieUnityCapabilitiesConstants.RegistryDirectory);
            var wrapper = new PieUnityInstanceRegistryFile
            {
                instances = instances.ToArray(),
            };
            var targetPath = PieUnityCapabilitiesConstants.RegistryFilePath;
            var json = JsonUtility.ToJson(wrapper, true);
            var tempPath = targetPath + "." + System.Diagnostics.Process.GetCurrentProcess().Id + "." + Guid.NewGuid().ToString("N") + ".tmp";

            Exception lastError = null;
            for (var attempt = 0; attempt < IoRetryCount; attempt++)
            {
                try
                {
                    using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(true)))
                    {
                        writer.Write(json);
                        writer.Flush();
                        stream.Flush(true);
                    }

                    if (File.Exists(targetPath))
                        File.Replace(tempPath, targetPath, null, true);
                    else
                        File.Move(tempPath, targetPath);

                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    try
                    {
                        if (File.Exists(tempPath))
                            File.Delete(tempPath);
                    }
                    catch
                    {
                        // Ignore temp cleanup failures.
                    }

                    System.Threading.Thread.Sleep(IoRetryDelayMs * (attempt + 1));
                }
            }

            throw lastError ?? new IOException("Failed to write Unity instance registry.");
        }

        private static string ReadRegistryText(string filePath)
        {
            Exception lastError = null;
            for (var attempt = 0; attempt < IoRetryCount; attempt++)
            {
                try
                {
                    using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                    {
                        return reader.ReadToEnd();
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    System.Threading.Thread.Sleep(IoRetryDelayMs * (attempt + 1));
                }
            }

            throw lastError ?? new IOException("Failed to read Unity instance registry.");
        }

        private static bool ShouldReplaceExisting(PieUnityInstance item, int pid, string instanceId, string projectPath, string mode)
        {
            if (item == null)
                return false;

            if (item.pid == pid && string.Equals(item.instanceId, instanceId, StringComparison.Ordinal))
                return true;

            if (string.Equals(item.instanceId, instanceId, StringComparison.Ordinal))
                return true;

            if (string.Equals(item.projectPath, projectPath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.mode, mode, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private static bool IsExpired(PieUnityInstance item, long now)
        {
            if (item == null)
                return true;

            if (now - item.lastSeenUnix > PieUnityCapabilitiesConstants.RegistryTtlSeconds)
                return true;

            if (!IsProcessAlive(item.pid))
                return true;

            return false;
        }

        private static bool IsProcessAlive(int pid)
        {
            try
            {
                var process = System.Diagnostics.Process.GetProcessById(pid);
                return process != null && !process.HasExited;
            }
            catch
            {
                return false;
            }
        }
    }
}

#endif
