using UnityEditor;
using UnityEngine;

namespace Air.UnityConnector
{
    /// <summary>
    /// Filter Unity Console with: [unity-connector][readiness]
    /// Logs state transitions and failures only.
    /// </summary>
    internal static class EditorReadinessLog
    {
        const string Prefix = "[unity-connector][readiness]";

        public static void Transition(string site, string message)
        {
            ConnectorLog.Log($"{Prefix} {site}: {message} | {FormatFlags()}");
        }

        public static string FormatFlags() =>
            $"DomainReloading={EditorHttpSession.DomainReloading} " +
            $"CatalogReady={EditorHttpSession.CatalogReady} " +
            $"ListenerActive={EditorHttpSession.ListenerActive} " +
            $"IsListening={EditorConnectorServer.IsListeningStatic} " +
            $"EditorCompiling={EditorApplication.isCompiling} " +
            $"PlayMode={EditorPlayState.IsPlaying}/{EditorPlayState.IsPaused} " +
            $"build={ConnectorBuild.Id} " +
            $"gen={EditorHttpSession.Generation}";
    }
}
