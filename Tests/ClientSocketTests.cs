using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using HubNetwork;
using HubNetwork.Client;
using HubNetwork.Server;
using Moq;
using NUnit.Framework;

namespace Tests
{
    [TestFixture]
    public class ClientSocketTests
    {
        [Test]
        public async Task PeerTest()
        {
            using (var r = Helper.CreateRouter(out var ep))
            using (var s1 = Helper.CreateClient(ep, "s1"))
            using (var s2 = Helper.CreateClient(ep, "s2"))
            {
                r.Start();

                await s1.ConnectAsync();
                await s2.ConnectAsync();

                s2.OnMessage += async (s, m) =>
                {
                    Assert.AreEqual(Encoding.UTF8.GetString(m.Payload), "some text");
                    if (m.Type == MessageType.Direct)
                    {
                        await s2.SendMessageAsync(new Message
                        {
                            Type = MessageType.Direct,
                            Reciever = "s1",
                            CorrelationId = m.CorrelationId,
                            Payload = Encoding.UTF8.GetBytes("ok")
                        });
                    }
                };

                var msg = await s1.SendDirectWaitReponseAsync("s2", Encoding.UTF8.GetBytes("some text")).Timeout(TimeSpan.FromSeconds(60));
                Assert.AreEqual(Encoding.UTF8.GetString(msg.Payload), "ok");
            }
        }

        [Test]
        public async Task BroadcastTest()
        {
            var sp = new CountdownEvent(3);

            using (var r = Helper.CreateRouter(out var ep))
            using (var s1 = Helper.CreateClient(ep, "s1"))
            using (var s2 = Helper.CreateClient(ep, "s2"))
            using (var s3 = Helper.CreateClient(ep, "s3"))
            using (var s4 = Helper.CreateClient(ep, "s4"))
            {
                r.Start();
                await s1.ConnectAsync();
                await s2.ConnectAsync();
                await s3.ConnectAsync();
                await s4.ConnectAsync();

                s2.OnMessage += (s, m) => { sp.Signal(); TestContext.Out.WriteLine("s2 signal"); };
                s3.OnMessage += (s, m) => { sp.Signal(); TestContext.Out.WriteLine("s3 signal"); };
                s4.OnMessage += (s, m) => { sp.Signal(); TestContext.Out.WriteLine("s4 signal"); };

                await s1.SendMessageAsync(new Message
                {
                    Type = MessageType.Broadcast,
                    Payload = Encoding.UTF8.GetBytes("broadcast message")
                }).ConfigureAwait(false);

                Assert.IsTrue(sp.Wait(1000));
            }
        }

        [Test]
        public async Task RandomTest()
        {
            var sp = new AutoResetEvent(false);

            using (var r = Helper.CreateRouter(out var ep))
            using (var s1 = Helper.CreateClient(ep, "s1"))
            using (var s2 = Helper.CreateClient(ep, "s2"))
            using (var s3 = Helper.CreateClient(ep, "s3"))
            using (var s4 = Helper.CreateClient(ep, "s4"))
            {
                r.Start();
                await s1.ConnectAsync();
                await s2.ConnectAsync();
                await s3.ConnectAsync();
                await s4.ConnectAsync();

                s2.OnMessage += (s, m) => sp.Set();
                s3.OnMessage += (s, m) => sp.Set();
                s4.OnMessage += (s, m) => sp.Set();

                await s1.SendMessageAsync(new Message() { Type = MessageType.Random }).ConfigureAwait(false);

                Assert.IsTrue(sp.WaitOne(5000));
            } 
        }

        [Test]
        public async Task ReconnectTest()
        {
            var connected = new AutoResetEvent(false);

            var ep = new IPEndPoint(IPAddress.Loopback, Helper.GetAvailablePort());

            using (var s1 = Helper.CreateClient(ep, "s1", new ClientSocketOptions { AutoReconnect = true, Heartbeat = true, HeartbeatInterval = TimeSpan.FromSeconds(1) }))
            {
                var r = new Router(ep);

                r.Start();
                await s1.ConnectAsync();

                r.Dispose();

                s1.OnConnected += () => connected.Set();

                r = new Router(ep);
                r.Start();

                Assert.IsTrue(connected.WaitOne(5000));

                r.Dispose();
            }
        }
    }
}
