using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;

namespace HubNetwork.Errors
{
    public class ErrorPayload
    {
        public ErrorCode Code { get; set; }
        public string Description { get; set; }

        public static ErrorPayload Deserialize(byte[] payload)
        {
            var bf = new BinaryFormatter();
            using (var ms = new MemoryStream())
            {
                return (ErrorPayload)bf.Deserialize(ms);
            }
        }

        public byte[] Serialize()
        {
            var bf = new BinaryFormatter();
            using (var ms = new MemoryStream())
            {
                bf.Serialize(ms, this);
                return ms.ToArray();
            }
        }
    }
}
