using System;
using System.Collections.Generic;
using System.Text;

namespace HubNetwork.Client
{
    public enum QueueStrategy
    {
        Wait,
        Discard,
        RemoveOld
    }
}
