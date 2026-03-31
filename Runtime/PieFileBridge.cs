using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;
using UnityEngine;

namespace Pie
{
    public static class PieFileBridge
    {
        private sealed class RequestState
        {
            public readonly int Id;
            public readonly CancellationTokenSource Cts = new CancellationTokenSource();
            public volatile bool IsComplete;
            public volatile bool ResultPushed;
            public string ResultJson;
            public string Error;

            public RequestState(int id)
            {
                Id = id;
            }
        }

        [Serializable]
        public sealed class FindRequestResult
        {
            public string[] Results;
            public int ScannedDirectories;
            public int ScannedFiles;
            public bool LimitReached;
            public string Pattern;
            public string RootPath;
        }

        [Serializable]
        public sealed class GrepRequestResult
        {
            public string[] Lines;
            public int MatchCount;
            public bool MatchLimitReached;
            public bool LinesTruncated;
            public int FilesScanned;
            public string Pattern;
            public string SearchPath;
            public string Glob;
            public bool Literal;
            public bool IgnoreCase;
        }

        [Serializable]
        public sealed class FileStatResult
        {
            public string Name;
            public bool IsDirectory;
            public bool IsFile;
            public long Size;
            public long LastWriteTicksUtc;
        }

        private static readonly ConcurrentDictionary<int, RequestState> _requests =
            new ConcurrentDictionary<int, RequestState>();

        private static int _nextRequestId = 1;

        private static readonly string[] _skipDirs = { ".git", "node_modules", "__pycache__", ".svn", ".hg" };
        private static readonly HashSet<string> _skipDirSet = new HashSet<string>(_skipDirs, StringComparer.OrdinalIgnoreCase);

        private static readonly string[] _binaryExtensions = {
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp",
            ".mp3", ".mp4", ".wav", ".avi", ".mov",
            ".zip", ".tar", ".gz", ".bz2", ".7z", ".rar",
            ".exe", ".dll", ".so", ".dylib", ".bin",
            ".pdf", ".doc", ".docx", ".xls", ".xlsx",
            ".woff", ".woff2", ".ttf", ".eot",
        };
        private static readonly HashSet<string> _binaryExtensionSet = new HashSet<string>(_binaryExtensions, StringComparer.OrdinalIgnoreCase);

        private const int GrepMaxLineLength = 400;

        public static Task<string> ReadAllTextAsync(string path)
        {
            return Task.Run(() => File.ReadAllText(path));
        }

        public static Task WriteAllTextAsync(string path, string content)
        {
            return Task.Run(() =>
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(path, content ?? string.Empty);
            });
        }

        public static Task CreateDirectoryAsync(string path)
        {
            return Task.Run(() => Directory.CreateDirectory(path));
        }

        public static Task<string[]> ReadDirectoryAsync(string path)
        {
            return Task.Run(() =>
            {
                var files = Directory.GetFiles(path).Select(Path.GetFileName);
                var dirs = Directory.GetDirectories(path).Select(Path.GetFileName);
                return files.Concat(dirs).ToArray();
            });
        }

        public static Task<bool> ExistsAsync(string path)
        {
            return Task.Run(() => File.Exists(path) || Directory.Exists(path));
        }

        public static Task<FileStatResult> StatAsync(string path)
        {
            return Task.Run(() =>
            {
                if (File.Exists(path))
                {
                    var info = new FileInfo(path);
                    return new FileStatResult
                    {
                        Name = info.Name,
                        IsDirectory = false,
                        IsFile = true,
                        Size = info.Length,
                        LastWriteTicksUtc = info.LastWriteTimeUtc.Ticks,
                    };
                }

                if (Directory.Exists(path))
                {
                    var info = new DirectoryInfo(path);
                    return new FileStatResult
                    {
                        Name = info.Name,
                        IsDirectory = true,
                        IsFile = false,
                        Size = 0,
                        LastWriteTicksUtc = info.LastWriteTimeUtc.Ticks,
                    };
                }

                throw new FileNotFoundException($"ENOENT: no such file or directory, stat '{path}'");
            });
        }

        public static Task DeleteFileAsync(string path)
        {
            return Task.Run(() => File.Delete(path));
        }

        public static Task<string> FindAsync(string rootPath, string pattern, int limit)
        {
            return Task.Run(() =>
            {
                var payload = ExecuteFind(rootPath, pattern, limit, CancellationToken.None);
                return JsonUtility.ToJson(payload);
            });
        }

        public static Task<string> GrepAsync(string searchPath, string pattern, string globPattern, bool ignoreCase, bool literal, int contextLines, int limit)
        {
            return Task.Run(() =>
            {
                var payload = ExecuteGrep(searchPath, pattern, globPattern, ignoreCase, literal, contextLines, limit, CancellationToken.None);
                return JsonUtility.ToJson(payload);
            });
        }

