using System;
using Epic.OnlineServices;
using Epic.OnlineServices.Auth;
using CocKleBursTransport.Plugins.NetickEOS.Util.Coroutines;
using UnityEngine;

namespace CocKleBursTransport.Plugins.NetickEOS.Util
{
    /// <summary>
    /// All the data necessary to login to EOS and connect to a remote peer.
    /// Also contains a coroutine to perform login and connect to remote peer.
    /// </summary>
    [Serializable]
    public class AuthData
    {
        public LoginCredentialType loginCredentialType = LoginCredentialType.DeviceCode;
        public ExternalCredentialType externalCredentialType = ExternalCredentialType.DeviceidAccessToken;
        public string id;
        public string token;
        public string displayName = "NetickEOS";
        public bool automaticallyCreateDeviceId = true;
        public bool automaticallyCreateConnectAccount = true;
        public AuthScopeFlags authScopeFlags = AuthScopeFlags.NoFlags;
        public float timeout = 30f;

        public Coroutine Connect(out AuthDataLogin authDataLogin)
        {
            return AuthDataLogin.Login(loginCredentialType, externalCredentialType, id, token, displayName,
                automaticallyCreateDeviceId, automaticallyCreateConnectAccount, timeout, authScopeFlags,
                out authDataLogin);
        }
    }
}