using UnityEditor;
using UnityEditor.Compilation;
using Air.UnityConnector.Cli;
using Air.UnityConnector.Invoke;
using Air.UnityConnector.Server;

namespace Air.UnityConnector
{
    /// <summary>
    /// Forwards Unity lifecycle events to <see cref="EditorServerSupervisor"/> (single HTTP write path).
    /// </summary>
    [InitializeOnLoad]
    internal static class EditorConnectorBootstrap
    {
        private static bool _hooksInstalled;

        static EditorConnectorBootstrap()
        {
            ConnectorSerialization.EnsureRegistered();
            EditorHttpSession.BeginDomain();
            InstallHooks();
            EditorServerSupervisor.Instance.HandleDomainStart();
            RequestEnsureRunningAfterDomain();
            EditorApplication.update += static () => EditorConnectorServer.Instance.Scheduler.Drain();
        }

        static void RequestEnsureRunningAfterDomain()
        {
            if (EditorHttpSession.DomainReloading)
            {
                EditorServerDiagnostics.Decision(
                    "RequestEnsureRunningAfterDomain",
                    "defer:domain_reloading (OnCompilationFinished will ensure)");
                return;
            }

            var delay = EditorServerSupervisor.Instance.IsHttpTransitionUnstable() ? 12 : 0;
            EditorServerSupervisor.RequestEnsureRunning(delay);
        }

        public static void RequestEnsureRunning(int delayFrames = 1) =>
            EditorServerSupervisor.RequestEnsureRunning(delayFrames);

        public static void Stop() => EditorServerSupervisor.Instance.RequestDrain();

        /// <summary>Drain and restart Editor HTTP (:6547). Safe recovery when CLI reports listener_restarting.</summary>
        public static void RequestControlledRestart() =>
            EditorServerSupervisor.Instance.RequestControlledRestart();

        internal static void LogThrottled(string message) =>
            EditorServerSupervisor.LogThrottled(message);

        internal static void LogConnectorError(string message) =>
            EditorServerSupervisor.LogConnectorError(message);

        internal static bool IsHttpTransitionUnstable() =>
            EditorServerSupervisor.Instance.IsHttpTransitionUnstable();

        private static void InstallHooks()
        {
            if (_hooksInstalled)
                return;
            _hooksInstalled = true;

            EditorApplication.quitting += Stop;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += OnEditorUpdate;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
        }

        private static void OnCompilationFinished(object _)
        {
            EditorPlayModePortCleanup.StopPlayerHttpIfNeeded("OnCompilationFinished");
            EditorHttpSession.SetDomainReloading(false, "OnCompilationFinished");
            EditorServerSupervisor.Instance.ResetTransientBackoff();
            EditorServerSupervisor.RequestEnsureRunning(4);
        }

        private static void OnBeforeAssemblyReload()
        {
            EditorServerDiagnostics.Trace("OnBeforeAssemblyReload", null);
            EditorPlayModePortCleanup.StopPlayerHttpIfNeeded("OnBeforeAssemblyReload");
            CliCommandDiscovery.Invalidate();
            InvokeCatalog.ClearCachedVersions();
            EditorInstanceFile.MarkReloading();
            EditorServerSupervisor.Instance.RequestDrain();
        }

        private static void OnAfterAssemblyReload()
        {
            EditorHttpSession.SetDomainReloading(true, "OnAfterAssemblyReload");
            EditorInstanceFile.MarkReloading();
            EditorJobStateManager.Reload();
            EditorServerSupervisor.Instance.OnAfterDomainReload();
            EditorServerSupervisor.Instance.HandleDomainStart();
            EditorApplication.delayCall += TryEnsureRunningAfterReloadSettled;
        }

        static void TryEnsureRunningAfterReloadSettled()
        {
            if (!EditorHttpSession.DomainReloading)
                return;

            if (EditorPlayState.IsCompiling || EditorPlayState.IsUpdating)
            {
                EditorApplication.delayCall += TryEnsureRunningAfterReloadSettled;
                return;
            }

            EditorServerSupervisor.Instance.TryRecoverStuckDomainReload();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            EditorServerDiagnostics.Trace("playModeStateChanged", state.ToString());
            EditorPlayModePortCleanup.StopPlayerHttpIfNeeded(state.ToString());
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                case PlayModeStateChange.ExitingPlayMode:
                    EditorServerSupervisor.Instance.MarkPlayTransition();
                    return;

                case PlayModeStateChange.EnteredPlayMode:
                case PlayModeStateChange.EnteredEditMode:
                    EditorServerSupervisor.Instance.OnPlayModeSettled();
                    return;
            }
        }

        private static void OnEditorUpdate() =>
            EditorServerSupervisor.Instance.OnWatchdog();
    }
}
