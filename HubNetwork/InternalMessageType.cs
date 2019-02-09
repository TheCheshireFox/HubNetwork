using System;
using System.Collections.Generic;
using System.Text;

namespace HubNetwork
{
    internal enum InternalMessageType
    {
        Direct,
        Broadcast,
        Random,
        RoundRobin,
        Init,
        Ack,
        Heartbeat,
        Error
    }
}
