using HubNetwork.Client;
using System;
using System.Collections.Generic;
using System.Text;

namespace HubNetwork.Server
{
    internal class NetworkMessage
    {
        public string Network { get; set; }
        public InternalHubNetworkClient Socket { get; set; }
        public InternalMessage Message { get; set; }
    }
}
