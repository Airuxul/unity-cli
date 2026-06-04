using System;
using Air.UnityConnector.Host;
using Air.UnityConnector.Http;

namespace Air.UnityConnector.Http
{
    /// <summary>Shared HTTP listener + main-thread scheduler wiring for Editor and Play hosts.</summary>
    public sealed class ConnectorServerCore
    {
        public ConnectorServerCore(
            IInvokeHost host,
            ConnectorMainThreadScheduler scheduler,
            ConnectorHttpEndpoint endpoint)
        {
            Host = host ?? throw new ArgumentNullException(nameof(host));
            Scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
            Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        }

        public IInvokeHost Host { get; }

        public ConnectorMainThreadScheduler Scheduler { get; }

        public ConnectorHttpEndpoint Endpoint { get; }

        public string HostName => Host.HostName;

        public bool IsListening => Endpoint.IsRunning;

        public HostListenOptions ListenConfig => Endpoint.ListenConfig;

        public int Port => Endpoint.Port;

        public bool TryStart(Action<string> log, Action<string> logError, bool requirePortFree = true) =>
            Endpoint.TryStart(log, logError, requirePortFree);

        public bool TryStartOnPort(int port, Action<string> log, Action<string> logError, bool requirePortFree = true) =>
            Endpoint.TryStartOnPort(port, log, logError, requirePortFree);

        public void Stop(Action<string> log) => Endpoint.Stop(log);

        public bool TryProbeHealth(
            string expectedHost,
            int? expectedBuild,
            string expectedSessionId,
            int timeoutMs,
            int attempts,
            out string error) =>
            Endpoint.TryProbeHealth(
                expectedHost,
                expectedBuild,
                expectedSessionId,
                timeoutMs,
                attempts,
                out error);
    }
}
