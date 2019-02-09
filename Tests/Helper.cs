using HubNetwork.Client;
using HubNetwork.Server;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Tests
{
    static class TaskExtentions
    {
        public static async Task Timeout(this Task task, TimeSpan timeout, CancellationToken? cts = null)
        {
            CancellationTokenRegistration? ctr = null;
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

            if (cts.HasValue)
            {
                ctr = cts.Value.Register(() =>
                {
                    tcs.TrySetCanceled();
                    ctr?.Dispose();
                });
            }

            if (timeout == TimeSpan.MaxValue)
            {
                await task;
                return;
            }

            var delay = Task.Delay(timeout).ContinueWith(d => throw new TimeoutException());
            await await Task.WhenAny(task, delay);
        }

        public static async Task<T> Timeout<T>(this Task<T> task, TimeSpan timeout, CancellationToken? cts = null)
        {
            CancellationTokenRegistration? ctr = null;
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

            if (cts.HasValue)
            {
                ctr = cts.Value.Register(() =>
                {
                    tcs.TrySetCanceled();
                    ctr?.Dispose();
                });
            }

            if (timeout == TimeSpan.MaxValue)
            {
                return await task;
            }

            var delay = Task.Delay(timeout).ContinueWith<T>(d => throw new TimeoutException());
            return await await Task.WhenAny(task, delay);
        }
    }

    static class Helper
    {
        public static int GetAvailablePort()
        {
            var props = IPGlobalProperties.GetIPGlobalProperties();
            var usedPorts = props.GetActiveTcpConnections().Select(c => c.LocalEndPoint.Port).Concat(props.GetActiveTcpListeners().Select(l => l.Port)).OrderBy(p => p);
            return Enumerable.Range(1000, 65355).First(v => !usedPorts.Contains(v));
        }

        public static HubNetworkClient CreateClient(EndPoint ep, string name, ClientSocketOptions opts = null)
        {
            return new HubNetworkClient(name, ep as IPEndPoint, opts ?? new ClientSocketOptions
            {
                AutoReconnect = false,
                Heartbeat = false,
                HeartbeatInterval = TimeSpan.MaxValue
            });
        }

        public static Router CreateRouter(out IPEndPoint ep)
        {
            ep = new IPEndPoint(IPAddress.Loopback, GetAvailablePort());
            return new Router(ep);
        }
    }
}
