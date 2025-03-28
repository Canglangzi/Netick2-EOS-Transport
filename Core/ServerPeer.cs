﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Epic.OnlineServices;
using Epic.OnlineServices.P2P;
using CocKleBursTransport.Plugins.NetickEOS.Util;
using UnityEngine;

namespace CocKleBursTransport.Transporting.EOSPlugin
{
    public class ServerPeer : CommonPeer
    {
        /// <summary>
        /// An auto-incrementing id for each peer.
        /// </summary>
        private static int _latestId = 1;

        # region Private.

        /// <summary>
        /// EOS Socket Id for this peer.
        /// </summary>
        private SocketId _socketId;

        /// <summary>
        /// EOS Server User Id for this peer.
        /// </summary>
        private ProductUserId _localUserId;

        /// <summary>
        /// Queue of incoming local client host packets.
        /// </summary>
        private Queue<LocalPacket> _clientHostIncoming = new Queue<LocalPacket>();

        /// <summary>
        /// Reference to Local Client Host.
        /// </summary>
        private ClientHostPeer _clientHost;

        /// <summary>
        /// EOS Handle for Incoming Peer Requests.
        /// </summary>
        private ulong? _acceptPeerConnectionsEventHandle;

        /// <summary>
        /// EOS Handle for Incoming Peer Connections.
        /// </summary>
        private Dictionary<Connection, ulong>
            _establishPeerConnectionEventHandles = new Dictionary<Connection, ulong>();

        /// <summary>
        /// EOS Handle for Incoming Peer Disconnections.
        /// </summary>
        private Dictionary<string, ulong> _closePeerConnectionEventHandles = new Dictionary<string, ulong>();

        /// <summary>
        /// List of connections to this server.
        /// </summary>
        private List<Connection> _clients = new List<Connection>();

        /// <summary>
        /// Maximum number of connections allowed.
        /// </summary>
        private int _maximumClients = short.MaxValue;

        #endregion

        /// <summary>
        /// Starts the server.
        /// </summary>
        internal bool StartConnection()
        {
            base.SetLocalConnectionState(LocalConnectionState.Starting, true);
            _transport.StartCoroutine(AuthenticateAndStartListeningForConnections());
            return true;
        }

        /// <summary>
        /// Coroutine that authenticates with EOS and starts listening for incoming connections.
        /// </summary>
        private IEnumerator AuthenticateAndStartListeningForConnections()
        {
            if (_transport.AutoAuthenticate)
            {
                yield return _transport.AuthConnectData.Connect(out var authDataLogin);
                if (authDataLogin.loginCallbackInfo?.ResultCode != Result.Success)
                {
                    //_transport.NetworkManager.LogError($"[ServerPeer] Failed to authenticate with EOS Connect. {authDataLogin.loginCallbackInfo?.ResultCode}");
                    base.SetLocalConnectionState(LocalConnectionState.Stopped, true);
                    yield break;
                }
            }

            //_transport.NetworkManager.Log($"[ServerPeer] Authenticated with EOS Connect. {EOS.LocalProductUserId}");

            // Attempt to Start Listening for Peer Connections...
            try
            {
                _localUserId = EOS.LocalProductUserId;
                _socketId = new SocketId { SocketName = _transport.SocketName };
                var addNotifyPeerConnectionRequestOptions = new AddNotifyPeerConnectionRequestOptions
                {
                    SocketId = _socketId,
                    LocalUserId = _localUserId,
                };
                _acceptPeerConnectionsEventHandle = EOS.GetCachedP2PInterface().AddNotifyPeerConnectionRequest(
                    ref addNotifyPeerConnectionRequestOptions, null, OnPeerConnectionRequest);

                //_transport.NetworkManager.Log($"[ServerPeer] Started listening for incoming connections. Handle #{_acceptPeerConnectionsEventHandle}");
            }
            catch (Exception e)
            {
                //_transport.NetworkManager.LogError($"[ServerPeer] Failed to start listening for incoming connections. {e}");
                base.SetLocalConnectionState(LocalConnectionState.Stopped, true);
                yield break;
            }

            base.SetLocalConnectionState(LocalConnectionState.Started, true);
        }

