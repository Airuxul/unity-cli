using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UnityCliConnector.Editor.Services
{
    public static class ReserializeService
    {
        public static Dictionary<string, object> Reserialize(CliParams p)
        {
            var paths = p.GetStringArray("paths");
            if (paths.Length == 0)
            {
                var single = p.GetString("path");
                if (!string.IsNullOrEmpty(single))
                    paths = new[] { single };
            }

            if (paths.Length == 0)
            {
                AssetDatabase.ForceReserializeAssets();
                Debug.Log("[unity-connector] ForceReserializeAssets: entire project");
                return new Dictionary<string, object>
                {
                    ["scope"] = "project",
                    ["count"] = 0,
                };
            }

            AssetDatabase.ForceReserializeAssets(paths);
            Debug.Log($"[unity-connector] ForceReserializeAssets: {string.Join(", ", paths)}");
            return new Dictionary<string, object>
            {
                ["scope"] = "paths",
                ["count"] = paths.Length,
                ["paths"] = paths,
            };
        }
    }
}
