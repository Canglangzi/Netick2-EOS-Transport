using Netick.Unity;
using Netick;
using UnityEngine;

namespace CocKleBurs.Transport
{
    using CocKleBursTransport.Transporting.EOSPlugin;
    using Netick;
    using System;
    using System.Buffers;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using UnityEngine;

    public unsafe class EpicOnlineServiceTransportConnection : TransportConnection
    {
        public bool IsClient;
        public int Id;

        public override IEndPoint EndPoint => default;
        public override int Mtu => NetickEOS.Instance?.GetMTU(0) ?? 1200; 

        public override void Send(IntPtr ptr, int length)
        {
            byte[] data = ArrayPool<byte>.Shared.Rent(length);
            Unsafe.CopyBlockUnaligned(ref data[0], ref *(byte*)ptr, (uint)length);

            if (IsClient) NetickEOS.Instance.SendToServer(0, data);
            else NetickEOS.Instance.SendToClient(0, data, Id);

            ArrayPool<byte>.Shared.Return(data);
        }
    }

    public unsafe class EpicOnlineServiceTransport : NetworkTransport
    {
        private BitBuffer _buffer;
        private Dictionary<int, EpicOnlineServiceTransportConnection> _connections;
        private EpicOnlineServiceTransportConnection _client;
        
        public override void Init()
        {
            base.Init();

            NetickEOS.Instance.OnServerConnectionState += OnServerConnectionState;
            NetickEOS.Instance.OnClientConnectionState += OnClientConnectionState;
            NetickEOS.Instance.OnRemoteConnectionState += OnRemoteConnectionState;
            NetickEOS.Instance.OnClientReceivedData += OnClientReceivedData;
            NetickEOS.Instance.OnServerReceivedData += OnServerReceivedData;
            
            Application.runInBackground = true;
            _buffer = new BitBuffer(createChunks: false);
            _connections = new Dictionary<int, EpicOnlineServiceTransportConnection>();
            _client = new EpicOnlineServiceTransportConnection() { IsClient = true };
            
        }

        private void OnServerReceivedData(ServerReceivedDataArgs obj)
        {
            if (_connections.TryGetValue(obj.ConnectionId, out EpicOnlineServiceTransportConnection connection))
            {
                Span<byte> data = obj.Data.AsSpan();
                fixed (byte* ptr = data)
                {
                    _buffer.SetFrom(ptr, data.Length,
                        data.Length);
                }

                NetworkPeer.Receive(connection, _buffer);
            }
        }

        private void OnClientReceivedData(ClientReceivedDataArgs obj)
        {
            Span<byte> data = obj.Data.AsSpan();
            fixed (byte* ptr = data)
            {
                _buffer.SetFrom(ptr, data.Length,
                    data.Length);
            }

            NetworkPeer.Receive(_client, _buffer);
        }

        private void OnRemoteConnectionState(RemoteConnectionStateArgs obj)
        {
            if (obj.ConnectionState == RemoteConnectionState.Started)
            {
                _connections[obj.ConnectionId] = new EpicOnlineServiceTransportConnection()
                {
                    Id = obj.ConnectionId, IsClient = false
                };
            }
            else   if (obj.ConnectionState == RemoteConnectionState.Stopped)
            {
                if (_connections.Remove(obj.ConnectionId, out EpicOnlineServiceTransportConnection connection))
                {
                    NetworkPeer.OnDisconnected(connection, TransportDisconnectReason.Shutdown);
                }
            }
           
        }

        private void OnClientConnectionState(ClientConnectionStateArgs obj)
        {
            if (obj.ConnectionState == LocalConnectionState.Started)
            {
                NetworkPeer.OnConnected(_client);
            }
            else 
            {
                NetworkPeer.OnDisconnected(_client, TransportDisconnectReason.Shutdown);
            }
        }

        private void OnServerConnectionState(ServerConnectionStateArgs obj)
        {
            if (obj.ConnectionState == LocalConnectionState.Started)
            {
                
            }
            else
            {
                _connections.Clear();
            }
        }

        public override void Connect(string address, int port, byte[] connectionData, int connectionDataLength)
        {
            NetickEOS.Instance.StartConnection(false);
        }

        public override void Disconnect(TransportConnection connection)
        {
            var id=((EpicOnlineServiceTransportConnection)connection).Id;
            if (_connections.Remove(id))
            {
                NetickEOS.Instance.StopConnection(id, true);
            }
        }

        public override void Run(RunMode mode, int port)
        {
            switch (mode)
            {
                case RunMode.Server:
                    NetickEOS.Instance.StartConnection(true);
                    break;
                case RunMode.Client:
                    break;
            }
        }

        public override void Shutdown()
        {
            NetickEOS.Instance.Shutdown();
        }

        public override void PollEvents()
        {
            NetickEOS.Instance.IterateIncoming(false);
            NetickEOS.Instance.IterateOutgoing(false);
            
            NetickEOS.Instance.IterateIncoming(true);
            NetickEOS.Instance.IterateOutgoing(true);
        }
    }
 
}