        /// <summary>
        /// Event Callback when peer connection request is received.
        /// </summary>
        private void OnPeerConnectionRequest(ref OnIncomingConnectionRequestInfo data)
        {
            var nextId = _latestId++;
            var clientConnection = new Connection(nextId, data.LocalUserId, data.RemoteUserId, data.SocketId);
            _clients.Add(clientConnection);

            var addNotifyPeerConnectionEstablishedOptions = new AddNotifyPeerConnectionEstablishedOptions
            {
                SocketId = data.SocketId,
                LocalUserId = data.LocalUserId,
            };
            var connectionEstablishedHandle = EOS.GetCachedP2PInterface().AddNotifyPeerConnectionEstablished(
                ref addNotifyPeerConnectionEstablishedOptions, clientConnection, OnPeerConnectionEstablished);
            _establishPeerConnectionEventHandles.Add(clientConnection, connectionEstablishedHandle);

            var acceptConnectionOptions = new AcceptConnectionOptions
            {
                LocalUserId = _localUserId,
                RemoteUserId = data.RemoteUserId,
                SocketId = data.SocketId,
            };
            var acceptConnectionResult = EOS.GetCachedP2PInterface().AcceptConnection(ref acceptConnectionOptions);

            if (acceptConnectionResult != Result.Success)
            {
                _clients.Remove(clientConnection);
                //_transport.NetworkManager.LogError($"[ServerPeer] Failed to accept connection from {data.RemoteUserId} with handle #{data.SocketId} and connection id {nextId}. {acceptConnectionResult}");
            }
        }

        /// <summary>
        /// Event Callback when peer connection is established.
        /// </summary>
        private void OnPeerConnectionEstablished(ref OnPeerConnectionEstablishedInfo data)
        {
            var clientConnection = (Connection)data.ClientData;
            if (_establishPeerConnectionEventHandles.TryGetValue(clientConnection, out var notificationId))
            {
                EOS.GetCachedP2PInterface().RemoveNotifyPeerConnectionEstablished(notificationId);
                _establishPeerConnectionEventHandles.Remove(clientConnection);
            }

            var addNotifyPeerConnectionClosedOptions = new AddNotifyPeerConnectionClosedOptions
            {
                SocketId = clientConnection.SocketId,
                LocalUserId = clientConnection.LocalUserId,
            };
            
            ulong closePeerConnectionHandle =
                EOS.GetCachedP2PInterface().AddNotifyPeerConnectionClosed(ref addNotifyPeerConnectionClosedOptions,
                    clientConnection, OnPeerConnectionClosed);
            
            string remoteUserId = clientConnection.RemoteUserId.ToString();
            if (!_closePeerConnectionEventHandles.TryGetValue(remoteUserId, out ulong existingClosePeerConnectionHandle))
            {
                _closePeerConnectionEventHandles.Add(remoteUserId, closePeerConnectionHandle);
            }
            else
            {
                if (closePeerConnectionHandle != existingClosePeerConnectionHandle)
                {
                    Debug.LogWarningFormat(
                        "[ServerPeer.OnPeerConnectionEstablished] Removing existing close peer connection handle for remote user id {0} with handle #{1} and adding new handle #{2}",
                        remoteUserId, existingClosePeerConnectionHandle, closePeerConnectionHandle);

                    RemoveClosePeerConnectionHandle(remoteUserId, existingClosePeerConnectionHandle);
                    EOS.GetCachedP2PInterface().RemoveNotifyPeerConnectionClosed(existingClosePeerConnectionHandle);
                }
                else
                {
                    Debug.LogWarningFormat("[ServerPeer.OnPeerConnectionEstablished] Existing close peer connection handle for remote user id {0} with handle #{1} is the same as the new handle #{2}",
                        remoteUserId, existingClosePeerConnectionHandle, closePeerConnectionHandle);
                }
            }

            _transport.HandleRemoteConnectionState(new RemoteConnectionStateArgs(RemoteConnectionState.Started,
                clientConnection.Id, _transport.Index));
            //_transport.NetworkManager.Log($"[ServerPeer.OnPeerConnectionEstablished] Established connection from {data.RemoteUserId} with handle #{data.SocketId} and connection id {clientConnection.Id}.");
        }

