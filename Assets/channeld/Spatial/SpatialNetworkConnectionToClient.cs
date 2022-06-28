
using System;

namespace Channeld.Spatial
{
    public class SpatialNetworkConnectionToClient : ChanneldNetworkConnectionToClient
    {
        public SpatialNetworkConnectionToClient(int networkConnectionId, Func<uint, uint> netIdOwningChannelMapper) :
            base(networkConnectionId, netIdOwningChannelMapper){ }

        protected override void SendSpawnInChannel(SpawnInChannelMessage msg, int unreliable = 0)
        {
            base.SendSpawnInChannel(msg, unreliable);

            msg.isOwner = false;
            // Also need to broadcast to all connection in the adjacent channels
            ChanneldConnection.Instance.BroadcastNetworkMessage(msg.channelId, msg, Channeldpb.BroadcastType.AdjacentChannels, (uint)connectionId);
        }
    }
}