        public static int StartFind(string rootPath, string pattern, int limit)
        {
            int id = Interlocked.Increment(ref _nextRequestId);
            var state = new RequestState(id);
            _requests[id] = state;

            PieDiagnostics.Verbose($"[PieFileBridge] find start path={rootPath} pattern={pattern} limit={limit}");
            Task.Run(() => RunFind(state, rootPath, pattern, limit));
            return id;
        }

        public static int StartGrep(string searchPath, string pattern, string globPattern, bool ignoreCase, bool literal, int contextLines, int limit)
        {
            int id = Interlocked.Increment(ref _nextRequestId);
            var state = new RequestState(id);
            _requests[id] = state;

            PieDiagnostics.Verbose($"[PieFileBridge] grep start path={searchPath} pattern={pattern} glob={globPattern} limit={limit}");
            Task.Run(() => RunGrep(state, searchPath, pattern, globPattern, ignoreCase, literal, contextLines, limit));
            return id;
        }

        public static bool IsRequestComplete(int requestId)
        {
            return _requests.TryGetValue(requestId, out var state) && state.IsComplete;
        }

        public static IEnumerable<int> GetActiveRequestIds()
        {
            return _requests.Keys;
        }

        public static bool IsResultPushed(int requestId)
        {
            return _requests.TryGetValue(requestId, out var state) && state.ResultPushed;
        }

        public static void MarkResultPushed(int requestId)
        {
            if (_requests.TryGetValue(requestId, out var state))
                state.ResultPushed = true;
        }

        public static string GetRequestResultJson(int requestId)
        {
            return _requests.TryGetValue(requestId, out var state) ? state.ResultJson : null;
        }

        public static string GetRequestError(int requestId)
        {
            return _requests.TryGetValue(requestId, out var state) ? state.Error : null;
        }

        public static void CancelRequest(int requestId)
        {
            if (_requests.TryGetValue(requestId, out var state))
                state.Cts.Cancel();
        }

        public static void ReleaseRequest(int requestId)
        {
            if (_requests.TryRemove(requestId, out var state))
                state.Cts.Dispose();
        }

        private static void RunFind(RequestState state, string rootPath, string pattern, int limit)
        {
            try
            {
                var payload = ExecuteFind(rootPath, pattern, limit, state.Cts.Token);
                state.ResultJson = JsonUtility.ToJson(payload);
                PieDiagnostics.Verbose($"[PieFileBridge] find done pattern={pattern} matches={payload.Results.Length} dirs={payload.ScannedDirectories} files={payload.ScannedFiles}");
            }
            catch (OperationCanceledException)
            {
                state.Error = "Operation aborted";
                PieDiagnostics.Warning("[PieFileBridge] find cancelled");
            }
            catch (Exception ex)
            {
                state.Error = ex.Message;
                PieDiagnostics.Error($"[PieFileBridge] find error: {ex.Message}");
            }
            finally
            {
                state.IsComplete = true;
            }
        }

        private static void RunGrep(RequestState state, string searchPath, string pattern, string globPattern, bool ignoreCase, bool literal, int contextLines, int limit)
        {
            try
            {
                var payload = ExecuteGrep(searchPath, pattern, globPattern, ignoreCase, literal, contextLines, limit, state.Cts.Token);
                state.ResultJson = JsonUtility.ToJson(payload);
                PieDiagnostics.Verbose($"[PieFileBridge] grep done pattern={pattern} matches={payload.MatchCount} files={payload.FilesScanned}");
            }
            catch (OperationCanceledException)
            {
                state.Error = "Operation aborted";
                PieDiagnostics.Warning("[PieFileBridge] grep cancelled");
            }
            catch (Exception ex)
            {
                state.Error = ex.Message;
                PieDiagnostics.Error($"[PieFileBridge] grep error: {ex.Message}");
            }
            finally
            {
                state.IsComplete = true;
            }
        }