        /// <summary>
        /// Event Callback when peer connection is closed.
        /// </summary>
        private void OnPeerConnectionClosed(ref OnRemoteConnectionClosedInfo data)
        {
            Connection? clientConnection = null;
            for (int i = _clients.Count - 1; i >= 0; i--)
            {
                if(data.RemoteUserId != _clients[i].RemoteUserId)
                    continue;
                
                clientConnection = _clients[i];
            }

            if (!clientConnection.HasValue)
            {
                Debug.LogWarningFormat("[ServerPeer.OnPeerConnectionClosed] Failed to find connection for remote user id {0}.", data.RemoteUserId);
                return;
            }
            
            if (_closePeerConnectionEventHandles.TryGetValue(data.RemoteUserId.ToString(), out ulong notificationId))
            {
                RemoveClosePeerConnectionHandle(data.RemoteUserId.ToString(), notificationId);
            }

            _transport.HandleRemoteConnectionState(new RemoteConnectionStateArgs(RemoteConnectionState.Stopped,
                clientConnection.Value.Id, _transport.Index));
            //_transport.NetworkManager.Log($"[ServerPeer.OnPeerConnectionClosed] Closed connection from {data.RemoteUserId} with handle #{data.SocketId} and connection id {clientConnection.Value.Id}.");
            
            _clients.Remove(clientConnection.Value);
        }

        private void RemoveClosePeerConnectionHandle(string remoteUserId, ulong notificationId)
        {
            EOS.GetCachedP2PInterface().RemoveNotifyPeerConnectionClosed(notificationId);
            _closePeerConnectionEventHandles.Remove(remoteUserId);
        }

        /// <summary>
        /// Stops the server.
        /// </summary>
        internal bool StopConnection()
        {
            if (GetLocalConnectionState() == LocalConnectionState.Stopped ||
                GetLocalConnectionState() == LocalConnectionState.Stopping)
                return false;

            base.SetLocalConnectionState(LocalConnectionState.Stopping, true);

            try
            {
                _clients.Clear();
                _clientHostIncoming.Clear();
                _clientHost?.StopConnection();

                foreach (var entry in _closePeerConnectionEventHandles)
                    EOS.GetCachedP2PInterface()?.RemoveNotifyPeerConnectionClosed(entry.Value);
                _closePeerConnectionEventHandles.Clear();

                foreach (var entry in _establishPeerConnectionEventHandles)
                    EOS.GetCachedP2PInterface()?.RemoveNotifyPeerConnectionEstablished(entry.Value);
                _establishPeerConnectionEventHandles.Clear();

                if (_acceptPeerConnectionsEventHandle.HasValue)
                {
                    EOS.GetCachedP2PInterface()?
                        .RemoveNotifyPeerConnectionRequest(_acceptPeerConnectionsEventHandle.Value);
                    _acceptPeerConnectionsEventHandle = null;
                }

                var closeConnectionOptions = new CloseConnectionsOptions
                {
                    SocketId = _socketId,
                    LocalUserId = _localUserId,
                };
                EOS.GetCachedP2PInterface()?.CloseConnections(ref closeConnectionOptions);
            }
            catch (Exception e)
            {
                //_transport.NetworkManager.LogError($"[ServerPeer] Failed to stop listening for incoming connections. {e}");
                base.SetLocalConnectionState(LocalConnectionState.Stopped, true);
                return false;
            }

            base.SetLocalConnectionState(LocalConnectionState.Stopped, true);
            return true;
        }

        /// <summary>
        /// Stops a remote client from the server, disconnecting the client.
        /// </summary>
        /// <param name="connectionId"></param>
        internal bool StopConnection(int connectionId)
        {
            if (connectionId == NetickEOS.CLIENT_HOST_ID)
            {
                _clientHost.StopConnection();
                return true;
            }

            var clientConnectionExists = _clients.Any(x => x.Id == connectionId);
            if (!clientConnectionExists) return false;

            var clientConnection = _clients.FirstOrDefault(x => x.Id == connectionId);
            var closeConnectionOptions = new CloseConnectionOptions
            {
                SocketId = _socketId,
                LocalUserId = clientConnection.LocalUserId,
                RemoteUserId = clientConnection.RemoteUserId
            };
            EOS.GetCachedP2PInterface().CloseConnection(ref closeConnectionOptions);
            return true;
        }

        /// <summary>
        /// Gets the current ConnectionState of a remote client on the server.
        /// </summary>
        /// <param name="connectionId">ConnectionId to get ConnectionState for.</param>
        internal RemoteConnectionState GetConnectionState(int connectionId)
        {
            if (_clients.Any(x => x.Id == connectionId))
                return RemoteConnectionState.Started;
            else
                return RemoteConnectionState.Stopped;
        }

        /// <summary>
        /// Unused by EOS.
        /// </summary>
        internal void IterateOutgoing() { }

