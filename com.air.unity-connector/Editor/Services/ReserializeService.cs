using System;
using System.Collections.Generic;
using System.Linq;
using Air.UnityConnector;
using Air.UnityConnector.Params;
using UnityEditor;
using UnityEngine;

namespace Air.UnityConnector.Editor.Services
{
    public static class ReserializeService
    {
        public static Dictionary<string, object> Reserialize(ReserializeParams p)
        {
            var paths = string.IsNullOrWhiteSpace(p.Paths)
                ? Array.Empty<string>()
                : p.Paths.Split(',', ';')
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0)
                    .ToArray();

            if (paths.Length == 0)
            {
                AssetDatabase.ForceReserializeAssets();
                ConnectorLog.Log("[unity-connector] ForceReserializeAssets: entire project");
                return new Dictionary<string, object>
                {
                    ["scope"] = "project",
                    ["count"] = 0,
                };
            }

            AssetDatabase.ForceReserializeAssets(paths);
            ConnectorLog.Log($"[unity-connector] ForceReserializeAssets: {string.Join(", ", paths)}");
            return new Dictionary<string, object>
            {
                ["scope"] = "paths",
                ["count"] = paths.Length,
                ["paths"] = paths,
            };
        }
    }
}
