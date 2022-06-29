
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

            //if (msg.isOwner)
            {
                msg.isOwner = false;

                // Also need to broadcast to all connection in the adjacent channels (except this client connection and this server)
                ChanneldConnection.Instance.BroadcastNetworkMessage(msg.channelId, msg, Channeldpb.BroadcastType.AdjacentChannels, (uint)connectionId);

                Log.Info($"Broadcast SpawnInChannelMessage to adjacent channels, channelId={msg.channelId}, netId={msg.netId}");
            }
        }
    }
}
