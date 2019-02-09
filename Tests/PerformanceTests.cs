using HubNetwork;
using NLog;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Tests
{
    [TestFixture]
    class PerformanceTests
    {
        [Test]
        public async Task DirectAck([Values]bool ack)
        {
            const int count = 10000;
            const int iterations = 3;

            double sndCount = 0;
            double rcvCount = 0;

            for (int _ = 0; _ < iterations; _++)
            {
                var evt = new CountdownEvent(count);
                var br = new Barrier(2);

                using (var server = Helper.CreateRouter(out var ep))
                using (var c1 = Helper.CreateClient(ep, "c1"))
                using (var c2 = Helper.CreateClient(ep, "c2"))
                {
                    server.Start();

                    var c1t = Task.Factory.StartNew(async () =>
                    {
                        await c1.ConnectAsync();

                        var sendSw = new Stopwatch();

                        br.SignalAndWait();

                        sendSw.Start();
                        for (int i = 0; i < count; i++)
                        {
                            await c1.SendMessageAsync(new Message
                            {
                                Reciever = "c2"
                            }, !ack);
                        }
                        sendSw.Stop();

                        sndCount += (count * TimeSpan.TicksPerSecond * 1.0) / (sendSw.ElapsedTicks);
                    });

                    await c2.ConnectAsync();
                    c2.OnMessage += (s, e) => evt.Signal();

                    var rcvSw = new Stopwatch();
                    br.SignalAndWait();

                    rcvSw.Start();
                    evt.Wait();
                    rcvSw.Stop();

                    server.Stop();

                    rcvCount += (count * TimeSpan.TicksPerSecond * 1.0) / (rcvSw.ElapsedTicks);
                }
            }

            TestContext.Out.WriteLine($"Send { (int)(sndCount / iterations)} msg/sec");
            TestContext.Out.WriteLine($"Recieve { (int)(rcvCount / iterations)} msg/sec");
        }
    }
}
