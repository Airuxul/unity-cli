using UnityEngine;

namespace Air.UnityConnector
{
    /// <summary>
    /// Gates unity-connector internal Unity Console output. Default: enabled.
    /// Does not affect CLI log commands (<see cref="Commands.RuntimeLogCommands"/>) or file trace logs.
    /// </summary>
    public static class ConnectorLog
    {
        static bool _enabled = true;

        /// <summary>When false, <see cref="Log"/>, <see cref="LogWarning"/>, and <see cref="LogError"/> are no-ops.</summary>
        public static bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        public static void Log(string message)
        {
            if (!Enabled || string.IsNullOrEmpty(message))
                return;

            Debug.Log(message);
        }

        public static void LogWarning(string message)
        {
            if (!Enabled || string.IsNullOrEmpty(message))
                return;

            Debug.LogWarning(message);
        }

        public static void LogError(string message)
        {
            if (!Enabled || string.IsNullOrEmpty(message))
                return;

            Debug.LogError(message);
        }

        /// <summary>Callback for <c>Action&lt;string&gt;</c> lifecycle hooks.</summary>
        public static void LogCallback(string message) => Log(message);

        /// <summary>Callback for error lifecycle hooks.</summary>
        public static void LogErrorCallback(string message) => LogError(message);
    }
}
