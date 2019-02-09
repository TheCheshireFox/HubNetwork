using HubNetwork.Client;
using HubNetwork.Errors;
using NLog;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace HubNetwork.Server
{
    public class Router : IDisposable
    {
        private Logger _logger = LogManager.GetLogger("HubNetwork.Server");
        private Logger _clientLogger = LogManager.GetLogger("HubNetwork.ServerClient");

        private TcpListener _server;
        private TimeSpan _timeout = TimeSpan.MaxValue;

        private ConcurrentDictionary<string, Network> _networks = new ConcurrentDictionary<string, Network>();

        private ActionBlock<NetworkMessage> _serveMessages;

        private CancellationTokenSource _cts = new CancellationTokenSource();

        private Task _listenTask = Task.CompletedTask;

        private async Task ServeMessage(NetworkMessage msg)
        {
            try
            {
                if (msg.Message.Type == InternalMessageType.Heartbeat)
                {
                    await msg.Socket.SendAsync(new InternalMessage
                    {
                        Type = InternalMessageType.Heartbeat
                    });
                }
                else if (_networks.TryGetValue(msg.Network, out var network))
                {
                    _logger.Trace("Redirect message with id {0} to network {1}", msg.Message.CorrelationId, msg.Network);
                    await network.ProcessMessage(msg.Socket, msg.Message);
                }
                else
                {
                    _logger.Trace("No network \"{1}\" for message with id {0}", msg.Message.CorrelationId, msg.Network);

                    await msg.Socket.SendAsync(new InternalMessage
                    {
                        CorrelationId = msg.Message.CorrelationId,
                        Type = InternalMessageType.Error,
                        Payload = Encoding.UTF8.GetBytes($"No network {network}")
                    });
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Error occured during process of message processing: ");

                try
                {
                    await msg.Socket.SendAsync(new InternalMessage
                    {
                        CorrelationId = msg.Message.CorrelationId,
                        Type = InternalMessageType.Error,
                        Payload = Encoding.UTF8.GetBytes($"Internal error")
                    });
                }
                catch (Exception inner)
                {
                    _logger.Error(inner, $"Unable to send error message: ");
                }
            }
        }

        private async Task Listen()
        {
            _server.Start();
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var client = await _server.AcceptTcpClientAsync();

                    _logger.Info($"New connection from {client.Client.RemoteEndPoint}");

                    var clientSocket = new InternalHubNetworkClient("", "", new Client.TcpClient(client), new ClientSocketOptions
                    {
                        AutoReconnect = false,
                        Heartbeat = false
                    }, _clientLogger);

                    clientSocket.SetConnected();

                    var m = await clientSocket.ReadAsync();

                    if (m.Type != InternalMessageType.Init)
                    {
                        throw new Exception($"Invalid init message from client {client.Client.RemoteEndPoint}");
                    }

                    clientSocket.Name = m.Reciever;

                    _logger.Info($"New connection {client.Client.RemoteEndPoint} identified as {clientSocket.Name}");

                    var networkName = Encoding.UTF8.GetString(m.Payload);
                    var network = _networks.GetOrAdd(networkName, n => new Network(networkName));

                    clientSocket.OnMessage += async (s, msg) =>
                    {
                        await _serveMessages.SendAsync(new NetworkMessage
                        {
                            Message = msg,
                            Network = networkName,
                            Socket = clientSocket
                        });
                    };
                    clientSocket.StartRecieve();

                    try
                    {
                        if (network.AddClient(clientSocket))
                        {
                            await clientSocket.SendAsync(new InternalMessage()
                            {
                                CorrelationId = m.CorrelationId
                            });
                        }
                        else
                        {
                            await clientSocket.SendAsync(new InternalMessage
                            {
                                Type = InternalMessageType.Error,
                                CorrelationId = m.CorrelationId,
                                Payload = Encoding.UTF8.GetBytes($"Client with name {clientSocket.Name} already connected")
                            });

                            continue;
                        }
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                }
                catch (Exception e)
                {
                    _logger.Error(e, $"Error occured in listener thread");
                }
            }
        }

        public Router(IPEndPoint ep)
        {
            CancellationTokenRegistration ctr;
            ctr = _cts.Token.Register(() =>
            {
                _server.Stop();
                ctr.Dispose();
            });

            _server = new TcpListener(ep);

            _serveMessages = new ActionBlock<NetworkMessage>(async m => await ServeMessage(m), new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = -1
            });
        }

        public void Start()
        {
            _listenTask = Task.Factory.StartNew(async () => await Listen(), TaskCreationOptions.LongRunning);
        }

        public void Stop()
        {
            _cts.Cancel();
            Task.WaitAll(_listenTask);

            foreach (var n in _networks.Values)
            {
                n.Dispose();
            }

            _networks.Clear();
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
