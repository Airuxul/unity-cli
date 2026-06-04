using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Air.UnityConnector;
using Air.UnityConnector.Invoke;
using Air.UnityConnector.Http;
using Air.UnityConnector.Host;
using Air.UnityConnector.Server;
using Air.UnityConnector.State;

namespace Air.UnityConnector
{
    /// <summary>
    /// Editor HTTP server (:6547): domain-scoped session, disk cache, single-flight commands.
    /// </summary>
    internal sealed class EditorConnectorServer : IConnectorServer
    {
        public static readonly EditorConnectorServer Instance = new();

        private readonly ConnectorServerCore _core;
        private string _listenerId;

        EditorConnectorServer()
        {
            _core = ConnectorServerFactory.Create(
                EditorInvokeHost.Instance,
                HostNetwork.ResolveEditorPort,
                "Editor HTTP server",
                EditorJobStore.Instance.GetCommandResponse,
                EditorJobStore.Instance,
                new ConnectorHealthMetadata(
                    new ConnectorStateHealthProvider(
                        () => EditorStateProvider.CaptureConnectorStateForHealth(),
                        (payload, _) =>
                        {
                            payload["session_id"] = EditorHttpSession.SessionId;
                            payload["generation"] = EditorHttpSession.Generation;
                        })),
                EditorHttpSession.MarkCatalogReady,
                wakeMainThread: EditorEditorLoopWake.Force,
                canAcceptCommand: CanAcceptCommand,
                logLifecycle: false,
                registerPendingHttp: RegisterPendingHttp);
        }

        static void RegisterPendingHttp(
            string commandId,
            string requestId,
            Action<int, Dictionary<string, object>> writeJson,
            Action releaseSlot)
        {
            PendingHttpResponses.Register(
                HostKind.Editor,
                commandId,
                requestId,
                writeJson,
                () =>
                {
                    releaseSlot?.Invoke();
                    Instance.Scheduler.ReleaseCommandSlot();
                },
                EditorJobStateManager.Get);
        }

        static bool CanAcceptCommand()
        {
            return EditorHttpSession.ListenerActive;
        }

        public string HostName => _core.HostName;

        public bool IsListening => _core.IsListening;

        public static HostListenOptions ListenConfig => Instance._core.ListenConfig;

        public static bool IsListeningStatic => Instance.IsListening;

        internal ConnectorMainThreadScheduler Scheduler => _core.Scheduler;

        internal int Port => _core.Port;

        public string ListenerId => _listenerId;

        public void SetListenerId(string listenerId) => _listenerId = listenerId;

        public void ClearListenerId() => _listenerId = null;

        public void Start() => TryStart();

        public bool TryStart(bool requirePortFree = true) =>
            _core.TryStart(ConnectorLog.LogCallback, ConnectorLog.LogErrorCallback, requirePortFree);

        public bool TryStartOnPort(int port, bool requirePortFree = true) =>
            _core.TryStartOnPort(port, ConnectorLog.LogCallback, ConnectorLog.LogErrorCallback, requirePortFree);

        public void Stop() => _core.Stop(ConnectorLog.LogCallback);

        public bool TryProbeHealth(int timeoutMs, int attempts, out string error) =>
            _core.TryProbeHealth(
                EditorInvokeHost.Instance.HostName,
                ConnectorBuild.Id,
                EditorHttpSession.SessionId,
                timeoutMs,
                attempts,
                out error);

        public bool MatchesRunningCache() => TryDescribeRunningCache(out _);

        /// <summary>Explains why cache reconciliation does or does not match (for readiness logs).</summary>
        public bool TryDescribeRunningCache(out string reason)
        {
            if (!_core.IsListening)
            {
                reason = "IsListening=false";
                EditorServerDiagnostics.Trace("TryDescribeRunningCache", reason);
                return false;
            }

            if (!TryProbeHealth(
                    EditorServerLifecycle.HealthTimeoutMs,
                    EditorServerLifecycle.HealthProbeAttempts,
                    out var healthError))
            {
                reason = "health_probe_failed:" + (healthError ?? "unknown");
                EditorServerDiagnostics.Trace("TryDescribeRunningCache", reason);
                return false;
            }

            if (!EditorHttpLocalCache.MatchesRunningListener(
                    EditorHttpSession.SessionId,
                    _core.Port,
                    _listenerId))
            {
                reason = "disk_cache_listener_mismatch";
                EditorServerDiagnostics.Trace("TryDescribeRunningCache", reason);
                return false;
            }

            reason = "ok";
            return true;
        }
    }
}
