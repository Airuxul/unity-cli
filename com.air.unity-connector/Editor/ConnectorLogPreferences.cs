using UnityEditor;
using UnityEngine;

namespace Air.UnityConnector
{
    /// <summary>Persists <see cref="ConnectorLog.Enabled"/> in Editor (default on).</summary>
    [InitializeOnLoad]
    static class ConnectorLogPreferences
    {
        const string EditorPrefsKey = "Air.UnityConnector.ConsoleLogEnabled";

        static ConnectorLogPreferences() =>
            ConnectorLog.Enabled = EditorPrefs.GetBool(EditorPrefsKey, true);

        internal static void SetEnabled(bool enabled)
        {
            ConnectorLog.Enabled = enabled;
            EditorPrefs.SetBool(EditorPrefsKey, enabled);
        }

        [MenuItem("Air/Unity Connector/Console Logs", false, 200)]
        static void ToggleMenu()
        {
            var enabled = !ConnectorLog.Enabled;
            SetEnabled(enabled);
            Debug.Log($"[unity-connector] Console logs {(enabled ? "enabled" : "disabled")}.");
        }

        [MenuItem("Air/Unity Connector/Console Logs", true)]
        static bool ToggleMenuValidate()
        {
            Menu.SetChecked("Air/Unity Connector/Console Logs", ConnectorLog.Enabled);
            return true;
        }
    }
}
