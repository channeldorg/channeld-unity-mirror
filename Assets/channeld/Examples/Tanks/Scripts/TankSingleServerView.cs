
using Channeldpb;
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
        protected override void LoadCmdLineArgs()
        {
            CmdLineArgParser.Default.GetEnumOptionFromString("--channel-type", "-ct", ref channelType);
            CmdLineArgParser.Default.GetOptionValue("--fan-out-interval", "-fo", ref fanOutIntervalMs);
            CmdLineArgParser.Default.GetOptionValue("--channel-meta", "-cm", ref channelMetadata);
        }

        protected override void InitChannels()
        {
            RegisterChannelDataParser(channelType, new TankGameChannelData(), TankGameChannelData.Parser);

            // Replace Mirror's NetworkConnectionToClient for customized spawning process
            NetworkServer.ConstructClientConnection = (connectionId) => new ChanneldNetworkConnectionToClient(connectionId, (netId) =>
            {
                if (channelId != null)
                    return channelId.Value;
                Log.Warning($"Server failed to map netId {netId} to any channel, the Global channel will be used.");
                return ChanneldConnection.GlobalChannelId;
            });

            Connection.AddMessageHandler((uint)MessageType.CreateChannel, (_, channelId, msg) =>
            {
                var resultMsg = (CreateChannelResultMessage)msg;
                if (resultMsg.OwnerConnId == Connection.Id)
                {
                    Log.Info($"Server owned channel {resultMsg.ChannelType}");
                    this.channelId = channelId;
                    ChanneldTransport.ServerSendChannelId = channelId;
                }
            });

            Connection.AddMessageHandler((uint)MessageType.RemoveChannel, (_, channelId, msg) =>
            {
                var removeMsg = (RemoveChannelMessage)msg;
                Log.Info("Channel is removed: " + channelId);
                if (removeMsg.ChannelId == this.channelId)
                {
                    Log.Info($"Server no longer owns channel {removeMsg.ChannelId}");
                    this.channelId = null;
                    ChanneldTransport.ServerSendChannelId = null;
                }
            });

            Connection.AddMessageHandler((uint)MessageType.SubToChannel, (_, channelId, msg) =>
            {
                var resultMsg = (SubscribedToChannelResultMessage)msg;
                Log.Info($"Server received sub of conn({resultMsg.ConnId}), connType={resultMsg.ConnType}, channelType={resultMsg.ChannelType}, channelId={channelId}");
                // A client subscribed to the target channel
                if (resultMsg.ConnType == ConnectionType.Client && resultMsg.ChannelType == channelType)
                {
                    int mirrorConnId = (int)resultMsg.ConnId;
                    if (!NetworkServer.connections.ContainsKey(mirrorConnId))
                    {
                        ChanneldTransport.Current.OnServerConnected?.Invoke(mirrorConnId);
                    }

                    if (!NetworkManager.singleton.autoCreatePlayer && resultMsg.ChannelType != ChannelType.Global)
                    {
                        NetworkManager.singleton.OnServerAddPlayer(NetworkServer.connections[mirrorConnId]);
                    }
                }
            });

            Connection.AddMessageHandler((uint)MessageType.UnsubFromChannel, (_, channelId, msg) =>
            {
                var resultMsg = (UnsubscribedFromChannelResultMessage)msg;
                Log.Info($"Server received unsub of conn({resultMsg.ConnId}), connType={resultMsg.ConnType}, channelType={resultMsg.ChannelType}, channelId={channelId}");
                // A client unsubscribed from the target channel
                if (resultMsg.ConnType == ConnectionType.Client && resultMsg.ChannelType == channelType)
                {
                    ChanneldTransport.Current.OnServerDisconnected?.Invoke((int)resultMsg.ConnId);
                }
            });


            Connection.CreateChannel(channelType, channelMetadata, new ChannelSubscriptionOptions()
            {
                DataAccess = ChannelDataAccess.WriteAccess,
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
