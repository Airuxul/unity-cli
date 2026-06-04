using System;
using System.IO;
using System.Text;
using Air.UnityConnector.Host;
using Air.UnityConnector.Server;
using UnityEditor;
using UnityEngine;

namespace Air.UnityConnector
{
    /// <summary>
    /// Supervisor / lifecycle trace for post-mortems. Filter Console: [unity-connector][supervisor]
    /// Full ring log: ~/.unity-cmd/editor-server-trace.log
    /// </summary>
    internal static class EditorServerDiagnostics
    {
        private const string Prefix = "[unity-connector][supervisor]";
        private const string TraceFileName = "editor-server-trace.log";
        private const int MaxFileBytes = 512 * 1024;
        private const double DuplicateCooldownSeconds = 0.25;

        private static readonly object Gate = new();
        private static string _lastKey;
        private static double _lastUtc;

        public static string TraceFilePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".unity-cmd",
                TraceFileName);

        public static void Phase(EditorServerSupervisorPhase from, EditorServerSupervisorPhase to, string site) =>
            Trace(site, $"phase {from} -> {to}");

        public static void Decision(string site, string decision) =>
            Trace(site, decision);

        public static void Trace(string site, string detail)
        {
            if (string.IsNullOrWhiteSpace(site))
                site = "unknown";

            var line = BuildLine(site, detail);
            var key = line;
            var now = EditorApplication.timeSinceStartup;
            lock (Gate)
            {
                if (key == _lastKey && now - _lastUtc < DuplicateCooldownSeconds)
                    return;

                _lastKey = key;
                _lastUtc = now;
            }

            ConnectorLog.Log($"{Prefix} {line}");
            AppendFile(line);
        }

        public static string CaptureSnapshot()
        {
            var server = EditorConnectorServer.Instance;
            var supervisor = EditorServerSupervisor.Instance;
            var sb = new StringBuilder(384);
            sb.Append($"phase={supervisor.Phase}");
            sb.Append($" gen={EditorHttpSession.Generation}");
            sb.Append($" session={Short(EditorHttpSession.SessionId)}");
            sb.Append($" IsListening={server.IsListening}");
            sb.Append($" ListenerActive={EditorHttpSession.ListenerActive}");
            sb.Append($" listenerId={Short(server.ListenerId)}");
            sb.Append($" DomainReloading={EditorHttpSession.DomainReloading}");
            sb.Append($" CatalogReady={EditorHttpSession.CatalogReady}");
            sb.Append($" cacheMatch={DescribeCacheMatchLight(server)}");
            sb.Append($" disk={EditorHttpLocalCache.DescribeForDiagnostics()}");
            sb.Append($" play={EditorPlayState.IsPlaying}/{EditorPlayState.IsPaused}");
            sb.Append($" compile={EditorPlayState.IsCompiling}");
            sb.Append($" update={EditorPlayState.IsUpdating}");
            sb.Append($" unstable={supervisor.IsHttpTransitionUnstable()}");
            sb.Append($" backoff={supervisor.IsInBackoff()}");
            sb.Append($" burst={supervisor.FailureBurst}");
            sb.Append($" pass={supervisor.EnsurePass}");
            sb.Append($" port={HostNetwork.ResolveEditorPort()}");
            return sb.ToString();
        }

        private static string BuildLine(string site, string detail)
        {
            var utc = DateTime.UtcNow.ToString("o");
            if (string.IsNullOrWhiteSpace(detail))
                return $"utc={utc} site={site} | {CaptureSnapshot()}";

            return $"utc={utc} site={site} {detail} | {CaptureSnapshot()}";
        }

        private static string Short(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "-";
            return value.Length <= 8 ? value : value.Substring(0, 8);
        }

        /// <summary>Disk/session match only — avoids /health probe (no reentrancy with TryDescribeRunningCache).</summary>
        private static string DescribeCacheMatchLight(EditorConnectorServer server)
        {
            if (!server.IsListening)
                return "not_listening";

            if (!EditorHttpLocalCache.MatchesRunningListener(
                    EditorHttpSession.SessionId,
                    server.Port,
                    server.ListenerId))
                return "disk_mismatch";

            return "ok";
        }

        private static void AppendFile(string line)
        {
            try
            {
                var path = TraceFilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? path);
                if (File.Exists(path) && new FileInfo(path).Length > MaxFileBytes)
                {
                    var lines = File.ReadAllLines(path);
                    var skip = Math.Max(0, lines.Length - 200);
                    var sb = new StringBuilder();
                    sb.AppendLine($"--- trace trimmed {DateTime.UtcNow:o} ---");
                    for (var i = skip; i < lines.Length; i++)
                        sb.AppendLine(lines[i]);
                    File.WriteAllText(path, sb.ToString());
                }

                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch
            {
                // ignored
            }
        }
    }
}
