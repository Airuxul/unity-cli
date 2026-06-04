using UnityEngine;

namespace Air.UnityConnector
{
    /// <summary>
    /// Starts player HTTP (<c>:6795</c>) in standalone Development Build players only.
    /// Unity Editor Play uses <see cref="EditorPlayHttpHost"/> on <c>:6794</c>; do not bind <c>:6795</c> in the Editor.
    /// </summary>
    public static class DevPlayerBootstrap
    {
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoad]
        sealed class DevPlayerEditorPlayCleanup
        {
            static DevPlayerEditorPlayCleanup()
            {
                UnityEditor.EditorApplication.playModeStateChanged += OnEditorPlayModeChanged;
                UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
                UnityEditor.EditorApplication.quitting += OnQuitting;
                StopPlayerHttp("editor_init");
            }

            static void OnBeforeAssemblyReload() => StopPlayerHttp("before_assembly_reload");

            static void OnQuitting() => StopPlayerHttp("editor_quitting");

            static void OnEditorPlayModeChanged(UnityEditor.PlayModeStateChange state)
            {
                StopPlayerHttp(state.ToString());
            }

            static void StopPlayerHttp(string site)
            {
                if (!PlayerHttpHost.IsRunning)
                    return;
                PlayerHttpHost.Stop();
                UnityEngine.Debug.Log($"[unity-connector] Editor cleanup: Player HTTP (:6795) stopped ({site}).");
            }
        }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (Application.isEditor)
                return;

            PlayerHttpHost.Start();
        }
    }
}
