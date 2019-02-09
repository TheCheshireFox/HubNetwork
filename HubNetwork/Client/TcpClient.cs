using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HubNetwork.Client
{
    public class TcpClient
    {
        private System.Net.Sockets.TcpClient _client;
        private Stream _stream;

        public bool Connected => _client.Connected;

        public IPEndPoint RemoteEndPoint => _client.Client.RemoteEndPoint as IPEndPoint; 

        public TcpClient(System.Net.Sockets.TcpClient client)
        {
            _client = client;
            
            if (client.Connected)
            {
                _stream = _client.GetStream();
            }
        }

        public async Task ConnectAsync(IPEndPoint ep)
        {
            await _client.ConnectAsync(ep.Address, ep.Port);
            _stream = _client.GetStream();
        }

        public void Dispose()
        {
            _client.Dispose();
        }

        public void Reset()
        {
            _client?.Close();
            _client?.Dispose();
            _stream?.Dispose();
            _client = new System.Net.Sockets.TcpClient();
        }

        public async Task ReadAsync(byte[] buffer, CancellationToken? ct)
        {
            var offset = 0;
            var currentCts = ct ?? CancellationToken.None;

            while (offset < buffer.Length)
            {
                var bytesReaded = await _stream.ReadAsync(buffer, offset, buffer.Length - offset, currentCts);
                if (bytesReaded == 0)
                {
                    throw new IOException("Connection closed");
                }

                offset += bytesReaded;
            }
        }

        public Task WriteAsync(byte[] buffer, CancellationToken? ct)
        {
            return _stream.WriteAsync(buffer, 0, buffer.Length, ct ?? CancellationToken.None);
        }
    }
}
