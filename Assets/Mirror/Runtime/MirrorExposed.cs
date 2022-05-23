// Add this file to Mirror's source code (Runtime folder) to expose the internal methods, to enable the integration with channeld.

using Mirror.RemoteCalls;
using System;

namespace Mirror
{
    public static class RemoteCallHelperExposed
    {
        public static int GetMethodHash(Type invokeClass, string methodName)
        {
            return RemoteCallHelper.GetMethodHash(invokeClass, methodName);
        }
    }

    public static class NetworkClientExposed
    {
        public static void OnSpawn(SpawnMessage msg)
        {
            NetworkClient.OnSpawn(msg);
        }
    }
    
    public static class NetworkIdentityExposed
    {
        public static void SetNetId(this NetworkIdentity ni, uint netId)
        {
            ni.netId = netId;
        }
    }
}
