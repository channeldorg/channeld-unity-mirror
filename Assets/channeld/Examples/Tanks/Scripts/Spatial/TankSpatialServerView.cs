
using Channeldpb;
using Mirror;
using System.Collections.Generic;
using UnityEngine;

namespace Channeld.Examples.Tanks.Scripts
{
    [CreateAssetMenu(fileName = "TankSpatialServerView", menuName = "ScriptableObjects/TankSpatialServerView", order = 2)]
    public class TankSpatialServerView : ChannelDataView
    {
        private Dictionary<int, uint> clientInChannels = new Dictionary<int, uint>();

        protected override void InitChannels()
        {
            RegisterChannelDataParser(ChannelType.Spatial, new TankGameChannelData(), TankGameChannelData.Parser);

            ChanneldTransport.GetServerSendChannelId = (mirrorConnId) =>
            {
                uint channelId;
                if (!clientInChannels.TryGetValue(mirrorConnId, out channelId))
                    channelId = ChanneldTransport.ServerSendChannelId ?? ChanneldConnection.GlobalChannelId;
                return channelId;
            };

            Connection.AddMessageHandler((uint)MessageType.SubToChannel, (_, channelId, msg) =>
            {
                var subResultMsg = (SubscribedToChannelResultMessage)msg;
                // A client is subscribed to a spatial channel the server owns (by the Global server)
                if (subResultMsg.ConnType == ConnectionType.Client && subResultMsg.ChannelType == ChannelType.Spatial)
                {
                    int mirrorConnId = (int)subResultMsg.ConnId;
                    if (!NetworkServer.connections.ContainsKey(mirrorConnId))
                    {
                        ChanneldTransport.Current.OnServerConnected?.Invoke(mirrorConnId);
                    }
                    
                    // Map the client to the channels, so ChanneldTransport.ServerSend() can set the right channelId.
                    clientInChannels[mirrorConnId] = channelId;

                    // Always use the EXACT same logic as the Global server
                    var startPos = NetworkManager.startPositions[mirrorConnId % NetworkManager.startPositions.Count];
                    // Code copied from NetworkManager.OnServerAddPlayer
                    var player = Instantiate(NetworkManager.singleton.playerPrefab, startPos.position, startPos.rotation); 
                    NetworkServer.AddPlayerForConnection(NetworkServer.connections[mirrorConnId], player);
                }
            });

            Connection.SubToChannel(ChanneldConnection.GlobalChannelId, callback: (_) =>
            {
                Connection.CreateSpatialChannel("", callback: (resultMsg) =>
                {
                    Log.Info($"Created spatial channels: {resultMsg.SpatialChannelId.ToString()}");
                });
            });
        }

        protected override void UninitChannels()
        {
            foreach (var kv in Connection.OwnedChannels)
            {
                if (kv.Value.ChannelType == ChannelType.Spatial)
                {
                    Connection.RemoveChannel(kv.Key);
                }
            }
        }
    }
}
