using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace Air.UnityConnector.Host
{
    public static class HostNetwork
    {
        public const string EnvBind = "UNITY_CMD_BIND";
        public const string EnvLan = "UNITY_CMD_LAN";
        public const string EnvAdvertiseHost = "UNITY_CMD_ADVERTISE_HOST";
        public const string EnvAuthToken = "UNITY_CMD_TOKEN";

        public static HostBindMode ResolveBindMode()
        {
            var bind = Environment.GetEnvironmentVariable(EnvBind)?.Trim().ToLowerInvariant();
            if (bind is "lan" or "any")
                return bind == "any" ? HostBindMode.Any : HostBindMode.Lan;
            if (bind == "loopback" || bind == "localhost" || bind == "127.0.0.1")
                return HostBindMode.Loopback;
            if (Environment.GetEnvironmentVariable(EnvLan) == "1")
                return HostBindMode.Lan;
            return HostBindMode.Loopback;
        }

        public const int EditorPortScanAttempts = 10;

        public static int ResolveEditorPort() =>
            ResolvePortFromEnv(HostPorts.EnvEditorPort, HostPorts.DefaultEditor);

        /// <summary>Base port from env/default, then consecutive ports (avoids bind failures after reload).</summary>
        public static int[] ResolveEditorPortCandidates()
        {
            var start = ResolveEditorPort();
            var count = Math.Min(EditorPortScanAttempts, Math.Max(1, 65535 - start));
            var ports = new int[count];
            for (var i = 0; i < count; i++)
                ports[i] = start + i;
            return ports;
        }

        public static int ResolveEditorPlayPort() =>
            ResolvePortFromEnv(HostPorts.EnvEditorPlayPort, HostPorts.DefaultEditorPlay);

        public static int ResolvePlayerPort() =>
            ResolvePortFromEnv(HostPorts.EnvPlayerPort, HostPorts.DefaultPlayer);

        static int ResolvePortFromEnv(string envVarName, int defaultPort)
        {
            var env = Environment.GetEnvironmentVariable(envVarName);
            if (int.TryParse(env, out var p) && p > 0)
                return p;
            return defaultPort;
        }

        public static HostListenOptions BuildListenOptions(int port, HostBindMode? bindMode = null)
        {
            var mode = bindMode ?? ResolveBindMode();
            var prefixes = BuildBindPrefixes(port, mode);
            var advertise = ResolveAdvertiseHost(mode);
            var endpoints = BuildEndpoints(port, mode, advertise);

            return new HostListenOptions
            {
                Port = port,
                BindMode = mode,
                AdvertiseHost = advertise,
                BindPrefixes = prefixes,
                Endpoints = endpoints,
            };
        }

        public static IReadOnlyList<string> BuildBindPrefixes(int port, HostBindMode mode)
        {
            var list = new List<string>();
            switch (mode)
            {
                case HostBindMode.Any:
                    list.Add($"http://+:{port}/");
                    break;
                case HostBindMode.Lan:
                    list.Add($"http://127.0.0.1:{port}/");
                    foreach (var ip in GetPrivateIPv4Addresses())
                        list.Add($"http://{ip}:{port}/");
                    break;
                default:
                    list.Add($"http://127.0.0.1:{port}/");
                    break;
            }

            return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static string ResolveAdvertiseHost(HostBindMode mode)
        {
            var explicitHost = Environment.GetEnvironmentVariable(EnvAdvertiseHost)?.Trim();
            if (!string.IsNullOrEmpty(explicitHost))
                return explicitHost;

            if (mode == HostBindMode.Lan)
            {
                var lan = GetPrivateIPv4Addresses().FirstOrDefault();
                if (!string.IsNullOrEmpty(lan))
                    return lan;
            }

            return "127.0.0.1";
        }

        public static IReadOnlyList<Dictionary<string, object>> BuildEndpoints(
            int port,
            HostBindMode mode,
            string primaryAdvertiseHost)
        {
            var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { primaryAdvertiseHost };
            if (mode == HostBindMode.Lan)
            {
                hosts.Add("127.0.0.1");
                foreach (var ip in GetPrivateIPv4Addresses())
                    hosts.Add(ip);
            }

            return hosts.Select(h => new Dictionary<string, object>
            {
                ["host"] = h,
                ["port"] = port,
            }).ToList();
        }

        public static bool IsAuthRequired() =>
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(EnvAuthToken));

        public static bool ValidateAuthToken(string headerToken)
        {
            var expected = Environment.GetEnvironmentVariable(EnvAuthToken);
            if (string.IsNullOrEmpty(expected))
                return true;
            return string.Equals(expected, headerToken ?? "", StringComparison.Ordinal);
        }

        public static IReadOnlyList<string> GetPrivateIPv4Addresses()
        {
            var result = new List<string>();
            try
            {
                foreach (var addr in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                {
                    if (addr.AddressFamily != AddressFamily.InterNetwork)
                        continue;
                    if (IsPrivateIPv4(addr))
                        result.Add(addr.ToString());
                }
            }
            catch
            {
                // ignored
            }

            return result.Distinct().ToList();
        }

        static bool IsPrivateIPv4(IPAddress addr)
        {
            var b = addr.GetAddressBytes();
            if (b[0] == 10) return true;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
            if (b[0] == 192 && b[1] == 168) return true;
            return false;
        }
    }
}
