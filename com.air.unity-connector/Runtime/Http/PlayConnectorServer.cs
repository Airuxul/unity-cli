using System;
using Air.UnityConnector.Host;
using Air.UnityConnector.Http;
using UnityEngine;

namespace Air.UnityConnector
{
    /// <summary>
    /// Play-mode HTTP server: lives for Editor Play or Development Build player session.
    /// </summary>
    internal sealed class PlayConnectorServer : IConnectorServer
    {
        private const string BridgeNamePrefix = "Air.UnityConnector.Bridge.";
        private readonly ConnectorServerCore _core;
        private GameObject _driver;

        public PlayConnectorServer(string hostName, string label, Func<int> resolvePort)
        {
            HostName = hostName;
            var host = new PlayModeInvokeHost(hostName);
            var commands = new RuntimeJobStore(hostName);
            _core = ConnectorServerFactory.Create(
                host,
                resolvePort,
                label,
                commands.GetCommandResponse,
                commands,
                ConnectorHealthMetadata.Default,
                onBeforeDrain: () =>
                {
                    RuntimeJobStateManager.Tick(hostName);
                    PendingHttpResponses.TryCompleteAll(hostName, id => RuntimeJobStateManager.Get(hostName, id));
                },
                onStarted: EnsureBridge,
                registerPendingHttp: (commandId, requestId, writeJson, releaseSlot) =>
                    PendingHttpResponses.Register(
                        hostName,
                        commandId,
                        requestId,
                        writeJson,
                        () =>
                        {
                            releaseSlot?.Invoke();
                            _core.Scheduler.ReleaseCommandSlot();
                        },
                        id => RuntimeJobStateManager.Get(hostName, id)));
        }

        public string HostName { get; }

        public bool IsListening => _core.IsListening;

        public ConnectorHttpEndpoint Endpoint => _core.Endpoint;

        public void Start()
        {
            ConnectorSerialization.EnsureRegistered();
            _core.TryStart(ConnectorLog.LogCallback, ConnectorLog.LogErrorCallback);
        }

        public void Stop()
        {
            _core.Stop(ConnectorLog.LogCallback);
            DisposeBridge();
        }

        public void StopForAssemblyReload()
        {
            _core.Stop(ConnectorLog.LogCallback);
            DisposeBridge(immediate: true);
            CleanupStaleBridgeObjects(immediate: true);
        }

        public void CleanupStaleBridgesNow() => CleanupStaleBridgeObjects();

        private void EnsureBridge()
        {
            if (_driver != null)
                return;

            CleanupStaleBridgeObjects();

            _driver = new GameObject($"{BridgeNamePrefix}{HostName}")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            UnityEngine.Object.DontDestroyOnLoad(_driver);
            _driver.AddComponent<BridgeDriver>().Init(_core.Scheduler);
        }

        private void DisposeBridge(bool immediate = false)
        {
            if (_driver == null)
                return;

            var go = _driver;
            _driver = null;

            if (!immediate && Application.isPlaying)
                UnityEngine.Object.Destroy(go);
            else
                UnityEngine.Object.DestroyImmediate(go);
        }

        private static void CleanupStaleBridgeObjects(bool immediate = false)
        {
            var allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (var go in allObjects)
            {
                if (go == null || string.IsNullOrEmpty(go.name))
                    continue;
                if (!IsBridgeObjectName(go.name))
                    continue;

                var components = go.GetComponents<Component>();
                var hasMissingScript = false;
                foreach (var component in components)
                {
                    if (component == null)
                    {
                        hasMissingScript = true;
                        break;
                    }
                }

                if (!hasMissingScript)
                    continue;

                if (!immediate && Application.isPlaying)
                    UnityEngine.Object.Destroy(go);
                else
                    UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static bool IsBridgeObjectName(string name) =>
            name.StartsWith(BridgeNamePrefix, StringComparison.Ordinal);

        private sealed class BridgeDriver : MonoBehaviour
        {
            private ConnectorMainThreadScheduler _scheduler;

            public void Init(ConnectorMainThreadScheduler scheduler) => _scheduler = scheduler;

            private void Update() => _scheduler?.Drain();
        }
    }
}
