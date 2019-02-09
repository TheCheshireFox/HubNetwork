using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HubNetwork
{
    internal class InternalMessage
    {
        public InternalMessageType Type { get; set; }
        public string CorrelationId { get; set; }
        public string Reciever { get; set; }
        public byte[] Payload { get; set; }
        public string Sender { get; set; }
        public bool NoAck { get; set; } = false;

        public InternalMessage() { }

        public InternalMessage(byte[] bytes)
        {
            var ms = new MessageStream(bytes);

            Type = (InternalMessageType)ms.ReadInt();
            CorrelationId = ms.ReadString();
            Reciever = ms.ReadString();
            Sender = ms.ReadString();
            NoAck = ms.ReadBool();
            Payload = ms.ReadBytes();
        }

        public byte[] ToBytes()
        {
            var ms = new MessageStream(4
                + 4 + (CorrelationId?.Length ?? 0)
                + 4 + (Reciever?.Length ?? 0)
                + 4 + (Sender?.Length ?? 0)
                + 4 + (Payload?.Length ?? 0)
                + 4);

            ms.Write((int)Type);
            ms.Write(CorrelationId);
            ms.Write(Reciever);
            ms.Write(Sender);
            ms.Write(NoAck);
            ms.Write(Payload);

            return ms.ToArray();
        }
    }
}
