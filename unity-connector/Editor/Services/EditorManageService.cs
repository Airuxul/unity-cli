using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace UnityCliConnector.Editor.Services
{
    public static class EditorManageService
    {
        private const int FirstUserLayerIndex = 8;
        private const int TotalLayerCount = 32;
        private static readonly TimeSpan PlayModeTimeout = TimeSpan.FromSeconds(60);

        public static Dictionary<string, object> Execute(Dictionary<string, object> parameters)
        {
            var p = new CliParams(parameters);
            var action = p.GetString("action")?.ToLowerInvariant();
            if (string.IsNullOrEmpty(action))
                throw new ArgumentException("Parameter 'action' is required.");

            var wait = p.GetBool("wait_for_completion");

            switch (action)
            {
                case "play":
                    return Play(wait);
                case "pause":
                    return Pause();
                case "stop":
                    return Stop(wait);
                case "refresh":
                    return AssetRefreshService.Refresh(parameters);
                case "set_active_tool":
                    return SetActiveTool(p.GetString("tool_name"));
                case "add_tag":
                    return AddTag(p.GetString("tag_name"));
                case "remove_tag":
                    return RemoveTag(p.GetString("tag_name"));
                case "add_layer":
                    return ManageLayer("add_layer", p.GetString("layer_name"));
                case "remove_layer":
                    return ManageLayer("remove_layer", p.GetString("layer_name"));
                default:
                    throw new ArgumentException(
                        $"Unknown action '{action}'. Valid: play, stop, pause, refresh, set_active_tool, add_tag, remove_tag, add_layer, remove_layer.");
            }
        }

        private static Dictionary<string, object> Play(bool wait)
        {
            if (EditorApplication.isPlaying)
                return Result("Already in play mode.", new { is_playing = true });

            EditorApplication.isPlaying = true;
            if (wait && !WaitForPlayModeState(PlayModeStateChange.EnteredPlayMode, PlayModeTimeout))
                throw new TimeoutException("Timed out waiting to enter play mode.");

            return Result(wait ? "Entered play mode (confirmed)." : "Entered play mode.",
                new { is_playing = EditorApplication.isPlaying });
        }

        private static Dictionary<string, object> Stop(bool wait)
        {
            if (!EditorApplication.isPlaying)
                return Result("Already stopped (not in play mode).", new { is_playing = false });

            EditorApplication.isPlaying = false;
            if (wait && !WaitForPlayModeState(PlayModeStateChange.EnteredEditMode, PlayModeTimeout))
                throw new TimeoutException("Timed out waiting to exit play mode.");

            return Result(wait ? "Exited play mode (confirmed)." : "Exited play mode.",
                new { is_playing = EditorApplication.isPlaying });
        }

        private static Dictionary<string, object> Pause()
        {
            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("Cannot pause/resume: not in play mode.");

            EditorApplication.isPaused = !EditorApplication.isPaused;
            return Result(
                EditorApplication.isPaused ? "Game paused." : "Game resumed.",
                new { is_paused = EditorApplication.isPaused });
        }

        private static Dictionary<string, object> SetActiveTool(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
                throw new ArgumentException("Parameter 'tool_name' is required.");

            if (Enum.TryParse(toolName, true, out Tool targetTool) &&
                targetTool != Tool.None &&
                targetTool <= Tool.Custom)
            {
                Tools.current = targetTool;
                return Result($"Set active tool to '{targetTool}'.", new { tool = targetTool.ToString() });
            }

            throw new ArgumentException($"Could not parse '{toolName}' as a Unity Tool.");
        }

        private static Dictionary<string, object> AddTag(string tagName)
        {
            if (string.IsNullOrWhiteSpace(tagName))
                throw new ArgumentException("Parameter 'tag_name' is required.");
            if (Array.IndexOf(InternalEditorUtility.tags, tagName) >= 0)
                throw new InvalidOperationException($"Tag '{tagName}' already exists.");

            InternalEditorUtility.AddTag(tagName);
            AssetDatabase.SaveAssets();
            return Result($"Tag '{tagName}' added.", new { tag = tagName });
        }

        private static Dictionary<string, object> RemoveTag(string tagName)
        {
            if (string.IsNullOrWhiteSpace(tagName))
                throw new ArgumentException("Parameter 'tag_name' is required.");
            if (Array.IndexOf(InternalEditorUtility.tags, tagName) < 0)
                throw new InvalidOperationException($"Tag '{tagName}' does not exist.");

            InternalEditorUtility.RemoveTag(tagName);
            AssetDatabase.SaveAssets();
            return Result($"Tag '{tagName}' removed.", new { tag = tagName });
        }

        private static Dictionary<string, object> ManageLayer(string action, string layerName)
        {
            if (string.IsNullOrWhiteSpace(layerName))
                throw new ArgumentException("Parameter 'layer_name' is required.");

            var tagManagerAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (tagManagerAssets == null || tagManagerAssets.Length == 0)
                throw new InvalidOperationException("Could not access TagManager asset.");

            using var tagManager = new SerializedObject(tagManagerAssets[0]);
            var layersProp = tagManager.FindProperty("layers");
            if (layersProp == null || !layersProp.isArray)
                throw new InvalidOperationException("Could not find 'layers' property.");

            if (action == "add_layer")
            {
                var firstEmpty = -1;
                for (var i = 0; i < TotalLayerCount; i++)
                {
                    var sp = layersProp.GetArrayElementAtIndex(i);
                    if (sp != null &&
                        layerName.Equals(sp.stringValue, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Layer '{layerName}' already exists at index {i}.");
                    if (firstEmpty == -1 && i >= FirstUserLayerIndex &&
                        (sp == null || string.IsNullOrEmpty(sp.stringValue)))
                        firstEmpty = i;
                }

                if (firstEmpty == -1)
                    throw new InvalidOperationException("No empty layer slots available.");

                layersProp.GetArrayElementAtIndex(firstEmpty).stringValue = layerName;
                tagManager.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                return Result($"Layer '{layerName}' added to slot {firstEmpty}.",
                    new { layer = layerName, slot = firstEmpty });
            }

            for (var i = FirstUserLayerIndex; i < TotalLayerCount; i++)
            {
                var sp = layersProp.GetArrayElementAtIndex(i);
                if (sp != null && layerName.Equals(sp.stringValue, StringComparison.OrdinalIgnoreCase))
                {
                    sp.stringValue = string.Empty;
                    tagManager.ApplyModifiedProperties();
                    AssetDatabase.SaveAssets();
                    return Result($"Layer '{layerName}' removed from slot {i}.",
                        new { layer = layerName, slot = i });
                }
            }

            throw new InvalidOperationException($"User layer '{layerName}' not found.");
        }

        private static bool WaitForPlayModeState(PlayModeStateChange target, TimeSpan timeout)
        {
            if (target == PlayModeStateChange.EnteredPlayMode && EditorApplication.isPlaying)
                return true;
            if (target == PlayModeStateChange.EnteredEditMode && !EditorApplication.isPlaying)
                return true;

            var done = false;
            void OnChange(PlayModeStateChange state)
            {
                if (state == target)
                    done = true;
            }

            EditorApplication.playModeStateChanged += OnChange;
            try
            {
                var start = DateTime.UtcNow;
                while (!done)
                {
                    if (DateTime.UtcNow - start > timeout)
                        return false;
                    System.Threading.Thread.Sleep(25);
                }

                return true;
            }
            finally
            {
                EditorApplication.playModeStateChanged -= OnChange;
            }
        }

        private static Dictionary<string, object> Result(string message, object data) =>
            new Dictionary<string, object>
            {
                ["message"] = message,
                ["data"] = data,
            };
    }
}
