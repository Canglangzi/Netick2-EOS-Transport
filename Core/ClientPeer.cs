using System;
using System.Collections;
using Epic.OnlineServices;
using Epic.OnlineServices.P2P;
using CocKleBursTransport.Plugins.NetickEOS.Util;
using UnityEngine;

namespace CocKleBursTransport.Transporting.EOSPlugin
{
    public class ClientPeer : CommonPeer
    {
        #region Private.

        /// <summary>
        /// EOS Socket Id
        /// </summary>
        private SocketId? _socketId;

        /// <summary>
        /// EOS Client User Id
        /// </summary>
        private ProductUserId _localUserId;

        /// <summary>
        /// EOS Server User Id
        /// </summary>
        private ProductUserId _remoteUserId;

        /// <summary>
        /// EOS Connection Type
        /// </summary>
        private string _connectionType;

        /// <summary>
        /// EOS NAT Type
        /// </summary>
        private string _natType;

        /// <summary>
        /// EOS Handle for Peer Connection Established Event
        /// </summary>
        private ulong? _peerConnectionEstablishedEventHandle;

        /// <summary>
        /// EOS Handle for Peer Connection Closed Event
        /// </summary>
        private ulong? _peerConnectionClosedEventHandle;

        #endregion

        /// <summary>
        /// Starts the client connection. [Uses data stored on FishyEOS Transport]
        /// </summary>
        internal void StartConnection()
        {
            _transport.StartCoroutine(StartConnectionCoroutine());
        }

        /// <summary>
        /// Coroutine that authenticates with EOS and then starts a connection to the server.
        /// </summary>
        private IEnumerator StartConnectionCoroutine()
        {
            base.SetLocalConnectionState(LocalConnectionState.Starting, false);

            if (_transport.AutoAuthenticate)
            {
                yield return _transport.AuthConnectData.Connect(out var authDataLogin);
                if (authDataLogin.loginCallbackInfo?.ResultCode != Result.Success)
                {
                    //_transport.NetworkManager.LogError($"[ClientPeer] Failed to authenticate with EOS Connect. {authDataLogin.loginCallbackInfo?.ResultCode}");
                    base.SetLocalConnectionState(LocalConnectionState.Stopped, true);
                    yield break;
                }
            }

            //_transport.NetworkManager.Log($"[ClientPeer] Authenticated with EOS Connect. {EOS.LocalProductUserId}");

            // Attempt to connect to Server Remote User Id P2P connection...
            _localUserId = EOS.LocalProductUserId;
            _remoteUserId = ProductUserId.FromString(_transport.RemoteProductUserId);
            _socketId = new SocketId { SocketName = _transport.SocketName };

            if (_peerConnectionEstablishedEventHandle.HasValue)
                EOS.GetCachedP2PInterface().RemoveNotifyPeerConnectionEstablished(_peerConnectionEstablishedEventHandle.Value);

            if (_peerConnectionClosedEventHandle.HasValue)
                EOS.GetCachedP2PInterface().RemoveNotifyPeerConnectionClosed(_peerConnectionClosedEventHandle.Value);

            var addNotifyPeerConnectionEstablishedOptions = new AddNotifyPeerConnectionEstablishedOptions
            {
                LocalUserId = _localUserId,
                SocketId = _socketId,
            };
            _peerConnectionEstablishedEventHandle = EOS.GetCachedP2PInterface().AddNotifyPeerConnectionEstablished(
                ref addNotifyPeerConnectionEstablishedOptions, null,
                OnPeerConnectionEstablished);

            var addNotifyPeerConnectionClosedOptions = new AddNotifyPeerConnectionClosedOptions
            {
                LocalUserId = _localUserId,
                SocketId = _socketId,
            };
            _peerConnectionClosedEventHandle = EOS.GetCachedP2PInterface().AddNotifyPeerConnectionClosed(
                ref addNotifyPeerConnectionClosedOptions, null,
                OnPeerConnectionClosed);

            var acceptConnectionOptions = new AcceptConnectionOptions
            {
                LocalUserId = _localUserId,
                RemoteUserId = _remoteUserId,
                SocketId = _socketId,
            };
            var acceptConnectionResult = EOS.GetCachedP2PInterface().AcceptConnection(ref acceptConnectionOptions);
            if (acceptConnectionResult != Result.Success)
            {
                base.SetLocalConnectionState(LocalConnectionState.Stopped, false);
                //_transport.NetworkManager.LogError($"[{nameof(ClientPeer)}] AcceptConnection failed with error: {acceptConnectionResult}");
                StopConnection();
                yield break;
            }

            var queryNATTypeOptions = new QueryNATTypeOptions();
            EOS.GetCachedP2PInterface().QueryNATType(ref queryNATTypeOptions, null, OnQueryNATType);
        }

