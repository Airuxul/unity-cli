using System;
using System.IO;
using Air.UnityConnector.Host;
using Air.UnityConnector.Server;
using Air.UnityConnector.State;
using Air.UnityGameCore.Runtime.Serialization;
using UnityEditor;
using UnityEngine;

namespace Air.UnityConnector
{
    /// <summary>
    /// Per-project heartbeat under ~/.unity-cmd/instances/.
    /// <see cref="InstanceSnapshot.connector_state"/> = command pipeline;
    /// <see cref="InstanceSnapshot.play_mode"/> = Unity Play Mode.
    /// </summary>
    [InitializeOnLoad]
    internal static class EditorInstanceFile
    {
        const double WriteIntervalSeconds = 0.5;

        static readonly string InstancesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".unity-cmd",
            "instances");

        static double _lastWriteUtc;
        static string _forcedConnectorState;
        static string _filePath;

        static EditorInstanceFile()
        {
            EditorApplication.update += Tick;
            EditorApplication.quitting += MarkStopped;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public static void MarkReloading()
        {
            _forcedConnectorState = ConnectorPipelineState.Reloading;
            _lastWriteUtc = 0;
            EditorReadinessLog.Transition(
                "EditorInstanceFile.MarkReloading",
                "heartbeat forced connector_state=reloading");
            Write();
        }

        public static void MarkStopped()
        {
            _forcedConnectorState = ConnectorPipelineState.Stopped;
            Write();
        }

        public static void PublishSnapshot() => Write();

        public static void NotifyHttpServing()
        {
            _forcedConnectorState = null;
            _lastWriteUtc = 0;
            EditorReadinessLog.Transition(
                "EditorInstanceFile.NotifyHttpServing",
                "cleared forced connector heartbeat state");
            Write();
        }

        static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                _forcedConnectorState = ConnectorPipelineState.EnteringPlayMode;
                Write();
            }
        }

        static void Tick()
        {
            var needsHeartbeat = EditorConnectorServer.IsListeningStatic
                || _forcedConnectorState != null
                || EditorApplication.isCompiling
                || EditorApplication.isUpdating;

            if (!needsHeartbeat)
                return;

            var now = EditorApplication.timeSinceStartup;
            if (now - _lastWriteUtc < WriteIntervalSeconds)
                return;

            _lastWriteUtc = now;

            if (_forcedConnectorState == ConnectorPipelineState.Reloading)
            {
                if (EditorConnectorServer.IsListeningStatic
                    && !EditorHttpSession.DomainReloading
                    && EditorHttpSession.CatalogReady)
                {
                    _forcedConnectorState = null;
                }
                else
                {
                    Write();
                    return;
                }
            }

            if (_forcedConnectorState == ConnectorPipelineState.EnteringPlayMode)
            {
                if (!EditorApplication.isPlaying && !EditorApplication.isCompiling)
                    _forcedConnectorState = null;
                else
                {
                    Write();
                    return;
                }
            }

            _forcedConnectorState = null;
            Write();
        }

        static void Write()
        {
            var port = EditorConnectorServer.ListenConfig?.Port ?? HostNetwork.ResolveEditorPort();
            var projectPath = EditorProjectPaths.GetProjectPath();
            var snapshot = EditorStateProvider.CaptureConnectorState(_forcedConnectorState);

            var payload = new InstanceSnapshot
            {
                connector_state = snapshot.ConnectorState,
                play_mode = snapshot.PlayMode,
                commands_ready = snapshot.CommandsReady,
                projectPath = projectPath,
                host = HostKind.Editor,
                port = port,
                pid = System.Diagnostics.Process.GetCurrentProcess().Id,
                unityVersion = Application.unityVersion,
                connector_build = ConnectorBuild.Id,
                session_id = EditorHttpSession.SessionId,
                generation = EditorHttpSession.Generation,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                listener_running = EditorConnectorServer.IsListeningStatic,
                compile_errors = EditorUtility.scriptCompilationFailed,
                supervisor_phase = EditorServerSupervisor.Instance.Phase.ToString(),
                http_status = EditorConnectorServer.IsListeningStatic ? "running" : "stopped",
            };

            try
            {
                Directory.CreateDirectory(InstancesDir);
                var path = GetFilePath(projectPath);
                ConnectorSerialization.EnsureRegistered();
                var json = JsonSerialization.Serialize(payload);
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(path))
                    File.Replace(tmp, path, null);
                else
                    File.Move(tmp, path);
            }
            catch
            {
                // ignored
            }
        }

        static string GetFilePath(string projectPath)
        {
            if (!string.IsNullOrEmpty(_filePath))
                return _filePath;

            _filePath = EditorProjectPaths.InstancesFilePath();
            return _filePath;
        }

        [Serializable]
        sealed class InstanceSnapshot
        {
            public string connector_state;
            public string play_mode;
            public bool commands_ready;
            public string projectPath;
            public string host;
            public int port;
            public int pid;
            public string unityVersion;
            public int connector_build;
            public string session_id;
            public int generation;
            public long timestamp;
            public bool listener_running;
            public bool compile_errors;
            public string supervisor_phase;
            public string http_status;
        }
    }
}
