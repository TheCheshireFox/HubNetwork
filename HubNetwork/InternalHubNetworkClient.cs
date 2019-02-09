using HubNetwork.Client;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HubNetwork
{
    internal class InternalHubNetworkClient
    {
        #region Public properties

        public string Name { get; internal set; }

        #endregion

        #region Events

        public event ConnectionEventHandler OnConnected;
        public event ConnectionEventHandler OnDisconnected;
        public event EventHandler<InternalMessage> OnMessage;

        #endregion

        #region Private members

        private Logger _logger;
        private CancellationTokenSource _cts = new CancellationTokenSource();

        #endregion

        #region Network

        private readonly ClientSocketOptions _opts;
        private readonly IPEndPoint _endpoint;
        private readonly string _network;
        private readonly TcpClient _client;
        private readonly byte[] _intBuffer = new byte[4] { 0, 0, 0, 0 };
        private readonly object _writeLock = new object();

        #endregion

        #region Heartbeat

        private readonly AutoResetEvent _newMessage = new AutoResetEvent(false);
        private readonly TimeSpan _heartbeatInterval;
        private Task _heartbeatTask = Task.CompletedTask;

        private async Task HeartbeatSender()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await WaitForConnection();

                    await SendAsync(new InternalMessage
                    {
                        Type = InternalMessageType.Heartbeat
                    });
                }
                catch (IOException) { }


                var dt = DateTime.UtcNow;

                if (!await _newMessage.WaitOneAsync((int)_heartbeatInterval.TotalMilliseconds, _cts.Token))
                {
                    try
                    {
                        await EmitDisconnectAndWait();
                    }
                    catch (IOException)
                    {
                        return;
                    }

                    continue;
                }

                var delta = (DateTime.UtcNow - dt);
                if (delta.TotalMilliseconds > 0)
                {
                    await Task.Delay(delta, _cts.Token);
                }
            }
        }

        #endregion

        #region Connection

        private object _connectionLock = new object();
        private bool _connected = false;
        private readonly TimeSpan _reconnectInterval = TimeSpan.FromSeconds(1);
        private Task _reconnectTask = Task.CompletedTask;

        private readonly ManualResetEvent _connectionLost = new ManualResetEvent(false);
        private readonly ManualResetEvent _connectionRestored = new ManualResetEvent(true);

        public void SetConnected()
        {
            lock(_connectionLock)
            {
                _connected = true;
            }
        }

        private async Task EmitDisconnectAndWait()
        {
            _logger.Warn($"[{Name}] Connection to {_endpoint} lost");

            lock (_connectionLock)
            {
                try
                {
                    if (!_opts.AutoReconnect)
                    {
                        throw new IOException("No connection");
                    }

                    if (_connected)
                    {
                        _connected = false;
                        _connectionRestored.Reset();
                        _connectionLost.Set();
                    }
                }
                finally
                {
                    OnDisconnected?.Invoke();
                }
            }

            await _connectionRestored.WaitOneAsync(-1, _cts.Token);
        }

        private void ThrowIfNotConnected()
        {
            lock (_connectionLock)
            {
                if (!_connected)
                {
                    throw new IOException("No connection.");
                }
            }
        }

        private async Task WaitForConnection()
        {
            lock (_connectionLock)
            {
                if (_connected)
                {
                    return;
                }

                if (!_opts.AutoReconnect)
                {
                    throw new IOException("Connection lost");
                }
            }

            await _connectionRestored.WaitOneAsync(-1, _cts.Token);
        }

        private async Task Reconnect()
        {
            while (!_cts.IsCancellationRequested)
            {
                await _connectionLost.WaitOneAsync(-1, _cts.Token);

                while (true)
                {
                    try
                    {
                        _logger.Warn($"Trying to reconnect to {_endpoint}");

                        Dispose();
                        _cts = new CancellationTokenSource();

                        await ConnectAsync();

                        SetConnected();

                        _connectionLost.Reset();
                        _connectionRestored.Set();

                        OnConnected?.Invoke();

                        break;
                    }
                    catch (Exception e)
                    {
                        _logger.Error(e, "Error occured in process of reconnection");
                        await Task.Delay(_reconnectInterval, _cts.Token);
                    }
                }
            }
        }

        #endregion

        #region Messaging

        private Task _rcvTask = Task.CompletedTask;

        private async Task Handshake()
        {
            var correlationId = Guid.NewGuid().ToString();
            await SendAsync(new InternalMessage
            {
                CorrelationId = correlationId,
                Payload = Encoding.UTF8.GetBytes(_network),
                Reciever = Name,
                Sender = Name,
                Type = InternalMessageType.Init
            }, false).Timeout(_opts.Timeout, _cts.Token);

            var resp = await ReadAsync(false).Timeout(_opts.Timeout, _cts.Token);

            if (resp.CorrelationId != correlationId)
            {
                throw new Exception("Error occured during handshake process");
            }
        }

        private async Task RecieveMessages()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    _logger.Trace("[{0}] Reading message from socket...", Name);

                    var msg = await ReadAsync();

                    _logger.Trace("[{0}] Recieve message with type {1} and id {2} from {3}", Name, msg.Type, msg.CorrelationId, msg.Sender);

                    _newMessage.Set();

                    OnMessage?.Invoke(this, msg);
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Error occured in process of reading message");
                    await EmitDisconnectAndWait();
                }

            }

            _logger.Trace("[{0}] Reading message process stopped", Name);
        }

        public Task SendAsync(InternalMessage msg, bool throwIfNotConnected = true)
        {
            if (throwIfNotConnected) ThrowIfNotConnected();

            _logger.Trace("[{0}] Send message with type {1} and id {2}", Name, msg.Type, msg.CorrelationId);

            lock (_writeLock)
            {
                _client.WriteAsync(msg.ToBytes(), _cts.Token)
                    .Timeout(_opts.Timeout, _cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            return Task.CompletedTask;
        }

        public async Task<InternalMessage> ReadAsync(bool waitForConnection = true)
        {
            if (waitForConnection) await WaitForConnection();

            await _client.ReadAsync(_intBuffer, _cts.Token);
            var size = BitConverter.ToInt32(_intBuffer, 0);

            var messageBytes = new byte[size];
            await _client.ReadAsync(messageBytes, _cts.Token).Timeout(_opts.Timeout);

            return new InternalMessage(messageBytes);
        }

        public async Task ConnectAsync()
        {
            if (_client?.Connected ?? false)
            {
                return;
            }

            _logger.Trace("Start connection process to {0}", _endpoint);

            await _client.ConnectAsync(_endpoint).Timeout(_opts.Timeout, _cts.Token);

            _logger.Trace("Connection to {0} established. Starting handshake process", _endpoint);

            await Handshake();

            SetConnected();

            _reconnectTask = _opts.AutoReconnect ? Task.Factory.StartNew(Reconnect, TaskCreationOptions.LongRunning) : Task.CompletedTask;
            _heartbeatTask = _opts.Heartbeat ? Task.Factory.StartNew(HeartbeatSender, TaskCreationOptions.LongRunning) : Task.CompletedTask;

            StartRecieve();

            _logger.Trace("Handshake with {0} completed. Socket fully initialized", _endpoint);

            OnConnected?.Invoke();
        }

        public void StartRecieve()
        {
            _rcvTask = Task.Factory.StartNew(RecieveMessages, TaskCreationOptions.LongRunning);
        }

        #endregion

        public InternalHubNetworkClient(string name, string network, IPEndPoint ep, ClientSocketOptions opts, Logger logger)
        {
            Name = name;

            _network = network;
            _endpoint = ep;
            _client = new TcpClient(new System.Net.Sockets.TcpClient());
            _opts = opts;
            _logger = logger ?? LogManager.GetLogger("HubNetwork.Client");
        }

        public InternalHubNetworkClient(string name, string network, TcpClient client, ClientSocketOptions opts, Logger logger)
        {
            Name = name;

            _network = network;
            _endpoint = client.RemoteEndPoint;
            _client = client;
            _opts = opts;
            _logger = logger ?? LogManager.GetLogger("HubNetwork.Client");
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
            _client?.Reset();

            Task.WaitAll(_heartbeatTask, _rcvTask, _reconnectTask);
        }
    }
}
