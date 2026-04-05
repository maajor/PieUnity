using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Pie
{
    public static class PieDevRpcServer
    {
        private static HttpListener _listener;
        private static CancellationTokenSource _cts;
        private static Task _loopTask;
        private static readonly object SyncRoot = new object();

        public static int Port => 8091;
        public static bool IsRunning
        {
            get
            {
                lock (SyncRoot)
                {
                    return _listener != null && _listener.IsListening;
                }
            }
        }

        public static void Start()
        {
            lock (SyncRoot)
            {
                if (_listener != null && _listener.IsListening)
                    return;

                try
                {
                    _cts?.Cancel();
                    _listener?.Close();

                    _cts = new CancellationTokenSource();
                    _listener = new HttpListener();
                    _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
                    _listener.Prefixes.Add($"http://localhost:{Port}/");
                    _listener.Start();
                    _loopTask = Task.Run(() => AcceptLoop(_cts.Token));
                    PieDiagnostics.Info($"[PieDevRpcServer] Listening on http://localhost:{Port}/");
                }
                catch (Exception ex)
                {
                    PieDiagnostics.Warning($"[PieDevRpcServer] Failed to start: {ex.Message}");
                }
            }
        }

        public static void Stop()
        {
            lock (SyncRoot)
            {
                try
                {
                    _cts?.Cancel();
                }
                catch { }

                try
                {
                    _listener?.Close();
                }
                catch { }

                _listener = null;
                _cts = null;
                _loopTask = null;
            }
        }

        private static async Task AcceptLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                HttpListenerContext context = null;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                        PieDiagnostics.Warning($"[PieDevRpcServer] Accept failed: {ex.Message}");
                    break;
                }

                _ = Task.Run(() => HandleContext(context), token);
            }
        }

        private static void HandleContext(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;
                if (request.Url == null)
                {
                    WriteJson(context.Response, 400, "{\"ok\":false,\"error\":\"Missing URL\"}");
                    return;
                }

                var path = request.Url.AbsolutePath ?? "/";
                if (string.Equals(path, "/health", StringComparison.Ordinal))
                {
                    WriteJson(context.Response, 200, $"{{\"ok\":true,\"service\":\"PieDevRpcServer\",\"port\":{Port},\"running\":true}}");
                    return;
                }

                if (!path.StartsWith("/rpc/", StringComparison.Ordinal))
                {
                    WriteJson(context.Response, 404, "{\"ok\":false,\"error\":\"Not found\"}");
                    return;
                }

                var method = Uri.UnescapeDataString(path.Substring("/rpc/".Length));
                var body = "{}";
                if (string.Equals(request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8);
                    body = reader.ReadToEnd();
                    if (string.IsNullOrWhiteSpace(body))
                        body = "{}";
                }

                var responseJson = PieDevRpcDispatcher.InvokeSync(() => PieDevRpc.InvokeMethod(method, body));
                WriteJson(context.Response, 200, responseJson);
            }
            catch (Exception ex)
            {
                WriteJson(context.Response, 500, $"{{\"ok\":false,\"error\":\"{EscapeJson(ex.Message)}\"}}");
            }
        }

        private static void WriteJson(HttpListenerResponse response, int statusCode, string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json ?? "{}");
            response.StatusCode = statusCode;
            response.ContentType = "application/json; charset=utf-8";
            response.ContentEncoding = Encoding.UTF8;
            response.ContentLength64 = bytes.LongLength;
            using var output = response.OutputStream;
            output.Write(bytes, 0, bytes.Length);
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
