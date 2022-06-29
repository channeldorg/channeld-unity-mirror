
using Channeldpb;
using Mirror;
using UnityEngine;

namespace Channeld.Examples.Tanks.Scripts
{
    [CreateAssetMenu(fileName = "TankClientSpatialView", menuName = "ScriptableObjects/TankClientSpatialView", order = 7)]
    public class TankClientSpatialView : ChannelDataView
    {
        protected override void InitChannels()
        {
            RegisterChannelDataParser(ChannelType.Spatial, new TankGameChannelData(), TankGameChannelData.Parser);

            Connection.AddMessageHandler((uint)MessageType.SubToChannel, (_, channelId, msg) =>
            {
                var subResultMsg = (SubscribedToChannelResultMessage)msg;
                if (subResultMsg.ChannelType == ChannelType.Spatial && subResultMsg.SubOptions.DataAccess == ChannelDataAccess.WriteAccess)
                {
                    ChanneldTransport.Current.OnClientSubToChannel(channelId);
                }
            });

            Connection.AddMessageHandler((uint)MessageType.ChannelDataHandover, (_, channelId, msg) =>
            {
                var handoverMsg = (ChannelDataHandoverMessage)msg;
                var channelData = handoverMsg.Data.Unpack<TankGameChannelData>();
                Log.Info($"ChannelDataHandover from channel {handoverMsg.SrcChannelId} to {handoverMsg.DstChannelId}: {channelData.ToString()}");

                if (Connection.SubscribedChannels.ContainsKey(handoverMsg.SrcChannelId))
                {
                    foreach (var kv in channelData.TransformStates)
                    {
                        // Update netId-channelId mapping
                        var netId = kv.Key;
                        if (Connection.SubscribedChannels.ContainsKey(handoverMsg.DstChannelId))
                            netIdOwningChannels[netId] = handoverMsg.DstChannelId;
                        else
                            netIdOwningChannels.Remove(netId);

                        // Move data providers
                        if (NetworkClient.spawned.TryGetValue(netId, out var ni))
                        {
                            var dataProvider = ni.GetComponent<IChannelDataProvider>();
                            if (dataProvider != null)
                            {
                                RemoveChannelDataProvider(handoverMsg.SrcChannelId, dataProvider, false);
                                if (Connection.SubscribedChannels.ContainsKey(handoverMsg.DstChannelId))
                                    AddChannelDataProvider(handoverMsg.DstChannelId, dataProvider);
                            }

                            // Update ClientSendChannelId (for sending Mirror's messages)
                            if (ni.isLocalPlayer)
                                ChanneldTransport.Current.OnClientSubToChannel(handoverMsg.DstChannelId);
                        }

                    }
                }
            });

            Connection.SubToChannel(ChanneldConnection.GlobalChannelId);
        }

        protected override void UninitChannels()
        {
        }
    }
}