        private static FindRequestResult ExecuteFind(string rootPath, string pattern, int limit, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
                throw new DirectoryNotFoundException($"Path not found: {rootPath}");

            if (string.IsNullOrWhiteSpace(pattern))
                throw new ArgumentException("Pattern must not be empty");

            var results = new List<string>();
            var queue = new Queue<string>();
            queue.Enqueue(rootPath);
            int scannedDirectories = 0;
            int scannedFiles = 0;
            bool limitReached = false;

            while (queue.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                var dir = queue.Dequeue();
                scannedDirectories++;

                string[] entries;
                try
                {
                    entries = Directory.GetFileSystemEntries(dir);
                }
                catch
                {
                    continue;
                }

                Array.Sort(entries, StringComparer.OrdinalIgnoreCase);

                foreach (var entryPath in entries)
                {
                    token.ThrowIfCancellationRequested();

                    bool isDirectory;
                    try
                    {
                        isDirectory = Directory.Exists(entryPath);
                    }
                    catch
                    {
                        continue;
                    }

                    var entryName = Path.GetFileName(entryPath);
                    if (isDirectory)
                    {
                        if (_skipDirSet.Contains(entryName))
                            continue;

                        queue.Enqueue(entryPath);
                        continue;
                    }

                    if (!File.Exists(entryPath))
                        continue;

                    scannedFiles++;
                    var relativePath = MakeRelativePath(rootPath, entryPath);
                    if (MatchesFindPattern(relativePath, entryName, pattern))
                    {
                        results.Add(relativePath);
                        if (results.Count >= limit)
                        {
                            limitReached = true;
                            break;
                        }
                    }
                }

                if (limitReached)
                    break;
            }

            return new FindRequestResult
            {
                Results = results.ToArray(),
                ScannedDirectories = scannedDirectories,
                ScannedFiles = scannedFiles,
                LimitReached = limitReached,
                Pattern = pattern,
                RootPath = rootPath,
            };
        }

        private static GrepRequestResult ExecuteGrep(string searchPath, string pattern, string globPattern, bool ignoreCase, bool literal, int contextLines, int limit, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(searchPath))
                throw new ArgumentException("Search path must not be empty");

            bool isDirectory = Directory.Exists(searchPath);
            bool isFile = File.Exists(searchPath);
            if (!isDirectory && !isFile)
                throw new FileNotFoundException($"Path not found: {searchPath}");

            var regex = BuildRegex(pattern, ignoreCase, literal);
            var files = isDirectory ? CollectSearchFiles(searchPath, globPattern, token) : new List<string> { searchPath };
            files.Sort(StringComparer.OrdinalIgnoreCase);

            var outputLines = new List<string>();
            int matchCount = 0;
            bool matchLimitReached = false;
            bool linesTruncated = false;
            int filesScanned = 0;

            foreach (var filePath in files)
            {
                token.ThrowIfCancellationRequested();

                if (matchCount >= limit)
                {
                    matchLimitReached = true;
                    break;
                }

                string content;
                try
                {
                    content = File.ReadAllText(filePath);
                }
                catch
                {
                    continue;
                }

                filesScanned++;
                var lines = content.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
                var relativePath = isDirectory ? MakeRelativePath(searchPath, filePath) : Path.GetFileName(filePath);

                for (int lineIdx = 0; lineIdx < lines.Length; lineIdx++)
                {
                    if (matchCount >= limit)
                    {
                        matchLimitReached = true;
                        break;
                    }

                    if (!regex.IsMatch(lines[lineIdx]))
                        continue;

                    matchCount++;
                    int start = contextLines > 0 ? Math.Max(0, lineIdx - contextLines) : lineIdx;
                    int end = contextLines > 0 ? Math.Min(lines.Length - 1, lineIdx + contextLines) : lineIdx;

                    for (int c = start; c <= end; c++)
                    {
                        string lineText = lines[c];
                        if (lineText.Length > GrepMaxLineLength)
                        {
                            lineText = lineText.Substring(0, GrepMaxLineLength) + "...";
                            linesTruncated = true;
                        }

                        int currentLineNum = c + 1;
                        if (c == lineIdx)
                            outputLines.Add(relativePath + ":" + currentLineNum + ": " + lineText);
                        else
                            outputLines.Add(relativePath + "-" + currentLineNum + "- " + lineText);
                    }
                }
            }

