using NLog;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace HubNetwork.Client
{
    public class QueueHubNetworkClient : IPropagatorBlock<Message, Message>
    {
        private readonly TaskCompletionSource<bool> _completed = new TaskCompletionSource<bool>();
        private readonly HubNetworkClient _socket;
        private readonly BufferBlock<Message> _receaveBuffer;
        private readonly BufferBlock<Message> _sendBuffer;

        private readonly QueueClientSocketOptions _opts;
        private readonly Task _processOutTask;

        public Task Completion => _completed.Task;

        private async Task ProcessOut()
        {
            while (true)
            {
                await _socket.SendMessageAsync(await _sendBuffer.ReceiveAsync());
            }
        }

        public QueueHubNetworkClient(HubNetworkClient socket, QueueClientSocketOptions opts)
        {
            _opts = opts;

            _receaveBuffer = new BufferBlock<Message>(new DataflowBlockOptions
            {
                BoundedCapacity = opts.MaxReceaveQueueSize,
            });

            _sendBuffer = new BufferBlock<Message>(new DataflowBlockOptions
            {
                BoundedCapacity = opts.MaxSendQueueSize
            });

            _socket = socket;
            _socket.OnMessage += async (s, m) =>
            {
                if (_opts.MaxReceaveQueueSize > -1 && _receaveBuffer.Count == _opts.MaxReceaveQueueSize)
                {
                    switch (_opts.ReceaveQueueStrategy)
                    {
                        case QueueStrategy.Discard:
                            return;
                        case QueueStrategy.RemoveOld:
                            _receaveBuffer.Receive();
                            break;
                    }
                }
                await _receaveBuffer.SendAsync(m);
            };

            _processOutTask = Task.Factory.StartNew(ProcessOut, TaskCreationOptions.LongRunning);
        }

        public Task ConnectAsync()
        {
            return _socket.ConnectAsync();
        }

        public void Complete()
        {
            _socket.Dispose();
            _receaveBuffer.Complete();
            _sendBuffer.Complete();
            _completed.TrySetResult(true);
        }

        public Message ConsumeMessage(DataflowMessageHeader messageHeader, ITargetBlock<Message> target, out bool messageConsumed)
        {
            return (_receaveBuffer as IReceivableSourceBlock<Message>).ConsumeMessage(messageHeader, target, out messageConsumed);
        }

        public void Fault(Exception exception)
        {
            _socket.Dispose();
            _completed.TrySetException(exception);
        }

        public IDisposable LinkTo(ITargetBlock<Message> target, DataflowLinkOptions linkOptions)
        {
            return _receaveBuffer.LinkTo(target, linkOptions);
        }

        public DataflowMessageStatus OfferMessage(DataflowMessageHeader messageHeader, Message messageValue, ISourceBlock<Message> source, bool consumeToAccept)
        {
            if (_opts.MaxSendQueueSize > -1 && _sendBuffer.Count == _opts.MaxSendQueueSize)
            {
                switch (_opts.SendQueueStrategy)
                {
                    case QueueStrategy.Discard:
                        return DataflowMessageStatus.Declined;
                    case QueueStrategy.RemoveOld:
                        _sendBuffer.Receive();
                        break;
                }
            }

            return (_sendBuffer as IPropagatorBlock<Message, Message>).OfferMessage(messageHeader, messageValue, source, consumeToAccept);
        }

        public void ReleaseReservation(DataflowMessageHeader messageHeader, ITargetBlock<Message> target)
        {
            (_receaveBuffer as IPropagatorBlock<Message, Message>).ReleaseReservation(messageHeader, target);
        }

        public bool ReserveMessage(DataflowMessageHeader messageHeader, ITargetBlock<Message> target)
        {
            return (_receaveBuffer as IPropagatorBlock<Message, Message>).ReserveMessage(messageHeader, target);
        }
    }
}
