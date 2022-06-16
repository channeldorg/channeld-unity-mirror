
using Channeldpb;
using Mirror;
using System.Collections.Generic;
using UnityEngine;

namespace Channeld.Examples.Tanks.Scripts
{
    [CreateAssetMenu(fileName = "TankGlobalServerView", menuName = "ScriptableObjects/TankGlobalServerView", order = 1)]
    public class TankGlobalServerView : ChannelDataView
    {
        public uint clientFanOutIntervalMs = 50;

        private List<uint> allSpatialChannelIds = new List<uint>();

        protected override void InitChannels()
        {
            Connection.AddMessageHandler((uint)MessageType.SubToChannel, (_, channelId, msg) =>
            {
                var subResultMsg = (SubscribedToChannelResultMessage)msg;
                // Received client subs to Global channel
                if (subResultMsg.ConnType == ConnectionType.Client && subResultMsg.ChannelType == ChannelType.Global)
                {
                    var startPos = NetworkManager.startPositions[(int)subResultMsg.ConnId % NetworkManager.startPositions.Count];
                    Connection.QuerySpatialChannel(new Vector3[]{startPos.position }, (queryResultMsg) =>
                    {
                        var startChannelId = queryResultMsg.ChannelId[0];
                        if (startChannelId == 0)
                        {
                            Log.Error($"Unable to map the player start position ({startPos.position}) to a spatial channel ID");
                            return;
                        }

                        var subOptions = new ChannelSubscriptionOptions()
                        {
                            CanUpdateData = true,
                            FanOutIntervalMs = clientFanOutIntervalMs,
                            FanOutDelayMs = 100,
                        };

                        // FIXME: should only sub to 8 adjacent spatial channels
                        foreach (var spatialChannelId in allSpatialChannelIds)
                        {
                            if (spatialChannelId != startChannelId)
                                Connection.SubConnectionToChannel(subResultMsg.ConnId, spatialChannelId, subOptions);
                        }

                        // Sub the client to the spatial channel in which the start position is.
                        // This must be sent at last as it affects client's ClientSendChannelId.
                        Connection.SubConnectionToChannel(subResultMsg.ConnId, startChannelId, subOptions);
                    });
                }
            });

            Connection.AddMessageHandler((uint)MessageType.CreateSpatialChannel, (_, channelId, msg) =>
            { 
                var resultMsg = (CreateSpatialChannelsResultMessage)msg;
                allSpatialChannelIds.AddRange(resultMsg.SpatialChannelId);
            });


            Connection.CreateChannel(ChannelType.Global, "Tanks");
        }

        protected override void UninitChannels()
        {
        }
    }
}
