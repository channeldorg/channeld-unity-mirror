
using Mirror;
using UnityEngine;

namespace Channeld.Examples.Tanks
{
    [CreateAssetMenu(fileName = "TankSingleServerView", menuName = "ScriptableObjects/TankSingleServerView", order = 1)]
    public class TankSingleServerView : ChannelDataView
    {
        public ChannelType channelType = ChannelType.Global;
        public uint fanOutIntervalMs = 10;
        public string channelMetadata = "";

        private uint? channelId;

        protected override void InitChannels()
        {
            RegisterChannelDataParser(channelType, new TankGameChannelData(), TankGameChannelData.Parser);

            Connection.AddMessageHandler((uint)MessageType.CreateChannel, (_, channelId, msg) =>
            {
                var resultMsg = (CreateChannelResultMessage)msg;
                Log.Info($"Server owned channel {resultMsg.ChannelType}");
                this.channelId = channelId;
                ChanneldTransport.ServerSendChannelId = channelId;
            });

            Connection.AddMessageHandler((uint)MessageType.RemoveChannel, (_, channelId, msg) =>
            {
                var removeMsg = (RemoveChannelMessage)msg;
                Log.Info("Channel is removed: " + channelId);
                if (removeMsg.ChannelId == this.channelId)
                {
                    Log.Info($"Server no longer owns channel {removeMsg.ChannelId}");
                    this.channelId = null;
                }
            });

            Connection.AddMessageHandler((uint)MessageType.SubToChannel, (_, channelId, msg) =>
            {
                var resultMsg = (SubscribedToChannelResultMessage)msg;
                Log.Info($"Server received sub of conn({resultMsg.ConnId}), connType={resultMsg.ConnType}, channelType={resultMsg.ChannelType}, channelId={channelId}");
                // A client subscribed to the target channel
                if (resultMsg.ConnType == ConnectionType.Client && resultMsg.ChannelType == channelType)
                {
                    if (!NetworkServer.connections.ContainsKey((int)resultMsg.ConnId))
                        (Transport.activeTransport as ChanneldTransport).OnServerConnected?.Invoke((int)resultMsg.ConnId);
                }
            });

            Connection.AddMessageHandler((uint)MessageType.UnsubFromChannel, (_, channelId, msg) =>
            {
                var resultMsg = (UnsubscribedFromChannelResultMessage)msg;
                Log.Info($"Server received unsub of conn({resultMsg.ConnId}), connType={resultMsg.ConnType}, channelType={resultMsg.ChannelType}, channelId={channelId}");
                // A client unsubscribed from the target channel
                if (resultMsg.ConnType == ConnectionType.Client && resultMsg.ChannelType == channelType)
                {
                    (Transport.activeTransport as ChanneldTransport).OnServerDisconnected?.Invoke((int)resultMsg.ConnId);
                }
            });


            Connection.CreateChannel(channelType, channelMetadata, new ChannelSubscriptionOptions()
            {
                CanUpdateData = true,
                FanOutIntervalMs = fanOutIntervalMs
            }, null, new ChannelDataMergeOptions() { ShouldCheckRemovableMapField = true });
        }

        protected override void UninitChannels()
        {
            if (channelId != null)
            {
                Connection.RemoveChannel(channelId.Value);
            }
        }
    }
}
