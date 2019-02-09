using System;
using System.Collections.Generic;
using System.Text;

namespace HubNetwork.Client
{
    public class QueueClientSocketOptions
    {
        public int MaxReceaveQueueSize { get; set; } = -1;
        public int MaxSendQueueSize { get; set; } = -1;
        public QueueStrategy ReceaveQueueStrategy { get; set; } = QueueStrategy.Wait;
        public QueueStrategy SendQueueStrategy { get; set; } = QueueStrategy.Wait;
    }
}