        /// <summary>
        /// Iterates through all incoming packets and handles them.
        /// </summary>
        internal void IterateIncoming()
        {
            if (GetLocalConnectionState() != LocalConnectionState.Started)
                return;

            //Iterate local client packets first.
            while (_clientHostIncoming.Count > 0)
            {
                var packet = _clientHostIncoming.Dequeue();
                var segment = new ArraySegment<byte>(packet.Data, 0, packet.Length);
                _transport.HandleServerReceivedDataArgs(new ServerReceivedDataArgs(segment, packet.Channel,
                    NetickEOS.CLIENT_HOST_ID, _transport.Index));
            }

            var incomingPacketCount = GetIncomingPacketQueueCurrentPacketCount();
            for (ulong i = 0; i < incomingPacketCount; i++)
                if (Receive(_localUserId, out var remoteUserId, out var data, out var channel))
                {
                    int index = 0;
                    bool hasFoundId = false;
                    int connectionId = 0;
                    while (index < _clients.Count && !hasFoundId)
                    {
                        if (_clients[index].RemoteUserId == remoteUserId)
                        {
                            hasFoundId = true;
                            connectionId = _clients[index].Id;
                        }
index++;
                    }

                    if (!hasFoundId) //prevent failures of not getting an id...FishyEOS didn't handle this
                    {
                        return;
                    }
                    _transport.HandleServerReceivedDataArgs(new ServerReceivedDataArgs(data, channel, connectionId,
                        _transport.Index));
                }
        }

        /// <summary>
        /// Sends a packet to a single, or all clients.
        /// </summary>
        internal void SendToClient(byte channelId, ArraySegment<byte> segment, int connectionId)
        {
            if (GetLocalConnectionState() != LocalConnectionState.Started)
                return;

            if (connectionId == NetickEOS.CLIENT_HOST_ID)
            {
                if (_clientHost != null)
                {
                    var packet = new LocalPacket(segment, channelId);
                    _clientHost.ReceivedFromLocalServer(packet);
                }

                return;
            }

            if (_clients.Any(x => x.Id == connectionId))
            {
                var clientConnection = _clients.First(x => x.Id == connectionId);
                var result = Send(_localUserId, clientConnection.RemoteUserId, _socketId, channelId, segment);

                if (result == Result.NoConnection || result == Result.InvalidParameters)
                {
                    //_transport.NetworkManager.Log($"Connection to {connectionId} was lost.");
                    StopConnection(connectionId);
                }
                else if (result != Result.Success)
                {
                    //_transport.NetworkManager.LogError($"Could not send: {result}");
                }
            }
            else
            {
                //_transport.NetworkManager.LogError($"ConnectionId {connectionId} does not exist, data will not be sent.");
            }
        }

        /// <summary>
        /// Returns the maximum number of clients allowed to connect to the server.
        /// If the transport does not support this method the value -1 is returned.
        /// </summary>
        public int GetMaximumClients()
        {
            return _maximumClients;
        }

        /// <summary>
        /// Sets the maximum number of clients allowed to connect to the server.
        /// </summary>
        public void SetMaximumClients(int value)
        {
            _maximumClients = value;
        }

        /// <summary>
        /// Sets the local client host.
        /// </summary>
        internal void SetClientHostPeer(ClientHostPeer clientHostPeer)
        {
            _clientHost = clientHostPeer;
        }

        /// <summary>
        /// Queues a received packet from the local client host.
        /// </summary>
        internal void ReceivedFromClientHost(LocalPacket packet)
        {
            if (_clientHost == null || _clientHost.GetLocalConnectionState() != LocalConnectionState.Started) return;
            _clientHostIncoming.Enqueue(packet);
        }

        /// <summary>
        /// Called when Client Host Connection state changes.
        /// </summary>
        internal void HandleClientHostConnectionStateChange(LocalConnectionState state, bool server)
        {
            switch (state)
            {
                case LocalConnectionState.Started:
                    _transport.HandleRemoteConnectionState(new RemoteConnectionStateArgs(RemoteConnectionState.Started,
                        NetickEOS.CLIENT_HOST_ID, _transport.Index));
                    break;
                case LocalConnectionState.Stopped:
                    _transport.HandleRemoteConnectionState(new RemoteConnectionStateArgs(RemoteConnectionState.Stopped,
                        NetickEOS.CLIENT_HOST_ID, _transport.Index));
                    break;
                case LocalConnectionState.Starting:
                case LocalConnectionState.Stopping:
                default:
                    throw new ArgumentOutOfRangeException(nameof(state), state, null);
            }
        }

        /// <summary>
        /// Gets the EOS Local Product User Id of the server.
        /// </summary>
        internal string GetConnectionAddress(int connectionId)
        {
            var client = _clients.FirstOrDefault(x => x.Id == connectionId);
            return client.RemoteUserId.ToString();
        }
    }
}
