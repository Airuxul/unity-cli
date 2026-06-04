using Air.UnityConnector.Job;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Air.UnityConnector.Invoke;
using Air.UnityConnector.Host;
using UnityEngine;
using Air.UnityConnector.Cli;

namespace Air.UnityConnector.Tests
{
    public class RuntimeCommandFlowTests
    {
        private const string CommandName = "test.runtime.command.flow";

        [Test]
        public void RuntimeCommand_AcceptsAndCompletes_OnEditorPlayHost()
        {
            CliCommandDiscovery.Invalidate();
            var host = new PlayModeInvokeHost(HostKind.EditorPlay);
            var request = new InvokeRequest
            {
                Command = CommandName,
                Parameters = new Dictionary<string, object>(),
                RequestId = "req-runtime-command-flow",
                Endpoint = HostKind.EditorPlay,
            };

            var post = host.HandleCommand(request);
            Assert.IsTrue(post.HoldConnectionUntilComplete);
            Assert.IsFalse(string.IsNullOrEmpty(post.CommandId));
            var command = WaitForCommand(HostKind.EditorPlay, post.CommandId, 7000);
            Assert.IsNotNull(command);
            Assert.AreEqual(InvokeJobStatus.Succeeded, command.Status);

            var payload = JobResponseBuilder.ToResponse(command);
            Assert.IsNotNull(payload);
            Assert.AreEqual("succeeded", payload["status"]);
            Assert.IsTrue(payload.ContainsKey("result"));
        }

        private sealed class RuntimeFlowParams { }

        private sealed class RuntimeFlowCommand : CliCommand<RuntimeFlowParams>
        {
            public override InvokeDescriptor Descriptor { get; } = new DeferredInvokeDescriptor<RuntimeFlowParams>(
                CommandName, CommandHostScope.Runtime, "runtime command flow test command");

            public override void Run(RuntimeFlowParams cliParams)
            {
                MarkRunning();
                _ = System.Threading.Tasks.Task.Run(() =>
                {
                    ConnectorLog.Log("[unity-connector][test] runtime command started");
                    Thread.Sleep(5000);
                    ConnectorLog.Log("[unity-connector][test] runtime command finished");
                    CompleteSuccess(InvokeResult.Ok("runtime command finished"));
                });
            }
        }

        private static InvokeJobRecord WaitForCommand(string host, string commandId, int timeoutMs)
        {
            var waited = 0;
            while (waited <= timeoutMs)
            {
                var command = RuntimeJobStateManager.Get(host, commandId);
                if (command != null && command.Status is InvokeJobStatus.Succeeded or InvokeJobStatus.Failed or InvokeJobStatus.Orphaned)
                    return command;
                Thread.Sleep(100);
                waited += 100;
            }

            return RuntimeJobStateManager.Get(host, commandId);
        }
    }
}
