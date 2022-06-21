
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

        private HashSet<uint> allSpatialChannelIds = new HashSet<uint>();

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

                        var authoritySubOptions = new ChannelSubscriptionOptions()
                        {
                            DataAccess = ChannelDataAccess.WriteAccess,
                            FanOutIntervalMs = clientFanOutIntervalMs,
                            FanOutDelayMs = 100,
                        };
                        var nonAuthoritySubOptions = new ChannelSubscriptionOptions()
                        {
                            DataAccess = ChannelDataAccess.ReadAccess,
                            FanOutIntervalMs = clientFanOutIntervalMs,
                            FanOutDelayMs = 100,
                        };

                        // FIXME: should only sub to 8 adjacent spatial channels
                        foreach (var spatialChannelId in allSpatialChannelIds)
                        {
                            Connection.SubConnectionToChannel(subResultMsg.ConnId, spatialChannelId, 
                                spatialChannelId == startChannelId ? authoritySubOptions : nonAuthoritySubOptions);
                        }
                    });
                }
            });

            Connection.AddMessageHandler((uint)MessageType.CreateSpatialChannel, (_, channelId, msg) =>
            { 
                var resultMsg = (CreateSpatialChannelsResultMessage)msg;
                foreach (var spatialChannelId in resultMsg.SpatialChannelId)
                    allSpatialChannelIds.Add(spatialChannelId);
            });

            Connection.AddMessageHandler((uint)MessageType.RemoveChannel, (_, channelId, msg) =>
            {
                var removeMsg = (RemoveChannelMessage)msg;
                allSpatialChannelIds.Remove(removeMsg.ChannelId);
            });


            Connection.CreateChannel(ChannelType.Global, "Tanks");
        }

        protected override void UninitChannels()
        {
        }
    }
}
