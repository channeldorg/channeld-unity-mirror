using Mirror;
using System;

namespace Channeld
{
    public class ChanneldNetworkConnectionToClient : NetworkConnectionToClient
    {
        protected Func<uint, uint> netIdOwningChannelMapper;

        public ChanneldNetworkConnectionToClient(int networkConnectionId, Func<uint, uint> netIdOwningChannelMapper) : base(networkConnectionId)
        {
            this.netIdOwningChannelMapper = netIdOwningChannelMapper;
        }

        public override void Send<T>(T message, int unreliable = 0)
        {
            // Separate the sending of SpawnMessage from other Mirror messages, as the channelId it's sent to sure be taken care of.
            if (message is SpawnMessage spawnMsg)
            {
                var channelId = netIdOwningChannelMapper(spawnMsg.netId);
                var spawnInChannelMsg = new SpawnInChannelMessage()
                {
                    channelId = channelId,
                    netId = spawnMsg.netId,
                    isLocalPlayer = spawnMsg.isLocalPlayer,
                    isOwner = spawnMsg.isOwner,
                    sceneId = spawnMsg.sceneId,
                    assetId = spawnMsg.assetId,
                    position = spawnMsg.position,
                    rotation = spawnMsg.rotation,
                    scale = spawnMsg.scale,
                    payload = spawnMsg.payload,
                };
                SendSpawnInChannel(spawnInChannelMsg, unreliable);
                Log.Info($"Server sent SpawnInChannelMessage to connId={connectionId}, channelId={spawnInChannelMsg.channelId}, netId={spawnInChannelMsg.netId}, isOwner={spawnInChannelMsg.isOwner}");
                return;
            }

            base.Send(message, unreliable);
        }

        protected virtual void SendSpawnInChannel(SpawnInChannelMessage msg, int unreliable = 0)
        {
            // Sends to the client (Mirror handles the broadcasting in current server)
            base.Send(msg, unreliable);
        }
    }
}
