using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace UnityCliConnector.Editor.Services
{
    public static class ProfilerHierarchyService
    {
        public static Dictionary<string, object> Execute(CliParams p)
        {
            var action = p.GetString("action", "hierarchy")?.ToLowerInvariant();
            switch (action)
            {
                case "hierarchy":
                    return Hierarchy(p);
                case "enable":
                    Profiler.enabled = true;
                    ProfilerDriver.enabled = true;
                    return new Dictionary<string, object> { ["enabled"] = true };
                case "disable":
                    ProfilerDriver.enabled = false;
                    Profiler.enabled = false;
                    return new Dictionary<string, object> { ["enabled"] = false };
                case "status":
                    var first = ProfilerDriver.firstFrameIndex;
                    var last = ProfilerDriver.lastFrameIndex;
                    return new Dictionary<string, object>
                    {
                        ["enabled"] = ProfilerDriver.enabled,
                        ["first_frame"] = first,
                        ["last_frame"] = last,
                        ["frame_count"] = last >= first ? last - first + 1 : 0,
                        ["is_playing"] = Application.isPlaying,
                    };
                case "clear":
                    ProfilerDriver.ClearAllFrames();
                    return new Dictionary<string, object> { ["cleared"] = true };
                default:
                    throw new ArgumentException(
                        $"Unknown action '{action}'. Valid: hierarchy, enable, disable, status, clear.");
            }
        }

        private static Dictionary<string, object> Hierarchy(CliParams p)
        {
            if (!ProfilerDriver.enabled && ProfilerDriver.lastFrameIndex < 0)
                throw new InvalidOperationException(
                    "Profiler has no captured data. Run with action=enable or enable profiling in Editor.");

            var fromFrame = p.GetInt("from", -1) ?? -1;
            var toFrame = p.GetInt("to", -1) ?? -1;
            var framesCount = p.GetInt("frames", 0) ?? 0;

            if (fromFrame >= 0 || toFrame >= 0)
            {
                if (fromFrame < 0) fromFrame = ProfilerDriver.firstFrameIndex;
                if (toFrame < 0) toFrame = ProfilerDriver.lastFrameIndex;
                return AveragedHierarchy(p, fromFrame, toFrame);
            }

            if (framesCount > 1)
                return AveragedHierarchy(
                    p,
                    ProfilerDriver.lastFrameIndex - framesCount + 1,
                    ProfilerDriver.lastFrameIndex);

            var frameIndex = p.GetInt("frame", -1) ?? -1;
            if (frameIndex < 0)
                frameIndex = ProfilerDriver.lastFrameIndex;

            if (frameIndex < ProfilerDriver.firstFrameIndex || frameIndex > ProfilerDriver.lastFrameIndex)
            {
                throw new ArgumentException(
                    $"Frame {frameIndex} out of range [{ProfilerDriver.firstFrameIndex}..{ProfilerDriver.lastFrameIndex}].");
            }

            var threadIndex = p.GetInt("thread", 0) ?? 0;
            var rootName = p.GetString("root");
            var parentId = p.GetInt("parent");
            var minTime = p.GetFloat("min", 0f) ?? 0f;
            var sortBy = p.GetString("sort", "total")?.ToLowerInvariant() ?? "total";
            var maxItems = p.GetInt("max", 30) ?? 30;
            if (maxItems <= 0) maxItems = 30;
            var depth = p.GetInt("depth", 1) ?? 1;
            if (depth <= 0) depth = 999;

            var sortColumn = GetSortColumn(sortBy);
            using var frameData = ProfilerDriver.GetHierarchyFrameDataView(
                frameIndex,
                threadIndex,
                HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                sortColumn,
                false);

            if (frameData == null || !frameData.valid)
            {
                throw new InvalidOperationException(
                    $"No profiler data for frame {frameIndex}, thread {threadIndex}.");
            }

            var rootId = frameData.GetRootItemID();
            var rootChildIds = new List<int>();
            frameData.GetItemChildren(rootId, rootChildIds);

            var parent = rootId;
            var parentName = "(root)";
            if (!string.IsNullOrEmpty(rootName))
            {
                var found = FindItemByName(frameData, rootId, rootName);
                if (found < 0)
                    throw new ArgumentException($"No profiler item matching '{rootName}' found.");
                parent = found;
                parentName = frameData.GetItemName(found);
            }
            else if (parentId.HasValue)
            {
                parent = parentId.Value;
                parentName = frameData.GetItemName(parent);
            }

            var children = BuildChildren(frameData, parent, minTime, maxItems, depth);
            return new Dictionary<string, object>
            {
                ["mode"] = "frame",
                ["frame"] = frameIndex,
                ["thread_index"] = threadIndex,
                ["parent"] = parent,
                ["parent_name"] = parentName,
                ["depth"] = depth >= 999 ? 0 : depth,
                ["children"] = children,
            };
        }

        private static Dictionary<string, object> AveragedHierarchy(CliParams p, int fromFrame, int toFrame)
        {
            var firstAvail = ProfilerDriver.firstFrameIndex;
            var lastAvail = ProfilerDriver.lastFrameIndex;
            fromFrame = Math.Max(fromFrame, firstAvail);
            toFrame = Math.Min(toFrame, lastAvail);
            var frameCount = toFrame - fromFrame + 1;
            if (frameCount <= 0)
            {
                throw new ArgumentException(
                    $"No frames in range [{fromFrame}..{toFrame}]. Available: [{firstAvail}..{lastAvail}].");
            }

            var threadIndex = p.GetInt("thread", 0) ?? 0;
            var rootName = p.GetString("root");
            var minTime = p.GetFloat("min", 0f) ?? 0f;
            var sortBy = p.GetString("sort", "total")?.ToLowerInvariant() ?? "total";
            var maxItems = p.GetInt("max", 30) ?? 30;
            if (maxItems <= 0) maxItems = 30;
            var depth = p.GetInt("depth", 1) ?? 1;
            if (depth <= 0) depth = 999;

            var sortColumn = GetSortColumn(sortBy);
            var accumulated = new Dictionary<string, (float totalMs, float selfMs, long calls, int count)>();

            for (var frameIndex = fromFrame; frameIndex <= toFrame; frameIndex++)
            {
                using var frameData = ProfilerDriver.GetHierarchyFrameDataView(
                    frameIndex,
                    threadIndex,
                    HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                    sortColumn,
                    false);

                if (frameData == null || !frameData.valid)
                    continue;

                var rootId = frameData.GetRootItemID();
                var rootChildIds = new List<int>();
                frameData.GetItemChildren(rootId, rootChildIds);

                var parentId = rootId;
                if (!string.IsNullOrEmpty(rootName))
                {
                    var found = FindItemByName(frameData, rootId, rootName);
                    if (found >= 0)
                        parentId = found;
                }

                CollectFlat(frameData, parentId, depth, accumulated);
            }

            var items = accumulated
                .Select(kv => new Dictionary<string, object>
                {
                    ["name"] = kv.Key,
                    ["avg_total_ms"] = Math.Round(kv.Value.totalMs / kv.Value.count, 3),
                    ["avg_self_ms"] = Math.Round(kv.Value.selfMs / kv.Value.count, 3),
                    ["avg_calls"] = Math.Round((double)kv.Value.calls / kv.Value.count, 1),
                    ["appeared_in"] = kv.Value.count,
                })
                .Where(x => (float)x["avg_total_ms"] >= minTime)
                .OrderByDescending(x =>
                    sortBy == "self" ? (float)x["avg_self_ms"] : (float)x["avg_total_ms"])
                .Take(maxItems)
                .ToList();

            return new Dictionary<string, object>
            {
                ["mode"] = "averaged",
                ["frame_count"] = frameCount,
                ["from_frame"] = fromFrame,
                ["to_frame"] = toFrame,
                ["thread_index"] = threadIndex,
                ["root"] = string.IsNullOrEmpty(rootName) ? "(root)" : rootName,
                ["items"] = items,
            };
        }

        private static void CollectFlat(
            HierarchyFrameDataView frameData,
            int parentId,
            int remainingDepth,
            Dictionary<string, (float totalMs, float selfMs, long calls, int count)> acc)
        {
            var childIds = new List<int>();
            frameData.GetItemChildren(parentId, childIds);

            foreach (var childId in childIds)
            {
                var name = frameData.GetItemName(childId);
                var totalMs = frameData.GetItemColumnDataAsFloat(childId, HierarchyFrameDataView.columnTotalTime);
                var selfMs = frameData.GetItemColumnDataAsFloat(childId, HierarchyFrameDataView.columnSelfTime);
                var calls = (long)frameData.GetItemColumnDataAsFloat(childId, HierarchyFrameDataView.columnCalls);

                if (acc.TryGetValue(name, out var existing))
                    acc[name] = (existing.totalMs + totalMs, existing.selfMs + selfMs, existing.calls + calls, existing.count + 1);
                else
                    acc[name] = (totalMs, selfMs, calls, 1);

                if (remainingDepth > 1)
                    CollectFlat(frameData, childId, remainingDepth - 1, acc);
            }
        }

        private static int FindItemByName(HierarchyFrameDataView frameData, int parentId, string name)
        {
            var childIds = new List<int>();
            frameData.GetItemChildren(parentId, childIds);

            foreach (var childId in childIds)
            {
                var itemName = frameData.GetItemName(childId);
                if (itemName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                    return childId;

                var found = FindItemByName(frameData, childId, name);
                if (found >= 0)
                    return found;
            }

            return -1;
        }

        private static int GetSortColumn(string sortBy)
        {
            switch (sortBy)
            {
                case "self":
                    return HierarchyFrameDataView.columnSelfTime;
                case "calls":
                    return HierarchyFrameDataView.columnCalls;
                default:
                    return HierarchyFrameDataView.columnTotalTime;
            }
        }

        private static List<Dictionary<string, object>> BuildChildren(
            HierarchyFrameDataView frameData,
            int parentId,
            float minTime,
            int maxItems,
            int remainingDepth)
        {
            var childIds = new List<int>();
            frameData.GetItemChildren(parentId, childIds);

            var items = new List<Dictionary<string, object>>();
            var shown = 0;
            foreach (var childId in childIds)
            {
                var totalTime = frameData.GetItemColumnDataAsFloat(childId, HierarchyFrameDataView.columnTotalTime);
                if (totalTime < minTime)
                    continue;
                if (shown >= maxItems)
                    break;
                shown++;

                var selfTime = frameData.GetItemColumnDataAsFloat(childId, HierarchyFrameDataView.columnSelfTime);
                var calls = (int)frameData.GetItemColumnDataAsFloat(childId, HierarchyFrameDataView.columnCalls);

                var item = new Dictionary<string, object>
                {
                    ["item_id"] = childId,
                    ["name"] = frameData.GetItemName(childId),
                    ["total_ms"] = Math.Round(totalTime, 3),
                    ["self_ms"] = Math.Round(selfTime, 3),
                    ["calls"] = calls,
                };

                if (remainingDepth > 1)
                {
                    var sub = BuildChildren(frameData, childId, minTime, maxItems, remainingDepth - 1);
                    if (sub.Count > 0)
                        item["children"] = sub;
                }

                items.Add(item);
            }

            return items;
        }
    }
}
