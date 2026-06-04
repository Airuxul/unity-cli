using System;
using System.Collections.Generic;
using Air.UnityConnector.Invoke;
using Air.UnityConnector.Job;

namespace Air.UnityConnector.Http
{
    /// <summary>
    /// Per-host held HTTP responses until jobs reach a terminal state (CONN-10).
    /// </summary>
    public static class PendingHttpResponses
    {
        sealed class Pending
        {
            public string CommandId;
            public string RequestId;
            public Action<int, Dictionary<string, object>> WriteJson;
            public Action ReleaseCommandSlot;
        }

        static readonly Dictionary<string, List<Pending>> ByHost = new(StringComparer.OrdinalIgnoreCase);
        static readonly object Gate = new();

        public static void Register(
            string host,
            string commandId,
            string requestId,
            Action<int, Dictionary<string, object>> writeJson,
            Action releaseCommandSlot,
            Func<string, InvokeJobRecord> getJob)
        {
            if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(commandId) || writeJson == null || getJob == null)
                return;

            lock (Gate)
            {
                if (!ByHost.TryGetValue(host, out var list))
                {
                    list = new List<Pending>();
                    ByHost[host] = list;
                }

                list.Add(new Pending
                {
                    CommandId = commandId,
                    RequestId = requestId,
                    WriteJson = writeJson,
                    ReleaseCommandSlot = releaseCommandSlot,
                });
            }

            TryCompleteOne(host, commandId, getJob);
        }

        public static void TryCompleteAll(string host, Func<string, InvokeJobRecord> getJob)
        {
            if (string.IsNullOrEmpty(host) || getJob == null)
                return;

            Pending[] copy;
            lock (Gate)
            {
                if (!ByHost.TryGetValue(host, out var list) || list.Count == 0)
                    return;
                copy = list.ToArray();
            }

            foreach (var pending in copy)
                TryCompleteOne(host, pending.CommandId, getJob);
        }

        static void TryCompleteOne(string host, string commandId, Func<string, InvokeJobRecord> getJob)
        {
            Pending pending;
            lock (Gate)
            {
                if (!ByHost.TryGetValue(host, out var list))
                    return;
                pending = list.Find(p => p.CommandId == commandId);
                if (pending == null)
                    return;
            }

            var job = getJob(commandId);
            if (job == null || !IsTerminal(job.Status))
                return;

            var body = BuildPostBody(job, pending.RequestId);
            var status = job.Status == InvokeJobStatus.Succeeded ? 200 : 400;

            Remove(host, pending);
            pending.WriteJson(status, body);
            pending.ReleaseCommandSlot?.Invoke();
        }

        static bool IsTerminal(InvokeJobStatus status) =>
            status is InvokeJobStatus.Succeeded or InvokeJobStatus.Failed or InvokeJobStatus.Orphaned;

        static Dictionary<string, object> BuildPostBody(InvokeJobRecord job, string requestId)
        {
            var ok = job.Status == InvokeJobStatus.Succeeded;
            var response = JobResponseBuilder.ToResponse(job);
            object data = null;
            if (response != null && response.TryGetValue("result", out var result))
                data = result;

            var body = new Dictionary<string, object>
            {
                ["ok"] = ok,
                ["data"] = data,
                ["error"] = ok ? job.Error : (job.Error ?? "command_failed"),
                ["request_id"] = requestId ?? job.RequestId,
                ["command_id"] = job.Id,
                ["status"] = job.Status.ToString().ToLowerInvariant(),
            };

            if (response != null)
            {
                if (response.TryGetValue("code", out var code) && code != null)
                    body["code"] = code;
                if (response.TryGetValue("message", out var message) && message != null)
                    body["message"] = message;
            }

            return body;
        }

        static void Remove(string host, Pending pending)
        {
            lock (Gate)
            {
                if (ByHost.TryGetValue(host, out var list))
                    list.Remove(pending);
            }
        }
    }
}
