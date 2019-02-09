using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HubNetwork.Client
{
    public delegate void ConnectionEventHandler();

    public class HubNetworkClient : IDisposable
    {
        #region Messaging

        private ConcurrentDictionary<string, TaskCompletionSource<bool>> _ackTasks = new ConcurrentDictionary<string, TaskCompletionSource<bool>>();
        private ConcurrentDictionary<string, TaskCompletionSource<InternalMessage>> _directResponses = new ConcurrentDictionary<string, TaskCompletionSource<InternalMessage>>();

        #endregion

        #region Public properties

        public string Name { get; internal set; }

        #endregion

        #region Events

        public event ConnectionEventHandler OnConnected;
        public event ConnectionEventHandler OnDisconnected;
        public event EventHandler<Message> OnMessage;

        #endregion

        private Logger _logger;
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private InternalHubNetworkClient _client;
        private ClientSocketOptions _opts;

        #region Messaging control

        private void OnInternalMessage(InternalMessage msg)
        {
            if (msg.Type == InternalMessageType.Heartbeat)
            {
                return;
            }

            if (_ackTasks.TryRemove(msg.CorrelationId, out var tcs))
            {
                if (msg.Type == InternalMessageType.Error)
                {
                    tcs.TrySetException(new Exception(Encoding.UTF8.GetString(msg.Payload)));
                }
                else
                {
                    tcs.TrySetResult(true);
                }
            }
            else if (_directResponses.TryRemove(msg.CorrelationId, out var resp))
            {
                resp.TrySetResult(msg);
            }
            else
            {
                OnMessage?.Invoke(this, new Message
                {
                    CorrelationId = msg.CorrelationId,
                    Payload = msg.Payload,
                    Sender = msg.Sender,
                    Type = (MessageType)msg.Type
                });
            }
        }

        private Task RegisterAck(string id)
        {
            var tcs = new TaskCompletionSource<bool>();
            _ackTasks.TryAdd(id, tcs);
            return tcs.Task;
        }

        private Task<InternalMessage> RegisterResponse(string id)
        {
            var tcs = new TaskCompletionSource<InternalMessage>();
            _directResponses.TryAdd(id, tcs);
            return tcs.Task;
        }

        #endregion

        public async Task<Message> SendDirectWaitReponseAsync(string peer, byte[] payload)
        {
            var id = Guid.NewGuid().ToString();
            var ack = RegisterAck(id);
            var res = RegisterResponse(id);

            _logger.Trace("Send DWR message with id {0}", id);

            await _client.SendAsync(new InternalMessage
            {
                CorrelationId = id,
                Payload = payload,
                Sender = Name,
                Reciever = peer,
                Type = InternalMessageType.Direct
            });

            await ack.Timeout(_opts.Timeout, _cts.Token);

            var im = await res.Timeout(_opts.Timeout, _cts.Token);

            return new Message
            {
                Type = (MessageType)im.Type,
                CorrelationId = im.CorrelationId,
                Payload = im.Payload,
                Reciever = im.Reciever,
                Sender = im.Sender
            };
        }

        public async Task SendMessageAsync(Message msg, bool noack = false)
        {
            var id = string.IsNullOrEmpty(msg.CorrelationId) ? Guid.NewGuid().ToString() : msg.CorrelationId;
            var t = noack ? Task.CompletedTask : RegisterAck(id);

            _logger.Trace("Send message with id {0}", id);

            await _client.SendAsync(new InternalMessage
            {
                Type = (InternalMessageType)msg.Type,
                CorrelationId = id,
                Payload = msg.Payload,
                Reciever = msg.Reciever,
                Sender = msg.Sender,
                NoAck = noack
            });

            await t.Timeout(_opts.Timeout, _cts.Token);
        }

        public HubNetworkClient(string name, IPEndPoint ep, ClientSocketOptions opts, Logger logger = null)
            : this(name, "", ep, opts, logger)
        {

        }

        public HubNetworkClient(string name, string network, IPEndPoint ep, ClientSocketOptions opts, Logger logger = null)
        {
            Name = name;

            _logger = logger ?? LogManager.GetLogger("HubNetwork.Client");
            _client = new InternalHubNetworkClient(name, network, ep, opts, _logger);
            _opts = opts;

            _client.OnConnected += () => OnConnected?.Invoke();
            _client.OnDisconnected += () => OnDisconnected?.Invoke();
            _client.OnMessage += (s, m) => OnInternalMessage(m);
        }

        public Task ConnectAsync()
        {
            return _client.ConnectAsync();
        }

        public void Dispose()
        {
            _client.Dispose();
        }
    }
}
