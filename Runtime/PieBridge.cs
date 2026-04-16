// Runtime/PieBridge.cs
// Manages the PuerTS V8 ScriptEnv lifecycle and the C#<->JS message channel.
//
// Lifecycle:
//   1. Initialize() — load core.js, create ScriptEnv (BackendV8), inject pieBridge, execute JS
//   2. EditorApplication.update drives ScriptEnv.Tick() for Promise resolution
//   3. SendToJs(action, json) — call JS pie.handleCSharpMessage(action, data)
//   4. ReceiveFromJs(event, json) — called by JS via pieBridge.sendToUnity(...)
//   5. Dispose() — cleanup ScriptEnv

using System;
using System.IO;
using UnityEngine;
using Puerts;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Pie
{
    public class PieBridge : IDisposable
    {
        private ScriptEnv _scriptEnv;
        private bool _isInitialized = false;
        private bool _isDisposed = false;
        private string _lastError = null;
        private string _projectRoot;
        private PieSettings _settings;
        private string _coreJsContent;

        /// <summary>Fired when JS calls pieBridge.sendToUnity(event, jsonData).</summary>
        public event Action<string, string> OnJsEvent;

        public bool IsInitialized => _isInitialized && !_isDisposed && _scriptEnv != null;
        public string LastError => _lastError;
        // PuerTS 3.0.2 exposes BackendV8.TerminateExecution(), but the Unity
        // Editor smoke test showed a native crash in PapiV8.bundle while
        // ScriptEnv.Eval formatted the terminated exception. Keep the V8
        // runtime baseline, but do not expose native termination as the
        // default safety mechanism until PuerTS can unwind it safely.
        public bool IsScriptStepTerminationAvailable => false;

        // Singleton so JS can call back via CS.Pie.PieBridge.Instance
        public static PieBridge Instance { get; private set; }
        public static string LastInitializationError { get; private set; }

        // ─────────────────────────────────────────────────────────────────────
        // Public API
        // ─────────────────────────────────────────────────────────────────────

        public bool Initialize(string projectRoot = null, PieSettings settings = null)
        {
            if (_isInitialized)
                return true;

            try
            {
                LastInitializationError = "";
                PieDiagnostics.Verbose("Loading core.js...");
                var jsContent = LoadCoreJs();
                if (string.IsNullOrEmpty(jsContent))
                    throw new Exception("Failed to load core.js");

                _projectRoot = projectRoot;
                _settings = settings;
                _coreJsContent = jsContent;
                CreateScriptEnvironment(jsContent, projectRoot, settings);

#if UNITY_EDITOR
                EditorApplication.update += Tick;
#endif

                _isInitialized = true;
                Instance = this;

                PieDiagnostics.Info($"PieBridge ready (V8, core.js {jsContent.Length} chars)");
                return true;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                LastInitializationError = ex.Message;
                PieDiagnostics.Error($"PieBridge init failed: {ex.Message}\n{ex.StackTrace}");

                DestroyScriptEnvironment();
                return false;
            }
        }

        /// <summary>Send a message to the JS Agent. action = "send_message", jsonData = "{\"content\":\"...\"}"</summary>
        public bool SendToJs(string action, string jsonData)
        {
            if (_scriptEnv == null || _isDisposed)
            {
                PieDiagnostics.Warning("[PieBridge.SendToJs] Not initialized");
                return false;
            }

            try
            {
                _scriptEnv.Eval($"pie.handleCSharpMessage('{action}', {jsonData});");
                return true;
            }
            catch (Exception ex)
            {
                PieDiagnostics.Error($"[PieBridge.SendToJs] Error: {ex.Message}");
                return false;
            }
        }

        /// <summary>Called by JS pieBridge.sendToUnity(event, json) — dispatches OnJsEvent.</summary>
        public void ReceiveFromJs(string eventName, string json)
        {
            OnJsEvent?.Invoke(eventName, json);
        }

        public string InvokeUnityScriptHost(string method, string argsJson)
        {
            if (_scriptEnv == null || _isDisposed)
                throw new InvalidOperationException("PieBridge is not initialized.");

            var script = $"(function(){{var host=globalThis.__pieUnityScriptHost;if(!host||typeof host.invoke!=='function') throw new Error('Unity script host is not ready.'); return host.invoke({JsonString(method ?? "")},{JsonString(argsJson ?? "{}")});}})()";
            return _scriptEnv.Eval<string>(script);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

#if UNITY_EDITOR
            EditorApplication.update -= Tick;
#endif

            // Domain reload can happen while a streaming request is still in flight.
            // Cancel all background bridge work before tearing down ScriptEnv so old-domain
            // tasks do not block assembly reload.
            PieHttpBridge.CancelAllRequests();
            PieFileBridge.CancelAllRequests();

            DestroyScriptEnvironment();

            if (Instance == this) Instance = null;
            PieDiagnostics.Info("[PieBridge] Disposed");
        }

        /// <summary>Drive JS microtasks and SSE delivery from a host update loop.</summary>
        public void Tick()
        {
            if (_isDisposed || _scriptEnv == null) return;

            try
            {
                // Pump Unity-backed async bridges before ticking JS so queued data
                // is visible to the same frame's microtasks and event handlers.
                PieHttpBridge.PumpActiveRequests();
                PushSSEData();
                PushFileData();
                _scriptEnv.Eval("(function(){var host=globalThis.__pieUnityScriptHost;if(host&&typeof host.tick==='function') host.tick();})()");
                _scriptEnv.Tick();
            }
            catch (Exception ex)
            {
                PieDiagnostics.Error($"[PieBridge.Tick] {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Internal
        // ─────────────────────────────────────────────────────────────────────

        private void CreateScriptEnvironment(string jsContent, string projectRoot, PieSettings settings)
        {
            PieDiagnostics.Verbose("Creating ScriptEnv (PuerTS BackendV8)...");
            _scriptEnv = new ScriptEnv(new BackendV8(new DefaultLoader()), -1);

            PieDiagnostics.Verbose("Injecting pieBridge...");
            InjectPieBridge(projectRoot, settings);

            PieDiagnostics.Verbose("Executing core.js...");
            _scriptEnv.Eval(jsContent);

            if (!VerifyPieObject())
                throw new Exception("pie.handleCSharpMessage not found after core.js execution");
        }

        private void DestroyScriptEnvironment()
        {
            try
            {
                _scriptEnv?.Dispose();
            }
            catch (Exception ex)
            {
                PieDiagnostics.Warning($"[PieBridge] ScriptEnv dispose failed: {ex.Message}");
            }
            _scriptEnv = null;
        }

        private string LoadCoreJs()
        {
#if UNITY_EDITOR
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PieBridge).Assembly);
            var resolvedPackagePath = packageInfo != null ? packageInfo.resolvedPath : "";

            // Try various locations the package might be installed at
            string[] candidates = {
                string.IsNullOrEmpty(resolvedPackagePath) ? "" : Path.Combine(resolvedPackagePath, "Resources", "pie", "core.js"),
                Path.Combine(projectRoot, "Packages", "com.pie.agent", "Resources", "pie", "core.js"),
                Path.Combine(projectRoot, "Packages", "pie-unity", "Resources", "pie", "core.js"),
            };

            foreach (var path in candidates)
            {
                if (string.IsNullOrEmpty(path))
                    continue;

                PieDiagnostics.Verbose($"Trying: {path}");
                if (File.Exists(path))
                {
                    try
                    {
                        var content = File.ReadAllText(path);
                        PieDiagnostics.Verbose($"Loaded core.js from: {path}");
                        return content;
                    }
                    catch (Exception ex)
                    {
                        PieDiagnostics.Warning($"Failed to read {path}: {ex.Message}");
                    }
                }
            }

            // Try PackageCache (installed via git/tarball)
            var cacheDir = Path.Combine(projectRoot, "Library", "PackageCache");
            if (Directory.Exists(cacheDir))
            {
                foreach (var dir in Directory.GetDirectories(cacheDir, "com.pie.agent@*"))
                {
                    var path = Path.Combine(dir, "Resources", "pie", "core.js");
                    if (File.Exists(path))
                    {
                        try { return File.ReadAllText(path); }
                        catch { /* continue */ }
                    }
                }
            }
#endif
            // Fallback: Resources.Load (works if core.js is in Assets/Resources/pie/)
            var textAsset = Resources.Load<TextAsset>("pie/core");
            if (textAsset != null)
            {
                PieDiagnostics.Verbose($"Loaded core.js via Resources.Load ({textAsset.text.Length} chars)");
                return textAsset.text;
            }

            return null;
        }

        private void InjectPieBridge(string projectRoot, PieSettings settings)
        {
            var isEditor = false;
#if UNITY_EDITOR
            isEditor = true;
            if (string.IsNullOrEmpty(projectRoot))
                projectRoot = Directory.GetParent(Application.dataPath).FullName
                    .Replace("\\", "/");
#else
            if (string.IsNullOrEmpty(projectRoot))
                projectRoot = Application.persistentDataPath.Replace("\\", "/");
#endif
            if (!Directory.Exists(projectRoot))
                Directory.CreateDirectory(projectRoot);

            var isEditorJs = isEditor ? "true" : "false";
            var extensionSearchPathsJson = JsonString(PieProjectPaths.GetExtensionSearchPathsJson(projectRoot, settings));
            var skillSearchPathsJson = JsonString(PieProjectPaths.GetSkillSearchPathsJson(projectRoot, settings));
            var fileToolRootsJson = JsonString(PieProjectPaths.GetFileToolRootsJson(projectRoot, settings, isEditor));
            var initCode = $@"
(function() {{
    globalThis.pieBridge = {{
        projectRoot: {JsonString(projectRoot)},
        isEditor: {isEditorJs},
        extensionSearchPathsJson: {extensionSearchPathsJson},
        skillSearchPathsJson: {skillSearchPathsJson},
        fileToolRootsJson: {fileToolRootsJson},

        getProjectRoot: function() {{
            return globalThis.pieBridge.projectRoot;
        }},

        getProjectMemoryPath: function() {{
            return CS.Pie.PieProjectPaths.GetProjectMemoryPath(globalThis.pieBridge.projectRoot);
        }},

        getPersistentDataPath: function() {{
            return String(CS.UnityEngine.Application.persistentDataPath || '').replace(/\\/g, '/');
        }},

        getRuntimeStateRoot: function() {{
            return CS.Pie.PieProjectPaths.GetRuntimeStateRoot();
        }},

        getSessionsDirectory: function() {{
            return CS.Pie.PieProjectPaths.GetSessionsDirectory();
        }},

        getExtensionSearchPathsJson: function() {{
            return globalThis.pieBridge.extensionSearchPathsJson;
        }},

        getSkillSearchPathsJson: function() {{
            return globalThis.pieBridge.skillSearchPathsJson;
        }},

        getFileToolRootsJson: function() {{
            return globalThis.pieBridge.fileToolRootsJson;
        }},

        sendToUnity: function(eventName, data) {{
            try {{
                var json = typeof data === 'string' ? data : JSON.stringify(data);
                var inst = CS.Pie.PieBridge.Instance;
                if (inst) inst.ReceiveFromJs(eventName, json);
            }} catch(e) {{
                console.error('[pieBridge.sendToUnity]', e && e.message || e);
            }}
        }},

        callHost: function(name, data) {{
            try {{
                var json = typeof data === 'string' ? data : JSON.stringify(data || {{}});
                return CS.Pie.PieHostBridge.Invoke(name, json);
            }} catch(e) {{
                throw new Error('[pieBridge.callHost] ' + (e && e.message || e));
            }}
        }},

        log: function(level, msg) {{
            try {{
                switch(level) {{
                    case 'error':   CS.Pie.PieDiagnostics.Error('[JS] ' + msg); break;
                    case 'warn':    CS.Pie.PieDiagnostics.Warning('[JS] ' + msg); break;
                    default:        CS.Pie.PieDiagnostics.Info('[JS] ' + msg); break;
                }}
            }} catch(e) {{ /* ignore */ }}
        }}
    }};

    // Route console → Unity (Verbose/Warning/Error)
    console.log   = function() {{ var m = Array.prototype.join.call(arguments, ' '); CS.Pie.PieDiagnostics.Verbose(m); }};
    console.warn  = function() {{ var m = Array.prototype.join.call(arguments, ' '); CS.Pie.PieDiagnostics.Warning(m); }};
    console.error = function() {{ var m = Array.prototype.join.call(arguments, ' '); CS.Pie.PieDiagnostics.Error(m); }};

    return true;
}})()";

            var ok = _scriptEnv.Eval<bool>(initCode);
            if (!ok) throw new Exception("pieBridge injection returned false");
        }

        private bool VerifyPieObject()
        {
            var pieExists = _scriptEnv.Eval<bool>("typeof globalThis.pie !== 'undefined'");
            if (!pieExists)
            {
                var keys = _scriptEnv.Eval<string>("Object.keys(globalThis).slice(0, 30).join(', ')");
                PieDiagnostics.Warning($"pie not found. globals: {keys}");
                return false;
            }

            var handlerExists = _scriptEnv.Eval<bool>(
                "typeof globalThis.pie.handleCSharpMessage === 'function'");
            if (!handlerExists)
            {
                var pieKeys = _scriptEnv.Eval<string>("Object.keys(globalThis.pie).join(', ')");
                PieDiagnostics.Warning($"handleCSharpMessage not found. pie keys: {pieKeys}");
                return false;
            }

            return true;
        }

        private const int MAX_LINES_PER_FRAME = 3;

        private void PushSSEData()
        {
            foreach (var reqId in PieHttpBridge.GetActiveRequestIds())
            {
                var status = PieHttpBridge.GetStatus(reqId);
                if (status != -1 && !PieHttpBridge.IsStatusPushed(reqId))
                {
                    PieHttpBridge.MarkStatusPushed(reqId);
                    var error = PieHttpBridge.GetError(reqId);
                    var errorArg = error != null ? JsonString(error) : "null";
                    _scriptEnv.Eval($"globalThis._pieSSEStatus({reqId},{status},{errorArg})");
                    return;
                }

                int pushed = 0;
                while (pushed < MAX_LINES_PER_FRAME)
                {
                    if (!PieHttpBridge.TryPollLine(reqId, out var line))
                        break;

                    if (line == null)
                    {
                        _scriptEnv.Eval($"globalThis._pieSSEPush({reqId},null)");
                        break;
                    }

                    _scriptEnv.Eval($"globalThis._pieSSEPush({reqId},{JsonString(line)})");
                    pushed++;
                }
            }
        }

        private void PushFileData()
        {
            foreach (var reqId in PieFileBridge.GetActiveRequestIds())
            {
                if (!PieFileBridge.IsRequestComplete(reqId) || PieFileBridge.IsResultPushed(reqId))
                    continue;

                PieFileBridge.MarkResultPushed(reqId);
                var error = PieFileBridge.GetRequestError(reqId);
                var resultJson = PieFileBridge.GetRequestResultJson(reqId);
                var errorArg = error != null ? JsonString(error) : "null";
                var resultArg = resultJson != null ? JsonString(resultJson) : "null";
                _scriptEnv.Eval($"globalThis._pieFileComplete({reqId},{resultArg},{errorArg})");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Produce a JSON string literal including surrounding quotes.</summary>
        private static string JsonString(string value)
        {
            if (value == null) return "null";
            return "\"" + value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t")
                + "\"";
        }

        private static string EscapeForJs(string value)
        {
            if (value == null) return "null";
            return value
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r");
        }
    }
}
