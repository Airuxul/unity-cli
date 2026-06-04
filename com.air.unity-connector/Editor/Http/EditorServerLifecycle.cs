using System;
using System.IO;
using Air.UnityConnector.Cli;
using Air.UnityConnector.Host;
using Air.UnityConnector.Http;
using Air.UnityConnector.Invoke;
using UnityEditor;
using UnityEngine;

namespace Air.UnityConnector.Server
{
    /// <summary>Shared stop/start/catalog operations for <see cref="EditorServerSupervisor"/> (single write path).</summary>
    internal static class EditorServerLifecycle
    {
        internal const int HealthTimeoutMs = 2500;
        internal const int HealthProbeAttempts = 8;
        internal const double MaxBackoffSeconds = 60.0;
        internal const double ForeignPortBackoffSeconds = 60.0;

        internal enum StartAttemptResult
        {
            Running,
            CacheReconciled,
            ForeignPort,
            Failed,
        }

        internal static void PerformStop(string site)
        {
            var server = EditorConnectorServer.Instance;
            var wasRunning = server.IsListening;
            var port = server.Port;

            EditorServerDiagnostics.Trace(site, $"PerformStop wasRunning={wasRunning}");
            EditorHttpSession.MarkCatalogNotReady();
            server.Stop();
            server.ClearListenerId();

            EditorHttpLocalCache.MarkStopped(
                EditorHttpSession.SessionId,
                EditorHttpSession.Generation,
                port,
                EditorServerSupervisorPhase.Stopped);

            EditorHttpSession.SetListenerActive(false, site);
            EditorInstanceFile.PublishSnapshot();

            if (wasRunning)
                ConnectorLog.Log($"[unity-connector] Editor HTTP server stopped (port {port}).");
        }

        internal static EditorHttpLocalCache.StartupAction ApplyCacheOnDomainStart()
        {
            var port = HostNetwork.ResolveEditorPort();
            return EditorHttpLocalCache.ReconcileOnDomainStart(
                EditorHttpSession.SessionId,
                EditorHttpSession.Generation,
                port);
        }

        internal static StartAttemptResult TryStartListening()
        {
            var server = EditorConnectorServer.Instance;
            var port = HostNetwork.ResolveEditorPort();

            if (server.TryDescribeRunningCache(out var cacheReason))
            {
                EditorServerDiagnostics.Trace("TryStartListening", $"path=cache_hit reason={cacheReason}");
                if (!EditorHttpSession.IsCommandReady)
                    ReconcileRunningCache(cacheReason, "TryStartListening(cache_hit)");
                if (!EditorHttpSession.IsCommandReady)
                {
                    EditorConnectorStartupLog.Record(
                        "TryStartListening(cache_hit)",
                        "cache hit but commands_ready=false (catalog not warmed)");
                    return StartAttemptResult.Failed;
                }

                return StartAttemptResult.CacheReconciled;
            }

            // Disk cache can lag (e.g. after Play/Stop) while the listener is still healthy — do not PerformStop.
            if (server.IsListening
                && server.TryProbeHealth(HealthTimeoutMs, HealthProbeAttempts, out _))
            {
                EditorServerDiagnostics.Trace("TryStartListening", "path=reuse_listener");
                if (string.IsNullOrEmpty(server.ListenerId))
                    server.SetListenerId(Guid.NewGuid().ToString("N"));
                EditorHttpSession.SetListenerActive(true, "TryStartListening(reuse)");
                EditorHttpSession.SetDomainReloading(false, "TryStartListening(reuse)");
                EditorHttpLocalCache.MarkRunning(
                    EditorHttpSession.SessionId,
                    EditorHttpSession.Generation,
                    port,
                    server.ListenerId,
                    EditorServerSupervisorPhase.Running);
                EditorConnectorStartupLog.Clear();
                EditorInstanceFile.NotifyHttpServing();
                WarmCatalogIfNeeded();
                return StartAttemptResult.Running;
            }

            if (server.IsListening)
                EditorServerDiagnostics.Trace(
                    "TryStartListening",
                    "path=cold_start listener_was_up health_probe_failed");

            var prepare = EditorHttpLocalCache.PrepareForStart(
                EditorHttpSession.SessionId,
                EditorHttpSession.Generation,
                port);

            if (prepare == EditorHttpLocalCache.PrepareResult.PortOwnedByOtherProcess)
            {
                EditorPlayModePortCleanup.StopPlayerHttpIfNeeded("TryStartListening(port_busy)");
                EditorHttpLocalCache.WaitForPortAvailable(port, 1500);
                prepare = EditorHttpLocalCache.PrepareForStart(
                    EditorHttpSession.SessionId,
                    EditorHttpSession.Generation,
                    port);
                if (prepare == EditorHttpLocalCache.PrepareResult.PortOwnedByOtherProcess)
                {
                    var portMsg = ConnectorHttpLifecycle.FormatPortInUseMessage(
                        "Editor HTTP server",
                        "127.0.0.1",
                        port,
                        "another Unity instance or stale listener");
                    if (EditorServerSupervisor.Instance.IsHttpTransitionUnstable())
                        EditorServerSupervisor.LogThrottled(portMsg);
                    else
                        EditorServerSupervisor.LogConnectorError(portMsg);
                    return StartAttemptResult.ForeignPort;
                }
            }

            EditorServerDiagnostics.Trace("TryStartListening", "path=prebind_stop");
            PerformStop("TryStartListening(prebind)");

            // Skip TCP pre-check after local Stop: TIME_WAIT can look "open" without a listener.
            var bound = false;
            var boundPort = port;
            foreach (var candidate in HostNetwork.ResolveEditorPortCandidates())
            {
                if (server.TryStartOnPort(candidate, requirePortFree: false))
                {
                    bound = true;
                    boundPort = candidate;
                    break;
                }
            }

            if (!bound)
            {
                EditorConnectorStartupLog.Record(
                    "TryStartListening",
                    "TryStart returned false for all port candidates (see prior LogError from HttpListener bind)");
                EditorHttpLocalCache.MarkStopped(
                    EditorHttpSession.SessionId,
                    EditorHttpSession.Generation,
                    port,
                    EditorServerSupervisorPhase.Stopped);
                return StartAttemptResult.Failed;
            }

            port = boundPort;

            if (!server.TryProbeHealth(HealthTimeoutMs, HealthProbeAttempts, out var healthError))
            {
                var healthMsg =
                    "health probe failed after bind"
                    + (string.IsNullOrEmpty(healthError) ? "" : ": " + healthError);
                EditorServerDiagnostics.Trace("TryStartListening(health)", healthMsg);
                if (EditorServerSupervisor.Instance.IsHttpTransitionUnstable() && server.IsListening)
                {
                    EditorServerSupervisor.LogThrottled("[unity-connector] " + healthMsg + " (keeping listener for retry)");
                    return StartAttemptResult.Failed;
                }

                if (EditorServerSupervisor.Instance.IsHttpTransitionUnstable())
                    EditorServerSupervisor.LogThrottled("[unity-connector] " + healthMsg);
                else
                    EditorConnectorStartupLog.Record("TryStartListening(health)", healthMsg);
                PerformStop("TryStartListening(health_failed)");
                return StartAttemptResult.Failed;
            }

            server.SetListenerId(Guid.NewGuid().ToString("N"));
            EditorHttpSession.SetListenerActive(true, "TryStartListening");
            EditorHttpSession.SetDomainReloading(false, "TryStartListening");
            EditorHttpLocalCache.MarkRunning(
                EditorHttpSession.SessionId,
                EditorHttpSession.Generation,
                port,
                server.ListenerId,
                EditorServerSupervisorPhase.Running);

            ConnectorLog.Log(
                $"[unity-connector] Editor HTTP server started (port {port}, host editor, build {ConnectorBuild.Id}).");
            EditorServerDiagnostics.Trace("TryStartListening", "path=started_fresh");
            EditorConnectorStartupLog.Clear();

            EditorInstanceFile.NotifyHttpServing();
            WarmCatalogIfNeeded();
            return StartAttemptResult.Running;
        }

