using System;
using System.Collections.Generic;
using System.Text;

namespace HubNetwork
{
    public class Message
    {
        public MessageType Type { get; set; }
        public string CorrelationId { get; set; }
        public string Sender { get; internal set; }
        public string Reciever { get; set; }
        public byte[] Payload { get; set; } = Array.Empty<byte>();
    }
}
