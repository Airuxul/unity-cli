// @tag cli-param
using Air.UnityConnector.Invoke;
using Air.UnityConnector.Cli;
using Air.UnityConnector.Params;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Air.UnityConnector
{
    static class CommandBindingBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RegisterRuntime() => Register();

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        static void RegisterEditor() => Register();
#endif

        static void Register()
        {
            ConnectorSerialization.EnsureRegistered();
            InvokeRegistry.Instance = DiscoveryInvokeRegistry.Instance;
            InvokeParameterBinding.Bind = CliParamBinder.Bind;
            InvokeParameterBinding.Describe = CliParamBinder.Describe;
            CliCommandDiscovery.LogWarning = ConnectorLog.LogWarning;
        }
    }
}
