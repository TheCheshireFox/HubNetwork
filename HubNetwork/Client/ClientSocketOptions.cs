using NLog;
using System;
using System.Collections.Generic;
using System.Text;

namespace HubNetwork.Client
{
    public class ClientSocketOptions
    {
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
        public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(10);
        public bool AutoReconnect { get; set; } = true;
        public bool Heartbeat { get; set; } = true;
    }
}
