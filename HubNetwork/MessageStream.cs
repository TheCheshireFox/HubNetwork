using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HubNetwork
{
    class MessageStream
    {
        private readonly MemoryStream _ms;
        private readonly byte[] _intBuffer = new byte[4];

        public MessageStream()
            : this(0)
        {

        }

        public MessageStream(int size)
        {
            _ms = new MemoryStream(size);
        }

        public MessageStream(byte[] buffer)
        {
            _ms = new MemoryStream(buffer);
        }

        public byte[] ToArray()
        {
            var allBytes = new byte[_ms.Length + 4];
            _ms.Seek(0, SeekOrigin.Begin);
            _ms.Read(allBytes, 4, allBytes.Length - 4);
            Array.Copy(BitConverter.GetBytes(allBytes.Length - 4), allBytes, 4);

            return allBytes;
        }

        public void Write(string str)
        {
            Write(Encoding.UTF8.GetBytes(str ?? ""));
        }

        public void Write(byte[] buffer)
        {
            var sz = BitConverter.GetBytes(buffer?.Length ?? 0);
            buffer = buffer ?? Array.Empty<byte>();

            _ms.Write(sz, 0, sz.Length);
            _ms.Write(buffer, 0, buffer.Length);
        }

        public void Write(int val)
        {
            var valBytes = BitConverter.GetBytes(val);
            _ms.Write(valBytes, 0, valBytes.Length);
        }

        public void Write(bool val)
        {
            _ms.WriteByte(val ? (byte)1 : (byte)0);
        }

        public int ReadInt()
        {
            _ms.Read(_intBuffer, 0, _intBuffer.Length);
            return BitConverter.ToInt32(_intBuffer, 0);
        }

        public string ReadString()
        {
            return Encoding.UTF8.GetString(ReadBytes());
        }

        public bool ReadBool()
        {
            _ms.Read(_intBuffer, 0, 1);
            return _intBuffer[0] == 1;
        }

        public byte[] ReadBytes()
        {
            var sz = ReadInt();
            var buf = new byte[sz];
            _ms.Read(buf, 0, sz);
            return buf;
        }
    }
}
