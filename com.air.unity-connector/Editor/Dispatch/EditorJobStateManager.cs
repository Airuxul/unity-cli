using Air.UnityConnector.Job;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Air.UnityConnector.Invoke;
using Air.UnityConnector;
using Air.UnityConnector.Host;
using Air.UnityConnector.Http;

namespace Air.UnityConnector
{
    /// <summary>
    /// In-memory editor jobs with SessionState + <see cref="EditorJobLedger"/> (disk) for domain reload.
    /// </summary>
    [InitializeOnLoad]
    public static class EditorJobStateManager
    {
        private const string SessionKey = "Air.UnityConnector.Jobs";
        private static readonly Dictionary<string, ICompletionPolicy> Policies = new()
        {
            { InvokeCompletionCatalog.CompletionCompilation, new Completion.CompilationPolicy() },
            { InvokeCompletionCatalog.CompletionEnterPlay, new Completion.EnterPlayModePolicy() },
            { InvokeCompletionCatalog.CompletionExitPlay, new Completion.ExitPlayModePolicy() },
        };
        private static Dictionary<string, InvokeJobRecord> _commands = new();

        static EditorJobStateManager()
        {
            Reload();
            EditorApplication.update += Tick;
        }

        public static IReadOnlyDictionary<string, InvokeJobRecord> AllCommands => _commands;

        public static InvokeJobRecord Get(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            if (_commands.TryGetValue(id, out var command))
                return command;

            var fromDisk = EditorJobLedger.TryLoad(id);
            if (fromDisk != null)
                _commands[id] = fromDisk;
            return fromDisk;
        }

        public static InvokeJobRecord Create(string command, string completionKind, string requestId)
        {
            var entry = JobStateCore.Create(
                _commands,
                command,
                completionKind,
                requestId,
                JobStateCore.UtcNowMs,
                Save);
            EditorJobLedger.Persist(entry);
            return entry;
        }

        public static void Fail(string id, string error)
        {
            var command = Get(id);
            if (command == null || JobStateCore.ShouldSkip(command))
                return;

            JobStateCore.MarkFailed(command, error, JobStateCore.UtcNowMs(), Save);
        }

        public static void MarkRunning(string id)
        {
            var command = Get(id);
            if (command == null || JobStateCore.ShouldSkip(command))
                return;

            command.Status = InvokeJobStatus.Running;
            command.UpdatedAtUtcMs = JobStateCore.UtcNowMs();
            Save();
        }

        public static void SetCompletionKind(string id, string completionKind)
        {
            var command = Get(id);
            if (command == null || JobStateCore.ShouldSkip(command))
                return;

            command.CompletionKind = completionKind ?? "";
            command.UpdatedAtUtcMs = JobStateCore.UtcNowMs();
            Save();
        }

        public static void Succeed(string id, object result)
        {
            var command = Get(id);
            if (command == null || JobStateCore.ShouldSkip(command))
                return;

            JobStateCore.MarkSucceeded(command, result, JobStateCore.UtcNowMs(), true, Save);
        }

        /// <summary>Flush all in-flight jobs to disk before domain reload.</summary>
        public static void FlushToLedger() => EditorJobLedger.FlushAll(_commands.Values);

        /// <summary>SessionState + disk merge after domain reload.</summary>
        public static void Reload()
        {
            EditorJobLedger.PurgeCorruptFiles();
            LoadFromSession();
            EditorJobLedger.MergePendingInto(_commands);
            OrphanJobsAfterDomainReload();
            Save();
        }

        /// <summary>P1: pending/running jobs without a completion policy cannot resume after reload.</summary>
        static void OrphanJobsAfterDomainReload()
        {
            var now = JobStateCore.UtcNowMs();
            foreach (var command in _commands.Values.ToList())
            {
                if (JobStateCore.ShouldSkip(command))
                    continue;

                if (command.Status is not (InvokeJobStatus.Pending or InvokeJobStatus.Running))
                    continue;

                var kind = command.CompletionKind ?? "";
                if (Policies.ContainsKey(kind))
                    continue;

                if (string.Equals(kind, InvokeCompletionCatalog.CompletionDeferred, StringComparison.Ordinal))
                    continue;

                JobStateCore.MarkOrphaned(
                    command,
                    JobStateCore.ReloadOrphanErrorMessage,
                    now,
                    Save);
            }
        }

        private static void Tick()
        {
            if (_commands.Count == 0)
                return;

            var state = EditorStateProvider.Capture();
            JobStateCore.TickJobs(
                _commands.Values,
                Save,
                command => ResolveTryComplete(command, state),
                command => $"No completion policy for '{command.CompletionKind}'.",
                command => string.IsNullOrEmpty(command.CompletionKind)
                    || string.Equals(
                        command.CompletionKind,
                        InvokeCompletionCatalog.CompletionDeferred,
                        StringComparison.Ordinal),
                serializeResultJson: false);

            PendingHttpResponses.TryCompleteAll(HostKind.Editor, Get);
        }

        private static JobStateCore.TryCompleteJob ResolveTryComplete(
            InvokeJobRecord command,
            EditorStateSnapshot state)
        {
            if (!Policies.TryGetValue(command.CompletionKind ?? "", out var policy))
                return null;

            return (InvokeJobRecord current, out object result, out string error) =>
                policy.TryComplete(current, state, out result, out error);
        }

        private static void LoadFromSession()
        {
            var json = SessionState.GetString(SessionKey, "");
            if (string.IsNullOrEmpty(json))
            {
                _commands = new Dictionary<string, InvokeJobRecord>();
                return;
            }

            try
            {
                var wrapper = JsonUtility.FromJson<InvokeJobListWrapper>(json);
                _commands = new Dictionary<string, InvokeJobRecord>();
                if (wrapper?.Items == null)
                    return;

                foreach (var item in wrapper.Items)
                {
                    if (!string.IsNullOrEmpty(item?.Id))
                        _commands[item.Id] = item;
                }
            }
            catch
            {
                _commands = new Dictionary<string, InvokeJobRecord>();
            }
        }

        private static void Save()
        {
            if (!MainThread.IsCurrent)
            {
                ConnectorLog.LogWarning("[unity-connector] EditorJobStateManager.Save skipped (not main thread).");
                return;
            }

            var wrapper = new InvokeJobListWrapper { Items = _commands.Values.ToList() };
            SessionState.SetString(SessionKey, JsonUtility.ToJson(wrapper));

            foreach (var job in _commands.Values)
                EditorJobLedger.Persist(job);
        }
    }
}