        internal static void ReconcileRunningCache(string cacheReason, string site)
        {
            EditorHttpSession.SetListenerActive(true, site);
            EditorHttpSession.SetDomainReloading(false, site);
            WarmCatalogIfNeeded();
            EditorInstanceFile.NotifyHttpServing();
        }

        internal static void WarmCatalogIfNeeded()
        {
            if (EditorHttpSession.CatalogReady)
                return;

            if (InvokeRegistry.Instance == null)
            {
                EditorApplication.delayCall += WarmCatalogIfNeeded;
                return;
            }

            try
            {
                InvokeCatalog.BuildResponse(EditorInvokeHost.Instance.HostName);
                EditorHttpSession.MarkCatalogReady();
                EditorHttpSession.SetDomainReloading(false, "WarmCatalogIfNeeded(success)");
                EditorInstanceFile.NotifyHttpServing();
            }
            catch (Exception ex)
            {
                EditorHttpSession.MarkCatalogNotReady();
                EditorServerSupervisor.LogThrottled($"[unity-connector] Catalog warmup failed: {ex.Message}");
                EditorApplication.delayCall += WarmCatalogIfNeeded;
            }
        }
    }

    /// <summary>
    /// Last Editor HTTP start failure — always Console Error + ~/.unity-cmd/last-editor-startup-failure.txt.
    /// </summary>
    internal static class EditorConnectorStartupLog
    {
        private const string Prefix = "[unity-connector][startup-failure]";
        private const double RepeatCooldownSeconds = 15.0;
        private static readonly object Gate = new();
        private static double _lastRecordUtc;
        private static string _lastRecordKey;

        public static string LastSite { get; private set; }
        public static string LastMessage { get; private set; }
        public static string LastUtc { get; private set; }

        public static void Record(string site, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            var key = (site ?? "") + "|" + message;
            var now = EditorApplication.timeSinceStartup;
            lock (Gate)
            {
                if (key == _lastRecordKey && now - _lastRecordUtc < RepeatCooldownSeconds)
                    return;

                _lastRecordKey = key;
                _lastRecordUtc = now;
                LastSite = site ?? "";
                LastMessage = message;
                LastUtc = DateTime.UtcNow.ToString("o");
                ConnectorLog.LogError($"{Prefix} {LastSite}: {LastMessage}");
                WriteDiskUnsafe();
            }
        }

        public static void Clear()
        {
            lock (Gate)
            {
                LastSite = "";
                LastMessage = "";
                LastUtc = "";
                TryDeleteFailureFileUnsafe();
            }

            EditorServerDiagnostics.Trace("startup-failure", "cleared");
        }

        private static void TryDeleteFailureFileUnsafe()
        {
            try
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".unity-cmd",
                    "last-editor-startup-failure.txt");
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // ignored
            }
        }

        private static void WriteDiskUnsafe()
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".unity-cmd");
                Directory.CreateDirectory(dir);
                File.WriteAllText(
                    Path.Combine(dir, "last-editor-startup-failure.txt"),
                    $"utc={LastUtc}\nsite={LastSite}\nmessage={LastMessage}\n");
            }
            catch
            {
                // ignored
            }
        }
    }
}