        /// <summary>
        /// Stops the client connection.
        /// </summary>
        internal bool StopConnection()
        {
            if (GetLocalConnectionState() == LocalConnectionState.Stopped ||
                GetLocalConnectionState() == LocalConnectionState.Stopping)
                return false;

            base.SetLocalConnectionState(LocalConnectionState.Stopping, false);

            var closeConnectionOptions = new CloseConnectionOptions
            {
                SocketId = _socketId,
                LocalUserId = _localUserId,
                RemoteUserId = _remoteUserId,
            };
            var result = EOS.GetCachedP2PInterface()?.CloseConnection(ref closeConnectionOptions);
            base.SetLocalConnectionState(LocalConnectionState.Stopped, false);

            if (result == Result.Success) return true;

            //_transport.NetworkManager.LogError($"[ClientPeer] Failed to close connection. Error: {result}");
            return false;
        }

        /// <summary>
        /// Called when connected to the server.
        /// </summary>
        private void OnPeerConnectionEstablished(ref OnPeerConnectionEstablishedInfo data)
        {
            if (_peerConnectionEstablishedEventHandle.HasValue)
                EOS.GetCachedP2PInterface().RemoveNotifyPeerConnectionEstablished(_peerConnectionEstablishedEventHandle.Value);

            _connectionType = data.ConnectionType.ToString();
            base.SetLocalConnectionState(LocalConnectionState.Started, false);
            //_transport.NetworkManager.Log($"[ClientPeer] Connection to server established. ConnectionType: {_connectionType}");
        }

        /// <summary>
        /// Called when disconnected from the server.
        /// </summary>
        private void OnPeerConnectionClosed(ref OnRemoteConnectionClosedInfo data)
        {
            if (_peerConnectionEstablishedEventHandle.HasValue)
                EOS.GetCachedP2PInterface().RemoveNotifyPeerConnectionEstablished(_peerConnectionEstablishedEventHandle.Value);

            if (_peerConnectionClosedEventHandle.HasValue)
                EOS.GetCachedP2PInterface().RemoveNotifyPeerConnectionClosed(_peerConnectionClosedEventHandle.Value);

            //_transport.NetworkManager.Log($"[ClientPeer] Connection to server closed.");
            StopConnection();
        }

        /// <summary>
        /// Unused for EOS Transport
        /// </summary>
        internal void IterateOutgoing() { }

        /// <summary>
        /// Iterates incoming packets.
        /// </summary>
        internal void IterateIncoming()
        {
            //Stopped or trying to stop.
            if (GetLocalConnectionState() == LocalConnectionState.Stopped ||
                GetLocalConnectionState() == LocalConnectionState.Stopping)
                return;

            var incomingPacketCount = GetIncomingPacketQueueCurrentPacketCount();
            for (ulong i = 0; i < incomingPacketCount; i++)
                if (Receive(_localUserId, out _, out var segment, out var channel))
                    _transport.HandleClientReceivedDataArgs(
                        new ClientReceivedDataArgs(segment, channel, _transport.Index));
        }

        /// <summary>
        /// Sends a packet to the server.
        /// </summary>
        internal void SendToServer(byte channelId, ArraySegment<byte> segment)
        {
            if (GetLocalConnectionState() != LocalConnectionState.Started)
                return;

            var result = Send(_localUserId, _remoteUserId, _socketId, channelId, segment);
            if (result == Result.NoConnection || result == Result.InvalidParameters)
            {
                //_transport.NetworkManager.Log($"[ClientPeer] Connection to server was lost.");
                StopConnection();
            }
            else if (result != Result.Success)
            {
                //_transport.NetworkManager.LogError($"[ClientPeer] Could not send: {result}");
            }
        }

        /// <summary>
        /// Determines the NAT Type of the client connection.
        /// </summary>
        private void OnQueryNATType(ref OnQueryNATTypeCompleteInfo data)
        {
            _natType = data.NATType.ToString();
            //_transport.NetworkManager.Log($"[{nameof(ClientPeer)}] NATType: {_natType}");
        }
    }
}