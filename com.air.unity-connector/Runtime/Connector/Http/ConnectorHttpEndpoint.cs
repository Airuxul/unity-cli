using System;
using System.Threading;
using Air.UnityConnector.Host;
using Air.UnityConnector.Http;

namespace Air.UnityConnector.Http
{
    /// <summary>
    /// Owns one HTTP listener: start/stop, optional lifecycle logs, and optional /health self-check.
    /// Editor supervisor adds cache, session, and watchdog on top; play-mode hosts use this directly.
    /// </summary>
    public sealed class ConnectorHttpEndpoint
    {
        private readonly ConnectorRequestDispatcher _dispatcher;
        private readonly Func<int> _resolvePort;
        private readonly string _label;
        private readonly Action _onStarted;
        private readonly bool _logLifecycle;

        private HttpListenerHost _server;
        private HostListenOptions _listen;

        public ConnectorHttpEndpoint(
            ConnectorRequestDispatcher dispatcher,
            Func<int> resolvePort,
            string label,
            Action onStarted = null,
            bool logLifecycle = true)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _resolvePort = resolvePort ?? throw new ArgumentNullException(nameof(resolvePort));
            _label = label ?? throw new ArgumentNullException(nameof(label));
            _onStarted = onStarted;
            _logLifecycle = logLifecycle;
        }

        public HostListenOptions ListenConfig => _listen;

        public bool IsRunning => _server != null && _server.IsRunning;

        public int Port => _listen?.Port ?? _resolvePort();

        public bool TryStart(Action<string> log, Action<string> logError, bool requirePortFree = true) =>
            TryStartOnPort(_resolvePort(), log, logError, requirePortFree);

        public bool TryStartOnPort(int port, Action<string> log, Action<string> logError, bool requirePortFree = true)
        {
            var wasRunning = _server != null;
            var ok = ConnectorHttpLifecycle.TryStart(
                ref _server,
                ref _listen,
                _dispatcher,
                port,
                _label,
                log,
                logError,
                _onStarted,
                requirePortFree);

            if (ok && _logLifecycle && !wasRunning)
            {
                log?.Invoke(
                    $"[unity-connector] {_label} started (port {Port}).");
            }

            return ok;
        }

        public void Stop(Action<string> log)
        {
            var wasRunning = _server != null;
            var port = Port;
            ConnectorHttpLifecycle.Stop(ref _server);
            _listen = null;

            if (wasRunning && _logLifecycle)
            {
                log?.Invoke($"[unity-connector] {_label} stopped (port {port}).");
            }
        }

        public bool TryProbeHealth(
            string expectedHost,
            int? expectedBuild,
            string expectedSessionId,
            int timeoutMs,
            int attempts,
            out string error)
        {
            error = null;
            if (_listen == null)
            {
                error = "listen config missing";
                return false;
            }

            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                if (TryProbeHealthOnce(expectedHost, expectedBuild, expectedSessionId, timeoutMs, out error))
                    return true;

                if (attempt < attempts)
                    Thread.Sleep(80 * attempt);
            }

            return false;
        }

        private bool TryProbeHealthOnce(
            string expectedHost,
            int? expectedBuild,
            string expectedSessionId,
            int timeoutMs,
            out string error)
        {
            error = null;
            if (!HttpProbe.TryGetHealth("127.0.0.1", Port, timeoutMs, out var body))
            {
                if (!string.IsNullOrWhiteSpace(body))
                {
                    error = "GET /health validation failed: " + HttpProbe.DescribeValidationFailure(
                        body,
                        expectedHost,
                        expectedBuild,
                        expectedSessionId);
                }
                else
                {
                    error = "GET /health timed out or connection refused";
                }
                return false;
            }

            if (HttpProbe.TryValidateHealth(body, expectedHost, expectedBuild, expectedSessionId))
                return true;

            error = HttpProbe.DescribeValidationFailure(
                body,
                expectedHost,
                expectedBuild,
                expectedSessionId);
            return false;
        }
    }
}
