using System;
using System.Collections.Generic;

namespace Air.UnityConnector.Http
{
    public static class ConnectorServerFactory
    {
        public static ConnectorServerCore Create(
            IInvokeHost host,
            Func<int> resolvePort,
            string label,
            Func<string, Dictionary<string, object>> getCommandResponse,
            IJobQuery commandQuery = null,
            IHealthMetadataProvider health = null,
            Action onCatalogReady = null,
            Action onBeforeDrain = null,
            Action wakeMainThread = null,
            Func<bool> canAcceptCommand = null,
            Action onStarted = null,
            bool logLifecycle = true,
            Action<string, string, Action<int, Dictionary<string, object>>, Action> registerPendingHttp = null)
        {
            var scheduler = new ConnectorMainThreadScheduler(
                host,
                getCommandResponse,
                onCatalogReady,
                onBeforeDrain,
                wakeMainThread,
                canAcceptCommand,
                registerPendingHttp);

            var endpoint = new ConnectorHttpEndpoint(
                new ConnectorRequestDispatcher(
                    host,
                    scheduler,
                    commandQuery,
                    health),
                resolvePort,
                label,
                onStarted,
                logLifecycle);

            return new ConnectorServerCore(host, scheduler, endpoint);
        }
    }
}
