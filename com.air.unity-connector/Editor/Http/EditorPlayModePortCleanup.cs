using UnityEditor;

namespace Air.UnityConnector
{
    /// <summary>
    /// Editor must never keep <see cref="PlayerHttpHost"/> (:6795) alive — it steals the player port
    /// and can destabilize <see cref="EditorServerSupervisor"/> restarts after Play / domain reload.
    /// </summary>
    internal static class EditorPlayModePortCleanup
    {
        internal static void StopPlayerHttpIfNeeded(string site)
        {
            if (!PlayerHttpHost.IsRunning)
                return;

            PlayerHttpHost.Stop();
            ConnectorLog.Log($"[unity-connector] Stopped stray Player HTTP (:6795) ({site}).");
        }
    }
}
