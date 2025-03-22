using CocKleBurs.Transport;
using Netick.Unity;
using Netick;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CocKleBurs.Transport
{
    [CreateAssetMenu(fileName = "EpicOnlineServiceTransportProvider", menuName = "Netick/Transport/EpicOnlineServiceTransportProvider", order = 1)]
    public class EpicOnlineServiceTransportProvider : NetworkTransportProvider
    {
        public override NetworkTransport MakeTransportInstance() => new EpicOnlineServiceTransport();
    }
}