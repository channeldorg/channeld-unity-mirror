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

        public override void Send<T>(T message, int channelId = 0)
        {
            // Separate the sending of SpawnMessage from other Mirror messages, as the channelId it's sent to sure be taken care of.
            if (message is SpawnMessage spawnMsg)
            {
                //SendSpawn(spawnMsg);
                base.Send(new SpawnInChannelMessage()
                {
                    channelId = netIdOwningChannelMapper(spawnMsg.netId),
                    netId = spawnMsg.netId,
                    isLocalPlayer = spawnMsg.isLocalPlayer,
                    isOwner = spawnMsg.isOwner,
                    sceneId = spawnMsg.sceneId,
                    assetId = spawnMsg.assetId,
                    position = spawnMsg.position,
                    rotation = spawnMsg.rotation,
                    scale = spawnMsg.scale,
                    payload = spawnMsg.payload,
                }, channelId);

                return;
            }
            base.Send(message, channelId);
        }

        public void SendSpawn(SpawnMessage spawnMessage)
        {
            // The spawned object's netId-channelId mapping must be set before sending SpawnMessage
            ChanneldConnection.Instance.SendNetworkMessage(ChannelDataView.GetOwningChannel(spawnMessage.netId), spawnMessage);
        }
    }
}
