using HubNetwork.Client;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HubNetwork.Server
{
    internal class Network : IDisposable
    {
        private readonly string _networkName;
        private Logger _logger = LogManager.GetLogger("HubNetwork.Network");
        private Random _rnd = new Random();
        private int _roundRobinIndex = -1;
        private object _roundRobinLock = new object();
        private ConcurrentDictionary<string, InternalHubNetworkClient> _clients = new ConcurrentDictionary<string, InternalHubNetworkClient>();

        public Network(string name)
        {
            _networkName = name;
        }

        public bool AddClient(InternalHubNetworkClient client)
        {
            client.OnDisconnected += () => _clients.TryRemove(client.Name, out var _);
            return _clients.TryAdd(client.Name, client);
        }

        public async Task ProcessMessage(InternalHubNetworkClient sender, InternalMessage msg)
        {
            if (msg == null)
            {
                return;
            }

            msg.Sender = sender.Name;
            switch (msg.Type)
            {
                case InternalMessageType.Direct:
                    await SendDirect(sender, msg);
                    break;
                case InternalMessageType.Broadcast:
                    await SendBroadcast(sender, msg);
                    break;
                case InternalMessageType.Random:
                    await SendRandom(sender, msg);
                    break;
                case InternalMessageType.RoundRobin:
                    await SendRoundRobin(sender, msg);
                    break;
            }
        }

        private async Task Ack(string id, InternalHubNetworkClient client)
        {
            await SendAsync(client, new InternalMessage
            {
                CorrelationId = id,
                Type = InternalMessageType.Ack
            });
        }

        private async Task SendDirect(InternalHubNetworkClient sender, InternalMessage msg)
        {
            if (String.IsNullOrEmpty(msg.Reciever) || !_clients.TryGetValue(msg.Reciever, out var peer))
            {
                _logger.Trace("[{0}] No reciever {1} for message with id {2} from client {3}", _networkName, msg.Reciever, msg.CorrelationId, sender.Name);

                await SendAsync(sender, new InternalMessage
                {
                    Reciever = sender.Name,
                    Type = InternalMessageType.Error,
                    CorrelationId = msg.CorrelationId,
                    Payload = Encoding.UTF8.GetBytes($"No reciever '{msg.Reciever}'"),
                });
                return;
            }

            if (!msg.NoAck) await Ack(msg.CorrelationId, sender);
            await SendAsync(peer, msg);
        }

        private async Task SendBroadcast(InternalHubNetworkClient sender, InternalMessage msg)
        {
            if (!msg.NoAck) await Ack(msg.CorrelationId, sender);
            await Task.WhenAll(_clients.Values.Where(c => c.Name != sender.Name).Select(c => SendAsync(c, msg)));
        }

        private async Task SendRandom(InternalHubNetworkClient sender, InternalMessage msg)
        {
            if (!msg.NoAck) await Ack(msg.CorrelationId, sender);
            while (_clients.Count > 0)
            {
                var index = _rnd.Next(0, _clients.Count);
                try
                {
                    await SendAsync(_clients.Values.ElementAt(index), msg);
                    break;
                }
                catch (ArgumentOutOfRangeException)
                {
                    continue;
                }
            }
        }

        private async Task SendRoundRobin(InternalHubNetworkClient sender, InternalMessage msg)
        {
            if (!msg.NoAck) await Ack(msg.CorrelationId, sender);

            while (_clients.Count > 0)
            {
                try
                {
                    InternalHubNetworkClient socket = null;
                    lock (_roundRobinLock)
                    {
                        _roundRobinIndex++;
                        if (_roundRobinIndex >= _clients.Count)
                        {
                            _roundRobinIndex = 0;
                        }

                        socket = _clients.Values.ElementAt(_roundRobinIndex);
                    }
                    await SendAsync(socket, msg);
                    break;
                }
                catch (ArgumentOutOfRangeException)
                {
                    continue;
                }
            }
        }

        private async Task SendAsync(InternalHubNetworkClient client, InternalMessage msg)
        {
            try
            {
                msg.Reciever = client.Name;

                _logger.Trace("[{0}] Send message from client {1} to client {2} with id {3} with type {4}", _networkName, msg.Sender, msg.Reciever, msg.CorrelationId, msg.Type);

                await client.SendAsync(msg);
            }
            catch (Exception)
            {
                _clients.TryRemove(client.Name, out var _);
            }
        }

        public void Dispose()
        {
            foreach (var c in _clients.Values)
            {
                c.Dispose();
            }

            _clients.Clear();
        }
    }
}
