// Runtime/PieHttpBridge.cs
// C# HTTP client with true SSE streaming for PuerTS V8 (no Node.js backend needed).
// JS side polls PollLine() to consume SSE lines one at a time as they arrive.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Pie
{
    /// <summary>
    /// Bridges HTTP SSE streaming from C# to PuerTS V8 JavaScript.
    ///
    /// Flow:
    ///   JS: requestId = PieHttpBridge.StartStreamRequest(url, body, bearerToken, headersJson)
    ///   JS: while (true) { line = PieHttpBridge.PollLine(requestId); if (line === null) break; yield line; }
    ///   JS: PieHttpBridge.CancelRequest(requestId)  // optional early cancel
    /// </summary>
    public static class PieHttpBridge
    {
        private static readonly HttpClient _httpClient = CreateHttpClient();

        private static readonly ConcurrentDictionary<int, RequestState> _requests =
            new ConcurrentDictionary<int, RequestState>();

        private static int _nextRequestId = 1;

        // ─────────────────────────────────────────────────────────────────────
        // Public API (called from JS via PuerTS)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Start an HTTP request. Returns a request ID for polling.
        /// headersJson is optional JSON object of additional headers, e.g. {"Content-Type":"application/json"}
        /// </summary>
        public static int StartStreamRequest(string method, string url, string body, string bearerToken, string headersJson = null)
        {
            int id = Interlocked.Increment(ref _nextRequestId);
            var state = new RequestState(id);
            _requests[id] = state;

            // Run on thread pool to avoid UnitySynchronizationContext marshaling
            // continuations back to the main thread (which would block the editor).
            Task.Run(() => RunRequestAsync(state, method, url, body, bearerToken, headersJson));

            return id;
        }

        /// <summary>
        /// Poll for the next SSE line.
        /// Returns: the next line string, "" if no data yet, or null if the stream has ended.
        /// After null is returned the request ID is cleaned up automatically.
        /// </summary>
        public static string PollLine(int requestId)
        {
            if (!_requests.TryGetValue(requestId, out var state))
                return null; // unknown id — treat as ended

            // Try to dequeue a buffered line
            if (state.Lines.TryDequeue(out var line))
            {
                return line; // may be null (the end sentinel) or a real line
            }

            if (state.IsComplete)
            {
                // Stream finished, no more lines
                _requests.TryRemove(requestId, out _);
                return null;
            }

            // No data available yet
            return "";
        }

        /// <summary>Cancel an in-flight request early.</summary>
        public static void CancelRequest(int requestId)
        {
            if (_requests.TryRemove(requestId, out var state))
            {
                state.Cts.Cancel();
            }
        }

        /// <summary>
        /// Get the HTTP status code for a request.
        /// Returns -1 if still waiting for response headers, 0 if unknown/finished.
        /// </summary>
        public static int GetStatus(int requestId)
        {
            if (_requests.TryGetValue(requestId, out var state))
                return state.StatusCode;
            return 0;
        }

        /// <summary>Check if a request encountered an error.</summary>
        public static string GetError(int requestId)
        {
            if (_requests.TryGetValue(requestId, out var state))
                return state.Error;
            return null;
        }

        /// <summary>
        /// Dequeue one line without the empty-string ambiguity of PollLine.
        /// Returns true if a line was dequeued (line may be "" for empty SSE lines, or null for end-of-stream).
        /// Returns false if the queue is empty (no data available yet).
        /// </summary>
        public static bool TryPollLine(int requestId, out string line)
        {
            if (!_requests.TryGetValue(requestId, out var state))
            {
                line = null;
                return true; // unknown id → treat as stream ended
            }

            if (state.Lines.TryDequeue(out line))
                return true; // real line (may be "" for SSE separator or null for end sentinel)

            if (state.IsComplete)
            {
                _requests.TryRemove(requestId, out _);
                line = null;
                return true; // stream ended
            }

            line = null;
            return false; // queue empty, still streaming
        }

        /// <summary>Returns IDs of all active (not yet cleaned up) requests.</summary>
        public static IEnumerable<int> GetActiveRequestIds() => _requests.Keys;

        /// <summary>Whether the HTTP status for this request has already been pushed to JS.</summary>
        public static bool IsStatusPushed(int requestId)
        {
            return _requests.TryGetValue(requestId, out var state) && state.StatusPushed;
        }

        /// <summary>Mark that the HTTP status has been pushed to JS.</summary>
        public static void MarkStatusPushed(int requestId)
        {
            if (_requests.TryGetValue(requestId, out var state))
                state.StatusPushed = true;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Internal
        // ─────────────────────────────────────────────────────────────────────

        private static HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip |
                                          System.Net.DecompressionMethods.Deflate
            };
            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(180)
            };
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }

        private static async Task RunRequestAsync(
            RequestState state,
            string method,
            string url,
            string body,
            string bearerToken,
            string headersJson)
        {
            try
            {
                using var request = new HttpRequestMessage(new HttpMethod(string.IsNullOrWhiteSpace(method) ? "POST" : method.ToUpperInvariant()), url);

                // Auth header
                if (!string.IsNullOrEmpty(bearerToken))
                {
                    var token = bearerToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                        ? bearerToken.Substring(7)
                        : bearerToken;
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }

                // Additional headers from JSON
                if (!string.IsNullOrEmpty(headersJson))
                {
                    try
                    {
                        var parsed = SimpleJsonParse(headersJson);
                        foreach (var kv in parsed)
                        {
                            // Skip auth header (already set above)
                            if (string.Equals(kv.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
                                continue;
                            if (string.Equals(kv.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                                continue;
                            request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                        }
                    }
                    catch (Exception ex)
                    {
                        PieDiagnostics.Warning($"[PieHttpBridge] Failed to parse headersJson: {ex.Message}");
                    }
                }

                // Body
                if (!string.IsNullOrEmpty(body))
                {
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                }

                // Send, stream response
                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    state.Cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    state.StatusCode = (int)response.StatusCode;
                    state.Error = $"HTTP {(int)response.StatusCode}: {errorBody}";
                    PieDiagnostics.Warning($"[PieHttpBridge] Request failed: {state.Error}");
                    state.IsComplete = true;
                    return;
                }

                state.StatusCode = (int)response.StatusCode;

                using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream, Encoding.UTF8);

                while (!reader.EndOfStream && !state.Cts.Token.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync();
                    if (line != null)
                    {
                        state.Lines.Enqueue(line);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                PieDiagnostics.Verbose("[PieHttpBridge] Request cancelled");
            }
            catch (Exception ex)
            {
                state.Error = ex.Message;
                PieDiagnostics.Error($"[PieHttpBridge] Request error: {ex.Message}");
            }
            finally
            {
                state.IsComplete = true;
            }
        }

        /// <summary>
        /// Minimal JSON object parser for header dictionaries.
        /// Handles flat JSON objects with string values and proper escaping.
        /// </summary>
        private static Dictionary<string, string> SimpleJsonParse(string json)
        {
            var result = new Dictionary<string, string>();
            json = json.Trim();
            if (!json.StartsWith("{") || !json.EndsWith("}")) return result;

            int index = 1;

            void SkipWhitespace()
            {
                while (index < json.Length && char.IsWhiteSpace(json[index]))
                    index++;
            }

            string ParseString()
            {
                if (index >= json.Length || json[index] != '"')
                    throw new FormatException("Expected string");

                index++; // Skip opening quote
                var sb = new StringBuilder();

                while (index < json.Length)
                {
                    char ch = json[index++];
                    if (ch == '"')
                        return sb.ToString();

                    if (ch != '\\')
                    {
                        sb.Append(ch);
                        continue;
                    }

                    if (index >= json.Length)
                        throw new FormatException("Invalid escape sequence");

                    char escaped = json[index++];
                    switch (escaped)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (index + 4 > json.Length)
                                throw new FormatException("Invalid unicode escape");
                            sb.Append((char)Convert.ToInt32(json.Substring(index, 4), 16));
                            index += 4;
                            break;
                        default:
                            throw new FormatException($"Unsupported escape sequence: \\{escaped}");
                    }
                }

                throw new FormatException("Unterminated string");
            }

            string ParseLiteral()
            {
                int start = index;
                while (index < json.Length && json[index] != ',' && json[index] != '}')
                    index++;
                return json.Substring(start, index - start).Trim();
            }

            while (index < json.Length)
            {
                SkipWhitespace();
                if (index < json.Length && json[index] == '}')
                    break;

                string key = ParseString();

                SkipWhitespace();
                if (index >= json.Length || json[index] != ':')
                    throw new FormatException("Expected ':' after key");
                index++;

                SkipWhitespace();
                string value = index < json.Length && json[index] == '"'
                    ? ParseString()
                    : ParseLiteral();

                result[key] = value;

                SkipWhitespace();
                if (index < json.Length && json[index] == ',')
                {
                    index++;
                    continue;
                }

                if (index < json.Length && json[index] == '}')
                    break;
            }

            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // State
        // ─────────────────────────────────────────────────────────────────────

        private class RequestState
        {
            public readonly int Id;
            public readonly ConcurrentQueue<string> Lines = new ConcurrentQueue<string>();
            public readonly CancellationTokenSource Cts = new CancellationTokenSource();
            public volatile bool IsComplete = false;
            public volatile int StatusCode = -1;
            public volatile bool StatusPushed = false;
            public string Error = null;

            public RequestState(int id) { Id = id; }
        }
    }
}
