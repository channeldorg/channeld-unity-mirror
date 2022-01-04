using System.Collections.Generic;
using Mirror;

namespace Channeld
{
    public class ChanneldInterestManagement : InterestManagement
    {
        public override void OnSpawned(NetworkIdentity identity)
        {
            ChanneldTransport.SetOwningChannel(identity.netId);
        }

        public override void OnDestroyed(NetworkIdentity identity)
        {
            ChanneldTransport.ResetOwningChannel(identity.netId);
        }

        public override bool OnCheckObserver(NetworkIdentity identity, NetworkConnection newObserver)
        {
            return true;
        }

        public override void OnRebuildObservers(NetworkIdentity identity, HashSet<NetworkConnection> newObservers, bool initialize)
        {
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
            {
                // authenticated and joined world with a player?
                if (conn != null && conn.isAuthenticated && conn.identity != null)
                {
                    newObservers.Add(conn);
                }
            }
        }
    }
}