            return new GrepRequestResult
            {
                Lines = outputLines.ToArray(),
                MatchCount = matchCount,
                MatchLimitReached = matchLimitReached,
                LinesTruncated = linesTruncated,
                FilesScanned = filesScanned,
                Pattern = pattern,
                SearchPath = searchPath,
                Glob = globPattern,
                Literal = literal,
                IgnoreCase = ignoreCase,
            };
        }

        private static List<string> CollectSearchFiles(string rootPath, string globPattern, CancellationToken token)
        {
            var files = new List<string>();
            var queue = new Queue<string>();
            queue.Enqueue(rootPath);

            while (queue.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                var dir = queue.Dequeue();
                string[] entries;
                try
                {
                    entries = Directory.GetFileSystemEntries(dir);
                }
                catch
                {
                    continue;
                }

                foreach (var entryPath in entries)
                {
                    token.ThrowIfCancellationRequested();

                    bool isDirectory;
                    try
                    {
                        isDirectory = Directory.Exists(entryPath);
                    }
                    catch
                    {
                        continue;
                    }

                    var entryName = Path.GetFileName(entryPath);
                    if (isDirectory)
                    {
                        if (!_skipDirSet.Contains(entryName))
                            queue.Enqueue(entryPath);
                        continue;
                    }

                    if (!File.Exists(entryPath))
                        continue;

                    string ext = Path.GetExtension(entryName);
                    if (_binaryExtensionSet.Contains(ext))
                        continue;

                    if (!MatchesSimpleGlob(entryName, globPattern))
                        continue;

                    files.Add(entryPath);
                }
            }

            return files;
        }

        private static Regex BuildRegex(string pattern, bool ignoreCase, bool literal)
        {
            if (literal)
                pattern = Regex.Escape(pattern);

            var options = RegexOptions.Multiline;
            if (ignoreCase)
                options |= RegexOptions.IgnoreCase;

            return new Regex(pattern, options);
        }

        private static bool MatchesSimpleGlob(string entryName, string globPattern)
        {
            if (string.IsNullOrWhiteSpace(globPattern))
                return true;

            if (globPattern.StartsWith("*.", StringComparison.Ordinal))
                return string.Equals(Path.GetExtension(entryName), globPattern.Substring(1), StringComparison.OrdinalIgnoreCase);

            if (globPattern.StartsWith("**/*.", StringComparison.Ordinal))
                return string.Equals(Path.GetExtension(entryName), globPattern.Substring(4), StringComparison.OrdinalIgnoreCase);

            return MatchesGlob(entryName, globPattern);
        }

        private static bool MatchesFindPattern(string relativePath, string entryName, string pattern)
        {
            var normalizedPattern = (pattern ?? string.Empty).Trim().Replace("\\", "/");
            var normalizedRelativePath = (relativePath ?? string.Empty).Replace("\\", "/");
            if (normalizedPattern.StartsWith("./", StringComparison.Ordinal))
                normalizedPattern = normalizedPattern.Substring(2);

            if (HasGlobChars(normalizedPattern))
            {
                if (normalizedPattern.IndexOf('/') < 0)
                    return MatchesGlob(entryName, normalizedPattern);

                if (normalizedPattern.StartsWith("**/", StringComparison.Ordinal) &&
                    MatchesGlob(normalizedRelativePath, normalizedPattern.Substring(3)))
                    return true;

                return MatchesGlob(normalizedRelativePath, normalizedPattern);
            }

            return normalizedRelativePath.Equals(normalizedPattern, StringComparison.OrdinalIgnoreCase)
                || normalizedRelativePath.EndsWith("/" + normalizedPattern, StringComparison.OrdinalIgnoreCase)
                || entryName.Equals(normalizedPattern, StringComparison.OrdinalIgnoreCase)
                || entryName.IndexOf(normalizedPattern, StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedRelativePath.IndexOf(normalizedPattern, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasGlobChars(string pattern)
        {
            return pattern.IndexOf('*') >= 0 || pattern.IndexOf('?') >= 0;
        }

        private static bool MatchesGlob(string input, string pattern)
        {
            var regex = GlobToRegex(pattern);
            return regex.IsMatch(input);
        }

        private static Regex GlobToRegex(string pattern)
        {
            var normalizedPattern = (pattern ?? string.Empty).Replace("\\", "/");
            var sb = new System.Text.StringBuilder("^");

            for (int i = 0; i < normalizedPattern.Length; i++)
            {
                char c = normalizedPattern[i];

                if (c == '*')
                {
                    bool isDoubleStar = i + 1 < normalizedPattern.Length && normalizedPattern[i + 1] == '*';
                    if (isDoubleStar)
                    {
                        bool followedBySlash = i + 2 < normalizedPattern.Length && normalizedPattern[i + 2] == '/';
                        if (followedBySlash)
                        {
                            sb.Append("(?:.*/)?");
                            i += 2;
                        }
                        else
                        {
                            sb.Append(".*");
                            i += 1;
                        }
                    }
                    else
                    {
                        sb.Append("[^/]*");
                    }
                    continue;
                }

                if (c == '?')
                {
                    sb.Append("[^/]");
                    continue;
                }

                if (c == '/')
                {
                    sb.Append("[/\\\\]");
                    continue;
                }

                sb.Append(Regex.Escape(c.ToString()));
            }

            sb.Append("$");
            return new Regex(sb.ToString(), RegexOptions.IgnoreCase);
        }

        private static string MakeRelativePath(string rootPath, string fullPath)
        {
            string relativePath = fullPath.Substring(rootPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return relativePath.Replace("\\", "/");
        }
    }
}
