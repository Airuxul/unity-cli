using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Air.UnityConnector;

namespace Air.UnityConnector.Http
{
    /// <summary>
    /// Drains HTTP work on the host main thread. At most one <c>POST /command</c> runs at a time;
    /// concurrent command posts receive <c>503</c> with <c>error=busy</c>.
    /// </summary>
    public sealed class ConnectorMainThreadScheduler : IMainThreadHttpScheduler
    {
        private readonly IInvokeHost _host;
        private readonly Func<string, Dictionary<string, object>> _getInvokeJobStatus;
        private readonly Action _onCatalogReady;
        private readonly Action _onBeforeDrain;
        private readonly Action _wakeMainThread;
        private readonly Func<bool> _canAcceptCommand;
        private readonly Action<string, string, Action<int, Dictionary<string, object>>, Action> _registerPendingHttp;
        private readonly ConcurrentQueue<MainThreadHttpWork.Item> _queue = new();
        private int _commandBusy;

        public ConnectorMainThreadScheduler(
            IInvokeHost host,
            Func<string, Dictionary<string, object>> getInvokeJobStatus,
            Action onCatalogReady = null,
            Action onBeforeDrain = null,
            Action wakeMainThread = null,
            Func<bool> canAcceptCommand = null,
            Action<string, string, Action<int, Dictionary<string, object>>, Action> registerPendingHttp = null)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _getInvokeJobStatus = getInvokeJobStatus;
            _onCatalogReady = onCatalogReady;
            _onBeforeDrain = onBeforeDrain;
            _wakeMainThread = wakeMainThread;
            _canAcceptCommand = canAcceptCommand;
            _registerPendingHttp = registerPendingHttp;
        }

        public void Schedule(string body, Action<int, Dictionary<string, object>> writeJson)
        {
            if (_canAcceptCommand != null && !_canAcceptCommand())
            {
                writeJson(503, new Dictionary<string, object>
                {
                    ["ok"] = false,
                    ["error"] = "reloading",
                    ["error_code"] = "DOMAIN_RELOADING",
                });
                return;
            }

            if (Interlocked.CompareExchange(ref _commandBusy, 1, 0) != 0)
            {
                writeJson(503, new Dictionary<string, object>
                {
                    ["ok"] = false,
                    ["error"] = "busy",
                    ["error_code"] = "SERVER_BUSY",
                });
                return;
            }

            _wakeMainThread?.Invoke();
            _queue.Enqueue(new MainThreadHttpWork.Item
            {
                Kind = MainThreadHttpWork.Kind.Command,
                Body = body,
                WriteJson = writeJson,
            });
        }

        public void ScheduleCatalog(Action<int, Dictionary<string, object>> writeJson)
        {
            _wakeMainThread?.Invoke();
            _queue.Enqueue(new MainThreadHttpWork.Item
            {
                Kind = MainThreadHttpWork.Kind.Catalog,
                WriteJson = writeJson,
            });
        }

        public void ScheduleInvokeJobStatus(string commandId, Action<int, Dictionary<string, object>> writeJson)
        {
            _wakeMainThread?.Invoke();
            _queue.Enqueue(new MainThreadHttpWork.Item
            {
                Kind = MainThreadHttpWork.Kind.InvokeJobStatus,
                CommandId = commandId,
                WriteJson = writeJson,
            });
        }

        public void ReleaseCommandSlot() => Interlocked.Exchange(ref _commandBusy, 0);

        public void Drain()
        {
            _onBeforeDrain?.Invoke();

            while (_queue.TryDequeue(out var item))
            {
                var isCommand = item.Kind == MainThreadHttpWork.Kind.Command;
                try
                {
                    var release = MainThreadHttpWork.Process(
                        item,
                        _host,
                        _getInvokeJobStatus,
                        _onCatalogReady,
                        _registerPendingHttp);

                    if (isCommand && release)
                        Interlocked.Exchange(ref _commandBusy, 0);
                }
                catch
                {
                    if (isCommand)
                        Interlocked.Exchange(ref _commandBusy, 0);
                    throw;
                }
            }
        }
    }
}
