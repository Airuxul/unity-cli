using System;
using System.Collections.Generic;
using UnityCliConnector.Http;

namespace UnityCliConnector
{
    public sealed class EditorRequestDispatcher : IRequestDispatcher
    {
        public const int ConnectorBuild = 6;

        public bool TryDispatch(string method, string path, string body, Action<int, Dictionary<string, object>> writeJson)
        {
            if (path == "/health" && method == "GET")
            {
                writeJson(200, new Dictionary<string, object>
                {
                    ["ok"] = true,
                    ["host"] = EditorCommandHost.Instance.HostName,
                    ["connector_build"] = ConnectorBuild,
                });
                return true;
            }

            if (path == "/list" && method == "POST")
            {
                writeJson(200, CommandCatalog.BuildResponse());
                return true;
            }

            if (path.StartsWith("/jobs/", StringComparison.Ordinal) && method == "GET")
            {
                var id = path.Substring("/jobs/".Length).Trim('/');
                var job = JobManager.Get(id);
                if (job == null)
                {
                    writeJson(404, new Dictionary<string, object> { ["ok"] = false, ["error"] = "job_not_found" });
                    return true;
                }

                writeJson(200, JobToResponse(job));
                return true;
            }

            if (path == "/command" && method == "POST")
            {
                var request = CommandHttpHelper.ParseCommandRequest(body, EditorCommandHost.Instance.HostName);
                var post = EditorCommandHost.Instance.HandleCommand(request);
                writeJson(post.StatusCode, post.Body);
                return true;
            }

            return false;
        }

        private static Dictionary<string, object> JobToResponse(JobRecord job)
        {
            var dict = new Dictionary<string, object>
            {
                ["ok"] = true,
                ["job_id"] = job.Id,
                ["status"] = job.Status.ToString().ToLowerInvariant(),
                ["command"] = job.Command,
                ["error"] = job.Error,
                ["request_id"] = job.RequestId,
            };
            if (job.Result != null)
                dict["result"] = job.Result;
            return dict;
        }
    }
}
