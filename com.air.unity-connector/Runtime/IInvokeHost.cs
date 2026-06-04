using Air.UnityConnector.Job;
using Air.UnityConnector.Http;

namespace Air.UnityConnector
{
    /// <summary>Host-specific command dispatch (CONN-10: POST completes on held connection).</summary>
    public interface IInvokeHost
    {
        string HostName { get; }
        InvokePipeline.PostResult HandleCommand(InvokeRequest request);
    }
}
