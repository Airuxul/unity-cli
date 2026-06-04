using System;
using System.Collections.Generic;
using Air.UnityConnector.Invoke;
using Air.UnityConnector;

namespace Air.UnityConnector.Http
{
    /// <summary>Shared drain logic for Editor and play-mode main-thread HTTP queues.</summary>
    public static class MainThreadHttpWork
    {
        public enum Kind
        {
            Command,
            Catalog,
            InvokeJobStatus,
        }

        public sealed class Item
        {
            public Kind Kind;
            public string Body;
            public string CommandId;
            public Action<int, Dictionary<string, object>> WriteJson;
            public bool HoldCommandSlot;
        }

        /// <returns>True when the command HTTP slot may be released (false while holding connection for async completion).</returns>
        public static bool Process(
            Item item,
            IInvokeHost host,
            Func<string, Dictionary<string, object>> getInvokeJobStatus,
            Action onCatalogReady = null,
            Action<string, string, Action<int, Dictionary<string, object>>, Action> registerPendingHttp = null)
        {
            var releaseCommandSlot = true;
            try
            {
                switch (item.Kind)
                {
                    case Kind.Catalog:
                        var catalog = InvokeCatalog.BuildResponse(host.HostName);
                        onCatalogReady?.Invoke();
                        item.WriteJson(200, catalog);
                        break;

                    case Kind.InvokeJobStatus:
                        var payload = getInvokeJobStatus?.Invoke(item.CommandId);
                        if (payload == null)
                        {
                            item.WriteJson(404, new Dictionary<string, object>
                            {
                                ["ok"] = false,
                                ["error"] = "command_not_found",
                            });
                        }
                        else
                        {
                            item.WriteJson(200, payload);
                        }

                        break;

                    default:
                        var request = CommandHttpHelper.ParseInvokeRequest(item.Body, host.HostName);
                        var post = host.HandleCommand(request);
                        if (post.HoldConnectionUntilComplete
                            && registerPendingHttp != null
                            && !string.IsNullOrEmpty(post.CommandId))
                        {
                            item.HoldCommandSlot = true;
                            releaseCommandSlot = false;
                            registerPendingHttp(
                                post.CommandId,
                                request.RequestId,
                                item.WriteJson,
                                () => { });
                        }
                        else if (post.HoldConnectionUntilComplete)
                        {
                            item.WriteJson(500, new Dictionary<string, object>
                            {
                                ["ok"] = false,
                                ["error"] = "hold_without_pending_http",
                                ["error_code"] = "INTERNAL_ERROR",
                                ["command_id"] = post.CommandId,
                            });
                        }
                        else
                        {
                            item.WriteJson(post.StatusCode, post.Body);
                        }

                        break;
                }
            }
            catch (Exception ex)
            {
                item.WriteJson(500, new Dictionary<string, object>
                {
                    ["ok"] = false,
                    ["error"] = ex.Message,
                });
            }

            return releaseCommandSlot && !item.HoldCommandSlot;
        }
    }
}
